using System.Collections.Generic;
using UnityEngine;

public readonly struct ActionRuleSpec
{
    public readonly FiniteEarthActionType actionType;
    public readonly string label;
    public readonly FiniteEarthResourcePool cost;
    public readonly FiniteEarthResourcePool reward;
    public readonly Color accent;

    public ActionRuleSpec(
        FiniteEarthActionType actionType,
        string label,
        FiniteEarthResourcePool cost,
        FiniteEarthResourcePool reward,
        Color accent)
    {
        this.actionType = actionType;
        this.label = label;
        this.cost = cost;
        this.reward = reward;
        this.accent = accent;
    }
}

public static class ActionCatalog
{
    private static readonly Dictionary<FiniteEarthActionType, ActionRuleSpec> Specs = new Dictionary<FiniteEarthActionType, ActionRuleSpec>
    {
        {
            FiniteEarthActionType.Claim,
            new ActionRuleSpec(
                FiniteEarthActionType.Claim,
                "Claim",
                default,
                default,
                new Color(0.26f, 0.50f, 0.39f, 1f))
        },
        {
            FiniteEarthActionType.BuildSettlement,
            new ActionRuleSpec(
                FiniteEarthActionType.BuildSettlement,
                "Settlement",
                new FiniteEarthResourcePool { wood = 2, food = 2 },
                default,
                new Color(0.59f, 0.42f, 0.28f, 1f))
        },
        {
            FiniteEarthActionType.BuildBarracks,
            new ActionRuleSpec(
                FiniteEarthActionType.BuildBarracks,
                "Barracks",
                new FiniteEarthResourcePool { wood = 2, food = 1, minerals = 1 },
                default,
                new Color(0.30f, 0.46f, 0.62f, 1f))
        },
        {
            FiniteEarthActionType.BuildIndustry,
            new ActionRuleSpec(
                FiniteEarthActionType.BuildIndustry,
                "Industry",
                new FiniteEarthResourcePool { wood = 1, minerals = 2 },
                default,
                new Color(0.42f, 0.44f, 0.49f, 1f))
        },
        {
            FiniteEarthActionType.RemoveBuilding,
            new ActionRuleSpec(
                FiniteEarthActionType.RemoveBuilding,
                "Remove",
                default,
                default,
                new Color(0.72f, 0.34f, 0.28f, 1f))
        },
        {
            FiniteEarthActionType.HarvestForest,
            new ActionRuleSpec(
                FiniteEarthActionType.HarvestForest,
                "Harvest",
                default,
                new FiniteEarthResourcePool { wood = 2 },
                new Color(0.52f, 0.23f, 0.18f, 1f))
        },
        {
            FiniteEarthActionType.Reforest,
            new ActionRuleSpec(
                FiniteEarthActionType.Reforest,
                "Reforest",
                new FiniteEarthResourcePool { wood = 1, food = 1 },
                default,
                new Color(0.17f, 0.45f, 0.25f, 1f))
        },
        {
            FiniteEarthActionType.Farm,
            new ActionRuleSpec(
                FiniteEarthActionType.Farm,
                "Farm",
                new FiniteEarthResourcePool { wood = 1 },
                new FiniteEarthResourcePool { food = 1 },
                new Color(0.66f, 0.56f, 0.24f, 1f))
        },
        {
            FiniteEarthActionType.Irrigate,
            new ActionRuleSpec(
                FiniteEarthActionType.Irrigate,
                "Irrigate",
                new FiniteEarthResourcePool { minerals = 1 },
                default,
                new Color(0.23f, 0.49f, 0.67f, 1f))
        },
        {
            FiniteEarthActionType.Mine,
            new ActionRuleSpec(
                FiniteEarthActionType.Mine,
                "Mine",
                default,
                new FiniteEarthResourcePool { minerals = 2 },
                new Color(0.50f, 0.52f, 0.56f, 1f))
        },
        {
            FiniteEarthActionType.Restore,
            new ActionRuleSpec(
                FiniteEarthActionType.Restore,
                "Restore",
                new FiniteEarthResourcePool { wood = 1, food = 1, minerals = 1 },
                default,
                new Color(0.36f, 0.60f, 0.52f, 1f))
        },
        {
            FiniteEarthActionType.SpawnArmy,
            new ActionRuleSpec(
                FiniteEarthActionType.SpawnArmy,
                "Train Army",
                new FiniteEarthResourcePool { wood = 2, food = 5 },
                default,
                new Color(0.24f, 0.38f, 0.68f, 1f))
        },
        {
            FiniteEarthActionType.EndTurn,
            new ActionRuleSpec(
                FiniteEarthActionType.EndTurn,
                "End Turn",
                default,
                default,
                new Color(0.20f, 0.24f, 0.30f, 1f))
        }
    };

    public static bool TryGet(FiniteEarthActionType actionType, out ActionRuleSpec spec)
    {
        return Specs.TryGetValue(actionType, out spec);
    }
}
