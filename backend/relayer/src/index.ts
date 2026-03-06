import "dotenv/config";
import { Contract, JsonRpcProvider, Wallet } from "ethers";

type CommitRow = {
  commitId: string;
  tick: number;
  intentId: string;
  accepted: boolean;
  globalForestDelta: number;
  globalCarbonDelta: number;
  batchHash: string;
};

const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:8080";
const MEGAETH_RPC_URL = process.env.MEGAETH_RPC_URL ?? "http://localhost:8545";
const RELAYER_PRIVATE_KEY = process.env.RELAYER_PRIVATE_KEY ?? "";
const GLOBAL_COUNTERS_ADDRESS = process.env.GLOBAL_COUNTERS_ADDRESS ?? "";
const POLL_MS = Number(process.env.RELAYER_POLL_MS ?? 3000);
const MAX_BATCH_COMMITS = 200;

const ABI = [
  "function commitCycle(uint64 cycleId,int256 forestDelta,int256 carbonDelta,bytes32 actionBatchHash,uint32 actionCount) external",
  "event CycleCommitted(uint64 indexed cycleId,int256 forestDelta,int256 carbonDelta,int256 forestTotal,int256 carbonTotal,bytes32 actionBatchHash,uint32 actionCount)"
];

if (!RELAYER_PRIVATE_KEY || !GLOBAL_COUNTERS_ADDRESS) {
  console.error("[relayer] missing RELAYER_PRIVATE_KEY or GLOBAL_COUNTERS_ADDRESS");
  process.exit(1);
}

const provider = new JsonRpcProvider(MEGAETH_RPC_URL);
const wallet = new Wallet(RELAYER_PRIVATE_KEY, provider);
const counters = new Contract(GLOBAL_COUNTERS_ADDRESS, ABI, wallet);

const retryBackoffMs = new Map<string, number>();
const nextAllowedAttemptAt = new Map<string, number>();

async function tick(): Promise<void> {
  const pending = await fetchPendingCommits();
  if (pending.length === 0) {
    return;
  }

  const selected = pending
    .filter((row) => allowAttempt(row.commitId))
    .slice(0, MAX_BATCH_COMMITS);

  if (selected.length === 0) {
    return;
  }

  const cycleId = selected[0].tick;
  const cycleBatch = selected.filter((row) => row.tick === cycleId).slice(0, MAX_BATCH_COMMITS);
  const forestDelta = cycleBatch.reduce((sum, item) => sum + item.globalForestDelta, 0);
  const carbonDelta = cycleBatch.reduce((sum, item) => sum + item.globalCarbonDelta, 0);
  const actionCount = cycleBatch.filter((item) => item.accepted).length;
  const batchHash = cycleBatch[cycleBatch.length - 1].batchHash;

  try {
    const tx = await counters.commitCycle(
      BigInt(cycleId),
      BigInt(forestDelta),
      BigInt(carbonDelta),
      batchHash,
      actionCount
    );

    const receipt = await tx.wait(1);
    console.log(`[relayer] committed cycle=${cycleId} tx=${receipt?.hash ?? tx.hash}`);

    await notifyGatewayCycleCommitted({
      tick: cycleId,
      cycleId,
      forestDelta,
      carbonDelta,
      txHash: receipt?.hash ?? tx.hash
    });

    for (const commit of cycleBatch) {
      await ackCommit(commit.commitId);
      retryBackoffMs.delete(commit.commitId);
      nextAllowedAttemptAt.delete(commit.commitId);
    }
  } catch (error) {
    console.error(`[relayer] commit failed cycle=${cycleId}:`, error);
    for (const commit of cycleBatch) {
      registerFailure(commit.commitId);
    }
  }
}

async function fetchPendingCommits(): Promise<CommitRow[]> {
  const response = await fetch(`${GATEWAY_URL}/internal/commits/pending`);
  if (!response.ok) {
    throw new Error(`Pending commits request failed (${response.status})`);
  }

  const data = await response.json() as { commits: CommitRow[] };
  return data.commits ?? [];
}

async function ackCommit(commitId: string): Promise<void> {
  const response = await fetch(`${GATEWAY_URL}/internal/commits/${encodeURIComponent(commitId)}/acked`, {
    method: "POST"
  });

  if (!response.ok) {
    throw new Error(`Ack commit failed for ${commitId} (${response.status})`);
  }
}

function allowAttempt(commitId: string): boolean {
  const nextAt = nextAllowedAttemptAt.get(commitId) ?? 0;
  return Date.now() >= nextAt;
}

function registerFailure(commitId: string): void {
  const previous = retryBackoffMs.get(commitId) ?? 1000;
  const next = Math.min(previous * 2, 60_000);
  retryBackoffMs.set(commitId, next);
  nextAllowedAttemptAt.set(commitId, Date.now() + next);
}

async function notifyGatewayCycleCommitted(payload: {
  tick: number;
  cycleId: number;
  forestDelta: number;
  carbonDelta: number;
  txHash: string;
}): Promise<void> {
  const response = await fetch(`${GATEWAY_URL}/internal/cycle-committed`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw new Error(`Failed to notify gateway of chain commit (${response.status})`);
  }
}

async function main(): Promise<void> {
  console.log("[relayer] started");

  // eslint-disable-next-line no-constant-condition
  while (true) {
    try {
      await tick();
    } catch (error) {
      console.error("[relayer] tick failed:", error);
    }

    await sleep(POLL_MS);
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

void main();
