using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CredentialAuthOverlayPresenter : MonoBehaviour
{
    private const string OverlayCanvasObjectName = "CredentialAuthCanvas";
    private const string DefaultGatewayBaseUrl = RuntimeEndpointResolver.DefaultGatewayBaseUrl;

    [SerializeField] private WalletSessionController walletSession;
    [SerializeField] private Vector2 panelSize = new Vector2(920f, 610f);
    [SerializeField] private int canvasSortingOrder = 240;
    [SerializeField] private float loginFormHeight = 200f;
    [SerializeField] private float signupFormHeight = 296f;
    [SerializeField] private float fieldRowHeight = 84f;
    [SerializeField] private float inputHeight = 56f;
    [SerializeField] private float tabRowHeight = 52f;
    [SerializeField] private float statusCardHeight = 50f;
    [SerializeField] private float actionRowHeight = 56f;
    [SerializeField] private Color overlayColor = new Color(0.01f, 0.04f, 0.05f, 0.82f);
    [SerializeField] private Color panelColor = new Color(0.04f, 0.08f, 0.09f, 0.97f);
    [SerializeField] private Color borderColor = new Color(0.26f, 0.91f, 0.70f, 1f);
    [SerializeField] private Color textColor = new Color(0.92f, 0.98f, 0.94f, 1f);
    [SerializeField] private Color mutedTextColor = new Color(0.56f, 0.75f, 0.68f, 1f);
    [SerializeField] private Color inputBackgroundColor = new Color(0.01f, 0.05f, 0.06f, 0.98f);
    [SerializeField] private Color buttonColor = new Color(0.03f, 0.10f, 0.10f, 0.98f);
    [SerializeField] private Color buttonActiveColor = new Color(0.10f, 0.24f, 0.20f, 0.98f);
    [SerializeField] private Color statusPanelColor = new Color(0.02f, 0.07f, 0.08f, 0.96f);

    private Canvas overlayCanvas;
    private GameObject overlayRoot;
    private RectTransform panelRoot;
    private RectTransform formCardRoot;
    private RectTransform confirmFieldRoot;
    private LayoutElement formCardLayoutElement;
    private LayoutElement confirmFieldLayoutElement;
    private Button loginTabButton;
    private Button signupTabButton;
    private Button submitButton;
    private Button offlineButton;
    private InputField usernameField;
    private InputField passwordField;
    private InputField confirmPasswordField;
    private Text loginTabLabel;
    private Text signupTabLabel;
    private Text submitLabel;
    private Text statusLabel;
    private Text gatewayLabel;
    private Font runtimeFont;
    private bool showingSignup;
    private bool walletEventsBound;

    private void Awake()
    {
        ResolveReferences();
        CreateOverlayIfNeeded();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CreateOverlayIfNeeded();
        BindWalletEvents();
        RefreshPresentation();
    }

    private void Start()
    {
        RefreshPresentation();
    }

    private void OnDisable()
    {
        UnbindWalletEvents();
    }

    private void ResolveReferences()
    {
        if (walletSession == null)
        {
            walletSession = FindAnyObjectByType<WalletSessionController>();
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

    private void CreateOverlayIfNeeded()
    {
        if (overlayRoot != null)
        {
            return;
        }

        EnsureEventSystem();
        runtimeFont = AsciiFontResolver.ResolveFont(18);

        GameObject canvasObject = new GameObject(OverlayCanvasObjectName);
        canvasObject.transform.SetParent(transform, false);
        overlayCanvas = canvasObject.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.pixelPerfect = true;
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = canvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        overlayRoot = new GameObject("OverlayRoot", typeof(RectTransform), typeof(Image));
        overlayRoot.transform.SetParent(canvasObject.transform, false);
        RectTransform rootRect = overlayRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        Image overlayImage = overlayRoot.GetComponent<Image>();
        overlayImage.color = overlayColor;
        overlayImage.raycastTarget = true;

        panelRoot = CreateContainer("Panel", overlayRoot.transform);
        panelRoot.anchorMin = new Vector2(0.5f, 0.5f);
        panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
        panelRoot.pivot = new Vector2(0.5f, 0.5f);
        panelRoot.sizeDelta = panelSize;
        panelRoot.anchoredPosition = Vector2.zero;

        Image panelImage = panelRoot.gameObject.AddComponent<Image>();
        panelImage.color = panelColor;
        Outline panelOutline = panelRoot.gameObject.AddComponent<Outline>();
        panelOutline.effectColor = borderColor;
        panelOutline.effectDistance = new Vector2(1f, -1f);
        panelOutline.useGraphicAlpha = true;

        VerticalLayoutGroup layout = panelRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 18, 18);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        Text title = CreateText(panelRoot, "Title", 30, FontStyle.Bold, TextAnchor.UpperLeft, textColor);
        title.text = "FINITE EARTH :: RELAY ACCESS";
        SetPreferredHeight(title, 40f);

        Text subtitle = CreateText(panelRoot, "Subtitle", 17, FontStyle.Normal, TextAnchor.UpperLeft, mutedTextColor);
        subtitle.text = "Sign in to an existing account, or create a new one to start playing.";
        SetPreferredHeight(subtitle, 28f);

        RectTransform tabRow = CreateContainer("TabRow", panelRoot);
        SetPreferredHeight(tabRow, tabRowHeight);
        HorizontalLayoutGroup tabLayout = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 12f;
        tabLayout.childAlignment = TextAnchor.MiddleCenter;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = true;
        tabLayout.childForceExpandHeight = true;

        loginTabButton = CreateButton(tabRow, "[ SIGN IN ]", HandleLoginTabPressed, out loginTabLabel);
        signupTabButton = CreateButton(tabRow, "[ CREATE ACCOUNT ]", HandleSignupTabPressed, out signupTabLabel);

        formCardRoot = CreateContainer("FormCard", panelRoot);
        Image formImage = formCardRoot.gameObject.AddComponent<Image>();
        formImage.color = new Color(0.02f, 0.07f, 0.08f, 0.96f);
        Outline formOutline = formCardRoot.gameObject.AddComponent<Outline>();
        formOutline.effectColor = new Color(borderColor.r, borderColor.g, borderColor.b, 0.65f);
        formOutline.effectDistance = new Vector2(1f, -1f);
        formOutline.useGraphicAlpha = true;
        formCardLayoutElement = SetPreferredHeight(formCardRoot, loginFormHeight);

        VerticalLayoutGroup formLayout = formCardRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        formLayout.padding = new RectOffset(18, 18, 18, 18);
        formLayout.spacing = 12f;
        formLayout.childAlignment = TextAnchor.UpperLeft;
        formLayout.childControlWidth = true;
        formLayout.childControlHeight = true;
        formLayout.childForceExpandWidth = true;
        formLayout.childForceExpandHeight = false;

        usernameField = CreateInputField(formCardRoot, "Username", "username");
        passwordField = CreateInputField(formCardRoot, "Password", "password", true);

        confirmFieldRoot = CreateContainer("ConfirmPasswordRow", formCardRoot);
        confirmFieldLayoutElement = SetPreferredHeight(confirmFieldRoot, fieldRowHeight);
        VerticalLayoutGroup confirmLayout = confirmFieldRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        confirmLayout.spacing = 6f;
        confirmLayout.childControlWidth = true;
        confirmLayout.childControlHeight = true;
        confirmLayout.childForceExpandWidth = true;
        confirmLayout.childForceExpandHeight = false;
        Text confirmLabel = CreateText(confirmFieldRoot, "ConfirmPasswordLabel", 15, FontStyle.Bold, TextAnchor.UpperLeft, mutedTextColor);
        confirmLabel.text = "CONFIRM PASSWORD";
        SetPreferredHeight(confirmLabel, 18f);
        confirmPasswordField = CreateStandaloneInput(confirmFieldRoot, "confirm password", true);

        RectTransform statusCard = CreateContainer("StatusCard", panelRoot);
        SetPreferredHeight(statusCard, statusCardHeight);
        Image statusImage = statusCard.gameObject.AddComponent<Image>();
        statusImage.color = statusPanelColor;
        Outline statusOutline = statusCard.gameObject.AddComponent<Outline>();
        statusOutline.effectColor = new Color(borderColor.r, borderColor.g, borderColor.b, 0.45f);
        statusOutline.effectDistance = new Vector2(1f, -1f);
        statusOutline.useGraphicAlpha = true;

        statusLabel = CreateText(statusCard, "Status", 17, FontStyle.Normal, TextAnchor.MiddleLeft, textColor);
        RectTransform statusRect = statusLabel.rectTransform;
        statusRect.anchorMin = Vector2.zero;
        statusRect.anchorMax = Vector2.one;
        statusRect.offsetMin = new Vector2(14f, 8f);
        statusRect.offsetMax = new Vector2(-14f, -8f);
        statusLabel.text = DefaultStatusText();

        RectTransform actionRow = CreateContainer("ActionRow", panelRoot);
        SetPreferredHeight(actionRow, actionRowHeight);
        HorizontalLayoutGroup actionLayout = actionRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        actionLayout.spacing = 12f;
        actionLayout.childAlignment = TextAnchor.MiddleCenter;
        actionLayout.childControlWidth = true;
        actionLayout.childControlHeight = true;
        actionLayout.childForceExpandWidth = true;
        actionLayout.childForceExpandHeight = true;

        submitButton = CreateButton(actionRow, "[ SIGN IN ]", HandleSubmitPressed, out submitLabel);
        offlineButton = CreateButton(actionRow, "[ OFFLINE MODE ]", HandleOfflinePressed, out _);

        // Second row: Connect Wallet (Thirdweb) — gives players a real on-chain identity
        RectTransform walletRow = CreateContainer("WalletRow", panelRoot);
        SetPreferredHeight(walletRow, actionRowHeight * 0.85f);
        HorizontalLayoutGroup walletLayout = walletRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        walletLayout.spacing = 12f;
        walletLayout.padding = new RectOffset(24, 24, 0, 0);
        walletLayout.childAlignment = TextAnchor.MiddleCenter;
        walletLayout.childControlWidth = true;
        walletLayout.childControlHeight = true;
        walletLayout.childForceExpandWidth = true;
        walletLayout.childForceExpandHeight = true;

        CreateButton(walletRow, "[ CONNECT WALLET (MEGAETH) ]", HandleConnectWalletPressed, out _);

        ForceLayoutRebuild();
    }

    private void HandleLoginTabPressed()
    {
        showingSignup = false;
        SetStatus(DefaultStatusText());
        RefreshPresentation();
    }

    private void HandleSignupTabPressed()
    {
        showingSignup = true;
        SetStatus(DefaultStatusText());
        RefreshPresentation();
    }

    private void HandleSubmitPressed()
    {
        ResolveReferences();
        if (walletSession == null)
        {
            SetStatus("[!] Wallet session controller is missing.");
            return;
        }

        string username = usernameField != null ? usernameField.text.Trim() : string.Empty;
        string password = passwordField != null ? passwordField.text : string.Empty;
        string confirmPassword = confirmPasswordField != null ? confirmPasswordField.text : string.Empty;

        SetInteractable(false);
        if (showingSignup)
        {
            SetStatus("[*] Creating account...");
            walletSession.BeginCredentialSignup(username, password, confirmPassword);
            return;
        }

        SetStatus("[*] Signing in...");
        walletSession.BeginCredentialLogin(username, password);
    }

    private void HandleOfflinePressed()
    {
        ResolveReferences();
        if (walletSession == null)
        {
            SetStatus("[!] Wallet session controller is missing.");
            return;
        }

        SetInteractable(false);
        SetStatus("[*] Starting offline session...");
        walletSession.BeginOfflineMode();
    }

    private void HandleConnectWalletPressed()
    {
        ResolveReferences();
        if (walletSession == null)
        {
            SetStatus("[!] Wallet session controller is missing.");
            return;
        }

        SetInteractable(false);
        SetStatus("[*] Connecting wallet...");
        // Triggers Thirdweb wallet connect — on success HandleWalletConnected fires
        // in WalletSessionController which calls AuthenticationSucceeded with the
        // real 0x ETH address as the player identity (no gateway round-trip needed).
        walletSession.BeginAuthentication();
    }

    private void HandleAuthenticationSucceeded(string _)
    {
        SetInteractable(true);
        string identity = walletSession != null && !string.IsNullOrWhiteSpace(walletSession.DisplayName)
            ? walletSession.DisplayName
            : (walletSession != null ? walletSession.WalletAddress : string.Empty);

        if (string.IsNullOrWhiteSpace(identity))
        {
            identity = "session";
        }

        SetStatus(walletSession != null && walletSession.IsOfflineMode
            ? "[+] Offline mode active."
            : $"[+] Session ready for {identity}.");
        RefreshPresentation();
    }

    private void HandleAuthenticationFailed(string reason)
    {
        SetInteractable(true);
        SetStatus(NormalizeFailureMessage(reason));
    }

    private void HandleAuthenticationBypassed(string reason)
    {
        SetStatus($"[~] {reason}");
    }

    private void RefreshPresentation()
    {
        bool shouldShow = walletSession != null && walletSession.UsesCredentialAuthentication && !walletSession.IsAuthenticated;
        if (overlayRoot != null)
        {
            overlayRoot.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        if (confirmFieldRoot != null)
        {
            confirmFieldRoot.gameObject.SetActive(showingSignup);
        }

        if (formCardLayoutElement != null)
        {
            formCardLayoutElement.preferredHeight = showingSignup ? signupFormHeight : loginFormHeight;
            formCardLayoutElement.minHeight = formCardLayoutElement.preferredHeight;
        }

        if (confirmFieldLayoutElement != null)
        {
            confirmFieldLayoutElement.preferredHeight = fieldRowHeight;
            confirmFieldLayoutElement.minHeight = fieldRowHeight;
        }

        if (submitLabel != null)
        {
            submitLabel.text = showingSignup ? "[ CREATE ACCOUNT ]" : "[ SIGN IN ]";
        }

        ApplyButtonVisual(loginTabButton, loginTabLabel, !showingSignup);
        ApplyButtonVisual(signupTabButton, signupTabLabel, showingSignup);
        SetInteractable(true);
        ForceLayoutRebuild();
    }

    private void SetInteractable(bool interactable)
    {
        if (loginTabButton != null)
        {
            loginTabButton.interactable = interactable;
        }

        if (signupTabButton != null)
        {
            signupTabButton.interactable = interactable;
        }

        if (submitButton != null)
        {
            submitButton.interactable = interactable;
        }

        if (offlineButton != null)
        {
            offlineButton.interactable = interactable;
        }

        if (usernameField != null)
        {
            usernameField.interactable = interactable;
        }

        if (passwordField != null)
        {
            passwordField.interactable = interactable;
        }

        if (confirmPasswordField != null)
        {
            confirmPasswordField.interactable = interactable;
        }
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null)
        {
            statusLabel.text = message;
        }
    }

    private string DefaultStatusText()
    {
        return showingSignup
            ? "[idle] Create a new credential account."
            : "[idle] Sign in with your existing username and password.";
    }

    private string NormalizeFailureMessage(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return $"[!] Relay unreachable. Start backend/gateway on {GetGatewayBaseUrl()}.";
        }

        string normalized = reason.Trim();
        if (normalized.IndexOf("destination host", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("remote server", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("connection refused", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("resolve destination host", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return $"[!] Relay unreachable at {GetGatewayBaseUrl()}. Start backend/gateway and try again.";
        }

        return $"[!] {normalized}";
    }

    private string BuildGatewayCaption()
    {
        return $"RELAY: {GetGatewayBaseUrl()}";
    }

    private string GetGatewayBaseUrl()
    {
        ResolveReferences();
        if (walletSession == null)
        {
            return RuntimeEndpointResolver.ResolveGatewayBaseUrl(DefaultGatewayBaseUrl);
        }

        return string.IsNullOrWhiteSpace(walletSession.GatewayBaseUrl)
            ? RuntimeEndpointResolver.ResolveGatewayBaseUrl(DefaultGatewayBaseUrl)
            : walletSession.GatewayBaseUrl;
    }

    private void ApplyButtonVisual(Button button, Text label, bool active)
    {
        if (button != null && button.targetGraphic is Image image)
        {
            image.color = active ? buttonActiveColor : buttonColor;
        }

        if (label != null)
        {
            label.color = active ? textColor : mutedTextColor;
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

    private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, out Text labelText)
    {
        RectTransform root = CreateContainer("Button", parent);
        SetPreferredHeight(root, tabRowHeight);

        Image image = root.gameObject.AddComponent<Image>();
        image.color = buttonColor;
        Outline outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        labelText = CreateText(root, "Label", 18, FontStyle.Bold, TextAnchor.MiddleCenter, textColor);
        labelText.text = label;
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        return button;
    }

    private InputField CreateInputField(Transform parent, string label, string placeholder, bool password = false)
    {
        RectTransform root = CreateContainer(label + "Row", parent);
        SetPreferredHeight(root, fieldRowHeight);

        VerticalLayoutGroup layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        Text labelText = CreateText(root, label + "Label", 15, FontStyle.Bold, TextAnchor.UpperLeft, mutedTextColor);
        labelText.text = label.ToUpperInvariant();
        SetPreferredHeight(labelText, 18f);
        return CreateStandaloneInput(root, placeholder, password);
    }

    private InputField CreateStandaloneInput(Transform parent, string placeholder, bool password)
    {
        RectTransform background = CreateContainer("Input", parent);
        SetPreferredHeight(background, inputHeight);
        Image image = background.gameObject.AddComponent<Image>();
        image.color = inputBackgroundColor;
        Outline outline = background.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(borderColor.r, borderColor.g, borderColor.b, 0.72f);
        outline.effectDistance = new Vector2(1f, -1f);

        InputField field = background.gameObject.AddComponent<InputField>();
        field.lineType = InputField.LineType.SingleLine;
        field.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
        field.targetGraphic = image;

        Text text = CreateText(background, "Text", 18, FontStyle.Normal, TextAnchor.MiddleLeft, textColor);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 10f);
        textRect.offsetMax = new Vector2(-14f, -10f);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        Text placeholderText = CreateText(background, "Placeholder", 18, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(mutedTextColor.r, mutedTextColor.g, mutedTextColor.b, 0.72f));
        RectTransform placeholderRect = placeholderText.rectTransform;
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(14f, 10f);
        placeholderRect.offsetMax = new Vector2(-14f, -10f);
        placeholderText.text = placeholder;

        field.textComponent = text;
        field.placeholder = placeholderText;
        return field;
    }

    private Text CreateText(Transform parent, string objectName, int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.font = runtimeFont;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = color;
        text.supportRichText = false;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static RectTransform CreateContainer(string objectName, Transform parent)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject.GetComponent<RectTransform>();
    }

    private static LayoutElement SetPreferredHeight(Component component, float preferredHeight)
    {
        LayoutElement layoutElement = component.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = component.gameObject.AddComponent<LayoutElement>();
        }

        layoutElement.minHeight = preferredHeight;
        layoutElement.preferredHeight = preferredHeight;
        layoutElement.flexibleHeight = 0f;
        return layoutElement;
    }

    private void ForceLayoutRebuild()
    {
        Canvas.ForceUpdateCanvases();

        if (confirmFieldRoot != null && confirmFieldRoot.gameObject.activeInHierarchy)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(confirmFieldRoot);
        }

        if (formCardRoot != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(formCardRoot);
        }

        if (panelRoot != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRoot);
        }

        Canvas.ForceUpdateCanvases();
    }
}
