using System;
using System.Collections.Generic;

[Serializable]
public sealed class WorldState
{
    public string worldId = "finite-earth-alpha";
    public int tick;
    public int cycleSeconds = 30;
    public int actionsPerCycle = 9999;
    public int settlementRadius = 3;
    public bool requireAdjacency = true;
    public bool requireSettlementRadius = true;
    public int globalForestToken;
    public int globalCarbonToken;
    public int rngSeed;
    public int initialForest;
    public int carbonCap;
    public int ecosystemScore;
    public IResolverWorldQuery query;
}

[Serializable]
public sealed class PlayerState
{
    public string walletAddress = string.Empty;
    public int ownedTilesCount;
    public int sustainabilityScore;
    public int actionsTaken;
    public int actionsRemaining = 9999;
    public long lastClientSeq;
    public FiniteEarthResourcePool resources;
    public int researchPoints;
    public bool techBasicForestry;
    public bool techRenewableEnergy;
    public bool techCarbonCapture;
    public int ecoActions;
    public int industrialActions;
    public int agricultureActions;
    public string reputationLabel = "Balanced";
}

[Serializable]
public struct ActionIntent
{
    public string intentId;
    public string worldId;
    public string walletAddress;
    public long clientSeq;
    public FiniteEarthActionType actionType;
    public int q;
    public int r;
    public BuildingType buildingType;
    public long clientIssuedAtMs;

    public ActionIntent(
        string intentId,
        string worldId,
        string walletAddress,
        long clientSeq,
        FiniteEarthActionType actionType,
        int q,
        int r,
        BuildingType buildingType,
        long clientIssuedAtMs)
    {
        this.intentId = intentId;
        this.worldId = worldId;
        this.walletAddress = walletAddress;
        this.clientSeq = clientSeq;
        this.actionType = actionType;
        this.q = q;
        this.r = r;
        this.buildingType = buildingType;
        this.clientIssuedAtMs = clientIssuedAtMs;
    }

    public HexCoord Coord => new HexCoord(q, r);
}

[Serializable]
public readonly struct ActionResolution
{
    public readonly bool accepted;
    public readonly string reason;
    public readonly TileDelta[] tileDeltas;
    public readonly PlayerDelta playerDelta;
    public readonly GlobalDelta globalDelta;

    public ActionResolution(bool accepted, string reason, TileDelta[] tileDeltas, PlayerDelta playerDelta, GlobalDelta globalDelta)
    {
        this.accepted = accepted;
        this.reason = reason;
        this.tileDeltas = tileDeltas ?? Array.Empty<TileDelta>();
        this.playerDelta = playerDelta;
        this.globalDelta = globalDelta;
    }
}

[Serializable]
public struct TileDelta
{
    public int q;
    public int r;
    public TileType previousTerrain;
    public TileType nextTerrain;
    public BuildingType previousBuilding;
    public BuildingType nextBuilding;
    public bool ownerChanged;
    public string ownerWallet;
    public int lastUpdatedTick;

    public TileDelta(
        int q,
        int r,
        TileType previousTerrain,
        TileType nextTerrain,
        BuildingType previousBuilding,
        BuildingType nextBuilding,
        bool ownerChanged,
        string ownerWallet,
        int lastUpdatedTick)
    {
        this.q = q;
        this.r = r;
        this.previousTerrain = previousTerrain;
        this.nextTerrain = nextTerrain;
        this.previousBuilding = previousBuilding;
        this.nextBuilding = nextBuilding;
        this.ownerChanged = ownerChanged;
        this.ownerWallet = ownerWallet;
        this.lastUpdatedTick = lastUpdatedTick;
    }

    public HexCoord Coord => new HexCoord(q, r);
}

[Serializable]
public struct PlayerDelta
{
    public string walletAddress;
    public int ownedTilesDelta;
    public int sustainabilityScoreDelta;
    public int actionsTakenDelta;
    public int actionsRemainingDelta;
    public FiniteEarthResourcePool resourceDelta;

    public PlayerDelta(
        string walletAddress,
        int ownedTilesDelta,
        int sustainabilityScoreDelta,
        int actionsTakenDelta,
        int actionsRemainingDelta,
        FiniteEarthResourcePool resourceDelta)
    {
        this.walletAddress = walletAddress;
        this.ownedTilesDelta = ownedTilesDelta;
        this.sustainabilityScoreDelta = sustainabilityScoreDelta;
        this.actionsTakenDelta = actionsTakenDelta;
        this.actionsRemainingDelta = actionsRemainingDelta;
        this.resourceDelta = resourceDelta;
    }
}

[Serializable]
public struct GlobalDelta
{
    public int forestDelta;
    public int carbonDelta;
    public int actionCount;

    public GlobalDelta(int forestDelta, int carbonDelta, int actionCount)
    {
        this.forestDelta = forestDelta;
        this.carbonDelta = carbonDelta;
        this.actionCount = actionCount;
    }
}

public static class ActionOrdering
{
    public static int Compare(ActionIntent left, ActionIntent right)
    {
        int byClientIssuedAt = left.clientIssuedAtMs.CompareTo(right.clientIssuedAtMs);
        if (byClientIssuedAt != 0)
        {
            return byClientIssuedAt;
        }

        int byWallet = string.CompareOrdinal(left.walletAddress, right.walletAddress);
        if (byWallet != 0)
        {
            return byWallet;
        }

        return string.CompareOrdinal(left.intentId, right.intentId);
    }
}

public sealed class ActionIntentComparer : IComparer<ActionIntent>
{
    public static readonly ActionIntentComparer Instance = new ActionIntentComparer();

    public int Compare(ActionIntent x, ActionIntent y)
    {
        return ActionOrdering.Compare(x, y);
    }
}
