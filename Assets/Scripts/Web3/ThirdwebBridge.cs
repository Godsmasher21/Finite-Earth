using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class ThirdwebBridge : MonoBehaviour
{
    [Serializable]
    private sealed class BridgeResponse
    {
        public bool ok;
        public string value;
        public string error;
    }

    [DllImport("__Internal")]
    private static extern void FE_ConnectWallet(string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void FE_ConnectWalletWithStrategy(string gameObjectName, string strategy, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void FE_RequestSiweMessage(string walletAddress, string nonce, string domain, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void FE_SignMessage(string message, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void FE_GetActiveAddress(string callbackMethod);

    public event Action<string> WalletConnected;
    public event Action<string> WalletConnectionFailed;
    public event Action<string> SiweMessageCreated;
    public event Action<string> MessageSigned;
    public event Action<string> ActiveAddressResolved;

    public void ConnectWallet()
    {
        ConnectWalletWithStrategy("injected");
    }

    public void ConnectWalletWithStrategy(string strategy)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string selectedStrategy = string.IsNullOrWhiteSpace(strategy) ? "injected" : strategy.Trim().ToLowerInvariant();
        FE_ConnectWalletWithStrategy(gameObject.name, selectedStrategy, nameof(HandleConnectWalletResult));
#else
        WalletConnected?.Invoke("editor-local-wallet");
#endif
    }

    public void RequestSiweMessage(string walletAddress, string nonce, string domain)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        FE_RequestSiweMessage(walletAddress, nonce, domain, nameof(HandleSiweMessageResult));
#else
        string fake = $"Sign-In With Ethereum:\nAddress: {walletAddress}\nNonce: {nonce}\nDomain: {domain}";
        SiweMessageCreated?.Invoke(fake);
#endif
    }

    public void SignMessage(string message)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        FE_SignMessage(message, nameof(HandleSignMessageResult));
#else
        MessageSigned?.Invoke("editor-signature-placeholder");
#endif
    }

    public void GetActiveAddress()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        FE_GetActiveAddress(nameof(HandleGetActiveAddressResult));
#else
        ActiveAddressResolved?.Invoke("editor-local-wallet");
#endif
    }

    public void HandleConnectWalletResult(string json)
    {
        BridgeResponse response = Parse(json);
        if (response.ok)
        {
            WalletConnected?.Invoke(response.value);
        }
        else
        {
            WalletConnectionFailed?.Invoke(response.error);
        }
    }

    public void HandleSiweMessageResult(string json)
    {
        BridgeResponse response = Parse(json);
        if (response.ok)
        {
            SiweMessageCreated?.Invoke(response.value);
        }
        else
        {
            WalletConnectionFailed?.Invoke(response.error);
        }
    }

    public void HandleSignMessageResult(string json)
    {
        BridgeResponse response = Parse(json);
        if (response.ok)
        {
            MessageSigned?.Invoke(response.value);
        }
        else
        {
            WalletConnectionFailed?.Invoke(response.error);
        }
    }

    public void HandleGetActiveAddressResult(string json)
    {
        BridgeResponse response = Parse(json);
        if (response.ok)
        {
            ActiveAddressResolved?.Invoke(response.value);
        }
        else
        {
            WalletConnectionFailed?.Invoke(response.error);
        }
    }

    private static BridgeResponse Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BridgeResponse { ok = false, error = "Empty bridge response." };
        }

        BridgeResponse parsed = JsonUtility.FromJson<BridgeResponse>(json);
        return parsed ?? new BridgeResponse { ok = false, error = "Invalid bridge response payload." };
    }
}
