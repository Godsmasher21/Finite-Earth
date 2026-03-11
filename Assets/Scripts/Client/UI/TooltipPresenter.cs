using TMPro;
using UnityEngine;

public class TooltipPresenter : MonoBehaviour
{
    [SerializeField] private RectTransform root;
    [SerializeField] private TMP_Text label;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Vector2 offset = new Vector2(0f, 56f);
    [SerializeField] private Vector2 padding = new Vector2(10f, 6f);
    [SerializeField] private Vector2 minSize = new Vector2(140f, 42f);
    [SerializeField, Min(80f)] private float maxWidth = 220f;
    [SerializeField, Min(0.01f)] private float fadeSpeed = 12f;

    private RectTransform canvasRect;
    private bool visible;

    public void Initialize(RectTransform rootRect, TMP_Text labelText, CanvasGroup group, RectTransform canvasRoot)
    {
        root = rootRect;
        label = labelText;
        canvasGroup = group;
        canvasRect = canvasRoot;
        SetVisible(false, true);
    }

    public void Show(string text, Vector2 screenPosition)
    {
        if (root == null || label == null || canvasRect == null)
        {
            return;
        }

        label.text = text ?? string.Empty;
        RefreshSize();
        visible = true;
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
        if (root == null || label == null)
        {
            return;
        }

        Vector2 preferred = label.GetPreferredValues(label.text ?? string.Empty);
        float width = Mathf.Clamp(preferred.x + (padding.x * 2f), minSize.x, maxWidth);
        float height = Mathf.Max(minSize.y, preferred.y + (padding.y * 2f));
        root.sizeDelta = new Vector2(width, height);
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
