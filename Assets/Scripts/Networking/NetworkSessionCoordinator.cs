using System;
using UnityEngine;

/// <summary>
/// Coordinates the session lifecycle:
/// WalletSessionController (auth) → SpacetimeClientManager (direct STDB) → FiniteEarthGameOrchestrator (game logic).
/// The gateway is no longer involved in game-state traffic; it only handles the chain relay.
/// </summary>
public class NetworkSessionCoordinator : MonoBehaviour
{
    [SerializeField] private WalletSessionController walletSession;
    [SerializeField] private SpacetimeClientManager stdbClient;
    [SerializeField] private FiniteEarthGameOrchestrator orchestrator;
    [SerializeField] private CredentialAuthOverlayPresenter credentialAuthOverlay;
    [SerializeField] private bool startOfflineByDefault = true;
    [SerializeField] private bool beginAuthenticationAutomatically;
    [SerializeField] private bool autoAuthenticateWhenDemoMode = true;

    // Held until the orchestrator's ViewModel is ready to receive it.
    private WorldSnapshotMessage pendingSnapshot;

    private void Awake()
    {
        ResolveRuntimeReferences();
        EnsureCredentialAuthOverlay();
    }

    private void ResolveRuntimeReferences()
    {
        if (walletSession == null)
            walletSession = FindAnyObjectByType<WalletSessionController>();
        if (stdbClient == null)
            stdbClient = FindAnyObjectByType<SpacetimeClientManager>();
        if (orchestrator == null)
            orchestrator = FindAnyObjectByType<FiniteEarthGameOrchestrator>();
        if (credentialAuthOverlay == null)
            credentialAuthOverlay = FindAnyObjectByType<CredentialAuthOverlayPresenter>();
    }

    private void OnEnable()
    {
        ResolveRuntimeReferences();

        if (walletSession != null)
        {
            walletSession.AuthenticationSucceeded += HandleAuthenticationSucceeded;
            walletSession.AuthenticationFailed += HandleAuthenticationFailed;
            walletSession.AuthenticationBypassed += HandleAuthenticationBypassed;
        }

        if (stdbClient != null)
        {
            stdbClient.Connected += HandleStdbConnected;
            stdbClient.Disconnected += HandleStdbDisconnected;
            stdbClient.SubscriptionReady += HandleSubscriptionReady;
            stdbClient.ActionCommitted += HandleActionCommitted;
            stdbClient.CycleStarted += HandleCycleStarted;
            stdbClient.RemoteTileChanged += HandleRemoteTileChanged;
            stdbClient.LocalPlayerUpdated += HandleLocalPlayerUpdated;
            stdbClient.ArmiesChanged += HandleArmiesChanged;
        }
    }

    private void Start()
    {
        ResolveRuntimeReferences();
        EnsureCredentialAuthOverlay();

        if (walletSession == null)
            return;

        if (walletSession.UsesCredentialAuthentication && !walletSession.IsAuthenticated)
            return;

        if (startOfflineByDefault && !walletSession.IsAuthenticated)
        {
            walletSession.BeginOfflineMode("Offline mode enabled by default.");
            return;
        }

        bool shouldAutoAuthenticate = beginAuthenticationAutomatically
            || (autoAuthenticateWhenDemoMode && walletSession.IsRuntimeDemoMode);

        if (shouldAutoAuthenticate)
            walletSession.BeginAuthentication();
    }

    private void Update()
    {
        ResolveRuntimeReferences();
        EnsureCredentialAuthOverlay();
        TryApplyPendingSnapshot();
    }

    private void OnDisable()
    {
        if (walletSession != null)
        {
            walletSession.AuthenticationSucceeded -= HandleAuthenticationSucceeded;
            walletSession.AuthenticationFailed -= HandleAuthenticationFailed;
            walletSession.AuthenticationBypassed -= HandleAuthenticationBypassed;
        }

        if (stdbClient != null)
        {
            stdbClient.Connected -= HandleStdbConnected;
            stdbClient.Disconnected -= HandleStdbDisconnected;
            stdbClient.SubscriptionReady -= HandleSubscriptionReady;
            stdbClient.ActionCommitted -= HandleActionCommitted;
            stdbClient.CycleStarted -= HandleCycleStarted;
            stdbClient.RemoteTileChanged -= HandleRemoteTileChanged;
            stdbClient.LocalPlayerUpdated -= HandleLocalPlayerUpdated;
            stdbClient.ArmiesChanged -= HandleArmiesChanged;
        }
    }

    // ── Auth callbacks ────────────────────────────────────────────────────────

