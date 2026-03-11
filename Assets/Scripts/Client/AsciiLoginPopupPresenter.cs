using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class AsciiLoginPopupPresenter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WalletSessionController walletSession;
    [SerializeField] private FiniteEarthGameOrchestrator orchestrator;

    [Header("Popup")]
    [SerializeField] private bool showOnStartup = false;
    [SerializeField] private bool hidePopupInDemoMode = true;
    [SerializeField] private Vector2 panelSize = new Vector2(840f, 570f);
    [SerializeField] private string offlineWalletPrefsKey = "finite-earth.offline-wallet";
    [SerializeField] private string offlineWalletPrefix = "offline";

    [Header("Theme")]
    [SerializeField] private Color overlayColor = new Color(0f, 0f, 0f, 0.52f);
    [SerializeField] private Color panelColor = new Color(0.01f, 0.03f, 0.03f, 0.95f);
    [SerializeField] private Color borderColor = new Color(0.22f, 0.95f, 0.61f, 1f);
    [SerializeField] private Color textColor = new Color(0.90f, 0.98f, 0.94f, 1f);
    [SerializeField] private Color mutedTextColor = new Color(0.56f, 0.70f, 0.64f, 1f);

    private GameObject popupRoot;
    private Button googleButton;
    private Button emailButton;
    private Button injectedWalletButton;
    private Button guestButton;
    private Button offlineButton;
    private Text statusText;
    private bool wasAuthenticated;
    private Font runtimeFont;
    private bool walletEventsBound;

    private void Awake()
    {
        EnsureRuntimeReferences();

        if (showOnStartup)
        {
            CreatePopupIfNeeded();

            if (ShouldBypassPopupForDemo())
            {
                HidePopup();
                wasAuthenticated = true;
                return;
            }

            ShowPopup();
        }
    }

    private void OnEnable()
    {
        EnsureRuntimeReferences();
        BindWalletEvents();
    }

    private void OnDisable()
    {
        UnbindWalletEvents();
    }

    private void HandleGooglePressed()
    {
        EnsureRuntimeReferences();
        BindWalletEvents();

        if (walletSession == null)
        {
            SetStatus("[!] WalletSession missing.");
            return;
        }

        SetButtonsInteractable(false);
        SetStatus("[*] Connecting with Google...");
        walletSession.BeginAuthentication("google");
    }

    private void HandleCreateAccountPressed()
    {
        EnsureRuntimeReferences();
        BindWalletEvents();

        if (walletSession == null)
        {
            SetStatus("[!] WalletSession missing.");
            return;
        }

        SetButtonsInteractable(false);
        SetStatus("[*] Opening email account flow...");
        walletSession.BeginAuthentication("email");
    }

    private void HandleInjectedWalletPressed()
    {
        EnsureRuntimeReferences();
        BindWalletEvents();

        if (walletSession == null)
        {
            SetStatus("[!] WalletSession missing.");
            return;
        }

        SetButtonsInteractable(false);
        SetStatus("[*] Connecting injected wallet...");
        walletSession.BeginAuthentication("injected");
    }

    private void HandleGuestPressed()
    {
        EnsureRuntimeReferences();
        BindWalletEvents();

        if (walletSession == null)
        {
            SetStatus("[!] WalletSession missing.");
            return;
        }

        SetButtonsInteractable(false);
        SetStatus("[*] Starting guest session...");
        walletSession.BeginGuestSession();
    }

    private void HandleOfflinePressed()
    {
        EnsureOrchestratorReference();
        if (walletSession == null)
        {
            walletSession = FindAnyObjectByType<WalletSessionController>();
        }

        SetButtonsInteractable(false);
        SetStatus("[*] Starting offline session...");

        if (walletSession != null)
        {
            BindWalletEvents();
            walletSession.BeginOfflineMode();
            return;
        }

        if (orchestrator == null)
        {
            SetStatus("[!] Offline mode failed: Game orchestrator missing.");
            SetButtonsInteractable(true);
            return;
        }

        string offlineWallet = GetOrCreateOfflineWalletAddress();
        orchestrator.HandleAuthenticatedPlayer(offlineWallet, true);
        wasAuthenticated = true;
        SetStatus("[+] Offline mode active. Entering local world...");
        HidePopup();
    }

    private void HandleAuthenticationSucceeded(string accessToken)
    {
        string wallet = walletSession != null ? walletSession.WalletAddress : string.Empty;
        bool localBootstrap = string.IsNullOrWhiteSpace(accessToken);

        if (orchestrator != null && !string.IsNullOrWhiteSpace(wallet))
        {
            orchestrator.HandleAuthenticatedPlayer(wallet, localBootstrap);
        }

        bool isGuest = !string.IsNullOrWhiteSpace(wallet) && wallet.StartsWith("guest-", System.StringComparison.OrdinalIgnoreCase);
        bool isOffline = !string.IsNullOrWhiteSpace(wallet) && wallet.StartsWith("offline-", System.StringComparison.OrdinalIgnoreCase);
        wasAuthenticated = true;
        if (isOffline)
        {
            SetStatus("[+] Offline mode active. Entering local world...");
        }
        else if (isGuest)
        {
            SetStatus("[+] Guest login ok. Entering world...");
        }
        else
        {
            SetStatus(localBootstrap
                ? "[+] Login ok (local mode). Entering world..."
                : "[+] Login ok. Entering world...");
        }

        HidePopup();
    }

    private void HandleAuthenticationFailed(string reason)
    {
        SetButtonsInteractable(true);
        SetStatus($"[!] Login failed: {reason}");
    }

    private void HandleAuthenticationBypassed(string reason)
    {
        SetStatus($"[~] Offline mode: {reason}");
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

        popupRoot = new GameObject("AsciiLoginPopup");
        popupRoot.transform.SetParent(canvas.transform, false);
        popupRoot.AddComponent<CanvasRenderer>();

        RectTransform rootRect = popupRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        Image overlay = popupRoot.AddComponent<Image>();
        overlay.color = overlayColor;
        overlay.raycastTarget = true;

        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(popupRoot.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = panelSize;
        panelRect.anchoredPosition = Vector2.zero;

        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = panelColor;
        panelImage.raycastTarget = true;

        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(21, 21, 21, 21);
        layout.spacing = 15f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        Text header = CreateText(panel.transform, 27, textColor, TextAnchor.UpperLeft);
        header.text =
            "+----------------------------------------------+\n" +
            "| FINITE EARTH :: ACCESS TERMINAL             |\n" +
            "| AUTH OR GUEST ACCESS TO ENTER WORLD         |\n" +
            "+----------------------------------------------+";
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 132f;

        Text body = CreateText(panel.transform, 24, mutedTextColor, TextAnchor.UpperLeft);
        body.text =
            "Identity options:\n" +
            "1) Google login (Thirdweb in-app wallet)\n" +
            "2) Create account (email in-app wallet)\n" +
            "3) External injected wallet (MetaMask etc)";
        body.gameObject.AddComponent<LayoutElement>().preferredHeight = 126f;

        GameObject buttonsRow = new GameObject("ButtonsRow");
        buttonsRow.transform.SetParent(panel.transform, false);
        LayoutElement rowLayoutElement = buttonsRow.AddComponent<LayoutElement>();
        rowLayoutElement.preferredHeight = 63f;

        HorizontalLayoutGroup buttonsLayout = buttonsRow.AddComponent<HorizontalLayoutGroup>();
        buttonsLayout.spacing = 10f;
        buttonsLayout.childAlignment = TextAnchor.MiddleCenter;
        buttonsLayout.childControlWidth = true;
        buttonsLayout.childControlHeight = true;
        buttonsLayout.childForceExpandWidth = true;
        buttonsLayout.childForceExpandHeight = true;

        googleButton = CreateAsciiButton(buttonsRow.transform, "[ GOOGLE LOGIN ]", HandleGooglePressed);
        emailButton = CreateAsciiButton(buttonsRow.transform, "[ CREATE ACCOUNT ]", HandleCreateAccountPressed);

        GameObject walletRow = new GameObject("WalletRow");
        walletRow.transform.SetParent(panel.transform, false);
        LayoutElement walletRowLayout = walletRow.AddComponent<LayoutElement>();
        walletRowLayout.preferredHeight = 63f;

        HorizontalLayoutGroup walletLayout = walletRow.AddComponent<HorizontalLayoutGroup>();
        walletLayout.spacing = 10f;
        walletLayout.childAlignment = TextAnchor.MiddleCenter;
        walletLayout.childControlWidth = true;
        walletLayout.childControlHeight = true;
        walletLayout.childForceExpandWidth = true;
        walletLayout.childForceExpandHeight = true;

        injectedWalletButton = CreateAsciiButton(walletRow.transform, "[ INJECTED WALLET ]", HandleInjectedWalletPressed);
        guestButton = CreateAsciiButton(walletRow.transform, "[ CONTINUE AS GUEST ]", HandleGuestPressed);

        GameObject offlineRow = new GameObject("OfflineRow");
        offlineRow.transform.SetParent(panel.transform, false);
        LayoutElement offlineRowLayout = offlineRow.AddComponent<LayoutElement>();
        offlineRowLayout.preferredHeight = 63f;

        HorizontalLayoutGroup offlineLayout = offlineRow.AddComponent<HorizontalLayoutGroup>();
        offlineLayout.spacing = 0f;
        offlineLayout.childAlignment = TextAnchor.MiddleCenter;
        offlineLayout.childControlWidth = true;
        offlineLayout.childControlHeight = true;
        offlineLayout.childForceExpandWidth = true;
        offlineLayout.childForceExpandHeight = true;

        offlineButton = CreateAsciiButton(offlineRow.transform, "[ OFFLINE MODE ]", HandleOfflinePressed);

        statusText = CreateText(panel.transform, 21, mutedTextColor, TextAnchor.UpperLeft);
        statusText.text = "[idle] choose a login method.";
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
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
        EnsureOrchestratorReference();

        if (walletSession == null)
        {
            walletSession = FindAnyObjectByType<WalletSessionController>();
        }

        if (walletSession == null)
        {
            GameObject host = orchestrator != null ? orchestrator.gameObject : gameObject;
            walletSession = host.GetComponent<WalletSessionController>();
            if (walletSession == null)
            {
                walletSession = host.AddComponent<WalletSessionController>();
            }
        }
    }

    private void EnsureOrchestratorReference()
    {
        if (orchestrator == null)
        {
            orchestrator = FindAnyObjectByType<FiniteEarthGameOrchestrator>();
        }
    }

    private void BindWalletEvents()
    {
        if (walletEventsBound || walletSession == null)
        {
            return;
        }

        walletSession.AuthenticationSucceeded += HandleAuthenticationSucceeded;
        walletSession.AuthenticationFailed += HandleAuthenticationFailed;
        walletSession.AuthenticationBypassed += HandleAuthenticationBypassed;
        walletEventsBound = true;
    }

    private void UnbindWalletEvents()
    {
        if (!walletEventsBound || walletSession == null)
        {
            walletEventsBound = false;
            return;
        }

        walletSession.AuthenticationSucceeded -= HandleAuthenticationSucceeded;
        walletSession.AuthenticationFailed -= HandleAuthenticationFailed;
        walletSession.AuthenticationBypassed -= HandleAuthenticationBypassed;
        walletEventsBound = false;
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

    private void ShowPopup()
    {
        CreatePopupIfNeeded();
        popupRoot.SetActive(!wasAuthenticated);
        SetButtonsInteractable(true);
    }

    private void HidePopup()
    {
        if (popupRoot != null)
        {
            popupRoot.SetActive(false);
        }
    }

    private bool ShouldBypassPopupForDemo()
    {
        if (!hidePopupInDemoMode || walletSession == null)
        {
            return false;
        }

        return walletSession.IsRuntimeDemoMode;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (googleButton != null)
        {
            googleButton.interactable = interactable;
        }

        if (emailButton != null)
        {
            emailButton.interactable = interactable;
        }

        if (injectedWalletButton != null)
        {
            injectedWalletButton.interactable = interactable;
        }

        if (guestButton != null)
        {
            guestButton.interactable = interactable;
        }

        if (offlineButton != null)
        {
            offlineButton.interactable = interactable;
        }
    }

    private Text CreateText(Transform parent, int size, Color color, TextAnchor alignment)
    {
        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.font = runtimeFont;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateAsciiButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject("Button");
        buttonObject.transform.SetParent(parent, false);
        buttonObject.AddComponent<LayoutElement>().preferredHeight = 63f;
        Image buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = new Color(0.02f, 0.05f, 0.05f, 0.96f);
        Outline buttonOutline = buttonObject.AddComponent<Outline>();
        buttonOutline.effectColor = borderColor;
        buttonOutline.effectDistance = new Vector2(1f, -1f);

        Button button = buttonObject.AddComponent<Button>();
        button.onClick.AddListener(onClick);

        Text buttonText = CreateText(buttonObject.transform, 23, textColor, TextAnchor.MiddleCenter);
        buttonText.text = label;
        RectTransform buttonTextRect = buttonText.rectTransform;
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero;
        buttonTextRect.offsetMax = Vector2.zero;

        return button;
    }

    private Font ResolveRuntimeFont()
    {
        return AsciiFontResolver.ResolveFont(18);
    }
}
