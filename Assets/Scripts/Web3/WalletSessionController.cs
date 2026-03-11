using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class WalletSessionController : MonoBehaviour
{
    [Serializable]
    private sealed class NonceEnvelope
    {
        public string walletAddress;
        public long chainId;
    }

    [Serializable]
    private sealed class VerifyEnvelope
    {
        public string message;
        public string signature;
        public string nonce;
    }

    [SerializeField] private ThirdwebBridge bridge;
    [SerializeField] private GameStateViewModel gameStateViewModel;
    [SerializeField] private string gatewayBaseUrl = "http://localhost:8080";
    [SerializeField] private long chainId = 6342;
    [SerializeField] private string domain = "finite-earth.local";
    [SerializeField] private bool useGatewayAuth = false;
    [SerializeField] private bool useDevelopmentAuthInEditor = true;
    [SerializeField] private bool webGlDemoMode = false;
    [SerializeField] private bool allowWebGlModeQueryOverride = true;
    [SerializeField] private string modeQueryParam = "mode";
    [SerializeField] private string demoQueryValue = "demo";
    [SerializeField] private string multiplayerQueryValue = "multi";
    [SerializeField] private string defaultWebLoginStrategy = "google";
    [SerializeField] private bool allowOfflineFallbackWhenGatewayUnavailable = false;
    [SerializeField] private bool allowGuestLogin = true;
    [SerializeField] private string guestWalletPrefix = "guest";
    [SerializeField] private string guestWalletPrefsKey = "finite-earth.guest-wallet";
    [SerializeField] private bool allowOfflineMode = true;
    [SerializeField] private bool forceOfflineMode;
    [SerializeField] private string offlineWalletPrefix = "offline";
    [SerializeField] private string offlineWalletPrefsKey = "finite-earth.offline-wallet";

    public string WalletAddress { get; private set; }
    public string AccessToken { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(WalletAddress);
    public bool IsOfflineMode { get; private set; }
    public bool IsRuntimeDemoMode { get; private set; }

    public event Action<string> AuthenticationSucceeded;
    public event Action<string> AuthenticationFailed;
    public event Action<string> AuthenticationBypassed;

    private string currentNonce;
    private string currentSiweMessage;

    private void Awake()
    {
        ConfigureRuntimeMode();

        if (bridge == null)
        {
            bridge = FindAnyObjectByType<ThirdwebBridge>();
        }

        if (gameStateViewModel == null)
        {
            gameStateViewModel = FindAnyObjectByType<GameStateViewModel>();
        }
    }

    private void OnEnable()
    {
        if (bridge == null)
        {
            return;
        }

        bridge.WalletConnected += HandleWalletConnected;
        bridge.WalletConnectionFailed += HandleWalletFailed;
        bridge.SiweMessageCreated += HandleSiweMessageCreated;
        bridge.MessageSigned += HandleMessageSigned;
    }

    private void OnDisable()
    {
        if (bridge == null)
        {
            return;
        }

        bridge.WalletConnected -= HandleWalletConnected;
        bridge.WalletConnectionFailed -= HandleWalletFailed;
        bridge.SiweMessageCreated -= HandleSiweMessageCreated;
        bridge.MessageSigned -= HandleMessageSigned;
    }

    public void BeginAuthentication()
    {
        BeginAuthentication(defaultWebLoginStrategy);
    }

    public void BeginAuthentication(string loginStrategy)
    {
        if (forceOfflineMode)
        {
            BeginOfflineMode("Offline mode forced by configuration.");
            return;
        }

        if (bridge == null)
        {
            AuthenticationFailed?.Invoke("ThirdwebBridge reference missing.");
            return;
        }

        bridge.ConnectWalletWithStrategy(NormalizeLoginStrategy(loginStrategy));
    }

    public void BeginGuestSession()
    {
        if (!allowGuestLogin)
        {
            AuthenticationFailed?.Invoke("Guest login is disabled.");
            return;
        }

        WalletAddress = GetOrCreateGuestWalletAddress();
        AccessToken = string.Empty;
        IsOfflineMode = true;

        if (gameStateViewModel != null)
        {
            gameStateViewModel.SetWalletAddress(WalletAddress);
        }

        AuthenticationBypassed?.Invoke("Guest session started.");
        AuthenticationSucceeded?.Invoke(string.Empty);
    }

    public void BeginOfflineMode(string reason = "Offline mode started.")
    {
        if (!allowOfflineMode)
        {
            AuthenticationFailed?.Invoke("Offline mode is disabled.");
            return;
        }

        WalletAddress = GetOrCreateOfflineWalletAddress();
        AccessToken = string.Empty;
        IsOfflineMode = true;

        if (gameStateViewModel != null)
        {
            gameStateViewModel.SetWalletAddress(WalletAddress);
        }

        AuthenticationBypassed?.Invoke(reason);
        AuthenticationSucceeded?.Invoke(string.Empty);
    }

    private void HandleWalletConnected(string address)
    {
        WalletAddress = address;
        IsOfflineMode = false;
        if (gameStateViewModel != null)
        {
            gameStateViewModel.SetWalletAddress(address);
        }

        if (!useGatewayAuth)
        {
            AccessToken = string.Empty;
            AuthenticationSucceeded?.Invoke(string.Empty);
            return;
        }

#if UNITY_EDITOR
        if (useDevelopmentAuthInEditor)
        {
            StartCoroutine(RequestDevelopmentToken());
            return;
        }
#endif

        StartCoroutine(RequestNonceAndMessage());
    }

    private void HandleWalletFailed(string error)
    {
        AuthenticationFailed?.Invoke(error);
    }

    private void HandleSiweMessageCreated(string message)
    {
        currentSiweMessage = message;
        bridge.SignMessage(message);
    }

    private void HandleMessageSigned(string signature)
    {
        StartCoroutine(VerifySignature(signature));
    }

    private IEnumerator RequestNonceAndMessage()
    {
        NonceEnvelope payload = new NonceEnvelope
        {
            walletAddress = WalletAddress,
            chainId = chainId
        };

        string json = JsonUtility.ToJson(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);
        using (UnityWebRequest request = new UnityWebRequest($"{gatewayBaseUrl}/auth/siwe/nonce", UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (TryFallbackToOfflineMode($"Nonce request failed: {request.error}"))
                {
                    yield break;
                }

                AuthenticationFailed?.Invoke($"Nonce request failed: {request.error}");
                yield break;
            }

            AuthNonceResponse response = JsonUtility.FromJson<AuthNonceResponse>(request.downloadHandler.text);
            currentNonce = response != null ? response.nonce : null;
            if (string.IsNullOrWhiteSpace(currentNonce))
            {
                AuthenticationFailed?.Invoke("Gateway did not return a nonce.");
                yield break;
            }
        }

        bridge.RequestSiweMessage(WalletAddress, currentNonce, domain);
    }

    private IEnumerator VerifySignature(string signature)
    {
        VerifyEnvelope payload = new VerifyEnvelope
        {
            message = currentSiweMessage,
            signature = signature,
            nonce = currentNonce
        };

        string json = JsonUtility.ToJson(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest($"{gatewayBaseUrl}/auth/siwe/verify", UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (TryFallbackToOfflineMode($"Verify request failed: {request.error}"))
                {
                    yield break;
                }

                AuthenticationFailed?.Invoke($"Verify request failed: {request.error}");
                yield break;
            }

            AuthVerifyResponse response = JsonUtility.FromJson<AuthVerifyResponse>(request.downloadHandler.text);
            if (response == null || string.IsNullOrWhiteSpace(response.accessToken))
            {
                AuthenticationFailed?.Invoke("Gateway did not return a valid access token.");
                yield break;
            }

            AccessToken = response.accessToken;
            AuthenticationSucceeded?.Invoke(AccessToken);
        }
    }

    private IEnumerator RequestDevelopmentToken()
    {
        string payload = JsonUtility.ToJson(new NonceEnvelope
        {
            walletAddress = WalletAddress,
            chainId = chainId
        });

        byte[] body = Encoding.UTF8.GetBytes(payload);
        using (UnityWebRequest request = new UnityWebRequest($"{gatewayBaseUrl}/auth/dev-login", UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (TryFallbackToOfflineMode($"Dev login failed: {request.error}"))
                {
                    yield break;
                }

                AuthenticationFailed?.Invoke($"Dev login failed: {request.error}");
                yield break;
            }

            AuthVerifyResponse response = JsonUtility.FromJson<AuthVerifyResponse>(request.downloadHandler.text);
            if (response == null || string.IsNullOrWhiteSpace(response.accessToken))
            {
                AuthenticationFailed?.Invoke("Dev login response missing access token.");
                yield break;
            }

            AccessToken = response.accessToken;
            AuthenticationSucceeded?.Invoke(AccessToken);
        }
    }

    private bool TryFallbackToOfflineMode(string reason)
    {
        if (!allowOfflineFallbackWhenGatewayUnavailable)
        {
            return false;
        }

        AccessToken = string.Empty;
        IsOfflineMode = true;
        AuthenticationBypassed?.Invoke(reason);
        AuthenticationSucceeded?.Invoke(string.Empty);
        return true;
    }

    private string GetOrCreateGuestWalletAddress()
    {
        string existing = PlayerPrefs.GetString(guestWalletPrefsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing.Trim().ToLowerInvariant();
        }

        uint suffix = (uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        string created = $"{guestWalletPrefix}-{suffix:x8}".ToLowerInvariant();
        PlayerPrefs.SetString(guestWalletPrefsKey, created);
        PlayerPrefs.Save();
        return created;
    }

    private string GetOrCreateOfflineWalletAddress()
    {
        string existing = PlayerPrefs.GetString(offlineWalletPrefsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing.Trim().ToLowerInvariant();
        }

        uint suffix = (uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        string created = $"{offlineWalletPrefix}-{suffix:x8}".ToLowerInvariant();
        PlayerPrefs.SetString(offlineWalletPrefsKey, created);
        PlayerPrefs.Save();
        return created;
    }

    private static string NormalizeLoginStrategy(string strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
        {
            return "google";
        }

        string normalized = strategy.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "google":
            case "email":
            case "injected":
                return normalized;
            default:
                return "google";
        }
    }

    private void ConfigureRuntimeMode()
    {
        bool baseForceOffline = forceOfflineMode;
        bool useDemoMode = webGlDemoMode;
        bool explicitMultiplayerRequested = false;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Full version should be the default in builds; demo mode must be explicit via query.
        useDemoMode = false;
#endif

        if (allowWebGlModeQueryOverride && TryReadQueryParam(Application.absoluteURL, modeQueryParam, out string modeValue))
        {
            if (string.Equals(modeValue, demoQueryValue, StringComparison.OrdinalIgnoreCase))
            {
                useDemoMode = true;
            }
            else if (string.Equals(modeValue, multiplayerQueryValue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(modeValue, "online", StringComparison.OrdinalIgnoreCase)
                || string.Equals(modeValue, "multiplayer", StringComparison.OrdinalIgnoreCase))
            {
                useDemoMode = false;
                explicitMultiplayerRequested = true;
            }
        }

        IsRuntimeDemoMode = useDemoMode;

        if (IsRuntimeDemoMode)
        {
            forceOfflineMode = true;
            allowOfflineMode = true;
            allowOfflineFallbackWhenGatewayUnavailable = true;
            allowGuestLogin = false;
            return;
        }

        if (explicitMultiplayerRequested)
        {
            forceOfflineMode = false;
            return;
        }

        forceOfflineMode = baseForceOffline;
    }

    private static bool TryReadQueryParam(string absoluteUrl, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(absoluteUrl) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        int queryIndex = absoluteUrl.IndexOf('?');
        if (queryIndex < 0 || queryIndex >= absoluteUrl.Length - 1)
        {
            return false;
        }

        string query = absoluteUrl.Substring(queryIndex + 1);
        string[] parts = query.Split('&');
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            int equalsIndex = part.IndexOf('=');
            string rawKey = equalsIndex >= 0 ? part.Substring(0, equalsIndex) : part;
            string decodedKey = Uri.UnescapeDataString(rawKey);
            if (!string.Equals(decodedKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rawValue = equalsIndex >= 0 && equalsIndex < part.Length - 1
                ? part.Substring(equalsIndex + 1)
                : string.Empty;

            value = Uri.UnescapeDataString(rawValue).Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }
}
