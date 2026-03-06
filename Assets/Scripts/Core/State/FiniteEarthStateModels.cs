using System;
using System.Collections.Generic;

[Serializable]
public sealed class WorldState
{
    public string worldId = "finite-earth-alpha";
    public int tick;
    public int cycleSeconds = 30;
    public int actionsPerCycle = 3;
    public int settlementRadius = 3;
    public bool requireAdjacency = true;
    public bool requireSettlementRadius = true;
    public int globalForestToken;
    public int globalCarbonToken;
    public int rngSeed;
    public IResolverWorldQuery query;
}

[Serializable]
public sealed class PlayerState
{
    public string walletAddress = string.Empty;
    public int ownedTilesCount;
    public int sustainabilityScore;
    public int actionsTaken;
    public int actionsRemaining = 3;
    public long lastClientSeq;
    public FiniteEarthResourcePool resources;
}

[Serializable]
public readonly struct ActionIntent
{
    public readonly string intentId;
    public readonly string worldId;
    public readonly string walletAddress;
    public readonly long clientSeq;
    public readonly FiniteEarthActionType actionType;
    public readonly int q;
    public readonly int r;
    public readonly BuildingType buildingType;
    public readonly long clientIssuedAtMs;

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
public readonly struct TileDelta
{
    public readonly int q;
    public readonly int r;
    public readonly TileType previousTerrain;
    public readonly TileType nextTerrain;
    public readonly BuildingType previousBuilding;
    public readonly BuildingType nextBuilding;
    public readonly bool ownerChanged;
    public readonly string ownerWallet;
    public readonly int lastUpdatedTick;

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
public readonly struct PlayerDelta
{
    public readonly string walletAddress;
    public readonly int ownedTilesDelta;
    public readonly int sustainabilityScoreDelta;
    public readonly int actionsTakenDelta;
    public readonly int actionsRemainingDelta;
    public readonly FiniteEarthResourcePool resourceDelta;

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
public readonly struct GlobalDelta
{
    public readonly int forestDelta;
    public readonly int carbonDelta;
    public readonly int actionCount;

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
