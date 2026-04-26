using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ActionWheelPresenter : MonoBehaviour
{
    private sealed class WheelClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public System.Action callback;
        public bool acceptClicks;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!acceptClicks || eventData == null || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            callback?.Invoke();
        }
    }

    private sealed class WheelButton
    {
        public RectTransform root;
        public Image hitArea;
        public Button button;
        public Image frame;
        public Image fill;
        public Image labelPlate;
        public TMP_Text label;
        public TMP_Text cost;
        public TooltipTrigger tooltip;
        public WheelClickRelay relay;
        public FiniteEarthActionType actionType;
        public bool hasActionType;
    }

    [SerializeField] private RectTransform wheelRoot;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite hexSprite;
    [SerializeField] private float radius = 112f;
    [SerializeField] private Vector2 screenMargin = new Vector2(24f, 24f);

    private readonly List<WheelButton> buttons = new List<WheelButton>();
    private Vector2[] slotOffsets;
    private Action<FiniteEarthActionType> actionHandler;
    private TooltipPresenter tooltip;

    public bool IsInitialized => wheelRoot != null && canvasGroup != null && canvasRect != null;

    public void ResetRuntimeWheel()
    {
        buttons.Clear();

        if (wheelRoot != null)
        {
            if (Application.isPlaying)
            {
                Destroy(wheelRoot.gameObject);
            }
            else
            {
                DestroyImmediate(wheelRoot.gameObject);
            }
        }

        wheelRoot = null;
        canvasGroup = null;
    }

    public void Initialize(RectTransform canvasRoot, TMP_FontAsset fontAsset, Sprite hexButtonSprite, TooltipPresenter tooltipPresenter, Action<FiniteEarthActionType> handler)
    {
        canvasRect = canvasRoot;
        font = fontAsset;
        hexSprite = hexButtonSprite;
        tooltip = tooltipPresenter;
        actionHandler = handler;
        if (worldCamera == null)
        {
            worldCamera = Camera.main;
        }

        if (wheelRoot == null)
        {
            wheelRoot = new GameObject("HexActionWheel", typeof(RectTransform)).GetComponent<RectTransform>();
            wheelRoot.SetParent(canvasRoot, false);
            wheelRoot.sizeDelta = new Vector2(360f, 340f);
            canvasGroup = wheelRoot.gameObject.AddComponent<CanvasGroup>();
        }

        BuildButtons();
        SetVisible(false, true);
    }

    public void Refresh(FiniteEarthGameOrchestrator orchestrator, HexWorldGeneratorTilemap world, OwnershipOverlayPointTop ownership)
    {
        if (wheelRoot == null || orchestrator == null || world == null || ownership == null)
        {
            SetVisible(false, true);
            return;
        }

        if (orchestrator.TryGetSelectedArmy(out ArmyUnit selectedArmy, out _))
        {
            List<WheelAction> armyActions = BuildArmyActions(orchestrator);
            ApplyWheelActions(armyActions);
            PositionWheelAt(selectedArmy.coord, world);
            SetVisible(armyActions.Count > 0, false);
            return;
        }

        if (!orchestrator.HasSelection)
        {
            SetVisible(false, true);
            return;
        }

        HexCoord coord = orchestrator.SelectedCoord;
        world.TryGetTileType(coord.ToVector3Int(), out TileType terrain);
        world.TryGetBuildingType(coord.ToVector3Int(), out BuildingType building);
        bool isOwned = ownership.IsOwned(coord.ToVector3Int());
        bool hasLocalArmy = orchestrator.TryGetArmyAt(coord, out _, true);

        List<WheelAction> actions = BuildContextActions(coord, terrain, building, isOwned, hasLocalArmy, orchestrator);
        ApplyWheelActions(actions);
        PositionWheelAt(coord, world);
        SetVisible(actions.Count > 0, false);
    }

    private void BuildButtons()
    {
        buttons.Clear();
        if (wheelRoot != null)
        {
            for (int i = wheelRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = wheelRoot.GetChild(i);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        slotOffsets = new[]
        {
            new Vector2(0f, radius),
            new Vector2(0.866f * radius, 0.5f * radius),
            new Vector2(0.866f * radius, -0.5f * radius),
            new Vector2(0f, -radius),
            new Vector2(-0.866f * radius, -0.5f * radius),
            new Vector2(-0.866f * radius, 0.5f * radius)
        };

        BuildHub();

        for (int i = 0; i < slotOffsets.Length; i++)
        {
            WheelButton button = CreateButton($"WheelButton_{i}");
            button.root.anchoredPosition = slotOffsets[i];
            buttons.Add(button);
        }
    }

    private void BuildHub()
    {
        RectTransform hub = new GameObject("Hub", typeof(RectTransform)).GetComponent<RectTransform>();
        hub.SetParent(wheelRoot, false);
        hub.sizeDelta = new Vector2(86f, 86f);

        Image hubFrame = hub.gameObject.AddComponent<Image>();
        hubFrame.sprite = hexSprite;
        hubFrame.color = new Color(0.16f, 0.86f, 0.62f, 0.92f);
        hubFrame.raycastTarget = false;

        RectTransform hubFillRect = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>();
        hubFillRect.SetParent(hub, false);
        hubFillRect.sizeDelta = new Vector2(74f, 74f);
        Image hubFill = hubFillRect.gameObject.AddComponent<Image>();
        hubFill.sprite = hexSprite;
        hubFill.color = new Color(0.01f, 0.08f, 0.06f, 0.94f);
        hubFill.raycastTarget = false;

        Shadow hubShadow = hub.gameObject.AddComponent<Shadow>();
        hubShadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        hubShadow.effectDistance = new Vector2(2f, -2f);
        hubShadow.useGraphicAlpha = true;

        TMP_Text hubLabel = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        RectTransform hubLabelRect = hubLabel.GetComponent<RectTransform>();
        hubLabelRect.SetParent(hub, false);
        hubLabelRect.anchorMin = Vector2.zero;
        hubLabelRect.anchorMax = Vector2.one;
        hubLabelRect.offsetMin = new Vector2(8f, 8f);
        hubLabelRect.offsetMax = new Vector2(-8f, -8f);
        hubLabel.font = font;
        hubLabel.fontSize = 20;
        hubLabel.alignment = TextAlignmentOptions.Center;
        hubLabel.text = "CMD";
        hubLabel.color = new Color(0.91f, 0.98f, 0.94f, 1f);
        hubLabel.raycastTarget = false;

        Shadow hubLabelShadow = hubLabel.gameObject.AddComponent<Shadow>();
        hubLabelShadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        hubLabelShadow.effectDistance = new Vector2(1f, -1f);
        hubLabelShadow.useGraphicAlpha = true;
    }

    private WheelButton CreateButton(string name)
    {
        RectTransform root = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(wheelRoot, false);
        root.sizeDelta = new Vector2(132f, 112f);

        Image hitArea = root.gameObject.AddComponent<Image>();
        hitArea.color = new Color(0f, 0f, 0f, 0.01f);

        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.targetGraphic = hitArea;
        WheelClickRelay relay = root.gameObject.AddComponent<WheelClickRelay>();

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
        colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 0.92f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
        button.colors = colors;

        RectTransform hexRoot = new GameObject("Hex", typeof(RectTransform)).GetComponent<RectTransform>();
        hexRoot.SetParent(root, false);
        hexRoot.anchorMin = new Vector2(0.5f, 1f);
        hexRoot.anchorMax = new Vector2(0.5f, 1f);
        hexRoot.pivot = new Vector2(0.5f, 1f);
        hexRoot.sizeDelta = new Vector2(90f, 90f);
        hexRoot.anchoredPosition = new Vector2(0f, 0f);

        Image frame = hexRoot.gameObject.AddComponent<Image>();
        frame.sprite = hexSprite;
        frame.color = new Color(0.16f, 0.86f, 0.62f, 0.92f);
        frame.raycastTarget = false;

        RectTransform fillRect = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>();
        fillRect.SetParent(hexRoot, false);
        fillRect.sizeDelta = new Vector2(78f, 78f);
        Image fill = fillRect.gameObject.AddComponent<Image>();
        fill.sprite = hexSprite;
        fill.color = new Color(0.02f, 0.08f, 0.06f, 0.97f);
        fill.raycastTarget = false;

        RectTransform plateRect = new GameObject("Plate", typeof(RectTransform)).GetComponent<RectTransform>();
        plateRect.SetParent(root, false);
        plateRect.anchorMin = new Vector2(0.5f, 0f);
        plateRect.anchorMax = new Vector2(0.5f, 0f);
        plateRect.pivot = new Vector2(0.5f, 0f);
        plateRect.sizeDelta = new Vector2(112f, 38f);
        plateRect.anchoredPosition = new Vector2(0f, 0f);
        Image plate = plateRect.gameObject.AddComponent<Image>();
        plate.color = new Color(0.01f, 0.07f, 0.06f, 0.96f);
        plate.raycastTarget = false;

        Outline plateOutline = plateRect.gameObject.AddComponent<Outline>();
        plateOutline.effectColor = new Color(0.16f, 0.86f, 0.62f, 0.78f);
        plateOutline.effectDistance = new Vector2(1f, -1f);
        plateOutline.useGraphicAlpha = true;

        Shadow plateShadow = plateRect.gameObject.AddComponent<Shadow>();
        plateShadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        plateShadow.effectDistance = new Vector2(2f, -2f);
        plateShadow.useGraphicAlpha = true;

        TMP_Text label = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.SetParent(plateRect, false);
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(6f, -1f);
        labelRect.offsetMax = new Vector2(-6f, -1f);
        label.font = font;
        label.fontSize = 18;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;

        Shadow labelShadow = label.gameObject.AddComponent<Shadow>();
        labelShadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
        labelShadow.effectDistance = new Vector2(1f, -1f);
        labelShadow.useGraphicAlpha = true;

        TMP_Text cost = new GameObject("Cost", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        RectTransform costRect = cost.GetComponent<RectTransform>();
        costRect.SetParent(plateRect, false);
        costRect.anchorMin = new Vector2(0f, 0f);
        costRect.anchorMax = new Vector2(1f, 0.5f);
        costRect.offsetMin = new Vector2(6f, 2f);
        costRect.offsetMax = new Vector2(-6f, 2f);
        cost.font = font;
        cost.fontSize = 14;
        cost.alignment = TextAlignmentOptions.Center;
        cost.textWrappingMode = TextWrappingModes.NoWrap;
        cost.overflowMode = TextOverflowModes.Ellipsis;
        cost.raycastTarget = false;

        Shadow costShadow = cost.gameObject.AddComponent<Shadow>();
        costShadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
        costShadow.effectDistance = new Vector2(1f, -1f);
        costShadow.useGraphicAlpha = true;

        TooltipTrigger trigger = root.gameObject.AddComponent<TooltipTrigger>();
        trigger.tooltip = tooltip;

        return new WheelButton
        {
            root = root,
            hitArea = hitArea,
            button = button,
            frame = frame,
            fill = fill,
            labelPlate = plate,
            label = label,
            cost = cost,
            tooltip = trigger,
            relay = relay
        };
    }

    private struct WheelAction
    {
        public string label;
        public bool hasActionType;
        public FiniteEarthActionType actionType;
        public Action onClick;
        public bool interactable;
        public FiniteEarthResourcePool cost;
        public FiniteEarthResourcePool displayCost;
        public bool showCost;
        public string reason;
        public Color accent;
    }

    private List<WheelAction> BuildContextActions(
        HexCoord coord,
        TileType terrain,
        BuildingType building,
        bool isOwned,
        bool hasLocalArmy,
        FiniteEarthGameOrchestrator orchestrator)
    {
        var list = new List<WheelAction>(6);
        Dictionary<FiniteEarthActionType, ActionAvailability> stateLookup = new Dictionary<FiniteEarthActionType, ActionAvailability>();
        IReadOnlyList<ActionAvailability> states = orchestrator.LastActionStates;
        for (int i = 0; i < states.Count; i++)
        {
            stateLookup[states[i].actionType] = states[i];
        }

        if (hasLocalArmy)
        {
            list.Add(BuildArmyMoveAction(orchestrator));
            list.Add(BuildArmyReinforceAction(orchestrator));
            return list;
        }

        if (building == BuildingType.Barracks)
        {
            TryAddAction(list, FiniteEarthActionType.SpawnArmy, stateLookup);
            return list;
        }

        if (building == BuildingType.Settlement)
        {
            list.Add(BuildPlaceholder("Anchored", "Settlements are permanent anchors. Build the next one on the bright outer ring."));
            return list;
        }

        if (building == BuildingType.Industry)
        {
            list.Add(BuildRemovalAction(building, stateLookup));
            return list;
        }

        if (!isOwned)
        {
            list.Add(BuildPlaceholder("Auto", "No manual claim step. Build a settlement closer to extend control here."));
            return list;
        }

        switch (terrain)
        {
            case TileType.Plains:
                TryAddAction(list, FiniteEarthActionType.BuildSettlement, stateLookup);
                TryAddAction(list, FiniteEarthActionType.BuildBarracks, stateLookup);
                TryAddAction(list, FiniteEarthActionType.BuildIndustry, stateLookup);
                TryAddAction(list, FiniteEarthActionType.Farm, stateLookup);
                TryAddAction(list, FiniteEarthActionType.Reforest, stateLookup);
                break;
            case TileType.Forest:
                TryAddAction(list, FiniteEarthActionType.HarvestForest, stateLookup);
                TryAddAction(list, FiniteEarthActionType.Reforest, stateLookup);
                break;
            case TileType.Barren:
                TryAddAction(list, FiniteEarthActionType.BuildIndustry, stateLookup);
                TryAddAction(list, FiniteEarthActionType.Restore, stateLookup);
                TryAddAction(list, FiniteEarthActionType.Reforest, stateLookup);
                TryAddAction(list, FiniteEarthActionType.Mine, stateLookup);
                break;
            case TileType.Mountain:
                TryAddAction(list, FiniteEarthActionType.BuildIndustry, stateLookup);
                TryAddAction(list, FiniteEarthActionType.Mine, stateLookup);
                break;
            case TileType.Desert:
                TryAddAction(list, FiniteEarthActionType.Irrigate, stateLookup);
                break;
        }

        return list;
    }

    private List<WheelAction> BuildArmyActions(FiniteEarthGameOrchestrator orchestrator)
    {
        var list = new List<WheelAction>(2)
        {
            BuildArmyMoveAction(orchestrator),
            BuildArmyReinforceAction(orchestrator)
        };
        return list;
    }

    private WheelAction BuildArmyMoveAction(FiniteEarthGameOrchestrator orchestrator)
    {
        float cooldownRemaining = 0f;
        bool hasArmy = orchestrator != null && orchestrator.TryGetSelectedArmy(out _, out cooldownRemaining);
        bool canMove = hasArmy && cooldownRemaining <= 0.01f;
        return new WheelAction
        {
            label = "Move",
            hasActionType = false,
            onClick = canMove ? new Action(() => orchestrator.ArmSelectedArmyMove()) : null,
            interactable = canMove,
            cost = default,
            displayCost = default,
            showCost = false,
            reason = canMove ? "Select an adjacent hex." : $"Move ready in {cooldownRemaining:0.0}s.",
            accent = new Color(0.28f, 0.58f, 0.90f, 1f)
        };
    }

    private WheelAction BuildArmyReinforceAction(FiniteEarthGameOrchestrator orchestrator)
    {
        FiniteEarthResourcePool cost = default;
        string reason = "Select an army first.";
        bool canReinforce = orchestrator != null && orchestrator.CanReinforceSelectedArmy(out cost, out reason);
        return new WheelAction
        {
            label = "Reinforce",
            hasActionType = false,
            onClick = canReinforce ? new Action(() => orchestrator.ReinforceSelectedArmy()) : null,
            interactable = canReinforce,
            cost = cost,
            displayCost = cost,
            showCost = true,
            reason = reason,
            accent = new Color(0.82f, 0.72f, 0.30f, 1f)
        };
    }

    private void TryAddAction(List<WheelAction> list, FiniteEarthActionType actionType, Dictionary<FiniteEarthActionType, ActionAvailability> lookup)
    {
        lookup.TryGetValue(actionType, out ActionAvailability state);
        bool hasSpec = ActionCatalog.TryGet(actionType, out ActionRuleSpec spec);
        list.Add(new WheelAction
        {
            label = hasSpec ? spec.label : actionType.ToString(),
            hasActionType = true,
            actionType = actionType,
            interactable = state.isInteractable,
            cost = state.cost,
            displayCost = hasSpec && state.cost.IsZero() ? spec.cost : state.cost,
            showCost = true,
            reason = state.reason,
            accent = hasSpec ? spec.accent : new Color(0.3f, 0.5f, 0.6f, 1f)
        });
    }

    private WheelAction BuildRemovalAction(BuildingType building, Dictionary<FiniteEarthActionType, ActionAvailability> lookup)
    {
        lookup.TryGetValue(FiniteEarthActionType.RemoveBuilding, out ActionAvailability state);
        ActionCatalog.TryGet(FiniteEarthActionType.RemoveBuilding, out ActionRuleSpec spec);

        FiniteEarthResourcePool refund = building switch
        {
            BuildingType.Industry => new FiniteEarthResourcePool { minerals = 1 },
            _ => default
        };

        string defaultReason = "Dismantle this industry and recover part of the cost.";
        string reason = string.IsNullOrWhiteSpace(state.reason) || string.Equals(state.reason, "Ready.", StringComparison.OrdinalIgnoreCase)
            ? defaultReason
            : state.reason;

        return new WheelAction
        {
            label = spec.label,
            hasActionType = true,
            actionType = FiniteEarthActionType.RemoveBuilding,
            interactable = state.isInteractable,
            cost = default,
            displayCost = refund,
            showCost = true,
            reason = reason,
            accent = spec.accent
        };
    }

    private static WheelAction BuildPlaceholder(string label, string reason)
    {
        return new WheelAction
        {
            label = label,
            hasActionType = false,
            actionType = FiniteEarthActionType.BuildSettlement,
            interactable = false,
            cost = default,
            displayCost = default,
            showCost = false,
            reason = reason,
            accent = new Color(0.25f, 0.30f, 0.34f, 1f)
        };
    }

    private void ApplyWheelActions(List<WheelAction> actions)
    {
        int count = Mathf.Min(actions.Count, buttons.Count);
        int[] slotOrder = ResolveSlotOrder(count);
        for (int i = 0; i < buttons.Count; i++)
        {
            if (i >= count)
            {
                buttons[i].root.gameObject.SetActive(false);
                continue;
            }

            WheelAction action = actions[i];
            WheelButton button = buttons[i];
            button.root.gameObject.SetActive(true);
            button.root.anchoredPosition = slotOffsets[slotOrder[i]];
            button.hasActionType = action.hasActionType;
            button.actionType = action.actionType;
            button.label.text = action.label.ToUpperInvariant();
            string economyText = BuildEconomyText(action);
            button.cost.text = action.showCost ? economyText : string.Empty;

            Color disabledFrame = new Color(0.24f, 0.32f, 0.28f, 0.68f);
            Color disabledFill = new Color(0.04f, 0.08f, 0.07f, 0.96f);
            Color plateColor = new Color(0.01f, 0.07f, 0.06f, 0.98f);
            button.frame.color = action.interactable
                ? new Color(action.accent.r, action.accent.g, action.accent.b, 0.95f)
                : disabledFrame;
            button.fill.color = action.interactable
                ? new Color(
                    Mathf.Lerp(0.02f, action.accent.r, 0.18f),
                    Mathf.Lerp(0.08f, action.accent.g, 0.18f),
                    Mathf.Lerp(0.06f, action.accent.b, 0.18f),
                    0.98f)
                : disabledFill;
            button.labelPlate.color = action.interactable
                ? new Color(
                    Mathf.Lerp(plateColor.r, action.accent.r, 0.14f),
                    Mathf.Lerp(plateColor.g, action.accent.g, 0.14f),
                    Mathf.Lerp(plateColor.b, action.accent.b, 0.14f),
                    0.98f)
                : plateColor;
            button.label.color = action.interactable ? new Color(0.95f, 0.99f, 0.95f, 1f) : new Color(0.60f, 0.70f, 0.64f, 1f);
            button.cost.color = action.interactable ? new Color(0.75f, 0.93f, 0.81f, 1f) : new Color(0.44f, 0.56f, 0.50f, 1f);

            button.button.onClick.RemoveAllListeners();
            button.button.interactable = action.interactable && (action.hasActionType || action.onClick != null);
            button.relay.callback = null;
            button.relay.acceptClicks = false;
            if (action.hasActionType)
            {
                button.relay.callback = () => actionHandler?.Invoke(action.actionType);
                button.relay.acceptClicks = true;
            }
            else if (action.onClick != null)
            {
                button.relay.callback = () => action.onClick.Invoke();
                button.relay.acceptClicks = true;
            }

            if (button.tooltip != null)
            {
                string reasonText = action.reason?.Trim() ?? string.Empty;
                bool suppressTooltip = action.interactable
                    && (string.IsNullOrWhiteSpace(reasonText)
                        || string.Equals(reasonText, "READY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(reasonText, "READY.", StringComparison.OrdinalIgnoreCase));

                if (suppressTooltip)
                {
                    button.tooltip.tooltipText = string.Empty;
                    continue;
                }

                string costLine = action.showCost ? economyText : string.Empty;
                if (!string.IsNullOrWhiteSpace(costLine))
                {
                    button.tooltip.tooltipText = BuildTooltipText(action.label, reasonText, costLine);
                }
                else
                {
                    button.tooltip.tooltipText = BuildTooltipText(action.label, reasonText, string.Empty);
                }
            }
        }
    }

    private static string BuildEconomyText(WheelAction action)
    {
        if (!action.showCost)
        {
            return string.Empty;
        }

        if (action.actionType == FiniteEarthActionType.RemoveBuilding)
        {
            return action.displayCost.IsZero()
                ? "[NO REFUND]"
                : $"[+W{action.displayCost.wood} +F{action.displayCost.food} +M{action.displayCost.minerals}]";
        }

        return action.displayCost.IsZero()
            ? "[FREE]"
            : $"[W{action.displayCost.wood} F{action.displayCost.food} M{action.displayCost.minerals}]";
    }

    private static string BuildTooltipText(string label, string reasonText, string economyText)
    {
        string title = string.IsNullOrWhiteSpace(label) ? "ACTION" : label.ToUpperInvariant();
        string body = string.IsNullOrWhiteSpace(reasonText) ? string.Empty : reasonText.Trim();

        if (string.IsNullOrWhiteSpace(body))
        {
            return string.IsNullOrWhiteSpace(economyText)
                ? title
                : $"{title}\n{economyText}";
        }

        return string.IsNullOrWhiteSpace(economyText)
            ? $"{title}\n{body}"
            : $"{title}\n{body}\n{economyText}";
    }

    private static int[] ResolveSlotOrder(int count)
    {
        return count switch
        {
            <= 0 => Array.Empty<int>(),
            1 => new[] { 0 },
            2 => new[] { 5, 1 },
            3 => new[] { 5, 0, 1 },
            4 => new[] { 5, 0, 1, 3 },
            5 => new[] { 5, 0, 1, 2, 4 },
            _ => new[] { 0, 1, 2, 3, 4, 5 }
        };
    }

    private void PositionWheelAt(HexCoord coord, HexWorldGeneratorTilemap world)
    {
        if (wheelRoot == null || worldCamera == null || world == null)
        {
            return;
        }

        Vector3 worldPos = world.GetCellCenterWorld(coord.ToVector3Int());
        Vector3 screenPos = worldCamera.WorldToScreenPoint(worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out Vector2 localPoint);
        localPoint = ClampToCanvas(localPoint);
        wheelRoot.anchoredPosition = localPoint;
    }

    private Vector2 ClampToCanvas(Vector2 anchored)
    {
        if (canvasRect == null)
        {
            return anchored;
        }

        float halfWidth = canvasRect.rect.width * 0.5f;
        float halfHeight = canvasRect.rect.height * 0.5f;
        float horizontalExtent = radius + 76f;
        float verticalExtent = radius + 64f;
        float x = Mathf.Clamp(anchored.x, -halfWidth + screenMargin.x + horizontalExtent, halfWidth - screenMargin.x - horizontalExtent);
        float y = Mathf.Clamp(anchored.y, -halfHeight + screenMargin.y + verticalExtent, halfHeight - screenMargin.y - verticalExtent);
        return new Vector2(x, y);
    }

    private void SetVisible(bool show, bool instant)
    {
        if (canvasGroup == null)
        {
            return;
        }

        float target = show ? 1f : 0f;
        canvasGroup.alpha = instant ? target : Mathf.MoveTowards(canvasGroup.alpha, target, 8f * Time.unscaledDeltaTime);
        canvasGroup.blocksRaycasts = show;
        canvasGroup.interactable = show;
    }
}
