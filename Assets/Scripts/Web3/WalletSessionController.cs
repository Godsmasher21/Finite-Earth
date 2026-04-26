using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
#if !UNITY_WEBGL || UNITY_EDITOR
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
#endif

public class WalletSessionController : MonoBehaviour
{
    [SerializeField] private ThirdwebBridge bridge;
    [SerializeField] private GameStateViewModel gameStateViewModel;
    [SerializeField] private string gatewayBaseUrl = RuntimeEndpointResolver.DefaultGatewayBaseUrl;
    [SerializeField] private long chainId = 6342;
    [SerializeField] private string domain = RuntimeEndpointResolver.DefaultSiweDomain;
    [SerializeField] private bool useGatewayAuth = false;
    [SerializeField] private bool enableCredentialAuthentication = true;
    [SerializeField] private string credentialLoginPath = "/auth/credentials/login";
    [SerializeField] private string credentialSignupPath = "/auth/credentials/signup";
    [SerializeField] private bool useDevelopmentAuthInEditor = true;
    [SerializeField] private bool webGlDemoMode = false;
    [SerializeField] private bool allowWebGlModeQueryOverride = true;
    [SerializeField] private string modeQueryParam = "mode";
    [SerializeField] private string demoQueryValue = "demo";
    [SerializeField] private string multiplayerQueryValue = "multi";
    [SerializeField] private string defaultWebLoginStrategy = "google";
    [Header("Relay")]
    [SerializeField] private bool autoStartLocalGatewayWhenUnavailable = true;
    [SerializeField, Min(2f)] private float localGatewayStartupTimeoutSeconds = 20f;
    [SerializeField, Min(0.1f)] private float localGatewayHealthPollIntervalSeconds = 0.5f;
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
    public string Username { get; private set; }
    public string DisplayName { get; private set; }
    public string AuthMode { get; private set; }
    public string GatewayBaseUrl => RuntimeEndpointResolver.ResolveGatewayBaseUrl(gatewayBaseUrl);
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(WalletAddress);
    public bool IsOfflineMode { get; private set; }
    public bool IsRuntimeDemoMode { get; private set; }
    public bool UsesCredentialAuthentication => enableCredentialAuthentication && !forceOfflineMode && !IsRuntimeDemoMode;

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

    public void BeginCredentialLogin(string username, string password)
    {
        if (!UsesCredentialAuthentication)
        {
            AuthenticationFailed?.Invoke("Credential authentication is disabled.");
            return;
        }

        if (forceOfflineMode)
        {
            BeginOfflineMode("Offline mode forced by configuration.");
            return;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            AuthenticationFailed?.Invoke("Username and password are required.");
            return;
        }

        CredentialLoginRequest payload = new CredentialLoginRequest
        {
            username = username.Trim(),
            password = password
        };

        StartCoroutine(RequestCredentialSession(BuildGatewayUrl(credentialLoginPath), JsonUtility.ToJson(payload), "Credential login failed"));
    }

    public void BeginCredentialSignup(string username, string password, string confirmPassword)
    {
        if (!UsesCredentialAuthentication)
        {
            AuthenticationFailed?.Invoke("Credential authentication is disabled.");
            return;
        }

        if (forceOfflineMode)
        {
            BeginOfflineMode("Offline mode forced by configuration.");
            return;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword))
        {
            AuthenticationFailed?.Invoke("Username, password, and confirm password are required.");
            return;
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            AuthenticationFailed?.Invoke("Password and confirm password must match.");
            return;
        }

        CredentialSignupRequest payload = new CredentialSignupRequest
        {
            username = username.Trim(),
            password = password,
            confirmPassword = confirmPassword
        };

