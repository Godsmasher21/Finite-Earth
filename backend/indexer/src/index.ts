import "dotenv/config";
import Database from "better-sqlite3";
import express from "express";
import { Contract, JsonRpcProvider } from "ethers";

const PORT = Number(process.env.PORT ?? 8090);
const DB_PATH = process.env.INDEXER_DB_PATH ?? "./indexer.db";
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:8080";
const MEGAETH_RPC_URL = process.env.MEGAETH_RPC_URL ?? "http://localhost:8545";
const GLOBAL_COUNTERS_ADDRESS = process.env.GLOBAL_COUNTERS_ADDRESS ?? "";

const app = express();
const db = new Database(DB_PATH);
const provider = new JsonRpcProvider(MEGAETH_RPC_URL);

const ABI = [
  "event CycleCommitted(uint64 indexed cycleId,int256 forestDelta,int256 carbonDelta,int256 forestTotal,int256 carbonTotal,bytes32 actionBatchHash,uint32 actionCount)"
];

setupSchema();

if (GLOBAL_COUNTERS_ADDRESS) {
  const contract = new Contract(GLOBAL_COUNTERS_ADDRESS, ABI, provider);
  contract.on("CycleCommitted", (cycleId, forestDelta, carbonDelta, forestTotal, carbonTotal, batchHash, actionCount, event) => {
    const stmt = db.prepare(`
      INSERT OR IGNORE INTO cycle_events (
        cycle_id, forest_delta, carbon_delta, forest_total, carbon_total, batch_hash, action_count, tx_hash, block_number, created_at_ms
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `);
    stmt.run(
      Number(cycleId),
      Number(forestDelta),
      Number(carbonDelta),
      Number(forestTotal),
      Number(carbonTotal),
      String(batchHash),
      Number(actionCount),
      String(event?.transactionHash ?? ""),
      Number(event?.blockNumber ?? 0),
      Date.now()
    );
  });
}

app.get("/health", (_req, res) => {
  res.json({
    ok: true,
    rows: {
      cycle_events: rowCount("cycle_events"),
      action_commits: rowCount("action_commits"),
      leaderboard: rowCount("leaderboard")
    }
  });
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
    rows
  });
});

app.get("/metrics/timeseries", (_req, res) => {
  const rows = db
    .prepare(`
      SELECT cycle_id, forest_total, carbon_total, action_count, created_at_ms
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

app.listen(PORT, () => {
  console.log(`[indexer] listening on http://localhost:${PORT}`);
});

void pollGatewayCommits();

async function pollGatewayCommits(): Promise<void> {
  // eslint-disable-next-line no-constant-condition
  while (true) {
    try {
      const response = await fetch(`${GATEWAY_URL}/internal/commits/pending`);
      if (response.ok) {
        const data = await response.json() as { commits?: Array<any> };
        const commits = data.commits ?? [];
        ingestCommits(commits);
      }
    } catch (error) {
      console.error("[indexer] commit polling failed:", error);
    }

    await sleep(5000);
  }
}

function ingestCommits(commits: Array<any>): void {
  const insertCommit = db.prepare(`
    INSERT OR IGNORE INTO action_commits (
      commit_id, tick, intent_id, accepted, reason, forest_delta, carbon_delta, batch_hash, committed_at_ms
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
  `);

  const upsertLeaderboard = db.prepare(`
    INSERT INTO leaderboard (
      wallet_address, sustainability_score, actions_taken, owned_tiles_count, updated_at_ms
    ) VALUES (?, ?, ?, ?, ?)
    ON CONFLICT(wallet_address) DO UPDATE SET
      sustainability_score = excluded.sustainability_score,
      actions_taken = excluded.actions_taken,
      owned_tiles_count = excluded.owned_tiles_count,
      updated_at_ms = excluded.updated_at_ms
  `);

  const tx = db.transaction((rows: Array<any>) => {
    for (const commit of rows) {
      insertCommit.run(
        commit.commitId,
        commit.tick,
        commit.intentId,
        commit.accepted ? 1 : 0,
        commit.reason,
        commit.globalForestDelta,
        commit.globalCarbonDelta,
        commit.batchHash,
        commit.committedAtMs ?? Date.now()
      );

      if (commit.playerDelta?.walletAddress) {
        const existing = db
          .prepare("SELECT sustainability_score, actions_taken, owned_tiles_count FROM leaderboard WHERE wallet_address = ?")
          .get(commit.playerDelta.walletAddress) as { sustainability_score: number; actions_taken: number; owned_tiles_count: number } | undefined;

        const nextScore = (existing?.sustainability_score ?? 0) + Number(commit.playerDelta.sustainabilityScoreDelta ?? 0);
        const nextActions = (existing?.actions_taken ?? 0) + Number(commit.playerDelta.actionsTakenDelta ?? 0);
        const nextOwned = (existing?.owned_tiles_count ?? 0) + Number(commit.playerDelta.ownedTilesDelta ?? 0);

        upsertLeaderboard.run(
          commit.playerDelta.walletAddress,
          nextScore,
          nextActions,
          nextOwned,
          Date.now()
        );
      }
    }
  });

  tx(commits);
}

function setupSchema(): void {
  db.exec(`
    CREATE TABLE IF NOT EXISTS cycle_events (
      cycle_id INTEGER PRIMARY KEY,
      forest_delta INTEGER NOT NULL,
      carbon_delta INTEGER NOT NULL,
      forest_total INTEGER NOT NULL,
      carbon_total INTEGER NOT NULL,
      batch_hash TEXT NOT NULL,
      action_count INTEGER NOT NULL,
      tx_hash TEXT NOT NULL,
      block_number INTEGER NOT NULL,
      created_at_ms INTEGER NOT NULL
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

    CREATE TABLE IF NOT EXISTS leaderboard (
      wallet_address TEXT PRIMARY KEY,
      sustainability_score INTEGER NOT NULL,
      actions_taken INTEGER NOT NULL,
      owned_tiles_count INTEGER NOT NULL,
      updated_at_ms INTEGER NOT NULL
    );
  `);
}

function rowCount(tableName: string): number {
  const row = db.prepare(`SELECT COUNT(1) AS count FROM ${tableName}`).get() as { count: number };
  return row.count;
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
