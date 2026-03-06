import crypto from "node:crypto";
import { v4 as uuid } from "uuid";
import type {
  ActionCommit,
  ActionIntent,
  ActionType,
  BuildingType,
  GlobalDelta,
  PlayerDelta,
  PlayerState,
  TileDelta,
  TileState,
  TileType
} from "./types.js";

const CYCLE_SECONDS = 30;
const ACTIONS_PER_CYCLE = 9999;
const DEFORESTED_RECOVERY_CYCLES = 3;

const MAP_ROWS = [
  "WWWWWWWWWWWWWWWWWWWWWWWW",
  "WWWWWWWWWWWWWWWWWWWWWWWW",
  "WWPPPPPFFFFFPPPPPPPPWWWW",
  "WPPPPPFFFFFFPPPPPEPPPWWW",
  "WPPPPFFFFFFFPPPPPEEPPWWW",
  "WPPPPFFFMFFFPPPPPEEPPWWW",
  "WPPPPPPMMMMPPPPPEEEPPWWW",
  "WPPPPPPMMMPPPPPPEEEPPWWW",
  "WPPPPPPPPPPPAAAPPEEEPPWW",
  "WPPPPPPPPPPAAAAPPPEEPPWW",
  "WPPBBPPPPPPAAAPPPPPPPPWW",
  "WPPBBBPPPPPPPPPPPPPPPPWW",
  "WWPPPPPDDPPPPPPPPPPPPWWW",
  "WWWWPPPPPPPPPPPPPPWWWWWW",
  "WWWWWWWWWWWWWWWWWWWWWWWW"
];

const TERRAIN_BY_SYMBOL: Record<string, TileType> = {
  F: "Forest",
  P: "Plains",
  M: "Mountain",
  W: "Water",
  E: "Desert",
  B: "Barren",
  D: "DeforestedForest",
  A: "Farmland"
};

const CARBON_VALUE: Record<TileType, number> = {
  Forest: 4,
  Plains: 1,
  Water: 1,
  Farmland: 0,
  Mountain: 0,
  Desert: -1,
  DeforestedForest: -2,
  Barren: -3
};

const BUILDING_CARBON: Record<BuildingType, number> = {
  None: 0,
  Settlement: -1,
  Industry: -3,
  RecoveryProject: 2
};

const ACTION_ORDER: ActionType[] = [
  "Claim",
  "BuildSettlement",
  "BuildIndustry",
  "HarvestForest",
  "Reforest",
  "Farm",
  "Irrigate",
  "Mine",
  "Restore",
  "EndTurn"
];

const ACTION_INDEX = new Map<ActionType, number>(ACTION_ORDER.map((action, idx) => [action, idx]));

type WorldSnapshot = {
  worldId: string;
  tick: number;
  cycleSeconds: number;
  actionsPerCycle: number;
  forestTotal: number;
  carbonTotal: number;
};

type FullWorldSnapshot = WorldSnapshot & {
  tiles: Array<{
    q: number;
    r: number;
    currentState: TileType;
    ownerWallet: string;
    buildingType: BuildingType;
    lastUpdatedTick: number;
  }>;
  players: PlayerState[];
};

export class InMemoryAuthoritativeWorld {
  private readonly worldId: string;
  private readonly tiles = new Map<string, TileState>();
  private readonly players = new Map<string, PlayerState>();
  private readonly intentQueue: ActionIntent[] = [];
  private readonly actionCommits: ActionCommit[] = [];
  private cycleTimer: NodeJS.Timeout | null = null;
  private tick = 1;
  private forestTotal = 0;
  private carbonTotal = 0;
  private cycleListener?: (tick: number) => void;
  private commitListener?: (commit: ActionCommit) => void;

  constructor(worldId = "finite-earth-alpha") {
    this.worldId = worldId;
    this.initializeTiles();
    this.recalculateGlobalCounters();
  }

  setCycleListener(listener: (tick: number) => void): void {
    this.cycleListener = listener;
  }

  setCommitListener(listener: (commit: ActionCommit) => void): void {
    this.commitListener = listener;
  }

  start(): void {
    if (this.cycleTimer) {
      return;
    }

    this.cycleTimer = setInterval(() => {
      this.advanceCycle();
    }, CYCLE_SECONDS * 1000);
  }

  stop(): void {
    if (!this.cycleTimer) {
      return;
    }

    clearInterval(this.cycleTimer);
    this.cycleTimer = null;
  }

  submitIntent(intent: ActionIntent): void {
    this.ensurePlayer(intent.walletAddress);

    const player = this.players.get(intent.walletAddress);
    if (!player) {
      return;
    }

    if (intent.clientSeq <= player.lastClientSeq) {
      return;
    }

    player.lastClientSeq = intent.clientSeq;
    this.intentQueue.push(intent);
  }

