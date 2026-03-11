public static class TileStateMachine
{
    public static bool TryGetPostActionState(
        FiniteEarthActionType actionType,
        TileType currentTerrain,
        BuildingType currentBuilding,
        out TileType nextTerrain,
        out BuildingType nextBuilding)
    {
        nextTerrain = currentTerrain;
        nextBuilding = currentBuilding;

        switch (actionType)
        {
            case FiniteEarthActionType.BuildSettlement:
                nextBuilding = BuildingType.Settlement;
                return true;

            case FiniteEarthActionType.BuildBarracks:
                nextBuilding = BuildingType.Barracks;
                return true;

            case FiniteEarthActionType.BuildIndustry:
                nextBuilding = BuildingType.Industry;
                return true;

            case FiniteEarthActionType.HarvestForest:
                nextTerrain = TileType.DeforestedForest;
                return true;

            case FiniteEarthActionType.Reforest:
                nextTerrain = TileType.Forest;
                return true;

            case FiniteEarthActionType.Farm:
                nextTerrain = TileType.Farmland;
                return true;

            case FiniteEarthActionType.Irrigate:
                nextTerrain = TileType.Plains;
                nextBuilding = BuildingType.RecoveryProject;
                return true;

            case FiniteEarthActionType.Mine:
                if (currentTerrain == TileType.Mountain)
                {
                    nextTerrain = TileType.Barren;
                }
                else
                {
                    nextTerrain = currentTerrain;
                }
                return true;

            case FiniteEarthActionType.Restore:
                nextTerrain = TileType.Plains;
                nextBuilding = BuildingType.RecoveryProject;
                return true;

            case FiniteEarthActionType.SpawnArmy:
                return true;

            case FiniteEarthActionType.Claim:
            case FiniteEarthActionType.EndTurn:
                return true;

            default:
                return false;
        }
    }
}
