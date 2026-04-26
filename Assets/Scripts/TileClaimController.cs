using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class TileClaimController : MonoBehaviour
{
    [Header("Migration")]
    [SerializeField] private bool useLegacyLocalController = false;

    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Tilemap terrainTilemap;
    [SerializeField] private OwnershipOverlayPointTop ownership;
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;

    [Header("Starting Resources")]
    [SerializeField, Min(0)] private int startingWood = 6;
    [SerializeField, Min(0)] private int startingFood = 8;
    [SerializeField, Min(0)] private int startingMinerals = 4;

    [Header("Action Economy")]
    [SerializeField, Min(1)] private int maxActionsPerTurn = 9999;
    [SerializeField] private bool requireAdjacency = true;
    [SerializeField] private bool requireSettlementRadius = true;
    [SerializeField, Min(1)] private int settlementRadiusOverride = 3;

    [Header("Universal Clock")]
    [SerializeField, Min(1f)] private float secondsPerCycle = 10f;
    [SerializeField, Min(1)] private int minutesPerCycle = 60;
    [SerializeField, Range(0, 23)] private int clockStartHour = 6;
    [SerializeField, Range(0, 59)] private int clockStartMinute = 0;
    [SerializeField, Min(1)] private int recoveryProjectDurationCycles = 2;

    [Header("Camera Controls")]
    [SerializeField] private float keyboardPanSpeed = 8f;
    [SerializeField] private float dragPanScale = 0.01f;
    [SerializeField] private float zoomSpeed = 0.018f;
    [SerializeField] private float minZoom = 4.5f;
    [SerializeField] private float maxZoom = 15f;

    [Header("HUD Theme")]
    [SerializeField] private Color topBarColor = new Color(0.10f, 0.14f, 0.16f, 0.92f);
    [SerializeField] private Color sidePanelColor = new Color(0.08f, 0.11f, 0.12f, 0.92f);
    [SerializeField] private Color primaryTextColor = new Color(0.93f, 0.91f, 0.82f, 1f);
    [SerializeField] private Color secondaryTextColor = new Color(0.66f, 0.71f, 0.70f, 1f);
    [SerializeField] private Color disabledButtonColor = new Color(0.24f, 0.27f, 0.28f, 0.95f);

    [Header("Optional UI Icons")]
    [SerializeField] private Sprite claimIcon;
    [SerializeField] private Sprite settlementIcon;
    [SerializeField] private Sprite industryIcon;
    [SerializeField] private Sprite harvestIcon;
    [SerializeField] private Sprite reforestIcon;
    [SerializeField] private Sprite farmIcon;
    [SerializeField] private Sprite irrigateIcon;
    [SerializeField] private Sprite mineIcon;
    [SerializeField] private Sprite restoreIcon;
    [SerializeField] private Sprite endTurnIcon;

    private readonly Dictionary<FiniteEarthActionType, ActionSpec> actionLookup = new Dictionary<FiniteEarthActionType, ActionSpec>();
    private readonly Dictionary<FiniteEarthActionType, ActionButtonRefs> actionButtons = new Dictionary<FiniteEarthActionType, ActionButtonRefs>();

    private Canvas hudCanvas;
    private Font uiFont;
    private Text turnText;
    private Text resourceText;
    private Text metricText;
    private Text tileInfoText;
    private Text statusText;
    private Text legendText;

    private FiniteEarthResourcePool resources;
    private Vector3Int selectedCell;
    private bool hasSelection;
    private bool isInitialized;
    private bool isDragPanning;
    private Vector2 lastPointerPosition;
    private int cycleNumber;
    private int actionsRemaining;
    private int clockDay;
    private int clockMinutes;
    private float cycleTimer;
    private string currentStatusMessage = "Initializing Finite Earth...";
    private readonly Dictionary<long, int> recoveryProjectStartedCycle = new Dictionary<long, int>();

    private sealed class ActionSpec
    {
        public ActionSpec(FiniteEarthActionType type, string label, string description, FiniteEarthResourcePool cost, Color accent, Sprite icon)
        {
            Type = type;
            Label = label;
            Description = description;
            Cost = cost;
            Accent = accent;
            Icon = icon;
        }

        public FiniteEarthActionType Type { get; }
        public string Label { get; }
        public string Description { get; }
        public FiniteEarthResourcePool Cost { get; }
        public Color Accent { get; }
        public Sprite Icon { get; }
    }

    private sealed class ActionButtonRefs
    {
        public Button Button;
        public Image Background;
        public Text Label;
        public Image Icon;
    }

    private void Awake()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (worldGenerator == null)
        {
            worldGenerator = FindAnyObjectByType<HexWorldGeneratorTilemap>();
        }

        if (ownership == null)
        {
            ownership = FindAnyObjectByType<OwnershipOverlayPointTop>();
        }
    }

    private void Start()
    {
        if (!useLegacyLocalController)
        {
            BootstrapNewArchitecture();
            enabled = false;
            return;
        }

        if (!EnsureCoreReferences())
        {
            return;
        }

        worldGenerator.Generate();
        ownership.Initialize(worldGenerator);
        terrainTilemap = worldGenerator.TerrainTilemap;

        Vector3Int startCell = ownership.CreateStartingTerritory();

        resources = new FiniteEarthResourcePool
        {
            wood = startingWood,
            food = startingFood,
            minerals = startingMinerals
        };

        cycleNumber = 1;
        actionsRemaining = maxActionsPerTurn;
        clockDay = 1;
        clockMinutes = (clockStartHour * 60) + clockStartMinute;
        cycleTimer = GetSecondsPerCycle();

        BuildActionSpecs();
        EnsureEventSystem();
        BuildHud();
        worldGenerator.FrameCamera(mainCamera);
        SelectCell(startCell, false);
        SetStatus($"Loaded world {worldGenerator.WorldId}. Select a tile, then use the action panel while the universal clock advances automatically.");
        RefreshUi();
        isInitialized = true;
    }

    private void BootstrapNewArchitecture()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (worldGenerator == null)
        {
            worldGenerator = FindAnyObjectByType<HexWorldGeneratorTilemap>();
        }

        if (ownership == null)
        {
            ownership = FindAnyObjectByType<OwnershipOverlayPointTop>();
        }

        EnsureMainCameraConfigured();
        EnsureEventSystem();
        EnsureRuntimeCanvas();

        EnsureComponent<GameStateViewModel>();
        EnsureComponent<ActionInputController>();
        EnsureComponent<WorldCameraController>();
        EnsureComponent<ArmyOverlayPointTop>();
        EnsureComponent<ClimateEventOverlayPointTop>();
        EnsureComponent<SpacetimeClientManager>();
        EnsureComponent<ThirdwebBridge>();
        EnsureComponent<WalletSessionController>();
        EnsureComponent<FiniteEarthGameOrchestrator>();
        EnsureComponent<NetworkSessionCoordinator>();
        if (FindAnyObjectByType<CommandTableHudPresenter>() == null)
        {
            EnsureComponent<CommandTableHudPresenter>();
        }
        EnsureComponent<ActionPanelPresenter>();
        EnsureComponent<AsciiHudPresenter>();
        EnsureComponent<AsciiMarketDiplomacyPresenter>();
        EnsureComponentByName("AsciiLoginPopupPresenter");
        EnsureComponentByName("AsciiTutorialPopupPresenter");
    }

    private void EnsureMainCameraConfigured()
    {
        if (mainCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            mainCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        mainCamera.orthographic = true;

        Vector3 cameraPosition = mainCamera.transform.position;
        if (Mathf.Approximately(cameraPosition.z, 0f))
        {
            cameraPosition.z = -10f;
            mainCamera.transform.position = cameraPosition;
        }
    }

    private static Canvas EnsureRuntimeCanvas()
    {
        Canvas existing = FindAnyObjectByType<Canvas>();
        if (existing != null)
        {
            EnsureCanvasComponents(existing.gameObject);
            return existing;
        }

        GameObject canvasObject = new GameObject("FiniteEarthRuntimeCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;
        EnsureCanvasComponents(canvasObject);
        return canvas;
    }

    private static void EnsureCanvasComponents(GameObject canvasObject)
    {
        if (canvasObject == null)
        {
            return;
        }

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvasObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (canvasObject.GetComponent<GraphicRaycaster>() == null)
        {
            canvasObject.AddComponent<GraphicRaycaster>();
        }
    }

    private T EnsureComponent<T>() where T : Component
    {
        T existing = GetComponent<T>();
        return existing != null ? existing : gameObject.AddComponent<T>();
    }

    private Component EnsureComponentByName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        Type componentType = Type.GetType(typeName) ?? Type.GetType($"{typeName}, Assembly-CSharp");
        if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
        {
            return null;
        }

        Component existing = GetComponent(componentType);
        return existing != null ? existing : gameObject.AddComponent(componentType);
    }

    private void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        UpdateUniversalClock();
        HandleSelectionInput();
        HandleCameraInput();
    }

    private bool EnsureCoreReferences()
    {
        if (mainCamera == null || worldGenerator == null || ownership == null)
        {
            Debug.LogError("TileClaimController: missing camera, board, or ownership overlay reference.");
            return false;
        }

        return true;
    }

    private void BuildActionSpecs()
    {
        actionLookup.Clear();
        RegisterAction(new ActionSpec(FiniteEarthActionType.Claim, "Claim", "Expand to an adjacent non-water tile inside settlement range.", default, new Color(0.26f, 0.50f, 0.39f, 1f), claimIcon));
        RegisterAction(new ActionSpec(FiniteEarthActionType.BuildSettlement, "Settlement", "Build a settlement on owned plains to expand influence.", new FiniteEarthResourcePool { wood = 2, food = 2 }, new Color(0.59f, 0.42f, 0.28f, 1f), settlementIcon));
        RegisterAction(new ActionSpec(FiniteEarthActionType.BuildIndustry, "Industry", "Place industry on owned plains, barren, or mountains. Barren yields 0.75x, plains 1x, mountains 1.5x mineral output.", new FiniteEarthResourcePool { wood = 1, minerals = 2 }, new Color(0.42f, 0.44f, 0.49f, 1f), industryIcon));
        RegisterAction(new ActionSpec(FiniteEarthActionType.HarvestForest, "Harvest", "Cut forest for wood, leaving the tile deforested.", default, new Color(0.52f, 0.23f, 0.18f, 1f), harvestIcon));
        RegisterAction(new ActionSpec(FiniteEarthActionType.Reforest, "Reforest", "Turn plains or barren land back into forest.", new FiniteEarthResourcePool { wood = 1, food = 1 }, new Color(0.17f, 0.45f, 0.25f, 1f), reforestIcon));
        RegisterAction(new ActionSpec(FiniteEarthActionType.Farm, "Farm", "Convert owned plains into farmland for food production.", new FiniteEarthResourcePool { wood = 1 }, new Color(0.66f, 0.56f, 0.24f, 1f), farmIcon));
        RegisterAction(new ActionSpec(FiniteEarthActionType.Irrigate, "Irrigate", "Turn desert near water into plains.", new FiniteEarthResourcePool { minerals = 1 }, new Color(0.23f, 0.49f, 0.67f, 1f), irrigateIcon));
        RegisterAction(new ActionSpec(FiniteEarthActionType.Mine, "Mine", "Strip a mountain for minerals and leave barren land.", default, new Color(0.50f, 0.52f, 0.56f, 1f), mineIcon));
        RegisterAction(new ActionSpec(FiniteEarthActionType.Restore, "Restore", "Spend resources to rehabilitate damaged land into plains.", new FiniteEarthResourcePool { wood = 1, food = 1, minerals = 1 }, new Color(0.36f, 0.60f, 0.52f, 1f), restoreIcon));
    }

    private void RegisterAction(ActionSpec spec)
    {
        actionLookup[spec.Type] = spec;
    }

    private void HandleSelectionInput()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null)
        {
            return;
        }

        if (!mouse.leftButton.wasPressedThisFrame || IsPointerOverUi())
        {
            return;
        }

        if (worldGenerator.TryGetCellUnderScreenPoint(mainCamera, mouse.position.ReadValue(), out Vector3Int cell))
        {
            SelectCell(cell, true);
        }
    }

    private void HandleCameraInput()
    {
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;

        if (mainCamera == null)
        {
            return;
        }

        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;

            if (Mathf.Abs(scroll) > 0.01f)
            {
                mainCamera.orthographicSize = Mathf.Clamp(mainCamera.orthographicSize - scroll * zoomSpeed, minZoom, maxZoom);
            }

            bool dragPressed = mouse.rightButton.isPressed || mouse.middleButton.isPressed;

            if (dragPressed)
            {
                Vector2 currentPosition = mouse.position.ReadValue();

                if (!isDragPanning)
                {
                    isDragPanning = true;
                    lastPointerPosition = currentPosition;
                }
                else
                {
                    Vector2 delta = currentPosition - lastPointerPosition;
                    float zoomFactor = Mathf.Max(1f, mainCamera.orthographicSize * 0.5f);
                    mainCamera.transform.position += new Vector3(-delta.x, -delta.y, 0f) * (dragPanScale * zoomFactor);
                    lastPointerPosition = currentPosition;
                }
            }
            else
            {
                isDragPanning = false;
            }
        }

        if (keyboard == null)
        {
            return;
        }

        Vector2 move = Vector2.zero;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;

        if (move.sqrMagnitude > 0.001f)
        {
            move.Normalize();
            float speed = keyboardPanSpeed * Mathf.Max(1f, mainCamera.orthographicSize * 0.35f);
            mainCamera.transform.position += new Vector3(move.x, move.y, 0f) * (speed * Time.unscaledDeltaTime);
        }
    }

    private bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void SelectCell(Vector3Int cell, bool updateStatus)
    {
        selectedCell = cell;
        hasSelection = true;
        ownership.SetSelectedCell(cell);

        if (updateStatus)
        {
            SetStatus($"Selected {DescribeCellShort(cell)}.");
        }

        RefreshUi();
    }

    private void OnActionPressed(FiniteEarthActionType action)
    {
        if (!hasSelection)
        {
            SetStatus("Select a tile before using an action.");
            RefreshUi();
            return;
        }

        if (actionsRemaining <= 0)
        {
            SetStatus("No actions left in this cycle. Wait for the next clock tick.");
            RefreshUi();
            return;
        }

        if (!CanExecuteAction(action, selectedCell, out string blockedReason))
        {
            SetStatus(blockedReason);
            RefreshUi();
            return;
        }

        if (!ApplyAction(action, selectedCell, out FiniteEarthResourcePool reward, out string resultMessage))
        {
            SetStatus(resultMessage);
            RefreshUi();
            return;
        }

        FiniteEarthResourcePool cost = actionLookup[action].Cost;
        resources.Spend(cost);
        resources.Add(reward);
        actionsRemaining = Mathf.Max(0, actionsRemaining - 1);
        ownership.RefreshOverlay();
        SetStatus(resultMessage);
        RefreshUi();
    }

    private bool CanExecuteAction(FiniteEarthActionType action, Vector3Int cell, out string reason)
    {
        reason = string.Empty;

        if (!worldGenerator.TryGetTileType(cell, out TileType terrainType))
        {
            reason = "That tile is outside the generated world.";
            return false;
        }

        worldGenerator.TryGetBuildingType(cell, out BuildingType buildingType);

        if (!resources.CanAfford(actionLookup[action].Cost))
        {
            reason = $"Not enough resources for {actionLookup[action].Label}. Need {FormatCostLong(actionLookup[action].Cost)}.";
            return false;
        }

        if (action == FiniteEarthActionType.Claim)
        {
            if (ownership.IsOwned(cell))
            {
                reason = "That tile is already part of your territory.";
                return false;
            }

            if (!terrainType.IsClaimable())
            {
                reason = "Water tiles cannot be claimed.";
                return false;
            }

            if (requireAdjacency && ownership.HasAnyOwnedTiles() && !ownership.IsAdjacentToOwned(cell))
            {
                reason = "Claims must stay adjacent to owned territory.";
                return false;
            }

            if (requireSettlementRadius && worldGenerator.HasAnySettlement() && !worldGenerator.IsWithinSettlementRadius(cell, GetEffectiveSettlementRadius()))
            {
                reason = "That tile is outside settlement influence.";
                return false;
            }

            return true;
        }

        if (!ownership.IsOwned(cell))
        {
            reason = "You must own a tile before reshaping it.";
            return false;
        }

        if (action != FiniteEarthActionType.Restore
            && requireSettlementRadius
            && worldGenerator.HasAnySettlement()
            && !worldGenerator.IsWithinSettlementRadius(cell, GetEffectiveSettlementRadius()))
        {
            reason = "This tile is outside settlement influence.";
            return false;
        }

        switch (action)
        {
            case FiniteEarthActionType.BuildSettlement:
                if (buildingType != BuildingType.None) { reason = "Remove the current building first."; return false; }
                if (terrainType != TileType.Plains) { reason = "Settlements can only be built on plains."; return false; }
                return true;
            case FiniteEarthActionType.BuildIndustry:
                if (buildingType != BuildingType.None) { reason = "Industry needs an empty tile."; return false; }
                if (terrainType != TileType.Barren) { reason = "Industry can only be placed on barren land."; return false; }
                return true;
            case FiniteEarthActionType.HarvestForest:
                if (buildingType != BuildingType.None) { reason = "Buildings block harvesting."; return false; }
                if (terrainType != TileType.Forest) { reason = "Only forest tiles can be harvested."; return false; }
                return true;
            case FiniteEarthActionType.Reforest:
                if (buildingType != BuildingType.None) { reason = "Clear the building before reforesting."; return false; }
                if (terrainType != TileType.Plains && terrainType != TileType.Barren) { reason = "Only plains or barren tiles can be reforested."; return false; }
                return true;
            case FiniteEarthActionType.Farm:
                if (buildingType != BuildingType.None) { reason = "Buildings and farms cannot share a tile."; return false; }
                if (terrainType != TileType.Plains) { reason = "Farming starts on plains."; return false; }
                return true;
            case FiniteEarthActionType.Irrigate:
                if (buildingType != BuildingType.None) { reason = "Buildings block irrigation."; return false; }
                if (terrainType != TileType.Desert) { reason = "Only desert tiles need irrigation."; return false; }
                if (!worldGenerator.HasTerrainTypeWithinRadius(cell, TileType.Water, 10)) { reason = "Irrigation needs water within 10 tiles."; return false; }
                return true;
            case FiniteEarthActionType.Mine:
                if (buildingType != BuildingType.None) { reason = "Buildings block mining."; return false; }
                if (terrainType != TileType.Mountain && terrainType != TileType.Barren) { reason = "Only mountains or barren tiles can be mined."; return false; }
                return true;
            case FiniteEarthActionType.Restore:
                if (buildingType != BuildingType.None) { reason = "Buildings must be cleared before restoration."; return false; }
                if (terrainType != TileType.Barren) { reason = "Only barren land can be restored."; return false; }
                return true;
            default:
                reason = "Unsupported action.";
                return false;
        }
    }

    private bool ApplyAction(FiniteEarthActionType action, Vector3Int cell, out FiniteEarthResourcePool reward, out string resultMessage)
    {
        reward = default;
        resultMessage = "Action failed.";

        switch (action)
        {
            case FiniteEarthActionType.Claim:
                ownership.SetOwned(cell, true);
                resultMessage = $"Claimed {DescribeCellShort(cell)}.";
                return true;

            case FiniteEarthActionType.BuildSettlement:
                worldGenerator.TrySetBuildingType(cell, BuildingType.Settlement);
                resultMessage = $"Built a settlement at {cell.x}, {cell.y}.";
                return true;

            case FiniteEarthActionType.BuildIndustry:
                worldGenerator.TrySetBuildingType(cell, BuildingType.Industry);
                resultMessage = $"Established industry at {cell.x}, {cell.y}.";
                return true;

            case FiniteEarthActionType.HarvestForest:
                worldGenerator.TrySetTileType(cell, TileType.DeforestedForest);
                reward = new FiniteEarthResourcePool { wood = 2 };
                resultMessage = "Harvested forest for +2 wood.";
                return true;

            case FiniteEarthActionType.Reforest:
                worldGenerator.TrySetTileType(cell, TileType.Forest);
                resultMessage = "Reforested the selected tile.";
                return true;

            case FiniteEarthActionType.Farm:
                worldGenerator.TrySetTileType(cell, TileType.Farmland);
                reward = new FiniteEarthResourcePool { food = 1 };
                resultMessage = "Established farmland and gained +1 food.";
                return true;

            case FiniteEarthActionType.Irrigate:
                worldGenerator.TrySetTileType(cell, TileType.Plains);
                worldGenerator.TrySetBuildingType(cell, BuildingType.RecoveryProject);
                recoveryProjectStartedCycle[PackCoord(cell)] = cycleNumber;
                resultMessage = "Irrigated desert into plains.";
                return true;

            case FiniteEarthActionType.Mine:
                if (worldGenerator.TryGetTileType(cell, out TileType terrainType) && terrainType == TileType.Mountain)
                {
                    worldGenerator.TrySetTileType(cell, TileType.Barren);
                }
                reward = new FiniteEarthResourcePool { minerals = 2 };
                resultMessage = "Mined the mountain for +2 minerals.";
                return true;

            case FiniteEarthActionType.Restore:
                worldGenerator.TrySetTileType(cell, TileType.Plains);
                worldGenerator.TrySetBuildingType(cell, BuildingType.RecoveryProject);
                recoveryProjectStartedCycle[PackCoord(cell)] = cycleNumber;
                resultMessage = "Restored damaged land back into plains.";
                return true;

            default:
                resultMessage = "That action is not implemented.";
                return false;
        }
    }

    private void UpdateUniversalClock()
    {
        cycleTimer -= Time.unscaledDeltaTime;
        bool cycleAdvanced = false;

        while (cycleTimer <= 0f)
        {
            cycleTimer += GetSecondsPerCycle();
            AdvanceClockCycle();
            cycleAdvanced = true;
        }

        if (cycleAdvanced)
        {
            RefreshUi();
            return;
        }

        RefreshHeaderText();
    }

    private void AdvanceClockCycle()
    {
        FiniteEarthResourcePool income = CalculatePassiveIncome();
        resources.Add(income);
        cycleNumber++;
        actionsRemaining = maxActionsPerTurn;
        AdvanceClockTime();
        ApplyRecoveryProjectLifecycle();
        SetStatus($"Cycle {cycleNumber} started. Passive income: {FormatIncome(income)}.");
    }

    private void ApplyRecoveryProjectLifecycle()
    {
        if (worldGenerator == null || recoveryProjectStartedCycle.Count == 0 || recoveryProjectDurationCycles <= 0)
        {
            return;
        }

        var toRemove = new List<long>();
        foreach (KeyValuePair<long, int> pair in recoveryProjectStartedCycle)
        {
            int q = (int)(pair.Key >> 32);
            int r = (int)(pair.Key & 0xFFFFFFFF);
            Vector3Int cell = new Vector3Int(q, r, 0);

            if (!worldGenerator.TryGetBuildingType(cell, out BuildingType building) || building != BuildingType.RecoveryProject)
            {
                toRemove.Add(pair.Key);
                continue;
            }

            if (cycleNumber - pair.Value < recoveryProjectDurationCycles)
            {
                continue;
            }

            worldGenerator.TrySetBuildingType(cell, BuildingType.None);
            toRemove.Add(pair.Key);
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            recoveryProjectStartedCycle.Remove(toRemove[i]);
        }
    }

    private static long PackCoord(Vector3Int cell)
    {
        return ((long)cell.x << 32) ^ (uint)cell.y;
    }

    private FiniteEarthResourcePool CalculatePassiveIncome()
    {
        var income = new FiniteEarthResourcePool();

        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (!ownership.IsOwned(cell))
            {
                continue;
            }

            if (worldGenerator.TryGetTileType(cell, out TileType terrainType) && terrainType == TileType.Farmland)
            {
                income.food += 1;
            }

            if (worldGenerator.TryGetBuildingType(cell, out BuildingType buildingType) && buildingType == BuildingType.Industry)
            {
                income.minerals += 1;
            }
        }

        return income;
    }

    private int GetEffectiveSettlementRadius()
    {
        return settlementRadiusOverride > 0 ? settlementRadiusOverride : worldGenerator.SettlementRadius;
    }

    private void RefreshUi()
    {
        if (turnText == null)
        {
            Debug.Log(currentStatusMessage);
            return;
        }

        RefreshHeaderText();
        resourceText.text = resources.ToShortString();
        tileInfoText.text = GetTileInfoText();
        statusText.text = currentStatusMessage;
        legendText.text = GetLegendText();
        RefreshActionButtons();
    }

    private void RefreshHeaderText()
    {
        if (turnText == null || metricText == null)
        {
            return;
        }

        turnText.text = $"UTC {GetClockTimeText()}  Day {clockDay}\nCycle {cycleNumber}  Actions {actionsRemaining}/{maxActionsPerTurn}";
        metricText.text = GetMetricsText();
    }

    private void RefreshActionButtons()
    {
        foreach (KeyValuePair<FiniteEarthActionType, ActionButtonRefs> pair in actionButtons)
        {
            bool interactable = hasSelection && actionsRemaining > 0 && CanExecuteAction(pair.Key, selectedCell, out _);

            ActionSpec spec = actionLookup[pair.Key];
            ActionButtonRefs refs = pair.Value;

            refs.Button.interactable = interactable;
            refs.Background.color = interactable ? spec.Accent : disabledButtonColor;
            refs.Label.color = interactable ? primaryTextColor : secondaryTextColor;

            if (refs.Icon != null)
            {
                refs.Icon.color = interactable ? Color.white : new Color(0.8f, 0.8f, 0.8f, 0.55f);
            }
        }
    }

    private string GetMetricsText()
    {
        int forestTiles = worldGenerator.CountTilesOfType(TileType.Forest);
        int carbonScore = worldGenerator.CalculateCarbonScore();
        int ownedTiles = ownership.GetOwnedCount();
        return $"Forest {forestTiles}   Carbon {carbonScore}   Owned {ownedTiles}   Next Cycle {FormatCountdown(cycleTimer)}";
    }

    private string GetTileInfoText()
    {
        if (!hasSelection || !worldGenerator.TryGetTileType(selectedCell, out TileType terrainType))
        {
            return "No tile selected.\n\nLeft click a hex to inspect it and stage your next action.";
        }

        worldGenerator.TryGetBuildingType(selectedCell, out BuildingType buildingType);
        bool owned = ownership.IsOwned(selectedCell);
        bool inRange = !requireSettlementRadius
            || !worldGenerator.HasAnySettlement()
            || worldGenerator.IsWithinSettlementRadius(selectedCell, GetEffectiveSettlementRadius());

        var validActions = new List<string>();

        foreach (KeyValuePair<FiniteEarthActionType, ActionSpec> pair in actionLookup)
        {
            if (CanExecuteAction(pair.Key, selectedCell, out _))
            {
                validActions.Add(pair.Value.Label);
            }
        }

        string passiveYield = GetPassiveYieldText(selectedCell, terrainType, buildingType);
        string validActionText = validActions.Count > 0 ? string.Join(", ", validActions) : "No legal actions right now";

        return
            $"Tile {selectedCell.x}, {selectedCell.y}\n" +
            $"Terrain: {terrainType.GetDisplayName()}\n" +
            $"Building: {buildingType.GetDisplayName()}\n" +
            $"Owned: {(owned ? "Yes" : "No")}\n" +
            $"Settlement Range: {(inRange ? "Inside" : "Outside")}\n" +
            $"Per Cycle Yield: {passiveYield}\n" +
            $"Valid Actions: {validActionText}";
    }

    private string GetPassiveYieldText(Vector3Int cell, TileType terrainType, BuildingType buildingType)
    {
        int food = terrainType == TileType.Farmland && ownership.IsOwned(cell) ? 1 : 0;
        int minerals = buildingType == BuildingType.Industry && ownership.IsOwned(cell) ? 1 : 0;

        if (food == 0 && minerals == 0)
        {
            return "No passive yield";
        }

        var pieces = new List<string>();

        if (food > 0)
        {
            pieces.Add($"+{food} food");
        }

        if (minerals > 0)
        {
            pieces.Add($"+{minerals} minerals");
        }

        return string.Join(", ", pieces);
    }

    private string GetLegendText()
    {
        return
            "Controls\n" +
            "Left Click: Select tile\n" +
            "Right or Middle Drag: Pan\n" +
            "Mouse Wheel: Zoom\n\n" +
            "World\n" +
            $"{worldGenerator.WorldId}\n\n" +
            "Universal Clock\n" +
            $"New cycle every {GetSecondsPerCycle():0.#} sec\n\n" +
            "Passive Income\n" +
            "Farmland: +1 food each cycle\n" +
            "Industry: +1 mineral each cycle";
    }

    private string DescribeCellShort(Vector3Int cell)
    {
        if (!worldGenerator.TryGetTileType(cell, out TileType terrainType))
        {
            return $"tile {cell.x}, {cell.y}";
        }

        return $"{terrainType.GetDisplayName()} tile at {cell.x}, {cell.y}";
    }

    private string FormatCostShort(FiniteEarthResourcePool cost)
    {
        if (cost.IsZero())
        {
            return "Free";
        }

        var pieces = new List<string>();
        if (cost.wood > 0) pieces.Add($"W{cost.wood}");
        if (cost.food > 0) pieces.Add($"F{cost.food}");
        if (cost.minerals > 0) pieces.Add($"M{cost.minerals}");
        return string.Join(" ", pieces);
    }

    private string FormatCostLong(FiniteEarthResourcePool cost)
    {
        var pieces = new List<string>();
        if (cost.wood > 0) pieces.Add($"{cost.wood} wood");
        if (cost.food > 0) pieces.Add($"{cost.food} food");
        if (cost.minerals > 0) pieces.Add($"{cost.minerals} minerals");
        return pieces.Count > 0 ? string.Join(", ", pieces) : "nothing";
    }

    private string FormatIncome(FiniteEarthResourcePool income)
    {
        return income.IsZero() ? "none" : FormatCostLong(income);
    }

    private float GetSecondsPerCycle()
    {
        return Mathf.Max(1f, secondsPerCycle);
    }

    private void AdvanceClockTime()
    {
        clockMinutes += Mathf.Max(1, minutesPerCycle);

        while (clockMinutes >= 24 * 60)
        {
            clockMinutes -= 24 * 60;
            clockDay++;
        }
    }

    private string GetClockTimeText()
    {
        int normalizedMinutes = Mathf.Clamp(clockMinutes, 0, (24 * 60) - 1);
        int hours = normalizedMinutes / 60;
        int minutes = normalizedMinutes % 60;
        return $"{hours:00}:{minutes:00}";
    }

    private string FormatCountdown(float seconds)
    {
        float clamped = Mathf.Max(0f, seconds);
        int totalSeconds = Mathf.CeilToInt(clamped);
        int minutes = totalSeconds / 60;
        int remainingSeconds = totalSeconds % 60;
        return $"{minutes:00}:{remainingSeconds:00}";
    }

    private void SetStatus(string message)
    {
        currentStatusMessage = message;

        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        var eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }

    private void BuildHud()
    {
        if (hudCanvas != null)
        {
            Destroy(hudCanvas.gameObject);
        }

        var canvasObject = new GameObject("RuntimeHUD");
        canvasObject.transform.SetParent(transform, false);

        hudCanvas = canvasObject.AddComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.pixelPerfect = true;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.6f;

        canvasObject.AddComponent<GraphicRaycaster>();

        RectTransform root = hudCanvas.GetComponent<RectTransform>();

        const float outerMargin = 18f;
        const float topBarHeight = 92f;
        const float sidePanelWidth = 412f;

        Color topCardColor = new Color(0.15f, 0.18f, 0.19f, 0.96f);
        Color infoCardColor = new Color(0.13f, 0.17f, 0.18f, 0.96f);
        Color actionCardColor = new Color(0.12f, 0.15f, 0.16f, 0.97f);

        RectTransform topBar = CreatePanel("TopBar", root, topBarColor);
        topBar.anchorMin = new Vector2(0f, 1f);
        topBar.anchorMax = new Vector2(1f, 1f);
        topBar.pivot = new Vector2(0.5f, 1f);
        topBar.offsetMin = new Vector2(outerMargin, -(topBarHeight + outerMargin));
        topBar.offsetMax = new Vector2(-(sidePanelWidth + (outerMargin * 2f)), -outerMargin);

        HorizontalLayoutGroup topLayout = topBar.gameObject.AddComponent<HorizontalLayoutGroup>();
        topLayout.padding = new RectOffset(12, 12, 12, 12);
        topLayout.spacing = 12f;
        topLayout.childAlignment = TextAnchor.MiddleLeft;
        topLayout.childControlWidth = true;
        topLayout.childForceExpandWidth = true;
        topLayout.childControlHeight = true;
        topLayout.childForceExpandHeight = true;

        RectTransform clockCard = CreatePanel("ClockCard", topBar, topCardColor);
        clockCard.gameObject.AddComponent<LayoutElement>().preferredWidth = 318f;
        turnText = CreateText("TurnText", clockCard, 20, FontStyle.Bold, TextAnchor.MiddleLeft, primaryTextColor);
        turnText.resizeTextForBestFit = true;
        turnText.resizeTextMinSize = 15;
        turnText.resizeTextMaxSize = 20;

        RectTransform resourceCard = CreatePanel("ResourceCard", topBar, topCardColor);
        resourceCard.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        resourceText = CreateText("ResourceText", resourceCard, 21, FontStyle.Bold, TextAnchor.MiddleCenter, primaryTextColor);
        resourceText.resizeTextForBestFit = true;
        resourceText.resizeTextMinSize = 16;
        resourceText.resizeTextMaxSize = 22;

        RectTransform metricCard = CreatePanel("MetricCard", topBar, topCardColor);
        metricCard.gameObject.AddComponent<LayoutElement>().preferredWidth = 408f;
        metricText = CreateText("MetricText", metricCard, 18, FontStyle.Normal, TextAnchor.MiddleRight, primaryTextColor);
        metricText.resizeTextForBestFit = true;
        metricText.resizeTextMinSize = 13;
        metricText.resizeTextMaxSize = 18;

        RectTransform sidePanel = CreatePanel("SidePanel", root, sidePanelColor);
        sidePanel.anchorMin = new Vector2(1f, 0f);
        sidePanel.anchorMax = new Vector2(1f, 1f);
        sidePanel.pivot = new Vector2(1f, 1f);
        sidePanel.offsetMin = new Vector2(-(sidePanelWidth + outerMargin), outerMargin);
        sidePanel.offsetMax = new Vector2(-outerMargin, -(topBarHeight + outerMargin));
        sidePanel.gameObject.AddComponent<RectMask2D>();

        VerticalLayoutGroup sideLayout = sidePanel.gameObject.AddComponent<VerticalLayoutGroup>();
        sideLayout.padding = new RectOffset(16, 16, 16, 16);
        sideLayout.spacing = 14f;
        sideLayout.childAlignment = TextAnchor.UpperLeft;
        sideLayout.childControlWidth = true;
        sideLayout.childForceExpandWidth = true;
        sideLayout.childControlHeight = false;
        sideLayout.childForceExpandHeight = false;

        RectTransform tileCard = CreateCard("TileCard", sidePanel, infoCardColor, 156);
        CreateSectionHeader("Selected Tile", tileCard);
        tileInfoText = CreateText("TileInfo", tileCard, 15, FontStyle.Normal, TextAnchor.UpperLeft, primaryTextColor);
        tileInfoText.gameObject.AddComponent<LayoutElement>().preferredHeight = 108f;
        tileInfoText.resizeTextForBestFit = true;
        tileInfoText.resizeTextMinSize = 11;
        tileInfoText.resizeTextMaxSize = 15;

        RectTransform actionCard = CreateCard("ActionCard", sidePanel, actionCardColor, 452);
        CreateSectionHeader("Actions", actionCard);
        RectTransform actionGrid = CreateContainer("ActionGrid", actionCard);
        GridLayoutGroup grid = actionGrid.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(180f, 70f);
        grid.spacing = new Vector2(10f, 10f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        actionGrid.gameObject.AddComponent<LayoutElement>().preferredHeight = 390f;

        foreach (KeyValuePair<FiniteEarthActionType, ActionSpec> pair in actionLookup)
        {
            actionButtons[pair.Key] = CreateActionButton(pair.Value, actionGrid);
        }

        RectTransform statusCard = CreateCard("StatusCard", sidePanel, infoCardColor, 102);
        CreateSectionHeader("Status", statusCard);
        statusText = CreateText("Status", statusCard, 14, FontStyle.Normal, TextAnchor.UpperLeft, primaryTextColor);
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;
        statusText.resizeTextForBestFit = true;
        statusText.resizeTextMinSize = 11;
        statusText.resizeTextMaxSize = 14;

        RectTransform guideCard = CreateCard("GuideCard", sidePanel, infoCardColor, 114);
        CreateSectionHeader("Guide", guideCard);
        legendText = CreateText("Legend", guideCard, 13, FontStyle.Normal, TextAnchor.UpperLeft, secondaryTextColor);
        legendText.gameObject.AddComponent<LayoutElement>().preferredHeight = 74f;
        legendText.resizeTextForBestFit = true;
        legendText.resizeTextMinSize = 10;
        legendText.resizeTextMaxSize = 13;
    }

    private ActionButtonRefs CreateActionButton(ActionSpec spec, Transform parent)
    {
        var buttonObject = new GameObject(spec.Label + "Button");
        buttonObject.transform.SetParent(parent, false);

        RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
        Image background = buttonObject.AddComponent<Image>();
        background.color = spec.Accent;
        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.20f);
        outline.effectDistance = new Vector2(1f, -1f);

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.96f);
        colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        colors.disabledColor = Color.white;
        button.colors = colors;

        FiniteEarthActionType capturedAction = spec.Type;
        button.onClick.AddListener(delegate { OnActionPressed(capturedAction); });

        Image icon = null;

        if (spec.Icon != null)
        {
            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(buttonRect, false);
            RectTransform iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.sizeDelta = new Vector2(30f, 30f);
            iconRect.anchoredPosition = new Vector2(12f, 0f);

            icon = iconObject.AddComponent<Image>();
            icon.sprite = spec.Icon;
            icon.preserveAspect = true;
            icon.color = Color.white;
        }

        Text label = CreateText("Label", buttonRect, 14, FontStyle.Bold, TextAnchor.MiddleCenter, primaryTextColor);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = spec.Icon != null ? new Vector2(42f, 8f) : new Vector2(10f, 8f);
        labelRect.offsetMax = new Vector2(-10f, -8f);
        label.text = $"<b>{spec.Label}</b>\n{FormatCostShort(spec.Cost)}";
        label.supportRichText = true;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 11;
        label.resizeTextMaxSize = 18;

        return new ActionButtonRefs
        {
            Button = button,
            Background = background,
            Label = label,
            Icon = icon
        };
    }

    private RectTransform CreatePanel(string objectName, Transform parent, Color color)
    {
        RectTransform rect = CreateContainer(objectName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return rect;
    }

    private RectTransform CreateCard(string objectName, Transform parent, Color color, float preferredHeight)
    {
        RectTransform rect = CreatePanel(objectName, parent, color);
        rect.gameObject.AddComponent<RectMask2D>();

        VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 14, 14);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;

        LayoutElement layoutElement = rect.gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = preferredHeight;
        return rect;
    }

    private RectTransform CreateContainer(string objectName, Transform parent)
    {
        var gameObject = new GameObject(objectName);
        gameObject.transform.SetParent(parent, false);
        return gameObject.AddComponent<RectTransform>();
    }

    private void CreateSectionHeader(string title, Transform parent)
    {
        Text header = CreateText(title + "Header", parent, 12, FontStyle.Bold, TextAnchor.MiddleLeft, secondaryTextColor);
        header.text = title.ToUpperInvariant();
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
    }

    private Text CreateText(string objectName, Transform parent, int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color)
    {
        var gameObject = new GameObject(objectName);
        gameObject.transform.SetParent(parent, false);

        Text text = gameObject.AddComponent<Text>();
        text.font = GetUiFont();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.supportRichText = true;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return text;
    }

    private Font GetUiFont()
    {
        if (uiFont != null)
        {
            return uiFont;
        }

        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        if (uiFont == null)
        {
            uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        if (uiFont == null)
        {
            uiFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
        }

        return uiFont;
    }
}
