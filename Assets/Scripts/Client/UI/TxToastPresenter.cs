using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class TxToastPresenter : MonoBehaviour
{
    private const string ExplorerBase = "https://explorer.testnet.megaeth.com/tx/";
    private const float DisplaySeconds = 2f;
    private const float FadeDuration = 0.18f;

    private Canvas toastCanvas;
    private CanvasGroup toastGroup;
    private Text toastLabel;
    private Button toastButton;
    private string pendingUrl;
    private Coroutine activeRoutine;

    private SpacetimeRealtimeClient realtimeClient;

    private static readonly Color PanelColor   = new Color(0.04f, 0.11f, 0.10f, 0.96f);
    private static readonly Color BorderColor  = new Color(0.18f, 0.85f, 0.75f, 1f);
    private static readonly Color LabelColor   = new Color(0.85f, 0.94f, 0.92f, 1f);
    private static readonly Color MutedColor   = new Color(0.50f, 0.65f, 0.63f, 1f);

    public void Initialize()
    {
        if (toastCanvas != null) return; // already built
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
        string abbrev = txHash.Length > 12
            ? txHash[..6] + "..." + txHash[^4..]
            : txHash;

        toastLabel.text = $"{label}  {abbrev}";
        pendingUrl = ExplorerBase + txHash;

        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(ShowRoutine());
    }

    private IEnumerator ShowRoutine()
    {
        toastGroup.gameObject.SetActive(true);

        // Fade in
        float t = 0f;
        while (t < FadeDuration)
        {
            t += Time.unscaledDeltaTime;
            toastGroup.alpha = Mathf.Clamp01(t / FadeDuration);
            yield return null;
        }
        toastGroup.alpha = 1f;

        yield return new WaitForSecondsRealtime(DisplaySeconds);

        // Fade out
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
        GameObject canvasGo = new GameObject("TxToastCanvas");
        canvasGo.transform.SetParent(transform, false);

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
        GameObject toastGo = toastRect.gameObject;

        toastGroup = toastGo.AddComponent<CanvasGroup>();
        toastGroup.alpha = 0f;
        toastGo.SetActive(false);

        toastRect.anchorMin = new Vector2(0.5f, 0f);
        toastRect.anchorMax = new Vector2(0.5f, 0f);
        toastRect.pivot = new Vector2(0.5f, 0f);
        toastRect.anchoredPosition = new Vector2(0f, 28f);
        toastRect.sizeDelta = new Vector2(420f, 42f);

        // Background panel
        RectTransform bgRect = new GameObject("BG", typeof(RectTransform)).GetComponent<RectTransform>();
        bgRect.SetParent(toastRect, false);
        Image bg = bgRect.gameObject.AddComponent<Image>();
        bg.color = PanelColor;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Border outline
        RectTransform borderRect = new GameObject("Border", typeof(RectTransform)).GetComponent<RectTransform>();
        borderRect.SetParent(toastRect, false);
        Outline border = borderRect.gameObject.AddComponent<Outline>();
        border.effectColor = BorderColor;
        border.effectDistance = new Vector2(1f, -1f);
        Image borderImg = borderRect.gameObject.AddComponent<Image>();
        borderImg.color = Color.clear;
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = Vector2.zero;
        borderRect.offsetMax = Vector2.zero;

        // Left accent bar
        RectTransform accentRect = new GameObject("Accent", typeof(RectTransform)).GetComponent<RectTransform>();
        accentRect.SetParent(toastRect, false);
        Image accent = accentRect.gameObject.AddComponent<Image>();
        accent.color = BorderColor;
        accentRect.anchorMin = new Vector2(0f, 0.15f);
        accentRect.anchorMax = new Vector2(0f, 0.85f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = new Vector2(0f, 0f);
        accentRect.sizeDelta = new Vector2(3f, 0f);

        // Label
        RectTransform labelRect = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
        labelRect.SetParent(toastRect, false);
        toastLabel = labelRect.gameObject.AddComponent<Text>();
        toastLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        toastLabel.fontSize = 13;
        toastLabel.color = LabelColor;
        toastLabel.alignment = TextAnchor.MiddleCenter;
        toastLabel.text = "";
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12f, 0f);
        labelRect.offsetMax = new Vector2(-12f, 0f);

        // Clickable button over the whole toast
        toastButton = toastGo.AddComponent<Button>();
        toastButton.targetGraphic = bg;
        ColorBlock cb = toastButton.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1f, 1f, 1f, 1.15f);
        cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        toastButton.colors = cb;
        toastButton.onClick.AddListener(() =>
        {
            if (!string.IsNullOrEmpty(pendingUrl))
                Application.OpenURL(pendingUrl);
        });

        // Ensure EventSystem exists
        if (FindObjectOfType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }
}
