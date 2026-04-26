import "dotenv/config";
import crypto from "node:crypto";
import http from "node:http";
import { DatabaseSync } from "node:sqlite";
import express from "express";
import cors from "cors";
import jwt from "jsonwebtoken";
import { Contract, JsonRpcProvider, Wallet, isAddress } from "ethers";
import { WebSocketServer, type WebSocket } from "ws";
import { z } from "zod";
import { SiweMessage } from "siwe";
import { DbConnection } from "./module_bindings/index.js";
import type {
  ActionCommitEventRow,
  CredentialAuthResult,
  PlayerRow,
  PlayerIdentityRow,
  TileRow,
  WorldStateRow,
} from "./module_bindings/types.js";

const PORT = Number(process.env.PORT ?? 8080);
const JWT_SECRET = process.env.JWT_SECRET ?? "finite-earth-dev-secret";
const TOKEN_TTL_SECONDS = 15 * 60;
const NONCE_TTL_MS = 5 * 60 * 1000;
const MAX_INTENTS_PER_MINUTE = Number(process.env.MAX_INTENTS_PER_MINUTE ?? 120);
const DEV_AUTH_ENABLED = String(process.env.DEV_AUTH_ENABLED ?? "true").toLowerCase() === "true";
const PASSWORD_MIN_LENGTH = 4;

const DB_PATH = process.env.GATEWAY_DB_PATH ?? "./gateway.db";
const STDB_URI = process.env.STDB_URI ?? "wss://maincloud.spacetimedb.com";
const STDB_DATABASE = process.env.STDB_DATABASE ?? "finite-earth";
const STDB_TOKEN = process.env.STDB_TOKEN ?? "";
const CLIENT_WORLD_ID = process.env.CLIENT_WORLD_ID ?? "finite-earth-alpha";
const CYCLE_SECONDS = Number(process.env.CYCLE_SECONDS ?? 30);
const UNBOUNDED_ACTIONS_DISPLAY = Number(process.env.ACTIONS_DISPLAY_HINT ?? 9999);
const SETTLEMENT_RADIUS = 3;

const MEGAETH_RPC_URL      = process.env.MEGAETH_RPC_URL        ?? "";
const RELAYER_PRIVATE_KEY  = process.env.RELAYER_PRIVATE_KEY    ?? "";
const GLOBAL_COUNTERS_ADDRESS = process.env.GLOBAL_COUNTERS_ADDRESS ?? "";
const TILE_NFT_ADDRESS     = process.env.TILE_NFT_ADDRESS       ?? "";
const FOREST_TOKEN_ADDRESS = process.env.FOREST_TOKEN_ADDRESS   ?? "";
const CARBON_TOKEN_ADDRESS = process.env.CARBON_TOKEN_ADDRESS   ?? "";
const CHAIN_RELAY_POLL_MS  = Number(process.env.CHAIN_RELAY_POLL_MS ?? 3000);
const MAX_CHAIN_BATCH_ACTIONS = 200;

// Action type constants (mirror Lib.cs)
const A_CLAIM = 0;

const GLOBAL_COUNTERS_ABI = [
  "function commitCycle(uint64 cycleId,int256 forestDelta,int256 carbonDelta,bytes32 actionBatchHash,uint32 actionCount) external",
  "event CycleCommitted(uint64 indexed cycleId,int256 forestDelta,int256 carbonDelta,int256 forestTotal,int256 carbonTotal,bytes32 actionBatchHash,uint32 actionCount)"
];

const TILE_NFT_ABI = [
  "function claimTile(address wallet, int32 q, int32 r) external"
];

const FOREST_TOKEN_ABI = [
  "function syncForest(uint64 cycleId, int256 forestDelta, address relayAddr) external",
  "function rewardPlayer(address wallet, uint256 forestTiles, uint64 cycleId) external"
];

const CARBON_TOKEN_ABI = [
  "function syncCarbon(uint64 cycleId, int256 carbonDelta, address relayAddr) external",
  "function emitCarbon(address wallet, uint256 carbonUnits, uint64 cycleId) external",
  "function offsetCarbon(address wallet, uint256 carbonUnits, uint64 cycleId) external"
];

type GatewayWebSocket = WebSocket & {
  walletAddress?: string;
};

type TileDeltaPayload = {
  q: number;
  r: number;
  previousTerrain: string;
  nextTerrain: string;
  previousBuilding: string;
  nextBuilding: string;
  ownerChanged: boolean;
  ownerWallet: string;
  lastUpdatedTick: number;
};

type ResourceDeltaPayload = {
  wood: number;
  food: number;
  minerals: number;
};

type PlayerDeltaPayload = {
  walletAddress: string;
  ownedTilesDelta: number;
  sustainabilityScoreDelta: number;
  actionsTakenDelta: number;
  actionsRemainingDelta: number;
  resourceDelta: ResourceDeltaPayload;
};

type GlobalDeltaPayload = {
  forestDelta: number;
  carbonDelta: number;
  actionCount: number;
};

type GatewayActionCommit = {
  worldId: string;
  commitId: string;
  tick: number;
  intentId: string;
  accepted: boolean;
  reason: string;
  globalForestDelta: number;
  globalCarbonDelta: number;
  batchHash: string;
  tileDeltas: TileDeltaPayload[];
  playerDelta: PlayerDeltaPayload;
  committedAtMs: number;
  walletAddress: string;
  actionType: number;
  q: number;
  r: number;
};

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

type AccountIdentity = {
  walletAddress: string;
  username: string;
  displayName: string;
};

type AuthResponsePayload = {
  accessToken: string;
  expiresAtUnixMs: number;
  walletAddress: string;
  username: string;
  displayName: string;
  authMode: string;
};

const app = express();
app.use(cors());
app.use(express.json({ limit: "256kb" }));

const db = new DatabaseSync(DB_PATH);
setupDbSchema(db);

const server = http.createServer(app);
const wsServer = new WebSocketServer({ server, path: "/realtime" });

const nonceStore = new Map<string, { nonce: string; expiresAt: number }>();
const minuteRateLimit = new Map<string, { minute: number; count: number }>();
const websocketClients = new Set<GatewayWebSocket>();
const connectedWalletCounts = new Map<string, number>();
const ensuredWallets = new Set<string>();

