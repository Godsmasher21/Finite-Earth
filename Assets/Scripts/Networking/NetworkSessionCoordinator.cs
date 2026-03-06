using UnityEngine;

public class NetworkSessionCoordinator : MonoBehaviour
{
    [SerializeField] private WalletSessionController walletSession;
    [SerializeField] private SpacetimeRealtimeClient realtimeClient;
    [SerializeField] private FiniteEarthGameOrchestrator orchestrator;
    [SerializeField] private bool beginAuthenticationAutomatically;
    private WorldSnapshotMessage pendingSnapshot;

    private void Awake()
    {
        if (walletSession == null)
        {
            walletSession = FindAnyObjectByType<WalletSessionController>();
        }

        if (realtimeClient == null)
        {
            realtimeClient = FindAnyObjectByType<SpacetimeRealtimeClient>();
        }

        if (orchestrator == null)
        {
            orchestrator = FindAnyObjectByType<FiniteEarthGameOrchestrator>();
        }
    }

    private void OnEnable()
    {
        if (walletSession != null)
        {
            walletSession.AuthenticationSucceeded += HandleAuthenticationSucceeded;
            walletSession.AuthenticationFailed += HandleAuthenticationFailed;
            walletSession.AuthenticationBypassed += HandleAuthenticationBypassed;
        }

        if (realtimeClient != null)
        {
            realtimeClient.Connected += HandleRealtimeConnected;
            realtimeClient.ConnectionClosed += HandleRealtimeClosed;
            realtimeClient.WorldSnapshotReceived += HandleWorldSnapshot;
            realtimeClient.ActionCommitted += HandleActionCommitted;
            realtimeClient.CycleStarted += HandleCycleStarted;
            realtimeClient.CycleCommittedToChain += HandleCycleCommittedToChain;
        }
    }

    private void Start()
    {
        if (beginAuthenticationAutomatically && walletSession != null)
        {
            walletSession.BeginAuthentication();
        }
    }

    private void Update()
    {
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

        if (realtimeClient != null)
        {
            realtimeClient.Connected -= HandleRealtimeConnected;
            realtimeClient.ConnectionClosed -= HandleRealtimeClosed;
            realtimeClient.WorldSnapshotReceived -= HandleWorldSnapshot;
            realtimeClient.ActionCommitted -= HandleActionCommitted;
            realtimeClient.CycleStarted -= HandleCycleStarted;
            realtimeClient.CycleCommittedToChain -= HandleCycleCommittedToChain;
        }
    }

    private async void HandleAuthenticationSucceeded(string accessToken)
    {
        if (realtimeClient == null)
        {
            return;
        }

        if (orchestrator != null && walletSession != null)
        {
            bool localBootstrap = string.IsNullOrWhiteSpace(accessToken);
            orchestrator.HandleAuthenticatedPlayer(walletSession.WalletAddress, localBootstrap);
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            Debug.Log("Gateway auth bypassed. Running local/offline simulation mode.");
            return;
        }

        await realtimeClient.ConnectAsync(accessToken);
    }

    private void HandleAuthenticationFailed(string reason)
    {
        Debug.LogWarning($"Wallet authentication failed: {reason}");
    }

    private void HandleAuthenticationBypassed(string reason)
    {
        Debug.Log($"Wallet authentication bypassed: {reason}");
    }

    private void HandleRealtimeConnected()
    {
        Debug.Log("Realtime session connected.");
    }

    private void HandleRealtimeClosed(string reason)
    {
        Debug.LogWarning($"Realtime session closed: {reason}");
    }

    private void HandleWorldSnapshot(WorldSnapshotMessage snapshot)
    {
        pendingSnapshot = snapshot;
        TryApplyPendingSnapshot();
    }

    private void HandleActionCommitted(ActionCommittedMessage commit)
    {
        if (commit == null || !commit.accepted)
        {
            return;
        }

        Debug.Log($"Action committed: intent={commit.intentId} tick={commit.tick}");
    }

    private void HandleCycleStarted(CycleStartedMessage cycle)
    {
        if (orchestrator == null)
        {
            return;
        }

        orchestrator.HandleCycleStarted(cycle);
    }

    private void HandleCycleCommittedToChain(CycleCommittedToChainMessage message)
    {
        Debug.Log($"Cycle committed on-chain: tick={message.tick} tx={message.transactionHash}");
    }

    private void TryApplyPendingSnapshot()
    {
        if (pendingSnapshot == null || orchestrator == null || orchestrator.ViewModel == null || orchestrator.ViewModel.WorldState == null)
        {
            return;
        }

        orchestrator.ApplyWorldSnapshot(pendingSnapshot);
        pendingSnapshot = null;
    }
}
