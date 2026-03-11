using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ActionConsolePresenter : MonoBehaviour
{
    [Serializable]
    private sealed class ActionButtonView
    {
        public FiniteEarthActionType actionType;
        public Button button;
        public Image background;
        public Image accentBar;
        public Image icon;
        public TMP_Text label;
        public TMP_Text cost;
        public TooltipTrigger tooltipTrigger;
    }

    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField] private RectTransform listRoot;
    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private Color textColor = new Color(0.92f, 0.98f, 0.94f, 1f);
    [SerializeField] private Color mutedText = new Color(0.56f, 0.76f, 0.67f, 1f);
    [SerializeField] private Color disabledBg = new Color(0.02f, 0.08f, 0.07f, 0.94f);

    private readonly List<ActionButtonView> buttons = new List<ActionButtonView>();
    private Action<FiniteEarthActionType> actionHandler;
    private TooltipPresenter tooltip;

    public void Initialize(TMP_Text title, TMP_Text subtitle, RectTransform actionsRoot, TooltipPresenter tooltipPresenter)
    {
        titleText = title;
        subtitleText = subtitle;
        listRoot = actionsRoot;
        panelRoot = actionsRoot != null ? actionsRoot.parent as RectTransform : null;
        tooltip = tooltipPresenter;
    }

    public void BuildButtons(TMP_FontAsset font, Sprite iconSprite, int buttonCount, Action<FiniteEarthActionType> handler)
    {
        if (listRoot == null)
        {
            return;
        }

        actionHandler = handler;
        buttons.Clear();
        bool showIcons = iconSprite != null;
        for (int i = 0; i < buttonCount; i++)
        {
            RectTransform row = new GameObject($"ActionRow_{i}", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(listRoot, false);
            row.sizeDelta = new Vector2(0f, 42f);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);

            Image bg = row.gameObject.AddComponent<Image>();
            bg.color = disabledBg;

            LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.minHeight = 42f;
            rowLayout.preferredHeight = 42f;
            rowLayout.flexibleHeight = 0f;
            rowLayout.flexibleWidth = 1f;

            Outline outline = row.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.18f, 0.76f, 0.60f, 0.72f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            Shadow shadow = row.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.60f);
            shadow.effectDistance = new Vector2(1f, -1f);
            shadow.useGraphicAlpha = true;

            Button button = row.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.1f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.2f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            button.colors = colors;

            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 8, 6, 6);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;

            RectTransform accentRect = new GameObject("Accent", typeof(RectTransform)).GetComponent<RectTransform>();
            accentRect.SetParent(row, false);
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.sizeDelta = new Vector2(3f, 0f);
            Image accent = accentRect.gameObject.AddComponent<Image>();
            accent.color = mutedText;
            LayoutElement accentLayout = accentRect.gameObject.AddComponent<LayoutElement>();
            accentLayout.preferredWidth = 3f;
            accentLayout.minWidth = 3f;
            accentLayout.preferredHeight = 28f;

            Image icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
            RectTransform iconRect = icon.GetComponent<RectTransform>();
            iconRect.SetParent(row, false);
            iconRect.sizeDelta = showIcons ? new Vector2(12f, 12f) : Vector2.zero;
            icon.sprite = iconSprite;
            icon.enabled = showIcons;
            icon.color = mutedText;
            LayoutElement iconLayout = icon.gameObject.AddComponent<LayoutElement>();
            iconLayout.preferredWidth = showIcons ? 12f : 0f;
            iconLayout.minWidth = showIcons ? 12f : 0f;
            iconLayout.preferredHeight = showIcons ? 12f : 0f;

            TMP_Text label = CreateText(row, "Label", font, 18, TextAlignmentOptions.Left);
            label.text = "--";
            LayoutElement labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.minWidth = 0f;
            labelLayout.flexibleWidth = 1f;

            TMP_Text cost = CreateText(row, "Cost", font, 14, TextAlignmentOptions.Right);
            cost.text = string.Empty;
            LayoutElement costLayout = cost.gameObject.AddComponent<LayoutElement>();
            costLayout.minWidth = 72f;
            costLayout.preferredWidth = 84f;

            TooltipTrigger trigger = row.gameObject.AddComponent<TooltipTrigger>();
            trigger.tooltip = tooltip;

            var view = new ActionButtonView
            {
                actionType = FiniteEarthActionType.Claim,
                button = button,
                background = bg,
                accentBar = accent,
                icon = icon,
                label = label,
                cost = cost,
                tooltipTrigger = trigger
            };
            buttons.Add(view);
        }
    }

    public void Refresh(string selectionTitle, bool hasSelection, IReadOnlyList<ActionAvailability> actionStates, FiniteEarthActionType[] ordering)
    {
        if (titleText != null)
        {
            titleText.text = ":: COMMAND CONSOLE";
        }

        if (subtitleText != null)
        {
            subtitleText.text = hasSelection ? $"[ {selectionTitle.ToUpperInvariant()} ]" : "[ SELECT A TILE ]";
        }

        if (buttons.Count == 0)
        {
            return;
        }

        Dictionary<FiniteEarthActionType, ActionAvailability> lookup = new Dictionary<FiniteEarthActionType, ActionAvailability>();
        if (actionStates != null)
        {
            for (int i = 0; i < actionStates.Count; i++)
            {
                ActionAvailability state = actionStates[i];
                lookup[state.actionType] = state;
            }
        }

        int buttonIndex = 0;
        for (int i = 0; i < ordering.Length && buttonIndex < buttons.Count; i++)
        {
            FiniteEarthActionType actionType = ordering[i];
            if (!lookup.TryGetValue(actionType, out ActionAvailability state))
            {
                continue;
            }

            if (!state.hasSelection)
            {
                continue;
            }

            if (!state.isApplicable)
            {
                continue;
            }

            ActionButtonView view = buttons[buttonIndex];
            buttonIndex++;
            ApplyState(view, actionType, state);
        }

        if (subtitleText != null && hasSelection && buttonIndex == 0)
        {
            subtitleText.text = $"[ {selectionTitle.ToUpperInvariant()} | NO ORDERS ]";
        }

        AdjustPanelHeight(buttonIndex);

        for (int i = buttonIndex; i < buttons.Count; i++)
        {
            HideButton(buttons[i]);
        }
    }

    private void AdjustPanelHeight(int visibleRows)
    {
        if (panelRoot == null)
        {
            return;
        }

        int clampedRows = Mathf.Clamp(visibleRows, 0, 4);
        float rowHeight = clampedRows > 0 ? (clampedRows * 42f) + ((clampedRows - 1) * 6f) : 0f;
        float targetHeight = 92f + rowHeight;
        if (clampedRows == 0)
        {
            targetHeight = 120f;
        }

        Vector2 size = panelRoot.sizeDelta;
        size.y = Mathf.Clamp(targetHeight, 120f, 320f);
        panelRoot.sizeDelta = size;
    }

    private void ApplyState(ActionButtonView view, FiniteEarthActionType actionType, ActionAvailability state)
    {
        if (view.button == null)
        {
            return;
        }

        view.actionType = actionType;
        view.button.onClick.RemoveAllListeners();
        view.button.onClick.AddListener(() => actionHandler?.Invoke(actionType));
        view.button.interactable = state.isInteractable;

        if (ActionCatalog.TryGet(actionType, out ActionRuleSpec spec))
        {
            view.label.text = $"> {spec.label.ToUpperInvariant()}";
            view.icon.color = spec.accent;
            if (view.accentBar != null)
            {
                view.accentBar.color = state.isInteractable ? spec.accent : mutedText;
            }

            view.background.color = state.isInteractable
                ? new Color(
                    Mathf.Lerp(disabledBg.r, spec.accent.r, 0.14f),
                    Mathf.Lerp(disabledBg.g, spec.accent.g, 0.14f),
                    Mathf.Lerp(disabledBg.b, spec.accent.b, 0.14f),
                    0.96f)
                : disabledBg;
        }
        else
        {
            view.label.text = $"> {actionType.ToString().ToUpperInvariant()}";
            view.background.color = disabledBg;
            if (view.accentBar != null)
            {
                view.accentBar.color = mutedText;
            }
        }

        view.label.color = state.isInteractable ? textColor : mutedText;
        view.cost.text = BuildCostLine(state.cost);
        view.cost.color = state.isAffordable ? textColor : new Color(1f, 0.72f, 0.72f, 1f);

        if (view.tooltipTrigger != null)
        {
            string costLine = BuildCostLine(state.cost);
            view.tooltipTrigger.tooltipText = string.IsNullOrWhiteSpace(state.reason)
                ? costLine
                : $"{state.reason.ToUpperInvariant()}\n{costLine}";
        }

        view.button.gameObject.SetActive(true);
    }

    private static void HideButton(ActionButtonView view)
    {
        if (view.button == null)
        {
            return;
        }

        view.button.gameObject.SetActive(false);
    }

    private static string BuildCostLine(FiniteEarthResourcePool cost)
    {
        if (cost.IsZero())
        {
            return "[FREE]";
        }

        return $"[W{cost.wood} F{cost.food} M{cost.minerals}]";
    }

    private static TMP_Text CreateText(Transform parent, string name, TMP_FontAsset font, int size, TextAlignmentOptions alignment)
    {
        TMP_Text text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        RectTransform rect = text.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;

        Shadow shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(1f, -1f);
        shadow.useGraphicAlpha = true;
        return text;
    }
}
