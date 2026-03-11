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

type CycleDelta = {
  tick: number;
  forestDelta: number;
  carbonDelta: number;
  tilesOwned: Record<string, number>;
};

const STDB_HTTP_URL = process.env.STDB_HTTP_URL ?? "";
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:8080";
const USE_GATEWAY = process.env.RELAYER_USE_GATEWAY === "true";

const MEGAETH_RPC_URL = process.env.MEGAETH_RPC_URL ?? "http://localhost:8545";
const RELAYER_PRIVATE_KEY = process.env.RELAYER_PRIVATE_KEY ?? "";
const GLOBAL_FOREST_TOKEN_ADDRESS = process.env.GLOBAL_FOREST_TOKEN_ADDRESS ?? "";
const GLOBAL_CARBON_TOKEN_ADDRESS = process.env.GLOBAL_CARBON_TOKEN_ADDRESS ?? "";
const TILES_OWNED_SBT_ADDRESS = process.env.TILES_OWNED_SBT_ADDRESS ?? "";
const POLL_MS = Number(process.env.RELAYER_POLL_MS ?? 3000);
const MAX_BATCH_COMMITS = 200;

const ERC20_ABI = [
  "function mint(address to,uint256 amount) external",
  "function burn(address from,uint256 amount) external"
];

const SBT_ABI = [
  "function setBalance(address wallet,uint256 amount) external"
];

if (!RELAYER_PRIVATE_KEY || !GLOBAL_FOREST_TOKEN_ADDRESS || !GLOBAL_CARBON_TOKEN_ADDRESS || !TILES_OWNED_SBT_ADDRESS) {
  console.error("[relayer] missing token addresses or RELAYER_PRIVATE_KEY");
  process.exit(1);
}

const provider = new JsonRpcProvider(MEGAETH_RPC_URL);
const wallet = new Wallet(RELAYER_PRIVATE_KEY, provider);
const forestToken = new Contract(GLOBAL_FOREST_TOKEN_ADDRESS, ERC20_ABI, wallet);
const carbonToken = new Contract(GLOBAL_CARBON_TOKEN_ADDRESS, ERC20_ABI, wallet);
const tilesSbt = new Contract(TILES_OWNED_SBT_ADDRESS, SBT_ABI, wallet);

const retryBackoffMs = new Map<string, number>();
const nextAllowedAttemptAt = new Map<string, number>();

async function tick(): Promise<void> {
  const deltas = await fetchCycleDeltas();
  if (deltas.length === 0) {
    return;
  }

  const cycle = deltas[0];
  await applyTokenDelta(forestToken, cycle.forestDelta);
  await applyTokenDelta(carbonToken, cycle.carbonDelta);

  const tilesEntries = Object.entries(cycle.tilesOwned ?? {});
  for (const [walletAddress, balance] of tilesEntries) {
    await tilesSbt.setBalance(walletAddress, BigInt(balance));
  }

  if (USE_GATEWAY) {
    await ackGatewayCommits(cycle.tick);
  }
}

async function fetchCycleDeltas(): Promise<CycleDelta[]> {
  if (USE_GATEWAY) {
    return fetchFromGateway();
  }

  if (!STDB_HTTP_URL) {
    return [];
  }

  console.warn("[relayer] STDB polling not configured yet.");
  return [];
}

async function fetchFromGateway(): Promise<CycleDelta[]> {
  const pending = await fetchPendingCommits();
  if (pending.length === 0) {
    return [];
  }

  const selected = pending
    .filter((row) => allowAttempt(row.commitId))
    .slice(0, MAX_BATCH_COMMITS);

  if (selected.length === 0) {
    return [];
  }

  const tick = selected[0].tick;
  const cycleBatch = selected.filter((row) => row.tick === tick).slice(0, MAX_BATCH_COMMITS);
  const forestDelta = cycleBatch.reduce((sum, item) => sum + item.globalForestDelta, 0);
  const carbonDelta = cycleBatch.reduce((sum, item) => sum + item.globalCarbonDelta, 0);

  return [
    {
      tick,
      forestDelta,
      carbonDelta,
      tilesOwned: {}
    }
  ];
}

async function fetchPendingCommits(): Promise<CommitRow[]> {
  const response = await fetch(`${GATEWAY_URL}/internal/commits/pending`);
  if (!response.ok) {
    throw new Error(`Pending commits request failed (${response.status})`);
  }

  const data = await response.json() as { commits: CommitRow[] };
  return data.commits ?? [];
}

async function ackGatewayCommits(tick: number): Promise<void> {
  const pending = await fetchPendingCommits();
  const cycleBatch = pending.filter((row) => row.tick === tick);

  for (const commit of cycleBatch) {
    try {
      await ackCommit(commit.commitId);
      retryBackoffMs.delete(commit.commitId);
      nextAllowedAttemptAt.delete(commit.commitId);
    } catch (error) {
      registerFailure(commit.commitId);
      console.error(`[relayer] ack failed for ${commit.commitId}:`, error);
    }
  }
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

async function applyTokenDelta(contract: Contract, delta: number): Promise<void> {
  if (delta === 0) {
    return;
  }

  if (delta > 0) {
    const tx = await contract.mint(wallet.address, BigInt(delta));
    await tx.wait(1);
    return;
  }

  const burnAmount = Math.abs(delta);
  const tx = await contract.burn(wallet.address, BigInt(burnAmount));
  await tx.wait(1);
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
