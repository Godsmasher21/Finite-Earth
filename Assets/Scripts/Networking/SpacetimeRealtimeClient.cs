using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using System.Runtime.InteropServices;

#if !UNITY_WEBGL || UNITY_EDITOR
using System.Net.WebSockets;
#endif

public class SpacetimeRealtimeClient : MonoBehaviour
{
    [SerializeField] private string realtimeEndpoint = RuntimeEndpointResolver.DefaultRealtimeEndpoint;
    [SerializeField] private bool autoConnectOnStart;

    [Header("Reconnect")]
    [SerializeField, Min(1f)] private float reconnectDelaySeconds = 2f;
    [SerializeField, Min(1f)] private float reconnectMaxDelaySeconds = 60f;
    [SerializeField, Min(1)] private int reconnectMaxAttempts = 10;

    public bool IsConnected { get; private set; }
    public bool IsConnecting { get; private set; }
    public int ReconnectAttempts { get; private set; }

    private bool realtimeEnabled;
    private string pendingAccessToken;
    private bool reconnectScheduled;
    private float reconnectAt;
    private float currentReconnectDelay;

    public event Action Connected;
    public event Action<string> ConnectionClosed;
    public event Action<ActionCommittedMessage> ActionCommitted;
    public event Action<WorldSnapshotMessage> WorldSnapshotReceived;
    public event Action<CycleStartedMessage> CycleStarted;
    public event Action<CycleCommittedToChainMessage> CycleCommittedToChain;
    public event Action<RemoteTileChangedMessage> RemoteTileChanged;
    public event Action<PlayerJoinedMessage> PlayerJoined;
    public event Action<PlayerLeftMessage> PlayerLeft;
    public event Action<TileNFTMintedMessage> TileNFTMinted;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void FE_WS_Connect(string url, string gameObjectName, string onOpenMethod, string onMessageMethod, string onCloseMethod);

    [DllImport("__Internal")]
    private static extern void FE_WS_Send(string payload);

    [DllImport("__Internal")]
    private static extern void FE_WS_Close();
#endif

#if !UNITY_WEBGL || UNITY_EDITOR
    private ClientWebSocket socket;
    private CancellationTokenSource receiveLoopTokenSource;
#endif

    private async void Start()
    {
        currentReconnectDelay = reconnectDelaySeconds;
        if (autoConnectOnStart)
        {
            await ConnectAsync(null);
        }
    }

    private void Update()
    {
        if (!reconnectScheduled || !realtimeEnabled)
        {
            return;
        }

        if (Time.unscaledTime >= reconnectAt)
        {
            reconnectScheduled = false;
            _ = ConnectAsync(pendingAccessToken);
        }
    }

    // Called by NetworkSessionCoordinator when a real multiplayer session begins.
    public void EnableRealtime(string accessToken)
    {
        realtimeEnabled = true;
        pendingAccessToken = accessToken;
        currentReconnectDelay = reconnectDelaySeconds;
        ReconnectAttempts = 0;
        _ = ConnectAsync(accessToken);
    }

    // Called when switching to offline/guest mode.
    public void DisableRealtime()
    {
        realtimeEnabled = false;
        reconnectScheduled = false;
        _ = DisconnectAsync();
    }

    public async Task ConnectAsync(string accessToken)
    {
        if (!realtimeEnabled)
        {
            IsConnected = false;
            await Task.CompletedTask;
            return;
        }

        if (IsConnecting || IsConnected)
        {
            return;
        }

        IsConnecting = true;
        pendingAccessToken = accessToken;

#if UNITY_WEBGL && !UNITY_EDITOR
        string endpoint = BuildEndpointUrl(accessToken);
        FE_WS_Connect(endpoint, gameObject.name, nameof(HandleWebSocketOpen), nameof(HandleWebSocketMessage), nameof(HandleWebSocketClose));
        await Task.CompletedTask;
        return;
#else
        string endpoint = BuildEndpointUrl(accessToken);
        try
        {
            socket = new ClientWebSocket();
            receiveLoopTokenSource = new CancellationTokenSource();
            await socket.ConnectAsync(new Uri(endpoint), receiveLoopTokenSource.Token);

            IsConnected = true;
            IsConnecting = false;
            ReconnectAttempts = 0;
            currentReconnectDelay = reconnectDelaySeconds;
            Connected?.Invoke();
            _ = ReceiveLoop(receiveLoopTokenSource.Token);
        }
        catch (Exception ex)
        {
            IsConnecting = false;
            IsConnected = false;
            Debug.LogWarning($"SpacetimeRealtimeClient: connection failed — {ex.Message}");
            ScheduleReconnect();
        }
#endif
    }

    public async Task DisconnectAsync()
    {
        reconnectScheduled = false;

#if UNITY_WEBGL && !UNITY_EDITOR
        FE_WS_Close();
        IsConnected = false;
        IsConnecting = false;
        ConnectionClosed?.Invoke("client disconnect");
        await Task.CompletedTask;
#else
        if (socket == null)
        {
            IsConnected = false;
            IsConnecting = false;
            await Task.CompletedTask;
            return;
        }

        receiveLoopTokenSource?.Cancel();

        if (socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client disconnect", CancellationToken.None);
            }
            catch { /* ignore close errors */ }
        }

