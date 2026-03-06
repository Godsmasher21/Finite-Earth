import "dotenv/config";
import crypto from "node:crypto";
import http from "node:http";
import Database from "better-sqlite3";
import express from "express";
import cors from "cors";
import jwt from "jsonwebtoken";
import { Contract, JsonRpcProvider, Wallet } from "ethers";
import { WebSocketServer } from "ws";
import { z } from "zod";
import { SiweMessage } from "siwe";
import { InMemoryAuthoritativeWorld } from "./world.js";
import type { ActionCommit, ActionIntent } from "./types.js";

const PORT = Number(process.env.PORT ?? 8080);
const JWT_SECRET = process.env.JWT_SECRET ?? "finite-earth-dev-secret";
const TOKEN_TTL_SECONDS = 15 * 60;
const NONCE_TTL_MS = 5 * 60 * 1000;
const MAX_INTENTS_PER_MINUTE = Number(process.env.MAX_INTENTS_PER_MINUTE ?? 120);
const DEV_AUTH_ENABLED = String(process.env.DEV_AUTH_ENABLED ?? "true").toLowerCase() === "true";

const DB_PATH = process.env.GATEWAY_DB_PATH ?? "./gateway.db";
const MEGAETH_RPC_URL = process.env.MEGAETH_RPC_URL ?? "";
const RELAYER_PRIVATE_KEY = process.env.RELAYER_PRIVATE_KEY ?? "";
const GLOBAL_COUNTERS_ADDRESS = process.env.GLOBAL_COUNTERS_ADDRESS ?? "";
const CHAIN_RELAY_POLL_MS = Number(process.env.CHAIN_RELAY_POLL_MS ?? 3000);
const MAX_CHAIN_BATCH_ACTIONS = 200;

const GLOBAL_COUNTERS_ABI = [
  "function commitCycle(uint64 cycleId,int256 forestDelta,int256 carbonDelta,bytes32 actionBatchHash,uint32 actionCount) external",
  "event CycleCommitted(uint64 indexed cycleId,int256 forestDelta,int256 carbonDelta,int256 forestTotal,int256 carbonTotal,bytes32 actionBatchHash,uint32 actionCount)"
];

type ChainCycleBatch = {
  cycleId: number;
  forestDelta: number;
  carbonDelta: number;
  actionCount: number;
  batchHash: string;
  sourceCommitIds: string[];
  attempt: number;
  nextAttemptAtMs: number;
  createdAtMs: number;
};

const app = express();
app.use(cors());
app.use(express.json({ limit: "256kb" }));

const db = new Database(DB_PATH);
setupDbSchema(db);

const server = http.createServer(app);
const wsServer = new WebSocketServer({ server, path: "/realtime" });
const world = new InMemoryAuthoritativeWorld("finite-earth-alpha");

const nonceStore = new Map<string, { nonce: string; expiresAt: number }>();
const minuteRateLimit = new Map<string, { minute: number; count: number }>();
const websocketClients = new Set<WebSocket>();

const acceptedCommitsByTick = new Map<number, ActionCommit[]>();
const carryOverAccepted: ActionCommit[] = [];
const pendingChainQueue: ChainCycleBatch[] = [];

const chainEnabled = Boolean(MEGAETH_RPC_URL && RELAYER_PRIVATE_KEY && GLOBAL_COUNTERS_ADDRESS);
let globalCountersContract: Contract | null = null;

if (chainEnabled) {
  const provider = new JsonRpcProvider(MEGAETH_RPC_URL);
  const signer = new Wallet(RELAYER_PRIVATE_KEY, provider);
  globalCountersContract = new Contract(GLOBAL_COUNTERS_ADDRESS, GLOBAL_COUNTERS_ABI, signer);
  console.log("[gateway] chain relay enabled");
} else {
  console.log("[gateway] chain relay disabled (missing MEGAETH_RPC_URL/RELAYER_PRIVATE_KEY/GLOBAL_COUNTERS_ADDRESS)");
}

const nonceSchema = z.object({
  walletAddress: z.string().min(4),
  chainId: z.number().int().positive()
});

const verifySchema = z.object({
  message: z.string().min(10),
  signature: z.string().min(10),
  nonce: z.string().min(8)
});

