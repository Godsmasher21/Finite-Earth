using System;
using System.Collections.Generic;
using UnityEngine;

public class FiniteEarthGameOrchestrator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;
    [SerializeField] private OwnershipOverlayPointTop ownership;
    [SerializeField] private GameStateViewModel viewModel;
    [SerializeField] private ActionInputController inputController;
    [SerializeField] private ActionPanelPresenter actionPanel;
    [SerializeField] private SpacetimeRealtimeClient realtimeClient;
    [SerializeField] private WorldCameraController worldCameraController;
    [SerializeField] private bool runUniversalCycleLocallyWhenOffline = true;
    [SerializeField] private bool useUnscaledTimeForCycleClock = true;
    [SerializeField] private bool assignStarterSettlementOnLocalLogin = true;
    [SerializeField] private bool rememberWalletSpawnLocally = true;

    [Header("Starting Resources")]
    [SerializeField, Min(0)] private int startingWood = 3;
    [SerializeField, Min(0)] private int startingFood = 3;
    [SerializeField, Min(0)] private int startingMinerals;

    [Header("Natural Recovery")]
    [SerializeField, Min(1)] private int deforestedToPlainsCycles = 3;

    [Header("Passive Yield")]
    [SerializeField, Min(0)] private int farmFoodPerCycle = 1;

    private readonly LocalPredictionEngine predictionEngine = new LocalPredictionEngine();
    private static readonly FiniteEarthActionType[] UiActions =
    {
        FiniteEarthActionType.Claim,
        FiniteEarthActionType.BuildSettlement,
        FiniteEarthActionType.BuildIndustry,
        FiniteEarthActionType.HarvestForest,
        FiniteEarthActionType.Reforest,
        FiniteEarthActionType.Farm,
        FiniteEarthActionType.Irrigate,
        FiniteEarthActionType.Mine,
        FiniteEarthActionType.Restore
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
    private readonly Dictionary<long, int> deforestedSinceTick = new Dictionary<long, int>();

    public GameStateViewModel ViewModel => viewModel;
    public float CycleRemainingSeconds => Mathf.Max(0f, cycleRemainingSeconds);
    public bool UsesLocalCycleClock => runUniversalCycleLocallyWhenOffline && (realtimeClient == null || !realtimeClient.IsConnected);

    private void Awake()
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

        if (realtimeClient == null)
        {
            realtimeClient = FindAnyObjectByType<SpacetimeRealtimeClient>();
        }

        if (worldCameraController == null)
        {
            worldCameraController = FindAnyObjectByType<WorldCameraController>();
        }
    }

    private void Start()
    {
        if (worldGenerator == null || ownership == null || viewModel == null)
        {
            Debug.LogError("FiniteEarthGameOrchestrator: missing required references.");
            enabled = false;
            return;
        }

        worldGenerator.Generate();
        ownership.Initialize(worldGenerator);

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

        if (realtimeClient != null)
        {
            realtimeClient.ActionCommitted += HandleActionCommitted;
        }

        isInitialized = true;
        if (!string.IsNullOrWhiteSpace(pendingAuthenticatedWallet))
        {
            HandleAuthenticatedPlayer(pendingAuthenticatedWallet, pendingLocalBootstrap);
        }

        RefreshActionPanelState();
    }

    private void Update()
    {
        if (viewModel == null || viewModel.WorldState == null)
        {
            return;
        }

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
            ApplyPassiveFarmFoodForCycle();
            ApplyNaturalRecoveryForCurrentCycle();
            cycleRemainingSeconds += cycleDuration;
        }
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

        if (realtimeClient != null)
        {
            realtimeClient.ActionCommitted -= HandleActionCommitted;
        }
    }

    public void HandleAuthenticatedPlayer(string walletAddress, bool createLocalStartingTerritory)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            return;
        }

        activeWalletAddress = walletAddress.Trim().ToLowerInvariant();

        if (viewModel != null)
        {
            viewModel.SetWalletAddress(walletAddress);
        }

        pendingAuthenticatedWallet = activeWalletAddress;
        pendingLocalBootstrap = createLocalStartingTerritory;

        if (!isInitialized || ownership == null || worldGenerator == null)
        {
            return;
        }

        if (createLocalStartingTerritory)
        {
            BootstrapLocalTerritory(activeWalletAddress);
        }
        else
        {
            FocusOwnedTerritoryIfAny(true);
        }

        RefreshActionPanelState();
    }

    private void HandleTileSelected(HexCoord coord)
    {
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

    private void HandleActionRequested(FiniteEarthActionType actionType)
    {
        if (actionType == FiniteEarthActionType.EndTurn)
        {
            return;
        }

        if (!hasSelection)
        {
            RefreshActionPanelState();
            return;
        }

        if (selectedCoords.Count > 1)
        {
            HandleBatchActionRequested(actionType);
            return;
        }

        ActionIntent intent = viewModel.BuildIntent(actionType, selectedCoord);
        if (realtimeClient != null && realtimeClient.IsConnected)
        {
            ActionResolution precheck = resolver.Resolve(
                intent,
                viewModel.WorldState,
                viewModel.PlayerState,
                viewModel.WorldState.tick);

            if (!precheck.accepted)
            {
                Debug.LogWarning($"Action rejected locally: {precheck.reason}");
                RefreshActionPanelState();
                return;
            }

            _ = realtimeClient.SendIntentAsync(intent);
            RefreshActionPanelState();
            return;
        }

        ActionResolution predicted = predictionEngine.Predict(
            resolver,
            intent,
            viewModel.WorldState,
            viewModel.PlayerState,
            viewModel.WorldState.tick);

        if (!predicted.accepted)
        {
            Debug.LogWarning($"Action rejected locally: {predicted.reason}");
            RefreshActionPanelState();
            return;
        }

        viewModel.ApplyResolution(predicted, worldAdapter);
        TrackDeforestedTransitions(predicted);
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

        if (realtimeClient != null && realtimeClient.IsConnected)
        {
            int sent = 0;
            for (int i = 0; i < selectedCoords.Count; i++)
            {
                HexCoord coord = selectedCoords[i];
                ActionIntent intent = viewModel.BuildIntent(actionType, coord);
                ActionResolution precheck = resolver.Resolve(
                    intent,
                    viewModel.WorldState,
                    viewModel.PlayerState,
                    viewModel.WorldState.tick);

                if (!precheck.accepted)
                {
                    continue;
                }

                _ = realtimeClient.SendIntentAsync(intent);
                sent++;
            }

            if (sent == 0)
            {
                Debug.LogWarning($"Batch action rejected locally for all selected tiles: {actionType}.");
            }

            RefreshActionPanelState();
            return;
        }

        int applied = 0;
        for (int i = 0; i < selectedCoords.Count; i++)
        {
            HexCoord coord = selectedCoords[i];
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
            TrackDeforestedTransitions(predicted);
            applied++;
        }

        if (applied == 0)
        {
            Debug.LogWarning($"Batch action not applied to any selected tile: {actionType}.");
        }

        ownership.RefreshOverlay();
        RefreshActionPanelState();
    }

    public void HandleCycleStarted(CycleStartedMessage cycle)
    {
        if (viewModel == null)
        {
            return;
        }

        int authoritativeTick = cycle != null ? cycle.tick : -1;
        viewModel.StartNewCycle(authoritativeTick);
        ApplyPassiveFarmFoodForCycle();
        cycleRemainingSeconds = GetCycleDurationSeconds();
        RefreshActionPanelState();
    }

    private void HandleActionCommitted(ActionCommittedMessage committed)
    {
        if (committed == null || string.IsNullOrWhiteSpace(committed.intentId))
        {
            return;
        }

        ActionResolution authoritative = new ActionResolution(
            committed.accepted,
            committed.reason,
            committed.tileDeltas,
            committed.playerDelta,
            committed.globalDelta);

        if (!authoritative.accepted)
        {
            return;
        }

        viewModel.ApplyResolution(authoritative, worldAdapter);
        TrackDeforestedTransitions(authoritative);
        ownership.RefreshOverlay();
        FocusOwnedTerritoryIfAny(false);
        RefreshActionPanelState();
    }

    public void ApplyWorldSnapshot(WorldSnapshotMessage snapshot)
    {
        if (snapshot == null || worldGenerator == null || ownership == null || viewModel == null || viewModel.WorldState == null)
        {
            return;
        }

        viewModel.WorldState.tick = snapshot.tick;
        viewModel.WorldState.globalForestToken = snapshot.globalForestToken;
        viewModel.WorldState.globalCarbonToken = snapshot.globalCarbonToken;
        viewModel.WorldState.cycleSeconds = snapshot.cycleSeconds;
        viewModel.WorldState.actionsPerCycle = snapshot.actionsPerCycle;
        cycleRemainingSeconds = Mathf.Min(cycleRemainingSeconds, GetCycleDurationSeconds());

        if (snapshot.players != null && viewModel.PlayerState != null)
        {
            for (int i = 0; i < snapshot.players.Length; i++)
            {
                WorldPlayerSnapshotMessage player = snapshot.players[i];
                if (!string.Equals(player.walletAddress, viewModel.PlayerState.walletAddress, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                viewModel.PlayerState.ownedTilesCount = player.ownedTilesCount;
                viewModel.PlayerState.sustainabilityScore = player.sustainabilityScore;
                viewModel.PlayerState.actionsTaken = player.actionsTaken;
                viewModel.PlayerState.actionsRemaining = player.actionsRemaining;
                viewModel.PlayerState.lastClientSeq = player.lastClientSeq;
                break;
            }
        }

        if (snapshot.tiles == null || snapshot.tiles.Length == 0)
        {
            return;
        }

        ownership.ResetOwnership();

        for (int i = 0; i < snapshot.tiles.Length; i++)
        {
            WorldTileSnapshotMessage tile = snapshot.tiles[i];
            Vector3Int cell = new Vector3Int(tile.q, tile.r, 0);

            if (TryParseTileType(tile.currentState, out TileType terrain))
            {
                worldGenerator.TrySetTileType(cell, terrain);
            }

            if (TryParseBuildingType(tile.buildingType, out BuildingType building))
            {
                worldGenerator.TrySetBuildingType(cell, building);
            }

            bool ownsTile = !string.IsNullOrWhiteSpace(tile.ownerWallet)
                && !string.IsNullOrWhiteSpace(viewModel.PlayerState.walletAddress)
                && string.Equals(tile.ownerWallet, viewModel.PlayerState.walletAddress, StringComparison.OrdinalIgnoreCase);

            if (ownsTile)
            {
                ownership.SetOwned(cell, true);
            }
        }

        RebuildDeforestedRegistryFromWorld(snapshot.tick);
        ownership.RefreshOverlay();
        FocusOwnedTerritoryIfAny(false);
        RefreshActionPanelState();
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
        if (actionPanel == null || resolver == null || viewModel == null || viewModel.WorldState == null || viewModel.PlayerState == null)
        {
            return;
        }

        int selectionCount = Mathf.Max(0, selectedCoords.Count);
        if (hasSelection && selectionCount == 0)
        {
            selectionCount = 1;
        }

        int previewCount = hasSelection ? selectionCount : 0;
        bool claimableSelection = false;
        string claimReason = hasSelection ? "Select an action." : "Select a tile.";

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

                    if (preview.accepted)
                    {
                        applicableSelectionCount++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(firstBlockedReason) && !string.IsNullOrWhiteSpace(preview.reason))
                    {
                        firstBlockedReason = preview.reason;
                    }
                }

                affordableSelectionCount = ComputeAffordableSelectionCount(
                    spec.cost,
                    viewModel.PlayerState.resources,
                    applicableSelectionCount);
                actionable = applicableSelectionCount > 0;
                FiniteEarthResourcePool totalSelectionCost = ScaleCost(spec.cost, applicableSelectionCount);
                affordableForSelection = viewModel.PlayerState.resources.CanAfford(totalSelectionCost);

                if (applicableSelectionCount == 0)
                {
                    reason = string.IsNullOrWhiteSpace(firstBlockedReason)
                        ? "Unavailable for selected tiles."
                        : firstBlockedReason;
                }
                else if (!affordableForSelection)
                {
                    reason = "Not enough resources for all selected tiles.";
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
                    reason = "Ready.";
                }
            }

            if (action == FiniteEarthActionType.Claim)
            {
                claimableSelection = actionable && affordableForSelection;
                claimReason = reason;
            }

            FiniteEarthResourcePool totalCost = ScaleCost(spec.cost, applicableSelectionCount);
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

        actionPanel.SetSelectionContext(hasSelection, selectedCoord, claimableSelection, claimReason, selectionCount);
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

    private static int ComputeAffordableSelectionCount(
        FiniteEarthResourcePool unitCost,
        FiniteEarthResourcePool available,
        int applicableSelectionCount)
    {
        if (applicableSelectionCount <= 0)
        {
            return 0;
        }

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

    private void ApplyPassiveFarmFoodForCycle()
    {
        if (farmFoodPerCycle <= 0 || viewModel?.PlayerState == null || worldGenerator == null || ownership == null)
        {
            return;
        }

        int ownedFarms = 0;
        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            if (!ownership.IsOwned(cell))
            {
                continue;
            }

            if (!worldGenerator.TryGetTileType(cell, out TileType terrain))
            {
                continue;
            }

            if (terrain == TileType.Farmland)
            {
                ownedFarms++;
            }
        }

        if (ownedFarms <= 0)
        {
            return;
        }

        viewModel.PlayerState.resources.Add(new FiniteEarthResourcePool
        {
            food = ownedFarms * farmFoodPerCycle
        });
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

    private void BootstrapLocalTerritory(string walletAddress)
    {
        if (!assignStarterSettlementOnLocalLogin || ownership == null || worldGenerator == null)
        {
            return;
        }

        string key = BuildWalletPrefsKey(walletAddress);
        bool seen = rememberWalletSpawnLocally && PlayerPrefs.GetInt(key + ".seen", 0) == 1;
        Vector3Int storedCell = default;
        bool hasStoredSpawn = seen && TryLoadStoredSpawn(key, out storedCell);

        Vector3Int spawnCell;
        if (hasStoredSpawn)
        {
            spawnCell = storedCell;
        }
        else
        {
            int slot = Mathf.Abs(walletAddress.GetHashCode());
            if (!worldGenerator.TryGetSpawnCell(slot, out spawnCell))
            {
                worldGenerator.TryGetRandomSpawnCell(out spawnCell);
            }
        }

        if (!ownership.IsOwned(spawnCell))
        {
            ownership.SetOwned(spawnCell, true);
        }

        worldGenerator.TrySetTileType(spawnCell, TileType.Plains);
        worldGenerator.TrySetBuildingType(spawnCell, BuildingType.Settlement);
        ownership.SetSelectedCell(spawnCell);
        ownership.RefreshOverlay();

        selectedCoord = HexCoord.FromVector3Int(spawnCell);
        selectedCoords.Clear();
        selectedCoords.Add(selectedCoord);
        hasSelection = true;
        hasFocusedOwnedArea = true;

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
