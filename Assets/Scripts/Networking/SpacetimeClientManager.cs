using System;
using System.Collections.Generic;
using System.Linq;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

/// <summary>
/// Direct SpacetimeDB connection via the C# SDK.
/// Replaces the gateway WebSocket for all game-state traffic.
/// The gateway is still used only for the chain relay (on-chain minting).
/// </summary>
public class SpacetimeClientManager : MonoBehaviour
{
    [Header("SpacetimeDB")]
    [SerializeField] private string stdbUri = "wss://maincloud.spacetimedb.com";
    [SerializeField] private string stdbDatabaseAddress = "c200209d45087f10cf9fcc10414041426d6aebb4c22e572a86e51de8c729a360";

    private const string TokenPrefKey = "stdb_auth_token";
    private const int CycleSeconds = 30;

    public bool IsConnected { get; private set; }
    public bool IsReady { get; private set; }

    private DbConnection conn;
    private string activeWallet;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action Connected;
    public event Action<string> Disconnected;
    public event Action<WorldSnapshotMessage> SubscriptionReady;
    public event Action<ActionCommittedMessage> ActionCommitted;
    public event Action<CycleStartedMessage> CycleStarted;
    // Fires for every tile row that changes after initial subscription (server-side effects
    // like ClaimSettlementRadius, deforestation recovery, climate events, etc.)
    public event Action<RemoteTileChangedMessage> RemoteTileChanged;
    // Fires when the local player's row is updated server-side (passive income, stat changes).
    public event Action<WorldPlayerSnapshotMessage> LocalPlayerUpdated;
    // Fires whenever any army is inserted, moved, or removed — triggers a full re-render.
    public event Action ArmiesChanged;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Update()
    {
        // Pump the message queue on the main thread each frame.
        conn?.FrameTick();
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Connect(string walletAddress)
    {
        if (IsConnected || IsReady)
            Disconnect();

        activeWallet = walletAddress?.Trim().ToLowerInvariant() ?? string.Empty;
        string savedToken = PlayerPrefs.GetString(TokenPrefKey, string.Empty);

        var builder = DbConnection.Builder()
            .WithUri(stdbUri)
            .WithDatabaseName(stdbDatabaseAddress)
            .OnConnect(HandleConnect)
            .OnConnectError(HandleConnectError)
            .OnDisconnect(HandleDisconnect);

        if (!string.IsNullOrWhiteSpace(savedToken))
            builder = builder.WithToken(savedToken);

        conn = builder.Build();

        // Wire table callbacks before subscription so we never miss an event.
        conn.Db.ActionCommitEvents.OnInsert += OnActionCommitEvent;
        conn.Db.WorldState.OnUpdate += OnWorldStateUpdate;
        conn.Db.Tiles.OnUpdate += OnTileUpdated;
        conn.Db.Players.OnUpdate += OnPlayerUpdated;
        conn.Db.Armies.OnInsert += (_, _2) => ArmiesChanged?.Invoke();
        conn.Db.Armies.OnUpdate += (_, _2, _3) => ArmiesChanged?.Invoke();
        conn.Db.Armies.OnDelete += (_, _2) => ArmiesChanged?.Invoke();
    }

    public void Disconnect()
    {
        if (conn == null) return;
        conn.Db.ActionCommitEvents.OnInsert -= OnActionCommitEvent;
        conn.Db.WorldState.OnUpdate -= OnWorldStateUpdate;
        conn.Db.Tiles.OnUpdate -= OnTileUpdated;
        conn.Db.Players.OnUpdate -= OnPlayerUpdated;
        // Army lambdas can't be unsubscribed by reference — clearing conn handles cleanup.
        conn.Disconnect();
        conn = null;
        IsConnected = false;
        IsReady = false;
    }

    public void SendIntent(string intentId, string wallet, long clientSeq, int actionType, int q, int r)
    {
        if (!IsReady) return;
        conn.Reducers.SubmitIntent(intentId, wallet, clientSeq, actionType, q, r);
    }

    public void SendIntentBatch(
        string[] intentIds, string wallet, long[] clientSeqs,
        int[] actionTypes, int[] qs, int[] rs)
    {
        if (!IsReady) return;
        conn.Reducers.SubmitIntentBatch(
            wallet,
            new List<string>(intentIds),
            new List<long>(clientSeqs),
            new List<int>(actionTypes),
            new List<int>(qs),
            new List<int>(rs));
    }

    // ── Connection callbacks ──────────────────────────────────────────────────

    private void HandleConnect(DbConnection connection, Identity identity, string token)
    {
        IsConnected = true;
        if (!string.IsNullOrWhiteSpace(token))
            PlayerPrefs.SetString(TokenPrefKey, token);

        Debug.Log($"[STDB] Connected as identity {identity}");
        Connected?.Invoke();

        connection.SubscriptionBuilder()
            .OnApplied(HandleSubscriptionApplied)
            .OnError((ctx, ex) => Debug.LogError($"[STDB] Subscription error: {ex.Message}"))
            .Subscribe(new[]
            {
                "SELECT * FROM world_state",
                "SELECT * FROM tiles",
                "SELECT * FROM players",
                "SELECT * FROM player_identities",
                "SELECT * FROM climate_events",
                "SELECT * FROM action_commit_events",
                "SELECT * FROM pacts",
                "SELECT * FROM armies",
            });
    }

    private void HandleSubscriptionApplied(SubscriptionEventContext ctx)
    {
        IsReady = true;
        Debug.Log("[STDB] Subscription ready — building initial snapshot.");

        // Ensure the player row exists for this wallet before sending any intents.
        if (!string.IsNullOrWhiteSpace(activeWallet))
            conn.Reducers.EnsurePlayer(activeWallet);

        SubscriptionReady?.Invoke(BuildSnapshot());
        ArmiesChanged?.Invoke(); // render initial army positions from all players
    }

    private void HandleDisconnect(DbConnection connection, Exception? error)
    {
        IsConnected = false;
        IsReady = false;
        string reason = error?.Message ?? "disconnected";
        Debug.LogWarning($"[STDB] Disconnected: {reason}");
        Disconnected?.Invoke(reason);
    }

    private void HandleConnectError(Exception error)
    {
        Debug.LogError($"[STDB] Connection error: {error.Message}");
        IsConnected = false;
        IsReady = false;
    }

    // ── Table callbacks ───────────────────────────────────────────────────────

    private void OnActionCommitEvent(EventContext ctx, ActionCommitEventRow row)
    {
        ActionCommitted?.Invoke(ToCommitMessage(row));
    }

    private void OnTileUpdated(EventContext ctx, TileRow oldRow, TileRow newRow)
    {
        // Skip rows that haven't meaningfully changed (prevents noise during initial sync).
        bool terrainChanged  = oldRow.Terrain  != newRow.Terrain;
        bool buildingChanged = oldRow.Building != newRow.Building;
        bool ownerChanged    = !string.Equals(oldRow.Owner, newRow.Owner, System.StringComparison.Ordinal);

        if (!terrainChanged && !buildingChanged && !ownerChanged) return;

        TileDelta delta = new TileDelta(
            newRow.Q, newRow.R,
            (TileType)oldRow.Terrain,  (TileType)newRow.Terrain,
            (BuildingType)oldRow.Building, (BuildingType)newRow.Building,
            ownerChanged,
            ownerChanged ? (newRow.Owner ?? string.Empty) : string.Empty,
            (int)newRow.LastUpdate);

        RemoteTileChangedMessage msg = new RemoteTileChangedMessage
        {
            walletAddress = newRow.Owner ?? string.Empty,
            tick          = (int)newRow.LastUpdate,
            tileDeltas    = new[] { delta }
        };

        RemoteTileChanged?.Invoke(msg);
    }

    private void OnPlayerUpdated(EventContext ctx, PlayerRow oldRow, PlayerRow newRow)
    {
        // Only care about the local player — rivals update via leaderboard poll.
        if (string.IsNullOrWhiteSpace(activeWallet)) return;
        if (!string.Equals(newRow.Wallet, activeWallet, System.StringComparison.OrdinalIgnoreCase)) return;

        PlayerIdentityRow? id = conn?.Db.PlayerIdentities.Wallet.Find(newRow.Wallet);
        WorldPlayerSnapshotMessage snap = ToPlayerSnapshot(newRow);
        if (id != null)
        {
            snap.username    = id.Username    ?? string.Empty;
            snap.displayName = id.DisplayName ?? string.Empty;
        }
        LocalPlayerUpdated?.Invoke(snap);
    }

    private void OnWorldStateUpdate(EventContext ctx, WorldStateRow oldRow, WorldStateRow newRow)
    {
        if (newRow.Tick == oldRow.Tick && newRow.Cycle == oldRow.Cycle)
            return;

        // New cycle — fire CycleStarted so the orchestrator resets action counts.
        PlayerRow? localPlayer = conn.Db.Players.Wallet.Find(activeWallet);
        CycleStarted?.Invoke(new CycleStartedMessage
        {
            tick = (int)newRow.Tick,
            startedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            globalForestToken = newRow.ForestTotal,
            globalCarbonToken = newRow.CarbonTotal,
            player = localPlayer != null ? ToPlayerSnapshot(localPlayer) : null,
            climateEvents = BuildClimateEvents()
        });
    }

    // ── Snapshot builder ──────────────────────────────────────────────────────

    private WorldSnapshotMessage BuildSnapshot()
    {
        WorldStateRow? ws = conn.Db.WorldState.Iter().FirstOrDefault();

        return new WorldSnapshotMessage
        {
            worldId = ws?.WorldId ?? "finite-earth-alpha",
            tick = ws != null ? (int)ws.Tick : 0,
            globalForestToken = ws?.ForestTotal ?? 0,
            globalCarbonToken = ws?.CarbonTotal ?? 0,
            cycleSeconds = CycleSeconds,
            actionsPerCycle = 9999,
            tiles = conn.Db.Tiles.Iter()
                .Select(ToTileSnapshot)
                .ToArray(),
            players = conn.Db.Players.Iter()
                .Select(p =>
                {
                    PlayerIdentityRow? id = conn.Db.PlayerIdentities.Wallet.Find(p.Wallet);
                    WorldPlayerSnapshotMessage snap = ToPlayerSnapshot(p);
                    if (id != null)
                    {
                        snap.username = id.Username ?? string.Empty;
                        snap.displayName = id.DisplayName ?? string.Empty;
                    }
                    return snap;
                })
                .ToArray(),
            climateEvents = BuildClimateEvents()
        };
    }

    // ── Converters ────────────────────────────────────────────────────────────

    private static WorldTileSnapshotMessage ToTileSnapshot(TileRow t) =>
        new WorldTileSnapshotMessage
        {
            q = t.Q,
            r = t.R,
            currentState = TerrainName(t.Terrain),
            ownerWallet = t.Owner ?? string.Empty,
            buildingType = BuildingName(t.Building),
            lastUpdatedTick = (int)t.LastUpdate
        };

    private static WorldPlayerSnapshotMessage ToPlayerSnapshot(PlayerRow p) =>
        new WorldPlayerSnapshotMessage
        {
            walletAddress = p.Wallet,
            ownedTilesCount = p.OwnedTiles,
            sustainabilityScore = p.SustainabilityScore,
            actionsTaken = p.ActionsTaken,
            actionsRemaining = 9999,
            lastClientSeq = p.LastClientSeq,
            wood = p.Wood,
            food = p.Food,
            minerals = p.Minerals,
            researchPoints = p.ResearchPoints,
            techBasicForestry = p.TechBasicForestry == 1,
            techRenewableEnergy = p.TechRenewableEnergy == 1,
            techCarbonCapture = p.TechCarbonCapture == 1,
            ecoActions = p.EcoActions,
            industrialActions = p.IndustrialActions,
            agricultureActions = p.AgricultureActions,
            reputation = p.Reputation ?? "Balanced",
            username = string.Empty,
            displayName = string.Empty
        };

    private ClimateEventSnapshotMessage[] BuildClimateEvents()
    {
        // Only include events that are still active — the ClimateEvents table accumulates
        // all historical events and the subscription returns every row including expired ones.
        WorldStateRow? ws = conn.Db.WorldState.Iter().FirstOrDefault();
        long currentTick = ws?.Tick ?? 0;

        return conn.Db.ClimateEvents.Iter()
            .Where(e => e.EndTick > currentTick)
            .Select(e => new ClimateEventSnapshotMessage
            {
                id = (long)e.Id,
                type = e.Type,
                startTick = (int)e.StartTick,
                endTick = (int)e.EndTick
            })
            .ToArray();
    }

    private static ActionCommittedMessage ToCommitMessage(ActionCommitEventRow row)
    {
        TileDelta[] deltas = row.IncludeTileDelta
            ? new[]
            {
                new TileDelta(
                    row.Q, row.R,
                    (TileType)row.PreviousTerrain, (TileType)row.NextTerrain,
                    (BuildingType)row.PreviousBuilding, (BuildingType)row.NextBuilding,
                    row.OwnerChanged,
                    row.OwnerWallet ?? string.Empty,
                    (int)row.LastUpdatedTick)
            }
            : Array.Empty<TileDelta>();

        PlayerDelta playerDelta = new PlayerDelta(
            row.Wallet,
            row.OwnedTilesDelta,
            row.SustainabilityScoreDelta,
            row.ActionsTakenDelta,
            row.ActionsRemainingDelta,
            new FiniteEarthResourcePool
            {
                wood = row.WoodDelta,
                food = row.FoodDelta,
                minerals = row.MineralsDelta
            });

        GlobalDelta globalDelta = new GlobalDelta(row.ForestDelta, row.CarbonDelta, row.ActionCount);

        return new ActionCommittedMessage
        {
            commitId = row.CommitId,
            tick = (int)row.Tick,
            intentId = row.IntentId,
            accepted = row.Accepted,
            reason = row.Reason,
            walletAddress = row.Wallet,
            actionType = row.ActionType,
            q = row.Q,
            r = row.R,
            tileDeltas = deltas,
            playerDelta = playerDelta,
            globalDelta = globalDelta,
            batchHash = row.BatchHash ?? string.Empty
        };
    }

    // ── STDB query helpers ────────────────────────────────────────────────────

    /// Moves an army in STDB so all clients see the new position immediately.
    public void SendArmyMove(ulong armyId, string owner, int q, int r)
    {
        if (!IsReady) return;
        conn.Reducers.ArmyMove(armyId, owner, q, r);
    }

    /// Finds the STDB army owned by `owner` currently at (q, r).
    public SpacetimeDB.Types.ArmyRow? FindArmyAt(string owner, int q, int r)
    {
        if (conn == null) return null;
        foreach (var row in conn.Db.Armies.Iter())
        {
            if (row.Q == q && row.R == r &&
                string.Equals(row.Owner, owner, System.StringComparison.OrdinalIgnoreCase))
                return row;
        }
        return null;
    }

    /// Returns the army row with the given STDB ID, or null if not found.
    public SpacetimeDB.Types.ArmyRow? GetArmyById(ulong id)
    {
        if (conn == null) return null;
        return conn.Db.Armies.Id.Find(id);
    }

    /// Counts how many armies in STDB are owned by `wallet`.
    public int CountArmiesForWallet(string wallet)
    {
        if (conn == null || string.IsNullOrWhiteSpace(wallet)) return 0;
        int count = 0;
        foreach (var row in conn.Db.Armies.Iter())
        {
            if (string.Equals(row.Owner, wallet, System.StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }

    /// Returns the current STDB tile state — used to revert optimistic updates on rejection.
    public SpacetimeDB.Types.TileRow? GetTile(int q, int r)
    {
        if (conn == null) return null;
        // Mirror Lib.cs PackTileId: (q << 20) | (uint)r
        long tileId = ((long)q << 20) | (uint)r;
        return conn.Db.Tiles.Id.Find(tileId);
    }

    /// Returns ArmyUnit representations of ALL armies in STDB for rendering.
    public System.Collections.Generic.List<ArmyUnit> GetArmyUnitsForRendering()
    {
        var result = new System.Collections.Generic.List<ArmyUnit>();
        if (conn == null) return result;
        foreach (var row in conn.Db.Armies.Iter())
        {
            result.Add(new ArmyUnit
            {
                id          = row.Id.ToString(),
                ownerWallet = row.Owner ?? string.Empty,
                coord       = new HexCoord(row.Q, row.R),
                strength    = 1,
                lastMoveAt  = 0f
            });
        }
        return result;
    }

    // ── Terrain/building name helpers (mirror Lib.cs constants) ──────────────

    private static string TerrainName(int t) => t switch
    {
        0 => "Forest",
        1 => "Plains",
        2 => "Mountain",
        3 => "Water",
        4 => "Desert",
        5 => "Barren",
        6 => "DeforestedForest",
        7 => "Farmland",
        8 => "Ice",
        _ => "Plains"
    };

    private static string BuildingName(int b) => b switch
    {
        0 => "None",
        1 => "Settlement",
        2 => "Industry",
        3 => "RecoveryProject",
        4 => "Barracks",
        _ => "None"
    };
}
