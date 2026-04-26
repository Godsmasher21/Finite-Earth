using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Displays on-chain web3 data in the game HUD:
///   • Player wallet address (truncated 0xAB...CD)
///   • ForestToken (FRT) balance
///   • CarbonToken (CRT) balance
///   • TileNFT count (soulbound tiles owned)
///
/// Polls MegaETH (chain 6342) via eth_call every refreshIntervalSeconds.
/// Attach to any persistent GameObject; it self-initialises via GameConfig.
/// </summary>
public class Web3HudPresenter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WalletSessionController walletSession;
    [SerializeField] private GameStateViewModel viewModel;

    [Header("UI Labels (auto-created if null)")]
    [SerializeField] private TMP_Text walletLabel;
    [SerializeField] private TMP_Text frtLabel;
    [SerializeField] private TMP_Text crtLabel;
    [SerializeField] private TMP_Text tilesLabel;

    [Header("Refresh")]
    [SerializeField, Min(10f)] private float refreshIntervalSeconds = 30f;

    private GameConfig config;
    private string lastWallet;
    private float nextRefreshAt;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private FiniteEarthGameOrchestrator orchestrator;

    private void Awake()
    {
        config = Resources.Load<GameConfig>("GameConfig");
        ResolveReferences();
    }

    private void OnEnable()
    {
        if (orchestrator == null) orchestrator = FindAnyObjectByType<FiniteEarthGameOrchestrator>();
        if (orchestrator != null) orchestrator.ActionExecuted += OnActionExecuted;
    }

    private void OnDisable()
    {
        if (orchestrator != null) orchestrator.ActionExecuted -= OnActionExecuted;
    }

    private void OnActionExecuted(FiniteEarthActionType actionType, int tileCount)
    {
        // Claim = new tile(s) owned → TileNFT will be minted → refresh count immediately.
        if (actionType == FiniteEarthActionType.Claim)
            ForceRefresh();
    }

    private void Update()
    {
        string wallet = ResolveWallet();
        if (string.IsNullOrWhiteSpace(wallet)) return;

        // Immediately refresh when wallet changes (login / account switch)
        if (wallet != lastWallet)
        {
            lastWallet = wallet;
            nextRefreshAt = 0f;
        }

        if (Time.unscaledTime >= nextRefreshAt)
        {
            nextRefreshAt = Time.unscaledTime + refreshIntervalSeconds;
            StartCoroutine(RefreshBalances(wallet));
        }

        RefreshWalletLabel(wallet);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Force an immediate refresh (call after a tile claim for instant feedback).</summary>
    public void ForceRefresh()
    {
        nextRefreshAt = 0f;
    }

    // ── Wallet label ──────────────────────────────────────────────────────────

    private void RefreshWalletLabel(string wallet)
    {
        if (walletLabel == null) return;
        walletLabel.text = TruncateWallet(wallet);
    }

    private static string TruncateWallet(string wallet)
    {
        if (string.IsNullOrWhiteSpace(wallet)) return "--";
        if (wallet.Length <= 10) return wallet.ToUpperInvariant();
        return $"{wallet[..6]}...{wallet[^4..]}";
    }

    // ── Balance fetching ──────────────────────────────────────────────────────

    private IEnumerator RefreshBalances(string wallet)
    {
        if (config == null) yield break;
        string rpc = config.megaEthRpc;
        if (string.IsNullOrWhiteSpace(rpc)) yield break;

        // balanceOf(address) selector: 0x70a08231
        string paddedAddr = PadAddress(wallet);
        string callData   = "0x70a08231" + paddedAddr;

        // Fire all three calls simultaneously
        UnityWebRequest tileReq = MakeEthCall(rpc, config.tileNftAddress,     callData);
        UnityWebRequest frtReq  = MakeEthCall(rpc, config.forestTokenAddress, callData);
        UnityWebRequest crtReq  = MakeEthCall(rpc, config.carbonTokenAddress, callData);

        yield return tileReq.SendWebRequest();
        yield return frtReq.SendWebRequest();
        yield return crtReq.SendWebRequest();

        if (tilesLabel  != null) tilesLabel.text  = $"TILES {ParseUint(tileReq)}";
        if (frtLabel    != null) frtLabel.text     = $"FRT {ParseErc20(frtReq)}";
        if (crtLabel    != null) crtLabel.text     = $"CRT {ParseErc20(crtReq)}";

        tileReq.Dispose();
        frtReq.Dispose();
        crtReq.Dispose();
    }

    private static UnityWebRequest MakeEthCall(string rpc, string toAddr, string data)
    {
        string body = $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_call\",\"params\":[{{\"to\":\"{toAddr}\",\"data\":\"{data}\"}},\"latest\"]}}";
        var req = new UnityWebRequest(rpc, "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 8;
        return req;
    }

    private static string ParseUint(UnityWebRequest req)
    {
        try
        {
            if (req.result != UnityWebRequest.Result.Success) return "--";
            string text = req.downloadHandler.text;
            int start = text.IndexOf("\"result\":\"0x", StringComparison.Ordinal);
            if (start < 0) return "0";
            start += 11; // skip "result":"
            int end = text.IndexOf('"', start);
            if (end < 0) return "0";
            string hex = text.Substring(start, end - start);
            if (hex.Length > 16) hex = hex.Substring(hex.Length - 16); // clamp
            ulong val = Convert.ToUInt64(hex.TrimStart('0').PadLeft(1, '0'), 16);
            return val.ToString("N0");
        }
        catch { return "--"; }
    }

    private static string ParseErc20(UnityWebRequest req)
    {
        try
        {
            if (req.result != UnityWebRequest.Result.Success) return "--";
            string text = req.downloadHandler.text;
            int start = text.IndexOf("\"result\":\"0x", StringComparison.Ordinal);
            if (start < 0) return "0";
            start += 11;
            int end = text.IndexOf('"', start);
            if (end < 0) return "0";
            string hex = text.Substring(start, end - start).TrimStart('0');
            if (hex.Length == 0) return "0";
            // Parse high 128 bits (ignore fractional 1e18 part for display)
            // Divide by 1e18: the last 18 decimal digits are fractional
            if (hex.Length <= 18)
            {
                // Less than 1 whole token
                return "0";
            }
            string wholeHex = hex.Substring(0, hex.Length - 18);
            if (wholeHex.Length > 16) wholeHex = wholeHex.Substring(wholeHex.Length - 16);
            ulong whole = Convert.ToUInt64(wholeHex, 16);
            return whole.ToString("N0");
        }
        catch { return "--"; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ResolveWallet()
    {
        if (walletSession != null && !string.IsNullOrWhiteSpace(walletSession.WalletAddress))
            return walletSession.WalletAddress;
        if (viewModel?.PlayerState != null && !string.IsNullOrWhiteSpace(viewModel.PlayerState.walletAddress))
            return viewModel.PlayerState.walletAddress;
        return string.Empty;
    }

    private static string PadAddress(string addr)
    {
        string hex = addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? addr.Substring(2)
            : addr;
        return hex.PadLeft(64, '0').ToLowerInvariant();
    }

    private void ResolveReferences()
    {
        if (walletSession == null) walletSession = FindAnyObjectByType<WalletSessionController>();
        if (viewModel     == null) viewModel     = FindAnyObjectByType<GameStateViewModel>();
    }

    // ── Label injection (called by CommandTableHudPresenter) ──────────────────

    public void InjectLabels(TMP_Text wallet, TMP_Text frt, TMP_Text crt, TMP_Text tiles)
    {
        walletLabel = wallet;
        frtLabel    = frt;
        crtLabel    = crt;
        tilesLabel  = tiles;
    }
}
