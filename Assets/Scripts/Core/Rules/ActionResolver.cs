using System;
using UnityEngine;

public sealed class ActionResolver : IActionResolver
{
    private const int IrrigationWaterRadius = 10;
    private const float MiningPenaltyBase = 1f;
    private const float MiningPenaltyGrowth = 1.5f;

    public ActionResolution Resolve(ActionIntent intent, WorldState state, PlayerState player, int tick)
    {
        if (state == null || player == null || state.query == null)
        {
            return Reject("Resolver world state is not initialized.");
        }

        if (!ActionCatalog.TryGet(intent.actionType, out ActionRuleSpec spec))
        {
            return Reject("Unsupported action.");
        }

        if (intent.actionType == FiniteEarthActionType.EndTurn)
        {
            return Reject("Manual end turn is disabled. Cycle resolution is timer-driven.");
        }

        if (!player.resources.CanAfford(spec.cost))
        {
            return Reject($"Not enough resources for {spec.label}.");
        }

        HexCoord coord = intent.Coord;
        if (!state.query.TryGetTileType(coord, out TileType terrain))
        {
            return Reject("Target tile is outside world bounds.");
        }

        state.query.TryGetBuildingType(coord, out BuildingType building);

        bool isOwned = state.query.IsOwned(coord, intent.walletAddress);
        bool hasAnyOwned = state.query.HasAnyOwnedTiles(intent.walletAddress);
        bool hasSettlement = state.query.HasAnySettlement();
        bool inSettlementRange = !state.requireSettlementRadius
            || !hasSettlement
            || state.query.IsWithinSettlementRadius(coord, state.settlementRadius);

        if (!ValidateAction(intent.actionType, terrain, building, isOwned, hasAnyOwned, inSettlementRange, state, intent.walletAddress, coord, out string blockedReason))
        {
            return Reject(blockedReason);
        }

        if (!TileStateMachine.TryGetPostActionState(intent.actionType, terrain, building, out TileType nextTerrain, out BuildingType nextBuilding))
        {
            return Reject("No transition registered for this action.");
        }

        bool ownerChanged = intent.actionType == FiniteEarthActionType.Claim;
        bool forceDelta = intent.actionType == FiniteEarthActionType.Mine;
        TileDelta[] tileDeltas = ownerChanged || nextTerrain != terrain || nextBuilding != building || forceDelta
            ? new[]
            {
                new TileDelta(
                    coord.q,
                    coord.r,
                    terrain,
                    nextTerrain,
                    building,
                    nextBuilding,
                    ownerChanged,
                    ownerChanged ? intent.walletAddress : string.Empty,
                    tick)
            }
            : Array.Empty<TileDelta>();

        FiniteEarthResourcePool resourceDelta = spec.reward;
        resourceDelta.wood -= spec.cost.wood;
        resourceDelta.food -= spec.cost.food;
        resourceDelta.minerals -= spec.cost.minerals;

        int beforeCarbon = terrain.GetCarbonValue() + building.GetCarbonModifier();
        int afterCarbon = nextTerrain.GetCarbonValue() + nextBuilding.GetCarbonModifier();
        int carbonDelta = afterCarbon - beforeCarbon;

        if (intent.actionType == FiniteEarthActionType.Mine)
        {
            int miningCount = state.query.GetMiningCount(coord);
            int extraCarbon = Mathf.RoundToInt(MiningPenaltyBase * Mathf.Pow(MiningPenaltyGrowth, Mathf.Max(0, miningCount)));
            carbonDelta += extraCarbon;
        }

        int forestDelta = (nextTerrain == TileType.Forest ? 1 : 0) - (terrain == TileType.Forest ? 1 : 0);
        int ownedTilesDelta = ownerChanged ? 1 : 0;
        int sustainabilityDelta = forestDelta - Math.Max(0, carbonDelta);

        PlayerDelta playerDelta = new PlayerDelta(
            intent.walletAddress,
            ownedTilesDelta,
            sustainabilityDelta,
            1,
            0,
            resourceDelta);

        GlobalDelta globalDelta = new GlobalDelta(
            forestDelta,
            carbonDelta,
            1);

        return new ActionResolution(true, "Accepted", tileDeltas, playerDelta, globalDelta);
    }