  listPendingCommits(max = 250): ActionCommit[] {
    return this.actionCommits.slice(0, max);
  }

  markCommitAsRelayed(commitId: string): void {
    const idx = this.actionCommits.findIndex((commit) => commit.commitId === commitId);
    if (idx >= 0) {
      this.actionCommits.splice(idx, 1);
    }
  }

  getSnapshot(): WorldSnapshot {
    return {
      worldId: this.worldId,
      tick: this.tick,
      cycleSeconds: CYCLE_SECONDS,
      actionsPerCycle: ACTIONS_PER_CYCLE,
      forestTotal: this.forestTotal,
      carbonTotal: this.carbonTotal
    };
  }

  getFullSnapshot(): FullWorldSnapshot {
    return {
      ...this.getSnapshot(),
      tiles: Array.from(this.tiles.values()).map((tile) => ({
        q: tile.q,
        r: tile.r,
        currentState: tile.currentState,
        ownerWallet: tile.ownerWallet ?? "",
        buildingType: tile.buildingType,
        lastUpdatedTick: tile.lastUpdatedTick
      })),
      players: Array.from(this.players.values())
    };
  }

  getLeaderboard(limit = 100): PlayerState[] {
    return Array.from(this.players.values())
      .sort((a, b) => b.sustainabilityScore - a.sustainabilityScore)
      .slice(0, limit);
  }

  private advanceCycle(): void {
    const pending = this.intentQueue
      .splice(0, this.intentQueue.length)
      .sort((a, b) => {
        if (a.submittedAtMs !== b.submittedAtMs) return a.submittedAtMs - b.submittedAtMs;
        if (a.walletAddress !== b.walletAddress) return a.walletAddress.localeCompare(b.walletAddress);
        return a.intentId.localeCompare(b.intentId);
      });

    for (const intent of pending) {
      const player = this.players.get(intent.walletAddress);
      if (!player) continue;

      const commit = this.applyIntent(intent, player);
      this.publishCommit(commit);
    }

    const recoveryCommits = this.applyNaturalRecoveryCommits();
    for (const commit of recoveryCommits) {
      this.publishCommit(commit);
    }

    this.tick += 1;
    this.cycleListener?.(this.tick);
  }

  private applyIntent(intent: ActionIntent, player: PlayerState): ActionCommit {
    const key = tileKey(intent.q, intent.r);
    const tile = this.tiles.get(key);
    if (!tile) {
      return this.rejectCommit(intent, "Tile out of bounds.");
    }

    const validation = this.validate(intent, tile, player);
    if (!validation.accepted) {
      return this.rejectCommit(intent, validation.reason);
    }

    const previousTerrain = tile.currentState;
    const previousBuilding = tile.buildingType;
    const { nextTerrain, nextBuilding, ownerChanged } = transition(intent.actionType, tile);

    tile.currentState = nextTerrain;
    tile.buildingType = nextBuilding;
    tile.lastUpdatedTick = this.tick;

    if (ownerChanged) {
      tile.ownerWallet = intent.walletAddress;
      player.ownedTilesCount += 1;
    }

    const beforeCarbon = CARBON_VALUE[previousTerrain] + BUILDING_CARBON[previousBuilding];
    const afterCarbon = CARBON_VALUE[nextTerrain] + BUILDING_CARBON[nextBuilding];
    const carbonDelta = afterCarbon - beforeCarbon;
    const forestDelta = (nextTerrain === "Forest" ? 1 : 0) - (previousTerrain === "Forest" ? 1 : 0);
    this.forestTotal += forestDelta;
    this.carbonTotal += carbonDelta;

    player.actionsTaken += 1;
    player.sustainabilityScore += forestDelta - Math.max(0, -carbonDelta);

    const tileDelta: TileDelta = {
      q: intent.q,
      r: intent.r,
      previousTerrain,
      nextTerrain,
      previousBuilding,
      nextBuilding,
      ownerChanged,
      ownerWallet: ownerChanged ? intent.walletAddress : "",
      lastUpdatedTick: this.tick
    };

    const playerDelta: PlayerDelta = {
      walletAddress: intent.walletAddress,
      ownedTilesDelta: ownerChanged ? 1 : 0,
      sustainabilityScoreDelta: forestDelta - Math.max(0, -carbonDelta),
      actionsTakenDelta: 1,
      actionsRemainingDelta: 0
    };

    const globalDelta: GlobalDelta = {
      forestDelta,
      carbonDelta,
      actionCount: 1
    };

    const batchHash = crypto
      .createHash("sha256")
      .update(`${this.worldId}:${this.tick}:${intent.intentId}:${forestDelta}:${carbonDelta}`)
      .digest("hex");

    return {
      worldId: this.worldId,
      commitId: uuid(),
      tick: this.tick,
      intentId: intent.intentId,
      accepted: true,
      reason: "Accepted",
      globalForestDelta: globalDelta.forestDelta,
      globalCarbonDelta: globalDelta.carbonDelta,
      batchHash: `0x${batchHash}`,
      tileDeltas: [tileDelta],
      playerDelta,
      committedAtMs: Date.now()
    };
  }

