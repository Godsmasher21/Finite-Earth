using UnityEngine;

public sealed class UnityWorldAdapter : IResolverWorldQuery, IResolverWorldMutation
{
    private readonly HexWorldGeneratorTilemap worldGenerator;
    private readonly OwnershipOverlayPointTop ownership;

    public UnityWorldAdapter(HexWorldGeneratorTilemap worldGenerator, OwnershipOverlayPointTop ownership)
    {
        this.worldGenerator = worldGenerator;
        this.ownership = ownership;
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

    public bool HasAdjacentTerrainType(HexCoord coord, TileType requiredType)
    {
        return worldGenerator != null && worldGenerator.HasAdjacentTerrainType(coord.ToVector3Int(), requiredType);
    }

    public int GetOwnedCount(string walletAddress)
    {
        return ownership != null ? ownership.GetOwnedCount() : 0;
    }

    public int CountTilesOfType(TileType type)
    {
        return worldGenerator != null ? worldGenerator.CountTilesOfType(type) : 0;
    }

    public int CalculateCarbonScore()
    {
        return worldGenerator != null ? worldGenerator.CalculateCarbonScore() : 0;
    }

    public void SetOwned(HexCoord coord, string walletAddress, bool isOwned)
    {
        if (ownership == null)
        {
            return;
        }

        ownership.SetOwned(coord.ToVector3Int(), isOwned);
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
