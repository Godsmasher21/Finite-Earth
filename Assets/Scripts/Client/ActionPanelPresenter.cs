using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class ActionPanelPresenter : MonoBehaviour
{
    private enum RuntimePanelStyle
    {
        Compact,
        AsciiTerminal
    }

    [Serializable]
    private sealed class ActionButtonBinding
    {
        public FiniteEarthActionType actionType;
        public Button button;
        public Text label;
        public Image background;
        public Outline outline;
        public string defaultLabel;
    }

    [Header("Runtime Panel")]
    [SerializeField] private RuntimePanelStyle runtimePanelStyle = RuntimePanelStyle.AsciiTerminal;
    [SerializeField] private Color disabledColor = new Color(0.26f, 0.29f, 0.32f, 0.95f);
    [SerializeField] private bool autoCreateRuntimePanel = true;
    [SerializeField] private Vector2 runtimePanelSize = new Vector2(364f, 542f);
    [SerializeField] private Vector2 runtimePanelAnchor = new Vector2(1f, 1f);
    [SerializeField] private Vector2 runtimePanelPivot = new Vector2(1f, 1f);
    [SerializeField] private Vector2 runtimePanelOffset = new Vector2(-16f, -16f);

    [Header("ASCII Theme")]
    [SerializeField] private Color asciiPanelColor = new Color(0.01f, 0.03f, 0.03f, 0.92f);
    [SerializeField] private Color asciiBorderColor = new Color(0.22f, 0.95f, 0.61f, 1f);
    [SerializeField] private Color asciiTextColor = new Color(0.90f, 0.98f, 0.94f, 1f);
    [SerializeField] private Color asciiMutedTextColor = new Color(0.62f, 0.72f, 0.68f, 1f);
    [SerializeField] private Color asciiPatternColor = new Color(0.26f, 0.36f, 0.34f, 0.07f);
    [SerializeField] private Color asciiButtonBackground = new Color(0.03f, 0.07f, 0.07f, 0.90f);
    [SerializeField] private Color asciiUnaffordableBackground = new Color(0.22f, 0.05f, 0.05f, 0.92f);
    [SerializeField] private Color asciiUnaffordableTextColor = new Color(1f, 0.78f, 0.78f, 1f);
    [SerializeField] private Color asciiUnaffordableBorderColor = new Color(1f, 0.34f, 0.34f, 1f);
    [SerializeField] private bool showAsciiPatternBackground = false;

    [SerializeField] private List<ActionButtonBinding> bindings = new List<ActionButtonBinding>();

    public event Action<FiniteEarthActionType> ActionRequested;

    private readonly Dictionary<FiniteEarthActionType, ActionButtonBinding> lookup = new Dictionary<FiniteEarthActionType, ActionButtonBinding>();
    private Font runtimeFont;
    private Text asciiHeaderText;
    private Text asciiFooterText;
    private const int HeaderInnerWidth = 32;

    private static readonly FiniteEarthActionType[] OrderedActions =
    {
        FiniteEarthActionType.Claim,
        FiniteEarthActionType.BuildSettlement,
        FiniteEarthActionType.BuildIndustry,
        FiniteEarthActionType.HarvestForest,
        FiniteEarthActionType.Reforest,
        FiniteEarthActionType.Farm,
        FiniteEarthActionType.Irrigate,
        FiniteEarthActionType.Mine,
        FiniteEarthActionType.Restore
    };

    private void Awake()
    {
        if (bindings.Count == 0 && autoCreateRuntimePanel)
        {
            CreateRuntimePanel();
        }

        lookup.Clear();

        for (int i = 0; i < bindings.Count; i++)
        {
            ActionButtonBinding binding = bindings[i];
            if (binding == null || binding.button == null)
            {
                continue;
            }

            lookup[binding.actionType] = binding;
            FiniteEarthActionType capturedAction = binding.actionType;
            binding.button.onClick.AddListener(() => ActionRequested?.Invoke(capturedAction));

            if (binding.label != null && ActionCatalog.TryGet(binding.actionType, out ActionRuleSpec spec))
            {
                int hotkeyIndex = GetHotkeyIndex(binding.actionType);
                if (string.IsNullOrWhiteSpace(binding.defaultLabel))
                {
                    binding.defaultLabel = BuildActionLabel(binding.actionType, spec.label, hotkeyIndex);
                }

                binding.label.text = binding.defaultLabel;
            }
        }
    }

    private void CreateRuntimePanel()
    {
        EnsureEventSystem();

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("RuntimeActionCanvas");
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

        GameObject panelObject = new GameObject("RuntimeActionPanel");
        panelObject.transform.SetParent(canvas.transform, false);
        RectTransform panelRect = panelObject.AddComponent<RectTransform>();
        panelRect.anchorMin = runtimePanelAnchor;
        panelRect.anchorMax = runtimePanelAnchor;
        panelRect.pivot = runtimePanelPivot;
        panelRect.anchoredPosition = runtimePanelOffset;
        panelRect.sizeDelta = runtimePanelSize;

        runtimeFont = ResolveRuntimeFont();

        if (runtimePanelStyle == RuntimePanelStyle.AsciiTerminal)
        {
            CreateAsciiPanel(panelObject.transform);
        }
        else
        {
            CreateCompactPanel(panelObject.transform);
        }
    }

    private void CreateCompactPanel(Transform parent)
    {
        Image panelImage = parent.gameObject.AddComponent<Image>();
        panelImage.color = new Color(0.07f, 0.10f, 0.12f, 0.62f);

        VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 6f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        bindings.Clear();

        for (int i = 0; i < OrderedActions.Length; i++)
        {
            FiniteEarthActionType actionType = OrderedActions[i];
            if (!ActionCatalog.TryGet(actionType, out ActionRuleSpec spec))
            {
                continue;
            }

            var binding = new ActionButtonBinding
            {
                actionType = actionType
            };

            GameObject buttonObject = new GameObject($"{spec.label}Button");
            buttonObject.transform.SetParent(parent, false);
            buttonObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            Image buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.color = spec.accent;
            buttonImage.raycastTarget = true;
            Button button = buttonObject.AddComponent<Button>();

            GameObject textObject = new GameObject("Label");
            textObject.transform.SetParent(buttonObject.transform, false);
            Text text = textObject.AddComponent<Text>();
            text.text = BuildActionLabel(actionType, spec.label);
            text.alignment = TextAnchor.MiddleCenter;
            text.font = runtimeFont;
            text.color = Color.white;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = 16;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            binding.button = button;
            binding.label = text;
            binding.background = buttonImage;
            binding.defaultLabel = text.text;
            bindings.Add(binding);
        }
    }

    private void CreateAsciiPanel(Transform parent)
    {
        Image panelImage = parent.gameObject.AddComponent<Image>();
        panelImage.color = asciiPanelColor;
        panelImage.raycastTarget = true;

        Outline outerOutline = parent.gameObject.AddComponent<Outline>();
        outerOutline.effectColor = asciiBorderColor;
        outerOutline.effectDistance = new Vector2(1f, -1f);
        outerOutline.useGraphicAlpha = true;

        Shadow innerShadow = parent.gameObject.AddComponent<Shadow>();
        innerShadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        innerShadow.effectDistance = new Vector2(0f, -1f);
        innerShadow.useGraphicAlpha = true;

        if (showAsciiPatternBackground)
        {
            CreateAsciiPatternBackground(parent);
        }

        VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 6f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        GameObject headerObject = new GameObject("AsciiHeader");
        headerObject.transform.SetParent(parent, false);
        LayoutElement headerLayout = headerObject.AddComponent<LayoutElement>();
        headerLayout.preferredHeight = 84f;

        Text header = headerObject.AddComponent<Text>();
        asciiHeaderText = header;
        header.font = runtimeFont;
        header.supportRichText = false;
        header.color = asciiTextColor;
        header.alignment = TextAnchor.UpperLeft;
        header.horizontalOverflow = HorizontalWrapMode.Wrap;
        header.verticalOverflow = VerticalWrapMode.Overflow;
        header.fontSize = 15;
        header.lineSpacing = 0.90f;
        header.text = BuildAsciiHeaderText(false, default, false, 0);
        header.raycastTarget = false;

        bindings.Clear();

        for (int i = 0; i < OrderedActions.Length; i++)
        {
            FiniteEarthActionType actionType = OrderedActions[i];
            if (!ActionCatalog.TryGet(actionType, out ActionRuleSpec spec))
            {
                continue;
            }

            ActionButtonBinding binding = CreateAsciiActionRow(parent, actionType, spec, i + 1);
            bindings.Add(binding);
        }

        GameObject footerObject = new GameObject("AsciiFooter");
        footerObject.transform.SetParent(parent, false);
        LayoutElement footerLayout = footerObject.AddComponent<LayoutElement>();
        footerLayout.preferredHeight = 38f;

        Text footer = footerObject.AddComponent<Text>();
        asciiFooterText = footer;
        footer.font = runtimeFont;
        footer.supportRichText = false;
        footer.color = asciiMutedTextColor;
        footer.alignment = TextAnchor.LowerLeft;
        footer.fontSize = 13;
        footer.text = "[1..9] actions  [AUTO CYCLE TIMER]";
        footer.raycastTarget = false;
    }

    private ActionButtonBinding CreateAsciiActionRow(Transform parent, FiniteEarthActionType actionType, ActionRuleSpec spec, int hotkeyIndex)
    {
        GameObject buttonObject = new GameObject($"{spec.label}AsciiRow");
        buttonObject.transform.SetParent(parent, false);

        LayoutElement rowLayout = buttonObject.AddComponent<LayoutElement>();
        rowLayout.preferredHeight = 38f;

        Image rowImage = buttonObject.AddComponent<Image>();
        rowImage.color = asciiButtonBackground;
        rowImage.raycastTarget = true;

        Outline rowOutline = buttonObject.AddComponent<Outline>();
        rowOutline.effectColor = asciiBorderColor;
        rowOutline.effectDistance = new Vector2(1f, -1f);
        rowOutline.useGraphicAlpha = true;

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.94f);
        colors.pressedColor = new Color(0.86f, 0.98f, 0.92f, 1f);
        colors.disabledColor = Color.white;
        button.colors = colors;

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);

        Text label = labelObject.AddComponent<Text>();
        label.font = runtimeFont;
        label.supportRichText = false;
        label.alignment = TextAnchor.MiddleLeft;
        label.color = asciiTextColor;
        label.fontSize = 14;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.text = BuildActionLabel(actionType, spec.label, hotkeyIndex);

        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 0f);
        labelRect.offsetMax = new Vector2(-8f, 0f);

        return new ActionButtonBinding
        {
            actionType = actionType,
            button = button,
            label = label,
            background = rowImage,
            outline = rowOutline,
            defaultLabel = label.text
        };
    }

    private void CreateAsciiPatternBackground(Transform parent)
    {
        GameObject patternObject = new GameObject("AsciiPattern");
        patternObject.transform.SetParent(parent, false);
        patternObject.transform.SetAsFirstSibling();
        LayoutElement layout = patternObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        Text pattern = patternObject.AddComponent<Text>();
        pattern.font = runtimeFont;
        pattern.supportRichText = false;
        pattern.color = asciiPatternColor;
        pattern.alignment = TextAnchor.UpperLeft;
        pattern.fontSize = 14;
        pattern.raycastTarget = false;
        pattern.horizontalOverflow = HorizontalWrapMode.Wrap;
        pattern.verticalOverflow = VerticalWrapMode.Overflow;
        pattern.text = BuildPattern(32, 14, "$");

        RectTransform patternRect = pattern.GetComponent<RectTransform>();
        patternRect.anchorMin = Vector2.zero;
        patternRect.anchorMax = Vector2.one;
        patternRect.offsetMin = new Vector2(6f, 6f);
        patternRect.offsetMax = new Vector2(-6f, -6f);
    }

    private static string BuildPattern(int columns, int rows, string token)
    {
        if (columns <= 0 || rows <= 0)
        {
            return string.Empty;
        }

        string row = string.Empty;
        for (int i = 0; i < columns; i++)
        {
            row += token;
        }

        string result = row;
        for (int y = 1; y < rows; y++)
        {
            result += "\n" + row;
        }

        return result;
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

    private Font ResolveRuntimeFont()
    {
        if (runtimeFont != null)
        {
            return runtimeFont;
        }

        runtimeFont = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "JetBrains Mono", "Courier New", "Lucida Console" }, 18);
        if (runtimeFont != null)
        {
            return runtimeFont;
        }

        runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (runtimeFont != null)
        {
            return runtimeFont;
        }

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private static string BuildActionLabel(FiniteEarthActionType actionType, string actionLabel, int hotkeyIndex = -1)
    {
        string keyText = actionType == FiniteEarthActionType.EndTurn
            ? "[SP]"
            : (hotkeyIndex > 0 && hotkeyIndex < 10 ? $"[{hotkeyIndex}]" : "[--]");

        string upper = string.IsNullOrWhiteSpace(actionLabel) ? actionType.ToString().ToUpperInvariant() : actionLabel.ToUpperInvariant();
        return $"| {keyText} {upper.PadRight(10)} 🪵0🍞0⛏️0 💰-- 🎯-- |";
    }

    private static int GetHotkeyIndex(FiniteEarthActionType actionType)
    {
        for (int i = 0; i < OrderedActions.Length; i++)
        {
            if (OrderedActions[i] == actionType)
            {
                return i + 1;
            }
        }

        return -1;
    }

    public void SetInteractable(FiniteEarthActionType actionType, bool interactable)
    {
        if (!lookup.TryGetValue(actionType, out ActionButtonBinding binding) || binding.button == null)
        {
            return;
        }

        binding.button.interactable = interactable;

        if (binding.background != null)
        {
            if (interactable && ActionCatalog.TryGet(actionType, out ActionRuleSpec spec))
            {
                binding.background.color = runtimePanelStyle == RuntimePanelStyle.AsciiTerminal
                    ? asciiButtonBackground
                    : spec.accent;
            }
            else
            {
                binding.background.color = disabledColor;
            }
        }

        if (binding.label != null)
        {
            string baseLabel = string.IsNullOrWhiteSpace(binding.defaultLabel)
                ? (binding.label.text ?? actionType.ToString().ToUpperInvariant())
                : binding.defaultLabel;

            binding.label.color = interactable ? asciiTextColor : asciiMutedTextColor;
            binding.label.text = interactable
                ? baseLabel
                : baseLabel.Replace(" > |", " X |");
        }

        if (binding.outline != null)
        {
            binding.outline.effectColor = interactable ? asciiBorderColor : new Color(0.34f, 0.39f, 0.39f, 1f);
        }
    }

    public void SetActionState(
        FiniteEarthActionType actionType,
        bool hasSelection,
        bool claimable,
        int applicableSelectionCount,
        int affordableSelectionCount,
        FiniteEarthResourcePool cost,
        FiniteEarthResourcePool availableResources,
        string reason)
    {
        if (!lookup.TryGetValue(actionType, out ActionButtonBinding binding) || binding.button == null)
        {
            return;
        }

        bool shouldShow = hasSelection && applicableSelectionCount > 0;
        if (binding.button.gameObject.activeSelf != shouldShow)
        {
            binding.button.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        bool affordable = availableResources.CanAfford(cost);
        bool interactable = hasSelection && claimable && affordable;
        binding.button.interactable = interactable;

        string label = BuildActionStatusLabel(
            actionType,
            hasSelection,
            claimable,
            applicableSelectionCount,
            affordableSelectionCount,
            cost,
            availableResources);

        binding.defaultLabel = label;
        if (binding.label != null)
        {
            binding.label.text = label;
            if (!hasSelection)
            {
                binding.label.color = asciiMutedTextColor;
            }
            else if (!affordable)
            {
                binding.label.color = asciiUnaffordableTextColor;
            }
            else
            {
                binding.label.color = claimable ? asciiTextColor : asciiMutedTextColor;
            }
        }

        if (binding.background != null)
        {
            if (runtimePanelStyle == RuntimePanelStyle.AsciiTerminal)
            {
                binding.background.color = hasSelection
                    ? (affordable ? (claimable ? asciiButtonBackground : disabledColor) : asciiUnaffordableBackground)
                    : new Color(0.03f, 0.05f, 0.05f, 0.80f);
            }
            else if (ActionCatalog.TryGet(actionType, out ActionRuleSpec spec))
            {
                binding.background.color = claimable ? spec.accent : disabledColor;
            }
            else
            {
                binding.background.color = disabledColor;
            }
        }

        if (binding.outline != null)
        {
            binding.outline.effectColor = hasSelection
                ? (affordable ? (claimable ? asciiBorderColor : new Color(0.34f, 0.39f, 0.39f, 1f)) : asciiUnaffordableBorderColor)
                : new Color(0.18f, 0.22f, 0.21f, 1f);
        }

        if (runtimePanelStyle == RuntimePanelStyle.AsciiTerminal && asciiFooterText != null && !claimable && hasSelection && !string.IsNullOrWhiteSpace(reason))
        {
            asciiFooterText.text = Truncate($"⚠ {reason}", 62);
        }
    }

    public void SetSelectionContext(bool hasSelection, HexCoord coord, bool claimable, string claimReason, int selectionCount = 1)
    {
        if (runtimePanelStyle != RuntimePanelStyle.AsciiTerminal)
        {
            return;
        }

        if (asciiHeaderText != null)
        {
            asciiHeaderText.text = BuildAsciiHeaderText(hasSelection, coord, claimable, selectionCount);
        }

        if (asciiFooterText != null)
        {
            if (!hasSelection)
            {
                asciiFooterText.text = "🖱 Click + drag to select tiles";
            }
            else if (selectionCount > 1)
            {
                asciiFooterText.text = Truncate($"🧩 {selectionCount} tiles selected. Action applies to all valid tiles.", 62);
            }
            else if (!claimable && !string.IsNullOrWhiteSpace(claimReason))
            {
                asciiFooterText.text = Truncate($"🚫 Claim blocked: {claimReason}", 62);
            }
            else
            {
                asciiFooterText.text = "⌨ 1..9 actions   ⏱ auto cycle timer";
            }
        }
    }

    private string BuildActionStatusLabel(
        FiniteEarthActionType actionType,
        bool hasSelection,
        bool claimable,
        int applicableSelectionCount,
        int affordableSelectionCount,
        FiniteEarthResourcePool cost,
        FiniteEarthResourcePool available)
    {
        int hotkey = GetHotkeyIndex(actionType);
        string keyText = hotkey > 0 ? $"[{hotkey}]" : "[--]";

        string labelText = actionType.ToString().ToUpperInvariant();
        if (ActionCatalog.TryGet(actionType, out ActionRuleSpec spec))
        {
            labelText = string.IsNullOrWhiteSpace(spec.label) ? labelText : spec.label.ToUpperInvariant();
        }

        string tileStatus = hasSelection ? (claimable ? "🎯✅" : "🎯❌") : "🎯--";
        bool affordable = available.CanAfford(cost);
        string affordStatus = affordable ? "💰✅" : "💰❌";
        string countText = affordableSelectionCount > 0 && affordableSelectionCount < applicableSelectionCount
            ? $"x{affordableSelectionCount}/{applicableSelectionCount}"
            : (applicableSelectionCount > 1 ? $"x{applicableSelectionCount}" : "x1");
        string costText = $"🪵{cost.wood}🍞{cost.food}⛏️{cost.minerals}";

        return $"| {keyText} {labelText.PadRight(10)} {countText} {costText} {affordStatus} {tileStatus} |";
    }

    private static string BuildAsciiHeaderText(bool hasSelection, HexCoord coord, bool claimable, int selectionCount)
    {
        string line1 = BuildAsciiLine("FINITE EARTH :: CMD TERMINAL");
        string line2;
        if (!hasSelection)
        {
            line2 = BuildAsciiLine("🖱 SELECT TILE TO PREVIEW");
        }
        else if (selectionCount > 1)
        {
            line2 = BuildAsciiLine($"🧩 {selectionCount} TILES | CLAIM {(claimable ? "✅" : "❌")}");
        }
        else
        {
            line2 = BuildAsciiLine($"📍 Q{coord.q} R{coord.r} | CLAIM {(claimable ? "✅" : "❌")}");
        }

        return
            "+--------------------------------+\n" +
            line1 + "\n" +
            line2 + "\n" +
            "+--------------------------------+";
    }

    private static string BuildAsciiLine(string text)
    {
        string normalized = text ?? string.Empty;
        if (normalized.Length > HeaderInnerWidth)
        {
            normalized = normalized.Substring(0, HeaderInnerWidth);
        }

        return "|" + normalized.PadRight(HeaderInnerWidth) + "|";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, Mathf.Max(0, maxLength - 3)) + "...";
    }

    public void SetAllInteractable(bool interactable)
    {
        for (int i = 0; i < bindings.Count; i++)
        {
            ActionButtonBinding binding = bindings[i];
            if (binding == null)
            {
                continue;
            }

            SetInteractable(binding.actionType, interactable);
        }
    }
}
