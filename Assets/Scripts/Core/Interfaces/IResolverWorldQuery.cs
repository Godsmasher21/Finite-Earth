public interface IResolverWorldQuery
{
    bool TryGetTileType(HexCoord coord, out TileType tileType);
    bool TryGetBuildingType(HexCoord coord, out BuildingType buildingType);
    bool IsOwned(HexCoord coord, string walletAddress);
    bool HasAnyOwnedTiles(string walletAddress);
    bool IsAdjacentToOwned(HexCoord coord, string walletAddress);
    bool HasAnySettlement();
    bool IsWithinSettlementRadius(HexCoord coord, int radius);
    bool IsOnSettlementRadiusRing(HexCoord coord, int radius);
    bool HasAdjacentTerrainType(HexCoord coord, TileType requiredType);
    bool HasTerrainTypeWithinRadius(HexCoord coord, TileType requiredType, int radius);
    int GetOwnedCount(string walletAddress);
    int CountOwnedBuildings(string walletAddress, BuildingType buildingType);
    int CountTilesOfType(TileType type);
    int CalculateCarbonScore();
    int GetMiningCount(HexCoord coord);
}

public interface IResolverWorldMutation
{
    void SetOwned(HexCoord coord, string walletAddress, bool isOwned);
    bool TrySetTileType(HexCoord coord, TileType tileType);
    bool TrySetBuildingType(HexCoord coord, BuildingType buildingType);
}
