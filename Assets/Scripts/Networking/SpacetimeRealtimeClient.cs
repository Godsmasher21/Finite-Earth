using System;
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
    [SerializeField] private string realtimeEndpoint = "ws://localhost:8080/realtime";
    [SerializeField] private bool autoConnectOnStart;
    [SerializeField] private bool useGatewayRealtime = false;

    public bool IsConnected { get; private set; }

    public event Action Connected;
    public event Action<string> ConnectionClosed;
    public event Action<ActionCommittedMessage> ActionCommitted;
    public event Action<WorldSnapshotMessage> WorldSnapshotReceived;
    public event Action<CycleStartedMessage> CycleStarted;
    public event Action<CycleCommittedToChainMessage> CycleCommittedToChain;

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
        if (autoConnectOnStart)
        {
            await ConnectAsync(null);
        }
    }

    public async Task ConnectAsync(string accessToken)
    {
        if (!useGatewayRealtime)
        {
            IsConnected = false;
            await Task.CompletedTask;
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        if (IsConnected)
        {
            return;
        }

        string endpoint = realtimeEndpoint;
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            endpoint += endpoint.Contains("?") ? "&" : "?";
            endpoint += "token=" + Uri.EscapeDataString(accessToken);
        }

        FE_WS_Connect(endpoint, gameObject.name, nameof(HandleWebSocketOpen), nameof(HandleWebSocketMessage), nameof(HandleWebSocketClose));
        await Task.CompletedTask;
        return;
#else
        if (IsConnected)
        {
            return;
        }

        string endpoint = realtimeEndpoint;
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            endpoint += endpoint.Contains("?") ? "&" : "?";
            endpoint += "token=" + Uri.EscapeDataString(accessToken);
        }

        socket = new ClientWebSocket();
        receiveLoopTokenSource = new CancellationTokenSource();
        await socket.ConnectAsync(new Uri(endpoint), receiveLoopTokenSource.Token);

        IsConnected = true;
        Connected?.Invoke();
        _ = ReceiveLoop(receiveLoopTokenSource.Token);
#endif
    }

    public async Task DisconnectAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        FE_WS_Close();
        IsConnected = false;
        ConnectionClosed?.Invoke("client disconnect");
        await Task.CompletedTask;
#else
        if (socket == null)
        {
            return;
        }

        receiveLoopTokenSource?.Cancel();

        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client disconnect", CancellationToken.None);
        }

        socket.Dispose();
        socket = null;
        IsConnected = false;
        ConnectionClosed?.Invoke("client disconnect");
#endif
    }

    public async Task SendIntentAsync(ActionIntent intent)
    {
        ActionIntentSubmitMessage envelope = new ActionIntentSubmitMessage
        {
            intent = intent
        };

        string json = JsonUtility.ToJson(envelope);
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

#if !UNITY_WEBGL || UNITY_EDITOR
    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];

        while (!cancellationToken.IsCancellationRequested && socket != null && socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                IsConnected = false;
                ConnectionClosed?.Invoke("server closed socket");
                return;
            }

            string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            DispatchMessage(json);
        }
    }
#endif

    private void DispatchMessage(string json)
    {
        if (json.Contains("\"type\":\"ActionCommitted\""))
        {
            ActionCommitted?.Invoke(JsonUtility.FromJson<ActionCommittedMessage>(json));
            return;
        }

        if (json.Contains("\"type\":\"WorldSnapshot\""))
        {
            WorldSnapshotReceived?.Invoke(JsonUtility.FromJson<WorldSnapshotMessage>(json));
            return;
        }

        if (json.Contains("\"type\":\"CycleStarted\""))
        {
            CycleStarted?.Invoke(JsonUtility.FromJson<CycleStartedMessage>(json));
            return;
        }

        if (json.Contains("\"type\":\"CycleCommittedToChain\""))
        {
            CycleCommittedToChain?.Invoke(JsonUtility.FromJson<CycleCommittedToChainMessage>(json));
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    public void HandleWebSocketOpen(string _)
    {
        IsConnected = true;
        Connected?.Invoke();
    }

    public void HandleWebSocketMessage(string json)
    {
        DispatchMessage(json);
    }

    public void HandleWebSocketClose(string reason)
    {
        IsConnected = false;
        ConnectionClosed?.Invoke(string.IsNullOrWhiteSpace(reason) ? "closed" : reason);
    }
#endif
}