    private void HandleAuthenticationSucceeded(string accessToken)
    {
        if (orchestrator != null && walletSession != null)
            orchestrator.HandleAuthenticatedPlayer(walletSession.WalletAddress, walletSession.IsOfflineMode);

        if (walletSession == null || walletSession.IsOfflineMode)
        {
            stdbClient?.Disconnect();
            return;
        }

        // Connect directly to SpacetimeDB — no gateway relay needed for game state.
        stdbClient?.Connect(walletSession.WalletAddress);
    }

    private void HandleAuthenticationFailed(string reason)
    {
        Debug.LogWarning(NormalizeAuthFailure(reason));
    }

    private void HandleAuthenticationBypassed(string reason)
    {
        Debug.Log($"Authentication bypassed: {reason}");
        stdbClient?.Disconnect();
    }

    // ── STDB client callbacks ─────────────────────────────────────────────────

    private void HandleStdbConnected()
    {
        Debug.Log("[STDB] Session connected.");
    }

    private void HandleStdbDisconnected(string reason)
    {
        pendingSnapshot = null;
        orchestrator?.ResetRealtimePendingState();
        Debug.LogWarning($"[STDB] Session disconnected: {reason}");
    }

    private void HandleSubscriptionReady(WorldSnapshotMessage snapshot)
    {
        // Store and apply via Update() so we retry until the orchestrator's
        // ViewModel is fully initialised (ApplyWorldSnapshot returns false when not ready).
        pendingSnapshot = snapshot;
        TryApplyPendingSnapshot();
    }

    private void HandleActionCommitted(ActionCommittedMessage commit)
    {
        if (commit == null) return;

        if (!commit.accepted)
        {
            Debug.LogWarning($"[STDB] Action rejected: {commit.reason}");
            return;
        }

        orchestrator?.HandleActionCommitted(commit);
    }

    private void HandleCycleStarted(CycleStartedMessage cycle)
    {
        orchestrator?.HandleCycleStarted(cycle);
    }

    private void HandleRemoteTileChanged(RemoteTileChangedMessage message)
    {
        orchestrator?.HandleRemoteTileChanged(message);
    }

    private void HandleArmiesChanged()
    {
        orchestrator?.RenderAllArmies();
    }

    private void HandleLocalPlayerUpdated(WorldPlayerSnapshotMessage player)
    {
        if (orchestrator?.ViewModel?.PlayerState == null) return;
        var ps = orchestrator.ViewModel.PlayerState;
        ps.ownedTilesCount      = player.ownedTilesCount;
        ps.sustainabilityScore  = player.sustainabilityScore;
        ps.actionsTaken         = player.actionsTaken;
        ps.actionsRemaining     = player.actionsRemaining;
        ps.lastClientSeq        = player.lastClientSeq;
        ps.resources            = new FiniteEarthResourcePool
        {
            wood     = player.wood,
            food     = player.food,
            minerals = player.minerals
        };
        ps.researchPoints       = player.researchPoints;
        ps.techBasicForestry    = player.techBasicForestry;
        ps.techRenewableEnergy  = player.techRenewableEnergy;
        ps.techCarbonCapture    = player.techCarbonCapture;
        ps.ecoActions           = player.ecoActions;
        ps.industrialActions    = player.industrialActions;
        ps.agricultureActions   = player.agricultureActions;
        if (!string.IsNullOrWhiteSpace(player.reputation))
            ps.reputationLabel  = player.reputation;
    }

    // ── Snapshot application ──────────────────────────────────────────────────

    private void TryApplyPendingSnapshot()
    {
        if (pendingSnapshot == null || orchestrator == null
            || orchestrator.ViewModel == null || orchestrator.ViewModel.WorldState == null)
        {
            return;
        }

        if (orchestrator.ApplyWorldSnapshot(pendingSnapshot))
        {
            pendingSnapshot = null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureCredentialAuthOverlay()
    {
        if (walletSession == null || !walletSession.UsesCredentialAuthentication)
            return;

        if (credentialAuthOverlay == null)
            credentialAuthOverlay = walletSession.GetComponent<CredentialAuthOverlayPresenter>();

        if (credentialAuthOverlay == null)
            credentialAuthOverlay = walletSession.gameObject.AddComponent<CredentialAuthOverlayPresenter>();
    }

    private string NormalizeAuthFailure(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "Authentication failed: SpacetimeDB unreachable.";

        if (reason.IndexOf("connection refused", StringComparison.OrdinalIgnoreCase) >= 0
            || reason.IndexOf("destination host", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Authentication failed: SpacetimeDB unreachable. Check network and STDB URI.";

        return $"Authentication failed: {reason}";
    }
}
