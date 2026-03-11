using UnityEngine;
using UnityEngine.EventSystems;

public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    [TextArea(1, 4)]
    public string tooltipText;
    public TooltipPresenter tooltip;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tooltip == null || string.IsNullOrWhiteSpace(tooltipText))
        {
            return;
        }

        tooltip.Show(tooltipText, eventData.position);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (tooltip == null || string.IsNullOrWhiteSpace(tooltipText))
        {
            return;
        }

        tooltip.Show(tooltipText, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltip == null)
        {
            return;
        }

        tooltip.Hide();
    }
}
