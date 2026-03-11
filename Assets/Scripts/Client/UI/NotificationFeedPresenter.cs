using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NotificationFeedPresenter : MonoBehaviour
{
    [SerializeField] private RectTransform listRoot;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Color textColor = new Color(0.90f, 0.94f, 0.94f, 1f);
    [SerializeField] private Color panelColor = new Color(0.07f, 0.10f, 0.12f, 0.92f);
    [SerializeField, Min(1)] private int maxEntries = 6;
    [SerializeField, Min(0.1f)] private float displaySeconds = 4.5f;
    [SerializeField, Min(0.05f)] private float fadeSeconds = 0.6f;

    private readonly List<NotificationEntry> entries = new List<NotificationEntry>();
    public bool HasEntries => entries.Count > 0;
    private TMP_Text emptyStateLabel;

    private sealed class NotificationEntry
    {
        public RectTransform root;
        public TMP_Text label;
        public CanvasGroup group;
        public float createdAt;
        public float duration;
    }

    public void Initialize(RectTransform root, TMP_FontAsset fontAsset, Color baseTextColor, Color basePanelColor)
    {
        listRoot = root;
        font = fontAsset;
        textColor = baseTextColor;
        panelColor = basePanelColor;
        EnsureEmptyState();
    }

    public void Push(string message, Color accent)
    {
        if (listRoot == null)
        {
            return;
        }

        if (entries.Count >= maxEntries)
        {
            DestroyEntry(entries[entries.Count - 1]);
            entries.RemoveAt(entries.Count - 1);
        }

        if (emptyStateLabel != null)
        {
            emptyStateLabel.gameObject.SetActive(false);
        }

        RectTransform entryRoot = new GameObject("FeedItem", typeof(RectTransform)).GetComponent<RectTransform>();
        entryRoot.SetParent(listRoot, false);
        entryRoot.anchorMin = new Vector2(0f, 1f);
        entryRoot.anchorMax = new Vector2(1f, 1f);
        entryRoot.pivot = new Vector2(0.5f, 1f);
        entryRoot.sizeDelta = new Vector2(0f, 38f);

        Image bg = entryRoot.gameObject.AddComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = false;

        Outline outline = entryRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = accent;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        CanvasGroup group = entryRoot.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        TMP_Text label = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.SetParent(entryRoot, false);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(10f, 4f);
        labelRect.offsetMax = new Vector2(-10f, -4f);
        label.font = font;
        label.fontSize = 18;
        label.color = textColor;
        label.text = $">> {(message ?? string.Empty).ToUpperInvariant()}";
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;

        Shadow shadow = label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(1f, -1f);
        shadow.useGraphicAlpha = true;

        entryRoot.SetAsFirstSibling();
        var entry = new NotificationEntry
        {
            root = entryRoot,
            label = label,
            group = group,
            createdAt = Time.unscaledTime,
            duration = displaySeconds
        };
        entries.Insert(0, entry);
    }

    private void Update()
    {
        EnsureEmptyState();

        float now = Time.unscaledTime;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            NotificationEntry entry = entries[i];
            float elapsed = now - entry.createdAt;
            float fadeStart = Mathf.Max(0f, entry.duration - fadeSeconds);
            float alpha = elapsed < fadeStart ? 1f : Mathf.Clamp01(1f - ((elapsed - fadeStart) / fadeSeconds));
            entry.group.alpha = Mathf.MoveTowards(entry.group.alpha, alpha, 6f * Time.unscaledDeltaTime);
            if (elapsed >= entry.duration + fadeSeconds)
            {
                DestroyEntry(entry);
                entries.RemoveAt(i);
            }
        }

        if (emptyStateLabel != null)
        {
            emptyStateLabel.gameObject.SetActive(entries.Count == 0);
        }
    }

    private static void DestroyEntry(NotificationEntry entry)
    {
        if (entry == null || entry.root == null)
        {
            return;
        }

        Destroy(entry.root.gameObject);
    }

    private void EnsureEmptyState()
    {
        if (listRoot == null || font == null || emptyStateLabel != null)
        {
            return;
        }

        emptyStateLabel = new GameObject("EmptyState", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        RectTransform rect = emptyStateLabel.GetComponent<RectTransform>();
        rect.SetParent(listRoot, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, 24f);
        rect.anchoredPosition = Vector2.zero;

        emptyStateLabel.font = font;
        emptyStateLabel.fontSize = 16;
        emptyStateLabel.color = new Color(textColor.r, textColor.g, textColor.b, 0.68f);
        emptyStateLabel.text = ">> STANDBY";
        emptyStateLabel.alignment = TextAlignmentOptions.MidlineLeft;
        emptyStateLabel.textWrappingMode = TextWrappingModes.NoWrap;
        emptyStateLabel.overflowMode = TextOverflowModes.Ellipsis;
        emptyStateLabel.raycastTarget = false;

        Shadow shadow = emptyStateLabel.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(1f, -1f);
        shadow.useGraphicAlpha = true;
    }
}