const acceptedCommitsByTick = new Map<number, GatewayActionCommit[]>();
const carryOverAccepted: GatewayActionCommit[] = [];
const pendingChainQueue: ChainCycleBatch[] = [];

const hasChainRelayConfig = Boolean(MEGAETH_RPC_URL && RELAYER_PRIVATE_KEY && GLOBAL_COUNTERS_ADDRESS);
let chainEnabled = false;
let globalCountersContract: Contract | null = null;
let tileNftContract: Contract | null = null;
let forestTokenContract: Contract | null = null;
let carbonTokenContract: Contract | null = null;
let relayerAddress = "";

// Queue for tile-claim mints so we don't block the event loop.
const pendingTileMints: Array<{ wallet: string; q: number; r: number }> = [];

let spacetimeConn: DbConnection | null = null;
let spacetimeReady = false;
let spacetimeReconnectScheduled = false;

if (hasChainRelayConfig) {
  try {
    if (!/^0x[0-9a-fA-F]{64}$/.test(RELAYER_PRIVATE_KEY)) {
      throw new Error("RELAYER_PRIVATE_KEY must be a 32-byte hex string.");
    }

    if (!isAddress(GLOBAL_COUNTERS_ADDRESS)) {
      throw new Error("GLOBAL_COUNTERS_ADDRESS must be a valid EVM address.");
    }

    // MegaETH quirk: eth_chainId returns 6342 but EIP-155 signing uses 6343.
    const provider = new JsonRpcProvider(
      MEGAETH_RPC_URL,
      { chainId: 6343, name: "megaeth" },
      { staticNetwork: true }
    );
    // Force legacy type-0 transactions (MegaETH rejects EIP-1559 type-2).
    (provider as any).getFeeData = async () => {
      const gasPrice = await provider.send("eth_gasPrice", []);
      const { FeeData } = await import("ethers");
      return new FeeData(BigInt(gasPrice), null, null);
    };
    const signer = new Wallet(RELAYER_PRIVATE_KEY, provider);
    relayerAddress = await signer.getAddress();

    globalCountersContract = new Contract(GLOBAL_COUNTERS_ADDRESS, GLOBAL_COUNTERS_ABI, signer);

    if (isAddress(TILE_NFT_ADDRESS)) {
      tileNftContract = new Contract(TILE_NFT_ADDRESS, TILE_NFT_ABI, signer);
      console.log(`[gateway] TileNFT relay enabled at ${TILE_NFT_ADDRESS}`);
    }

    if (isAddress(FOREST_TOKEN_ADDRESS)) {
      forestTokenContract = new Contract(FOREST_TOKEN_ADDRESS, FOREST_TOKEN_ABI, signer);
      console.log(`[gateway] ForestToken relay enabled at ${FOREST_TOKEN_ADDRESS}`);
    }

    if (isAddress(CARBON_TOKEN_ADDRESS)) {
      carbonTokenContract = new Contract(CARBON_TOKEN_ADDRESS, CARBON_TOKEN_ABI, signer);
      console.log(`[gateway] CarbonToken relay enabled at ${CARBON_TOKEN_ADDRESS}`);
    }

    chainEnabled = true;
    console.log("[gateway] chain relay enabled (MegaETH chain 6342)");

    // Drain pending tile-mint queue every poll interval.
    setInterval(() => void drainTileMintQueue(), CHAIN_RELAY_POLL_MS);
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.warn(`[gateway] chain relay disabled (invalid configuration: ${reason})`);
  }
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

const credentialSchema = z.object({
  username: z.string().trim().min(3).max(24).regex(/^[A-Za-z0-9_]+$/, "Username may only contain letters, numbers, and underscores."),
  password: z.string().min(PASSWORD_MIN_LENGTH).max(128)
});

const credentialSignupSchema = credentialSchema.extend({
  confirmPassword: z.string().min(PASSWORD_MIN_LENGTH).max(128)
});

const intentSchema = z.object({
  intentId: z.string().min(3),
  worldId: z.string().min(3),
  walletAddress: z.string().min(4),
  clientSeq: z.number().int().positive(),
  actionType: z.number().int().nonnegative(),
  q: z.number().int(),
  r: z.number().int(),
  buildingType: z.union([z.string(), z.number().int()]).optional(),
  clientIssuedAtMs: z.number().int().positive()
});

const intentBatchSchema = z.object({
  intents: z.array(intentSchema).min(1).max(256)
});

app.get("/health", (_req, res) => {
  const snapshot = buildWorldSnapshotPayload();
  res.json({
    ok: true,
    service: "gateway",
    spacetimeReady,
    spacetime: {
      uri: STDB_URI,
      database: STDB_DATABASE
    },
    world: snapshot,
    chainEnabled,
    pendingChainBatches: pendingChainQueue.length,
    rows: {
      actionCommits: rowCount(db, "action_commits"),
      cycleEvents: rowCount(db, "cycle_events"),
      leaderboard: rowCount(db, "leaderboard")
    },
    identities: {
      playerProfiles: spacetimeConn != null ? Array.from(spacetimeConn.db.PlayerIdentities.iter()).length : 0
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
  const normalizedWallet = normalizeWalletAddress(walletAddress);
  const nonce = crypto.randomBytes(8).toString("hex");
  const expiresAt = Date.now() + NONCE_TTL_MS;
  nonceStore.set(normalizedWallet, { nonce, expiresAt });

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
    const normalizedWallet = normalizeWalletAddress(siwe.address);
    const expected = nonceStore.get(normalizedWallet);
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

    nonceStore.delete(normalizedWallet);
    res.json(createAuthResponse(normalizedWallet, "wallet"));
  } catch (error) {
    res.status(500).json({ error: `SIWE verify failed: ${String(error)}` });
  }
});

app.post("/auth/credentials/login", async (req, res) => {
  const parsed = credentialSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ error: "Invalid credential login payload." });
    return;
  }

  const authResult = await authenticateWithSpacetimeCredentials("login", parsed.data.username, parsed.data.password);
  if (!authResult.success) {
    const message = authResult.error || "Invalid username or password.";
    const statusCode = message.includes("not ready") ? 503 : 401;
    res.status(statusCode).json({ error: message });
    return;
  }

  res.json(createAuthResponse(authResult.wallet, "credentials", {
    walletAddress: normalizeWalletAddress(authResult.wallet),
    username: authResult.username ?? "",
    displayName: authResult.displayName ?? authResult.username ?? ""
  }));
});

