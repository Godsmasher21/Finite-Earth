using System;

public enum TechNode
{
    BasicForestry = 0,
    RenewableEnergy = 1,
    CarbonCapture = 2
}

public readonly struct TechDefinition
{
    public readonly TechNode node;
    public readonly string label;
    public readonly string description;
    public readonly int cost;
    public readonly TechNode? prerequisite;

    public TechDefinition(TechNode node, string label, string description, int cost, TechNode? prerequisite)
    {
        this.node = node;
        this.label = label;
        this.description = description;
        this.cost = cost;
        this.prerequisite = prerequisite;
    }
}

public static class FiniteEarthTechTree
{
    public static readonly TechDefinition[] Nodes =
    {
        new TechDefinition(
            TechNode.BasicForestry,
            "Basic Forestry",
            "+10% wood gain on harvest",
            3,
            null),
        new TechDefinition(
            TechNode.RenewableEnergy,
            "Renewable Energy",
            "-25% industry carbon",
            5,
            TechNode.BasicForestry),
        new TechDefinition(
            TechNode.CarbonCapture,
            "Carbon Capture",
            "-1 global carbon each cycle",
            7,
            TechNode.RenewableEnergy)
    };

    public static bool IsUnlocked(PlayerState player, TechNode node)
    {
        if (player == null)
        {
            return false;
        }

        switch (node)
        {
            case TechNode.BasicForestry:
                return player.techBasicForestry;
            case TechNode.RenewableEnergy:
                return player.techRenewableEnergy;
            case TechNode.CarbonCapture:
                return player.techCarbonCapture;
            default:
                return false;
        }
    }

    public static void Unlock(PlayerState player, TechNode node)
    {
        if (player == null)
        {
            return;
        }

        switch (node)
        {
            case TechNode.BasicForestry:
                player.techBasicForestry = true;
                break;
            case TechNode.RenewableEnergy:
                player.techRenewableEnergy = true;
                break;
            case TechNode.CarbonCapture:
                player.techCarbonCapture = true;
                break;
        }
    }

    public static bool HasPrerequisite(PlayerState player, TechDefinition definition)
    {
        if (definition.prerequisite == null)
        {
            return true;
        }

        return IsUnlocked(player, definition.prerequisite.Value);
    }
}