        StartCoroutine(RequestCredentialSession(BuildGatewayUrl(credentialSignupPath), JsonUtility.ToJson(payload), "Credential signup failed"));
    }

    public void BeginGuestSession()
    {
        if (!allowGuestLogin)
        {
            AuthenticationFailed?.Invoke("Guest login is disabled.");
            return;
        }

        SetSessionIdentity(GetOrCreateGuestWalletAddress(), string.Empty, "Guest", "Guest", "guest", true);

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

        SetSessionIdentity(GetOrCreateOfflineWalletAddress(), string.Empty, "Offline", "Offline", "offline", true);

        AuthenticationBypassed?.Invoke(reason);
        AuthenticationSucceeded?.Invoke(string.Empty);
    }

    private void HandleWalletConnected(string address)
    {
        SetSessionIdentity(address, string.Empty, string.Empty, string.Empty, "wallet", false);

        if (!useGatewayAuth)
        {
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
        AuthNonceRequest payload = new AuthNonceRequest
        {
            walletAddress = WalletAddress,
            chainId = chainId
        };

        string json = JsonUtility.ToJson(payload);
        string endpoint = BuildGatewayUrl("/auth/siwe/nonce");
        bool allowRelayBootstrap = true;

        while (true)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            using (UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = BuildGatewayError("Nonce request failed", request);
                    if (allowRelayBootstrap)
                    {
                        bool shouldRetry = false;
                        string updatedError = error;
                        yield return TryAutoStartLocalGatewayAndWait(error, (ready, retryError) =>
                        {
                            shouldRetry = ready;
                            updatedError = retryError;
                        });

                        if (shouldRetry)
                        {
                            allowRelayBootstrap = false;
                            continue;
                        }

                        error = updatedError;
                    }

                    if (TryFallbackToOfflineMode(error))
                    {
                        yield break;
                    }

                    AuthenticationFailed?.Invoke(error);
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

            break;
        }

        bridge.RequestSiweMessage(WalletAddress, currentNonce, RuntimeEndpointResolver.ResolveSiweDomain(domain));
    }

    private IEnumerator VerifySignature(string signature)
    {
        AuthVerifyRequest payload = new AuthVerifyRequest
        {
            message = currentSiweMessage,
            signature = signature,
            nonce = currentNonce
        };

        string json = JsonUtility.ToJson(payload);
        string endpoint = BuildGatewayUrl("/auth/siwe/verify");
        bool allowRelayBootstrap = true;

        while (true)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            using (UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = BuildGatewayError("Verify request failed", request);
                    if (allowRelayBootstrap)
                    {
                        bool shouldRetry = false;
                        string updatedError = error;
                        yield return TryAutoStartLocalGatewayAndWait(error, (ready, retryError) =>
                        {
                            shouldRetry = ready;
                            updatedError = retryError;
                        });

                        if (shouldRetry)
                        {
                            allowRelayBootstrap = false;
                            continue;
                        }

                        error = updatedError;
                    }

                    if (TryFallbackToOfflineMode(error))
                    {
                        yield break;
                    }

                    AuthenticationFailed?.Invoke(error);
                    yield break;
                }

                AuthVerifyResponse response = JsonUtility.FromJson<AuthVerifyResponse>(request.downloadHandler.text);
                if (response == null || string.IsNullOrWhiteSpace(response.accessToken))
                {
                    AuthenticationFailed?.Invoke("Gateway did not return a valid access token.");
                    yield break;
                }

                ApplyAuthenticatedSession(response, WalletAddress, "wallet");
            }

            break;
        }
    }

    private IEnumerator RequestDevelopmentToken()
    {
        string payload = JsonUtility.ToJson(new AuthNonceRequest
        {
            walletAddress = WalletAddress,
            chainId = chainId
        });

        string endpoint = BuildGatewayUrl("/auth/dev-login");
        bool allowRelayBootstrap = true;

        while (true)
        {
            byte[] body = Encoding.UTF8.GetBytes(payload);
            using (UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = BuildGatewayError("Dev login failed", request);
                    if (allowRelayBootstrap)
                    {
                        bool shouldRetry = false;
                        string updatedError = error;
                        yield return TryAutoStartLocalGatewayAndWait(error, (ready, retryError) =>
                        {
                            shouldRetry = ready;
                            updatedError = retryError;
                        });

                        if (shouldRetry)
                        {
                            allowRelayBootstrap = false;
                            continue;
                        }

                        error = updatedError;
                    }

                    if (TryFallbackToOfflineMode(error))
                    {
                        yield break;
                    }

                    AuthenticationFailed?.Invoke(error);
                    yield break;
                }

                AuthVerifyResponse response = JsonUtility.FromJson<AuthVerifyResponse>(request.downloadHandler.text);
                if (response == null || string.IsNullOrWhiteSpace(response.accessToken))
                {
                    AuthenticationFailed?.Invoke("Dev login response missing access token.");
                    yield break;
                }

                ApplyAuthenticatedSession(response, WalletAddress, "dev");
            }

            break;
        }
    }

    private IEnumerator RequestCredentialSession(string endpoint, string json, string failurePrefix)
    {
        bool allowRelayBootstrap = true;

        while (true)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            using (UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = BuildGatewayError(failurePrefix, request);
                    if (allowRelayBootstrap)
                    {
                        bool shouldRetry = false;
                        string updatedError = error;
                        yield return TryAutoStartLocalGatewayAndWait(error, (ready, retryError) =>
                        {
                            shouldRetry = ready;
                            updatedError = retryError;
                        });

                        if (shouldRetry)
                        {
                            allowRelayBootstrap = false;
                            continue;
                        }

                        error = updatedError;
                    }

                    if (TryFallbackToOfflineMode(error))
                    {
                        yield break;
                    }

                    AuthenticationFailed?.Invoke(error);
                    yield break;
                }

                AuthVerifyResponse response = JsonUtility.FromJson<AuthVerifyResponse>(request.downloadHandler.text);
                if (response == null || string.IsNullOrWhiteSpace(response.accessToken) || string.IsNullOrWhiteSpace(response.walletAddress))
                {
                    AuthenticationFailed?.Invoke("Gateway did not return a valid credential session.");
                    yield break;
                }

                ApplyAuthenticatedSession(response, response.walletAddress, "credentials");
            }

            break;
        }
    }

    private bool TryFallbackToOfflineMode(string reason)
    {
        if (!allowOfflineFallbackWhenGatewayUnavailable)
        {
            return false;
        }

        string fallbackWallet = !string.IsNullOrWhiteSpace(WalletAddress)
            ? WalletAddress
            : GetOrCreateOfflineWalletAddress();
        string fallbackLabel = !string.IsNullOrWhiteSpace(DisplayName)
            ? DisplayName
            : (!string.IsNullOrWhiteSpace(Username) ? Username : "Offline");
        SetSessionIdentity(fallbackWallet, string.Empty, Username, fallbackLabel, "offline", true);
        AuthenticationBypassed?.Invoke(reason);
        AuthenticationSucceeded?.Invoke(string.Empty);
        return true;
    }

    private void ApplyAuthenticatedSession(AuthVerifyResponse response, string fallbackWalletAddress, string fallbackAuthMode)
    {
        string resolvedWallet = !string.IsNullOrWhiteSpace(response.walletAddress)
            ? response.walletAddress
            : fallbackWalletAddress;
        string resolvedUsername = string.IsNullOrWhiteSpace(response.username) ? string.Empty : response.username.Trim();
        string resolvedDisplayName = string.IsNullOrWhiteSpace(response.displayName) ? resolvedUsername : response.displayName.Trim();
        string resolvedAuthMode = string.IsNullOrWhiteSpace(response.authMode) ? fallbackAuthMode : response.authMode.Trim();

        SetSessionIdentity(resolvedWallet, response.accessToken, resolvedUsername, resolvedDisplayName, resolvedAuthMode, false);
        AuthenticationSucceeded?.Invoke(AccessToken);
    }

    private void SetSessionIdentity(string walletAddress, string accessToken, string username, string displayName, string authMode, bool offline)
    {
        WalletAddress = NormalizeWalletAddress(walletAddress);
        AccessToken = string.IsNullOrWhiteSpace(accessToken) ? string.Empty : accessToken.Trim();
        Username = string.IsNullOrWhiteSpace(username) ? string.Empty : username.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? (string.IsNullOrWhiteSpace(Username) ? string.Empty : Username)
            : displayName.Trim();
        AuthMode = string.IsNullOrWhiteSpace(authMode) ? string.Empty : authMode.Trim();
        IsOfflineMode = offline;

        if (gameStateViewModel != null && !string.IsNullOrWhiteSpace(WalletAddress))
        {
            gameStateViewModel.SetWalletAddress(WalletAddress);
        }
    }

    private IEnumerator TryAutoStartLocalGatewayAndWait(string error, Action<bool, string> callback)
    {
        if (!autoStartLocalGatewayWhenUnavailable || !IsGatewayUnavailableError(error) || !UsesLoopbackGateway())
        {
            callback?.Invoke(false, error);
            yield break;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        callback?.Invoke(false, $"{error} Local relay auto-start is not supported in WebGL.");
        yield break;
#else
        if (!TryResolveGatewayWorkingDirectory(out string gatewayDirectory))
        {
            callback?.Invoke(false, $"{error} Auto-start failed: backend/gateway was not found beside the project.");
            yield break;
        }

        if (!TryLaunchGatewayProcess(gatewayDirectory, out string launchError))
        {
            callback?.Invoke(false, $"{error} Auto-start failed: {launchError}");
            yield break;
        }

        Debug.Log($"WalletSessionController: starting local gateway from {gatewayDirectory}");

        bool gatewayReady = false;
        yield return WaitForGatewayHealth((ready) => gatewayReady = ready);

        callback?.Invoke(
            gatewayReady,
            gatewayReady
                ? string.Empty
                : $"{error} Auto-start launched, but the relay did not become healthy at {GatewayBaseUrl}.");
#endif
    }

    private IEnumerator WaitForGatewayHealth(Action<bool> callback)
    {
        float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(2f, localGatewayStartupTimeoutSeconds);
        string healthEndpoint = BuildGatewayUrl("/health");

        while (Time.realtimeSinceStartup < timeoutAt)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(healthEndpoint))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    // Gateway HTTP server is up — but wait for SpacetimeDB to connect too.
                    // Without this check the login request arrives while spacetimeConn is
                    // still null and the gateway returns 503 "auth not ready".
                    GatewayHealthResponse health = null;
                    try { health = JsonUtility.FromJson<GatewayHealthResponse>(request.downloadHandler?.text); }
                    catch { }

                    if (health != null && health.spacetimeReady)
                    {
                        callback?.Invoke(true);
                        yield break;
                    }
                    // SpacetimeDB not ready yet — keep polling.
                }
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, localGatewayHealthPollIntervalSeconds));
        }

        callback?.Invoke(false);
    }

    private bool UsesLoopbackGateway()
    {
        if (!Uri.TryCreate(GatewayBaseUrl, UriKind.Absolute, out Uri gatewayUri))
        {
            return false;
        }

        return gatewayUri.IsLoopback
            || string.Equals(gatewayUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(gatewayUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGatewayUnavailableError(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return true;
        }

        string normalized = reason.Trim().ToLowerInvariant();
        return normalized.Contains("destination host")
            || normalized.Contains("remote server")
            || normalized.Contains("connection refused")
            || normalized.Contains("resolve destination host")
            || normalized.Contains("unable to process request")
            || normalized.Contains("name or service not known")
            || normalized.Contains("actively refused");
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    private static bool TryResolveGatewayWorkingDirectory(out string gatewayDirectory)
    {
        string[] roots =
        {
            Directory.GetCurrentDirectory(),
            Application.dataPath,
            Path.GetDirectoryName(Application.dataPath) ?? string.Empty
        };

        for (int i = 0; i < roots.Length; i++)
        {
            if (TryFindGatewayDirectoryFromRoot(roots[i], out gatewayDirectory))
            {
                return true;
            }
        }

        gatewayDirectory = string.Empty;
        return false;
    }

    private static bool TryFindGatewayDirectoryFromRoot(string rootPath, out string gatewayDirectory)
    {
        gatewayDirectory = string.Empty;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        string current = Directory.Exists(rootPath)
            ? Path.GetFullPath(rootPath)
            : Path.GetDirectoryName(rootPath);

        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(current, "backend", "gateway");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "dist", "index.js")))
            {
                gatewayDirectory = candidate;
                return true;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return false;
    }

    private static bool TryLaunchGatewayProcess(string gatewayDirectory, out string error)
    {
        string launchError = string.Empty;
        foreach (string nodeExecutable in EnumerateNodeExecutableCandidates())
        {
            if (TryStartProcess(nodeExecutable, "--experimental-specifier-resolution=node dist/index.js", gatewayDirectory, out launchError))
            {
                error = string.Empty;
                return true;
            }
        }

        if (TryStartProcess("cmd.exe", "/c node --experimental-specifier-resolution=node dist/index.js", gatewayDirectory, out launchError))
        {
            error = string.Empty;
            return true;
        }

        if (TryStartProcess("npm.cmd", "run start", gatewayDirectory, out launchError))
        {
            error = string.Empty;
            return true;
        }

        if (TryStartProcess("cmd.exe", "/c npm.cmd run start", gatewayDirectory, out launchError))
        {
            error = string.Empty;
            return true;
        }

        error = launchError;
        return false;
    }

    private static IEnumerable<string> EnumerateNodeExecutableCandidates()
    {
        List<string> candidates = new List<string>();
        AddCommandCandidate(candidates, "node");

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        AddPathCandidate(candidates, Path.Combine(programFiles, "nodejs", "node.exe"));
        AddPathCandidate(candidates, Path.Combine(programFilesX86, "nodejs", "node.exe"));
        AddPathCandidate(candidates, Path.Combine(localAppData, "Programs", "nodejs", "node.exe"));

        string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] pathEntries = pathValue.Split(Path.PathSeparator);
        for (int i = 0; i < pathEntries.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(pathEntries[i]))
            {
                continue;
            }

            try
            {
                AddPathCandidate(candidates, Path.Combine(pathEntries[i].Trim(), "node.exe"));
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return candidates;
    }

    private static void AddCommandCandidate(List<string> candidates, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i], candidate, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        candidates.Add(candidate);
    }

    private static void AddPathCandidate(List<string> candidates, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
        {
            return;
        }

        AddCommandCandidate(candidates, candidate);
    }

    private static bool TryStartProcess(string fileName, string arguments, string workingDirectory, out string error)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process = Process.Start(startInfo);
            if (process == null)
            {
                error = $"Failed to start {fileName}.";
                return false;
            }

            if (process.WaitForExit(250))
            {
                string exitCode = process.ExitCode.ToString();
                process.Dispose();
                error = $"{fileName} exited immediately with code {exitCode}.";
                return false;
            }

            process.Dispose();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
