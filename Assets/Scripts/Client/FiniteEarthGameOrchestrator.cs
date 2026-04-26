using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.Tilemaps;

public class FiniteEarthGameOrchestrator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;
    [SerializeField] private OwnershipOverlayPointTop ownership;
    [SerializeField] private GameStateViewModel viewModel;
    [SerializeField] private ActionInputController inputController;
    [SerializeField] private ActionPanelPresenter actionPanel;
    [SerializeField] private SpacetimeClientManager stdbClient;
    [SerializeField] private WalletSessionController walletSession;
    [SerializeField] private WorldCameraController worldCameraController;
    [SerializeField] private ArmyOverlayPointTop armyOverlay;
    [SerializeField] private bool runUniversalCycleLocallyWhenOffline = true;
    [SerializeField] private bool useUnscaledTimeForCycleClock = true;
    [SerializeField] private bool assignStarterSettlementOnLocalLogin = true;
    [SerializeField] private bool rememberWalletSpawnLocally = true;

    [Header("Starting Resources")]
    [SerializeField, Min(0)] private int startingWood = 6;
    [SerializeField, Min(0)] private int startingFood = 8;
    [SerializeField, Min(0)] private int startingMinerals = 4;

    [Header("Natural Recovery")]
    [SerializeField, Min(1)] private int deforestedToPlainsCycles = 3;
    [SerializeField, Min(1)] private int recoveryProjectCycles = 2;

    [Header("Passive Yield")]
    [SerializeField, Min(0f)] private float farmFoodPerCycle = 0.125f;
    [SerializeField, Min(0f)] private float industryMineralsPerCycle = 0.05f;
    [SerializeField, Min(0f)] private float industryYieldOnBarren = 0.75f;
    [SerializeField, Min(0f)] private float industryYieldOnPlains = 1f;
    [SerializeField, Min(0f)] private float industryYieldOnMountain = 1.5f;

    [Header("Armies")]
    [SerializeField, Min(1f)] private float armyMoveCooldownSeconds = 10f;
    [SerializeField, Min(1)] private int maxArmyStrength = 3;
    [SerializeField, Min(0)] private int reinforceFoodCost = 2;

    [Header("Climate Events")]
    [SerializeField, Range(0f, 1f)] private float carbonTierOne = 0.60f;
    [SerializeField, Range(0f, 1f)] private float carbonTierTwo = 0.75f;
    [SerializeField, Range(0f, 1f)] private float carbonTierThree = 0.90f;
    [SerializeField, Range(0f, 1f)] private float forestTierOne = 0.40f;
    [SerializeField, Range(0f, 1f)] private float forestTierTwo = 0.25f;
    [SerializeField] private int heatwaveDurationCycles = 3;
    [SerializeField] private float heatwaveFoodPenalty = 0.25f;
    [SerializeField] private Vector2Int wildfireTilesRange = new Vector2Int(3, 6);
    [SerializeField, Min(1)] private int wildfireDeforestedDelayCycles = 1;
    [SerializeField, Min(2)] private int wildfireBarrenDelayCycles = 2;
    [SerializeField, Min(1)] private int wildfirePlayerRadius = 12;
    [SerializeField] private Vector2Int floodTilesRange = new Vector2Int(3, 5);
    [SerializeField, Range(0f, 1f)] private float floodWoodRotFraction = 0.15f;
    [SerializeField] private Vector2Int iceMeltTilesRange = new Vector2Int(2, 4);
    [SerializeField] private Vector2Int desertSpreadRange = new Vector2Int(1, 2);

    [Header("Adjacency Bonuses")]
    [SerializeField, Range(0f, 1f)] private float adjacencyYieldBonus = 0.10f;
    [SerializeField, Min(0)] private int forestClusterEcosystemBonus = 10;

    [Header("Debug Cheats")]
    [SerializeField] private bool enableResourceCheatHotkeys = true;
    [SerializeField, Min(1)] private int cheatResourceAmount = 10;

    private readonly LocalPredictionEngine predictionEngine = new LocalPredictionEngine();
    private static readonly FiniteEarthActionType[] UiActions =
    {
        FiniteEarthActionType.BuildSettlement,
        FiniteEarthActionType.BuildBarracks,
        FiniteEarthActionType.BuildIndustry,
        FiniteEarthActionType.RemoveBuilding,
        FiniteEarthActionType.HarvestForest,
        FiniteEarthActionType.Reforest,
        FiniteEarthActionType.Farm,
        FiniteEarthActionType.Irrigate,
        FiniteEarthActionType.Mine,
        FiniteEarthActionType.Restore,
        FiniteEarthActionType.SpawnArmy
    };

    private IActionResolver resolver;
    private UnityWorldAdapter worldAdapter;
    private HexCoord selectedCoord;
    private readonly List<HexCoord> selectedCoords = new List<HexCoord>();
    private bool hasSelection;
    private float cycleRemainingSeconds;
    private bool isInitialized;
    private bool hasFocusedOwnedArea;
    private bool hasSelectionPreview;
    private string pendingAuthenticatedWallet;
    private bool pendingLocalBootstrap;
    private string activeWalletAddress;
    private bool hasAttemptedOfflineSpawnRecovery;
    private string selectedArmyId;
    private bool armyMoveMode;
    private readonly Dictionary<long, int> deforestedSinceTick = new Dictionary<long, int>();
    private readonly Dictionary<long, int> recoveryProjectUntilTick = new Dictionary<long, int>();
    private readonly Dictionary<long, int> miningCounts = new Dictionary<long, int>();
    // Army state — STDB is the single source of truth.
    // These three dicts hold only transient client-side overrides.
    private readonly Dictionary<ulong, HexCoord> optimisticArmyPositions = new Dictionary<ulong, HexCoord>();
    private readonly Dictionary<ulong, float>    armyLastMoveTimes        = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, int>      armyStrengths            = new Dictionary<ulong, int>();
    private readonly List<ClimateEventInstance> activeEvents = new List<ClimateEventInstance>();
    private readonly List<ClimateTileHighlight> climateTileHighlights = new List<ClimateTileHighlight>();
    private readonly List<TradeOffer> tradeOffers = new List<TradeOffer>();
    private readonly List<DiplomacyPact> diplomacyPacts = new List<DiplomacyPact>();
    private readonly Dictionary<long, WildfirePatchState> activeWildfirePatches = new Dictionary<long, WildfirePatchState>();
    private readonly HashSet<long> activeServerClimateEventIds = new HashSet<long>();
    private readonly Dictionary<long, int> capturePressure = new Dictionary<long, int>();
    private readonly Dictionary<long, string> ownerByTile = new Dictionary<long, string>();
    private readonly Dictionary<string, string> playerLabelByWallet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FiniteEarthResourcePool> pactShareLedger = new Dictionary<string, FiniteEarthResourcePool>();
    private readonly List<ActionAvailability> lastActionStates = new List<ActionAvailability>();
    private readonly HashSet<string> optimisticallyApplied = new HashSet<string>();
    private float woodBonusRemainder;
    private float passiveFoodRemainder;
    private float passiveMineralRemainder;

    public GameStateViewModel ViewModel => viewModel;
    public float CycleRemainingSeconds => Mathf.Max(0f, cycleRemainingSeconds);
    public bool UsesLocalCycleClock
    {
        get
        {
            if (!runUniversalCycleLocallyWhenOffline)
            {
                return false;
            }

            if (walletSession != null)
            {
                return walletSession.IsOfflineMode;
            }

            return stdbClient == null || !stdbClient.IsReady;
        }
    }
    public bool HasSelection => hasSelection;
    public HexCoord SelectedCoord => selectedCoord;
    public int SelectionCount => selectedCoords.Count > 0 ? selectedCoords.Count : (hasSelection ? 1 : 0);
    public IReadOnlyList<HexCoord> SelectedCoords => selectedCoords;
    public IReadOnlyList<ActionAvailability> LastActionStates => lastActionStates;
    public string ActiveWalletAddress => activeWalletAddress;
    public float FarmFoodPerCycle => farmFoodPerCycle;
    public float IndustryMineralsPerCycle => industryMineralsPerCycle;
    public float IndustryYieldOnBarren => industryYieldOnBarren;
    public float IndustryYieldOnPlains => industryYieldOnPlains;
    public float IndustryYieldOnMountain => industryYieldOnMountain;
    public float AdjacencyYieldBonus => adjacencyYieldBonus;
    public int OwnedArmyCount => CountOwnedArmies();
    public int OwnedSettlementCount => CountOwnedSettlements();
    public int OwnedBarracksCount => CountOwnedBarracks();
    public float ArmyMoveCooldownSeconds => armyMoveCooldownSeconds;
    public int MaxArmyStrength => Mathf.Max(1, maxArmyStrength);
    public bool IsArmyMoveArmed => armyMoveMode;
    public int ForestClusterEcosystemBonus => forestClusterEcosystemBonus;
    public bool HasForestClusterBonusActive => HasForestCluster();
    public IReadOnlyList<TradeOffer> GetTradeOffers() => tradeOffers;
    public IReadOnlyList<DiplomacyPact> GetDiplomacyPacts() => diplomacyPacts;
    public IReadOnlyList<ClimateTileHighlight> GetActiveClimateTileHighlights()
    {
        if (viewModel?.WorldState != null)
        {
            PruneExpiredClimateTileHighlights(viewModel.WorldState.tick);
        }

        return climateTileHighlights;
    }

    public event Action<FiniteEarthActionType, int> ActionExecuted;
    public event Action<HexCoord, FiniteEarthResourcePool> ResourcePopupRequested;
    public event Action<ClimateEventType> ClimateEventTriggered;

    private sealed class WildfirePatchState
    {
        public HexCoord coord;
        public int deforestedTick;
        public int barrenTick;
        public int endTick;
    }

    private void Awake()
    {
        ResolveRuntimeReferences();
    }

    private void ResolveRuntimeReferences()
    {
        if (worldGenerator == null)
        {
            worldGenerator = FindAnyObjectByType<HexWorldGeneratorTilemap>();
        }

        if (ownership == null)
        {
            ownership = FindAnyObjectByType<OwnershipOverlayPointTop>();
        }

        if (viewModel == null)
        {
            viewModel = FindAnyObjectByType<GameStateViewModel>();
        }

        if (inputController == null)
        {
            inputController = FindAnyObjectByType<ActionInputController>();
        }

        if (actionPanel == null)
        {
            actionPanel = FindAnyObjectByType<ActionPanelPresenter>();
        }

        if (stdbClient == null)
        {
            stdbClient = FindAnyObjectByType<SpacetimeClientManager>();
        }

        if (walletSession == null)
        {
            walletSession = FindAnyObjectByType<WalletSessionController>();
        }

        if (worldCameraController == null)
        {
            worldCameraController = FindAnyObjectByType<WorldCameraController>();
        }

        if (armyOverlay == null)
        {
            armyOverlay = FindAnyObjectByType<ArmyOverlayPointTop>();
        }
    }

    private void Start()
    {
        ResolveRuntimeReferences();
        EnsureArmyOverlay();

        if (worldGenerator == null || ownership == null || viewModel == null)
        {
            Debug.LogError("FiniteEarthGameOrchestrator: missing required references.");
            enabled = false;
            return;
        }

        if (!worldGenerator.IsGenerated)
        {
            worldGenerator.Generate();
        }
        ownership.Initialize(worldGenerator);
        ownership.SetAutomaticSettlementClaimsEnabled(UsesLocalCycleClock);

        worldAdapter = new UnityWorldAdapter(worldGenerator, ownership);
        resolver = new ActionResolver();

        viewModel.Initialize(
            worldAdapter,
            new FiniteEarthResourcePool
            {
                wood = startingWood,
                food = startingFood,
                minerals = startingMinerals
            });

        cycleRemainingSeconds = GetCycleDurationSeconds();
        RebuildDeforestedRegistryFromWorld(viewModel.WorldState.tick);
        RebuildRecoveryProjectRegistryFromWorld(viewModel.WorldState.tick);

        if (inputController != null)
        {
            inputController.TileSelected += HandleTileSelected;
            inputController.TilesSelected += HandleTilesSelected;
            inputController.SelectionPreviewChanged += HandleSelectionPreviewChanged;
            inputController.ActionHotkeyPressed += HandleActionRequested;
        }

        if (actionPanel != null)
        {
            actionPanel.ActionRequested += HandleActionRequested;
        }

        isInitialized = true;
        if (!string.IsNullOrWhiteSpace(pendingAuthenticatedWallet))
        {
            HandleAuthenticatedPlayer(pendingAuthenticatedWallet, pendingLocalBootstrap);
        }

        RefreshActionPanelState();
    }

    private void EnsureArmyOverlay()
    {
        if (armyOverlay != null)
        {
            return;
        }

        armyOverlay = GetComponent<ArmyOverlayPointTop>();
        if (armyOverlay == null)
        {
            armyOverlay = gameObject.AddComponent<ArmyOverlayPointTop>();
        }
    }

    private void Update()
    {
        if (viewModel == null || viewModel.WorldState == null)
        {
            return;
        }

        HandleCheatHotkeys();
        EnsureOfflineStarterTerritory();

        if (!UsesLocalCycleClock)
        {
            return;
        }

        float delta = useUnscaledTimeForCycleClock ? Time.unscaledDeltaTime : Time.deltaTime;
        if (delta <= 0f)
        {
            return;
        }

        cycleRemainingSeconds -= delta;

        float cycleDuration = GetCycleDurationSeconds();
        while (cycleRemainingSeconds <= 0f)
        {
            viewModel.StartNewCycle();
            ApplyPassiveIncomeForCycle();
            AdvanceTradeOffersForCycle();
            ApplyClimateEventsForCycle();
            ApplyCapturePressureForCycle();
            ApplyCarbonCaptureForCycle();
            ApplyNaturalRecoveryForCurrentCycle();
            ApplyRecoveryProjectDecayForCurrentCycle();
            cycleRemainingSeconds += cycleDuration;
        }
    }

    private void HandleCheatHotkeys()
    {
        if (!enableResourceCheatHotkeys || viewModel?.PlayerState == null)
        {
            return;
        }

        if (WasFunctionKeyPressed(1))
        {
            GrantCheatResources(new FiniteEarthResourcePool { wood = cheatResourceAmount }, "wood");
        }

        if (WasFunctionKeyPressed(2))
        {
            GrantCheatResources(new FiniteEarthResourcePool { minerals = cheatResourceAmount }, "ore");
        }

        if (WasFunctionKeyPressed(3))
        {
            GrantCheatResources(new FiniteEarthResourcePool { food = cheatResourceAmount }, "food");
        }
    }

    private void GrantCheatResources(FiniteEarthResourcePool delta, string label)
    {
        if (viewModel?.PlayerState == null || delta.IsZero())
        {
            return;
        }

        viewModel.PlayerState.resources.Add(delta);
        RefreshActionPanelState();

        HexCoord popupCoord = ResolveCheatPopupCoord();
        ResourcePopupRequested?.Invoke(popupCoord, delta);
        Debug.Log($"FiniteEarthGameOrchestrator: cheat granted +{cheatResourceAmount} {label}.");
    }

    private HexCoord ResolveCheatPopupCoord()
    {
        if (hasSelection)
        {
            return selectedCoord;
        }

        if (worldGenerator != null && ownership != null)
        {
            foreach (Vector3Int cell in worldGenerator.EnumerateCells())
            {
                if (ownership.IsOwned(cell))
                {
                    return HexCoord.FromVector3Int(cell);
                }
            }
        }

        return new HexCoord(0, 0);
    }

    private static bool WasFunctionKeyPressed(int index)
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            switch (index)
            {
                case 1:
                    pressed = keyboard.f1Key.wasPressedThisFrame;
                    break;
                case 2:
                    pressed = keyboard.f2Key.wasPressedThisFrame;
                    break;
                case 3:
                    pressed = keyboard.f3Key.wasPressedThisFrame;
                    break;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (!pressed)
        {
            switch (index)
            {
                case 1:
                    pressed = Input.GetKeyDown(KeyCode.F1);
                    break;
                case 2:
                    pressed = Input.GetKeyDown(KeyCode.F2);
                    break;
                case 3:
                    pressed = Input.GetKeyDown(KeyCode.F3);
                    break;
            }
        }
#endif

        return pressed;
    }

    private void OnDestroy()
    {
        if (inputController != null)
        {
            inputController.TileSelected -= HandleTileSelected;
            inputController.TilesSelected -= HandleTilesSelected;
            inputController.SelectionPreviewChanged -= HandleSelectionPreviewChanged;
            inputController.ActionHotkeyPressed -= HandleActionRequested;
        }

        if (actionPanel != null)
        {
            actionPanel.ActionRequested -= HandleActionRequested;
        }

    }

    public void HandleAuthenticatedPlayer(string walletAddress, bool createLocalStartingTerritory)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            return;
        }

        activeWalletAddress = walletAddress.Trim().ToLowerInvariant();
        hasAttemptedOfflineSpawnRecovery = false;
        hasFocusedOwnedArea = false;
        optimisticArmyPositions.Clear();
        armyLastMoveTimes.Clear();
        armyStrengths.Clear();
        selectedArmyId = null;
        armyMoveMode = false;

        if (viewModel != null)
        {
            viewModel.SetWalletAddress(walletAddress);
        }

        worldAdapter?.SetLocalWalletAddress(activeWalletAddress);

        pendingAuthenticatedWallet = activeWalletAddress;
        pendingLocalBootstrap = createLocalStartingTerritory;

        if (!isInitialized || ownership == null || worldGenerator == null)
        {
            return;
        }

        ownership.SetAutomaticSettlementClaimsEnabled(createLocalStartingTerritory || UsesLocalCycleClock);

        if (createLocalStartingTerritory)
        {
            BootstrapLocalTerritory(activeWalletAddress, true);
        }
        else
        {
            ownerByTile.Clear();
            ownership.ResetOwnership();
            ownership.ClearRivalOwnership();
            ClearCurrentSelection();
        }

        RefreshActionPanelState();
    }

    private void HandleTileSelected(HexCoord coord)
    {
        if (TryHandleArmySelectionOrMove(coord))
        {
            return;
        }

        if (hasSelection
            && string.IsNullOrWhiteSpace(selectedArmyId)
            && selectedCoords.Count <= 1
            && selectedCoord.q == coord.q
            && selectedCoord.r == coord.r)
        {
            ClearCurrentSelection();
            return;
        }

        selectedArmyId = null;
        armyMoveMode = false;
        hasSelectionPreview = false;
        selectedCoords.Clear();
        selectedCoords.Add(coord);
        selectedCoord = coord;
        hasSelection = true;
        ownership.SetSelectedCell(coord.ToVector3Int());
        RefreshActionPanelState();
    }

    private void HandleTilesSelected(HexCoord[] coords)
    {
        selectedArmyId = null;
        armyMoveMode = false;
        hasSelectionPreview = false;
        selectedCoords.Clear();
        if (coords == null || coords.Length == 0)
        {
            hasSelection = false;
            ownership.ClearSelection();
            RefreshActionPanelState();
            return;
        }

        var unique = new HashSet<long>();
        var selectedCells = new List<Vector3Int>(coords.Length);

        for (int i = 0; i < coords.Length; i++)
        {
            HexCoord coord = coords[i];
            long key = (((long)coord.q) << 32) ^ (uint)coord.r;
            if (!unique.Add(key))
            {
                continue;
            }

            selectedCoords.Add(coord);
            selectedCells.Add(coord.ToVector3Int());
        }

        if (selectedCoords.Count == 0)
        {
            hasSelection = false;
            ownership.ClearSelection();
            RefreshActionPanelState();
            return;
        }

        selectedCoord = selectedCoords[0];
        hasSelection = true;
        ownership.SetSelectedCells(selectedCells);
        RefreshActionPanelState();
    }

    private bool TryHandleArmySelectionOrMove(HexCoord coord)
    {
        if (string.IsNullOrWhiteSpace(activeWalletAddress))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selectedArmyId))
        {
            if (TryGetSelectedArmy(out ArmyUnit selectedArmy, out _)
                && selectedArmy.coord.q == coord.q
                && selectedArmy.coord.r == coord.r)
            {
                selectedArmyId = null;
                armyMoveMode = false;
                ClearCurrentSelection();
                return true;
            }

            if ((armyMoveMode || HexWorldGeneratorTilemap.HexDistance(selectedCoord.ToVector3Int(), coord.ToVector3Int()) == 1)
                && TryMoveArmy(selectedArmyId, coord))
            {
                armyMoveMode = false;
                SetArmySelectionState(coord);
                return true;
            }
        }

        if (TrySelectArmyAt(coord, out string armyId))
        {
            selectedArmyId = armyId;
            armyMoveMode = false;
            SetArmySelectionState(coord);
            return true;
        }

        return false;
    }

    private bool TrySelectArmyAt(HexCoord coord, out string armyId)
    {
        armyId = string.Empty;
        if (stdbClient == null || !stdbClient.IsReady || string.IsNullOrWhiteSpace(activeWalletAddress))
            return false;

        // Check optimistic positions first — army we just moved whose STDB row
        // hasn't reflected the new position yet.
        foreach (var kv in optimisticArmyPositions)
        {
            if (kv.Value.q != coord.q || kv.Value.r != coord.r) continue;
            var row = stdbClient.GetArmyById(kv.Key);
            if (row == null) continue;
            if (!string.Equals(row.Owner, activeWalletAddress, StringComparison.OrdinalIgnoreCase)) continue;
            armyId = kv.Key.ToString();
            return true;
        }

        // Query STDB directly.
        var stdbArmy = stdbClient.FindArmyAt(activeWalletAddress, coord.q, coord.r);
        if (stdbArmy != null)
        {
            armyId = stdbArmy.Id.ToString();
            return true;
        }

        return false;
    }

    private bool TryMoveArmy(string armyId, HexCoord target)
    {
        if (!ulong.TryParse(armyId, out ulong stdbId)) return false;
        if (stdbClient == null || !stdbClient.IsReady) return false;

        var row = stdbClient.GetArmyById(stdbId);
        if (row == null) return false;

        // Current position is optimistic override (if recent move pending) or STDB.
        HexCoord currentPos = optimisticArmyPositions.TryGetValue(stdbId, out HexCoord opt)
            ? opt
            : new HexCoord(row.Q, row.R);

        if (HexWorldGeneratorTilemap.HexDistance(currentPos.ToVector3Int(), target.ToVector3Int()) != 1)
            return false;

        float movedAt = armyLastMoveTimes.TryGetValue(stdbId, out float t) ? t : float.MinValue;
        if (Time.unscaledTime - movedAt < armyMoveCooldownSeconds)
            return false;

        if (worldGenerator == null
            || !worldGenerator.TryGetTileType(target.ToVector3Int(), out TileType terrain)
            || !terrain.IsClaimable())
            return false;

        // Optimistic update for instant visual feedback.
        optimisticArmyPositions[stdbId] = target;
        armyLastMoveTimes[stdbId] = Time.unscaledTime;

        stdbClient.SendArmyMove(stdbId, row.Owner ?? activeWalletAddress, target.q, target.r);
        RenderArmies();
        return true;
    }

    private void SetArmySelectionState(HexCoord coord)
    {
        selectedCoord = coord;
        selectedCoords.Clear();
        selectedCoords.Add(coord);
        hasSelection = true;
        ownership?.SetSelectedCell(coord.ToVector3Int());
        RefreshActionPanelState();
    }

    private void ClearCurrentSelection()
    {
        selectedArmyId = null;
        armyMoveMode = false;
        hasSelection = false;
        hasSelectionPreview = false;
        selectedCoords.Clear();
        ownership?.ClearSelection();
        RefreshActionPanelState();
    }

    private void HandleSelectionPreviewChanged(HexCoord[] coords)
    {
        if (ownership == null)
        {
            return;
        }

        if (coords == null || coords.Length == 0)
        {
            if (!hasSelectionPreview)
            {
                return;
            }

            hasSelectionPreview = false;
            RestoreCommittedSelectionVisual();
            return;
        }

        hasSelectionPreview = true;
        var unique = new HashSet<long>();
        var previewCells = new List<Vector3Int>(coords.Length);

        for (int i = 0; i < coords.Length; i++)
        {
            HexCoord coord = coords[i];
            long key = (((long)coord.q) << 32) ^ (uint)coord.r;
            if (!unique.Add(key))
            {
                continue;
            }

            previewCells.Add(coord.ToVector3Int());
        }

        if (previewCells.Count == 0)
        {
            hasSelectionPreview = false;
            RestoreCommittedSelectionVisual();
            return;
        }

        if (previewCells.Count == 1)
        {
            ownership.SetSelectedCell(previewCells[0]);
            return;
        }

        ownership.SetSelectedCells(previewCells);
    }

    private bool CanSpawnArmyAt(HexCoord coord)
    {
        if (worldGenerator == null || ownership == null || string.IsNullOrWhiteSpace(activeWalletAddress))
        {
            return false;
        }

        if (!ownership.IsOwned(coord.ToVector3Int()))
        {
            return false;
        }

        if (!worldGenerator.TryGetBuildingType(coord.ToVector3Int(), out BuildingType building) || building != BuildingType.Barracks)
        {
            return false;
        }

        int barracksCount = CountOwnedBarracks();
        int ownedArmies = CountOwnedArmies();
        return ownedArmies < barracksCount;
    }

    private int CountOwnedSettlements()
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

            if (worldGenerator.TryGetBuildingType(cell, out BuildingType building) && building == BuildingType.Settlement)
            {
                count++;
            }
        }

        return count;
    }

    private int CountOwnedBarracks()
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

            if (worldGenerator.TryGetBuildingType(cell, out BuildingType building) && building == BuildingType.Barracks)
            {
                count++;
            }
        }

        return count;
    }

    private int CountOwnedArmies()
    {
        if (stdbClient != null && stdbClient.IsReady)
            return stdbClient.CountArmiesForWallet(activeWalletAddress);
        return 0;
    }

    private void PostApplyLocalAction(FiniteEarthActionType actionType, HexCoord coord, ActionResolution resolution)
    {
        if (!resolution.accepted || viewModel == null || viewModel.PlayerState == null || worldGenerator == null)
        {
            return;
        }

        FiniteEarthResourcePool popupDelta = resolution.playerDelta.resourceDelta;

        if (actionType == FiniteEarthActionType.Mine)
        {
            worldGenerator.IncrementMiningCount(coord.ToVector3Int());
        }

        if (actionType == FiniteEarthActionType.Restore
            || actionType == FiniteEarthActionType.Reforest
            || actionType == FiniteEarthActionType.Farm
            || actionType == FiniteEarthActionType.Irrigate)
        {
            worldGenerator.ResetMiningCount(coord.ToVector3Int());
        }

        if (actionType == FiniteEarthActionType.HarvestForest && viewModel.PlayerState.techBasicForestry)
        {
            float bonus = (resolution.playerDelta.resourceDelta.wood > 0 ? resolution.playerDelta.resourceDelta.wood : 0) * 0.10f;
            woodBonusRemainder += bonus;
            int payout = Mathf.FloorToInt(woodBonusRemainder);
            if (payout > 0)
            {
                viewModel.PlayerState.resources.Add(new FiniteEarthResourcePool { wood = payout });
                woodBonusRemainder -= payout;
                popupDelta.wood += payout;
            }
        }

        if (actionType == FiniteEarthActionType.BuildIndustry && viewModel.PlayerState.techRenewableEnergy)
        {
            int reduction = Mathf.RoundToInt(Mathf.Max(0, resolution.globalDelta.carbonDelta) * 0.25f);
            if (reduction > 0)
            {
                ApplyGlobalCarbonDelta(-reduction);
            }
        }

        if (actionType == FiniteEarthActionType.Claim)
        {
            long key = PackCoord(coord.q, coord.r);
            ownerByTile[key] = activeWalletAddress;
        }

        TrackReputation(actionType);
        ApplyForestClusterBonus();
        // Army rendering is driven by ArmiesChanged event from STDB (SpawnArmy case)
        // and explicit RenderArmies calls elsewhere; no local list to update here.

        if (!popupDelta.IsZero())
        {
            ResourcePopupRequested?.Invoke(coord, popupDelta);
        }

        ActionExecuted?.Invoke(actionType, Mathf.Max(1, resolution.tileDeltas?.Length ?? 1));
    }

    // Builds the render list from STDB with optimistic position overrides applied.
    // Also expires stale optimistic entries (e.g. when a move was rejected server-side).
    private System.Collections.Generic.List<ArmyUnit> GetArmyUnitsWithOptimistic()
    {
        if (stdbClient == null || !stdbClient.IsReady)
            return new System.Collections.Generic.List<ArmyUnit>();

        var list = stdbClient.GetArmyUnitsForRendering();
        float now = Time.unscaledTime;

        // Expire optimistic entries that are older than the cooldown window.
        var expired = new System.Collections.Generic.List<ulong>();
        foreach (var kv in optimisticArmyPositions)
        {
            if (!armyLastMoveTimes.TryGetValue(kv.Key, out float movedAt)
                || now - movedAt > armyMoveCooldownSeconds + 5f)
                expired.Add(kv.Key);
        }
        foreach (var id in expired) optimisticArmyPositions.Remove(id);

        // Apply remaining optimistic overrides and clear entries STDB has confirmed.
        for (int i = 0; i < list.Count; i++)
        {
            if (!ulong.TryParse(list[i].id, out ulong id)) continue;
            if (!optimisticArmyPositions.TryGetValue(id, out HexCoord opt)) continue;

            if (list[i].coord.q == opt.q && list[i].coord.r == opt.r)
                optimisticArmyPositions.Remove(id); // STDB caught up — drop override
            else
                list[i].coord = opt;
        }
        return list;
    }

    private void RenderArmies()
    {
        if (armyOverlay == null) return;
        armyOverlay.RenderArmies(GetArmyUnitsWithOptimistic(), ResolveArmyColor);
    }

    public void RenderAllArmies() => RenderArmies();

    private void RevertOptimisticTile(int q, int r)
    {
        if (stdbClient == null || !stdbClient.IsReady) return;
        SpacetimeDB.Types.TileRow? tile = stdbClient.GetTile(q, r);
        if (tile == null) return;

        // Re-apply the server's authoritative tile state to overwrite the bad optimistic render.
        TileDelta delta = new TileDelta(
            q, r,
            (TileType)tile.Terrain, (TileType)tile.Terrain,
            (BuildingType)tile.Building, (BuildingType)tile.Building,
            ownerByTile.ContainsKey(PackCoord(q, r)),
            tile.Owner ?? string.Empty,
            (int)tile.LastUpdate);

        HandleRemoteTileChanged(new RemoteTileChangedMessage
        {
            tileDeltas = new[] { delta }
        });

        // Also restore worldGenerator tilemap to server state
        Vector3Int cell = new Vector3Int(q, r, 0);
        worldGenerator.TrySetTileType(cell, (TileType)tile.Terrain);
        worldGenerator.TrySetBuildingType(cell, (BuildingType)tile.Building);
    }

    public bool IsLocalWallet(string wallet)
    {
        return !string.IsNullOrWhiteSpace(wallet)
            && string.Equals(wallet.Trim(), activeWalletAddress, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryGetSelectedOwner(out string wallet)
    {
        wallet = string.Empty;
        if (!hasSelection)
        {
            return false;
        }

        return TryGetOwnerAt(selectedCoord, out wallet);
    }

    public bool TryGetOwnerAt(HexCoord coord, out string wallet)
    {
        wallet = string.Empty;
        long key = PackCoord(coord.q, coord.r);
        if (ownerByTile.TryGetValue(key, out string stored) && !string.IsNullOrWhiteSpace(stored))
        {
            wallet = stored;
            return true;
        }

        if (ownership != null && ownership.IsOwned(coord.ToVector3Int()))
        {
            wallet = activeWalletAddress;
            return !string.IsNullOrWhiteSpace(wallet);
        }

        return false;
    }

    public bool TryGetSelectedOwnerLabel(out string ownerLabel)
    {
        ownerLabel = "NEUTRAL";
        if (!TryGetSelectedOwner(out string wallet))
        {
            return false;
        }

        ownerLabel = DescribeOwnerLabel(wallet);
        return true;
    }

    public bool TryGetOwnerLabelAt(HexCoord coord, out string ownerLabel)
    {
        ownerLabel = "NEUTRAL";
        if (!TryGetOwnerAt(coord, out string wallet))
        {
            return false;
        }

        ownerLabel = DescribeOwnerLabel(wallet);
        return true;
    }

    public string DescribeOwnerLabel(string wallet)
    {
        if (string.IsNullOrWhiteSpace(wallet))
        {
            return "NEUTRAL";
        }

        if (IsLocalWallet(wallet))
        {
            return "YOU";
        }

        if (TryGetKnownPlayerLabel(wallet, out string playerLabel))
        {
            return playerLabel;
        }

        return FormatWalletLabel(wallet);
    }

    public void RememberPlayerIdentity(string walletAddress, string username, string displayName)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            return;
        }

        string resolvedLabel = string.IsNullOrWhiteSpace(displayName) ? username : displayName;
        if (string.IsNullOrWhiteSpace(resolvedLabel))
        {
            playerLabelByWallet.Remove(walletAddress);
            return;
        }

        playerLabelByWallet[walletAddress.Trim()] = resolvedLabel.Trim();
    }

    private bool TryGetKnownPlayerLabel(string wallet, out string playerLabel)
    {
        playerLabel = string.Empty;
        if (string.IsNullOrWhiteSpace(wallet))
        {
            return false;
        }

        return playerLabelByWallet.TryGetValue(wallet.Trim(), out playerLabel)
            && !string.IsNullOrWhiteSpace(playerLabel);
    }

    private static string FormatWalletLabel(string wallet)
    {
        string normalized = wallet.Trim();
        if (normalized.Length <= 12 || (normalized.IndexOf('-') >= 0 && normalized.Length <= 24))
        {
            return normalized.ToUpperInvariant();
        }

        return $"{normalized.Substring(0, 6)}...{normalized.Substring(normalized.Length - 4)}".ToUpperInvariant();
    }

    public int GetResearchPoints()
    {
        return viewModel?.PlayerState != null ? viewModel.PlayerState.researchPoints : 0;
    }

    public bool IsTechUnlocked(TechNode node)
    {
        return viewModel?.PlayerState != null && FiniteEarthTechTree.IsUnlocked(viewModel.PlayerState, node);
    }

    public bool HasTechPrerequisite(TechDefinition definition)
    {
        return viewModel?.PlayerState != null && FiniteEarthTechTree.HasPrerequisite(viewModel.PlayerState, definition);
    }

    public bool TryResearchTech(TechNode node, out string reason)
    {
        reason = string.Empty;
        if (viewModel?.PlayerState == null)
        {
            reason = "Player state unavailable.";
            return false;
        }

        TechDefinition? definition = null;
        for (int i = 0; i < FiniteEarthTechTree.Nodes.Length; i++)
        {
            if (FiniteEarthTechTree.Nodes[i].node == node)
            {
                definition = FiniteEarthTechTree.Nodes[i];
                break;
            }
        }

        if (definition == null)
        {
            reason = "Unknown tech.";
            return false;
        }

        if (FiniteEarthTechTree.IsUnlocked(viewModel.PlayerState, node))
        {
            reason = "Tech already unlocked.";
            return false;
        }

        if (!FiniteEarthTechTree.HasPrerequisite(viewModel.PlayerState, definition.Value))
        {
            reason = "Prerequisite tech missing.";
            return false;
        }

        if (viewModel.PlayerState.researchPoints < definition.Value.cost)
        {
            reason = "Not enough research points.";
            return false;
        }

        viewModel.PlayerState.researchPoints -= definition.Value.cost;
        FiniteEarthTechTree.Unlock(viewModel.PlayerState, node);
        return true;
    }

    public bool TryCreateTradeOffer(FiniteEarthResourcePool give, FiniteEarthResourcePool want, out string reason)
    {
        reason = string.Empty;

        if (viewModel?.PlayerState == null)
        {
            reason = "Player state unavailable.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(activeWalletAddress))
        {
            reason = "Wallet not initialized.";
            return false;
        }

        if (give.IsZero() || want.IsZero())
        {
            reason = "Offer must include give and want.";
            return false;
        }

        if (!viewModel.PlayerState.resources.CanAfford(give))
        {
            reason = "Not enough resources to post offer.";
            return false;
        }

        viewModel.PlayerState.resources.Spend(give);

        TradeOffer offer = new TradeOffer
        {
            id = Guid.NewGuid().ToString("N"),
            ownerWallet = activeWalletAddress,
            give = give,
            want = want,
            status = TradeOfferStatus.Open,
            createdTick = viewModel.WorldState != null ? viewModel.WorldState.tick : 0,
            expiresTick = viewModel.WorldState != null ? viewModel.WorldState.tick + 3 : 3,
            acceptedBy = string.Empty
        };

        tradeOffers.Add(offer);
        return true;
    }

    public bool TryAcceptTradeOffer(string offerId, out string reason)
    {
        reason = string.Empty;

        if (viewModel?.PlayerState == null)
        {
            reason = "Player state unavailable.";
            return false;
        }

        TradeOffer offer = tradeOffers.Find(entry => entry.id == offerId);
        if (offer == null || offer.status != TradeOfferStatus.Open)
        {
            reason = "Offer not available.";
            return false;
        }

        if (IsLocalWallet(offer.ownerWallet))
        {
            reason = "Cannot accept your own offer.";
            return false;
        }

        if (!viewModel.PlayerState.resources.CanAfford(offer.want))
        {
            reason = "Not enough resources to accept.";
            return false;
        }

        viewModel.PlayerState.resources.Spend(offer.want);
        viewModel.PlayerState.resources.Add(offer.give);

        offer.status = TradeOfferStatus.Accepted;
        offer.acceptedBy = activeWalletAddress;
        return true;
    }

    public bool TryCancelTradeOffer(string offerId, out string reason)
    {
        reason = string.Empty;
        if (viewModel?.PlayerState == null)
        {
            reason = "Player state unavailable.";
            return false;
        }

        TradeOffer offer = tradeOffers.Find(entry => entry.id == offerId);
        if (offer == null)
        {
            reason = "Offer not found.";
            return false;
        }

        if (!IsLocalWallet(offer.ownerWallet))
        {
            reason = "Only the owner can cancel.";
            return false;
        }

        if (offer.status != TradeOfferStatus.Open)
        {
            reason = "Offer is not open.";
            return false;
        }

        offer.status = TradeOfferStatus.Canceled;
        RefundTradeOffer(offer);
        return true;
    }

    public bool TryCreatePact(DiplomacyPactType type, string targetWallet, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(activeWalletAddress))
        {
            reason = "Wallet not initialized.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetWallet))
        {
            reason = "No target selected.";
            return false;
        }

        if (IsLocalWallet(targetWallet))
        {
            reason = "Cannot pact with self.";
            return false;
        }

        DiplomacyPact existing = FindPact(type, targetWallet);
        if (existing != null && existing.status == DiplomacyPactStatus.Active)
        {
            reason = "Pact already active.";
            return false;
        }

        DiplomacyPact pact = new DiplomacyPact
        {
            id = Guid.NewGuid().ToString("N"),
            walletA = activeWalletAddress,
            walletB = targetWallet,
            type = type,
            status = DiplomacyPactStatus.Pending,
            createdTick = viewModel?.WorldState != null ? viewModel.WorldState.tick : 0
        };

        diplomacyPacts.Add(pact);
        return true;
    }

    public bool TryAcceptPact(string pactId, out string reason)
    {
        reason = string.Empty;
        DiplomacyPact pact = diplomacyPacts.Find(entry => entry.id == pactId);
        if (pact == null)
        {
            reason = "Pact not found.";
            return false;
        }

        if (pact.status != DiplomacyPactStatus.Pending)
        {
            reason = "Pact is not pending.";
            return false;
        }

        if (!IsLocalWallet(pact.walletB))
        {
            reason = "Only the invited player can accept.";
            return false;
        }

        pact.status = DiplomacyPactStatus.Active;
        return true;
    }

    public bool TryCancelPact(string pactId, out string reason)
    {
        reason = string.Empty;
        DiplomacyPact pact = diplomacyPacts.Find(entry => entry.id == pactId);
        if (pact == null)
        {
            reason = "Pact not found.";
            return false;
        }

        if (!IsLocalWallet(pact.walletA) && !IsLocalWallet(pact.walletB))
        {
            reason = "Only pact members can cancel.";
            return false;
        }

        pact.status = DiplomacyPactStatus.Canceled;
        return true;
    }

    private DiplomacyPact FindPact(DiplomacyPactType type, string targetWallet)
    {
        return diplomacyPacts.Find(entry =>
            entry.type == type &&
            entry.status != DiplomacyPactStatus.Canceled &&
            ((IsLocalWallet(entry.walletA) && string.Equals(entry.walletB, targetWallet, StringComparison.OrdinalIgnoreCase))
             || (IsLocalWallet(entry.walletB) && string.Equals(entry.walletA, targetWallet, StringComparison.OrdinalIgnoreCase))));
    }

    private void RefundTradeOffer(TradeOffer offer)
    {
        if (offer == null || viewModel?.PlayerState == null)
        {
            return;
        }

        viewModel.PlayerState.resources.Add(offer.give);
    }

    private Color ResolveArmyColor(string wallet)
    {
        if (string.IsNullOrWhiteSpace(wallet))
        {
            return Color.gray;
        }

        if (string.Equals(wallet, activeWalletAddress, StringComparison.OrdinalIgnoreCase))
        {
            return new Color(0.20f, 0.55f, 1f, 1f);
        }

        int hash = wallet.GetHashCode();
        float r = ((hash & 0xFF) / 255f) * 0.6f + 0.2f;
        float g = (((hash >> 8) & 0xFF) / 255f) * 0.6f + 0.2f;
        float b = (((hash >> 16) & 0xFF) / 255f) * 0.6f + 0.2f;
        return new Color(r, g, b, 1f);
    }

    private void TrackReputation(FiniteEarthActionType actionType)
    {
        if (viewModel?.PlayerState == null)
        {
            return;
        }

        switch (actionType)
        {
            case FiniteEarthActionType.Reforest:
            case FiniteEarthActionType.Restore:
                viewModel.PlayerState.ecoActions++;
                break;
            case FiniteEarthActionType.BuildIndustry:
            case FiniteEarthActionType.Mine:
            case FiniteEarthActionType.HarvestForest:
                viewModel.PlayerState.industrialActions++;
                break;
            case FiniteEarthActionType.Farm:
            case FiniteEarthActionType.Irrigate:
                viewModel.PlayerState.agricultureActions++;
                break;
        }

        int eco = viewModel.PlayerState.ecoActions;
        int ind = viewModel.PlayerState.industrialActions;
        int ag = viewModel.PlayerState.agricultureActions;
        int total = eco + ind + ag;
        if (total <= 0)
        {
            viewModel.PlayerState.reputationLabel = "Balanced";
            return;
        }

        float ecoShare = eco / (float)total;
        float indShare = ind / (float)total;
        float agShare = ag / (float)total;

        float maxShare = Mathf.Max(ecoShare, Mathf.Max(indShare, agShare));
        if (maxShare < 0.40f)
        {
            viewModel.PlayerState.reputationLabel = "Balanced";
            return;
        }

        if (maxShare == ecoShare)
        {
            viewModel.PlayerState.reputationLabel = "Eco Guardian";
        }
        else if (maxShare == indShare)
        {
            viewModel.PlayerState.reputationLabel = "Industrial Titan";
        }
        else
        {
            viewModel.PlayerState.reputationLabel = "Agricultural Powerhouse";
        }
    }

    private void ApplyGlobalCarbonDelta(int delta)
    {
        if (viewModel?.WorldState == null)
        {
            return;
        }

        viewModel.WorldState.globalCarbonToken = Mathf.Max(0, viewModel.WorldState.globalCarbonToken + delta);
        RecalculateEcosystemScore();
    }

    private void ApplyForestClusterBonus()
    {
        RecalculateEcosystemScore();
    }

    private void RecalculateEcosystemScore()
    {
        if (viewModel?.WorldState == null)
        {
            return;
        }

        bool hasCluster = HasForestCluster();
        int baseScore = GameStateViewModel.ComputeEcosystemScore(
            viewModel.WorldState.globalForestToken,
            viewModel.WorldState.globalCarbonToken,
            viewModel.WorldState.initialForest,
            viewModel.WorldState.carbonCap);
        int score = baseScore + (hasCluster ? forestClusterEcosystemBonus : 0);
        viewModel.WorldState.ecosystemScore = Mathf.Clamp(score, 0, 100);
    }

    private bool HasForestCluster()
    {
        if (worldGenerator == null)
        {
            return false;
        }

        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (!worldGenerator.TryGetTileType(cell, out TileType terrain) || terrain != TileType.Forest)
            {
                continue;
            }

            Vector3Int[] neighbors = HexWorldGeneratorTilemap.GetNeighborsPointTop(cell);
            int forestNeighbors = 0;
            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector3Int neighbor = neighbors[i];
                if (!worldGenerator.HasTile(neighbor))
                {
                    continue;
                }

                if (worldGenerator.TryGetTileType(neighbor, out TileType neighborType) && neighborType == TileType.Forest)
                {
                    forestNeighbors++;
                }
            }

            if (forestNeighbors >= 3)
            {
                return true;
            }
        }

        return false;
    }

    private void HandleActionRequested(FiniteEarthActionType actionType)
    {
        if (actionType == FiniteEarthActionType.EndTurn)
        {
            return;
        }

        if (actionType == FiniteEarthActionType.Claim)
        {
            RefreshActionPanelState();
            return;
        }

        if (!hasSelection)
        {
            RefreshActionPanelState();
            return;
        }

        if (stdbClient != null && stdbClient.IsReady)
        {
            if (actionType == FiniteEarthActionType.SpawnArmy && !CanSpawnArmyAt(selectedCoord))
            {
                RefreshActionPanelState();
                return;
            }

            if (selectedCoords.Count > 1)
            {
                HandleBatchActionRequested(actionType);
                return;
            }

            ActionIntent realtimeIntent = viewModel.BuildIntent(actionType, selectedCoord);
            ActionResolution realtimePredicted = predictionEngine.Predict(
                resolver, realtimeIntent, viewModel.WorldState, viewModel.PlayerState, viewModel.WorldState.tick);

            if (!realtimePredicted.accepted)
            {
                RefreshActionPanelState();
                return;
            }

            if (!QueueRealtimeIntent(realtimeIntent))
            {
                RefreshActionPanelState();
                return;
            }

            optimisticallyApplied.Add(realtimeIntent.intentId);
            viewModel.ApplyResolution(realtimePredicted, worldAdapter);
            PostApplyLocalAction(actionType, selectedCoord, realtimePredicted);
            TrackDeforestedTransitions(realtimePredicted);
            TrackRecoveryProjectTransitions(realtimePredicted);
            ownership.RefreshOverlay();
            RefreshActionPanelState();
            ClearCurrentSelection();
            return;
        }

        if (selectedCoords.Count > 1)
        {
            HandleBatchActionRequested(actionType);
            return;
        }

        if (actionType == FiniteEarthActionType.SpawnArmy && !CanSpawnArmyAt(selectedCoord))
        {
            RefreshActionPanelState();
            return;
        }

        if (actionType == FiniteEarthActionType.Claim && IsClaimBlockedByPact(selectedCoord, out _))
        {
            RefreshActionPanelState();
            return;
        }

        ActionIntent intent = viewModel.BuildIntent(actionType, selectedCoord);
        ActionResolution predicted = predictionEngine.Predict(
            resolver,
            intent,
            viewModel.WorldState,
            viewModel.PlayerState,
            viewModel.WorldState.tick);

        if (!predicted.accepted)
        {
            RefreshActionPanelState();
            return;
        }

        viewModel.ApplyResolution(predicted, worldAdapter);
        PostApplyLocalAction(actionType, selectedCoord, predicted);
        TrackDeforestedTransitions(predicted);
        TrackRecoveryProjectTransitions(predicted);
        ownership.RefreshOverlay();
        RefreshActionPanelState();
    }

    private void HandleBatchActionRequested(FiniteEarthActionType actionType)
    {
        if (selectedCoords.Count == 0)
        {
            RefreshActionPanelState();
            return;
        }

        if (actionType == FiniteEarthActionType.SpawnArmy)
        {
            RefreshActionPanelState();
            return;
        }

        if (stdbClient != null && stdbClient.IsReady)
        {
            var batchIntents = new List<ActionIntent>(selectedCoords.Count);
            for (int i = 0; i < selectedCoords.Count; i++)
            {
                HexCoord coord = selectedCoords[i];
                batchIntents.Add(viewModel.BuildIntent(actionType, coord));
            }

            if (!QueueRealtimeIntents(batchIntents))
            {
                RefreshActionPanelState();
                return;
            }

            bool appliedAny = false;
            for (int i = 0; i < batchIntents.Count; i++)
            {
                ActionIntent batchIntent = batchIntents[i];
                ActionResolution batchPredicted = predictionEngine.Predict(
                    resolver, batchIntent, viewModel.WorldState, viewModel.PlayerState, viewModel.WorldState.tick);

                if (!batchPredicted.accepted)
                {
                    continue;
                }

                optimisticallyApplied.Add(batchIntent.intentId);
                viewModel.ApplyResolution(batchPredicted, worldAdapter);
                PostApplyLocalAction(actionType, new HexCoord(batchIntent.q, batchIntent.r), batchPredicted);
                TrackDeforestedTransitions(batchPredicted);
                TrackRecoveryProjectTransitions(batchPredicted);
                appliedAny = true;
            }

            if (appliedAny)
            {
                ownership.RefreshOverlay();
                RefreshActionPanelState();
                ClearCurrentSelection();
            }
            else
            {
                RefreshActionPanelState();
            }
            return;
        }

        int applied = 0;
        for (int i = 0; i < selectedCoords.Count; i++)
        {
            HexCoord coord = selectedCoords[i];
            if (actionType == FiniteEarthActionType.Claim && IsClaimBlockedByPact(coord, out _))
            {
                continue;
            }
            ActionIntent intent = viewModel.BuildIntent(actionType, coord);
            ActionResolution predicted = predictionEngine.Predict(
                resolver,
                intent,
                viewModel.WorldState,
                viewModel.PlayerState,
                viewModel.WorldState.tick);

            if (!predicted.accepted)
            {
                continue;
            }

            viewModel.ApplyResolution(predicted, worldAdapter);
            PostApplyLocalAction(actionType, coord, predicted);
            TrackDeforestedTransitions(predicted);
            TrackRecoveryProjectTransitions(predicted);
            applied++;
        }

        if (applied == 0)
        {
        }

        ownership.RefreshOverlay();
        RefreshActionPanelState();
    }

    private bool QueueRealtimeIntent(ActionIntent intent)
    {
        if (intent.Equals(default(ActionIntent)))
        {
            return false;
        }

        return QueueRealtimeIntents(new[] { intent });
    }

    private bool QueueRealtimeIntents(IReadOnlyList<ActionIntent> intents)
    {
        if (stdbClient == null || !stdbClient.IsReady)
        {
            Debug.LogWarning("Action submit skipped: SpacetimeDB not ready.");
            return false;
        }

        if (intents == null || intents.Count == 0)
            return false;

        // SpacetimeDB reducer calls are synchronous fire-and-forget — no async queue needed.
        if (intents.Count == 1)
        {
            ActionIntent i = intents[0];
            Debug.Log($"Submit intent: action={i.actionType} q={i.q} r={i.r} seq={i.clientSeq}");
            stdbClient.SendIntent(i.intentId, i.walletAddress, i.clientSeq, (int)i.actionType, i.q, i.r);
        }
        else
        {
            string[] ids      = new string[intents.Count];
            long[]   seqs     = new long[intents.Count];
            int[]    types    = new int[intents.Count];
            int[]    qs       = new int[intents.Count];
            int[]    rs       = new int[intents.Count];
            string   wallet   = intents[0].walletAddress;

            for (int k = 0; k < intents.Count; k++)
            {
                ids[k]   = intents[k].intentId;
                seqs[k]  = intents[k].clientSeq;
                types[k] = (int)intents[k].actionType;
                qs[k]    = intents[k].q;
                rs[k]    = intents[k].r;
            }

            Debug.Log($"Submit batch: count={intents.Count}");
            stdbClient.SendIntentBatch(ids, wallet, seqs, types, qs, rs);
        }

        return true;
    }

    public void HandleRemoteTileChanged(RemoteTileChangedMessage message)
    {
        if (message == null || message.tileDeltas == null || message.tileDeltas.Length == 0)
        {
            return;
        }

        for (int i = 0; i < message.tileDeltas.Length; i++)
        {
            TileDelta delta = message.tileDeltas[i];
            Vector3Int cell = new Vector3Int(delta.q, delta.r, 0);

            if (delta.nextTerrain != delta.previousTerrain)
            {
                worldGenerator.TrySetTileType(cell, delta.nextTerrain);
            }

            if (delta.nextBuilding != delta.previousBuilding)
            {
                worldGenerator.TrySetBuildingType(cell, delta.nextBuilding);
            }

            if (delta.ownerChanged)
            {
                long key = PackCoord(delta.q, delta.r);
                string normalizedOwner = string.IsNullOrWhiteSpace(delta.ownerWallet)
                    ? string.Empty
                    : delta.ownerWallet.Trim().ToLowerInvariant();
                ownerByTile[key] = normalizedOwner;

                bool isLocalTile = !string.IsNullOrWhiteSpace(normalizedOwner)
                    && !string.IsNullOrWhiteSpace(activeWalletAddress)
                    && string.Equals(normalizedOwner, activeWalletAddress, StringComparison.OrdinalIgnoreCase);

                if (isLocalTile)
                {
                    ownership.SetOwned(cell, true);
                    ownership.SetRivalOwned(cell, string.Empty);
                }
                else
                {
                    ownership.SetOwned(cell, false);
                    ownership.SetRivalOwned(cell, normalizedOwner);
                }
            }
        }

        ownership.RefreshOverlay();
        // For brand-new players, EnsurePlayer bootstraps territory AFTER the initial
        // snapshot (which had 0 tiles). This is the first time owned tiles appear, so
        // attempt the camera zoom now that the overlay is populated.
        FocusOwnedTerritoryIfAny(true);
    }

    public void HandleCycleStarted(CycleStartedMessage cycle)
    {
        if (viewModel == null)
        {
            return;
        }

        int authoritativeTick = cycle != null ? cycle.tick : -1;
        if (cycle != null && cycle.player != null)
        {
            if (viewModel.WorldState != null)
            {
                viewModel.WorldState.tick = authoritativeTick >= 0 ? authoritativeTick : viewModel.WorldState.tick + 1;
                viewModel.WorldState.globalForestToken = cycle.globalForestToken;
                viewModel.WorldState.globalCarbonToken = cycle.globalCarbonToken;
                viewModel.WorldState.ecosystemScore = GameStateViewModel.ComputeEcosystemScore(
                    viewModel.WorldState.globalForestToken,
                    viewModel.WorldState.globalCarbonToken,
                    viewModel.WorldState.initialForest,
                    viewModel.WorldState.carbonCap);
            }

            if (viewModel.PlayerState != null)
            {
                WorldPlayerSnapshotMessage player = cycle.player;
                viewModel.PlayerState.ownedTilesCount = player.ownedTilesCount;
                viewModel.PlayerState.sustainabilityScore = player.sustainabilityScore;
                viewModel.PlayerState.actionsTaken = player.actionsTaken;
                viewModel.PlayerState.actionsRemaining = player.actionsRemaining;
                viewModel.PlayerState.lastClientSeq = player.lastClientSeq;
                viewModel.PlayerState.resources = new FiniteEarthResourcePool
                {
                    wood = player.wood,
                    food = player.food,
                    minerals = player.minerals
                };
                viewModel.PlayerState.researchPoints = player.researchPoints;
                viewModel.PlayerState.techBasicForestry = player.techBasicForestry;
                viewModel.PlayerState.techRenewableEnergy = player.techRenewableEnergy;
                viewModel.PlayerState.techCarbonCapture = player.techCarbonCapture;
                viewModel.PlayerState.ecoActions = player.ecoActions;
                viewModel.PlayerState.industrialActions = player.industrialActions;
                viewModel.PlayerState.agricultureActions = player.agricultureActions;
                viewModel.PlayerState.reputationLabel = string.IsNullOrWhiteSpace(player.reputation)
                    ? viewModel.PlayerState.reputationLabel
                    : player.reputation;
                viewModel.SyncClientSequence(player.lastClientSeq);
            }

            ApplyServerClimateSnapshot(cycle.climateEvents, authoritativeTick);
            AdvanceTradeOffersForCycle();
            cycleRemainingSeconds = GetCycleDurationSeconds();
            RefreshActionPanelState();
            return;
        }

        viewModel.StartNewCycle(authoritativeTick);
        ApplyPassiveIncomeForCycle();
        AdvanceTradeOffersForCycle();
        ApplyClimateEventsForCycle();
        ApplyCapturePressureForCycle();
        ApplyCarbonCaptureForCycle();
        cycleRemainingSeconds = GetCycleDurationSeconds();
        RefreshActionPanelState();
    }

    public void ResetRealtimePendingState()
    {
        optimisticallyApplied.Clear();
    }

    public void HandleActionCommitted(ActionCommittedMessage committed)
    {
        if (committed == null || string.IsNullOrWhiteSpace(committed.intentId))
        {
            return;
        }

        bool wasOptimistic = optimisticallyApplied.Remove(committed.intentId);
        bool isLocalCommit = !string.IsNullOrWhiteSpace(committed.walletAddress)
            && !string.IsNullOrWhiteSpace(activeWalletAddress)
            && string.Equals(committed.walletAddress.Trim(), activeWalletAddress, StringComparison.OrdinalIgnoreCase);
        TileDelta[] authoritativeTileDeltas = committed.tileDeltas;
        if (!isLocalCommit && authoritativeTileDeltas != null && authoritativeTileDeltas.Length > 0)
        {
            authoritativeTileDeltas = Array.Empty<TileDelta>();
        }

        ActionResolution authoritative = new ActionResolution(
            committed.accepted,
            committed.reason,
            authoritativeTileDeltas,
            committed.playerDelta,
            committed.globalDelta);

        if (!authoritative.accepted)
        {
            if (wasOptimistic)
            {
                // Immediately revert the tile to its server-authoritative state instead of
                // waiting up to 30 s for the next CycleStarted snapshot.
                Debug.LogWarning($"Optimistic action rejected ({committed.reason}); reverting tile ({committed.q},{committed.r}) from STDB.");
                RevertOptimisticTile(committed.q, committed.r);
            }
            RefreshActionPanelState();
            return;
        }

        if (viewModel?.WorldState != null)
        {
            viewModel.WorldState.tick = Mathf.Max(viewModel.WorldState.tick, committed.tick);
        }

        if (!wasOptimistic)
        {
            viewModel.ApplyResolution(authoritative, worldAdapter);
            if (authoritative.tileDeltas != null && authoritative.tileDeltas.Length > 0)
            {
                for (int i = 0; i < authoritative.tileDeltas.Length; i++)
                {
                    TileDelta delta = authoritative.tileDeltas[i];
                    if (!delta.ownerChanged)
                        continue;
                    ownerByTile[PackCoord(delta.q, delta.r)] = string.IsNullOrWhiteSpace(delta.ownerWallet)
                        ? string.Empty
                        : delta.ownerWallet.Trim().ToLowerInvariant();
                }
            }

            if (isLocalCommit)
            {
                FiniteEarthActionType committedActionType = (FiniteEarthActionType)committed.actionType;
                TrackReputation(committedActionType);
                ApplyForestClusterBonus();

                FiniteEarthResourcePool popupDelta = authoritative.playerDelta.resourceDelta;
                if (!popupDelta.IsZero())
                {
                    ResourcePopupRequested?.Invoke(new HexCoord(committed.q, committed.r), popupDelta);
                }

                ActionExecuted?.Invoke(committedActionType, Mathf.Max(1, authoritative.tileDeltas?.Length ?? 1));
            }

            TrackDeforestedTransitions(authoritative);
            TrackRecoveryProjectTransitions(authoritative);
        }

        ownership.RefreshOverlay();
        FocusOwnedTerritoryIfAny(false);
        RefreshActionPanelState();
    }

    public bool ApplyWorldSnapshot(WorldSnapshotMessage snapshot)
    {
        if (snapshot == null || worldGenerator == null || ownership == null || viewModel == null || viewModel.WorldState == null)
        {
            return false;
        }

        if (optimisticallyApplied.Count > 0)
        {
            return false;
        }

        EnsureWorldMatchesSnapshot(snapshot);
        ownership.SetAutomaticSettlementClaimsEnabled(false);

        viewModel.WorldState.tick = snapshot.tick;
        viewModel.WorldState.worldId = string.IsNullOrWhiteSpace(snapshot.worldId) ? viewModel.WorldState.worldId : snapshot.worldId;
        viewModel.WorldState.globalForestToken = snapshot.globalForestToken;
        viewModel.WorldState.globalCarbonToken = snapshot.globalCarbonToken;
        viewModel.WorldState.cycleSeconds = snapshot.cycleSeconds;
        viewModel.WorldState.actionsPerCycle = snapshot.actionsPerCycle;
        if (viewModel.WorldState.initialForest <= 0)
        {
            viewModel.WorldState.initialForest = snapshot.globalForestToken;
        }
        if (viewModel.WorldState.carbonCap <= 0)
        {
            viewModel.WorldState.carbonCap = Mathf.Max(1, Mathf.RoundToInt(snapshot.globalCarbonToken * 1.25f));
        }
        viewModel.WorldState.ecosystemScore = GameStateViewModel.ComputeEcosystemScore(
            viewModel.WorldState.globalForestToken,
            viewModel.WorldState.globalCarbonToken,
            viewModel.WorldState.initialForest,
            viewModel.WorldState.carbonCap);
        cycleRemainingSeconds = Mathf.Min(cycleRemainingSeconds, GetCycleDurationSeconds());

        if (snapshot.players != null && viewModel.PlayerState != null)
        {
            playerLabelByWallet.Clear();
            for (int i = 0; i < snapshot.players.Length; i++)
            {
                WorldPlayerSnapshotMessage player = snapshot.players[i];
                RememberPlayerIdentity(player.walletAddress, player.username, player.displayName);
                if (!string.Equals(player.walletAddress, viewModel.PlayerState.walletAddress, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                viewModel.PlayerState.ownedTilesCount = player.ownedTilesCount;
                viewModel.PlayerState.sustainabilityScore = player.sustainabilityScore;
                viewModel.PlayerState.actionsTaken = player.actionsTaken;
                viewModel.PlayerState.actionsRemaining = player.actionsRemaining;
                viewModel.PlayerState.lastClientSeq = player.lastClientSeq;
                viewModel.PlayerState.resources = new FiniteEarthResourcePool
                {
                    wood = player.wood,
                    food = player.food,
                    minerals = player.minerals
                };
                viewModel.PlayerState.researchPoints = player.researchPoints;
                viewModel.PlayerState.techBasicForestry = player.techBasicForestry;
                viewModel.PlayerState.techRenewableEnergy = player.techRenewableEnergy;
                viewModel.PlayerState.techCarbonCapture = player.techCarbonCapture;
                viewModel.PlayerState.ecoActions = player.ecoActions;
                viewModel.PlayerState.industrialActions = player.industrialActions;
                viewModel.PlayerState.agricultureActions = player.agricultureActions;
                viewModel.PlayerState.reputationLabel = string.IsNullOrWhiteSpace(player.reputation)
                    ? viewModel.PlayerState.reputationLabel
                    : player.reputation;
                viewModel.SyncClientSequence(player.lastClientSeq);
                break;
            }
        }

        if (snapshot.tiles == null || snapshot.tiles.Length == 0)
        {
            return true;
        }

        ownership.ResetOwnership();
        ownership.ClearRivalOwnership();
        ownerByTile.Clear();

        string localWallet = viewModel.PlayerState?.walletAddress?.Trim().ToLowerInvariant() ?? string.Empty;

        for (int i = 0; i < snapshot.tiles.Length; i++)
        {
            WorldTileSnapshotMessage tile = snapshot.tiles[i];
            Vector3Int cell = new Vector3Int(tile.q, tile.r, 0);
            long key = PackCoord(cell.x, cell.y);
            ownerByTile[key] = tile.ownerWallet;

            if (TryParseTileType(tile.currentState, out TileType terrain))
            {
                worldGenerator.TrySetTileType(cell, terrain);
            }

            if (TryParseBuildingType(tile.buildingType, out BuildingType building))
            {
                worldGenerator.TrySetBuildingType(cell, building);
            }

            if (string.IsNullOrWhiteSpace(tile.ownerWallet))
            {
                continue;
            }

            string tileOwner = tile.ownerWallet.Trim().ToLowerInvariant();
            bool isLocalTile = !string.IsNullOrWhiteSpace(localWallet)
                && string.Equals(tileOwner, localWallet, StringComparison.OrdinalIgnoreCase);

            if (isLocalTile)
            {
                ownership.SetOwned(cell, true);
            }
            else
            {
                ownership.SetRivalOwned(cell, tileOwner);
            }
        }

        RebuildDeforestedRegistryFromWorld(snapshot.tick);
        RebuildRecoveryProjectRegistryFromWorld(snapshot.tick);
        ApplyServerClimateSnapshot(snapshot.climateEvents, snapshot.tick);
        ownership.RefreshOverlay();
        FocusOwnedTerritoryIfAny(true);
        RefreshActionPanelState();
        return true;
    }

    private void EnsureWorldMatchesSnapshot(WorldSnapshotMessage snapshot)
    {
        if (snapshot?.tiles == null || snapshot.tiles.Length == 0 || worldGenerator == null || ownership == null)
        {
            return;
        }

        int requiredWidth = 0;
        int requiredHeight = 0;
        for (int i = 0; i < snapshot.tiles.Length; i++)
        {
            WorldTileSnapshotMessage tile = snapshot.tiles[i];
            requiredWidth = Mathf.Max(requiredWidth, tile.q + 1);
            requiredHeight = Mathf.Max(requiredHeight, tile.r + 1);
        }

        if (requiredWidth <= 0 || requiredHeight <= 0)
        {
            return;
        }

        bool needsReload = worldGenerator.Width != requiredWidth
            || worldGenerator.Height != requiredHeight
            || worldGenerator.UsesProceduralDefaultWorld;
        if (!needsReload)
        {
            // Even without a full reload, always reset focus so ApplyWorldSnapshot
            // will re-evaluate and zoom to owned territory.
            hasFocusedOwnedArea = false;
            return;
        }

        var terrainMap = new TileType[requiredWidth, requiredHeight];
        var buildingMap = new BuildingType[requiredWidth, requiredHeight];

        for (int y = 0; y < requiredHeight; y++)
        {
            for (int x = 0; x < requiredWidth; x++)
            {
                terrainMap[x, y] = TileType.Plains;
                buildingMap[x, y] = BuildingType.None;
            }
        }

        for (int i = 0; i < snapshot.tiles.Length; i++)
        {
            WorldTileSnapshotMessage tile = snapshot.tiles[i];
            if (tile.q < 0 || tile.q >= requiredWidth || tile.r < 0 || tile.r >= requiredHeight)
            {
                continue;
            }

            terrainMap[tile.q, tile.r] = TryParseTileType(tile.currentState, out TileType terrain)
                ? terrain
                : TileType.Plains;
            buildingMap[tile.q, tile.r] = TryParseBuildingType(tile.buildingType, out BuildingType building)
                ? building
                : BuildingType.None;
        }

        worldGenerator.LoadSnapshotWorld(terrainMap, buildingMap);
        ownership.Initialize(worldGenerator);
        ownership.SetAutomaticSettlementClaimsEnabled(false);
        hasFocusedOwnedArea = false;
        ClearCurrentSelection();

        worldCameraController?.ReframeToWorld();

        if (worldAdapter == null)
        {
            worldAdapter = new UnityWorldAdapter(worldGenerator, ownership);
        }
        worldAdapter.SetLocalWalletAddress(activeWalletAddress);

        if (viewModel?.WorldState != null)
        {
            viewModel.WorldState.query = worldAdapter;
            viewModel.WorldState.settlementRadius = worldGenerator.SettlementRadius;
        }
    }

    private void ApplyServerClimateSnapshot(ClimateEventSnapshotMessage[] snapshotEvents, int tick)
    {
        if (UsesLocalCycleClock)
        {
            return;
        }

        activeEvents.Clear();
        climateTileHighlights.Clear();
        activeWildfirePatches.Clear();

        var nextIds = new HashSet<long>();
        if (snapshotEvents == null || snapshotEvents.Length == 0)
        {
            activeServerClimateEventIds.Clear();
            return;
        }

        for (int i = 0; i < snapshotEvents.Length; i++)
        {
            ClimateEventSnapshotMessage message = snapshotEvents[i];

            // Skip events that have already expired — the table keeps historical rows.
            if (message.endTick > 0 && message.endTick <= tick)
                continue;

            if (!TryParseClimateEventType(message.type, out ClimateEventType climateType))
            {
                continue;
            }

            nextIds.Add(message.id);
            activeEvents.Add(new ClimateEventInstance
            {
                type = climateType,
                startTick = message.startTick,
                endTick = message.endTick
            });

            List<Vector3Int> highlightCells = GetServerClimateHighlightCells(climateType);
            if (highlightCells.Count > 0)
            {
                RegisterClimateTileHighlights(climateType, highlightCells, message.endTick);
            }

            if (!activeServerClimateEventIds.Contains(message.id))
            {
                ClimateEventTriggered?.Invoke(climateType);
            }
        }

        activeServerClimateEventIds.Clear();
        foreach (long id in nextIds)
        {
            activeServerClimateEventIds.Add(id);
        }

        PruneExpiredClimateTileHighlights(tick);
    }

    private List<Vector3Int> GetServerClimateHighlightCells(ClimateEventType climateType)
    {
        switch (climateType)
        {
            case ClimateEventType.Heatwave:
                return CollectTerrainCells(TileType.Farmland);
            case ClimateEventType.Wildfire:
                return CollectTerrainCells(TileType.Forest);
            case ClimateEventType.Flood:
                return CollectCellsAdjacentToTerrain(TileType.Water);
            case ClimateEventType.IceMelt:
                return CollectTerrainCells(TileType.Ice);
            case ClimateEventType.DesertSpread:
                return CollectTerrainCells(TileType.Plains, TileType.Forest, TileType.Farmland);
            default:
                return new List<Vector3Int>();
        }
    }

    private float GetCycleDurationSeconds()
    {
        if (viewModel == null || viewModel.WorldState == null)
        {
            return 30f;
        }

        return Mathf.Max(1f, viewModel.WorldState.cycleSeconds);
    }

    private void RefreshActionPanelState()
    {
        if (resolver == null || viewModel == null || viewModel.WorldState == null || viewModel.PlayerState == null)
        {
            return;
        }

        lastActionStates.Clear();
        int selectionCount = Mathf.Max(0, selectedCoords.Count);
        if (hasSelection && selectionCount == 0)
        {
            selectionCount = 1;
        }

        int previewCount = hasSelection ? selectionCount : 0;
        bool selectionReady = false;
        string selectionReason = hasSelection ? "Select an action." : "Select a tile.";

        for (int i = 0; i < UiActions.Length; i++)
        {
            FiniteEarthActionType action = UiActions[i];
            if (!ActionCatalog.TryGet(action, out ActionRuleSpec spec))
            {
                continue;
            }

            bool actionable = false;
            bool affordableForSelection = false;
            int applicableSelectionCount = 0;
            int affordableSelectionCount = 0;
            string reason = hasSelection ? "Unavailable" : "Select a tile.";

            if (hasSelection)
            {
                string firstBlockedReason = string.Empty;
                for (int selectionIndex = 0; selectionIndex < previewCount; selectionIndex++)
                {
                    HexCoord previewCoord = selectedCoords.Count > 0
                        ? selectedCoords[Mathf.Min(selectionIndex, selectedCoords.Count - 1)]
                        : selectedCoord;

                    ActionIntent previewIntent = BuildPreviewIntent(action, previewCoord);
                    ActionResolution preview = resolver.Resolve(
                        previewIntent,
                        viewModel.WorldState,
                        viewModel.PlayerState,
                        viewModel.WorldState.tick);

                    bool accepted = preview.accepted;
                    string blockedReason = preview.reason;

                    if (accepted && action == FiniteEarthActionType.Claim && IsClaimBlockedByPact(previewCoord, out string pactReason))
                    {
                        accepted = false;
                        blockedReason = pactReason;
                    }

                    if (accepted && action == FiniteEarthActionType.SpawnArmy && !CanSpawnArmyAt(previewCoord))
                    {
                        accepted = false;
                        blockedReason = "Army cap reached or invalid spawn tile.";
                    }

                    if (accepted)
                    {
                        applicableSelectionCount++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(firstBlockedReason) && !string.IsNullOrWhiteSpace(blockedReason))
                    {
                        firstBlockedReason = blockedReason;
                    }
                }

                affordableSelectionCount = ComputeAffordableSelectionCount(
                    action,
                    viewModel.PlayerState.resources,
                    applicableSelectionCount);
                actionable = applicableSelectionCount > 0;
                FiniteEarthResourcePool totalSelectionCost = GetPreviewSelectionCost(action, applicableSelectionCount);
                affordableForSelection = viewModel.PlayerState.resources.CanAfford(totalSelectionCost);

                if (applicableSelectionCount == 0)
                {
                    reason = string.IsNullOrWhiteSpace(firstBlockedReason)
                        ? "Unavailable for selected tiles."
                        : firstBlockedReason;
                }
                else if (!affordableForSelection)
                {
                    FiniteEarthResourcePool available = viewModel.PlayerState.resources;
                    reason =
                        $"Need W{totalSelectionCost.wood} F{totalSelectionCost.food} O{totalSelectionCost.minerals} " +
                        $"| Have W{available.wood} F{available.food} O{available.minerals}.";
                }
                else if (affordableSelectionCount < applicableSelectionCount)
                {
                    reason = $"Affordable now: {affordableSelectionCount}/{applicableSelectionCount} tiles.";
                }
                else if (applicableSelectionCount < previewCount)
                {
                    reason = $"Valid tiles: {applicableSelectionCount}/{previewCount}.";
                }
                else
                {
                    reason = action == FiniteEarthActionType.RemoveBuilding
                        ? BuildRemovalRefundReason(previewCoord: selectedCoords.Count > 0 ? selectedCoords[0] : selectedCoord, applicableSelectionCount)
                        : "Ready.";
                }
            }

            if (actionable && affordableForSelection)
            {
                selectionReady = true;
            }
            else if (!selectionReady && hasSelection && !string.IsNullOrWhiteSpace(reason) && !string.Equals(reason, "Ready.", StringComparison.OrdinalIgnoreCase))
            {
                selectionReason = reason;
            }

            FiniteEarthResourcePool totalCost = GetPreviewSelectionCost(action, applicableSelectionCount);
            if (actionPanel != null)
            {
                actionPanel.SetActionState(
                    action,
                    hasSelection,
                    actionable && affordableForSelection,
                    applicableSelectionCount,
                    affordableSelectionCount,
                    totalCost,
                    viewModel.PlayerState.resources,
                    reason);
            }

            lastActionStates.Add(new ActionAvailability(
                action,
                hasSelection,
                applicableSelectionCount > 0,
                affordableForSelection,
                actionable && affordableForSelection,
                applicableSelectionCount,
                affordableSelectionCount,
                totalCost,
                reason));
        }

        if (actionPanel != null)
        {
            actionPanel.SetSelectionContext(hasSelection, selectedCoord, selectionReady, selectionReason, selectionCount);
        }
    }

    private static FiniteEarthResourcePool ScaleCost(FiniteEarthResourcePool baseCost, int multiplier)
    {
        int safeMultiplier = Mathf.Max(0, multiplier);
        return new FiniteEarthResourcePool
        {
            wood = baseCost.wood * safeMultiplier,
            food = baseCost.food * safeMultiplier,
            minerals = baseCost.minerals * safeMultiplier
        };
    }

    private FiniteEarthResourcePool GetPreviewSelectionCost(FiniteEarthActionType actionType, int applicableSelectionCount)
    {
        if (applicableSelectionCount <= 0 || !ActionCatalog.TryGet(actionType, out ActionRuleSpec spec))
        {
            return default;
        }

        if (actionType != FiniteEarthActionType.BuildSettlement)
        {
            return ScaleCost(spec.cost, applicableSelectionCount);
        }

        int currentSettlementCount = CountOwnedSettlements();
        FiniteEarthResourcePool total = default;
        for (int i = 0; i < applicableSelectionCount; i++)
        {
            total.wood += spec.cost.wood;
            total.food += spec.cost.food + ((currentSettlementCount + i) * 2);
            total.minerals += spec.cost.minerals;
        }

        return total;
    }

    private string BuildRemovalRefundReason(HexCoord previewCoord, int applicableSelectionCount)
    {
        if (worldGenerator == null)
        {
            return "Ready.";
        }

        FiniteEarthResourcePool refund = default;
        int targetCount = Mathf.Max(1, applicableSelectionCount);
        if (selectedCoords.Count > 1)
        {
            for (int i = 0; i < selectedCoords.Count; i++)
            {
                refund.Add(GetRemovalRefundForCoord(selectedCoords[i]));
            }
        }
        else
        {
            refund = GetRemovalRefundForCoord(previewCoord);
        }

        if (refund.IsZero())
        {
            return "Ready.";
        }

        return targetCount > 1
            ? $"Refund total: W{refund.wood} F{refund.food} O{refund.minerals}."
            : $"Refund: W{refund.wood} F{refund.food} O{refund.minerals}.";
    }

    private FiniteEarthResourcePool GetRemovalRefundForCoord(HexCoord coord)
    {
        if (worldGenerator == null || !worldGenerator.TryGetBuildingType(coord.ToVector3Int(), out BuildingType building))
        {
            return default;
        }

        return building switch
        {
            BuildingType.Industry => new FiniteEarthResourcePool { minerals = 1 },
            _ => default
        };
    }

    private int ComputeAffordableSelectionCount(
        FiniteEarthActionType actionType,
        FiniteEarthResourcePool available,
        int applicableSelectionCount)
    {
        if (applicableSelectionCount <= 0)
        {
            return 0;
        }

        if (!ActionCatalog.TryGet(actionType, out ActionRuleSpec spec))
        {
            return 0;
        }

        if (actionType == FiniteEarthActionType.BuildSettlement)
        {
            int currentSettlementCount = CountOwnedSettlements();
            FiniteEarthResourcePool spent = default;
            int affordable = 0;
            for (int i = 0; i < applicableSelectionCount; i++)
            {
                spent.wood += spec.cost.wood;
                spent.food += spec.cost.food + ((currentSettlementCount + i) * 2);
                spent.minerals += spec.cost.minerals;
                if (!available.CanAfford(spent))
                {
                    break;
                }

                affordable++;
            }

            return affordable;
        }

        FiniteEarthResourcePool unitCost = spec.cost;
        int byWood = unitCost.wood <= 0 ? int.MaxValue : available.wood / unitCost.wood;
        int byFood = unitCost.food <= 0 ? int.MaxValue : available.food / unitCost.food;
        int byMinerals = unitCost.minerals <= 0 ? int.MaxValue : available.minerals / unitCost.minerals;

        int maxAffordable = Math.Min(byWood, Math.Min(byFood, byMinerals));
        if (maxAffordable == int.MaxValue)
        {
            maxAffordable = applicableSelectionCount;
        }

        return Mathf.Clamp(maxAffordable, 0, applicableSelectionCount);
    }

    private ActionIntent BuildPreviewIntent(FiniteEarthActionType actionType, HexCoord coord)
    {
        string wallet = viewModel != null && viewModel.PlayerState != null
            ? viewModel.PlayerState.walletAddress
            : "local-player";

        string worldId = viewModel != null && viewModel.WorldState != null
            ? viewModel.WorldState.worldId
            : "finite-earth-alpha";

        long issuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string previewId = $"preview-{wallet}-{actionType}-{coord.q}-{coord.r}";

        return new ActionIntent(
            previewId,
            worldId,
            wallet,
            0,
            actionType,
            coord.q,
            coord.r,
            BuildingType.None,
            issuedAt);
    }

    private static bool TryParseTileType(string raw, out TileType tileType)
    {
        return Enum.TryParse(raw, true, out tileType);
    }

    private static bool TryParseBuildingType(string raw, out BuildingType buildingType)
    {
        return Enum.TryParse(raw, true, out buildingType);
    }

    private void TrackDeforestedTransitions(ActionResolution resolution)
    {
        if (!resolution.accepted || resolution.tileDeltas == null || resolution.tileDeltas.Length == 0 || viewModel?.WorldState == null)
        {
            return;
        }

        int currentTick = viewModel.WorldState.tick;
        for (int i = 0; i < resolution.tileDeltas.Length; i++)
        {
            TileDelta delta = resolution.tileDeltas[i];
            long key = PackCoord(delta.q, delta.r);
            if (delta.nextTerrain == TileType.DeforestedForest)
            {
                int stampedTick = delta.lastUpdatedTick > 0 ? delta.lastUpdatedTick : currentTick;
                deforestedSinceTick[key] = stampedTick;
                continue;
            }

            if (delta.previousTerrain == TileType.DeforestedForest)
            {
                deforestedSinceTick.Remove(key);
            }
        }
    }

    private void TrackRecoveryProjectTransitions(ActionResolution resolution)
    {
        if (!resolution.accepted || resolution.tileDeltas == null || resolution.tileDeltas.Length == 0 || viewModel?.WorldState == null)
        {
            return;
        }

        int minimumDuration = Mathf.Max(1, recoveryProjectCycles);
        int currentTick = viewModel.WorldState.tick;
        for (int i = 0; i < resolution.tileDeltas.Length; i++)
        {
            TileDelta delta = resolution.tileDeltas[i];
            long key = PackCoord(delta.q, delta.r);
            if (delta.nextBuilding == BuildingType.RecoveryProject)
            {
                int stampedTick = delta.lastUpdatedTick > 0 ? delta.lastUpdatedTick : currentTick;
                recoveryProjectUntilTick[key] = stampedTick + minimumDuration;
                continue;
            }

            if (delta.previousBuilding == BuildingType.RecoveryProject)
            {
                recoveryProjectUntilTick.Remove(key);
            }
        }
    }

    private void ApplyNaturalRecoveryForCurrentCycle()
    {
        if (viewModel?.WorldState == null || worldAdapter == null || worldGenerator == null || deforestedToPlainsCycles <= 0)
        {
            return;
        }

        int tick = viewModel.WorldState.tick;
        var recoverNow = new List<TileDelta>();
        var removeKeys = new List<long>();

        foreach (KeyValuePair<long, int> pair in deforestedSinceTick)
        {
            int q = (int)(pair.Key >> 32);
            int r = (int)(pair.Key & 0xFFFFFFFF);
            HexCoord coord = new HexCoord(q, r);

            if (!worldAdapter.TryGetTileType(coord, out TileType terrain) || terrain != TileType.DeforestedForest)
            {
                removeKeys.Add(pair.Key);
                continue;
            }

            if (tick - pair.Value < deforestedToPlainsCycles)
            {
                continue;
            }

            worldAdapter.TryGetBuildingType(coord, out BuildingType building);
            recoverNow.Add(new TileDelta(
                q,
                r,
                TileType.DeforestedForest,
                TileType.Plains,
                building,
                building,
                false,
                string.Empty,
                tick));
            removeKeys.Add(pair.Key);
        }

        for (int i = 0; i < removeKeys.Count; i++)
        {
            deforestedSinceTick.Remove(removeKeys[i]);
        }

        if (recoverNow.Count == 0)
        {
            return;
        }

        int carbonDelta = (TileType.Plains.GetCarbonValue() - TileType.DeforestedForest.GetCarbonValue()) * recoverNow.Count;
        ActionResolution recoveryResolution = new ActionResolution(
            true,
            "Natural recovery advanced.",
            recoverNow.ToArray(),
            new PlayerDelta(string.Empty, 0, 0, 0, 0, default),
            new GlobalDelta(0, carbonDelta, 0));

        viewModel.ApplyResolution(recoveryResolution, worldAdapter);
        ownership.RefreshOverlay();
    }

    private void ApplyRecoveryProjectDecayForCurrentCycle()
    {
        if (viewModel?.WorldState == null || worldAdapter == null || worldGenerator == null || recoveryProjectUntilTick.Count == 0)
        {
            return;
        }

        int tick = viewModel.WorldState.tick;
        var clearNow = new List<TileDelta>();
        var removeKeys = new List<long>();
        int carbonDelta = 0;

        foreach (KeyValuePair<long, int> pair in recoveryProjectUntilTick)
        {
            if (tick < pair.Value)
            {
                continue;
            }

            int q = (int)(pair.Key >> 32);
            int r = (int)(pair.Key & 0xFFFFFFFF);
            HexCoord coord = new HexCoord(q, r);
            if (!worldAdapter.TryGetBuildingType(coord, out BuildingType building) || building != BuildingType.RecoveryProject)
            {
                removeKeys.Add(pair.Key);
                continue;
            }

            if (!worldAdapter.TryGetTileType(coord, out TileType terrain))
            {
                removeKeys.Add(pair.Key);
                continue;
            }

            clearNow.Add(new TileDelta(
                q,
                r,
                terrain,
                terrain,
                BuildingType.RecoveryProject,
                BuildingType.None,
                false,
                string.Empty,
                tick));

            int beforeCarbon = terrain.GetCarbonValue() + BuildingType.RecoveryProject.GetCarbonModifier();
            int afterCarbon = terrain.GetCarbonValue() + BuildingType.None.GetCarbonModifier();
            carbonDelta += afterCarbon - beforeCarbon;
            removeKeys.Add(pair.Key);
        }

        for (int i = 0; i < removeKeys.Count; i++)
        {
            recoveryProjectUntilTick.Remove(removeKeys[i]);
        }

        if (clearNow.Count == 0)
        {
            return;
        }

        ActionResolution recoveryDecayResolution = new ActionResolution(
            true,
            "Recovery projects completed.",
            clearNow.ToArray(),
            new PlayerDelta(string.Empty, 0, 0, 0, 0, default),
            new GlobalDelta(0, carbonDelta, 0));

        viewModel.ApplyResolution(recoveryDecayResolution, worldAdapter);
        ownership.RefreshOverlay();
    }

    private void ApplyPassiveIncomeForCycle()
    {
        if (viewModel?.PlayerState == null || worldGenerator == null || ownership == null)
        {
            return;
        }

        CalculatePassiveIncomePreview(out float foodGain, out float mineralGain, out _, out _);

        foodGain += passiveFoodRemainder;
        mineralGain += passiveMineralRemainder;

        int foodInt = Mathf.FloorToInt(Mathf.Max(0f, foodGain));
        int mineralInt = Mathf.FloorToInt(Mathf.Max(0f, mineralGain));
        passiveFoodRemainder = Mathf.Max(0f, foodGain - foodInt);
        passiveMineralRemainder = Mathf.Max(0f, mineralGain - mineralInt);

        if (foodInt != 0 || mineralInt != 0)
        {
            viewModel.PlayerState.resources.Add(new FiniteEarthResourcePool
            {
                food = Mathf.Max(0, foodInt),
                minerals = Mathf.Max(0, mineralInt)
            });
        }

        viewModel.PlayerState.researchPoints += 1;
        ApplyResourcePactShare(foodInt, mineralInt);
    }

    private void CalculatePassiveIncomePreview(out float foodGain, out float mineralGain, out float foodModifierPercent, out float mineralsModifierPercent)
    {
        foodGain = 0f;
        mineralGain = 0f;
        foodModifierPercent = 0f;
        mineralsModifierPercent = 0f;

        if (worldGenerator == null || ownership == null)
        {
            return;
        }

        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (!ownership.IsOwned(cell))
            {
                continue;
            }

            worldGenerator.TryGetTileType(cell, out TileType terrain);
            worldGenerator.TryGetBuildingType(cell, out BuildingType building);

            if (terrain == TileType.Farmland)
            {
                foodGain += Mathf.Max(0f, farmFoodPerCycle);
            }

            if (building == BuildingType.Industry)
            {
                mineralGain += Mathf.Max(0f, industryMineralsPerCycle);
            }
        }

        if (IsHeatwaveActive())
        {
            foodModifierPercent = -Mathf.Abs(heatwaveFoodPenalty) * 100f;
            foodGain *= Mathf.Clamp01(1f - heatwaveFoodPenalty);
        }
    }

    private void ApplyResourcePactShare(int foodGain, int mineralGain)
    {
        if (diplomacyPacts.Count == 0 || viewModel?.PlayerState == null)
        {
            return;
        }

        var active = diplomacyPacts.FindAll(pact =>
            pact.status == DiplomacyPactStatus.Active
            && pact.type == DiplomacyPactType.ResourceShare
            && (IsLocalWallet(pact.walletA) || IsLocalWallet(pact.walletB)));

        if (active.Count == 0)
        {
            return;
        }

        int sharedFoodTotal = Mathf.FloorToInt(foodGain * 0.20f);
        int sharedMineralsTotal = Mathf.FloorToInt(mineralGain * 0.20f);
        if (sharedFoodTotal <= 0 && sharedMineralsTotal <= 0)
        {
            return;
        }

        FiniteEarthResourcePool available = viewModel.PlayerState.resources;
        sharedFoodTotal = Mathf.Min(sharedFoodTotal, available.food);
        sharedMineralsTotal = Mathf.Min(sharedMineralsTotal, available.minerals);
        if (sharedFoodTotal <= 0 && sharedMineralsTotal <= 0)
        {
            return;
        }

        pactShareLedger.Clear();

        int foodPerPact = active.Count > 0 ? sharedFoodTotal / active.Count : 0;
        int foodRemainder = active.Count > 0 ? sharedFoodTotal % active.Count : 0;
        int mineralPerPact = active.Count > 0 ? sharedMineralsTotal / active.Count : 0;
        int mineralRemainder = active.Count > 0 ? sharedMineralsTotal % active.Count : 0;

        for (int i = 0; i < active.Count; i++)
        {
            int foodShare = foodPerPact + (foodRemainder > 0 ? 1 : 0);
            int mineralShare = mineralPerPact + (mineralRemainder > 0 ? 1 : 0);
            if (foodRemainder > 0) foodRemainder--;
            if (mineralRemainder > 0) mineralRemainder--;

            pactShareLedger[active[i].id] = new FiniteEarthResourcePool
            {
                food = foodShare,
                minerals = mineralShare
            };
        }

        viewModel.PlayerState.resources.Spend(new FiniteEarthResourcePool
        {
            food = sharedFoodTotal,
            minerals = sharedMineralsTotal
        });
    }

    private void AdvanceTradeOffersForCycle()
    {
        if (tradeOffers.Count == 0 || viewModel?.WorldState == null)
        {
            return;
        }

        int tick = viewModel.WorldState.tick;
        for (int i = 0; i < tradeOffers.Count; i++)
        {
            TradeOffer offer = tradeOffers[i];
            if (offer == null || offer.status != TradeOfferStatus.Open)
            {
                continue;
            }

            if (tick >= offer.expiresTick)
            {
                offer.status = TradeOfferStatus.Expired;
                if (IsLocalWallet(offer.ownerWallet))
                {
                    RefundTradeOffer(offer);
                }
            }
        }
    }

    private void ApplyClimateEventsForCycle()
    {
        if (!UsesLocalCycleClock || viewModel?.WorldState == null || worldGenerator == null)
        {
            return;
        }

        int tick = viewModel.WorldState.tick;
        AdvanceWildfirePatches(tick);
        activeEvents.RemoveAll(evt => tick >= evt.endTick);
        PruneExpiredClimateTileHighlights(tick);
        PruneExpiredWildfirePatches(tick);

        float carbonRatio = viewModel.WorldState.carbonCap > 0
            ? viewModel.WorldState.globalCarbonToken / (float)viewModel.WorldState.carbonCap
            : 0f;
        float forestRatio = viewModel.WorldState.initialForest > 0
            ? viewModel.WorldState.globalForestToken / (float)viewModel.WorldState.initialForest
            : 0f;

        var candidates = new List<(ClimateEventType type, int severity, float chance)>();

        int carbonTier = GetTier(carbonRatio, carbonTierOne, carbonTierTwo, carbonTierThree);
        int forestTier = GetInverseTier(forestRatio, forestTierOne, forestTierTwo);

        if (carbonTier >= 1) candidates.Add((ClimateEventType.Heatwave, carbonTier, ChanceForTier(carbonTier)));
        if (carbonTier >= 2) candidates.Add((ClimateEventType.Wildfire, carbonTier, ChanceForTier(carbonTier)));
        if (carbonTier >= 1) candidates.Add((ClimateEventType.Flood, carbonTier, ChanceForTier(carbonTier)));
        if (carbonTier >= 2) candidates.Add((ClimateEventType.IceMelt, carbonTier, ChanceForTier(carbonTier)));
        if (forestTier >= 1) candidates.Add((ClimateEventType.DesertSpread, forestTier, ChanceForTier(forestTier)));

        var triggered = new List<(ClimateEventType type, int severity)>();

        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (UnityEngine.Random.value <= candidate.chance)
            {
                triggered.Add((candidate.type, candidate.severity));
            }
        }

        if (triggered.Count == 0)
        {
            return;
        }

        triggered.Sort((a, b) => b.severity.CompareTo(a.severity));
        if (triggered.Count > 2)
        {
            triggered.RemoveRange(2, triggered.Count - 2);
        }

        for (int i = 0; i < triggered.Count; i++)
        {
            ApplyClimateEvent(triggered[i].type, tick);
        }
    }

    private static int GetTier(float ratio, float tierOne, float tierTwo, float tierThree)
    {
        if (ratio >= tierThree) return 3;
        if (ratio >= tierTwo) return 2;
        if (ratio >= tierOne) return 1;
        return 0;
    }

    private static int GetInverseTier(float ratio, float tierOne, float tierTwo)
    {
        if (ratio <= tierTwo) return 3;
        if (ratio <= tierOne) return 2;
        return 0;
    }

    private static float ChanceForTier(int tier)
    {
        switch (tier)
        {
            case 3: return 0.70f;
            case 2: return 0.50f;
            case 1: return 0.30f;
            default: return 0f;
        }
    }

    private static bool TryParseClimateEventType(int rawType, out ClimateEventType climateType)
    {
        switch (rawType)
        {
            case 0:
                climateType = ClimateEventType.Heatwave;
                return true;
            case 1:
                climateType = ClimateEventType.Wildfire;
                return true;
            case 2:
                climateType = ClimateEventType.Flood;
                return true;
            case 3:
                climateType = ClimateEventType.IceMelt;
                return true;
            case 4:
                climateType = ClimateEventType.DesertSpread;
                return true;
            default:
                climateType = default;
                return false;
        }
    }

    private void ApplyClimateEvent(ClimateEventType type, int tick)
    {
        List<Vector3Int> highlightedCells = null;
        int endTick = tick + 1;

        switch (type)
        {
            case ClimateEventType.Heatwave:
                endTick = tick + Mathf.Max(1, heatwaveDurationCycles);
                activeEvents.Add(new ClimateEventInstance
                {
                    type = type,
                    startTick = tick,
                    endTick = endTick
                });
                highlightedCells = CollectTerrainCells(TileType.Farmland);
                break;
            case ClimateEventType.Wildfire:
                endTick = tick + Mathf.Max(2, wildfireBarrenDelayCycles);
                activeEvents.Add(new ClimateEventInstance
                {
                    type = type,
                    startTick = tick,
                    endTick = endTick
                });
                highlightedCells = IgniteWildfire(endTick);
                break;
            case ClimateEventType.Flood:
                activeEvents.Add(new ClimateEventInstance
                {
                    type = type,
                    startTick = tick,
                    endTick = endTick
                });
                highlightedCells = ApplyFlood();
                ApplyFloodWoodRotPenalty();
                break;
            case ClimateEventType.IceMelt:
                activeEvents.Add(new ClimateEventInstance
                {
                    type = type,
                    startTick = tick,
                    endTick = endTick
                });
                highlightedCells = ApplyIceMelt();
                break;
            case ClimateEventType.DesertSpread:
                activeEvents.Add(new ClimateEventInstance
                {
                    type = type,
                    startTick = tick,
                    endTick = endTick
                });
                highlightedCells = ApplyDesertSpread();
                break;
        }

        RegisterClimateTileHighlights(type, highlightedCells, endTick);
        ClimateEventTriggered?.Invoke(type);
    }

    private bool IsHeatwaveActive()
    {
        if (viewModel?.WorldState == null)
        {
            return false;
        }

        int tick = viewModel.WorldState.tick;
        for (int i = 0; i < activeEvents.Count; i++)
        {
            ClimateEventInstance evt = activeEvents[i];
            if (evt.type == ClimateEventType.Heatwave && tick < evt.endTick)
            {
                return true;
            }
        }

        return false;
    }

    private List<Vector3Int> IgniteWildfire(int endTick)
    {
        int count = UnityEngine.Random.Range(wildfireTilesRange.x, wildfireTilesRange.y + 1);
        List<Vector3Int> patchTargets = BuildWildfirePatchTargetsNearPlayer(count);
        if (patchTargets.Count == 0)
        {
            return patchTargets;
        }

        int currentTick = viewModel?.WorldState != null ? viewModel.WorldState.tick : 0;
        int deforestedTick = currentTick + Mathf.Max(1, wildfireDeforestedDelayCycles);
        int barrenTick = currentTick + Mathf.Max(wildfireBarrenDelayCycles, wildfireDeforestedDelayCycles + 1);

        for (int i = 0; i < patchTargets.Count; i++)
        {
            Vector3Int cell = patchTargets[i];
            long key = PackCoord(cell.x, cell.y);
            activeWildfirePatches[key] = new WildfirePatchState
            {
                coord = HexCoord.FromVector3Int(cell),
                deforestedTick = deforestedTick,
                barrenTick = barrenTick,
                endTick = endTick
            };
        }

        return patchTargets;
    }

    private List<Vector3Int> ApplyFlood()
    {
        int count = UnityEngine.Random.Range(floodTilesRange.x, floodTilesRange.y + 1);
        return ApplyRandomAdjacentTerrainChange(TileType.Water, TileType.Water, count);
    }

    private void ApplyFloodWoodRotPenalty()
    {
        if (viewModel?.PlayerState == null)
        {
            return;
        }

        int currentWood = Mathf.Max(0, viewModel.PlayerState.resources.wood);
        if (currentWood <= 0 || floodWoodRotFraction <= 0f)
        {
            return;
        }

        int woodLoss = Mathf.Clamp(Mathf.CeilToInt(currentWood * floodWoodRotFraction), 1, currentWood);
        var loss = new FiniteEarthResourcePool { wood = woodLoss };
        viewModel.PlayerState.resources.Spend(loss);
        RefreshActionPanelState();
        ResourcePopupRequested?.Invoke(ResolveCheatPopupCoord(), new FiniteEarthResourcePool { wood = -woodLoss });
    }

    private List<Vector3Int> ApplyIceMelt()
    {
        int count = UnityEngine.Random.Range(iceMeltTilesRange.x, iceMeltTilesRange.y + 1);
        return ApplyRandomTerrainChange(TileType.Ice, TileType.Water, count);
    }

    private List<Vector3Int> ApplyDesertSpread()
    {
        int count = UnityEngine.Random.Range(desertSpreadRange.x, desertSpreadRange.y + 1);
        return ApplyRandomTerrainChange(TileType.Plains, TileType.Desert, count, includeForests: true, includeFarmland: true);
    }

    private List<Vector3Int> ApplyRandomAdjacentTerrainChange(TileType requiredNeighbor, TileType nextTerrain, int count)
    {
        if (worldGenerator == null)
        {
            return new List<Vector3Int>();
        }

        var candidates = new List<Vector3Int>();
        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (!worldGenerator.TryGetTileType(cell, out TileType terrain))
            {
                continue;
            }

            if (terrain == TileType.Water || terrain == TileType.Ice || terrain == TileType.Mountain)
            {
                continue;
            }

            if (worldGenerator.HasAdjacentTerrainType(cell, requiredNeighbor))
            {
                candidates.Add(cell);
            }
        }

        return ApplyTerrainChangesFromCandidates(candidates, nextTerrain, count);
    }

    private List<Vector3Int> ApplyRandomTerrainChange(TileType requiredTerrain, TileType nextTerrain, int count, bool includeForests = false, bool includeFarmland = false)
    {
        if (worldGenerator == null)
        {
            return new List<Vector3Int>();
        }

        var candidates = new List<Vector3Int>();
        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (!worldGenerator.TryGetTileType(cell, out TileType terrain))
            {
                continue;
            }

            if (terrain == requiredTerrain
                || (includeForests && terrain == TileType.Forest)
                || (includeFarmland && terrain == TileType.Farmland))
            {
                candidates.Add(cell);
            }
        }

        return ApplyTerrainChangesFromCandidates(candidates, nextTerrain, count);
    }

    private List<Vector3Int> ApplyTerrainChangesFromCandidates(List<Vector3Int> candidates, TileType nextTerrain, int count)
    {
        if (candidates == null || candidates.Count == 0 || viewModel?.WorldState == null)
        {
            return new List<Vector3Int>();
        }

        var deltas = new List<TileDelta>();
        var affectedCells = new List<Vector3Int>();
        int applied = 0;
        while (applied < count && candidates.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, candidates.Count);
            Vector3Int cell = candidates[index];
            candidates.RemoveAt(index);

            if (!worldGenerator.TryGetTileType(cell, out TileType terrain))
            {
                continue;
            }

            if (terrain == nextTerrain)
            {
                continue;
            }

            worldGenerator.TryGetBuildingType(cell, out BuildingType building);
            deltas.Add(new TileDelta(
                cell.x,
                cell.y,
                terrain,
                nextTerrain,
                building,
                building,
                false,
                string.Empty,
                viewModel.WorldState.tick));
            affectedCells.Add(cell);
            applied++;
        }

        if (deltas.Count == 0)
        {
            return affectedCells;
        }

        int carbonDelta = 0;
        int forestDelta = 0;
        for (int i = 0; i < deltas.Count; i++)
        {
            TileDelta delta = deltas[i];
            carbonDelta += delta.nextTerrain.GetCarbonValue() - delta.previousTerrain.GetCarbonValue();
            forestDelta += (delta.nextTerrain == TileType.Forest ? 1 : 0) - (delta.previousTerrain == TileType.Forest ? 1 : 0);
        }

        ActionResolution eventResolution = new ActionResolution(
            true,
            "Climate event applied.",
            deltas.ToArray(),
            new PlayerDelta(string.Empty, 0, 0, 0, 0, default),
            new GlobalDelta(forestDelta, carbonDelta, 0));

        viewModel.ApplyResolution(eventResolution, worldAdapter);
        for (int i = 0; i < deltas.Count; i++)
        {
            TileDelta delta = deltas[i];
            if (delta.nextTerrain != TileType.Barren && delta.nextTerrain != TileType.Mountain)
            {
                worldGenerator.ResetMiningCount(new Vector3Int(delta.q, delta.r, 0));
            }
        }
        TrackDeforestedTransitions(eventResolution);
        ownership.RefreshOverlay();
        ApplyForestClusterBonus();
        return affectedCells;
    }

    private List<Vector3Int> BuildWildfirePatchTargetsNearPlayer(int count)
    {
        var nearbyForestCells = new List<Vector3Int>();
        var fallbackForestCells = new List<Vector3Int>();
        if (worldGenerator == null || count <= 0)
        {
            return nearbyForestCells;
        }

        bool hasAnchor = TryResolveWildfireAnchor(out Vector3Int anchorCell);
        int radius = Mathf.Max(1, wildfirePlayerRadius);

        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (!worldGenerator.TryGetTileType(cell, out TileType terrain) || terrain != TileType.Forest)
            {
                continue;
            }

            long key = PackCoord(cell.x, cell.y);
            if (activeWildfirePatches.ContainsKey(key))
            {
                continue;
            }

            fallbackForestCells.Add(cell);
            if (!hasAnchor || HexWorldGeneratorTilemap.HexDistance(anchorCell, cell) <= radius)
            {
                nearbyForestCells.Add(cell);
            }
        }

        List<Vector3Int> source = nearbyForestCells.Count > 0 ? nearbyForestCells : fallbackForestCells;
        return BuildClusteredWildfirePatchSelection(source, count);
    }

    private bool TryResolveWildfireAnchor(out Vector3Int anchorCell)
    {
        if (hasSelection && worldGenerator != null && worldGenerator.HasTile(selectedCoord.ToVector3Int()))
        {
            anchorCell = selectedCoord.ToVector3Int();
            return true;
        }

        if (ownership != null && ownership.TryGetAnyOwnedCell(out anchorCell))
        {
            return true;
        }

        anchorCell = default;
        return false;
    }

    private List<Vector3Int> BuildClusteredWildfirePatchSelection(List<Vector3Int> forestCells, int count)
    {
        var selected = new List<Vector3Int>(Mathf.Max(0, count));
        if (forestCells == null || forestCells.Count == 0 || count <= 0)
        {
            return selected;
        }

        if (forestCells.Count <= count)
        {
            selected.AddRange(forestCells);
            return selected;
        }

        var selectedKeys = new HashSet<long>();
        var frontier = new Queue<Vector3Int>();
        int seedCount = Mathf.Clamp(Mathf.CeilToInt(count / 3f), 1, 2);

        for (int i = 0; i < seedCount && forestCells.Count > 0; i++)
        {
            int seedIndex = UnityEngine.Random.Range(0, forestCells.Count);
            Vector3Int seed = forestCells[seedIndex];
            forestCells.RemoveAt(seedIndex);
            frontier.Enqueue(seed);
        }

        while (selected.Count < count && (frontier.Count > 0 || forestCells.Count > 0))
        {
            if (frontier.Count == 0)
            {
                int refillIndex = UnityEngine.Random.Range(0, forestCells.Count);
                frontier.Enqueue(forestCells[refillIndex]);
                forestCells.RemoveAt(refillIndex);
            }

            Vector3Int cell = frontier.Dequeue();
            long key = PackCoord(cell.x, cell.y);
            if (!selectedKeys.Add(key))
            {
                continue;
            }

            if (!worldGenerator.TryGetTileType(cell, out TileType terrain) || terrain != TileType.Forest)
            {
                continue;
            }

            selected.Add(cell);

            Vector3Int[] neighbors = HexWorldGeneratorTilemap.GetNeighborsPointTop(cell);
            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector3Int neighbor = neighbors[i];
                long neighborKey = PackCoord(neighbor.x, neighbor.y);
                if (selectedKeys.Contains(neighborKey) || !worldGenerator.HasTile(neighbor))
                {
                    continue;
                }

                if (worldGenerator.TryGetTileType(neighbor, out TileType neighborTerrain) && neighborTerrain == TileType.Forest)
                {
                    frontier.Enqueue(neighbor);
                }
            }
        }

        while (selected.Count < count && forestCells.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, forestCells.Count);
            Vector3Int fallback = forestCells[index];
            forestCells.RemoveAt(index);
            long key = PackCoord(fallback.x, fallback.y);
            if (selectedKeys.Add(key))
            {
                selected.Add(fallback);
            }
        }

        return selected;
    }

    private void AdvanceWildfirePatches(int tick)
    {
        if (activeWildfirePatches.Count == 0 || worldGenerator == null)
        {
            return;
        }

        var toDeforest = new List<Vector3Int>();
        var toBarren = new List<Vector3Int>();
        var removeKeys = new List<long>();

        foreach (KeyValuePair<long, WildfirePatchState> pair in activeWildfirePatches)
        {
            WildfirePatchState patch = pair.Value;
            Vector3Int cell = patch.coord.ToVector3Int();
            if (!worldGenerator.HasTile(cell) || !worldGenerator.TryGetTileType(cell, out TileType terrain))
            {
                removeKeys.Add(pair.Key);
                continue;
            }

            if (tick >= patch.barrenTick)
            {
                if (terrain == TileType.DeforestedForest)
                {
                    toBarren.Add(cell);
                }

                removeKeys.Add(pair.Key);
                continue;
            }

            if (tick >= patch.deforestedTick)
            {
                if (terrain == TileType.Forest)
                {
                    toDeforest.Add(cell);
                }
                else if (terrain != TileType.DeforestedForest)
                {
                    removeKeys.Add(pair.Key);
                }
            }
        }

        ApplyTerrainChangeToCells(toDeforest, TileType.Forest, TileType.DeforestedForest, "Wildfire scorched the forest.");
        ApplyTerrainChangeToCells(toBarren, TileType.DeforestedForest, TileType.Barren, "Wildfire left the land barren.");

        for (int i = 0; i < removeKeys.Count; i++)
        {
            activeWildfirePatches.Remove(removeKeys[i]);
        }
    }

    private void PruneExpiredWildfirePatches(int tick)
    {
        if (activeWildfirePatches.Count == 0)
        {
            return;
        }

        List<long> expiredKeys = null;
        foreach (KeyValuePair<long, WildfirePatchState> pair in activeWildfirePatches)
        {
            if (pair.Value == null || tick < pair.Value.endTick)
            {
                continue;
            }

            expiredKeys ??= new List<long>();
            expiredKeys.Add(pair.Key);
        }

        if (expiredKeys == null)
        {
            return;
        }

        for (int i = 0; i < expiredKeys.Count; i++)
        {
            activeWildfirePatches.Remove(expiredKeys[i]);
        }
    }

    private List<Vector3Int> ApplyTerrainChangeToCells(List<Vector3Int> cells, TileType requiredTerrain, TileType nextTerrain, string reason)
    {
        if (cells == null || cells.Count == 0 || viewModel?.WorldState == null)
        {
            return new List<Vector3Int>();
        }

        var deltas = new List<TileDelta>(cells.Count);
        var affectedCells = new List<Vector3Int>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3Int cell = cells[i];
            if (!worldGenerator.TryGetTileType(cell, out TileType terrain) || terrain != requiredTerrain)
            {
                continue;
            }

            worldGenerator.TryGetBuildingType(cell, out BuildingType building);
            deltas.Add(new TileDelta(
                cell.x,
                cell.y,
                terrain,
                nextTerrain,
                building,
                building,
                false,
                string.Empty,
                viewModel.WorldState.tick));
            affectedCells.Add(cell);
        }

        if (deltas.Count == 0)
        {
            return affectedCells;
        }

        int carbonDelta = 0;
        int forestDelta = 0;
        for (int i = 0; i < deltas.Count; i++)
        {
            TileDelta delta = deltas[i];
            carbonDelta += delta.nextTerrain.GetCarbonValue() - delta.previousTerrain.GetCarbonValue();
            forestDelta += (delta.nextTerrain == TileType.Forest ? 1 : 0) - (delta.previousTerrain == TileType.Forest ? 1 : 0);
        }

        ActionResolution wildfireResolution = new ActionResolution(
            true,
            reason,
            deltas.ToArray(),
            new PlayerDelta(string.Empty, 0, 0, 0, 0, default),
            new GlobalDelta(forestDelta, carbonDelta, 0));

        viewModel.ApplyResolution(wildfireResolution, worldAdapter);
        for (int i = 0; i < deltas.Count; i++)
        {
            TileDelta delta = deltas[i];
            if (delta.nextTerrain != TileType.Barren && delta.nextTerrain != TileType.Mountain)
            {
                worldGenerator.ResetMiningCount(new Vector3Int(delta.q, delta.r, 0));
            }
        }

        TrackDeforestedTransitions(wildfireResolution);
        ownership.RefreshOverlay();
        ApplyForestClusterBonus();
        return affectedCells;
    }

    private List<Vector3Int> CollectTerrainCells(TileType terrainType)
    {
        return CollectTerrainCells(new[] { terrainType });
    }

    private List<Vector3Int> CollectTerrainCells(params TileType[] terrainTypes)
    {
        var cells = new List<Vector3Int>();
        if (worldGenerator == null || terrainTypes == null || terrainTypes.Length == 0)
        {
            return cells;
        }

        var allowed = new HashSet<TileType>(terrainTypes);
        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (worldGenerator.TryGetTileType(cell, out TileType terrain) && allowed.Contains(terrain))
            {
                cells.Add(cell);
            }
        }

        return cells;
    }

    private List<Vector3Int> CollectCellsAdjacentToTerrain(TileType requiredNeighbor)
    {
        var cells = new List<Vector3Int>();
        if (worldGenerator == null)
        {
            return cells;
        }

        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (!worldGenerator.TryGetTileType(cell, out TileType terrain))
            {
                continue;
            }

            if (terrain == TileType.Water || terrain == TileType.Ice || terrain == TileType.Mountain)
            {
                continue;
            }

            if (worldGenerator.HasAdjacentTerrainType(cell, requiredNeighbor))
            {
                cells.Add(cell);
            }
        }

        return cells;
    }

    private void RegisterClimateTileHighlights(ClimateEventType type, List<Vector3Int> cells, int endTick)
    {
        if (cells == null || cells.Count == 0)
        {
            return;
        }

        var uniqueCells = new HashSet<long>();
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3Int cell = cells[i];
            long key = PackCoord(cell.x, cell.y);
            if (!uniqueCells.Add(key))
            {
                continue;
            }

            climateTileHighlights.Add(new ClimateTileHighlight
            {
                type = type,
                coord = HexCoord.FromVector3Int(cell),
                endTick = endTick
            });
        }
    }

    private void PruneExpiredClimateTileHighlights(int tick)
    {
        climateTileHighlights.RemoveAll(highlight => tick >= highlight.endTick);
    }

    private void ApplyCapturePressureForCycle()
    {
        if (worldGenerator == null || ownership == null || string.IsNullOrWhiteSpace(activeWalletAddress))
        {
            return;
        }

        var activeThisCycle = new HashSet<long>();
        var armyList = GetArmyUnitsWithOptimistic();

        for (int i = 0; i < armyList.Count; i++)
        {
            ArmyUnit unit = armyList[i];
            if (!string.Equals(unit.ownerWallet, activeWalletAddress, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Vector3Int cell = unit.coord.ToVector3Int();
            if (!worldGenerator.HasTile(cell))
            {
                continue;
            }

            if (ownership.IsOwned(cell))
            {
                continue;
            }

            long key = PackCoord(cell.x, cell.y);
            if (ownerByTile.TryGetValue(key, out string pactOwner) && IsActiveNonAggressionWith(pactOwner))
            {
                continue;
            }

            int pressure = capturePressure.TryGetValue(key, out int existing) ? existing : 0;
            pressure += 1;
            capturePressure[key] = pressure;
            activeThisCycle.Add(key);

            if (pressure >= 10)
            {
                ownership.SetOwned(cell, true);
                capturePressure.Remove(key);
                ownerByTile[key] = activeWalletAddress;
                if (viewModel?.PlayerState != null)
                {
                    viewModel.PlayerState.ownedTilesCount += 1;
                }
            }
        }

        var decayKeys = new List<long>();
        foreach (KeyValuePair<long, int> pair in capturePressure)
        {
            if (activeThisCycle.Contains(pair.Key))
            {
                continue;
            }

            int next = pair.Value - 1;
            if (next <= 0)
            {
                decayKeys.Add(pair.Key);
            }
            else
            {
                capturePressure[pair.Key] = next;
            }
        }

        for (int i = 0; i < decayKeys.Count; i++)
        {
            capturePressure.Remove(decayKeys[i]);
        }

        ownership.RefreshOverlay();
    }

    private bool IsActiveNonAggressionWith(string wallet)
    {
        if (string.IsNullOrWhiteSpace(wallet))
        {
            return false;
        }

        for (int i = 0; i < diplomacyPacts.Count; i++)
        {
            DiplomacyPact pact = diplomacyPacts[i];
            if (pact == null || pact.status != DiplomacyPactStatus.Active || pact.type != DiplomacyPactType.NonAggression)
            {
                continue;
            }

            if (IsLocalWallet(pact.walletA) && string.Equals(pact.walletB, wallet, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsLocalWallet(pact.walletB) && string.Equals(pact.walletA, wallet, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsClaimBlockedByPact(HexCoord coord, out string reason)
    {
        reason = string.Empty;

        if (diplomacyPacts.Count == 0 || worldGenerator == null)
        {
            return false;
        }

        Vector3Int cell = coord.ToVector3Int();
        Vector3Int[] neighbors = HexWorldGeneratorTilemap.GetNeighborsPointTop(cell);
        for (int i = 0; i < neighbors.Length; i++)
        {
            Vector3Int neighbor = neighbors[i];
            long key = PackCoord(neighbor.x, neighbor.y);
            if (!ownerByTile.TryGetValue(key, out string owner) || string.IsNullOrWhiteSpace(owner))
            {
                continue;
            }

            if (IsActiveNonAggressionWith(owner))
            {
                reason = "Non-aggression pact blocks claims near allied borders.";
                return true;
            }
        }

        return false;
    }

    private void ApplyCarbonCaptureForCycle()
    {
        if (viewModel?.PlayerState == null || viewModel.WorldState == null)
        {
            return;
        }

        if (!viewModel.PlayerState.techCarbonCapture)
        {
            return;
        }

        ApplyGlobalCarbonDelta(-1);
    }

    public ClimateEventInstance[] GetActiveClimateEvents()
    {
        return activeEvents.ToArray();
    }

    public ResourceRateSnapshot GetResourceRateSnapshot()
    {
        CalculatePassiveIncomePreview(out float foodGain, out float mineralGain, out float foodModifierPercent, out float mineralsModifierPercent);
        float cycleDuration = Mathf.Max(1f, GetCycleDurationSeconds());
        float foodPerMinute = foodGain * 60f / cycleDuration;
        float mineralsPerMinute = mineralGain * 60f / cycleDuration;

        return new ResourceRateSnapshot(
            0f,
            mineralsPerMinute,
            foodPerMinute,
            0f,
            mineralsModifierPercent,
            foodModifierPercent);
    }

    public float GetIndustryYieldPerCycle(HexCoord coord)
    {
        return Mathf.Max(0f, industryMineralsPerCycle);
    }

    public string DescribeClimateEvent(ClimateEventType type)
    {
        switch (type)
        {
            case ClimateEventType.Heatwave:
                return $"Food output reduced by {Mathf.RoundToInt(heatwaveFoodPenalty * 100f)}% for {Mathf.Max(1, heatwaveDurationCycles)} cycles.";
            case ClimateEventType.Wildfire:
                return $"Burns {wildfireTilesRange.x}-{wildfireTilesRange.y} forest tiles into deforested land immediately.";
            case ClimateEventType.Flood:
                return $"Flood surge affects {floodTilesRange.x}-{floodTilesRange.y} water-adjacent tiles this cycle.";
            case ClimateEventType.IceMelt:
                return $"Melts {iceMeltTilesRange.x}-{iceMeltTilesRange.y} ice tiles into open water immediately.";
            case ClimateEventType.DesertSpread:
                return $"Desertification converts {desertSpreadRange.x}-{desertSpreadRange.y} vulnerable tiles this cycle.";
            default:
                return "Planetary conditions shifted this cycle.";
        }
    }

    private void RebuildDeforestedRegistryFromWorld(int referenceTick)
    {
        deforestedSinceTick.Clear();
        if (worldGenerator == null)
        {
            return;
        }

        int stampedTick = Mathf.Max(0, referenceTick);
        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (!worldGenerator.TryGetTileType(cell, out TileType terrain) || terrain != TileType.DeforestedForest)
            {
                continue;
            }

            deforestedSinceTick[PackCoord(cell.x, cell.y)] = stampedTick;
        }
    }

    private void RebuildRecoveryProjectRegistryFromWorld(int referenceTick)
    {
        recoveryProjectUntilTick.Clear();
        if (worldGenerator == null)
        {
            return;
        }

        int minimumDuration = Mathf.Max(1, recoveryProjectCycles);
        int expiryTick = Mathf.Max(0, referenceTick) + minimumDuration;
        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (!worldGenerator.TryGetBuildingType(cell, out BuildingType building) || building != BuildingType.RecoveryProject)
            {
                continue;
            }

            recoveryProjectUntilTick[PackCoord(cell.x, cell.y)] = expiryTick;
        }
    }

    private static long PackCoord(int q, int r)
    {
        return ((long)q << 32) ^ (uint)r;
    }

    private void RestoreCommittedSelectionVisual()
    {
        if (ownership == null)
        {
            return;
        }

        if (!hasSelection)
        {
            ownership.ClearSelection();
            return;
        }

        if (selectedCoords.Count > 1)
        {
            var selectedCells = new List<Vector3Int>(selectedCoords.Count);
            for (int i = 0; i < selectedCoords.Count; i++)
            {
                selectedCells.Add(selectedCoords[i].ToVector3Int());
            }

            ownership.SetSelectedCells(selectedCells);
            return;
        }

        ownership.SetSelectedCell(selectedCoord.ToVector3Int());
    }

    private void BootstrapLocalTerritory(string walletAddress, bool forceAssignment)
    {
        if ((!assignStarterSettlementOnLocalLogin && !forceAssignment) || ownership == null || worldGenerator == null)
        {
            return;
        }

        string key = BuildWalletPrefsKey(walletAddress);
        bool seen = rememberWalletSpawnLocally && PlayerPrefs.GetInt(key + ".seen", 0) == 1;
        Vector3Int storedCell = default;
        bool hasStoredSpawn = seen && TryLoadStoredSpawn(key, out storedCell) && IsValidStarterCell(storedCell);

        Vector3Int spawnCell = default;
        bool hasSpawn = false;

        if (hasStoredSpawn)
        {
            spawnCell = storedCell;
            hasSpawn = true;
        }
        else
        {
            int slot = walletAddress.GetHashCode();
            if (worldGenerator.TryGetSpawnCell(slot, out Vector3Int slotSpawn) && IsValidStarterCell(slotSpawn))
            {
                spawnCell = slotSpawn;
                hasSpawn = true;
            }
            else if (worldGenerator.TryGetRandomSpawnCell(out Vector3Int randomSpawn) && IsValidStarterCell(randomSpawn))
            {
                spawnCell = randomSpawn;
                hasSpawn = true;
            }
        }

        if (!hasSpawn && !TryFindFallbackStarterCell(out spawnCell))
        {
            return;
        }

        if (!ownership.IsOwned(spawnCell))
        {
            ownership.SetOwned(spawnCell, true);
        }
        ownerByTile[PackCoord(spawnCell.x, spawnCell.y)] = walletAddress;

        worldGenerator.TrySetTileType(spawnCell, TileType.Plains);
        worldGenerator.TrySetBuildingType(spawnCell, BuildingType.Settlement);
        ownership.SetSelectedCell(spawnCell);
        ownership.RefreshOverlay();

        if (ownership.GetOwnedCount() <= 0)
        {
            return;
        }

        selectedCoord = HexCoord.FromVector3Int(spawnCell);
        selectedCoords.Clear();
        selectedCoords.Add(selectedCoord);
        hasSelection = true;
        hasFocusedOwnedArea = true;
        hasAttemptedOfflineSpawnRecovery = true;

        if (viewModel != null && viewModel.PlayerState != null)
        {
            viewModel.PlayerState.ownedTilesCount = Mathf.Max(1, ownership.GetOwnedCount());
        }

        if (rememberWalletSpawnLocally)
        {
            PlayerPrefs.SetInt(key + ".seen", 1);
            PlayerPrefs.SetInt(key + ".spawnX", spawnCell.x);
            PlayerPrefs.SetInt(key + ".spawnY", spawnCell.y);
            PlayerPrefs.Save();
        }

        if (worldCameraController != null)
        {
            worldCameraController.FocusOnCell(spawnCell, true);
        }
    }

    private void EnsureOfflineStarterTerritory()
    {
        if (!isInitialized || ownership == null || worldGenerator == null || !UsesLocalCycleClock)
        {
            return;
        }

        if (ownership.GetOwnedCount() > 0)
        {
            return;
        }

        string wallet = !string.IsNullOrWhiteSpace(activeWalletAddress)
            ? activeWalletAddress
            : (viewModel != null && viewModel.PlayerState != null ? viewModel.PlayerState.walletAddress : string.Empty);

        if (string.IsNullOrWhiteSpace(wallet))
        {
            return;
        }

        if (hasAttemptedOfflineSpawnRecovery)
        {
            return;
        }

        hasAttemptedOfflineSpawnRecovery = true;
        int ownedBefore = ownership.GetOwnedCount();
        BootstrapLocalTerritory(wallet, true);
        if (ownership.GetOwnedCount() <= ownedBefore)
        {
            hasAttemptedOfflineSpawnRecovery = true;
        }
        RefreshActionPanelState();
    }

    private bool IsValidStarterCell(Vector3Int cell)
    {
        if (worldGenerator == null || !worldGenerator.HasTile(cell))
        {
            return false;
        }

        return worldGenerator.TryGetTileType(cell, out TileType type) && type.IsClaimable();
    }

    private bool TryFindFallbackStarterCell(out Vector3Int cell)
    {
        if (worldGenerator == null)
        {
            cell = default;
            return false;
        }

        foreach (Vector3Int candidate in worldGenerator.EnumerateCells())
        {
            if (IsValidStarterCell(candidate))
            {
                cell = candidate;
                return true;
            }
        }

        cell = default;
        return false;
    }

    private void FocusOwnedTerritoryIfAny(bool zoomIn)
    {
        if (hasFocusedOwnedArea || ownership == null || worldCameraController == null)
        {
            return;
        }

        if (!ownership.TryGetAnyOwnedCell(out Vector3Int ownedCell))
        {
            return;
        }

        worldCameraController.FocusOnCell(ownedCell, zoomIn);
        ownership.SetSelectedCell(ownedCell);
        selectedCoord = HexCoord.FromVector3Int(ownedCell);
        selectedCoords.Clear();
        selectedCoords.Add(selectedCoord);
        hasSelection = true;
        hasFocusedOwnedArea = true;
    }

    public void RequestAction(FiniteEarthActionType actionType)
    {
        Debug.Log($"Orchestrator.RequestAction: {actionType}");
        HandleActionRequested(actionType);
    }

    public bool TryGetArmyAt(HexCoord coord, out ArmyUnit unit, bool onlyLocal = false)
    {
        unit = null;

        // Check optimistic positions first.
        foreach (var kv in optimisticArmyPositions)
        {
            if (kv.Value.q != coord.q || kv.Value.r != coord.r) continue;
            if (stdbClient == null) continue;
            var row = stdbClient.GetArmyById(kv.Key);
            if (row == null) continue;
            if (onlyLocal && !string.Equals(row.Owner, activeWalletAddress, StringComparison.OrdinalIgnoreCase)) continue;
            unit = new ArmyUnit { id = kv.Key.ToString(), ownerWallet = row.Owner ?? string.Empty, coord = kv.Value, strength = GetArmyStrength(kv.Key) };
            return true;
        }

        // Query STDB list.
        var list = stdbClient?.GetArmyUnitsForRendering();
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].coord.q != coord.q || list[i].coord.r != coord.r) continue;
            if (onlyLocal && !string.Equals(list[i].ownerWallet, activeWalletAddress, StringComparison.OrdinalIgnoreCase)) continue;
            unit = list[i];
            if (ulong.TryParse(unit.id, out ulong id)) unit.strength = GetArmyStrength(id);
            return true;
        }

        return false;
    }

    public bool TryGetSelectedArmy(out ArmyUnit unit, out float cooldownRemaining)
    {
        unit = null;
        cooldownRemaining = 0f;

        if (string.IsNullOrWhiteSpace(selectedArmyId) || stdbClient == null || !stdbClient.IsReady)
            return false;

        if (!ulong.TryParse(selectedArmyId, out ulong stdbId)) return false;

        var row = stdbClient.GetArmyById(stdbId);
        if (row == null) return false;

        HexCoord pos = optimisticArmyPositions.TryGetValue(stdbId, out HexCoord opt)
            ? opt
            : new HexCoord(row.Q, row.R);

        unit = new ArmyUnit
        {
            id          = selectedArmyId,
            ownerWallet = row.Owner ?? string.Empty,
            coord       = pos,
            strength    = GetArmyStrength(stdbId),
            lastMoveAt  = armyLastMoveTimes.TryGetValue(stdbId, out float t) ? t : 0f
        };

        float movedAt = armyLastMoveTimes.TryGetValue(stdbId, out float mt) ? mt : float.MinValue;
        cooldownRemaining = Mathf.Max(0f, armyMoveCooldownSeconds - (Time.unscaledTime - movedAt));
        return true;
    }

    private int GetArmyStrength(ulong id) =>
        armyStrengths.TryGetValue(id, out int s) ? s : 1;

    public void ArmSelectedArmyMove()
    {
        if (!TryGetSelectedArmy(out ArmyUnit unit, out _))
        {
            return;
        }

        armyMoveMode = true;
        SetArmySelectionState(unit.coord);
    }

    public bool CanReinforceSelectedArmy(out FiniteEarthResourcePool cost, out string reason)
    {
        cost = new FiniteEarthResourcePool { food = Mathf.Max(0, reinforceFoodCost) };
        reason = string.Empty;

        if (!TryGetSelectedArmy(out ArmyUnit unit, out _))
        {
            reason = "Select an army first.";
            return false;
        }

        if (unit.strength >= MaxArmyStrength)
        {
            reason = "Army is already at max strength.";
            return false;
        }

        if (worldGenerator == null || !worldGenerator.TryGetBuildingType(unit.coord.ToVector3Int(), out BuildingType building) || building != BuildingType.Barracks)
        {
            reason = "Reinforce only on a barracks tile.";
            return false;
        }

        if (viewModel?.PlayerState == null || !viewModel.PlayerState.resources.CanAfford(cost))
        {
            reason = "Not enough food to reinforce.";
            return false;
        }

        return true;
    }

    public bool ReinforceSelectedArmy()
    {
        if (!CanReinforceSelectedArmy(out FiniteEarthResourcePool cost, out _))
            return false;

        if (!TryGetSelectedArmy(out ArmyUnit unit, out _) || viewModel?.PlayerState == null)
            return false;

        if (!ulong.TryParse(unit.id, out ulong stdbId)) return false;

        int newStrength = Mathf.Clamp(unit.strength + 1, 1, MaxArmyStrength);
        armyStrengths[stdbId] = newStrength;
        viewModel.PlayerState.resources.Spend(cost);
        ResourcePopupRequested?.Invoke(unit.coord, new FiniteEarthResourcePool { food = -cost.food, wood = -cost.wood, minerals = -cost.minerals });
        return true;
    }

    public string DescribeSelectedArmyStatus(float cooldownRemaining)
    {
        if (armyMoveMode)
        {
            return "AWAITING MOVE";
        }

        if (cooldownRemaining > 0.01f)
        {
            return $"RECOVERING {cooldownRemaining:0.0}S";
        }

        return "HOLDING";
    }

    public int GetCapturePressure(HexCoord coord)
    {
        long key = PackCoord(coord.q, coord.r);
        return capturePressure.TryGetValue(key, out int pressure) ? pressure : 0;
    }

    private static string BuildWalletPrefsKey(string wallet)
    {
        string normalized = string.IsNullOrWhiteSpace(wallet) ? "local-player" : wallet.Trim().ToLowerInvariant();
        return $"fe.wallet.{normalized}";
    }

    private static bool TryLoadStoredSpawn(string key, out Vector3Int cell)
    {
        bool hasX = PlayerPrefs.HasKey(key + ".spawnX");
        bool hasY = PlayerPrefs.HasKey(key + ".spawnY");

        if (!hasX || !hasY)
        {
            cell = default;
            return false;
        }

        cell = new Vector3Int(
            PlayerPrefs.GetInt(key + ".spawnX"),
            PlayerPrefs.GetInt(key + ".spawnY"),
            0);

        return true;
    }

}

public class ClimateEventOverlayPointTop : MonoBehaviour
{
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;
    [SerializeField] private FiniteEarthGameOrchestrator orchestrator;
    [SerializeField] private string layerName = "ClimateEventOverlay";
    [SerializeField] private string overlayTileResourcePath = "Tiles/Tile_Overlay";
    [SerializeField] private int sortingOrder = 25;
    [SerializeField] private float pulseSpeed = 5.5f;
    [SerializeField, Range(0f, 1f)] private float minimumAlpha = 0.24f;
    [SerializeField, Range(0f, 1f)] private float maximumAlpha = 0.74f;

    private Tilemap overlayTilemap;
    private Tile overlayTile;
    private readonly HashSet<Vector3Int> paintedCells = new HashSet<Vector3Int>();

    private void Awake()
    {
        ResolveReferences();
        EnsureLayer();
    }

    private void Update()
    {
        ResolveReferences();
        EnsureLayer();
        RefreshOverlay();
    }

    private void ResolveReferences()
    {
        if (worldGenerator == null)
        {
            worldGenerator = FindAnyObjectByType<HexWorldGeneratorTilemap>();
        }

        if (orchestrator == null)
        {
            orchestrator = FindAnyObjectByType<FiniteEarthGameOrchestrator>();
        }
    }

    private void RefreshOverlay()
    {
        if (overlayTilemap == null)
        {
            return;
        }

        foreach (Vector3Int cell in paintedCells)
        {
            overlayTilemap.SetTile(cell, null);
        }
        paintedCells.Clear();

        if (orchestrator == null)
        {
            return;
        }

        IReadOnlyList<ClimateTileHighlight> highlights = orchestrator.GetActiveClimateTileHighlights();
        if (highlights == null || highlights.Count == 0)
        {
            return;
        }

        float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed));
        float alpha = Mathf.Lerp(minimumAlpha, maximumAlpha, pulse);

        for (int i = 0; i < highlights.Count; i++)
        {
            ClimateTileHighlight highlight = highlights[i];
            Vector3Int cell = highlight.coord.ToVector3Int();
            if (worldGenerator != null && !worldGenerator.HasTile(cell))
            {
                continue;
            }

            overlayTilemap.SetTile(cell, overlayTile);
            overlayTilemap.SetTileFlags(cell, TileFlags.None);
            overlayTilemap.SetColor(cell, ResolveColor(highlight.type, alpha, pulse));
            paintedCells.Add(cell);
        }
    }

    private Color ResolveColor(ClimateEventType type, float alpha, float pulse)
    {
        switch (type)
        {
            case ClimateEventType.Heatwave:
                return Color.Lerp(new Color(1f, 0.78f, 0.15f, alpha), new Color(1f, 0.55f, 0.10f, alpha), pulse);
            case ClimateEventType.Wildfire:
                return Color.Lerp(new Color(1f, 0.68f, 0.12f, alpha), new Color(0.96f, 0.34f, 0.06f, alpha), pulse);
            case ClimateEventType.Flood:
                return Color.Lerp(new Color(0.28f, 0.74f, 1f, alpha), new Color(0.14f, 0.48f, 0.98f, alpha), pulse);
            case ClimateEventType.IceMelt:
                return Color.Lerp(new Color(0.72f, 0.94f, 1f, alpha), new Color(0.42f, 0.82f, 1f, alpha), pulse);
            case ClimateEventType.DesertSpread:
                return Color.Lerp(new Color(0.95f, 0.82f, 0.32f, alpha), new Color(0.88f, 0.62f, 0.18f, alpha), pulse);
            default:
                return new Color(1f, 1f, 1f, alpha);
        }
    }

    private void EnsureLayer()
    {
        if (worldGenerator == null)
        {
            return;
        }

        Grid grid = worldGenerator.RuntimeGrid;
        if (grid == null)
        {
            return;
        }

        Transform existing = grid.transform.Find(layerName);
        if (existing != null)
        {
            overlayTilemap = existing.GetComponent<Tilemap>();
        }

        if (overlayTilemap == null)
        {
            GameObject layerObject = new GameObject(layerName);
            layerObject.transform.SetParent(grid.transform, false);
            overlayTilemap = layerObject.AddComponent<Tilemap>();
        }

        overlayTilemap.orientation = Tilemap.Orientation.XY;
        overlayTilemap.tileAnchor = Vector3.zero;

        TilemapRenderer renderer = overlayTilemap.GetComponent<TilemapRenderer>();
        if (renderer == null)
        {
            renderer = overlayTilemap.gameObject.AddComponent<TilemapRenderer>();
        }

        renderer.sortingOrder = sortingOrder;
        renderer.sortOrder = TilemapRenderer.SortOrder.BottomLeft;
        renderer.mode = TilemapRenderer.Mode.Individual;

        if (overlayTile == null)
        {
            overlayTile = Resources.Load<Tile>(overlayTileResourcePath);
            if (overlayTile == null)
            {
                overlayTile = ScriptableObject.CreateInstance<Tile>();
                overlayTile.sprite = BuildHexSprite(32);
                overlayTile.color = Color.white;
            }
        }
    }

    private static Sprite BuildHexSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color white = new Color(1f, 1f, 1f, 1f);

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.45f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - center.x);
                float dy = Mathf.Abs(y - center.y);
                float angle = Mathf.Atan2(dy, dx);
                float maxRadius = radius * Mathf.Cos(Mathf.PI / 6f) / Mathf.Cos(Mathf.Repeat(angle, Mathf.PI / 3f) - Mathf.PI / 6f);
                float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                tex.SetPixel(x, y, distance <= maxRadius ? white : clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
    }
}