app.post("/auth/credentials/signup", async (req, res) => {
  const parsed = credentialSignupSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ error: "Invalid credential signup payload." });
    return;
  }

  const username = parsed.data.username.trim();
  if (parsed.data.password !== parsed.data.confirmPassword) {
    res.status(400).json({ error: "Password and confirm password must match." });
    return;
  }

  const authResult = await authenticateWithSpacetimeCredentials("signup", username, parsed.data.password);
  if (!authResult.success) {
    const message = authResult.error || "Credential signup failed.";
    const statusCode = message.includes("not ready")
      ? 503
      : (message === "Username is already taken." ? 409 : 400);
    res.status(statusCode).json({ error: message });
    return;
  }

  res.status(201).json(createAuthResponse(authResult.wallet, "credentials", {
    walletAddress: normalizeWalletAddress(authResult.wallet),
    username: authResult.username ?? username,
    displayName: authResult.displayName ?? authResult.username ?? username
  }));
});

app.post("/auth/refresh", (req, res) => {
  const token = readBearerToken(req.headers.authorization);
  if (!token) {
    res.status(401).json({ error: "Missing bearer token." });
    return;
  }

  try {
    const decoded = jwt.verify(token, JWT_SECRET) as { sub: string; worldId: string; mode?: string };
    res.json(createAuthResponse(decoded.sub, decoded.mode ?? inferAuthMode(decoded.sub)));
  } catch {
    res.status(401).json({ error: "Invalid token." });
  }
});

app.post("/auth/dev-login", (req, res) => {
  if (!DEV_AUTH_ENABLED) {
    res.status(403).json({ error: "Development auth is disabled." });
    return;
  }

  const walletAddress = normalizeWalletAddress(String((req.body as { walletAddress?: string })?.walletAddress ?? ""));
  if (!walletAddress || walletAddress.length < 4) {
    res.status(400).json({ error: "walletAddress is required." });
    return;
  }

  res.json(createAuthResponse(walletAddress, "dev"));
});

app.get("/world/snapshot", authenticateHttpToken, (_req, res) => {
  const snapshot = buildWorldSnapshotPayload();
  if (!snapshot) {
    res.status(503).json({ error: "SpacetimeDB is not ready." });
    return;
  }

  res.json(snapshot);
});