const intentSchema = z.object({
  intentId: z.string().min(3),
  worldId: z.string().min(3),
  walletAddress: z.string().min(4),
  clientSeq: z.number().int().positive(),
  actionType: z.string().min(3),
  q: z.number().int(),
  r: z.number().int(),
  buildingType: z.string().min(1),
  clientIssuedAtMs: z.number().int().positive()
});

app.get("/health", (_req, res) => {
  res.json({
    ok: true,
    service: "gateway",
    world: world.getSnapshot(),
    chainEnabled,
    pendingChainBatches: pendingChainQueue.length,
    rows: {
      actionCommits: rowCount(db, "action_commits"),
      cycleEvents: rowCount(db, "cycle_events"),
      leaderboard: rowCount(db, "leaderboard")
    }
  });
});

app.post("/auth/siwe/nonce", (req, res) => {
  const parsed = nonceSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ error: "Invalid nonce request payload." });
    return;
  }

  const { walletAddress } = parsed.data;
  const nonce = crypto.randomBytes(8).toString("hex");
  const expiresAt = Date.now() + NONCE_TTL_MS;
  nonceStore.set(walletAddress.toLowerCase(), { nonce, expiresAt });

  res.json({
    nonce,
    expiresAtUnixMs: expiresAt
  });
});

app.post("/auth/siwe/verify", async (req, res) => {
  const parsed = verifySchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ error: "Invalid SIWE verify payload." });
    return;
  }

  const { message, signature, nonce } = parsed.data;

  try {
    const siwe = new SiweMessage(message);
    const expected = nonceStore.get(siwe.address.toLowerCase());
    if (!expected || expected.expiresAt < Date.now()) {
      res.status(401).json({ error: "Nonce expired or not found." });
      return;
    }

    if (expected.nonce !== nonce) {
      res.status(401).json({ error: "Nonce mismatch." });
      return;
    }

    const verification = await siwe.verify({ signature, nonce });
    if (!verification.success) {
      res.status(401).json({ error: "SIWE verification failed." });
      return;
    }

    nonceStore.delete(siwe.address.toLowerCase());

    const token = jwt.sign(
      { sub: siwe.address.toLowerCase(), worldId: "finite-earth-alpha" },
      JWT_SECRET,
      { expiresIn: TOKEN_TTL_SECONDS }
    );

    res.json({
      accessToken: token,
      expiresAtUnixMs: Date.now() + TOKEN_TTL_SECONDS * 1000,
      walletAddress: siwe.address.toLowerCase()
    });
  } catch (error) {
    res.status(500).json({ error: `SIWE verify failed: ${String(error)}` });
  }
});

app.post("/auth/refresh", (req, res) => {
  const token = readBearerToken(req.headers.authorization);
  if (!token) {
    res.status(401).json({ error: "Missing bearer token." });
    return;
  }

  try {
    const decoded = jwt.verify(token, JWT_SECRET) as { sub: string; worldId: string };
    const refreshed = jwt.sign(
      { sub: decoded.sub, worldId: decoded.worldId },
      JWT_SECRET,
      { expiresIn: TOKEN_TTL_SECONDS }
    );

    res.json({
      accessToken: refreshed,
      expiresAtUnixMs: Date.now() + TOKEN_TTL_SECONDS * 1000
    });
  } catch {
    res.status(401).json({ error: "Invalid token." });
  }
});

app.post("/auth/dev-login", (req, res) => {
  if (!DEV_AUTH_ENABLED) {
    res.status(403).json({ error: "Development auth is disabled." });
    return;
  }

  const walletAddress = String((req.body as { walletAddress?: string })?.walletAddress ?? "").toLowerCase();
  if (!walletAddress || walletAddress.length < 4) {
    res.status(400).json({ error: "walletAddress is required." });
    return;
  }

  const token = jwt.sign(
    { sub: walletAddress, worldId: "finite-earth-alpha", mode: "dev" },
    JWT_SECRET,
    { expiresIn: TOKEN_TTL_SECONDS }
  );

  res.json({
    accessToken: token,
    expiresAtUnixMs: Date.now() + TOKEN_TTL_SECONDS * 1000,
    walletAddress
  });
});

app.get("/world/snapshot", authenticateHttpToken, (_req, res) => {
  res.json(toWorldSnapshotPayload(world.getFullSnapshot()));
});