  private validate(intent: ActionIntent, tile: TileState, player: PlayerState): { accepted: boolean; reason: string } {
    if (intent.actionType === "Claim") {
      if (tile.ownerWallet === intent.walletAddress) return { accepted: false, reason: "Tile already owned." };
      if (tile.currentState === "Water") return { accepted: false, reason: "Water cannot be claimed." };
      return { accepted: true, reason: "Accepted" };
    }

    if (tile.ownerWallet !== intent.walletAddress) {
      return { accepted: false, reason: "Tile must be owned before action." };
    }

    switch (intent.actionType) {
      case "BuildSettlement":
        if (tile.buildingType !== "None") return { accepted: false, reason: "Building already exists." };
        if (tile.currentState !== "Plains") return { accepted: false, reason: "Settlement requires plains." };
        return { accepted: true, reason: "Accepted" };
      case "BuildIndustry":
        if (tile.buildingType !== "None") return { accepted: false, reason: "Building already exists." };
        if (tile.currentState !== "Barren") return { accepted: false, reason: "Industry requires barren terrain." };
        return { accepted: true, reason: "Accepted" };
      case "HarvestForest":
        if (tile.buildingType !== "None") return { accepted: false, reason: "Building blocks harvest." };
        if (tile.currentState !== "Forest") return { accepted: false, reason: "Harvest requires forest." };
        return { accepted: true, reason: "Accepted" };
      case "Reforest":
        if (tile.buildingType !== "None") return { accepted: false, reason: "Building blocks reforest." };
        if (!["Plains", "Barren"].includes(tile.currentState)) return { accepted: false, reason: "Cannot reforest this terrain." };
        return { accepted: true, reason: "Accepted" };
      case "Farm":
        if (tile.buildingType !== "None") return { accepted: false, reason: "Building blocks farming." };
        if (tile.currentState !== "Plains") return { accepted: false, reason: "Farm requires plains." };
        return { accepted: true, reason: "Accepted" };
      case "Irrigate":
        if (tile.buildingType !== "None") return { accepted: false, reason: "Building blocks irrigation." };
        if (tile.currentState !== "Desert") return { accepted: false, reason: "Irrigation requires desert." };
        return { accepted: true, reason: "Accepted" };
      case "Mine":
        if (tile.buildingType !== "None") return { accepted: false, reason: "Building blocks mining." };
        if (tile.currentState !== "Mountain") return { accepted: false, reason: "Mine requires mountain." };
        return { accepted: true, reason: "Accepted" };
      case "Restore":
        if (tile.buildingType !== "None") return { accepted: false, reason: "Building blocks restore." };
        if (!["Barren", "DeforestedForest"].includes(tile.currentState)) return { accepted: false, reason: "Restore requires barren or deforested terrain." };
        return { accepted: true, reason: "Accepted" };
      case "EndTurn":
        return { accepted: true, reason: "Accepted" };
      default:
        return { accepted: false, reason: "Unsupported action." };
    }
  }

  private rejectCommit(intent: ActionIntent, reason: string): ActionCommit {
    const batchHash = crypto
      .createHash("sha256")
      .update(`${this.worldId}:${this.tick}:${intent.intentId}:rejected`)
      .digest("hex");

    return {
      worldId: this.worldId,
      commitId: uuid(),
      tick: this.tick,
      intentId: intent.intentId,
      accepted: false,
      reason,
      globalForestDelta: 0,
      globalCarbonDelta: 0,
      batchHash: `0x${batchHash}`,
      tileDeltas: [],
      playerDelta: {
        walletAddress: intent.walletAddress,
        ownedTilesDelta: 0,
        sustainabilityScoreDelta: 0,
        actionsTakenDelta: 0,
        actionsRemainingDelta: 0
      },
      committedAtMs: Date.now()
    };
  }

  private publishCommit(commit: ActionCommit): void {
    this.actionCommits.push(commit);
    this.commitListener?.(commit);
  }