app.get("/leaderboard", (req, res) => {
  const limit = parseIntQuery(req.query.limit, 100, 1, 200);
  const offset = parseIntQuery(req.query.offset, 0, 0, 10_000);

  const totalRow = db
    .prepare("SELECT COUNT(1) AS count FROM leaderboard")
    .get() as { count: number };

  const rows = db
    .prepare(`
      SELECT wallet_address, sustainability_score, actions_taken, owned_tiles_count,
             COALESCE(tile_nft_count, 0) AS tile_nft_count, updated_at_ms
      FROM leaderboard
      ORDER BY owned_tiles_count DESC,
               sustainability_score DESC,
               actions_taken ASC,
               wallet_address ASC
      LIMIT ? OFFSET ?
    `)
    .all(limit, offset) as Array<{
      wallet_address: string;
      sustainability_score: number;
      actions_taken: number;
      owned_tiles_count: number;
      tile_nft_count: number;
      updated_at_ms: number;
    }>;

  const identities = getAccountIdentitiesByWallets(rows.map((row) => row.wallet_address));

  res.json({
    worldId: CLIENT_WORLD_ID,
    total: totalRow.count,
    limit,
    offset,
    players: rows.map((row, index) => ({
      rank: offset + index + 1,
      username: identities.get(normalizeWalletAddress(row.wallet_address))?.username ?? "",
      displayName: identities.get(normalizeWalletAddress(row.wallet_address))?.displayName ?? "",
      ...row
    }))
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
    worldId: CLIENT_WORLD_ID,
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

  if (!spacetimeReady || spacetimeConn == null) {
    res.status(503).json({ error: "SpacetimeDB is not ready." });
    return;
  }

  const intent = parsed.data;
  const wallet = (req as any).walletAddress as string;
  if (normalizeWalletAddress(intent.walletAddress) !== wallet) {
    res.status(403).json({ error: "Intent wallet does not match authenticated wallet." });
    return;
  }

  if (intent.worldId !== CLIENT_WORLD_ID) {
    res.status(400).json({ error: `Unsupported worldId '${intent.worldId}'.` });
    return;
  }

  if (!allowPerMinute(wallet)) {
    res.status(429).json({ error: "Intent rate limit exceeded for current minute." });
    return;
  }

  dispatchIntent(intent);
  res.json({ accepted: true, queued: true });
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

  const world = getWorldStateRow();
  insertCycleEvent(db, {
    cycleId: payload.cycleId,
    forestDelta: payload.forestDelta ?? 0,
    carbonDelta: payload.carbonDelta ?? 0,
    forestTotal: world ? toNumber(world.forestTotal) : 0,
    carbonTotal: world ? toNumber(world.carbonTotal) : 0,
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

  if (!spacetimeReady || spacetimeConn == null) {
    socket.close(1013, "SpacetimeDB not ready.");
    return;
  }

  const client = socket as GatewayWebSocket;
  client.walletAddress = authResult.walletAddress;
  websocketClients.add(client);

  void ensurePlayerForWallet(authResult.walletAddress)
    .then(() => {
      sendSnapshot(client);
    })
    .catch((error) => {
      console.error("[gateway] ensurePlayer failed during websocket connect", error);
      sendSnapshot(client);
    });

  const joined = incrementWalletConnection(authResult.walletAddress);
  if (joined) {
    const identity = getAccountIdentityByWallet(db, authResult.walletAddress);
    broadcast({
      type: "PlayerJoined",
      walletAddress: authResult.walletAddress,
      username: identity?.username ?? "",
      displayName: identity?.displayName ?? "",
      tick: getCurrentTick()
    });
  }

  socket.on("message", (data) => {
    try {
      const decoded = JSON.parse(data.toString()) as { type?: string; intent?: unknown; intents?: unknown };

      if (decoded.type === "ActionIntentBatchSubmit" && decoded.intents) {
        const parsedBatch = intentBatchSchema.safeParse({ intents: decoded.intents });
        if (!parsedBatch.success) {
          return;
        }

        const intents = parsedBatch.data.intents;
        for (const intent of intents) {
          if (normalizeWalletAddress(intent.walletAddress) !== authResult.walletAddress) {
            return;
          }

          if (intent.worldId !== CLIENT_WORLD_ID) {
            return;
          }

          if (!allowPerMinute(authResult.walletAddress)) {
            return;
          }
        }

        dispatchIntentBatch(intents);
        return;
      }

      if (decoded.type !== "ActionIntentSubmit" || !decoded.intent) {
        return;
      }

      const parsed = intentSchema.safeParse(decoded.intent);
      if (!parsed.success) {
        return;
      }

      const intent = parsed.data;
      if (normalizeWalletAddress(intent.walletAddress) !== authResult.walletAddress) {
        return;
      }

      if (intent.worldId !== CLIENT_WORLD_ID) {
        return;
      }

      if (!allowPerMinute(authResult.walletAddress)) {
        return;
      }

      dispatchIntent(intent);
    } catch {
      // Drop malformed payloads.
    }
  });

  socket.on("close", () => {
    websocketClients.delete(client);
    const left = decrementWalletConnection(authResult.walletAddress);
    if (left) {
      const identity = getAccountIdentityByWallet(db, authResult.walletAddress);
      broadcast({
        type: "PlayerLeft",
        walletAddress: authResult.walletAddress,
        username: identity?.username ?? "",
        displayName: identity?.displayName ?? ""
      });
    }
  });
});

connectToSpacetime();
setInterval(() => {
  void processPendingChainBatches();
}, CHAIN_RELAY_POLL_MS);

server.listen(PORT, () => {
  console.log(`[gateway] listening on http://localhost:${PORT}`);
});

function connectToSpacetime(): void {
  spacetimeReconnectScheduled = false;
  spacetimeReady = false;

  console.log(`[gateway] connecting to SpacetimeDB ${STDB_DATABASE} @ ${STDB_URI}`);

  const builder = DbConnection.builder()
    .withUri(STDB_URI)
    .withDatabaseName(STDB_DATABASE);

  if (STDB_TOKEN) {
    builder.withToken(STDB_TOKEN);
  }

  builder
    .onConnect((conn) => {
      spacetimeConn = conn;
      registerSpacetimeCallbacks(conn);
      void conn.procedures.ensureSeedCredentialAccount({})
        .catch((error) => {
          console.warn("[gateway] failed to ensure seed credential account", error);
        });
      conn.subscriptionBuilder()
        .onApplied(() => {
          spacetimeReady = true;
          console.log("[gateway] SpacetimeDB subscription applied");
          broadcastSnapshot();
        })
        .onError((ctx) => {
          spacetimeReady = false;
          console.error("[gateway] subscription error", ctx.event);
        })
        .subscribe([
          "SELECT * FROM world_state",
          "SELECT * FROM tiles",
          "SELECT * FROM players",
          "SELECT * FROM player_identities",
          "SELECT * FROM climate_events",
          "SELECT * FROM action_commit_events"
        ]);
    })
    .onConnectError((_ctx, error) => {
      spacetimeReady = false;
      console.error("[gateway] SpacetimeDB connect error", error);
      scheduleSpacetimeReconnect();
    })
    .onDisconnect((_ctx, error) => {
      spacetimeReady = false;
      spacetimeConn = null;
      ensuredWallets.clear();
      console.error("[gateway] SpacetimeDB disconnected", error);
      closeAllRealtimeClients("SpacetimeDB disconnected.");
      scheduleSpacetimeReconnect();
    })
    .build();
}

function registerSpacetimeCallbacks(conn: DbConnection): void {
  conn.db.ActionCommitEvents.onInsert((_ctx, row) => {
    handleActionCommitEvent(row);
  });

  conn.db.WorldState.onUpdate((_ctx, oldRow, newRow) => {
    const oldTick = toNumber(oldRow.tick);
    const newTick = toNumber(newRow.tick);
    const oldCycle = toNumber(oldRow.cycle);
    const newCycle = toNumber(newRow.cycle);

    if (newTick === oldTick && newCycle === oldCycle) {
      return;
    }

    broadcastCycleStarted(newTick);
    enqueueCycleBatch(newTick - 1);
  });

  conn.db.Players.onInsert((ctx, _row) => {
    if (ctx.event.tag === "SubscribeApplied") {
      return;
    }
  });

  conn.db.PlayerIdentities.onInsert((ctx, _row) => {
    if (ctx.event.tag === "SubscribeApplied") {
      return;
    }
  });

  conn.db.PlayerIdentities.onUpdate((ctx, _oldRow, _newRow) => {
    if (ctx.event.tag === "SubscribeApplied") {
      return;
    }
  });

  conn.db.ClimateEvents.onInsert((ctx, _row) => {
    if (ctx.event.tag === "SubscribeApplied") {
      return;
    }
  });
}

function dispatchIntent(intent: z.infer<typeof intentSchema>): void {
  if (spacetimeConn == null) {
    return;
  }

  // SubmitIntent reducer already calls EnsurePlayerRow internally, so there
  // is no need to await a separate ensurePlayer round-trip before each action.
  // ensurePlayer is called once at WebSocket connect (see wsServer.on("connection")).
  const dbCoord = clientToDbCoord(intent.q, intent.r);
  void spacetimeConn.reducers.submitIntent({
    intentId: intent.intentId,
    wallet: normalizeWalletAddress(intent.walletAddress),
    clientSeq: BigInt(intent.clientSeq),
    actionType: intent.actionType,
    q: dbCoord.q,
    r: dbCoord.r
  })
  .catch((error) => {
    console.error("[gateway] submitIntent failed", error);
  });
}

function dispatchIntentBatch(intents: z.infer<typeof intentSchema>[]): void {
  if (spacetimeConn == null || intents.length === 0) {
    return;
  }

  const first = intents[0];
  const wallet = normalizeWalletAddress(first.walletAddress);
  const intentIds: string[] = [];
  const clientSeqs: bigint[] = [];
  const actionTypes: number[] = [];
  const qs: number[] = [];
  const rs: number[] = [];

  for (const intent of intents) {
    if (normalizeWalletAddress(intent.walletAddress) !== wallet || intent.worldId !== CLIENT_WORLD_ID) {
      console.warn("[gateway] dropping malformed action batch");
      return;
    }

    const dbCoord = clientToDbCoord(intent.q, intent.r);
    intentIds.push(intent.intentId);
    clientSeqs.push(BigInt(intent.clientSeq));
    actionTypes.push(intent.actionType);
    qs.push(dbCoord.q);
    rs.push(dbCoord.r);
  }

  void spacetimeConn.reducers.submitIntentBatch({
    wallet,
    intentIds,
    clientSeqs,
    actionTypes,
    qs,
    rs
  }).catch((error) => {
    console.error("[gateway] submitIntentBatch failed", error);
  });
}

async function ensurePlayerForWallet(walletAddress: string): Promise<void> {
  if (spacetimeConn == null) {
    return;
  }

  const normalizedWallet = normalizeWalletAddress(walletAddress);
  if (ensuredWallets.has(normalizedWallet)) {
    return;
  }

  await spacetimeConn.reducers.ensurePlayer({ wallet: normalizedWallet });
  ensuredWallets.add(normalizedWallet);
}

function buildViewerPlayerSnapshot(viewerWalletAddress?: string): Record<string, unknown> | null {
  if (spacetimeConn == null) {
    return null;
  }

  const normalizedViewerWallet = normalizeWalletAddress(viewerWalletAddress ?? "");
  if (normalizedViewerWallet === "") {
    return null;
  }

  let playerRow: PlayerRow | null = null;
  for (const player of spacetimeConn.db.Players.iter()) {
    if (normalizeWalletAddress(player.wallet) === normalizedViewerWallet) {
      playerRow = player;
      break;
    }
  }

  if (playerRow == null) {
    return null;
  }

  const identity = getAccountIdentityByWallet(db, playerRow.wallet);
  const world = getWorldStateRow();
  return {
    walletAddress: playerRow.wallet,
    username: identity?.username ?? "",
    displayName: identity?.displayName ?? "",
    ownedTilesCount: playerRow.ownedTiles,
    sustainabilityScore: playerRow.sustainabilityScore,
    actionsTaken: playerRow.actionsTaken,
    actionsRemaining: world ? computeActionsRemaining(playerRow, world) : UNBOUNDED_ACTIONS_DISPLAY,
    lastClientSeq: toNumber(playerRow.lastClientSeq),
    wood: playerRow.wood,
    food: playerRow.food,
    minerals: playerRow.minerals,
    researchPoints: playerRow.researchPoints,
    techBasicForestry: playerRow.techBasicForestry === 1,
    techRenewableEnergy: playerRow.techRenewableEnergy === 1,
    techCarbonCapture: playerRow.techCarbonCapture === 1,
    ecoActions: playerRow.ecoActions,
    industrialActions: playerRow.industrialActions,
    agricultureActions: playerRow.agricultureActions,
    reputation: playerRow.reputation
  };
}

function getActiveClimateEventSnapshots(worldTick: number): Array<Record<string, unknown>> {
  if (spacetimeConn == null) {
    return [];
  }

  return Array.from(spacetimeConn.db.ClimateEvents.iter())
    .filter((eventRow) => toNumber(eventRow.startTick) <= worldTick && toNumber(eventRow.endTick) > worldTick)
    .map((eventRow) => ({
      id: toNumber(eventRow.id),
      type: eventRow.type,
      startTick: toNumber(eventRow.startTick),
      endTick: toNumber(eventRow.endTick)
    }))
    .sort((left, right) => Number(left.startTick) - Number(right.startTick) || Number(left.id) - Number(right.id));
}

function buildWorldSnapshotPayload(viewerWalletAddress?: string): Record<string, unknown> | null {
  if (!spacetimeReady || spacetimeConn == null) {
    return null;
  }

  const world = getWorldStateRow();
  if (!world) {
    return null;
  }

  const tiles = Array.from(spacetimeConn.db.Tiles.iter())
    .map((tile) => {
      const clientCoord = dbToClientCoord(tile.q, tile.r);
      return {
        q: clientCoord.q,
        r: clientCoord.r,
        currentState: terrainToName(tile.terrain),
        ownerWallet: tile.owner ?? "",
        buildingType: buildingToName(tile.building),
        lastUpdatedTick: toNumber(tile.lastUpdate)
      };
    })
    .sort((left, right) => left.r - right.r || left.q - right.q);

  const players = Array.from(spacetimeConn.db.Players.iter());
  const identities = getAccountIdentitiesByWallets(players.map((player) => player.wallet));
  const normalizedViewerWallet = normalizeWalletAddress(viewerWalletAddress ?? "");
  const playerSnapshots = players
    .map((player) => {
      const identity = identities.get(normalizeWalletAddress(player.wallet));
      const isViewerPlayer = normalizedViewerWallet !== ""
        && normalizeWalletAddress(player.wallet) === normalizedViewerWallet;
      return {
        walletAddress: player.wallet,
        username: identity?.username ?? "",
        displayName: identity?.displayName ?? "",
        ownedTilesCount: player.ownedTiles,
        sustainabilityScore: player.sustainabilityScore,
        actionsTaken: player.actionsTaken,
        actionsRemaining: computeActionsRemaining(player, world),
        lastClientSeq: toNumber(player.lastClientSeq),
        wood: isViewerPlayer ? player.wood : 0,
        food: isViewerPlayer ? player.food : 0,
        minerals: isViewerPlayer ? player.minerals : 0,
        researchPoints: isViewerPlayer ? player.researchPoints : 0,
        techBasicForestry: isViewerPlayer ? player.techBasicForestry === 1 : false,
        techRenewableEnergy: isViewerPlayer ? player.techRenewableEnergy === 1 : false,
        techCarbonCapture: isViewerPlayer ? player.techCarbonCapture === 1 : false,
        ecoActions: isViewerPlayer ? player.ecoActions : 0,
        industrialActions: isViewerPlayer ? player.industrialActions : 0,
        agricultureActions: isViewerPlayer ? player.agricultureActions : 0,
        reputation: isViewerPlayer ? player.reputation : ""
      };
    })
    .sort((left, right) => left.walletAddress.localeCompare(right.walletAddress));

  const worldTick = toNumber(world.tick);
  const climateEvents = getActiveClimateEventSnapshots(worldTick);

  return {
    type: "WorldSnapshot",
    worldId: world.worldId,
    tick: worldTick,
    cycleSeconds: CYCLE_SECONDS,
    actionsPerCycle: UNBOUNDED_ACTIONS_DISPLAY,
    globalForestToken: world.forestTotal,
    globalCarbonToken: world.carbonTotal,
    tiles,
    players: playerSnapshots,
    climateEvents
  };
}

function buildCycleStartedPayload(viewerWalletAddress: string | undefined, tick: number): Record<string, unknown> | null {
  if (!spacetimeReady || spacetimeConn == null) {
    return null;
  }

  const world = getWorldStateRow();
  if (!world) {
    return null;
  }

  return {
    type: "CycleStarted",
    tick,
    startedAtUnixMs: Date.now(),
    globalForestToken: world.forestTotal,
    globalCarbonToken: world.carbonTotal,
    player: buildViewerPlayerSnapshot(viewerWalletAddress),
    climateEvents: getActiveClimateEventSnapshots(toNumber(world.tick))
  };
}

function sendJson(socket: GatewayWebSocket, payload: Record<string, unknown> | null): void {
  if (!payload) {
    return;
  }

  try {
    socket.send(JSON.stringify(payload));
  } catch {
    // Ignore client send failures.
  }
}

function sendSnapshot(socket: GatewayWebSocket): void {
  sendJson(socket, buildWorldSnapshotPayload(socket.walletAddress));
}

function broadcastSnapshot(): void {
  for (const socket of websocketClients) {
    sendSnapshot(socket);
  }
}

function broadcastCycleStarted(tick: number): void {
  for (const socket of websocketClients) {
    sendJson(socket, buildCycleStartedPayload(socket.walletAddress, tick));
  }
}

function buildRemoteTileChangedPayload(commit: GatewayActionCommit): Record<string, unknown> | null {
  if (!commit.accepted) {
    return null;
  }

  const tileDeltas = collectRealtimeTileDeltas(commit);
  if (tileDeltas.length === 0) {
    return null;
  }

  return {
    type: "RemoteTileChanged",
    walletAddress: commit.walletAddress,
    tick: commit.tick,
    tileDeltas
  };
}

function collectRealtimeTileDeltas(commit: GatewayActionCommit): TileDeltaPayload[] {
  const byCoord = new Map<string, TileDeltaPayload>();
  for (const delta of commit.tileDeltas) {
    byCoord.set(`${delta.q}:${delta.r}`, delta);
  }

  if (commit.actionType === 1 && spacetimeConn != null) {
    const normalizedWallet = normalizeWalletAddress(commit.walletAddress);
    for (const tile of spacetimeConn.db.Tiles.iter()) {
      if (normalizeWalletAddress(tile.owner) !== normalizedWallet) {
        continue;
      }

      const clientCoord = dbToClientCoord(tile.q, tile.r);
      if (offsetHexDistance(clientCoord.q, clientCoord.r, commit.q, commit.r) > SETTLEMENT_RADIUS) {
        continue;
      }

      const key = `${clientCoord.q}:${clientCoord.r}`;
      if (byCoord.has(key)) {
        continue;
      }

      byCoord.set(key, {
        q: clientCoord.q,
        r: clientCoord.r,
        previousTerrain: terrainToName(tile.terrain),
        nextTerrain: terrainToName(tile.terrain),
        previousBuilding: buildingToName(tile.building),
        nextBuilding: buildingToName(tile.building),
        ownerChanged: true,
        ownerWallet: tile.owner ?? "",
        lastUpdatedTick: toNumber(tile.lastUpdate)
      });
    }
  }

  return Array.from(byCoord.values());
}

function handleActionCommitEvent(row: ActionCommitEventRow): void {
  const commit = toGatewayActionCommit(row);
  persistActionCommit(db, commit);
  updateLeaderboard(db, commit);

  if (commit.accepted) {
    const list = acceptedCommitsByTick.get(commit.tick) ?? [];
    list.push(commit);
    acceptedCommitsByTick.set(commit.tick, list);

    // ── On-chain transparency: queue tile-claim NFT mint on MegaETH ──────────
    // commit.walletAddress comes lowercased from SpacetimeDB; ethers.isAddress
    // accepts both checksummed and lowercase 0x addresses, so this is fine.
    if (chainEnabled && tileNftContract && commit.actionType === A_CLAIM
        && commit.walletAddress && isAddress(commit.walletAddress)) {
      // ethers expects checksummed address for contract calls
      const checksummedWallet = commit.walletAddress; // ethers handles lowercase automatically
      pendingTileMints.push({ wallet: checksummedWallet, q: commit.q, r: commit.r });
    }
  }

  broadcast({
    type: "ActionCommitted",
    commitId: commit.commitId,
    tick: commit.tick,
    intentId: commit.intentId,
    accepted: commit.accepted,
    reason: commit.reason,
    walletAddress: commit.walletAddress,
    actionType: commit.actionType,
    q: commit.q,
    r: commit.r,
    tileDeltas: commit.tileDeltas,
    playerDelta: commit.playerDelta,
    globalDelta: {
      forestDelta: commit.globalForestDelta,
      carbonDelta: commit.globalCarbonDelta,
      actionCount: commit.accepted ? 1 : 0
    },
    batchHash: commit.batchHash
  });

  const remoteTileChanged = buildRemoteTileChangedPayload(commit);
  if (remoteTileChanged) {
    broadcast(remoteTileChanged);
  }
}

async function drainTileMintQueue(): Promise<void> {
  if (!tileNftContract || pendingTileMints.length === 0) return;

  const batch = pendingTileMints.splice(0, 10); // process up to 10 per tick
  for (const { wallet, q, r } of batch) {
    try {
      const tx = await tileNftContract.claimTile(wallet, q, r);
      console.log(`[chain] TileNFT claimTile(${wallet}, ${q}, ${r}) tx=${tx.hash}`);
      // Track on-chain tile count in the leaderboard.
      const normalizedWallet = normalizeWalletAddress(wallet);
      db.prepare(`
        INSERT INTO leaderboard (wallet_address, sustainability_score, actions_taken, owned_tiles_count, tile_nft_count, updated_at_ms)
        VALUES (?, 0, 0, 0, 1, ?)
        ON CONFLICT(wallet_address) DO UPDATE SET
          tile_nft_count = tile_nft_count + 1,
          updated_at_ms = excluded.updated_at_ms
      `).run(normalizedWallet, Date.now());
    } catch (err) {
      console.error(`[chain] TileNFT claimTile failed for (${q},${r}):`, err);
      // Re-queue on failure so it retries next interval.
      pendingTileMints.push({ wallet, q, r });
    }
  }
}

function toGatewayActionCommit(row: ActionCommitEventRow): GatewayActionCommit {
  const tileCoord = dbToClientCoord(row.q, row.r);

  return {
    worldId: row.worldId,
    commitId: row.commitId,
    tick: toNumber(row.tick),
    intentId: row.intentId,
    accepted: row.accepted,
    reason: row.reason,
    globalForestDelta: row.forestDelta,
    globalCarbonDelta: row.carbonDelta,
    batchHash: row.batchHash,
    tileDeltas: row.includeTileDelta
      ? [{
          q: tileCoord.q,
          r: tileCoord.r,
          previousTerrain: terrainToName(row.previousTerrain),
          nextTerrain: terrainToName(row.nextTerrain),
          previousBuilding: buildingToName(row.previousBuilding),
          nextBuilding: buildingToName(row.nextBuilding),
          ownerChanged: row.ownerChanged,
          ownerWallet: row.ownerWallet ?? "",
          lastUpdatedTick: toNumber(row.lastUpdatedTick)
        }]
      : [],
    playerDelta: {
      walletAddress: row.wallet,
      ownedTilesDelta: row.ownedTilesDelta,
      sustainabilityScoreDelta: row.sustainabilityScoreDelta,
      actionsTakenDelta: row.actionsTakenDelta,
      actionsRemainingDelta: row.actionsRemainingDelta,
      resourceDelta: {
        wood: row.woodDelta,
        food: row.foodDelta,
        minerals: row.mineralsDelta
      }
    },
    committedAtMs: toNumber(row.committedAtMs),
    walletAddress: row.wallet,
    actionType: row.actionType,
    q: tileCoord.q,
    r: tileCoord.r
  };
}

function getWorldStateRow(): WorldStateRow | null {
  if (spacetimeConn == null) {
    return null;
  }

  let first: WorldStateRow | null = null;
  for (const row of spacetimeConn.db.WorldState.iter()) {
    if (first == null) {
      first = row;
    }

    if (row.worldId === CLIENT_WORLD_ID) {
      return row;
    }
  }

  return first;
}

function getCurrentTick(): number {
  const world = getWorldStateRow();
  return world ? toNumber(world.tick) : 0;
}

function computeActionsRemaining(player: PlayerRow, world: WorldStateRow): number {
  void player;
  void world;
  return UNBOUNDED_ACTIONS_DISPLAY;
}

function offsetHexDistance(q1: number, r1: number, q2: number, r2: number): number {
  const x1 = q1 - Math.floor((r1 - (r1 & 1)) / 2);
  const z1 = r1;
  const y1 = -x1 - z1;
  const x2 = q2 - Math.floor((r2 - (r2 & 1)) / 2);
  const z2 = r2;
  const y2 = -x2 - z2;
  return Math.max(Math.abs(x1 - x2), Math.abs(y1 - y2), Math.abs(z1 - z2));
}

// Server now stores offset coordinates (q = column index), same as Unity client.
// No conversion needed — pass through directly.
function clientToDbCoord(q: number, r: number): { q: number; r: number } {
  return { q, r };
}

function dbToClientCoord(q: number, r: number): { q: number; r: number } {
  return { q, r };
}

function terrainToName(value: number): string {
  switch (value) {
    case 0: return "Forest";
    case 1: return "Plains";
    case 2: return "Mountain";
    case 3: return "Water";
    case 4: return "Desert";
    case 5: return "Barren";
    case 6: return "DeforestedForest";
    case 7: return "Farmland";
    case 8: return "Ice";
    default: return "Plains";
  }
}

function buildingToName(value: number): string {
  switch (value) {
    case 1: return "Settlement";
    case 2: return "Industry";
    case 3: return "RecoveryProject";
    case 4: return "Barracks";
    default: return "None";
  }
}

function scheduleSpacetimeReconnect(): void {
  if (spacetimeReconnectScheduled) {
    return;
  }

  spacetimeReconnectScheduled = true;
  setTimeout(() => {
    connectToSpacetime();
  }, 2000);
}

function closeAllRealtimeClients(reason: string): void {
  for (const socket of websocketClients) {
    try {
      socket.close(1013, reason);
    } catch {
      // Ignore close failures.
    }
  }

  websocketClients.clear();
  connectedWalletCounts.clear();
}

function incrementWalletConnection(walletAddress: string): boolean {
  const current = connectedWalletCounts.get(walletAddress) ?? 0;
  connectedWalletCounts.set(walletAddress, current + 1);
  return current === 0;
}

function decrementWalletConnection(walletAddress: string): boolean {
  const current = connectedWalletCounts.get(walletAddress) ?? 0;
  if (current <= 1) {
    connectedWalletCounts.delete(walletAddress);
    return current > 0;
  }

  connectedWalletCounts.set(walletAddress, current - 1);
  return false;
}

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

    // ── Sync forest/carbon token supply on MegaETH after cycle commit ────────
    const cycleIdBig = BigInt(next.cycleId);
    if (forestTokenContract) {
      void forestTokenContract
        .syncForest(cycleIdBig, BigInt(next.forestDelta), relayerAddress)
        .then((t: { hash: string }) => console.log(`[chain] FRT syncForest cycle=${next.cycleId} tx=${t.hash}`))
        .catch((e: unknown) => console.error("[chain] FRT syncForest failed:", e));
    }
    if (carbonTokenContract) {
      void carbonTokenContract
        .syncCarbon(cycleIdBig, BigInt(next.carbonDelta), relayerAddress)
        .then((t: { hash: string }) => console.log(`[chain] CRT syncCarbon cycle=${next.cycleId} tx=${t.hash}`))
        .catch((e: unknown) => console.error("[chain] CRT syncCarbon failed:", e));
    }
  } catch (error) {
    next.attempt += 1;
    const backoffMs = Math.min(60_000, 1000 * (2 ** next.attempt));
    next.nextAttemptAtMs = Date.now() + backoffMs;
    console.error(`[gateway] chain commit failed cycle=${next.cycleId} retryInMs=${backoffMs}`, error);
  }
}

function finalizeCycleBatch(batch: ChainCycleBatch, txHash: string): void {
  const world = getWorldStateRow();
  insertCycleEvent(db, {
    cycleId: batch.cycleId,
    forestDelta: batch.forestDelta,
    carbonDelta: batch.carbonDelta,
    forestTotal: world ? toNumber(world.forestTotal) : 0,
    carbonTotal: world ? toNumber(world.carbonTotal) : 0,
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
    return { ok: true, walletAddress: normalizeWalletAddress(decoded.sub) };
  } catch {
    return { ok: false, error: "Invalid token." };
  }
}

function createAuthResponse(
  walletAddress: string,
  authMode: string,
  knownIdentity?: AccountIdentity | null
): AuthResponsePayload {
  const normalizedWallet = normalizeWalletAddress(walletAddress);
  const identity = knownIdentity ?? getAccountIdentityByWallet(db, normalizedWallet);
  const accessToken = jwt.sign(
    { sub: normalizedWallet, worldId: CLIENT_WORLD_ID, mode: authMode },
    JWT_SECRET,
    { expiresIn: TOKEN_TTL_SECONDS }
  );

  return {
    accessToken,
    expiresAtUnixMs: Date.now() + TOKEN_TTL_SECONDS * 1000,
    walletAddress: normalizedWallet,
    username: identity?.username ?? "",
    displayName: identity?.displayName ?? identity?.username ?? "",
    authMode
  };
}

function inferAuthMode(walletAddress: string): string {
  return getAccountIdentityByWallet(db, walletAddress) ? "credentials" : "wallet";
}

function normalizeWalletAddress(value: string): string {
  return value.trim().toLowerCase();
}

function normalizeUsername(value: string): string {
  return value.trim().toLowerCase();
}

async function authenticateWithSpacetimeCredentials(
  mode: "login" | "signup",
  username: string,
  password: string
): Promise<CredentialAuthResult> {
  if (spacetimeConn == null) {
    return {
      success: false,
      created: false,
      error: "SpacetimeDB auth is not ready.",
      wallet: "",
      username: "",
      displayName: ""
    };
  }

  try {
    const trimmedUsername = username.trim();
    if (mode === "login") {
      return await spacetimeConn.procedures.credentialLogin({
        username: trimmedUsername,
        password
      });
    }

    return await spacetimeConn.procedures.credentialSignup({
      username: trimmedUsername,
      password
    });
  } catch (error) {
    return {
      success: false,
      created: false,
      error: `SpacetimeDB credential ${mode} failed: ${String(error)}`,
      wallet: "",
      username: "",
      displayName: ""
    };
  }
}

function resolvePlayerIdentityByWallet(walletAddress: string): PlayerIdentityRow | null {
  if (spacetimeConn == null) {
    return null;
  }

  const normalizedWallet = normalizeWalletAddress(walletAddress);
  for (const identity of spacetimeConn.db.PlayerIdentities.iter()) {
    if (normalizeWalletAddress(identity.wallet) === normalizedWallet) {
      return identity;
    }
  }

  return null;
}

function getAccountIdentityByWallet(_database: DatabaseSync, walletAddress: string): AccountIdentity | null {
  const identity = resolvePlayerIdentityByWallet(walletAddress);
  if (!identity) {
    return null;
  }

  return {
    walletAddress: normalizeWalletAddress(identity.wallet),
    username: identity.username ?? "",
    displayName: identity.displayName ?? identity.username ?? ""
  };
}

function getAccountIdentitiesByWallets(walletAddresses: string[]): Map<string, AccountIdentity> {
  const identities = new Map<string, AccountIdentity>();
  for (const walletAddress of new Set(walletAddresses.map((value) => normalizeWalletAddress(value)).filter(Boolean))) {
    const identity = getAccountIdentityByWallet(db, walletAddress);
    if (identity) {
      identities.set(walletAddress, identity);
    }
  }

  return identities;
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

function setupDbSchema(database: DatabaseSync): void {
  database.exec(`
    CREATE TABLE IF NOT EXISTS accounts (
      account_id INTEGER PRIMARY KEY AUTOINCREMENT,
      username TEXT NOT NULL,
      username_normalized TEXT NOT NULL UNIQUE,
      display_name TEXT NOT NULL,
      password_hash TEXT NOT NULL,
      password_salt TEXT NOT NULL,
      wallet_address TEXT NOT NULL UNIQUE,
      created_at_ms INTEGER NOT NULL,
      updated_at_ms INTEGER NOT NULL
    );

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
      tile_nft_count INTEGER NOT NULL DEFAULT 0,
      updated_at_ms INTEGER NOT NULL
    );

    CREATE UNIQUE INDEX IF NOT EXISTS idx_accounts_username_normalized
      ON accounts(username_normalized);

    CREATE UNIQUE INDEX IF NOT EXISTS idx_accounts_wallet_address
      ON accounts(wallet_address);
  `);

  // Idempotent migration: add tile_nft_count if it doesn't exist yet.
  try {
    database.exec(`ALTER TABLE leaderboard ADD COLUMN tile_nft_count INTEGER NOT NULL DEFAULT 0`);
  } catch {
    // Column already exists — safe to ignore.
  }
}

function persistActionCommit(database: DatabaseSync, commit: GatewayActionCommit): void {
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

function updateLeaderboard(database: DatabaseSync, commit: GatewayActionCommit): void {
  if (!commit.playerDelta?.walletAddress) {
    return;
  }

  const walletAddress = normalizeWalletAddress(commit.playerDelta.walletAddress);
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
        wallet_address, sustainability_score, actions_taken, owned_tiles_count, tile_nft_count, updated_at_ms
      ) VALUES (?, ?, ?, ?, 0, ?)
      ON CONFLICT(wallet_address) DO UPDATE SET
        sustainability_score = excluded.sustainability_score,
        actions_taken = excluded.actions_taken,
        owned_tiles_count = excluded.owned_tiles_count,
        updated_at_ms = excluded.updated_at_ms
    `)
    .run(walletAddress, nextScore, nextActions, nextOwned, Date.now());
}

function insertCycleEvent(
  database: DatabaseSync,
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

function rowCount(database: DatabaseSync, tableName: string): number {
  const row = database.prepare(`SELECT COUNT(1) AS count FROM ${tableName}`).get() as { count: number };
  return row.count;
}

function parseIntQuery(raw: unknown, fallback: number, min: number, max: number): number {
  const parsed = Number(raw);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.min(max, Math.max(min, Math.trunc(parsed)));
}

function toNumber(value: number | bigint): number {
  return typeof value === "bigint" ? Number(value) : value;
}