app.get("/leaderboard", (_req, res) => {
  const rows = db
    .prepare(`
      SELECT wallet_address, sustainability_score, actions_taken, owned_tiles_count, updated_at_ms
      FROM leaderboard
      ORDER BY sustainability_score DESC
      LIMIT 100
    `)
    .all();

  res.json({
    worldId: "finite-earth-alpha",
    players: rows
  });
});

app.get("/metrics/timeseries", (_req, res) => {
  const rows = db
    .prepare(`
      SELECT cycle_id, forest_delta, carbon_delta, forest_total, carbon_total, action_count, tx_hash, created_at_ms
      FROM cycle_events
      ORDER BY cycle_id ASC
      LIMIT 5000
    `)
    .all();

  res.json({
    worldId: "finite-earth-alpha",
    points: rows
  });
});

app.get("/export/csv", (_req, res) => {
  const rows = db
    .prepare(`
      SELECT cycle_id, forest_delta, carbon_delta, forest_total, carbon_total, action_count, tx_hash
      FROM cycle_events
      ORDER BY cycle_id ASC
    `)
    .all() as Array<Record<string, string | number>>;

  const header = "cycle_id,forest_delta,carbon_delta,forest_total,carbon_total,action_count,tx_hash";
  const lines = rows.map((row) =>
    [
      row.cycle_id,
      row.forest_delta,
      row.carbon_delta,
      row.forest_total,
      row.carbon_total,
      row.action_count,
      row.tx_hash
    ].join(",")
  );

  res.setHeader("Content-Type", "text/csv");
  res.send([header, ...lines].join("\n"));
});

app.post("/intent", authenticateHttpToken, (req, res) => {
  const parsed = intentSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ error: "Invalid action intent payload." });
    return;
  }

  const intent = parsed.data as ActionIntent;
  const wallet = (req as any).walletAddress as string;

  if (intent.walletAddress.toLowerCase() !== wallet) {
    res.status(403).json({ error: "Intent wallet does not match authenticated wallet." });
    return;
  }

  if (!allowPerMinute(wallet)) {
    res.status(429).json({ error: "Intent rate limit exceeded for current minute." });
    return;
  }

  world.submitIntent({
    ...intent,
    submittedAtMs: Date.now()
  });

  res.json({ accepted: true, queued: true });
});

app.get("/internal/commits/pending", (_req, res) => {
  res.json({
    worldId: "finite-earth-alpha",
    commits: world.listPendingCommits(250)
  });
});

app.post("/internal/commits/:commitId/acked", (req, res) => {
  const commitId = req.params.commitId;
  world.markCommitAsRelayed(commitId);
  res.json({ ok: true, commitId });
});

app.post("/internal/cycle-committed", (req, res) => {
  const payload = req.body as {
    tick?: number;
    cycleId?: number;
    forestDelta?: number;
    carbonDelta?: number;
    txHash?: string;
  };

  if (!payload || typeof payload.cycleId !== "number" || typeof payload.txHash !== "string") {
    res.status(400).json({ error: "Invalid chain commit payload." });
    return;
  }

  const snapshot = world.getSnapshot();
  insertCycleEvent(db, {
    cycleId: payload.cycleId,
    forestDelta: payload.forestDelta ?? 0,
    carbonDelta: payload.carbonDelta ?? 0,
    forestTotal: snapshot.forestTotal,
    carbonTotal: snapshot.carbonTotal,
    actionCount: 0,
    txHash: payload.txHash
  });

  broadcast({
    type: "CycleCommittedToChain",
    tick: payload.tick ?? payload.cycleId,
    cycleId: payload.cycleId,
    forestDelta: payload.forestDelta ?? 0,
    carbonDelta: payload.carbonDelta ?? 0,
    transactionHash: payload.txHash
  });

  res.json({ ok: true });
});