#endif

    private string BuildGatewayUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return GatewayBaseUrl;
        }

        string normalizedBase = GatewayBaseUrl.TrimEnd('/');
        string normalizedPath = path.StartsWith("/", StringComparison.Ordinal) ? path : $"/{path}";
        return $"{normalizedBase}{normalizedPath}";
    }

    private static string BuildGatewayError(string prefix, UnityWebRequest request)
    {
        string detail = request != null ? ExtractGatewayError(request.downloadHandler != null ? request.downloadHandler.text : string.Empty) : string.Empty;
        if (string.IsNullOrWhiteSpace(detail) && request != null)
        {
            detail = request.error;
        }

        if (request != null && request.responseCode > 0)
        {
            return string.IsNullOrWhiteSpace(detail)
                ? $"{prefix} ({request.responseCode})."
                : $"{prefix} ({request.responseCode}): {detail}";
        }

        return string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix}: {detail}";
    }

    private static string ExtractGatewayError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            GatewayErrorResponse parsed = JsonUtility.FromJson<GatewayErrorResponse>(raw);
            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.error))
            {
                return parsed.error.Trim();
            }
        }
        catch
        {
            // Fall back to the raw body when the gateway response is not JSON.
        }

        return raw.Trim();
    }

    private static string NormalizeWalletAddress(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
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

public static class RuntimeEndpointResolver
{
    public const string DefaultGatewayBaseUrl = "http://localhost:8080";
    public const string DefaultRealtimeEndpoint = "ws://localhost:8080/realtime";
    public const string DefaultSiweDomain = "finite-earth.local";

    private static GameConfig _configCache;
    private static bool _configCacheLoaded;

    private static GameConfig LoadConfig()
    {
        if (!_configCacheLoaded)
        {
            _configCacheLoaded = true;
            _configCache = Resources.Load<GameConfig>("GameConfig");
        }

        return _configCache;
    }

    public static string ResolveGatewayBaseUrl(string configuredBaseUrl)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // 1. Runtime query-param override (?gateway= or ?relay=)
        if (TryReadQueryParam(Application.absoluteURL, "gateway", out string queryGateway)
            || TryReadQueryParam(Application.absoluteURL, "relay", out queryGateway))
        {
            return NormalizeHttpBaseUrl(queryGateway, DefaultGatewayBaseUrl);
        }
#endif

        // 2. Build-time production config (all platforms)
        GameConfig config = LoadConfig();
        if (config != null && !string.IsNullOrWhiteSpace(config.gatewayBaseUrl))
        {
            return NormalizeHttpBaseUrl(config.gatewayBaseUrl, DefaultGatewayBaseUrl);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // 3. Inspector-configured non-loopback value
        if (!IsLoopbackUrl(configuredBaseUrl))
        {
            return NormalizeHttpBaseUrl(configuredBaseUrl, DefaultGatewayBaseUrl);
        }

        // 4. Auto-derive from page origin
        if (TryGetOrigin(Application.absoluteURL, out string origin))
        {
            return origin;
        }
#endif

        // 5. Inspector value / hardcoded localhost fallback
        return NormalizeHttpBaseUrl(configuredBaseUrl, DefaultGatewayBaseUrl);
    }

    public static string ResolveRealtimeEndpoint(string configuredEndpoint)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // 1. Runtime query-param override (?realtime= or ?ws=)
        if (TryReadQueryParam(Application.absoluteURL, "realtime", out string queryRealtime)
            || TryReadQueryParam(Application.absoluteURL, "ws", out queryRealtime))
        {
            return NormalizeRealtimeEndpoint(queryRealtime, DefaultRealtimeEndpoint);
        }
#endif

        // 2a. Build-time config explicit realtime endpoint
        GameConfig config = LoadConfig();
        if (config != null && !string.IsNullOrWhiteSpace(config.realtimeEndpoint))
        {
            return NormalizeRealtimeEndpoint(config.realtimeEndpoint, DefaultRealtimeEndpoint);
        }

        // 2b. Derive realtime from config gateway URL
        if (config != null && !string.IsNullOrWhiteSpace(config.gatewayBaseUrl)
            && Uri.TryCreate(config.gatewayBaseUrl, UriKind.Absolute, out Uri configGatewayUri))
        {
            string wsScheme = string.Equals(configGatewayUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            return wsScheme + "://" + configGatewayUri.Authority.TrimEnd('/') + "/realtime";
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // 3. Inspector-configured non-loopback value
        if (!IsLoopbackUrl(configuredEndpoint))
        {
            return NormalizeRealtimeEndpoint(configuredEndpoint, DefaultRealtimeEndpoint);
        }

        // 4. Auto-derive from page origin
        string gatewayBase = ResolveGatewayBaseUrl(string.Empty);
        if (Uri.TryCreate(gatewayBase, UriKind.Absolute, out Uri gatewayUri))
        {
            string scheme = string.Equals(gatewayUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            string authority = gatewayUri.GetLeftPart(UriPartial.Authority).Replace(gatewayUri.Scheme + "://", scheme + "://");
            return authority.TrimEnd('/') + "/realtime";
        }
#endif

        // 5. Inspector value / hardcoded localhost fallback
        return NormalizeRealtimeEndpoint(configuredEndpoint, DefaultRealtimeEndpoint);
    }

    public static string ResolveSiweDomain(string configuredDomain)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // 1. Runtime query-param override (?domain=)
        if (TryReadQueryParam(Application.absoluteURL, "domain", out string queryDomain))
        {
            return queryDomain.Trim();
        }

        // 2. Auto-derive from page host (most authoritative for WebGL)
        if (Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out Uri absoluteUri)
            && !string.IsNullOrWhiteSpace(absoluteUri.Host))
        {
            return absoluteUri.Host;
        }
#endif

        // 3. Build-time config
        GameConfig config = LoadConfig();
        if (config != null && !string.IsNullOrWhiteSpace(config.siweDomain))
        {
            return config.siweDomain.Trim();
        }

        // 4. Inspector value / hardcoded fallback
        return string.IsNullOrWhiteSpace(configuredDomain) ? DefaultSiweDomain : configuredDomain.Trim();
    }

    private static bool TryGetOrigin(string absoluteUrl, out string origin)
    {
        origin = string.Empty;
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri absoluteUri))
        {
            return false;
        }

        origin = absoluteUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return !string.IsNullOrWhiteSpace(origin);
    }

    private static string NormalizeHttpBaseUrl(string rawUrl, string fallback)
    {
        string candidate = string.IsNullOrWhiteSpace(rawUrl) ? fallback : rawUrl.Trim();
        return candidate.TrimEnd('/');
    }

    private static string NormalizeRealtimeEndpoint(string rawUrl, string fallback)
    {
        string candidate = string.IsNullOrWhiteSpace(rawUrl) ? fallback : rawUrl.Trim();
        return candidate.TrimEnd('/');
    }

    private static bool IsLoopbackUrl(string rawUrl)
    {
        string candidate = string.IsNullOrWhiteSpace(rawUrl) ? string.Empty : rawUrl.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return true;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri))
        {
            return false;
        }

        return uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
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
