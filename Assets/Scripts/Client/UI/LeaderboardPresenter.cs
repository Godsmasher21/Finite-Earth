using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public readonly struct LeaderboardLineData
{
    public readonly string Left;
    public readonly string Right;
    public readonly bool Emphasized;

    public LeaderboardLineData(string left, string right, bool emphasized)
    {
        Left = left ?? string.Empty;
        Right = right ?? string.Empty;
        Emphasized = emphasized;
    }
}

public class LeaderboardPresenter : MonoBehaviour
{
    private sealed class RowBinding
    {
        public RectTransform root;
        public TMP_Text left;
        public TMP_Text right;
        public Image accent;
    }

    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField] private TMP_Text emptyStateText;

    private readonly List<RowBinding> rows = new List<RowBinding>(7);

    private TMP_FontAsset fontAsset;
    private Color textColor;
    private Color mutedTextColor;
    private Color accentColor;
    private Color subtleAccentColor;

    public void Initialize(
        RectTransform root,
        TMP_FontAsset sharedFont,
        Color primaryTextColor,
        Color secondaryTextColor,
        Color primaryAccentColor)
    {
        panelRoot = root;
        fontAsset = sharedFont;
        textColor = primaryTextColor;
        mutedTextColor = secondaryTextColor;
        accentColor = primaryAccentColor;
        subtleAccentColor = new Color(primaryAccentColor.r, primaryAccentColor.g, primaryAccentColor.b, 0.28f);

        BuildView();
    }

    public void Refresh(string title, string subtitle, IReadOnlyList<LeaderboardLineData> lines, string emptyState)
    {
        if (panelRoot == null)
        {
            return;
        }

        if (titleText != null)
        {
            titleText.text = string.IsNullOrWhiteSpace(title) ? ":: GLOBAL BOARD" : title;
        }

        if (subtitleText != null)
        {
            subtitleText.text = string.IsNullOrWhiteSpace(subtitle) ? "SYNCING GLOBAL RANKS" : subtitle;
        }

        bool hasLines = lines != null && lines.Count > 0;
        if (emptyStateText != null)
        {
            emptyStateText.gameObject.SetActive(!hasLines);
            emptyStateText.text = string.IsNullOrWhiteSpace(emptyState) ? "NO STANDINGS AVAILABLE" : emptyState;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            RowBinding row = rows[i];
            bool show = hasLines && i < lines.Count;
            row.root.gameObject.SetActive(show);
            if (!show)
            {
                continue;
            }

            LeaderboardLineData line = lines[i];
            row.left.text = line.Left;
            row.right.text = line.Right;

            Color rowTextColor = line.Emphasized ? textColor : mutedTextColor;
            row.left.color = rowTextColor;
            row.right.color = line.Emphasized ? accentColor : textColor;
            row.accent.color = line.Emphasized ? accentColor : subtleAccentColor;
        }
    }

    private void BuildView()
    {
        if (panelRoot == null)
        {
            return;
        }

        titleText = EnsureText("Title", titleText, panelRoot, ":: GLOBAL BOARD", 18, TextAlignmentOptions.Left, textColor);
        SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(-20f, 20f), new Vector2(10f, -8f));

        subtitleText = EnsureText("Subtitle", subtitleText, panelRoot, "SYNCING GLOBAL RANKS", 12, TextAlignmentOptions.Left, mutedTextColor);
        SetRect(subtitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(-20f, 18f), new Vector2(10f, -28f));

        RectTransform listRoot = panelRoot.Find("LeaderboardList") as RectTransform;
        if (listRoot == null)
        {
            listRoot = new GameObject("LeaderboardList", typeof(RectTransform)).GetComponent<RectTransform>();
            listRoot.SetParent(panelRoot, false);
        }

        SetRect(listRoot, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-20f, -56f), new Vector2(0f, -8f));

        VerticalLayoutGroup layout = listRoot.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = listRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 3f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = listRoot.GetComponent<ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = listRoot.gameObject.AddComponent<ContentSizeFitter>();
        }

        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        rows.Clear();
        for (int i = 0; i < 7; i++)
        {
            rows.Add(CreateRow(listRoot, i));
        }

        emptyStateText = EnsureText("EmptyState", emptyStateText, listRoot, "NO STANDINGS AVAILABLE", 13, TextAlignmentOptions.Center, mutedTextColor);
        SetRect(emptyStateText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-12f, -12f), Vector2.zero);
    }

    private RowBinding CreateRow(RectTransform parent, int index)
    {
        RectTransform rowRoot = new GameObject($"Row_{index}", typeof(RectTransform)).GetComponent<RectTransform>();
        rowRoot.SetParent(parent, false);
        rowRoot.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

        Image accent = new GameObject("Accent", typeof(RectTransform)).AddComponent<Image>();
        RectTransform accentRect = accent.GetComponent<RectTransform>();
        accentRect.SetParent(rowRoot, false);
        SetRect(accentRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(3f, 14f), new Vector2(0f, 0f));
        accent.raycastTarget = false;

        TMP_Text left = CreateRowText("Left", rowRoot, TextAlignmentOptions.Left, mutedTextColor);
        SetRect(left.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(-76f, 0f), new Vector2(10f, 0f));

        TMP_Text right = CreateRowText("Right", rowRoot, TextAlignmentOptions.Right, textColor);
        SetRect(right.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(68f, 0f), new Vector2(-2f, 0f));

        return new RowBinding
        {
            root = rowRoot,
            left = left,
            right = right,
            accent = accent
        };
    }

    private TMP_Text CreateRowText(string name, RectTransform parent, TextAlignmentOptions alignment, Color color)
    {
        TMP_Text text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        text.rectTransform.SetParent(parent, false);
        text.font = fontAsset;
        text.fontSize = 13f;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private TMP_Text EnsureText(string name, TMP_Text existing, RectTransform parent, string content, float size, TextAlignmentOptions alignment, Color color)
    {
        TMP_Text text = existing;
        if (text == null)
        {
            text = parent.Find(name)?.GetComponent<TMP_Text>();
        }

        if (text == null)
        {
            text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.rectTransform.SetParent(parent, false);
        }

        text.font = fontAsset;
        text.fontSize = size;
        text.text = content;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 size,
        Vector2 anchoredPosition)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
    }
}