wsServer.on("connection", (socket, request) => {
  const url = new URL(request.url ?? "", `http://${request.headers.host}`);
  const token = url.searchParams.get("token");

  const authResult = verifyToken(token);
  if (!authResult.ok) {
    socket.close(4401, authResult.error);
    return;
  }

  websocketClients.add(socket as unknown as WebSocket);
  socket.send(JSON.stringify(toWorldSnapshotPayload(world.getFullSnapshot())));

  socket.on("message", (data) => {
    try {
      const decoded = JSON.parse(data.toString()) as { type?: string; intent?: ActionIntent };
      if (decoded.type !== "ActionIntentSubmit" || !decoded.intent) {
        return;
      }

      const parsed = intentSchema.safeParse(decoded.intent);
      if (!parsed.success) {
        return;
      }

      const intent = parsed.data as ActionIntent;
      if (intent.walletAddress.toLowerCase() !== authResult.walletAddress) {
        return;
      }

      if (!allowPerMinute(authResult.walletAddress)) {
        return;
      }

      world.submitIntent({
        ...intent,
        submittedAtMs: Date.now()
      });
    } catch {
      // Drop malformed payloads.
    }
  });

  socket.on("close", () => {
    websocketClients.delete(socket as unknown as WebSocket);
  });
});

world.setCycleListener((tick) => {
  broadcast({ type: "CycleStarted", tick, startedAtUnixMs: Date.now() });

  if (tick % 10 === 0) {
    broadcast(toWorldSnapshotPayload(world.getFullSnapshot()));
  }

  enqueueCycleBatch(tick - 1);
});

world.setCommitListener((commit: ActionCommit) => {
  persistActionCommit(db, commit);
  updateLeaderboard(db, commit);

  if (commit.accepted) {
    const list = acceptedCommitsByTick.get(commit.tick) ?? [];
    list.push(commit);
    acceptedCommitsByTick.set(commit.tick, list);
  }

  broadcast({
    type: "ActionCommitted",
    commitId: commit.commitId,
    tick: commit.tick,
    intentId: commit.intentId,
    accepted: commit.accepted,
    reason: commit.reason,
    tileDeltas: commit.tileDeltas,
    playerDelta: commit.playerDelta,
    globalDelta: {
      forestDelta: commit.globalForestDelta,
      carbonDelta: commit.globalCarbonDelta,
      actionCount: commit.accepted ? 1 : 0
    },
    batchHash: commit.batchHash
  });
});

world.start();
setInterval(() => {
  void processPendingChainBatches();
}, CHAIN_RELAY_POLL_MS);

server.listen(PORT, () => {
  console.log(`[gateway] listening on http://localhost:${PORT}`);
});

function enqueueCycleBatch(cycleId: number): void {
  if (cycleId <= 0) {
    return;
  }

  const currentCycleCommits = acceptedCommitsByTick.get(cycleId) ?? [];
  acceptedCommitsByTick.delete(cycleId);

  const merged = [...carryOverAccepted, ...currentCycleCommits];
  if (merged.length === 0) {
    return;
  }

  const selected = merged.slice(0, MAX_CHAIN_BATCH_ACTIONS);
  const overflow = merged.slice(MAX_CHAIN_BATCH_ACTIONS);

  carryOverAccepted.length = 0;
  for (const commit of overflow) {
    carryOverAccepted.push(commit);
  }

  const forestDelta = selected.reduce((sum, commit) => sum + commit.globalForestDelta, 0);
  const carbonDelta = selected.reduce((sum, commit) => sum + commit.globalCarbonDelta, 0);
  const actionCount = selected.length;
  const combinedHash = crypto
    .createHash("sha256")
    .update(selected.map((commit) => commit.batchHash).join(":"))
    .digest("hex");

  pendingChainQueue.push({
    cycleId,
    forestDelta,
    carbonDelta,
    actionCount,
    batchHash: `0x${combinedHash}`,
    sourceCommitIds: selected.map((commit) => commit.commitId),
    attempt: 0,
    nextAttemptAtMs: Date.now(),
    createdAtMs: Date.now()
  });
}

