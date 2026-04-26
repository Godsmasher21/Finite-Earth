using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class AsciiTutorialPopupPresenter : MonoBehaviour
{
    private static AsciiTutorialPopupPresenter instance;

    [Header("References")]
    [SerializeField] private WalletSessionController walletSession;
    [SerializeField] private FiniteEarthGameOrchestrator orchestrator;
    [SerializeField] private OwnershipOverlayPointTop ownership;

    [Header("Behavior")]
    [SerializeField] private bool showTutorial = true;
    [SerializeField] private bool enforceGreenAsciiTheme = true;
    [SerializeField] private bool disableIfCommandTablePresent = true;
    [SerializeField] private bool showOnlyOncePerWallet = false;
    [SerializeField] private float showDelaySeconds = 0.45f;
    [SerializeField] private string prefsPrefix = "finite-earth.tutorial.seen";
    [SerializeField] private bool allowManualHotkey = true;
    [SerializeField] private bool forceShowOnSessionStart = true;

    [Header("Popup")]
    [SerializeField] private Vector2 panelSize = new Vector2(780f, 282f);
    [SerializeField] private Vector2 panelOffset = new Vector2(16f, 16f);

    [Header("Theme")]
    [SerializeField] private Color panelColor = new Color(0.05f, 0.38f, 0.26f, 0.95f);
    [SerializeField] private Color borderColor = new Color(0.96f, 0.97f, 0.98f, 0.96f);
    [SerializeField] private Color textColor = new Color(0.95f, 0.98f, 0.96f, 1f);
    [SerializeField] private Color mutedTextColor = new Color(0.74f, 0.84f, 0.79f, 1f);

    private GameObject popupRoot;
    private Text statusText;
    private Font runtimeFont;
    private bool isVisible;
    private bool isPendingShow;
    private bool hasAutoShownThisSession;
    private float scheduledShowAt;
    private string shownWallet;
    private float sessionStartedAt;
    private bool hasForcedInitialShow;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        if (TryDisableForCommandTable())
        {
            return;
        }

        ApplyForcedThemeIfNeeded();
        hasAutoShownThisSession = false;
        sessionStartedAt = Time.unscaledTime;
        EnsureRuntimeReferences();
        CleanupDuplicatePopupRoots();
        CreatePopupIfNeeded();
        HidePopup();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        if (TryDisableForCommandTable())
        {
            return;
        }

        if (allowManualHotkey && IsTutorialHotkeyPressed())
        {
            TogglePopup();
            return;
        }

        if (!showTutorial)
        {
            return;
        }

        if (hasAutoShownThisSession)
        {
            return;
        }

        if (forceShowOnSessionStart && !hasForcedInitialShow && Time.unscaledTime >= sessionStartedAt + Mathf.Max(0.2f, showDelaySeconds))
        {
            hasForcedInitialShow = true;
            hasAutoShownThisSession = true;
            ShowNowIgnoringSeen();
            return;
        }

        EnsureRuntimeReferences();
        if (isVisible)
        {
            return;
        }

        if (!TryGetEligibleWallet(out string wallet))
        {
            return;
        }

        if (showOnlyOncePerWallet && IsTutorialSeen(wallet))
        {
            return;
        }

        if (!isPendingShow)
        {
            isPendingShow = true;
            scheduledShowAt = Time.unscaledTime + Mathf.Max(0f, showDelaySeconds);
            shownWallet = wallet;
            return;
        }

        if (Time.unscaledTime < scheduledShowAt)
        {
            return;
        }

        ShowPopup(wallet);
        hasAutoShownThisSession = true;
    }

    public void ShowNowIgnoringSeen()
    {
        if (TryDisableForCommandTable())
        {
            return;
        }

        EnsureRuntimeReferences();
        string wallet = ResolveBestWalletForTutorial();
        ShowPopup(wallet);
    }

    public void TogglePopup()
    {
        if (TryDisableForCommandTable())
        {
            return;
        }

        if (isVisible)
        {
            HidePopup();
            return;
        }

        ShowNowIgnoringSeen();
    }

    private bool TryDisableForCommandTable()
    {
        if (!disableIfCommandTablePresent)
        {
            return false;
        }

        CommandTableHudPresenter commandTable = FindAnyObjectByType<CommandTableHudPresenter>();
        if (commandTable == null || commandTable == (Object)this)
        {
            return false;
        }

        showTutorial = false;
        allowManualHotkey = false;
        HidePopup();
        enabled = false;
        return true;
    }

    private bool TryGetEligibleWallet(out string wallet)
    {
        wallet = string.Empty;

        if (orchestrator == null || orchestrator.ViewModel == null || orchestrator.ViewModel.PlayerState == null)
        {
            string fallback = ResolveBestWalletForTutorial();
            if (string.IsNullOrWhiteSpace(fallback))
            {
                return false;
            }

            wallet = fallback;
            return true;
        }

        wallet = orchestrator.ViewModel.PlayerState.walletAddress;
        if (string.IsNullOrWhiteSpace(wallet))
        {
            wallet = ResolveBestWalletForTutorial();
        }

        return !string.IsNullOrWhiteSpace(wallet);
    }

    private void ShowPopup(string wallet)
    {
        if (popupRoot == null)
        {
            CreatePopupIfNeeded();
        }

        shownWallet = string.IsNullOrWhiteSpace(wallet) ? shownWallet : wallet.Trim().ToLowerInvariant();
        isPendingShow = false;
        isVisible = true;
        popupRoot.SetActive(true);
        popupRoot.transform.SetAsLastSibling();

        if (statusText != null)
        {
            string mode = walletSession != null && walletSession.IsRuntimeDemoMode ? "DEMO" : "LIVE";
            statusText.text = $"[mode: {mode}] wallet {ShortWallet(shownWallet)}";
        }
    }

    private void HidePopup()
    {
        isVisible = false;
        isPendingShow = false;
        if (popupRoot != null)
        {
            popupRoot.SetActive(false);
        }
    }

    private void HandleGotItPressed()
    {
        if (showOnlyOncePerWallet && !string.IsNullOrWhiteSpace(shownWallet))
        {
            MarkTutorialSeen(shownWallet);
        }

        HidePopup();
    }

    private void HandleShowAgainPressed()
    {
        if (!string.IsNullOrWhiteSpace(shownWallet))
        {
            PlayerPrefs.SetInt(GetPrefsKey(shownWallet), 0);
            PlayerPrefs.Save();
        }

        HidePopup();
    }

    private void CreatePopupIfNeeded()
    {
        if (popupRoot != null)
        {
            return;
        }

        EnsureEventSystem();

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("RuntimeAsciiCanvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        runtimeFont = ResolveRuntimeFont();

        popupRoot = new GameObject("AsciiTutorialPopup");
        popupRoot.transform.SetParent(canvas.transform, false);

        RectTransform rootRect = popupRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 0f);
        rootRect.anchorMax = new Vector2(0f, 0f);
        rootRect.pivot = new Vector2(0f, 0f);
        rootRect.sizeDelta = panelSize;
        rootRect.anchoredPosition = panelOffset;

        Image bg = popupRoot.AddComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = true;

        Outline outline = popupRoot.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        VerticalLayoutGroup layout = popupRoot.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(21, 21, 18, 18);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        Text header = CreateText(popupRoot.transform, 24, textColor, TextAnchor.UpperLeft);
        header.text = "QUICK START :: FINITE EARTH";
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 39f;

        Text body = CreateText(popupRoot.transform, 20, textColor, TextAnchor.UpperLeft);
        body.text =
            "1) Left-click a tile (or drag to paint a line selection).\n" +
            "2) Choose a valid action from the right terminal panel.\n" +
            "3) Build settlements to expand your territory.\n" +
            "4) Tiles inside settlement radius are claimed automatically.\n" +
            "5) Farms passively generate food each cycle.\n" +
            "6) Press H to show/hide this help.";
        body.gameObject.AddComponent<LayoutElement>().preferredHeight = 138f;

        statusText = CreateText(popupRoot.transform, 20, mutedTextColor, TextAnchor.MiddleLeft);
        statusText.text = "[mode] wallet";
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

        GameObject buttonRow = new GameObject("Buttons");
        buttonRow.transform.SetParent(popupRoot.transform, false);
        buttonRow.AddComponent<LayoutElement>().preferredHeight = 51f;

        HorizontalLayoutGroup rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        CreateAsciiButton(buttonRow.transform, "[ HIDE (H) ]", HandleGotItPressed);
        CreateAsciiButton(buttonRow.transform, "[ SHOW NEXT RUN ]", HandleShowAgainPressed);
    }

    private void CleanupDuplicatePopupRoots()
    {
        GameObject[] roots = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject go = roots[i];
            if (go == null)
            {
                continue;
            }

            if (!string.Equals(go.name, "AsciiTutorialPopup", System.StringComparison.Ordinal))
            {
                continue;
            }

            if (popupRoot != null && go == popupRoot)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }

    private void EnsureRuntimeReferences()
    {
        if (walletSession == null)
        {
            walletSession = FindAnyObjectByType<WalletSessionController>();
        }

        if (orchestrator == null)
        {
            orchestrator = FindAnyObjectByType<FiniteEarthGameOrchestrator>();
        }

        if (ownership == null)
        {
            ownership = FindAnyObjectByType<OwnershipOverlayPointTop>();
        }
    }

    private Text CreateText(Transform parent, int fontSize, Color color, TextAnchor anchor)
    {
        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(parent, false);

        Text text = textObject.AddComponent<Text>();
        text.font = runtimeFont;
        text.fontSize = Mathf.Clamp(fontSize, 12, 36);
        text.color = color;
        text.alignment = anchor;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        text.lineSpacing = 0.95f;
        return text;
    }

    private Button CreateAsciiButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(label.Replace(" ", string.Empty));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.07f, 0.33f, 0.24f, 0.95f);

        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.07f, 0.33f, 0.24f, 0.95f);
        colors.highlightedColor = new Color(0.09f, 0.42f, 0.30f, 0.98f);
        colors.pressedColor = new Color(0.05f, 0.26f, 0.19f, 0.98f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.05f, 0.05f, 0.05f, 0.65f);
        button.colors = colors;
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        GameObject textObject = new GameObject("Label");
        textObject.transform.SetParent(buttonObject.transform, false);

        Text text = textObject.AddComponent<Text>();
        text.font = runtimeFont;
        text.fontSize = 20;
        text.color = textColor;
        text.alignment = TextAnchor.MiddleCenter;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.text = label;
        text.raycastTarget = false;

        RectTransform rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return button;
    }

    private Font ResolveRuntimeFont()
    {
        return AsciiFontResolver.ResolveFont(16);
    }

    private bool IsTutorialSeen(string wallet)
    {
        return PlayerPrefs.GetInt(GetPrefsKey(wallet), 0) == 1;
    }

    private void MarkTutorialSeen(string wallet)
    {
        PlayerPrefs.SetInt(GetPrefsKey(wallet), 1);
        PlayerPrefs.Save();
    }

    private string GetPrefsKey(string wallet)
    {
        string normalized = string.IsNullOrWhiteSpace(wallet)
            ? "default"
            : wallet.Trim().ToLowerInvariant();

        return $"{prefsPrefix}.{normalized}";
    }

    private static string ShortWallet(string wallet)
    {
        if (string.IsNullOrWhiteSpace(wallet))
        {
            return "local";
        }

        string trimmed = wallet.Trim();
        if (trimmed.Length <= 12)
        {
            return trimmed;
        }

        return $"{trimmed.Substring(0, 6)}..{trimmed.Substring(trimmed.Length - 4)}";
    }

    private static bool IsTutorialHotkeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null
            && Keyboard.current.hKey.wasPressedThisFrame)
        {
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.H);
#else
        return false;
#endif
    }

    private string ResolveBestWalletForTutorial()
    {
        if (orchestrator != null && orchestrator.ViewModel != null && orchestrator.ViewModel.PlayerState != null)
        {
            string modelWallet = orchestrator.ViewModel.PlayerState.walletAddress;
            if (!string.IsNullOrWhiteSpace(modelWallet))
            {
                return modelWallet.Trim().ToLowerInvariant();
            }
        }

        if (walletSession != null && !string.IsNullOrWhiteSpace(walletSession.WalletAddress))
        {
            return walletSession.WalletAddress.Trim().ToLowerInvariant();
        }

        return "local-player";
    }

    private void ApplyForcedThemeIfNeeded()
    {
        if (!enforceGreenAsciiTheme)
        {
            return;
        }

        panelColor = new Color(0.05f, 0.38f, 0.26f, 0.95f);
        borderColor = new Color(0.96f, 0.97f, 0.98f, 0.96f);
        textColor = new Color(0.95f, 0.98f, 0.96f, 1f);
        mutedTextColor = new Color(0.74f, 0.84f, 0.79f, 1f);
    }
}
