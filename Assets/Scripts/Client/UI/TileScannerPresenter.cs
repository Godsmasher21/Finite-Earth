using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TileScannerPresenter : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text terrainText;
    [SerializeField] private TMP_Text ownerText;
    [SerializeField] private TMP_Text buildingText;
    [SerializeField] private TMP_Text influenceText;
    [SerializeField] private TMP_Text yieldText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text coordText;
    [SerializeField] private Image terrainIcon;
    [SerializeField] private Image buildingIcon;

    public void Initialize(
        TMP_Text title,
        TMP_Text terrain,
        TMP_Text owner,
        TMP_Text building,
        TMP_Text influence,
        TMP_Text yield,
        TMP_Text status,
        TMP_Text coord,
        Image terrainImg,
        Image buildingImg)
    {
        titleText = title;
        terrainText = terrain;
        ownerText = owner;
        buildingText = building;
        influenceText = influence;
        yieldText = yield;
        statusText = status;
        coordText = coord;
        terrainIcon = terrainImg;
        buildingIcon = buildingImg;
    }

    public void Refresh(
        bool hasSelection,
        HexCoord coord,
        HexWorldGeneratorTilemap world,
        OwnershipOverlayPointTop ownership,
        FiniteEarthGameOrchestrator orchestrator)
    {
        if (orchestrator != null && orchestrator.TryGetSelectedArmy(out ArmyUnit selectedArmy, out float cooldownRemaining))
        {
            RefreshArmyScanner(selectedArmy, cooldownRemaining, world, ownership, orchestrator);
            return;
        }

        if (titleText != null)
        {
            titleText.text = ":: HEX SCAN";
        }

        if (!hasSelection || world == null || ownership == null)
        {
            SetEmpty();
            return;
        }

        world.TryGetTileType(coord.ToVector3Int(), out TileType terrain);
        world.TryGetBuildingType(coord.ToVector3Int(), out BuildingType building);

        bool isOwned = ownership.IsOwned(coord.ToVector3Int());
        string resolvedOwnerLabel = isOwned ? "YOU" : "NEUTRAL";
        if (orchestrator != null && orchestrator.TryGetOwnerLabelAt(coord, out string ownerLabel))
        {
            resolvedOwnerLabel = ownerLabel;
        }

        int capture = orchestrator != null ? orchestrator.GetCapturePressure(coord) : 0;

        if (terrainText != null)
        {
            terrainText.text = $"TERRAIN: {terrain.GetDisplayName().ToUpperInvariant()}";
        }

        if (ownerText != null)
        {
            ownerText.text = $"OWNER: {resolvedOwnerLabel}";
        }

        if (buildingText != null)
        {
            string buildingLabel = building == BuildingType.None ? "NONE" : building.GetDisplayName().ToUpperInvariant();
            buildingText.text = $"BUILDING: {buildingLabel}";
        }

        if (influenceText != null)
        {
            influenceText.text = capture > 0 ? $"INFLUENCE: +{capture} PRESSURE" : "INFLUENCE: STABLE";
        }

        if (yieldText != null)
        {
            yieldText.text = $"YIELD: {BuildYieldLine(coord, terrain, building, world, orchestrator)}";
        }

        if (statusText != null)
        {
            statusText.text = BuildStatusTags(terrain, building, isOwned, capture);
        }

        if (coordText != null)
        {
            coordText.text = $"Q{coord.q}  R{coord.r}";
        }

        if (terrainIcon != null)
        {
            terrainIcon.color = ResolveTerrainColor(terrain);
        }

        if (buildingIcon != null)
        {
            buildingIcon.color = building == BuildingType.None ? new Color(0.2f, 0.25f, 0.28f, 1f) : new Color(0.30f, 0.50f, 0.70f, 1f);
        }
    }

    private void RefreshArmyScanner(
        ArmyUnit unit,
        float cooldownRemaining,
        HexWorldGeneratorTilemap world,
        OwnershipOverlayPointTop ownership,
        FiniteEarthGameOrchestrator orchestrator)
    {
        if (titleText != null)
        {
            titleText.text = ":: ARMY SCAN";
        }

        if (world != null)
        {
            world.TryGetTileType(unit.coord.ToVector3Int(), out TileType terrain);
            world.TryGetBuildingType(unit.coord.ToVector3Int(), out BuildingType building);

            if (terrainText != null)
            {
                terrainText.text = $"UNIT: ARMY DOT / {terrain.GetDisplayName().ToUpperInvariant()}";
            }

            if (buildingText != null)
            {
                string buildingLabel = building == BuildingType.None ? "NONE" : building.GetDisplayName().ToUpperInvariant();
                buildingText.text = $"POST: {buildingLabel}";
            }

            if (terrainIcon != null)
            {
                terrainIcon.color = new Color(0.20f, 0.55f, 1f, 1f);
            }

            if (buildingIcon != null)
            {
                buildingIcon.color = building == BuildingType.Barracks
                    ? new Color(0.30f, 0.50f, 0.70f, 1f)
                    : new Color(0.2f, 0.25f, 0.28f, 1f);
            }
        }

        if (ownerText != null)
        {
            string ownerLabel = orchestrator != null
                ? orchestrator.DescribeOwnerLabel(unit.ownerWallet)
                : "HOSTILE";
            ownerText.text = $"OWNER: {ownerLabel}";
        }

        if (influenceText != null)
        {
            influenceText.text = $"STRENGTH: {Mathf.Max(1, unit.strength)}/{Mathf.Max(1, orchestrator != null ? orchestrator.MaxArmyStrength : 1)}";
        }

        if (yieldText != null)
        {
            string status = orchestrator != null ? orchestrator.DescribeSelectedArmyStatus(cooldownRemaining) : "HOLDING";
            yieldText.text = $"ORDERS: {status}";
        }

        if (statusText != null)
        {
            string readyTag = cooldownRemaining <= 0.01f ? "[READY]" : "[COOLDOWN]";
            string moveTag = orchestrator != null && orchestrator.IsArmyMoveArmed ? "[MOVE]" : "[HOLD]";
            statusText.text = $"STATUS: [SELECTED] {readyTag} {moveTag}";
        }

        if (coordText != null)
        {
            coordText.text = $"Q{unit.coord.q}  R{unit.coord.r}";
        }
    }

    private void SetEmpty()
    {
        if (terrainText != null) terrainText.text = "TERRAIN: --";
        if (ownerText != null) ownerText.text = "OWNER: --";
        if (buildingText != null) buildingText.text = "BUILDING: --";
        if (influenceText != null) influenceText.text = "INFLUENCE: --";
        if (yieldText != null) yieldText.text = "YIELD: --";
        if (statusText != null) statusText.text = "STATUS: [ SELECT A TILE ]";
        if (coordText != null) coordText.text = "--";
    }

    private static string BuildYieldLine(HexCoord coord, TileType terrain, BuildingType building, HexWorldGeneratorTilemap world, FiniteEarthGameOrchestrator orchestrator)
    {
        float food = 0f;
        float minerals = 0f;

        if (terrain == TileType.Farmland)
        {
            food = orchestrator != null ? orchestrator.FarmFoodPerCycle : 0.125f;
        }

        if (building == BuildingType.Industry)
        {
            minerals = orchestrator != null ? orchestrator.GetIndustryYieldPerCycle(coord) : 0.05f;
        }

        if (food <= 0.001f && minerals <= 0.001f)
        {
            return "NONE";
        }

        string parts = string.Empty;
        if (food > 0)
        {
            parts = $"FOOD +{FormatYieldValue(food)}";
        }

        if (minerals > 0)
        {
            parts = string.IsNullOrEmpty(parts) ? $"MINERALS +{FormatYieldValue(minerals)}" : $"{parts}, MINERALS +{FormatYieldValue(minerals)}";
        }

        return parts;
    }

    private static string FormatYieldValue(float value)
    {
        float rounded = Mathf.Round(value * 100f) / 100f;
        return Mathf.Approximately(rounded, Mathf.Round(rounded))
            ? Mathf.RoundToInt(rounded).ToString()
            : rounded.ToString("0.##");
    }

    private static string BuildStatusTags(TileType terrain, BuildingType building, bool isOwned, int capture)
    {
        List<string> tags = new List<string>(4);
        tags.Add(isOwned ? "Owned" : "Neutral");
        if (capture > 0 && !isOwned)
        {
            tags.Add("Contested");
        }

        if (building == BuildingType.RecoveryProject)
        {
            tags.Add("Restoring");
        }

        if (terrain == TileType.Barren || terrain == TileType.DeforestedForest || terrain == TileType.Desert)
        {
            tags.Add("Degraded");
        }

        if (terrain == TileType.Forest)
        {
            tags.Add("Healthy");
        }

        for (int i = 0; i < tags.Count; i++)
        {
            tags[i] = $"[{tags[i].ToUpperInvariant()}]";
        }

        return $"STATUS: {string.Join(" ", tags)}";
    }

    private static Color ResolveTerrainColor(TileType terrain)
    {
        switch (terrain)
        {
            case TileType.Forest:
                return new Color(0.17f, 0.55f, 0.28f, 1f);
            case TileType.Plains:
                return new Color(0.37f, 0.74f, 0.42f, 1f);
            case TileType.Mountain:
                return new Color(0.45f, 0.47f, 0.50f, 1f);
            case TileType.Desert:
                return new Color(0.70f, 0.58f, 0.26f, 1f);
            case TileType.Barren:
                return new Color(0.45f, 0.24f, 0.18f, 1f);
            case TileType.Water:
                return new Color(0.25f, 0.50f, 0.65f, 1f);
            case TileType.Ice:
                return new Color(0.73f, 0.88f, 0.94f, 1f);
            default:
                return new Color(0.32f, 0.36f, 0.38f, 1f);
        }
    }
}