async function processPendingChainBatches(): Promise<void> {
  if (pendingChainQueue.length === 0) {
    return;
  }

  pendingChainQueue.sort((left, right) => left.cycleId - right.cycleId);
  const next = pendingChainQueue[0];
  if (!next || next.nextAttemptAtMs > Date.now()) {
    return;
  }

  if (!chainEnabled || globalCountersContract == null) {
    finalizeCycleBatch(next, `local-sim://${next.cycleId}`);
    pendingChainQueue.shift();
    return;
  }

  try {
    const tx = await globalCountersContract.commitCycle(
      BigInt(next.cycleId),
      BigInt(next.forestDelta),
      BigInt(next.carbonDelta),
      next.batchHash,
      next.actionCount
    );

    const receipt = await tx.wait(1);
    const txHash = String(receipt?.hash ?? tx.hash);

    finalizeCycleBatch(next, txHash);
    pendingChainQueue.shift();
  } catch (error) {
    next.attempt += 1;
    const backoffMs = Math.min(60_000, 1000 * (2 ** next.attempt));
    next.nextAttemptAtMs = Date.now() + backoffMs;
    console.error(`[gateway] chain commit failed cycle=${next.cycleId} retryInMs=${backoffMs}`, error);
  }
}

function finalizeCycleBatch(batch: ChainCycleBatch, txHash: string): void {
  const worldSnapshot = world.getSnapshot();
  insertCycleEvent(db, {
    cycleId: batch.cycleId,
    forestDelta: batch.forestDelta,
    carbonDelta: batch.carbonDelta,
    forestTotal: worldSnapshot.forestTotal,
    carbonTotal: worldSnapshot.carbonTotal,
    actionCount: batch.actionCount,
    txHash
  });

  broadcast({
    type: "CycleCommittedToChain",
    tick: batch.cycleId,
    cycleId: batch.cycleId,
    forestDelta: batch.forestDelta,
    carbonDelta: batch.carbonDelta,
    transactionHash: txHash
  });
}

function toWorldSnapshotPayload(snapshot: {
  worldId: string;
  tick: number;
  cycleSeconds: number;
  actionsPerCycle: number;
  forestTotal: number;
  carbonTotal: number;
  tiles: Array<{
    q: number;
    r: number;
    currentState: string;
    ownerWallet: string;
    buildingType: string;
    lastUpdatedTick: number;
  }>;
  players: Array<{
    walletAddress: string;
    ownedTilesCount: number;
    sustainabilityScore: number;
    actionsTaken: number;
    actionsRemaining: number;
    lastClientSeq: number;
  }>;
}): unknown {
  return {
    type: "WorldSnapshot",
    worldId: snapshot.worldId,
    tick: snapshot.tick,
    cycleSeconds: snapshot.cycleSeconds,
    actionsPerCycle: snapshot.actionsPerCycle,
    globalForestToken: snapshot.forestTotal,
    globalCarbonToken: snapshot.carbonTotal,
    tiles: snapshot.tiles,
    players: snapshot.players
  };
}

function authenticateHttpToken(req: express.Request, res: express.Response, next: express.NextFunction): void {
  const token = readBearerToken(req.headers.authorization);
  const auth = verifyToken(token);

  if (!auth.ok) {
    res.status(401).json({ error: auth.error });
    return;
  }

  (req as any).walletAddress = auth.walletAddress;
  next();
}

function readBearerToken(header?: string): string | null {
  if (!header) return null;
  const [kind, token] = header.split(" ");
  if (kind?.toLowerCase() !== "bearer" || !token) {
    return null;
  }

  return token;
}

function verifyToken(token: string | null): { ok: true; walletAddress: string } | { ok: false; error: string } {
  if (!token) {
    return { ok: false, error: "Missing token." };
  }

  try {
    const decoded = jwt.verify(token, JWT_SECRET) as { sub: string };
    return { ok: true, walletAddress: decoded.sub };
  } catch {
    return { ok: false, error: "Invalid token." };
  }
}

function allowPerMinute(walletAddress: string): boolean {
  const minute = Math.floor(Date.now() / 60000);
  const current = minuteRateLimit.get(walletAddress);

  if (!current || current.minute !== minute) {
    minuteRateLimit.set(walletAddress, { minute, count: 1 });
    return true;
  }

  if (current.count >= MAX_INTENTS_PER_MINUTE) {
    return false;
  }

  current.count += 1;
  return true;
}

function broadcast(payload: unknown): void {
  const raw = JSON.stringify(payload);
  for (const socket of websocketClients) {
    try {
      socket.send(raw);
    } catch {
      // Ignore client send failures.
    }
  }
}