    private static bool ValidateAction(
        FiniteEarthActionType actionType,
        TileType terrain,
        BuildingType building,
        bool isOwned,
        bool hasAnyOwned,
        bool inSettlementRange,
        WorldState state,
        string walletAddress,
        HexCoord coord,
        out string reason)
    {
        reason = string.Empty;

        if (actionType == FiniteEarthActionType.Claim)
        {
            if (isOwned)
            {
                reason = "Tile already owned by this player.";
                return false;
            }

            if (!terrain.IsClaimable())
            {
                reason = "Tile is not claimable.";
                return false;
            }

            if (state.requireAdjacency && hasAnyOwned && !state.query.IsAdjacentToOwned(coord, walletAddress))
            {
                reason = "Claim must be adjacent to owned territory.";
                return false;
            }

            if (!inSettlementRange)
            {
                reason = "Tile is outside settlement influence.";
                return false;
            }

            return true;
        }

        if (!isOwned)
        {
            reason = "Tile must be owned before this action.";
            return false;
        }

        if (actionType != FiniteEarthActionType.Restore && !inSettlementRange)
        {
            reason = "Tile is outside settlement influence.";
            return false;
        }

        switch (actionType)
        {
            case FiniteEarthActionType.BuildSettlement:
                if (building != BuildingType.None) { reason = "Building already exists."; return false; }
                if (terrain != TileType.Plains) { reason = "Settlement requires plains."; return false; }
                return true;
            case FiniteEarthActionType.BuildBarracks:
                if (building != BuildingType.None) { reason = "Building already exists."; return false; }
                if (terrain != TileType.Plains) { reason = "Barracks requires plains."; return false; }
                return true;
            case FiniteEarthActionType.BuildIndustry:
                if (building != BuildingType.None) { reason = "Building already exists."; return false; }
                if (terrain != TileType.Barren && terrain != TileType.Plains && terrain != TileType.Mountain)
                {
                    reason = "Industry requires barren, plains, or mountain terrain.";
                    return false;
                }
                return true;
            case FiniteEarthActionType.HarvestForest:
                if (building != BuildingType.None) { reason = "Building blocks harvest."; return false; }
                if (terrain != TileType.Forest) { reason = "Harvest requires forest."; return false; }
                return true;
            case FiniteEarthActionType.Reforest:
                if (building != BuildingType.None) { reason = "Building blocks reforest."; return false; }
                if (terrain != TileType.Plains && terrain != TileType.Barren && terrain != TileType.DeforestedForest)
                {
                    reason = "Reforest requires plains, deforested, or barren.";
                    return false;
                }
                return true;
            case FiniteEarthActionType.Farm:
                if (building != BuildingType.None) { reason = "Building blocks farming."; return false; }
                if (terrain != TileType.Plains) { reason = "Farm requires plains."; return false; }
                return true;
            case FiniteEarthActionType.Irrigate:
                if (building != BuildingType.None) { reason = "Building blocks irrigation."; return false; }
                if (terrain != TileType.Desert) { reason = "Irrigation requires desert."; return false; }
                if (!state.query.HasTerrainTypeWithinRadius(coord, TileType.Water, IrrigationWaterRadius)) { reason = $"Irrigation requires water within {IrrigationWaterRadius} tiles."; return false; }
                return true;
            case FiniteEarthActionType.Mine:
                if (building != BuildingType.None) { reason = "Building blocks mining."; return false; }
                if (terrain != TileType.Mountain && terrain != TileType.Barren) { reason = "Mine requires mountain or barren terrain."; return false; }
                return true;
            case FiniteEarthActionType.Restore:
                if (building != BuildingType.None) { reason = "Building blocks restoration."; return false; }
                if (terrain != TileType.Barren) { reason = "Restore requires barren terrain."; return false; }
                return true;
            case FiniteEarthActionType.SpawnArmy:
                if (building != BuildingType.Barracks) { reason = "Army must train from a barracks."; return false; }
                return true;
            default:
                reason = "Unsupported action.";
                return false;
        }
    }

    private static ActionResolution Reject(string reason)
    {
        return new ActionResolution(
            false,
            reason,
            Array.Empty<TileDelta>(),
            new PlayerDelta(string.Empty, 0, 0, 0, 0, default),
            new GlobalDelta(0, 0, 0));
    }
}
