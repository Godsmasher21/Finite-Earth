export type ActionType =
  | "Claim"
  | "BuildSettlement"
  | "BuildIndustry"
  | "HarvestForest"
  | "Reforest"
  | "Farm"
  | "Irrigate"
  | "Mine"
  | "Restore"
  | "EndTurn";

export type BuildingType = "None" | "Settlement" | "Industry" | "RecoveryProject";
export type TileType = "Forest" | "Plains" | "Mountain" | "Water" | "Desert" | "Barren" | "DeforestedForest" | "Farmland";

export type ActionIntent = {
  intentId: string;
  worldId: string;
  walletAddress: string;
  clientSeq: number;
  actionType: ActionType;
  q: number;
  r: number;
  buildingType: BuildingType;
  clientIssuedAtMs: number;
  submittedAtMs: number;
};

export type TileState = {
  q: number;
  r: number;
  baseType: TileType;
  currentState: TileType;
  ownerWallet: string | null;
  buildingType: BuildingType;
  fertilityBp: number;
  pollutionBp: number;
  biodiversityBp: number;
  lastUpdatedTick: number;
};

export type PlayerState = {
  walletAddress: string;
  ownedTilesCount: number;
  sustainabilityScore: number;
  actionsTaken: number;
  actionsRemaining: number;
  lastClientSeq: number;
};

export type TileDelta = {
  q: number;
  r: number;
  previousTerrain: TileType;
  nextTerrain: TileType;
  previousBuilding: BuildingType;
  nextBuilding: BuildingType;
  ownerChanged: boolean;
  ownerWallet: string;
  lastUpdatedTick: number;
};

export type PlayerDelta = {
  walletAddress: string;
  ownedTilesDelta: number;
  sustainabilityScoreDelta: number;
  actionsTakenDelta: number;
  actionsRemainingDelta: number;
};

export type GlobalDelta = {
  forestDelta: number;
  carbonDelta: number;
  actionCount: number;
};

export type ActionCommit = {
  worldId: string;
  commitId: string;
  tick: number;
  intentId: string;
  accepted: boolean;
  reason: string;
  globalForestDelta: number;
  globalCarbonDelta: number;
  batchHash: string;
  tileDeltas: TileDelta[];
  playerDelta: PlayerDelta;
  committedAtMs: number;
};