  private applyNaturalRecoveryCommits(): ActionCommit[] {
    const commits: ActionCommit[] = [];
    for (const tile of this.tiles.values()) {
      if (tile.currentState !== "DeforestedForest") {
        continue;
      }

      if (this.tick - tile.lastUpdatedTick < DEFORESTED_RECOVERY_CYCLES) {
        continue;
      }

      const previousTerrain = tile.currentState;
      const previousBuilding = tile.buildingType;
      const nextTerrain: TileType = "Plains";
      const nextBuilding = tile.buildingType;

      tile.currentState = nextTerrain;
      tile.lastUpdatedTick = this.tick;

      const beforeCarbon = CARBON_VALUE[previousTerrain] + BUILDING_CARBON[previousBuilding];
      const afterCarbon = CARBON_VALUE[nextTerrain] + BUILDING_CARBON[nextBuilding];
      const carbonDelta = afterCarbon - beforeCarbon;
      const forestDelta = 0;

      this.forestTotal += forestDelta;
      this.carbonTotal += carbonDelta;

      const tileDelta: TileDelta = {
        q: tile.q,
        r: tile.r,
        previousTerrain,
        nextTerrain,
        previousBuilding,
        nextBuilding,
        ownerChanged: false,
        ownerWallet: "",
        lastUpdatedTick: this.tick
      };

      const batchHash = crypto
        .createHash("sha256")
        .update(`${this.worldId}:${this.tick}:natural:${tile.q}:${tile.r}:${forestDelta}:${carbonDelta}`)
        .digest("hex");

      commits.push({
        worldId: this.worldId,
        commitId: uuid(),
        tick: this.tick,
        intentId: `natural-recovery:${tile.q}:${tile.r}:${this.tick}`,
        accepted: true,
        reason: "Natural recovery advanced.",
        globalForestDelta: forestDelta,
        globalCarbonDelta: carbonDelta,
        batchHash: `0x${batchHash}`,
        tileDeltas: [tileDelta],
        playerDelta: {
          walletAddress: "",
          ownedTilesDelta: 0,
          sustainabilityScoreDelta: 0,
          actionsTakenDelta: 0,
          actionsRemainingDelta: 0
        },
        committedAtMs: Date.now()
      });
    }

    return commits;
  }

  private ensurePlayer(walletAddress: string): void {
    if (this.players.has(walletAddress)) {
      return;
    }

    this.players.set(walletAddress, {
      walletAddress,
      ownedTilesCount: 0,
      sustainabilityScore: 0,
      actionsTaken: 0,
      actionsRemaining: ACTIONS_PER_CYCLE,
      lastClientSeq: 0
    });
  }

  private initializeTiles(): void {
    const height = MAP_ROWS.length;

    for (let row = 0; row < MAP_ROWS.length; row += 1) {
      const line = MAP_ROWS[row];
      const y = height - 1 - row;

      for (let x = 0; x < line.length; x += 1) {
        const symbol = line[x];
        const tileType = TERRAIN_BY_SYMBOL[symbol] ?? "Plains";
        this.tiles.set(tileKey(x, y), {
          q: x,
          r: y,
          baseType: tileType,
          currentState: tileType,
          ownerWallet: null,
          buildingType: "None",
          fertilityBp: 5000,
          pollutionBp: 0,
          biodiversityBp: tileType === "Forest" ? 8000 : 3000,
          lastUpdatedTick: 0
        });
      }
    }
  }

  private recalculateGlobalCounters(): void {
    this.forestTotal = 0;
    this.carbonTotal = 0;

    for (const tile of this.tiles.values()) {
      if (tile.currentState === "Forest") {
        this.forestTotal += 1;
      }

      this.carbonTotal += CARBON_VALUE[tile.currentState] + BUILDING_CARBON[tile.buildingType];
    }
  }
}

function tileKey(q: number, r: number): string {
  return `${q}:${r}`;
}

function transition(actionType: ActionType, tile: TileState): { nextTerrain: TileType; nextBuilding: BuildingType; ownerChanged: boolean } {
  let nextTerrain = tile.currentState;
  let nextBuilding = tile.buildingType;
  let ownerChanged = false;

  switch (actionType) {
    case "Claim":
      ownerChanged = true;
      break;
    case "BuildSettlement":
      nextBuilding = "Settlement";
      break;
    case "BuildIndustry":
      nextBuilding = "Industry";
      break;
    case "HarvestForest":
      nextTerrain = "DeforestedForest";
      break;
    case "Reforest":
      nextTerrain = "Forest";
      break;
    case "Farm":
      nextTerrain = "Farmland";
      break;
    case "Irrigate":
      nextTerrain = "Plains";
      break;
    case "Mine":
      nextTerrain = "Barren";
      break;
    case "Restore":
      nextTerrain = "Plains";
      break;
    case "EndTurn":
      break;
    default:
      if (!ACTION_INDEX.has(actionType)) {
        throw new Error(`Unsupported action type: ${actionType}`);
      }
  }

  return {
    nextTerrain,
    nextBuilding,
    ownerChanged
  };
}
