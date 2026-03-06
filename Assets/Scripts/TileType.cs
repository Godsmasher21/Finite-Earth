using System;

public enum TileType
{
    Forest = 0,
    Plains = 1,
    Mountain = 2,
    Water = 3,
    Desert = 4,
    Barren = 5,
    DeforestedForest = 6,
    Farmland = 7,
    Settlement = 8,
    Factory = 9,
    RecoveryBuilding = 10
}

public enum BuildingType
{
    None = 0,
    Settlement = 1,
    Industry = 2,
    RecoveryProject = 3
}

public enum FiniteEarthActionType
{
    Claim = 0,
    BuildSettlement = 1,
    BuildIndustry = 2,
    HarvestForest = 3,
    Reforest = 4,
    Farm = 5,
    Irrigate = 6,
    Mine = 7,
    Restore = 8,
    EndTurn = 9
}

[Serializable]
public struct FiniteEarthResourcePool
{
    public int wood;
    public int food;
    public int minerals;

    public bool CanAfford(FiniteEarthResourcePool cost)
    {
        return wood >= cost.wood
            && food >= cost.food
            && minerals >= cost.minerals;
    }

    public void Add(FiniteEarthResourcePool delta)
    {
        wood += delta.wood;
        food += delta.food;
        minerals += delta.minerals;
    }

    public void Spend(FiniteEarthResourcePool cost)
    {
        wood -= cost.wood;
        food -= cost.food;
        minerals -= cost.minerals;
    }

    public bool IsZero()
    {
        return wood == 0 && food == 0 && minerals == 0;
    }

    public string ToShortString()
    {
        return $"Wood {wood}   Food {food}   Minerals {minerals}";
    }
}

public static class TileTypeUtility
{
    public static bool IsTerrain(this TileType type)
    {
        return type <= TileType.Farmland;
    }

    public static bool IsClaimable(this TileType type)
    {
        return type != TileType.Water;
    }

    public static string GetDisplayName(this TileType type)
    {
        switch (type)
        {
            case TileType.Forest:
                return "Forest";
            case TileType.Plains:
                return "Plains";
            case TileType.Mountain:
                return "Mountain";
            case TileType.Water:
                return "Water";
            case TileType.Desert:
                return "Desert";
            case TileType.Barren:
                return "Barren";
            case TileType.DeforestedForest:
                return "Deforested";
            case TileType.Farmland:
                return "Farmland";
            case TileType.Settlement:
                return "Settlement";
            case TileType.Factory:
                return "Industry";
            case TileType.RecoveryBuilding:
                return "Recovery";
            default:
                return type.ToString();
        }
    }

    public static string GetDisplayName(this BuildingType type)
    {
        switch (type)
        {
            case BuildingType.None:
                return "None";
            case BuildingType.Settlement:
                return "Settlement";
            case BuildingType.Industry:
                return "Industry";
            case BuildingType.RecoveryProject:
                return "Recovery Project";
            default:
                return type.ToString();
        }
    }

    public static TileType ToTileType(this BuildingType type)
    {
        switch (type)
        {
            case BuildingType.Settlement:
                return TileType.Settlement;
            case BuildingType.Industry:
                return TileType.Factory;
            case BuildingType.RecoveryProject:
                return TileType.RecoveryBuilding;
            default:
                return TileType.Plains;
        }
    }

    public static int GetCarbonValue(this TileType type)
    {
        switch (type)
        {
            case TileType.Forest:
                return 4;
            case TileType.Plains:
                return 1;
            case TileType.Water:
                return 1;
            case TileType.Farmland:
                return 0;
            case TileType.Mountain:
                return 0;
            case TileType.Desert:
                return -1;
            case TileType.DeforestedForest:
                return -2;
            case TileType.Barren:
                return -3;
            default:
                return 0;
        }
    }

    public static int GetCarbonModifier(this BuildingType type)
    {
        switch (type)
        {
            case BuildingType.Settlement:
                return -1;
            case BuildingType.Industry:
                return -3;
            case BuildingType.RecoveryProject:
                return 2;
            default:
                return 0;
        }
    }
}