        socket.Dispose();
        socket = null;
        IsConnected = false;
        IsConnecting = false;
        ConnectionClosed?.Invoke("client disconnect");
#endif
    }

    public async Task SendIntentAsync(ActionIntent intent)
    {
        ActionIntentSubmitMessage envelope = new ActionIntentSubmitMessage { intent = intent };
        string json = JsonUtility.ToJson(envelope);
        Debug.Log($"Realtime send intent: action={intent.actionType} q={intent.q} r={intent.r} seq={intent.clientSeq}");
        await SendRawAsync(json);
    }

    public async Task SendIntentBatchAsync(ActionIntent[] intents)
    {
        if (intents == null || intents.Length == 0)
        {
            return;
        }

        ActionIntentBatchSubmitMessage envelope = new ActionIntentBatchSubmitMessage { intents = intents };
        string json = JsonUtility.ToJson(envelope);
        Debug.Log($"Realtime send batch: count={intents.Length} firstSeq={intents[0].clientSeq} lastSeq={intents[intents.Length - 1].clientSeq}");
        await SendRawAsync(json);
    }

    public async Task SendRawAsync(string json)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        FE_WS_Send(json);
        await Task.CompletedTask;
#else
        if (socket == null || socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Realtime socket is not connected.");
        }

        byte[] payload = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(
            new ArraySegment<byte>(payload),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
#endif
    }

    private void ScheduleReconnect()
    {
        if (!realtimeEnabled || reconnectScheduled)
        {
            return;
        }

        if (reconnectMaxAttempts > 0 && ReconnectAttempts >= reconnectMaxAttempts)
        {
            Debug.LogWarning($"SpacetimeRealtimeClient: max reconnect attempts ({reconnectMaxAttempts}) reached. Giving up.");
            return;
        }

        ReconnectAttempts++;
        reconnectAt = Time.unscaledTime + currentReconnectDelay;
        reconnectScheduled = true;
        Debug.Log($"SpacetimeRealtimeClient: reconnect #{ReconnectAttempts} scheduled in {currentReconnectDelay:0.0}s");

        currentReconnectDelay = Mathf.Min(currentReconnectDelay * 2f, reconnectMaxDelaySeconds);
    }

    private string BuildEndpointUrl(string accessToken)
    {
        string endpoint = RuntimeEndpointResolver.ResolveRealtimeEndpoint(realtimeEndpoint);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            endpoint += endpoint.Contains("?") ? "&" : "?";
            endpoint += "token=" + Uri.EscapeDataString(accessToken);
        }
        return endpoint;
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8 * 1024];

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket != null && socket.State == WebSocketState.Open)
            {
                string json = await ReceiveTextMessageAsync(buffer, cancellationToken);
                if (json == null)
                {
                    IsConnected = false;
                    ConnectionClosed?.Invoke("server closed socket");
                    ScheduleReconnect();
                    return;
                }

                DispatchMessage(json);
            }
        }
        catch (OperationCanceledException) { /* clean shutdown */ }
        catch (Exception ex)
        {
            Debug.LogWarning($"SpacetimeRealtimeClient: receive error — {ex.Message}");
            IsConnected = false;
            ConnectionClosed?.Invoke("receive error");
            ScheduleReconnect();
        }
    }

    private async Task<string> ReceiveTextMessageAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        using MemoryStream messageBuffer = new MemoryStream();

        while (!cancellationToken.IsCancellationRequested && socket != null && socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.Count > 0)
            {
                messageBuffer.Write(buffer, 0, result.Count);
            }

            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
            }
        }

        return null;
    }
#endif

    private void DispatchMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        if (json.Contains("\"type\":\"ActionCommitted\""))
        {
            ActionCommitted?.Invoke(JsonUtility.FromJson<ActionCommittedMessage>(json));
        }
        else if (json.Contains("\"type\":\"WorldSnapshot\""))
        {
            WorldSnapshotReceived?.Invoke(JsonUtility.FromJson<WorldSnapshotMessage>(json));
        }
        else if (json.Contains("\"type\":\"CycleStarted\""))
        {
            CycleStarted?.Invoke(JsonUtility.FromJson<CycleStartedMessage>(json));
        }
        else if (json.Contains("\"type\":\"CycleCommittedToChain\""))
        {
            CycleCommittedToChain?.Invoke(JsonUtility.FromJson<CycleCommittedToChainMessage>(json));
        }
        else if (json.Contains("\"type\":\"RemoteTileChanged\""))
        {
            RemoteTileChanged?.Invoke(JsonUtility.FromJson<RemoteTileChangedMessage>(json));
        }
        else if (json.Contains("\"type\":\"PlayerJoined\""))
        {
            PlayerJoined?.Invoke(JsonUtility.FromJson<PlayerJoinedMessage>(json));
        }
        else if (json.Contains("\"type\":\"PlayerLeft\""))
        {
            PlayerLeft?.Invoke(JsonUtility.FromJson<PlayerLeftMessage>(json));
        }
        else if (json.Contains("\"type\":\"TileNFTMinted\""))
        {
            TileNFTMinted?.Invoke(JsonUtility.FromJson<TileNFTMintedMessage>(json));
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    public void HandleWebSocketOpen(string _)
    {
        IsConnected = true;
        IsConnecting = false;
        ReconnectAttempts = 0;
        currentReconnectDelay = reconnectDelaySeconds;
        Connected?.Invoke();
    }

    public void HandleWebSocketMessage(string json)
    {
        DispatchMessage(json);
    }

    public void HandleWebSocketClose(string reason)
    {
        IsConnected = false;
        IsConnecting = false;
        ConnectionClosed?.Invoke(string.IsNullOrWhiteSpace(reason) ? "closed" : reason);
        ScheduleReconnect();
    }
#endif
}
