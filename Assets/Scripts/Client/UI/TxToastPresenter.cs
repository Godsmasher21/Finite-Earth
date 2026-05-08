using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TxToastPresenter : MonoBehaviour
{
    private const string ExplorerBase = "https://explorer.testnet.megaeth.com/tx/";
    private const float DisplaySeconds = 2f;
    private const float FadeDuration = 0.15f;

    private Canvas toastCanvas;
    private CanvasGroup toastGroup;
    private TextMeshProUGUI toastLabel;
    private string pendingUrl;
    private Coroutine activeRoutine;

    private SpacetimeRealtimeClient realtimeClient;

    private static readonly Color PanelColor  = new Color(0.04f, 0.11f, 0.10f, 0.96f);
    private static readonly Color BorderColor = new Color(0.18f, 0.85f, 0.75f, 1f);
    private static readonly Color LabelColor  = new Color(0.85f, 0.94f, 0.92f, 1f);

    public void Initialize()
    {
        if (toastCanvas != null) return;
        BuildToastCanvas();
    }

    public void BindRealtimeClient(SpacetimeRealtimeClient client)
    {
        if (realtimeClient != null)
        {
            realtimeClient.CycleCommittedToChain -= HandleChainCommit;
            realtimeClient.TileNFTMinted -= HandleTileNFTMinted;
        }
        realtimeClient = client;
        if (realtimeClient != null)
        {
            realtimeClient.CycleCommittedToChain += HandleChainCommit;
            realtimeClient.TileNFTMinted += HandleTileNFTMinted;
        }
    }

    private void OnDestroy()
    {
        if (realtimeClient != null)
        {
            realtimeClient.CycleCommittedToChain -= HandleChainCommit;
            realtimeClient.TileNFTMinted -= HandleTileNFTMinted;
        }
        if (toastCanvas != null)
            Destroy(toastCanvas.gameObject);
    }

    private void HandleChainCommit(CycleCommittedToChainMessage msg)
    {
        if (string.IsNullOrEmpty(msg.transactionHash)) return;
        ShowToast($"CYCLE {msg.cycleId}  ON-CHAIN", msg.transactionHash);
    }

    private void HandleTileNFTMinted(TileNFTMintedMessage msg)
    {
        if (string.IsNullOrEmpty(msg.transactionHash)) return;
        ShowToast($"{msg.tileCount} TILE NFT{(msg.tileCount == 1 ? "" : "S")}  MINTED", msg.transactionHash);
    }

    public void ShowToast(string label, string txHash)
    {
        if (toastLabel == null) return;

        string abbrev = txHash.Length > 12
            ? txHash[..6] + "..." + txHash[^4..]
            : txHash;

        Debug.Log($"[Chain] {label} — {ExplorerBase}{txHash}");

        toastLabel.text = $"{label}  {abbrev}";
        pendingUrl = ExplorerBase + txHash;

        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(ShowRoutine());
    }

    private IEnumerator ShowRoutine()
    {
        toastGroup.gameObject.SetActive(true);

        float t = 0f;
        while (t < FadeDuration)
        {
            t += Time.unscaledDeltaTime;
            toastGroup.alpha = Mathf.Clamp01(t / FadeDuration);
            yield return null;
        }
        toastGroup.alpha = 1f;

        yield return new WaitForSecondsRealtime(DisplaySeconds);

        t = 0f;
        while (t < FadeDuration)
        {
            t += Time.unscaledDeltaTime;
            toastGroup.alpha = 1f - Mathf.Clamp01(t / FadeDuration);
            yield return null;
        }

        toastGroup.gameObject.SetActive(false);
        activeRoutine = null;
    }

    private void BuildToastCanvas()
    {
        // Root-level canvas so it's never parented under the HUD hierarchy.
        GameObject canvasGo = new GameObject("TxToastCanvas");
        DontDestroyOnLoad(canvasGo);

        toastCanvas = canvasGo.AddComponent<Canvas>();
        toastCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        toastCanvas.sortingOrder = 300;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Toast container
        RectTransform toastRect = new GameObject("Toast", typeof(RectTransform)).GetComponent<RectTransform>();
        toastRect.SetParent(canvasGo.transform, false);

        toastGroup = toastRect.gameObject.AddComponent<CanvasGroup>();
        toastGroup.alpha = 0f;
        toastRect.gameObject.SetActive(false);

        toastRect.anchorMin = new Vector2(0.5f, 0f);
        toastRect.anchorMax = new Vector2(0.5f, 0f);
        toastRect.pivot     = new Vector2(0.5f, 0f);
        toastRect.anchoredPosition = new Vector2(0f, 28f);
        toastRect.sizeDelta        = new Vector2(460f, 44f);

        // Background
        RectTransform bgRect = new GameObject("BG", typeof(RectTransform)).GetComponent<RectTransform>();
        bgRect.SetParent(toastRect, false);
        bgRect.gameObject.AddComponent<Image>().color = PanelColor;
        bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;

        // Left accent bar
        RectTransform accentRect = new GameObject("Accent", typeof(RectTransform)).GetComponent<RectTransform>();
        accentRect.SetParent(toastRect, false);
        accentRect.gameObject.AddComponent<Image>().color = BorderColor;
        accentRect.anchorMin = new Vector2(0f, 0.1f);
        accentRect.anchorMax = new Vector2(0f, 0.9f);
        accentRect.pivot     = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta        = new Vector2(3f, 0f);

        // TMP label
        RectTransform labelRect = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
        labelRect.SetParent(toastRect, false);
        toastLabel = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        toastLabel.fontSize  = 14f;
        toastLabel.color     = LabelColor;
        toastLabel.alignment = TextAlignmentOptions.Center;
        toastLabel.text      = "";
        labelRect.anchorMin  = Vector2.zero;
        labelRect.anchorMax  = Vector2.one;
        labelRect.offsetMin  = new Vector2(14f, 0f);
        labelRect.offsetMax  = new Vector2(-14f, 0f);

        // Clickable button
        Button btn = toastRect.gameObject.AddComponent<Button>();
        btn.targetGraphic = bgRect.gameObject.GetComponent<Image>();
        btn.onClick.AddListener(() =>
        {
            if (!string.IsNullOrEmpty(pendingUrl))
                Application.OpenURL(pendingUrl);
        });
    }
}
