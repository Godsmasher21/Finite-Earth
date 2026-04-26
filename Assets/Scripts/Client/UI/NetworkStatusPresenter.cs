using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NetworkStatusPresenter : MonoBehaviour
{
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private Image statusDot;
    [SerializeField] private SpacetimeClientManager stdbClient;
    [SerializeField] private WalletSessionController walletSession;

    private static readonly Color ColorOnline      = new Color(0.24f, 0.90f, 0.64f, 1f);
    private static readonly Color ColorOffline     = new Color(0.58f, 0.78f, 0.69f, 0.7f);
    private static readonly Color ColorConnecting  = new Color(0.95f, 0.74f, 0.30f, 1f);

    private float dotPulsePhase;

    private void Update()
    {
        ResolveReferences();
        Refresh();
    }

    private void ResolveReferences()
    {
        if (stdbClient == null)
            stdbClient = FindAnyObjectByType<SpacetimeClientManager>();
        if (walletSession == null)
            walletSession = FindAnyObjectByType<WalletSessionController>();
    }

    public void Initialize(TMP_Text label, Image dot = null)
    {
        statusLabel = label;
        statusDot = dot;
    }

    private void Refresh()
    {
        bool isOffline    = walletSession == null || walletSession.IsOfflineMode;
        bool isConnected  = stdbClient != null && stdbClient.IsReady;
        bool isConnecting = stdbClient != null && stdbClient.IsConnected && !stdbClient.IsReady;

        string text;
        Color dotColor;

        if (isOffline)
        {
            text = "LOCAL";
            dotColor = ColorOffline;
        }
        else if (isConnected)
        {
            text = "LIVE";
            dotColor = ColorOnline;
        }
        else if (isConnecting)
        {
            text = "LINKING";
            dotColor = ColorConnecting;
        }
        else
        {
            text = "LOCAL";
            dotColor = ColorOffline;
        }

        if (statusLabel != null)
        {
            statusLabel.text  = text;
            statusLabel.color = dotColor;
        }

        if (statusDot != null)
        {
            dotPulsePhase += Time.unscaledDeltaTime * 3f;
            float pulse = isConnecting ? (0.7f + Mathf.Sin(dotPulsePhase) * 0.3f) : 1f;
            statusDot.color = new Color(dotColor.r, dotColor.g, dotColor.b, dotColor.a * pulse);
        }
    }
}
