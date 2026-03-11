using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class AsciiMarketDiplomacyPresenter : MonoBehaviour
{
    private const string MarketCanvasName = "AsciiMarketCanvas";
    private const int MarketCanvasSortingOrder = 110;
    private const float ToggleTabWidth = 18f;
    private const float ToggleTabHeight = 80f;

    [Serializable]
    public struct TradePreset
    {
        public string label;
        public FiniteEarthResourcePool give;
        public FiniteEarthResourcePool want;
    }

    [Header("References")]
    [SerializeField] private FiniteEarthGameOrchestrator orchestrator;

    [Header("Panel")]
    [SerializeField] private bool showOnStart = false;
    [SerializeField] private bool allowToggleHotkey = true;
    [SerializeField] private Vector2 panelSize = new Vector2(376f, 396f);
    [SerializeField] private Vector2 panelOffset = new Vector2(8f, -112f);
    [SerializeField] private float refreshIntervalSeconds = 0.5f;

    [Header("Theme")]
    [SerializeField] private Color panelColor = new Color(0.05f, 0.38f, 0.26f, 0.95f);
    [SerializeField] private Color borderColor = new Color(0.96f, 0.97f, 0.98f, 0.96f);
    [SerializeField] private Color textColor = new Color(0.95f, 0.98f, 0.96f, 1f);
    [SerializeField] private Color mutedTextColor = new Color(0.74f, 0.84f, 0.79f, 1f);
    [SerializeField] private Color buttonColor = new Color(0.07f, 0.33f, 0.24f, 0.95f);
    [SerializeField] private Color disabledButtonColor = new Color(0.07f, 0.24f, 0.20f, 0.85f);

    [Header("Market Presets")]
    [SerializeField] private List<TradePreset> tradePresets = new List<TradePreset>();
    [SerializeField, Min(1)] private int maxOffersVisible = 6;

    private GameObject panelRoot;
    private GameObject toggleTabRoot;
    private RectTransform panelRect;
    private RectTransform contentRoot;
    private Text headerText;
    private Text statusText;
    private Text toggleTabText;
    private Font runtimeFont;
    private bool isVisible;
    private float nextRefreshAt;
    private string lastStatus;
    private Canvas runtimeCanvas;

    private void Awake()
    {
        ResolveReferences();
        EnsureDefaultPresets();
        CreatePanelIfNeeded();
        SetVisible(showOnStart);
    }

    private void Update()
    {
        ResolveReferences();

        if (allowToggleHotkey && IsToggleHotkeyPressed())
        {
            Toggle();
        }

        if (!isVisible)
        {
            return;
        }

        if (Time.unscaledTime >= nextRefreshAt)
        {
            nextRefreshAt = Time.unscaledTime + Mathf.Max(0.1f, refreshIntervalSeconds);
            RefreshPanel();
        }
    }

    private void ResolveReferences()
    {
        if (orchestrator == null)
        {
            orchestrator = FindAnyObjectByType<FiniteEarthGameOrchestrator>();
        }
    }

    private void EnsureDefaultPresets()
    {
        if (tradePresets != null && tradePresets.Count > 0)
        {
            return;
        }

        tradePresets = new List<TradePreset>
        {
            new TradePreset
            {
                label = "Give W2 -> Want F2",
                give = new FiniteEarthResourcePool { wood = 2 },
                want = new FiniteEarthResourcePool { food = 2 }
            },
            new TradePreset
            {
                label = "Give F3 -> Want W2",
                give = new FiniteEarthResourcePool { food = 3 },
                want = new FiniteEarthResourcePool { wood = 2 }
            },
            new TradePreset
            {
                label = "Give W2 -> Want O1",
                give = new FiniteEarthResourcePool { wood = 2 },
                want = new FiniteEarthResourcePool { minerals = 1 }
            }
        };
    }

    private void CreatePanelIfNeeded()
    {
        if (panelRoot != null)
        {
            return;
        }

        EnsureEventSystem();

        Canvas canvas = EnsureCanvas();

        runtimeFont = AsciiFontResolver.ResolveFont(16);
        CreateToggleTab(canvas.transform);

        panelRoot = new GameObject("AsciiMarketDiplomacyPanel");
        panelRoot.transform.SetParent(canvas.transform, false);

        panelRect = panelRoot.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = panelOffset;
        panelRect.sizeDelta = panelSize;

        Image bg = panelRoot.AddComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = true;

        panelRoot.AddComponent<RectMask2D>();

        Outline outline = panelRoot.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        VerticalLayoutGroup layout = panelRoot.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        headerText = CreateText(panelRoot.transform, 20, textColor, TextAnchor.UpperLeft);
        headerText.text = BuildHeader();
        headerText.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

        GameObject viewportObject = new GameObject("Viewport");
        viewportObject.transform.SetParent(panelRoot.transform, false);
        RectTransform viewportRect = viewportObject.AddComponent<RectTransform>();
        LayoutElement viewportLayout = viewportObject.AddComponent<LayoutElement>();
        viewportLayout.preferredHeight = panelSize.y - 96f;
        viewportObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        viewportObject.AddComponent<RectMask2D>();

        GameObject contentObject = new GameObject("Content");
        contentObject.transform.SetParent(viewportObject.transform, false);
        contentRoot = contentObject.AddComponent<RectTransform>();
        contentRoot.anchorMin = new Vector2(0f, 1f);
        contentRoot.anchorMax = new Vector2(1f, 1f);
        contentRoot.pivot = new Vector2(0.5f, 1f);
        contentRoot.anchoredPosition = Vector2.zero;
        contentRoot.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup contentLayout = contentObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(2, 2, 0, 0);
        contentLayout.spacing = 6f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        ContentSizeFitter contentFitter = contentObject.AddComponent<ContentSizeFitter>();
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scrollRect = viewportObject.AddComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRoot;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 18f;

        statusText = CreateText(panelRoot.transform, 16, mutedTextColor, TextAnchor.UpperLeft);
        statusText.text = "Press M to collapse this panel.";
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
    }

    private void CreateToggleTab(Transform parent)
    {
        if (toggleTabRoot != null)
        {
            return;
        }

        toggleTabRoot = new GameObject("AsciiMarketToggleTab");
        toggleTabRoot.transform.SetParent(parent, false);

        RectTransform rect = toggleTabRoot.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(Mathf.Max(0f, panelOffset.x - ToggleTabWidth + 2f), panelOffset.y + 10f);
        rect.sizeDelta = new Vector2(ToggleTabWidth, ToggleTabHeight);

        Image bg = toggleTabRoot.AddComponent<Image>();
        bg.color = buttonColor;

        Outline outline = toggleTabRoot.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        Button button = toggleTabRoot.AddComponent<Button>();
        button.onClick.AddListener(Toggle);

        toggleTabText = CreateText(toggleTabRoot.transform, 20, textColor, TextAnchor.MiddleCenter);
        toggleTabText.text = ">";
        toggleTabText.rectTransform.anchorMin = Vector2.zero;
        toggleTabText.rectTransform.anchorMax = Vector2.one;
        toggleTabText.rectTransform.offsetMin = new Vector2(0f, 0f);
        toggleTabText.rectTransform.offsetMax = new Vector2(0f, 0f);
    }

    private Canvas EnsureCanvas()
    {
        if (runtimeCanvas != null)
        {
            return runtimeCanvas;
        }

        GameObject existing = GameObject.Find(MarketCanvasName);
        if (existing != null)
        {
            runtimeCanvas = existing.GetComponent<Canvas>();
            if (runtimeCanvas == null)
            {
                runtimeCanvas = existing.AddComponent<Canvas>();
            }
        }
        else
        {
            GameObject canvasObject = new GameObject(MarketCanvasName);
            runtimeCanvas = canvasObject.AddComponent<Canvas>();
        }

        runtimeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        runtimeCanvas.pixelPerfect = true;
        runtimeCanvas.overrideSorting = true;
        runtimeCanvas.sortingOrder = MarketCanvasSortingOrder;

        CanvasScaler scaler = runtimeCanvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = runtimeCanvas.gameObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (runtimeCanvas.GetComponent<GraphicRaycaster>() == null)
        {
            runtimeCanvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        return runtimeCanvas;
    }

    private void RefreshPanel()
    {
        if (contentRoot == null)
        {
            return;
        }

        headerText.text = BuildHeader();
        ClearChildren(contentRoot);

        BuildMarketSection(contentRoot);
        BuildDiplomacySection(contentRoot);
        BuildTechSection(contentRoot);

        if (statusText != null)
        {
            statusText.text = string.IsNullOrWhiteSpace(lastStatus) ? "Press M to collapse this panel." : lastStatus;
        }
    }

    private void BuildMarketSection(Transform parent)
    {
        CreateSectionHeader(parent, "MARKET BOARD");

        IReadOnlyList<TradeOffer> offers = orchestrator != null ? orchestrator.GetTradeOffers() : null;
        int shown = 0;

        if (offers != null)
        {
            for (int i = 0; i < offers.Count && shown < maxOffersVisible; i++)
            {
                TradeOffer offer = offers[i];
                if (offer == null || offer.status != TradeOfferStatus.Open)
                {
                    continue;
                }

                string label = BuildOfferLabel(offer);
                bool isOwner = IsLocalWallet(offer.ownerWallet);
                CreateActionButton(parent, label, () =>
                {
                    if (isOwner)
                    {
                        string reason = "Unable to cancel trade offer.";
                        if (orchestrator != null && orchestrator.TryCancelTradeOffer(offer.id, out reason))
                        {
                            lastStatus = $"Canceled offer {offer.id}.";
                        }
                        else
                        {
                            lastStatus = reason;
                        }
                    }
                    else
                    {
                        string reason = "Unable to accept trade offer.";
                        if (orchestrator != null && orchestrator.TryAcceptTradeOffer(offer.id, out reason))
                        {
                            lastStatus = $"Accepted offer {offer.id}.";
                        }
                        else
                        {
                            lastStatus = reason;
                        }
                    }
                });
                shown++;
            }
        }

        if (shown == 0)
        {
            CreateMutedRow(parent, "No open offers.");
        }

        CreateSectionHeader(parent, "POST OFFER");
        for (int i = 0; i < tradePresets.Count; i++)
        {
            TradePreset preset = tradePresets[i];
            string label = $"POST {FormatResources(preset.give)} -> {FormatResources(preset.want)}";
            CreateActionButton(parent, label, () =>
            {
                string reason = "Unable to create trade offer.";
                if (orchestrator != null && orchestrator.TryCreateTradeOffer(preset.give, preset.want, out reason))
                {
                    lastStatus = "Offer posted.";
                }
                else
                {
                    lastStatus = reason;
                }
            });
        }
    }

    private void BuildDiplomacySection(Transform parent)
    {
        CreateSectionHeader(parent, "DIPLOMACY");

        string targetWallet = string.Empty;
        if (orchestrator != null)
        {
            orchestrator.TryGetSelectedOwner(out targetWallet);
        }

        string targetLabel = string.IsNullOrWhiteSpace(targetWallet) ? "Target: none" : $"Target: {ShortWallet(targetWallet)}";
        CreateMutedRow(parent, targetLabel);

        bool canTarget = !string.IsNullOrWhiteSpace(targetWallet) && !IsLocalWallet(targetWallet);

        CreateActionButton(parent, "PROPOSE NON-AGGRESSION", () =>
        {
            string reason = "Unable to propose non-aggression pact.";
            if (orchestrator != null && orchestrator.TryCreatePact(DiplomacyPactType.NonAggression, targetWallet, out reason))
            {
                lastStatus = "Non-aggression pact proposed.";
            }
            else
            {
                lastStatus = reason;
            }
        }, canTarget);

        CreateActionButton(parent, "PROPOSE RESOURCE PACT", () =>
        {
            string reason = "Unable to propose resource pact.";
            if (orchestrator != null && orchestrator.TryCreatePact(DiplomacyPactType.ResourceShare, targetWallet, out reason))
            {
                lastStatus = "Resource pact proposed.";
            }
            else
            {
                lastStatus = reason;
            }
        }, canTarget);

        IReadOnlyList<DiplomacyPact> pacts = orchestrator != null ? orchestrator.GetDiplomacyPacts() : null;
        if (pacts == null || pacts.Count == 0)
        {
            CreateMutedRow(parent, "No active pacts.");
            return;
        }

        for (int i = 0; i < pacts.Count; i++)
        {
            DiplomacyPact pact = pacts[i];
            if (pact == null)
            {
                continue;
            }

            string label = BuildPactLabel(pact);
            bool isLocal = IsLocalWallet(pact.walletA) || IsLocalWallet(pact.walletB);
            if (pact.status == DiplomacyPactStatus.Pending && IsLocalWallet(pact.walletB))
            {
                CreateActionButton(parent, $"ACCEPT {label}", () =>
                {
                    string reason = "Unable to accept pact.";
                    if (orchestrator != null && orchestrator.TryAcceptPact(pact.id, out reason))
                    {
                        lastStatus = "Pact accepted.";
                    }
                    else
                    {
                        lastStatus = reason;
                    }
                }, true);
                continue;
            }

            if (pact.status == DiplomacyPactStatus.Active && isLocal)
            {
                CreateActionButton(parent, $"CANCEL {label}", () =>
                {
                    string reason = "Unable to cancel pact.";
                    if (orchestrator != null && orchestrator.TryCancelPact(pact.id, out reason))
                    {
                        lastStatus = "Pact canceled.";
                    }
                    else
                    {
                        lastStatus = reason;
                    }
                }, true);
                continue;
            }

            CreateMutedRow(parent, label);
        }
    }

    private void BuildTechSection(Transform parent)
    {
        CreateSectionHeader(parent, "TECH TREE");

        int rp = orchestrator != null ? orchestrator.GetResearchPoints() : 0;
        CreateMutedRow(parent, $"Research Points: {rp}");

        for (int i = 0; i < FiniteEarthTechTree.Nodes.Length; i++)
        {
            TechDefinition node = FiniteEarthTechTree.Nodes[i];
            bool unlocked = orchestrator != null && orchestrator.IsTechUnlocked(node.node);
            bool affordable = orchestrator != null && orchestrator.GetResearchPoints() >= node.cost;
            bool prereq = orchestrator != null && orchestrator.HasTechPrerequisite(node);

            string label = unlocked
                ? $"UNLOCKED {node.label}"
                : $"RESEARCH {node.label} (RP {node.cost})";

            bool canClick = !unlocked && affordable && prereq;
            CreateActionButton(parent, label, () =>
            {
                string reason = "Unable to research tech.";
                if (orchestrator != null && orchestrator.TryResearchTech(node.node, out reason))
                {
                    lastStatus = $"Researched {node.label}.";
                }
                else
                {
                    lastStatus = reason;
                }
            }, canClick);
        }
    }

    private void CreateSectionHeader(Transform parent, string title)
    {
        Text header = CreateText(parent, 17, textColor, TextAnchor.UpperLeft);
        header.text = title;
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
    }

    private void CreateMutedRow(Transform parent, string text)
    {
        Text row = CreateText(parent, 15, mutedTextColor, TextAnchor.UpperLeft);
        row.text = text;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
    }

    private Button CreateActionButton(Transform parent, string label, Action onClick, bool interactable = true)
    {
        GameObject buttonObject = new GameObject("AsciiActionButton");
        buttonObject.transform.SetParent(parent, false);

        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 32f;

        Image image = buttonObject.AddComponent<Image>();
        image.color = interactable ? buttonColor : disabledButtonColor;

        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        Button button = buttonObject.AddComponent<Button>();
        button.interactable = interactable;
        button.onClick.AddListener(() => onClick?.Invoke());

        Text text = CreateText(buttonObject.transform, 16, textColor, TextAnchor.MiddleLeft);
        text.text = label;
        text.rectTransform.offsetMin = new Vector2(10f, 0f);
        text.rectTransform.offsetMax = new Vector2(-10f, 0f);

        return button;
    }

    private Text CreateText(Transform parent, int size, Color color, TextAnchor alignment)
    {
        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.font = runtimeFont;
        text.fontSize = Mathf.Clamp(size, 12, 26);
        text.color = color;
        text.alignment = alignment;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Destroy(root.GetChild(i).gameObject);
        }
    }

    private string BuildHeader()
    {
        int rp = orchestrator != null ? orchestrator.GetResearchPoints() : 0;
        return
            "+-------------- MARKET + DIPLOMACY -------------+\n" +
            $"RP {rp} | PRESS [M] TO TOGGLE";
    }

    private static string FormatResources(FiniteEarthResourcePool pool)
    {
        return $"W{pool.wood} F{pool.food} O{pool.minerals}";
    }

    private string BuildOfferLabel(TradeOffer offer)
    {
        string owner = ShortWallet(offer.ownerWallet);
        string prefix = IsLocalWallet(offer.ownerWallet) ? "CANCEL" : "ACCEPT";
        return $"{prefix} {owner} {FormatResources(offer.give)} -> {FormatResources(offer.want)}";
    }

    private static string BuildPactLabel(DiplomacyPact pact)
    {
        string type = pact.type == DiplomacyPactType.NonAggression ? "NON-AGGRESSION" : "RESOURCE PACT";
        string status = pact.status.ToString().ToUpperInvariant();
        return $"{type} [{status}]";
    }

    private bool IsLocalWallet(string wallet)
    {
        return orchestrator != null && orchestrator.IsLocalWallet(wallet);
    }

    private static string ShortWallet(string wallet)
    {
        if (string.IsNullOrWhiteSpace(wallet))
        {
            return "unknown";
        }

        if (wallet.Length <= 10)
        {
            return wallet;
        }

        return $"{wallet.Substring(0, 6)}..{wallet.Substring(wallet.Length - 4)}";
    }

    private void Toggle()
    {
        SetVisible(!isVisible);
    }

    private void SetVisible(bool visible)
    {
        isVisible = visible;
        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }

        if (toggleTabText != null)
        {
            toggleTabText.text = visible ? "<" : ">";
        }

        if (visible)
        {
            RefreshPanel();
        }
    }

    private static bool IsToggleHotkeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
        {
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.M);
#else
        return false;
#endif
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
}
