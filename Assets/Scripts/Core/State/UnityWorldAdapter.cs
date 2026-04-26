using UnityEngine;

public sealed class UnityWorldAdapter : IResolverWorldQuery, IResolverWorldMutation
{
    private readonly HexWorldGeneratorTilemap worldGenerator;
    private readonly OwnershipOverlayPointTop ownership;
    private string localWalletAddress = string.Empty;

    public UnityWorldAdapter(HexWorldGeneratorTilemap worldGenerator, OwnershipOverlayPointTop ownership)
    {
        this.worldGenerator = worldGenerator;
        this.ownership = ownership;
    }

    public void SetLocalWalletAddress(string walletAddress)
    {
        localWalletAddress = string.IsNullOrWhiteSpace(walletAddress)
            ? string.Empty
            : walletAddress.Trim().ToLowerInvariant();
    }

    public bool TryGetTileType(HexCoord coord, out TileType tileType)
    {
        if (worldGenerator == null)
        {
            tileType = default;
            return false;
        }

        return worldGenerator.TryGetTileType(coord.ToVector3Int(), out tileType);
    }

    public bool TryGetBuildingType(HexCoord coord, out BuildingType buildingType)
    {
        if (worldGenerator == null)
        {
            buildingType = default;
            return false;
        }

        return worldGenerator.TryGetBuildingType(coord.ToVector3Int(), out buildingType);
    }

    public bool IsOwned(HexCoord coord, string walletAddress)
    {
        return ownership != null && ownership.IsOwned(coord.ToVector3Int());
    }

    public bool HasAnyOwnedTiles(string walletAddress)
    {
        return ownership != null && ownership.HasAnyOwnedTiles();
    }

    public bool IsAdjacentToOwned(HexCoord coord, string walletAddress)
    {
        return ownership != null && ownership.IsAdjacentToOwned(coord.ToVector3Int());
    }

    public bool HasAnySettlement()
    {
        return worldGenerator != null && worldGenerator.HasAnySettlement();
    }

    public bool IsWithinSettlementRadius(HexCoord coord, int radius)
    {
        return worldGenerator != null && worldGenerator.IsWithinSettlementRadius(coord.ToVector3Int(), radius);
    }

    public bool IsOnSettlementRadiusRing(HexCoord coord, int radius)
    {
        return worldGenerator != null && worldGenerator.IsOnSettlementRadiusRing(coord.ToVector3Int(), radius);
    }

    public bool HasAdjacentTerrainType(HexCoord coord, TileType requiredType)
    {
        return worldGenerator != null && worldGenerator.HasAdjacentTerrainType(coord.ToVector3Int(), requiredType);
    }

    public bool HasTerrainTypeWithinRadius(HexCoord coord, TileType requiredType, int radius)
    {
        return worldGenerator != null && worldGenerator.HasTerrainTypeWithinRadius(coord.ToVector3Int(), requiredType, radius);
    }

    public int GetOwnedCount(string walletAddress)
    {
        return ownership != null ? ownership.GetOwnedCount() : 0;
    }

    public int CountOwnedBuildings(string walletAddress, BuildingType buildingType)
    {
        if (worldGenerator == null || ownership == null)
        {
            return 0;
        }

        int count = 0;
        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (!ownership.IsOwned(cell))
            {
                continue;
            }

            if (worldGenerator.TryGetBuildingType(cell, out BuildingType current) && current == buildingType)
            {
                count++;
            }
        }

        return count;
    }

    public int CountTilesOfType(TileType type)
    {
        return worldGenerator != null ? worldGenerator.CountTilesOfType(type) : 0;
    }

    public int CalculateCarbonScore()
    {
        return worldGenerator != null ? worldGenerator.CalculateCarbonScore() : 0;
    }

    public int GetMiningCount(HexCoord coord)
    {
        return worldGenerator != null ? worldGenerator.GetMiningCount(coord.ToVector3Int()) : 0;
    }

    public void SetOwned(HexCoord coord, string walletAddress, bool isOwned)
    {
        if (ownership == null)
        {
            return;
        }

        Vector3Int cell = coord.ToVector3Int();
        if (!isOwned)
        {
            ownership.SetOwned(cell, false);
            ownership.SetRivalOwned(cell, string.Empty);
            return;
        }

        string normalizedWallet = string.IsNullOrWhiteSpace(walletAddress)
            ? string.Empty
            : walletAddress.Trim().ToLowerInvariant();
        bool isLocalOwner = !string.IsNullOrWhiteSpace(localWalletAddress)
            && string.Equals(normalizedWallet, localWalletAddress, System.StringComparison.OrdinalIgnoreCase);

        ownership.SetOwned(cell, isLocalOwner);
        ownership.SetRivalOwned(cell, isLocalOwner ? string.Empty : normalizedWallet);
    }

    public bool TrySetTileType(HexCoord coord, TileType tileType)
    {
        return worldGenerator != null && worldGenerator.TrySetTileType(coord.ToVector3Int(), tileType);
    }

    public bool TrySetBuildingType(HexCoord coord, BuildingType buildingType)
    {
        return worldGenerator != null && worldGenerator.TrySetBuildingType(coord.ToVector3Int(), buildingType);
    }
}
