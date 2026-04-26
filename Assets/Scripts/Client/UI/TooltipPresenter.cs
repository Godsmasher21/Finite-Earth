using TMPro;
using UnityEngine;

public class TooltipPresenter : MonoBehaviour
{
    [SerializeField] private RectTransform root;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text bodyLabel;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Vector2 offset = new Vector2(0f, 56f);
    [SerializeField] private Vector2 padding = new Vector2(14f, 10f);
    [SerializeField] private Vector2 minSize = new Vector2(196f, 60f);
    [SerializeField, Min(120f)] private float maxWidth = 300f;
    [SerializeField, Min(0.01f)] private float fadeSpeed = 12f;

    private RectTransform canvasRect;
    private bool visible;

    public void Initialize(
        RectTransform rootRect,
        TMP_Text titleText,
        TMP_Text bodyText,
        CanvasGroup group,
        RectTransform canvasRoot)
    {
        root = rootRect;
        titleLabel = titleText;
        bodyLabel = bodyText;
        canvasGroup = group;
        canvasRect = canvasRoot;
        SetVisible(false, true);
    }

    public void Show(string text, Vector2 screenPosition)
    {
        if (root == null || titleLabel == null || bodyLabel == null || canvasRect == null)
        {
            return;
        }

        ParseTooltipText(text, out string title, out string body);

        titleLabel.text = title;
        titleLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(title));
        bodyLabel.text = body;
        bodyLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(body));

        RefreshSize();
        visible = true;
        root.SetAsLastSibling();
        PositionAt(screenPosition);
    }

    public void Hide()
    {
        visible = false;
    }

    private void Update()
    {
        if (canvasGroup == null)
        {
            return;
        }

        float target = visible ? 1f : 0f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    private void PositionAt(Vector2 screenPosition)
    {
        if (root == null || canvasRect == null)
        {
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out Vector2 localPoint);
        Vector2 anchored = localPoint + offset;
        root.anchoredPosition = ClampToCanvas(anchored);
    }

    private void RefreshSize()
    {
        if (root == null || titleLabel == null || bodyLabel == null)
        {
            return;
        }

        float contentMaxWidth = Mathf.Max(80f, maxWidth - (padding.x * 2f));
        Vector2 titlePreferred = titleLabel.gameObject.activeSelf
            ? titleLabel.GetPreferredValues(titleLabel.text ?? string.Empty, contentMaxWidth, 0f)
            : Vector2.zero;
        Vector2 bodyPreferred = bodyLabel.gameObject.activeSelf
            ? bodyLabel.GetPreferredValues(bodyLabel.text ?? string.Empty, contentMaxWidth, 0f)
            : Vector2.zero;

        float contentWidth = Mathf.Max(titlePreferred.x, bodyPreferred.x);
        float width = Mathf.Clamp(contentWidth + (padding.x * 2f), minSize.x, maxWidth);
        float finalContentWidth = Mathf.Max(80f, width - (padding.x * 2f));
        float titleHeight = titleLabel.gameObject.activeSelf
            ? titleLabel.GetPreferredValues(titleLabel.text ?? string.Empty, finalContentWidth, 0f).y
            : 0f;
        float bodyHeight = bodyLabel.gameObject.activeSelf
            ? bodyLabel.GetPreferredValues(bodyLabel.text ?? string.Empty, finalContentWidth, 0f).y
            : 0f;
        float spacing = titleLabel.gameObject.activeSelf && bodyLabel.gameObject.activeSelf ? 6f : 0f;
        float height = Mathf.Max(minSize.y, titleHeight + bodyHeight + spacing + (padding.y * 2f));
        root.sizeDelta = new Vector2(width, height);
    }

    private static void ParseTooltipText(string text, out string title, out string body)
    {
        string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            title = string.Empty;
            body = string.Empty;
            return;
        }

        string[] lines = normalized.Split('\n');
        if (lines.Length == 1)
        {
            title = lines[0].Trim();
            body = string.Empty;
            return;
        }

        title = lines[0].Trim();
        body = string.Join("\n", lines, 1, lines.Length - 1).Trim();
    }

    private Vector2 ClampToCanvas(Vector2 anchored)
    {
        if (root == null || canvasRect == null)
        {
            return anchored;
        }

        Vector2 size = root.sizeDelta + padding;
        Vector2 min = new Vector2(-canvasRect.rect.width * 0.5f + size.x * 0.5f, -canvasRect.rect.height * 0.5f + size.y * 0.5f);
        Vector2 max = new Vector2(canvasRect.rect.width * 0.5f - size.x * 0.5f, canvasRect.rect.height * 0.5f - size.y * 0.5f);
        return new Vector2(Mathf.Clamp(anchored.x, min.x, max.x), Mathf.Clamp(anchored.y, min.y, max.y));
    }

    private void SetVisible(bool show, bool instant)
    {
        visible = show;
        if (canvasGroup == null)
        {
            return;
        }

        canvasGroup.alpha = instant ? (show ? 1f : 0f) : canvasGroup.alpha;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }
}