function setupDbSchema(database: Database.Database): void {
  database.exec(`
    CREATE TABLE IF NOT EXISTS action_commits (
      commit_id TEXT PRIMARY KEY,
      tick INTEGER NOT NULL,
      intent_id TEXT NOT NULL,
      accepted INTEGER NOT NULL,
      reason TEXT NOT NULL,
      forest_delta INTEGER NOT NULL,
      carbon_delta INTEGER NOT NULL,
      batch_hash TEXT NOT NULL,
      committed_at_ms INTEGER NOT NULL
    );

    CREATE TABLE IF NOT EXISTS cycle_events (
      cycle_id INTEGER PRIMARY KEY,
      forest_delta INTEGER NOT NULL,
      carbon_delta INTEGER NOT NULL,
      forest_total INTEGER NOT NULL,
      carbon_total INTEGER NOT NULL,
      action_count INTEGER NOT NULL,
      tx_hash TEXT NOT NULL,
      created_at_ms INTEGER NOT NULL
    );

    CREATE TABLE IF NOT EXISTS leaderboard (
      wallet_address TEXT PRIMARY KEY,
      sustainability_score INTEGER NOT NULL,
      actions_taken INTEGER NOT NULL,
      owned_tiles_count INTEGER NOT NULL,
      updated_at_ms INTEGER NOT NULL
    );
  `);
}

function persistActionCommit(database: Database.Database, commit: ActionCommit): void {
  database
    .prepare(`
      INSERT OR IGNORE INTO action_commits (
        commit_id, tick, intent_id, accepted, reason, forest_delta, carbon_delta, batch_hash, committed_at_ms
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    `)
    .run(
      commit.commitId,
      commit.tick,
      commit.intentId,
      commit.accepted ? 1 : 0,
      commit.reason,
      commit.globalForestDelta,
      commit.globalCarbonDelta,
      commit.batchHash,
      commit.committedAtMs
    );
}

function updateLeaderboard(database: Database.Database, commit: ActionCommit): void {
  if (!commit.playerDelta?.walletAddress) {
    return;
  }

  const walletAddress = commit.playerDelta.walletAddress;
  const current = database
    .prepare(`
      SELECT sustainability_score, actions_taken, owned_tiles_count
      FROM leaderboard
      WHERE wallet_address = ?
    `)
    .get(walletAddress) as { sustainability_score: number; actions_taken: number; owned_tiles_count: number } | undefined;

  const nextScore = (current?.sustainability_score ?? 0) + Number(commit.playerDelta.sustainabilityScoreDelta ?? 0);
  const nextActions = (current?.actions_taken ?? 0) + Number(commit.playerDelta.actionsTakenDelta ?? 0);
  const nextOwned = (current?.owned_tiles_count ?? 0) + Number(commit.playerDelta.ownedTilesDelta ?? 0);

  database
    .prepare(`
      INSERT INTO leaderboard (
        wallet_address, sustainability_score, actions_taken, owned_tiles_count, updated_at_ms
      ) VALUES (?, ?, ?, ?, ?)
      ON CONFLICT(wallet_address) DO UPDATE SET
        sustainability_score = excluded.sustainability_score,
        actions_taken = excluded.actions_taken,
        owned_tiles_count = excluded.owned_tiles_count,
        updated_at_ms = excluded.updated_at_ms
    `)
    .run(walletAddress, nextScore, nextActions, nextOwned, Date.now());
}

function insertCycleEvent(
  database: Database.Database,
  payload: {
    cycleId: number;
    forestDelta: number;
    carbonDelta: number;
    forestTotal: number;
    carbonTotal: number;
    actionCount: number;
    txHash: string;
  }
): void {
  database
    .prepare(`
      INSERT OR REPLACE INTO cycle_events (
        cycle_id, forest_delta, carbon_delta, forest_total, carbon_total, action_count, tx_hash, created_at_ms
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
    `)
    .run(
      payload.cycleId,
      payload.forestDelta,
      payload.carbonDelta,
      payload.forestTotal,
      payload.carbonTotal,
      payload.actionCount,
      payload.txHash,
      Date.now()
    );
}

function rowCount(database: Database.Database, tableName: string): number {
  const row = database.prepare(`SELECT COUNT(1) AS count FROM ${tableName}`).get() as { count: number };
  return row.count;
}

type WebSocket = {
  send(data: string): void;
};
