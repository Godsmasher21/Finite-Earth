using UnityEngine;

/// <summary>
/// Build-time production config. Place a single instance of this asset at
/// Assets/Resources/GameConfig.asset (right-click → Create → Finite Earth → Game Config).
///
/// Priority for each endpoint:
///   1. Query param at runtime (?gateway=, ?realtime=, ?domain=)  [WebGL only]
///   2. This asset                                                  [all platforms]
///   3. Per-component Inspector value (if non-loopback)            [WebGL only]
///   4. Auto-derive from page origin                               [WebGL only]
///   5. Hardcoded localhost defaults                               [fallback]
///
/// Leaving a field blank skips that slot and falls through to the next.
/// </summary>
[CreateAssetMenu(fileName = "GameConfig", menuName = "Finite Earth/Game Config")]
public sealed class GameConfig : ScriptableObject
{
    [Header("Production Endpoints")]
    [Tooltip("Gateway base URL.\nExample: https://game.yoursite.com\nLeave blank to fall through to auto-derive / localhost.")]
    public string gatewayBaseUrl = "";

    [Tooltip("WebSocket realtime URL.\nLeave blank to auto-derive from gatewayBaseUrl + /realtime.")]
    public string realtimeEndpoint = "";

    [Header("Authentication")]
    [Tooltip("SIWE domain for wallet auth.\nLeave blank to auto-derive from page host (WebGL) or use localhost fallback.")]
    public string siweDomain = "";

    [Header("MegaETH Chain Relay (chain 6342)")]
    [Tooltip("RPC used by the in-game Web3 HUD to query token balances.")]
    public string megaEthRpc = "https://6342.rpc.thirdweb.com";

    [Tooltip("TileNFT contract address on MegaETH.")]
    public string tileNftAddress = "0x8D5Edb8EE8e7CC188b6d3E974F6A19cbd42C20Df";

    [Tooltip("ForestToken (FRT) ERC-20 contract address.")]
    public string forestTokenAddress = "0xc8ca68d22Ba46391C1C32DAE749b7232B9E3Ab74";

    [Tooltip("CarbonToken (CRT) ERC-20 contract address.")]
    public string carbonTokenAddress = "0x135eECda61ba638095C6c29d0FC8fcc0B69fD947";
}
