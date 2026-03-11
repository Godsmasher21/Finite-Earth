using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AsciiHudPresenter : MonoBehaviour
{
    private static AsciiHudPresenter instance;

    [Header("References")]
    [SerializeField] private GameStateViewModel viewModel;
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;
    [SerializeField] private OwnershipOverlayPointTop ownership;
    [SerializeField] private FiniteEarthGameOrchestrator orchestrator;
    [SerializeField] private WalletSessionController walletSession;

    [Header("Runtime HUD")]
    [SerializeField] private bool autoCreateRuntimeHud = true;
    [SerializeField] private bool forceRuntimeHudRebuild = true;
    [SerializeField] private bool enforceGreenAsciiTheme = true;
    [SerializeField] private bool disableIfCommandTablePresent = true;
    [SerializeField] private Vector2 globalPanelSize = new Vector2(900f, 126f);
    [SerializeField] private Vector2 globalPanelOffset = new Vector2(16f, -16f);
    [SerializeField] private Vector2 resourcePanelSize = new Vector2(380f, 84f);
    [SerializeField] private Vector2 resourcePanelOffset = new Vector2(16f, -188f);
    [SerializeField] private float hudPanelSpacing = 10f;
    [SerializeField] private float refreshIntervalSeconds = 0.25f;

    [Header("ASCII Theme")]
    [SerializeField] private Color panelColor = new Color(0.05f, 0.38f, 0.26f, 0.95f);
    [SerializeField] private Color borderColor = new Color(0.96f, 0.97f, 0.98f, 0.96f);
    [SerializeField] private Color textColor = new Color(0.95f, 0.98f, 0.96f, 1f);
    [SerializeField] private int globalFontSize = 22;
    [SerializeField] private int resourceFontSize = 24;
    [SerializeField] private Color demoBadgeColor = new Color(1f, 0.85f, 0.30f, 1f);
    [SerializeField] private float resourceDeltaDuration = 0.85f;
    [SerializeField] private float resourceDeltaRisePixels = 18f;
    [SerializeField] private Color resourceGainColor = new Color(0.35f, 0.95f, 0.62f, 1f);
    [SerializeField] private Color resourceLossColor = new Color(1f, 0.45f, 0.45f, 1f);
    [SerializeField] private Color resourceMixedColor = new Color(1f, 0.82f, 0.45f, 1f);

    [Header("HUD Icons")]
    [SerializeField] private Sprite planetHealthIcon;
    [SerializeField] private Sprite woodIcon;
    [SerializeField] private Sprite foodIcon;
    [SerializeField] private Sprite oreIcon;
    [SerializeField] private Sprite heatwaveIcon;
    [SerializeField] private Sprite wildfireIcon;
    [SerializeField] private Sprite floodIcon;
    [SerializeField] private Sprite iceMeltIcon;
    [SerializeField] private Sprite desertIcon;

    private Text globalHudText;
    private Text resourceHudText;
    private Text resourceDeltaText;
    private Text demoBadgeText;
    private Text planetHealthText;
    private Image planetHealthIconImage;
    private Image[] climateIconImages;
    private Text woodText;
    private Text foodText;
    private Text oreText;
    private RectTransform globalHudRootRect;
    private RectTransform resourceHudRootRect;
    private RectTransform resourceDeltaRect;
    private Font runtimeFont;
    private float nextRefreshAt;
    private FiniteEarthResourcePool lastResources;
    private bool hasLastResources;
    private float resourceDeltaRemaining;
    private Color activeResourceDeltaColor;
    private readonly Vector2 resourceDeltaBaseOffset = new Vector2(12f, -40f);

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        if (TryDisableForCommandTable())
        {
            return;
        }

        ResolveRuntimeReferences();
        ResolveRuntimeIcons();
        ApplyForcedThemeIfNeeded();

        if (forceRuntimeHudRebuild)
        {
            DestroyLegacyHudRoots();
        }

        if (autoCreateRuntimeHud)
        {
            CreateRuntimeHud();
        }

        InitializeLastResourceSnapshot();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private bool TryDisableForCommandTable()
    {
        if (!disableIfCommandTablePresent)
        {
            return false;
        }

        if (FindAnyObjectByType<CommandTableHudPresenter>() == null)
        {
            return false;
        }

        autoCreateRuntimeHud = false;
        enabled = false;
        return true;
    }

    private void OnEnable()
    {
        if (TryDisableForCommandTable())
        {
            return;
        }

        if (viewModel != null)
        {
            viewModel.ResolutionApplied += HandleResolutionApplied;
        }
    }

    private void OnDisable()
    {
        if (viewModel != null)
        {
            viewModel.ResolutionApplied -= HandleResolutionApplied;
        }
    }

    private void Update()
    {
        ResolveRuntimeReferences();
        TickResourceDeltaAnimation();

        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }

        nextRefreshAt = Time.unscaledTime + Mathf.Max(0.05f, refreshIntervalSeconds);
        RefreshHud();
    }

    private void HandleResolutionApplied(ActionResolution _)
    {
        RefreshHud();
    }

    private void ResolveRuntimeReferences()
    {
        if (viewModel == null)
        {
            viewModel = FindAnyObjectByType<GameStateViewModel>();
        }

        if (worldGenerator == null)
        {
            worldGenerator = FindAnyObjectByType<HexWorldGeneratorTilemap>();
        }

        if (ownership == null)
        {
            ownership = FindAnyObjectByType<OwnershipOverlayPointTop>();
        }

        if (orchestrator == null)
        {
            orchestrator = FindAnyObjectByType<FiniteEarthGameOrchestrator>();
        }

        if (walletSession == null)
        {
            walletSession = FindAnyObjectByType<WalletSessionController>();
        }
    }

    private void ResolveRuntimeIcons()
    {
        if (planetHealthIcon == null)
        {
            planetHealthIcon = FiniteEarthIconLibrary.GetPlanetHealthIcon();
        }

        if (woodIcon == null)
        {
            woodIcon = FiniteEarthIconLibrary.GetWoodIcon();
        }

        if (foodIcon == null)
        {
            foodIcon = FiniteEarthIconLibrary.GetFoodIcon();
        }

        if (oreIcon == null)
        {
            oreIcon = FiniteEarthIconLibrary.GetOreIcon();
        }

        if (heatwaveIcon == null)
        {
            heatwaveIcon = FiniteEarthIconLibrary.GetClimateIcon(ClimateEventType.Heatwave);
        }

        if (wildfireIcon == null)
        {
            wildfireIcon = FiniteEarthIconLibrary.GetClimateIcon(ClimateEventType.Wildfire);
        }

        if (floodIcon == null)
        {
            floodIcon = FiniteEarthIconLibrary.GetClimateIcon(ClimateEventType.Flood);
        }

        if (iceMeltIcon == null)
        {
            iceMeltIcon = FiniteEarthIconLibrary.GetClimateIcon(ClimateEventType.IceMelt);
        }

        if (desertIcon == null)
        {
            desertIcon = FiniteEarthIconLibrary.GetClimateIcon(ClimateEventType.DesertSpread);
        }
    }

    private void CreateRuntimeHud()
    {
        Canvas canvas = EnsureCanvas();
        runtimeFont = ResolveRuntimeFont();
        resourcePanelOffset = ResolveResourcePanelOffset();

        CreatePanel(
            canvas.transform,
            "AsciiTopHud",
            globalPanelOffset,
            globalPanelSize,
            out globalHudRootRect,
            out globalHudText,
            Mathf.Clamp(globalFontSize, 12, 28),
            TextAnchor.UpperLeft,
            new Vector2(10f, 6f),
            new Vector2(-10f, -6f));

        CreatePanel(
            canvas.transform,
            "AsciiResourceHud",
            resourcePanelOffset,
            resourcePanelSize,
            out resourceHudRootRect,
            out resourceHudText,
            Mathf.Clamp(resourceFontSize, 12, 28),
            TextAnchor.UpperLeft,
            new Vector2(10f, 6f),
            new Vector2(-10f, -6f));

        CreatePlanetHealthRow();
        CreateClimateIconRow();
        CreateResourceRow();
        CreateDemoBadge();
        EnsureResourceDeltaText();
        RemoveLegacyHudTutorialPopup();
        RefreshHud();
    }

    private Canvas EnsureCanvas()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas != null)
        {
            return canvas;
        }

        GameObject canvasObject = new GameObject("RuntimeAsciiCanvas");
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private void CreatePanel(
        Transform parent,
        string rootName,
        Vector2 anchoredPosition,
        Vector2 size,
        out RectTransform rootRect,
        out Text text,
        int fontSize,
        TextAnchor alignment,
        Vector2 textOffsetMin,
        Vector2 textOffsetMax)
    {
        GameObject root = new GameObject(rootName);
        root.transform.SetParent(parent, false);

        rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = anchoredPosition;
        rootRect.sizeDelta = size;

        Image bg = root.AddComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = false;

        Outline outline = root.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(root.transform, false);

        text = textObject.AddComponent<Text>();
        text.font = runtimeFont;
        text.fontSize = fontSize;
        text.supportRichText = false;
        text.color = textColor;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.lineSpacing = 0.98f;

        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textOffsetMin;
        textRect.offsetMax = textOffsetMax;
    }

    private Text CreateText(Transform parent, int size, Color color, TextAnchor alignment)
    {
        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(parent, false);

        Text text = textObject.AddComponent<Text>();
        text.font = runtimeFont;
        text.fontSize = Mathf.Clamp(size, 12, 26);
        text.supportRichText = false;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.lineSpacing = 0.98f;
        return text;
    }

    private void RefreshHud()
    {
        if (globalHudText == null || resourceHudText == null)
        {
            return;
        }

        int forest = viewModel != null && viewModel.WorldState != null
            ? viewModel.WorldState.globalForestToken
            : (worldGenerator != null ? worldGenerator.CountTilesOfType(TileType.Forest) : 0);
        int carbon = viewModel != null && viewModel.WorldState != null
            ? viewModel.WorldState.globalCarbonToken
            : (worldGenerator != null ? worldGenerator.CalculateCarbonScore() : 0);
        int owned = ownership != null ? ownership.GetOwnedCount() : 0;
        int tick = viewModel != null && viewModel.WorldState != null ? viewModel.WorldState.tick : 0;
        FiniteEarthResourcePool resources = viewModel != null && viewModel.PlayerState != null
            ? viewModel.PlayerState.resources
            : default;
        string reputation = viewModel != null && viewModel.PlayerState != null
            ? viewModel.PlayerState.reputationLabel
            : "Balanced";

        TrackResourceDelta(resources);

        string wallet = viewModel != null && viewModel.PlayerState != null
            ? ShortWallet(viewModel.PlayerState.walletAddress)
            : "local";
        bool isDemoMode = walletSession != null && walletSession.IsRuntimeDemoMode;
        float cycleLeft = orchestrator != null ? orchestrator.CycleRemainingSeconds : 0f;
        string cycleText = FormatTimer(cycleLeft);
        string modeText = orchestrator != null && orchestrator.UsesLocalCycleClock ? "LOCAL" : "SERVER";

        globalHudText.text =
            "+--------------------- GLOBAL COUNTERS ---------------------+\n" +
            $"FOREST {forest} | CARBON {carbon} | OWNED {owned} | REP {reputation} | TICK {tick}\n" +
            $"CYCLE {cycleText} ({modeText}){(isDemoMode ? " [DEMO]" : string.Empty)} | WALLET {wallet}";

        resourceHudText.text =
            "+-- RESOURCES --+";

        if (woodText != null) woodText.text = $"WOOD {resources.wood}";
        if (foodText != null) foodText.text = $"FOOD {resources.food}";
        if (oreText != null) oreText.text = $"ORE {resources.minerals}";

        UpdatePlanetHealth();
        UpdateClimateIcons();

        UpdateDemoBadge(isDemoMode);
    }

    private void UpdatePlanetHealth()
    {
        if (planetHealthText == null || viewModel == null || viewModel.WorldState == null)
        {
            return;
        }

        int score = Mathf.Clamp(viewModel.WorldState.ecosystemScore, 0, 100);
        planetHealthText.text = $"PLANET HEALTH: {score}%";

        if (score >= 70)
        {
            planetHealthText.color = new Color(0.36f, 0.92f, 0.54f, 1f);
        }
        else if (score >= 40)
        {
            planetHealthText.color = new Color(1f, 0.82f, 0.38f, 1f);
        }
        else
        {
            planetHealthText.color = new Color(1f, 0.44f, 0.44f, 1f);
        }
    }

    private void UpdateClimateIcons()
    {
        if (climateIconImages == null || orchestrator == null)
        {
            return;
        }

        ClimateEventInstance[] events = orchestrator.GetActiveClimateEvents();

        for (int i = 0; i < climateIconImages.Length; i++)
        {
            Image icon = climateIconImages[i];
            if (icon == null)
            {
                continue;
            }

            if (events == null || i >= events.Length)
            {
                icon.enabled = false;
                continue;
            }

            icon.sprite = ResolveClimateSprite(events[i].type);
            icon.enabled = icon.sprite != null;
        }
    }

    private Sprite ResolveClimateSprite(ClimateEventType type)
    {
        switch (type)
        {
            case ClimateEventType.Heatwave:
                return heatwaveIcon;
            case ClimateEventType.Wildfire:
                return wildfireIcon;
            case ClimateEventType.Flood:
                return floodIcon;
            case ClimateEventType.IceMelt:
                return iceMeltIcon;
            case ClimateEventType.DesertSpread:
                return desertIcon;
            default:
                return null;
        }
    }

    private Font ResolveRuntimeFont()
    {
        return AsciiFontResolver.ResolveFont(18);
    }

    private void CreateDemoBadge()
    {
        if (demoBadgeText != null || globalHudRootRect == null)
        {
            return;
        }

        GameObject badgeObject = new GameObject("DemoBadgeText");
        badgeObject.transform.SetParent(globalHudRootRect, false);

        demoBadgeText = badgeObject.AddComponent<Text>();
        demoBadgeText.font = runtimeFont;
        demoBadgeText.fontSize = Mathf.Clamp(globalFontSize - 1, 11, 28);
        demoBadgeText.supportRichText = false;
        demoBadgeText.color = demoBadgeColor;
        demoBadgeText.alignment = TextAnchor.UpperRight;
        demoBadgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        demoBadgeText.verticalOverflow = VerticalWrapMode.Overflow;
        demoBadgeText.raycastTarget = false;
        demoBadgeText.text = "[ DEMO ]";

        RectTransform badgeRect = demoBadgeText.rectTransform;
        badgeRect.anchorMin = new Vector2(1f, 1f);
        badgeRect.anchorMax = new Vector2(1f, 1f);
        badgeRect.pivot = new Vector2(1f, 1f);
        badgeRect.anchoredPosition = new Vector2(-10f, -6f);
        badgeRect.sizeDelta = new Vector2(132f, 24f);
    }

    private void UpdateDemoBadge(bool isDemoMode)
    {
        if (demoBadgeText == null)
        {
            CreateDemoBadge();
        }

        if (demoBadgeText != null)
        {
            demoBadgeText.enabled = isDemoMode;
        }
    }

    private void EnsureResourceDeltaText()
    {
        if (resourceDeltaText != null || resourceHudRootRect == null)
        {
            return;
        }

        GameObject deltaObject = new GameObject("ResourceDeltaText");
        deltaObject.transform.SetParent(resourceHudRootRect, false);

        resourceDeltaText = deltaObject.AddComponent<Text>();
        resourceDeltaText.font = runtimeFont;
        resourceDeltaText.fontSize = Mathf.Clamp(resourceFontSize - 2, 11, 24);
        resourceDeltaText.supportRichText = false;
        resourceDeltaText.color = new Color(textColor.r, textColor.g, textColor.b, 0f);
        resourceDeltaText.alignment = TextAnchor.UpperLeft;
        resourceDeltaText.horizontalOverflow = HorizontalWrapMode.Overflow;
        resourceDeltaText.verticalOverflow = VerticalWrapMode.Overflow;
        resourceDeltaText.raycastTarget = false;
        resourceDeltaText.text = string.Empty;

        resourceDeltaRect = resourceDeltaText.rectTransform;
        resourceDeltaRect.anchorMin = new Vector2(0f, 1f);
        resourceDeltaRect.anchorMax = new Vector2(0f, 1f);
        resourceDeltaRect.pivot = new Vector2(0f, 1f);
        resourceDeltaRect.anchoredPosition = resourceDeltaBaseOffset;
        resourceDeltaRect.sizeDelta = new Vector2(Mathf.Max(220f, resourcePanelSize.x - 20f), 24f);
    }

    private void CreatePlanetHealthRow()
    {
        if (planetHealthText != null || globalHudRootRect == null)
        {
            return;
        }

        GameObject row = new GameObject("PlanetHealthRow");
        row.transform.SetParent(globalHudRootRect, false);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.spacing = 6f;
        layout.childControlHeight = true;
        layout.childControlWidth = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        RectTransform rowRect = row.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(0f, 1f);
        rowRect.pivot = new Vector2(0f, 1f);
        rowRect.anchoredPosition = new Vector2(10f, -70f);
        rowRect.sizeDelta = new Vector2(globalPanelSize.x - 20f, 26f);

        if (planetHealthIcon != null)
        {
            GameObject iconObject = new GameObject("PlanetIcon");
            iconObject.transform.SetParent(row.transform, false);
            planetHealthIconImage = iconObject.AddComponent<Image>();
            planetHealthIconImage.sprite = planetHealthIcon;
            planetHealthIconImage.preserveAspect = true;
            planetHealthIconImage.color = Color.white;
            RectTransform iconRect = planetHealthIconImage.rectTransform;
            iconRect.sizeDelta = new Vector2(20f, 20f);
        }

        planetHealthText = CreateText(row.transform, Mathf.Clamp(globalFontSize - 2, 12, 26), textColor, TextAnchor.MiddleLeft);
        planetHealthText.text = "PLANET HEALTH: 100%";
        planetHealthText.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
    }

    private void CreateClimateIconRow()
    {
        if (climateIconImages != null || globalHudRootRect == null)
        {
            return;
        }

        climateIconImages = new Image[2];
        GameObject row = new GameObject("ClimateIcons");
        row.transform.SetParent(globalHudRootRect, false);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperRight;
        layout.spacing = 4f;
        layout.childControlHeight = true;
        layout.childControlWidth = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        RectTransform rowRect = row.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(1f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(1f, 1f);
        rowRect.anchoredPosition = new Vector2(-10f, -70f);
        rowRect.sizeDelta = new Vector2(120f, 24f);

        for (int i = 0; i < climateIconImages.Length; i++)
        {
            GameObject iconObject = new GameObject($"ClimateIcon{i}");
            iconObject.transform.SetParent(row.transform, false);
            Image icon = iconObject.AddComponent<Image>();
            icon.enabled = false;
            icon.preserveAspect = true;
            RectTransform iconRect = icon.rectTransform;
            iconRect.sizeDelta = new Vector2(18f, 18f);
            climateIconImages[i] = icon;
        }
    }

    private void CreateResourceRow()
    {
        if (woodText != null || resourceHudRootRect == null)
        {
            return;
        }

        GameObject row = new GameObject("ResourceRow");
        row.transform.SetParent(resourceHudRootRect, false);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 10f;
        layout.childControlHeight = true;
        layout.childControlWidth = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        RectTransform rowRect = row.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(0f, 1f);
        rowRect.pivot = new Vector2(0f, 1f);
        rowRect.anchoredPosition = new Vector2(10f, -36f);
        rowRect.sizeDelta = new Vector2(resourcePanelSize.x - 20f, 28f);

        woodText = CreateResourceCell(row.transform, woodIcon, "WOOD 0");
        foodText = CreateResourceCell(row.transform, foodIcon, "FOOD 0");
        oreText = CreateResourceCell(row.transform, oreIcon, "ORE 0");
    }

    private Text CreateResourceCell(Transform parent, Sprite iconSprite, string label)
    {
        GameObject cell = new GameObject("ResourceCell");
        cell.transform.SetParent(parent, false);

        HorizontalLayoutGroup layout = cell.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 4f;
        layout.childControlHeight = true;
        layout.childControlWidth = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        if (iconSprite != null)
        {
            GameObject iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(cell.transform, false);
            Image icon = iconObject.AddComponent<Image>();
            icon.sprite = iconSprite;
            icon.preserveAspect = true;
            RectTransform iconRect = icon.rectTransform;
            iconRect.sizeDelta = new Vector2(18f, 18f);
        }

        Text text = CreateText(cell.transform, Mathf.Clamp(resourceFontSize - 2, 12, 26), textColor, TextAnchor.MiddleLeft);
        text.text = label;
        return text;
    }

    private static void RemoveLegacyHudTutorialPopup()
    {
        GameObject[] roots = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject go = roots[i];
            if (go == null)
            {
                continue;
            }

            if (!string.Equals(go.name, "HudTutorialPopup", StringComparison.Ordinal))
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(go);
            }
        }
    }

    private void InitializeLastResourceSnapshot()
    {
        if (viewModel == null || viewModel.PlayerState == null)
        {
            return;
        }

        lastResources = viewModel.PlayerState.resources;
        hasLastResources = true;
    }

    private void TrackResourceDelta(FiniteEarthResourcePool current)
    {
        EnsureResourceDeltaText();
        if (!hasLastResources)
        {
            lastResources = current;
            hasLastResources = true;
            return;
        }

        int woodDelta = current.wood - lastResources.wood;
        int foodDelta = current.food - lastResources.food;
        int oreDelta = current.minerals - lastResources.minerals;

        if (woodDelta == 0 && foodDelta == 0 && oreDelta == 0)
        {
            return;
        }

        lastResources = current;
        if (resourceDeltaText == null)
        {
            return;
        }

        string deltaText = BuildResourceDeltaText(woodDelta, foodDelta, oreDelta);
        if (string.IsNullOrWhiteSpace(deltaText))
        {
            return;
        }

        activeResourceDeltaColor = ResolveResourceDeltaColor(woodDelta, foodDelta, oreDelta);
        resourceDeltaText.text = deltaText;
        resourceDeltaRemaining = Mathf.Max(0.1f, resourceDeltaDuration);
        ApplyResourceDeltaVisual(0f, 1f);
    }

    private static string BuildResourceDeltaText(int woodDelta, int foodDelta, int oreDelta)
    {
        var parts = new List<string>(3);
        if (woodDelta != 0)
        {
            parts.Add($"{(woodDelta > 0 ? "+" : string.Empty)}{woodDelta} WOOD");
        }

        if (foodDelta != 0)
        {
            parts.Add($"{(foodDelta > 0 ? "+" : string.Empty)}{foodDelta} FOOD");
        }

        if (oreDelta != 0)
        {
            parts.Add($"{(oreDelta > 0 ? "+" : string.Empty)}{oreDelta} ORE");
        }

        return parts.Count == 0 ? string.Empty : string.Join("   ", parts);
    }

    private Color ResolveResourceDeltaColor(int woodDelta, int foodDelta, int oreDelta)
    {
        bool hasPositive = woodDelta > 0 || foodDelta > 0 || oreDelta > 0;
        bool hasNegative = woodDelta < 0 || foodDelta < 0 || oreDelta < 0;

        if (hasPositive && !hasNegative)
        {
            return resourceGainColor;
        }

        if (hasNegative && !hasPositive)
        {
            return resourceLossColor;
        }

        return resourceMixedColor;
    }

    private void TickResourceDeltaAnimation()
    {
        if (resourceDeltaText == null)
        {
            return;
        }

        if (resourceDeltaRemaining <= 0f)
        {
            if (!string.IsNullOrEmpty(resourceDeltaText.text))
            {
                resourceDeltaText.text = string.Empty;
            }

            Color hidden = resourceDeltaText.color;
            if (hidden.a > 0f)
            {
                hidden.a = 0f;
                resourceDeltaText.color = hidden;
            }

            if (resourceDeltaRect != null)
            {
                resourceDeltaRect.anchoredPosition = resourceDeltaBaseOffset;
            }

            return;
        }

        float duration = Mathf.Max(0.1f, resourceDeltaDuration);
        resourceDeltaRemaining = Mathf.Max(0f, resourceDeltaRemaining - Time.unscaledDeltaTime);
        float normalized = 1f - (resourceDeltaRemaining / duration);
        float alpha = 1f - Mathf.Clamp01(normalized * 1.15f);
        ApplyResourceDeltaVisual(normalized, alpha);
    }

    private void ApplyResourceDeltaVisual(float normalized, float alpha)
    {
        if (resourceDeltaText == null)
        {
            return;
        }

        Color active = activeResourceDeltaColor;
        active.a = Mathf.Clamp01(alpha);
        resourceDeltaText.color = active;

        if (resourceDeltaRect != null)
        {
            float rise = Mathf.Lerp(0f, resourceDeltaRisePixels, Mathf.Clamp01(normalized));
            resourceDeltaRect.anchoredPosition = resourceDeltaBaseOffset + new Vector2(0f, rise);
        }
    }

    private void ApplyForcedThemeIfNeeded()
    {
        if (!enforceGreenAsciiTheme)
        {
            return;
        }

        panelColor = new Color(0.05f, 0.38f, 0.26f, 0.95f);
        borderColor = new Color(0.96f, 0.97f, 0.98f, 0.96f);
        textColor = new Color(0.95f, 0.98f, 0.96f, 1f);
        globalPanelSize = new Vector2(900f, 126f);
        resourcePanelSize = new Vector2(380f, 84f);
        resourcePanelOffset = ResolveResourcePanelOffset();
    }

    private Vector2 ResolveResourcePanelOffset()
    {
        float spacing = Mathf.Max(4f, hudPanelSpacing);
        float y = globalPanelOffset.y - globalPanelSize.y - spacing;
        return new Vector2(globalPanelOffset.x, y);
    }

    private static string ShortWallet(string wallet)
    {
        if (string.IsNullOrWhiteSpace(wallet))
        {
            return "local";
        }

        if (wallet.Length <= 12)
        {
            return wallet;
        }

        return $"{wallet.Substring(0, 6)}..{wallet.Substring(wallet.Length - 4)}";
    }

    private static string FormatTimer(float seconds)
    {
        int clamped = Mathf.Max(0, Mathf.CeilToInt(seconds));
        int mins = clamped / 60;
        int secs = clamped % 60;
        return $"{mins:00}:{secs:00}";
    }

    private static void DestroyLegacyHudRoots()
    {
        string[] names =
        {
            "AsciiTopHud",
            "AsciiResourceHud",
            "RuntimeHUD",
            "TopBar",
            "ResourceBar"
        };

        for (int i = 0; i < names.Length; i++)
        {
            GameObject[] roots = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int j = 0; j < roots.Length; j++)
            {
                GameObject go = roots[j];
                if (go == null)
                {
                    continue;
                }

                if (!string.Equals(go.name, names[i], StringComparison.Ordinal))
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(go);
                }
                else
                {
                    DestroyImmediate(go);
                }
            }
        }
    }
}
