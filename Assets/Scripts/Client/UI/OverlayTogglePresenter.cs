using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OverlayTogglePresenter : MonoBehaviour
{
    [SerializeField] private Button influenceButton;
    [SerializeField] private Button resourceButton;
    [SerializeField] private Button ecosystemButton;
    [SerializeField] private TMP_Text influenceLabel;
    [SerializeField] private TMP_Text resourceLabel;
    [SerializeField] private TMP_Text ecosystemLabel;
    [SerializeField] private Color activeColor = new Color(0.20f, 0.65f, 0.55f, 1f);
    [SerializeField] private Color inactiveColor = new Color(0.60f, 0.66f, 0.68f, 1f);

    private HexOverlayPainter overlayPainter;
    private HexOverlayPainter.OverlayMode currentMode = HexOverlayPainter.OverlayMode.None;

    public void Initialize(Button influence, Button resource, Button ecosystem, TMP_Text influenceText, TMP_Text resourceText, TMP_Text ecosystemText, HexOverlayPainter painter)
    {
        influenceButton = influence;
        resourceButton = resource;
        ecosystemButton = ecosystem;
        influenceLabel = influenceText;
        resourceLabel = resourceText;
        ecosystemLabel = ecosystemText;
        overlayPainter = painter;

        if (influenceButton != null) influenceButton.onClick.AddListener(() => ToggleMode(HexOverlayPainter.OverlayMode.Influence));
        if (resourceButton != null) resourceButton.onClick.AddListener(() => ToggleMode(HexOverlayPainter.OverlayMode.Resource));
        if (ecosystemButton != null) ecosystemButton.onClick.AddListener(() => ToggleMode(HexOverlayPainter.OverlayMode.Ecosystem));

        RefreshVisuals();
    }

    private void ToggleMode(HexOverlayPainter.OverlayMode mode)
    {
        currentMode = currentMode == mode ? HexOverlayPainter.OverlayMode.None : mode;
        if (overlayPainter != null)
        {
            overlayPainter.SetMode(currentMode);
        }
        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        if (influenceLabel != null) influenceLabel.color = currentMode == HexOverlayPainter.OverlayMode.Influence ? activeColor : inactiveColor;
        if (resourceLabel != null) resourceLabel.color = currentMode == HexOverlayPainter.OverlayMode.Resource ? activeColor : inactiveColor;
        if (ecosystemLabel != null) ecosystemLabel.color = currentMode == HexOverlayPainter.OverlayMode.Ecosystem ? activeColor : inactiveColor;
    }
}
