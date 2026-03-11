using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class CommandTableHudPresenter : MonoBehaviour
{
    private const string HudCanvasObjectName = "CommandTableHUDCanvas";
    private const string PanelRevealFeedbackObjectName = "HUD_PanelRevealFeel";
    private const string ActionClickFeedbackObjectName = "HUD_ActionClickFeel";
    private const string NotificationFeedbackObjectName = "HUD_NotificationFeel";
    private const string MmfPlayerTypeName = "MoreMountains.Feedbacks.MMF_Player";
    private const float TopBarHeight = 92f;
    private const float RightColumnWidth = 256f;
    private static readonly Color NegativeDeltaColor = new Color(0.96f, 0.43f, 0.43f, 1f);
    private static readonly Color PositiveDeltaColor = new Color(0.40f, 0.92f, 0.66f, 1f);

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] private float safeMargin = 16f;
    [SerializeField] private int canvasSortingOrder = 100;

    [Header("Theme")]
    [SerializeField] private bool enforceAsciiCommandTableTheme = true;

    [Header("Colors")]
    [SerializeField] private Color panelColor = new Color(0.055f, 0.078f, 0.094f, 0.95f);
    [SerializeField] private Color borderColor = new Color(0.11f, 0.16f, 0.20f, 0.95f);
    [SerializeField] private Color textColor = new Color(0.90f, 0.94f, 0.94f, 1f);
    [SerializeField] private Color mutedTextColor = new Color(0.60f, 0.66f, 0.68f, 1f);
    [SerializeField] private Color accentTeal = new Color(0.18f, 0.85f, 0.75f, 1f);
    [SerializeField] private Color accentAmber = new Color(0.95f, 0.75f, 0.30f, 1f);

    [Header("Auto Setup")]
    [SerializeField] private bool autoSetupFeedbackHooks = true;

    [Header("Feedback")]
    [SerializeField] private Component panelRevealFeedback;
    [SerializeField] private Component actionClickFeedback;
    [SerializeField] private Component notificationFeedback;
    [SerializeField] private GameObject actionPingPrefab;
    [SerializeField] private GameObject climatePingPrefab;

    private Canvas hudCanvas;
    private RectTransform hudRoot;
    private RectTransform topBarRoot;
    private RectTransform topBarRightCluster;
    private TMP_FontAsset fontAsset;
    private Sprite solidSprite;
    private Sprite hexSprite;

    private FiniteEarthGameOrchestrator orchestrator;
    private GameStateViewModel viewModel;
    private HexWorldGeneratorTilemap worldGenerator;
    private OwnershipOverlayPointTop ownership;

    private TMP_Text planetHealthLabel;
    private TMP_Text planetHealthMetaText;
    private Image planetHealthFill;
    private Image planetHealthIconImage;
    private TMP_Text forestText;
    private TMP_Text carbonText;
    private TMP_Text woodText;
    private TMP_Text woodMetaText;
    private TMP_Text mineralsText;
    private TMP_Text mineralsMetaText;
    private TMP_Text foodText;
    private TMP_Text foodMetaText;
    private TMP_Text playerText;
    private TMP_Text tickText;
    private TMP_Text fieldLogClimateText;
    private readonly List<Image> climateStatusIcons = new List<Image>();
    private float nextRateSnapshotAt;
    private ResourceRateSnapshot cachedRateSnapshot;

    private CanvasGroup climateModalGroup;
    private RectTransform climateModalRoot;
    private Image climateModalIcon;
    private TMP_Text climateModalTitle;
    private TMP_Text climateModalBody;
    private Button climateModalOkButton;
    private readonly Queue<ClimateEventType> pendingClimateModalQueue = new Queue<ClimateEventType>();
    private bool climateModalVisible;

    private TileScannerPresenter tileScanner;
    private ActionConsolePresenter actionConsole;
    private NotificationFeedPresenter notificationFeed;
    private TooltipPresenter tooltip;
    private OverlayTogglePresenter overlayToggle;
    private HexOverlayPainter overlayPainter;
    private ActionWheelPresenter actionWheel;
    private ResourcePopupPresenter resourcePopups;
    private RectTransform eventFeedPanel;
    private RectTransform miniMapPanel;
    private bool eventsBound;
    private bool hasLoggedFontResolutionError;

    private void Reset()
    {
        ApplyThemePreset();
        EnsureFeedbackHooks(true);
    }

    private void OnValidate()
    {
        ApplyThemePreset();
        if (Application.isPlaying || !autoSetupFeedbackHooks)
        {
            return;
        }

        if (gameObject == null || !gameObject.scene.IsValid())
        {
            return;
        }

        EnsureFeedbackHooks(true);
    }

    private readonly FiniteEarthActionType[] consoleOrder =
    {
        FiniteEarthActionType.Claim,
        FiniteEarthActionType.BuildSettlement,
        FiniteEarthActionType.BuildBarracks,
        FiniteEarthActionType.BuildIndustry,
        FiniteEarthActionType.HarvestForest,
        FiniteEarthActionType.Reforest,
        FiniteEarthActionType.Farm,
        FiniteEarthActionType.Irrigate,
        FiniteEarthActionType.Mine,
        FiniteEarthActionType.Restore,
        FiniteEarthActionType.SpawnArmy
    };

    private void Awake()
    {
        ApplyThemePreset();
        EnsureFeedbackHooks(true);
        ResolveReferences();
        SuppressLegacyAsciiUi();
        BuildHud();
        BindEvents();
    }

    private void OnDestroy()
    {
        if (orchestrator != null)
        {
            orchestrator.ActionExecuted -= HandleActionExecuted;
            orchestrator.ResourcePopupRequested -= HandleResourcePopupRequested;
            orchestrator.ClimateEventTriggered -= HandleClimateEvent;
        }
    }

    private void Update()
    {
        RefreshResponsiveCanvasScale();
        ResolveReferences();
        SuppressLegacyAsciiUi();
        if (!eventsBound)
        {
            BindEvents();
        }

        if (hudRoot == null)
        {
            BuildHud();
        }

        RefreshTopBar();
        RefreshTileScanner();
        RefreshActionConsole();
        RefreshActionWheel();
        RefreshRightColumnPanels();
    }

    private void ResolveReferences()
    {
        if (orchestrator == null)
        {
            orchestrator = FindAnyObjectByType<FiniteEarthGameOrchestrator>();
        }

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

        if (overlayPainter != null && !overlayPainter.IsInitialized && worldGenerator != null && ownership != null && orchestrator != null)
        {
            overlayPainter.Initialize(worldGenerator, ownership, orchestrator);
        }
    }

    private void BindEvents()
    {
        if (orchestrator == null)
        {
            return;
        }

        orchestrator.ActionExecuted -= HandleActionExecuted;
        orchestrator.ActionExecuted += HandleActionExecuted;
        orchestrator.ResourcePopupRequested -= HandleResourcePopupRequested;
        orchestrator.ResourcePopupRequested += HandleResourcePopupRequested;
        orchestrator.ClimateEventTriggered -= HandleClimateEvent;
        orchestrator.ClimateEventTriggered += HandleClimateEvent;
        eventsBound = true;
    }

    private void BuildHud()
    {
        if (hudRoot != null)
        {
            return;
        }

        EnsureCanvas();
        fontAsset = EnsureFontAsset();
        if (fontAsset == null)
        {
            if (!hasLoggedFontResolutionError)
            {
                Debug.LogError("CommandTableHudPresenter: no TMP font asset available. Import TMP Essential Resources or add a TMP font asset under Resources/Fonts.");
                hasLoggedFontResolutionError = true;
            }
            return;
        }

        solidSprite = EnsureSolidSprite();
        hexSprite = BuildHexSprite(64);

        hudRoot = new GameObject("CommandTableHUDRoot", typeof(RectTransform)).GetComponent<RectTransform>();
        hudRoot.SetParent(hudCanvas.transform, false);
        hudRoot.anchorMin = Vector2.zero;
        hudRoot.anchorMax = Vector2.one;
        hudRoot.pivot = new Vector2(0.5f, 0.5f);
        hudRoot.sizeDelta = Vector2.zero;
        hudRoot.SetAsLastSibling();

        BuildTopBar();
        BuildTooltip();
        BuildTileScanner();
        BuildActionConsole();
        BuildEventFeedAndMinimap();
        BuildActionWheel();
        BuildResourcePopups();
        BuildClimateModal();
        EnsureHoverController();

        PlayFeedback(panelRevealFeedback);
    }

    private void EnsureCanvas()
    {
        GameObject existingObject = GameObject.Find(HudCanvasObjectName);
        if (existingObject != null)
        {
            hudCanvas = existingObject.GetComponent<Canvas>();
            EnsureCanvasComponents(existingObject);
            return;
        }

        GameObject canvasObject = new GameObject(HudCanvasObjectName);
        hudCanvas = canvasObject.AddComponent<Canvas>();
        EnsureCanvasComponents(canvasObject);
    }

    private void EnsureCanvasComponents(GameObject canvasObject)
    {
        if (canvasObject == null)
        {
            return;
        }

        if (hudCanvas == null)
        {
            hudCanvas = canvasObject.GetComponent<Canvas>();
            if (hudCanvas == null)
            {
                hudCanvas = canvasObject.AddComponent<Canvas>();
            }
        }

        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.pixelPerfect = true;
        hudCanvas.overrideSorting = true;
        hudCanvas.sortingOrder = canvasSortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvasObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = ResolveCanvasScreenMatch();

        if (canvasObject.GetComponent<GraphicRaycaster>() == null)
        {
            canvasObject.AddComponent<GraphicRaycaster>();
        }
    }

    private void RefreshResponsiveCanvasScale()
    {
        if (hudCanvas == null)
        {
            return;
        }

        CanvasScaler scaler = hudCanvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            return;
        }

        scaler.matchWidthOrHeight = ResolveCanvasScreenMatch();
    }

    private float ResolveCanvasScreenMatch()
    {
        float width = Screen.width > 0 ? Screen.width : referenceResolution.x;
        float height = Screen.height > 0 ? Screen.height : referenceResolution.y;

        if (width <= height)
        {
            return 0f;
        }

        float aspect = width / Mathf.Max(1f, height);
        if (aspect < 1.45f)
        {
            return 0.2f;
        }

        return 0.5f;
    }

    private TMP_FontAsset EnsureFontAsset()
    {
        if (fontAsset != null)
        {
            return fontAsset;
        }

        Font sourceFont = Resources.Load<Font>("Fonts/VT323-Regular");
        if (sourceFont != null)
        {
            try
            {
                fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont, 48, 8, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
                if (fontAsset != null)
                {
                    fontAsset.name = "VT323-Regular Runtime";
                    if (fontAsset.atlasTexture != null)
                    {
                        fontAsset.atlasTexture.filterMode = FilterMode.Point;
                    }
                }

                return fontAsset;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"CommandTableHudPresenter: failed to create VT323 TMP font asset. {ex.Message}");
            }
        }

        TMP_FontAsset resourceFont = Resources.Load<TMP_FontAsset>("Fonts/VT323-Regular SDF");
        if (IsUsableFontAsset(resourceFont))
        {
            fontAsset = resourceFont;
            return fontAsset;
        }

        resourceFont = Resources.Load<TMP_FontAsset>("Fonts/VT323-Regular");
        if (IsUsableFontAsset(resourceFont))
        {
            fontAsset = resourceFont;
            return fontAsset;
        }

#if UNITY_EDITOR
        EnsureTmpEssentialResources();
#endif

        TMP_FontAsset defaultFontAsset = null;
        try
        {
            defaultFontAsset = TMP_Settings.defaultFontAsset;
        }
        catch
        {
            defaultFontAsset = null;
        }

        if (defaultFontAsset != null)
        {
            fontAsset = defaultFontAsset;
            return fontAsset;
        }

        TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        if (loadedFonts != null && loadedFonts.Length > 0)
        {
            fontAsset = loadedFonts[0];
            return fontAsset;
        }

        return null;
    }

    private static bool IsUsableFontAsset(TMP_FontAsset asset)
    {
        if (asset == null)
        {
            return false;
        }

        try
        {
            return asset.atlasTextures != null
                && asset.atlasTextures.Length > 0
                && asset.atlasTextures[0] != null;
        }
        catch
        {
            return false;
        }
    }

#if UNITY_EDITOR
    private static void EnsureTmpEssentialResources()
    {
        string settingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        if (System.IO.File.Exists(settingsPath))
        {
            return;
        }

        TMP_PackageResourceImporter.ImportResources(true, false, false);
        AssetDatabase.Refresh();
    }
#endif

    private Sprite EnsureSolidSprite()
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    private void BuildTopBar()
    {
        RectTransform topBar = CreatePanel("TopBar", hudRoot,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-safeMargin * 2f, TopBarHeight), new Vector2(0f, -safeMargin));
        topBarRoot = topBar;

        planetHealthIconImage = CreateIcon(topBar, "PlanetHealthIcon", new Vector2(32f, 32f), Color.white);
        SetAnchor(planetHealthIconImage.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(32f, 32f), new Vector2(18f, 14f));
        SetOptionalIconSprite(planetHealthIconImage, FiniteEarthIconLibrary.GetPlanetHealthIcon());

        planetHealthLabel = CreateText(topBar, "HealthLabel", "PLANET HEALTH", 24, TextAlignmentOptions.Left, textColor);
        SetAnchor(planetHealthLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(320f, 26f), new Vector2(58f, 14f));

        RectTransform barBg = new GameObject("HealthBarBg", typeof(RectTransform)).GetComponent<RectTransform>();
        barBg.SetParent(topBar, false);
        SetAnchor(barBg, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(248f, 12f), new Vector2(58f, -12f));
        Image barBgImage = barBg.gameObject.AddComponent<Image>();
        barBgImage.sprite = solidSprite;
        barBgImage.color = new Color(0.12f, 0.16f, 0.20f, 0.8f);

        RectTransform barFill = new GameObject("HealthBarFill", typeof(RectTransform)).GetComponent<RectTransform>();
        barFill.SetParent(barBg, false);
        barFill.anchorMin = new Vector2(0f, 0f);
        barFill.anchorMax = new Vector2(1f, 1f);
        barFill.pivot = new Vector2(0f, 0.5f);
        barFill.sizeDelta = Vector2.zero;
        Image fillImage = barFill.gameObject.AddComponent<Image>();
        fillImage.sprite = solidSprite;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = 0;
        fillImage.fillAmount = 1f;
        fillImage.color = accentTeal;
        planetHealthFill = fillImage;

        planetHealthMetaText = CreateText(topBar, "HealthMetaText", "F +0% | C -0% | CL +0%", 15, TextAlignmentOptions.Left, mutedTextColor);
        SetAnchor(planetHealthMetaText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(360f, 18f), new Vector2(58f, -30f));

        forestText = CreateText(topBar, "ForestText", "FOREST --", 24, TextAlignmentOptions.Left, textColor);
        SetAnchor(forestText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(190f, 26f), new Vector2(332f, 2f));

        carbonText = CreateText(topBar, "CarbonText", "CARBON --", 24, TextAlignmentOptions.Left, textColor);
        SetAnchor(carbonText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(210f, 26f), new Vector2(548f, 2f));

        Image woodIcon = CreateIcon(topBar, "WoodIcon", new Vector2(28f, 28f), Color.white);
        SetAnchor(woodIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 28f), new Vector2(726f, 2f));
        SetOptionalIconSprite(woodIcon, FiniteEarthIconLibrary.GetWoodIcon());

        woodText = CreateText(topBar, "WoodText", "WOOD --", 24, TextAlignmentOptions.Left, textColor);
        SetAnchor(woodText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(178f, 26f), new Vector2(760f, 2f));

        woodMetaText = CreateText(topBar, "WoodMetaText", "[0/MIN] +0%", 15, TextAlignmentOptions.Left, mutedTextColor);
        SetAnchor(woodMetaText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(178f, 18f), new Vector2(760f, -24f));

        Image oreIcon = CreateIcon(topBar, "OreIcon", new Vector2(28f, 28f), Color.white);
        SetAnchor(oreIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 28f), new Vector2(944f, 2f));
        SetOptionalIconSprite(oreIcon, FiniteEarthIconLibrary.GetOreIcon());

        mineralsText = CreateText(topBar, "MineralsText", "MINERALS --", 24, TextAlignmentOptions.Left, textColor);
        SetAnchor(mineralsText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(214f, 26f), new Vector2(978f, 2f));

        mineralsMetaText = CreateText(topBar, "MineralsMetaText", "[0/MIN] +0%", 15, TextAlignmentOptions.Left, mutedTextColor);
        SetAnchor(mineralsMetaText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(214f, 18f), new Vector2(978f, -24f));

        Image foodIcon = CreateIcon(topBar, "FoodIcon", new Vector2(28f, 28f), Color.white);
        SetAnchor(foodIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 28f), new Vector2(1192f, 2f));
        SetOptionalIconSprite(foodIcon, FiniteEarthIconLibrary.GetFoodIcon());

        foodText = CreateText(topBar, "FoodText", "FOOD --", 24, TextAlignmentOptions.Left, textColor);
        SetAnchor(foodText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(188f, 26f), new Vector2(1226f, 2f));

        foodMetaText = CreateText(topBar, "FoodMetaText", "[0/MIN] +0%", 15, TextAlignmentOptions.Left, mutedTextColor);
        SetAnchor(foodMetaText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(188f, 18f), new Vector2(1226f, -24f));

        topBarRightCluster = new GameObject("TopBarRightCluster", typeof(RectTransform)).GetComponent<RectTransform>();
        topBarRightCluster.SetParent(topBar, false);
        SetAnchor(topBarRightCluster, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(320f, 64f), new Vector2(-12f, 0f));

        RectTransform climateRoot = new GameObject("ClimateStatus", typeof(RectTransform)).GetComponent<RectTransform>();
        climateRoot.SetParent(topBarRightCluster, false);
        SetAnchor(climateRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(116f, 30f), new Vector2(4f, -12f));

        HorizontalLayoutGroup climateLayout = climateRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        climateLayout.spacing = 4f;
        climateLayout.childAlignment = TextAnchor.MiddleLeft;
        climateLayout.childControlWidth = false;
        climateLayout.childControlHeight = false;
        climateLayout.childForceExpandWidth = false;
        climateLayout.childForceExpandHeight = false;

        climateStatusIcons.Clear();
        for (int i = 0; i < 5; i++)
        {
            Image climateIcon = CreateIcon(climateRoot, "ClimateIcon_" + i, new Vector2(24f, 24f), Color.white);
            LayoutElement layoutElement = climateIcon.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 24f;
            layoutElement.preferredHeight = 24f;
            climateIcon.enabled = false;
            climateStatusIcons.Add(climateIcon);
        }

        playerText = CreateText(topBarRightCluster, "PlayerText", "YOU", 22, TextAlignmentOptions.Right, textColor);
        SetAnchor(playerText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(224f, 22f), new Vector2(-4f, -12f));

        tickText = CreateText(topBarRightCluster, "TickText", "TICK --", 20, TextAlignmentOptions.Right, mutedTextColor);
        SetAnchor(tickText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(224f, 20f), new Vector2(-4f, -38f));
    }

    private void BuildTileScanner()
    {
        RectTransform panel = CreatePanel("TileScanner", hudRoot,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(320f, 210f), new Vector2(safeMargin, safeMargin));

        TMP_Text title = CreateText(panel, "Title", ":: HEX SCAN", 22, TextAlignmentOptions.Left, textColor);
        SetAnchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 24f), new Vector2(12f, -8f));

        Image terrainIcon = CreateIcon(panel, "TerrainIcon", new Vector2(18f, 18f), new Color(0.3f, 0.5f, 0.6f, 1f));
        SetAnchor(terrainIcon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, 18f), new Vector2(12f, -40f));

        TMP_Text terrainText = CreateText(panel, "Terrain", "Terrain: --", 18, TextAlignmentOptions.Left, textColor);
        SetAnchor(terrainText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 20f), new Vector2(40f, -40f));

        TMP_Text ownerText = CreateText(panel, "Owner", "Owner: --", 18, TextAlignmentOptions.Left, textColor);
        SetAnchor(ownerText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 20f), new Vector2(12f, -66f));

        Image buildingIcon = CreateIcon(panel, "BuildingIcon", new Vector2(18f, 18f), new Color(0.3f, 0.5f, 0.6f, 1f));
        SetAnchor(buildingIcon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, 18f), new Vector2(12f, -92f));

        TMP_Text buildingText = CreateText(panel, "Building", "Building: --", 18, TextAlignmentOptions.Left, textColor);
        SetAnchor(buildingText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 20f), new Vector2(40f, -92f));

        TMP_Text influenceText = CreateText(panel, "Influence", "Influence: --", 18, TextAlignmentOptions.Left, textColor);
        SetAnchor(influenceText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 20f), new Vector2(12f, -118f));

        TMP_Text yieldText = CreateText(panel, "Yield", "Yield: --", 18, TextAlignmentOptions.Left, textColor);
        SetAnchor(yieldText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 20f), new Vector2(12f, -144f));

        TMP_Text statusText = CreateText(panel, "Status", "Status: --", 16, TextAlignmentOptions.Left, mutedTextColor);
        SetAnchor(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 40f), new Vector2(12f, 20f));

        TMP_Text coordText = CreateText(panel, "Coord", "--", 16, TextAlignmentOptions.Right, mutedTextColor);
        SetAnchor(coordText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(120f, 20f), new Vector2(-12f, 8f));

        tileScanner = gameObject.AddComponent<TileScannerPresenter>();
        tileScanner.Initialize(title, terrainText, ownerText, buildingText, influenceText, yieldText, statusText, coordText, terrainIcon, buildingIcon);
    }

    private void BuildActionConsole()
    {
        RectTransform panel = CreatePanel("ActionConsole", hudRoot,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(400f, 212f), new Vector2(-safeMargin, safeMargin));
        panel.gameObject.AddComponent<RectMask2D>();

        TMP_Text title = CreateText(panel, "Title", ":: COMMAND CONSOLE", 22, TextAlignmentOptions.Left, textColor);
        SetAnchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 24f), new Vector2(12f, -8f));

        TMP_Text subtitle = CreateText(panel, "Subtitle", "[ SELECT A TILE ]", 18, TextAlignmentOptions.Left, mutedTextColor);
        SetAnchor(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 20f), new Vector2(12f, -34f));

        RectTransform listRoot = new GameObject("Actions", typeof(RectTransform)).GetComponent<RectTransform>();
        listRoot.SetParent(panel, false);
        SetAnchor(listRoot, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-40f, -92f), new Vector2(-4f, -16f));
        listRoot.gameObject.AddComponent<RectMask2D>();

        VerticalLayoutGroup layout = listRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 0, 0);
        layout.spacing = 6f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.childAlignment = TextAnchor.UpperLeft;

        actionConsole = gameObject.AddComponent<ActionConsolePresenter>();
        actionConsole.Initialize(title, subtitle, listRoot, tooltip);
        actionConsole.BuildButtons(fontAsset, null, consoleOrder.Length, HandleActionRequested);
    }

    private void BuildEventFeedAndMinimap()
    {
        const float fieldLogHeight = 172f;
        const float feedViewportHeight = 74f;

        RectTransform feedPanel = CreatePanel("EventFeed", hudRoot,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(RightColumnWidth, fieldLogHeight), new Vector2(-safeMargin, -(safeMargin + TopBarHeight + safeMargin)));
        eventFeedPanel = feedPanel;

        TMP_Text title = CreateText(feedPanel, "Title", ":: FIELD LOG", 20, TextAlignmentOptions.Left, textColor);
        SetAnchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 20f), new Vector2(10f, -8f));

        fieldLogClimateText = CreateText(feedPanel, "ClimateStatus", ">> ACTIVE CLIMATE: CLEAR", 13, TextAlignmentOptions.TopLeft, mutedTextColor);
        fieldLogClimateText.textWrappingMode = TextWrappingModes.Normal;
        fieldLogClimateText.overflowMode = TextOverflowModes.Overflow;
        SetAnchor(fieldLogClimateText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(-20f, 42f), new Vector2(10f, -30f));

        RectTransform listRoot = new GameObject("FeedList", typeof(RectTransform)).GetComponent<RectTransform>();
        listRoot.SetParent(feedPanel, false);
        SetAnchor(listRoot, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(-20f, feedViewportHeight), new Vector2(0f, 10f));
        listRoot.gameObject.AddComponent<RectMask2D>();

        VerticalLayoutGroup layout = listRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 4f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.childAlignment = TextAnchor.UpperCenter;

        notificationFeed = gameObject.AddComponent<NotificationFeedPresenter>();
        notificationFeed.Initialize(listRoot, fontAsset, textColor, panelColor);

        RectTransform minimap = CreatePanel("MiniMap", hudRoot,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(RightColumnWidth, 72f), new Vector2(-safeMargin, -(safeMargin + TopBarHeight + safeMargin + fieldLogHeight + safeMargin)));
        miniMapPanel = minimap;

        TMP_Text mapTitle = CreateText(minimap, "Title", ":: WORLD NODE", 18, TextAlignmentOptions.Left, textColor);
        SetAnchor(mapTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 20f), new Vector2(10f, -8f));

        TMP_Text mapLabel = CreateText(minimap, "Label", "M: MARKET + TECH", 14, TextAlignmentOptions.Center, mutedTextColor);
        mapLabel.textWrappingMode = TextWrappingModes.Normal;
        SetAnchor(mapLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-20f, -28f), new Vector2(0f, -4f));
    }

    private void BuildTooltip()
    {
        RectTransform tooltipRoot = new GameObject("Tooltip", typeof(RectTransform)).GetComponent<RectTransform>();
        tooltipRoot.SetParent(hudRoot, false);
        tooltipRoot.sizeDelta = new Vector2(240f, 48f);

        Image bg = tooltipRoot.gameObject.AddComponent<Image>();
        bg.color = panelColor;
        bg.sprite = solidSprite;
        bg.raycastTarget = false;

        CanvasGroup group = tooltipRoot.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        TMP_Text label = CreateText(tooltipRoot, "TooltipText", string.Empty, 16, TextAlignmentOptions.MidlineLeft, textColor);
        SetAnchor(label.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-12f, -8f), Vector2.zero);

        tooltip = gameObject.AddComponent<TooltipPresenter>();
        tooltip.Initialize(tooltipRoot, label, group, hudRoot);
    }

    private void BuildActionWheel()
    {
        actionWheel = gameObject.AddComponent<ActionWheelPresenter>();
        actionWheel.Initialize(hudRoot, fontAsset, hexSprite, tooltip, HandleActionRequested);
    }

    private void BuildResourcePopups()
    {
        resourcePopups = gameObject.AddComponent<ResourcePopupPresenter>();
        resourcePopups.Initialize(hudRoot, fontAsset, worldGenerator, Camera.main);
    }

    private void BuildClimateModal()
    {
        climateModalRoot = new GameObject("ClimateModal", typeof(RectTransform)).GetComponent<RectTransform>();
        climateModalRoot.SetParent(hudRoot, false);
        climateModalRoot.anchorMin = Vector2.zero;
        climateModalRoot.anchorMax = Vector2.one;
        climateModalRoot.pivot = new Vector2(0.5f, 0.5f);
        climateModalRoot.sizeDelta = Vector2.zero;
        climateModalRoot.anchoredPosition = Vector2.zero;

        Image scrim = climateModalRoot.gameObject.AddComponent<Image>();
        scrim.sprite = solidSprite;
        scrim.color = new Color(0f, 0f, 0f, 0.58f);
        scrim.raycastTarget = true;

        climateModalGroup = climateModalRoot.gameObject.AddComponent<CanvasGroup>();

        RectTransform dialog = CreatePanel("Dialog", climateModalRoot,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(560f, 252f), Vector2.zero);

        climateModalTitle = CreateText(dialog, "Title", ":: CLIMATE ALERT", 24, TextAlignmentOptions.Left, textColor);
        SetAnchor(climateModalTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(-56f, 28f), new Vector2(20f, -14f));

        climateModalIcon = CreateIcon(dialog, "Icon", new Vector2(28f, 28f), Color.white);
        SetAnchor(climateModalIcon.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(28f, 28f), new Vector2(-20f, -18f));

        climateModalBody = CreateText(dialog, "Body", string.Empty, 18, TextAlignmentOptions.TopLeft, textColor);
        climateModalBody.textWrappingMode = TextWrappingModes.Normal;
        climateModalBody.overflowMode = TextOverflowModes.Overflow;
        SetAnchor(climateModalBody.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-40f, -108f), new Vector2(0f, -8f));

        climateModalOkButton = CreateToggleButton(dialog, "[ OK ]");
        RectTransform buttonRect = climateModalOkButton.GetComponent<RectTransform>();
        SetAnchor(buttonRect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(108f, 28f), new Vector2(-18f, 16f));
        climateModalOkButton.onClick.RemoveAllListeners();
        climateModalOkButton.onClick.AddListener(HideClimateModal);

        TMP_Text buttonText = climateModalOkButton.GetComponentInChildren<TMP_Text>();
        if (buttonText != null)
        {
            buttonText.color = textColor;
        }

        SetClimateModalVisible(false);
    }

    private void BuildOverlayPainter()
    {
        overlayPainter = gameObject.AddComponent<HexOverlayPainter>();
        overlayPainter.Initialize(worldGenerator, ownership, orchestrator);
    }

    private void EnsureHoverController()
    {
        HexHoverController hover = gameObject.GetComponent<HexHoverController>();
        if (hover != null)
        {
            Destroy(hover);
        }

        if (worldGenerator != null && worldGenerator.RuntimeGrid != null)
        {
            Transform hoverLayer = worldGenerator.RuntimeGrid.transform.Find("Hover");
            if (hoverLayer != null)
            {
                Destroy(hoverLayer.gameObject);
            }
        }
    }

    private void RefreshTopBar()
    {
        if (viewModel == null || viewModel.WorldState == null || viewModel.PlayerState == null)
        {
            return;
        }

        WorldState world = viewModel.WorldState;
        PlayerState player = viewModel.PlayerState;
        RefreshResourceRateSummary();

        if (planetHealthLabel != null)
        {
            planetHealthLabel.text = $"PLANET HEALTH {world.ecosystemScore}%";
        }

        if (planetHealthFill != null)
        {
            planetHealthFill.fillAmount = Mathf.Clamp01(world.ecosystemScore / 100f);
            planetHealthFill.color = world.ecosystemScore >= 70
                ? accentTeal
                : (world.ecosystemScore >= 40 ? accentAmber : new Color(0.92f, 0.26f, 0.24f, 1f));
        }

        if (planetHealthMetaText != null)
        {
            float forestRatio = world.initialForest <= 0 ? 0f : Mathf.Clamp01(world.globalForestToken / Mathf.Max(1f, world.initialForest));
            float carbonRatio = world.carbonCap <= 0 ? 0f : Mathf.Clamp01(world.globalCarbonToken / Mathf.Max(1f, world.carbonCap));
            int forestBoost = Mathf.RoundToInt(65f * forestRatio);
            int carbonPenalty = Mathf.RoundToInt(35f * carbonRatio);
            int clusterBonus = orchestrator != null && orchestrator.HasForestClusterBonusActive
                ? Mathf.Max(0, orchestrator.ForestClusterEcosystemBonus)
                : 0;

            string posColor = ColorUtility.ToHtmlStringRGB(PositiveDeltaColor);
            string negColor = ColorUtility.ToHtmlStringRGB(NegativeDeltaColor);
            string muteColor = ColorUtility.ToHtmlStringRGB(mutedTextColor);
            planetHealthMetaText.text =
                $"F <color=#{posColor}>+{forestBoost}%</color>  |  " +
                $"C <color=#{negColor}>-{carbonPenalty}%</color>  |  " +
                $"CL <color=#{(clusterBonus > 0 ? posColor : muteColor)}>+{clusterBonus}%</color>";
        }

        if (forestText != null)
        {
            forestText.text = $"FOREST {world.globalForestToken}";
        }

        if (carbonText != null)
        {
            carbonText.text = $"CARBON {world.globalCarbonToken}";
        }

        if (woodText != null)
        {
            woodText.text = BuildResourceLine("WOOD", player.resources.wood);
        }

        if (mineralsText != null)
        {
            mineralsText.text = BuildResourceLine("MINERALS", player.resources.minerals, cachedRateSnapshot.mineralsPerMinute);
        }

        if (foodText != null)
        {
            foodText.text = BuildResourceLine("FOOD", player.resources.food, cachedRateSnapshot.foodPerMinute);
        }

        if (playerText != null)
        {
            playerText.text = $"YOU | {player.reputationLabel.ToUpperInvariant()}";
        }

        if (tickText != null && orchestrator != null)
        {
            tickText.text = $"TICK {world.tick} | {orchestrator.CycleRemainingSeconds:0}s";
        }

        RefreshClimateStatusIcons();
    }

    private void RefreshClimateStatusIcons()
    {
        if (climateStatusIcons.Count == 0)
        {
            return;
        }

        ClimateEventInstance[] activeEvents = orchestrator != null ? orchestrator.GetActiveClimateEvents() : Array.Empty<ClimateEventInstance>();
        int visibleCount = Mathf.Min(activeEvents.Length, 3);
        for (int i = 0; i < climateStatusIcons.Count; i++)
        {
            Image icon = climateStatusIcons[i];
            if (icon == null)
            {
                continue;
            }

            if (i < visibleCount)
            {
                SetOptionalIconSprite(icon, FiniteEarthIconLibrary.GetClimateIcon(activeEvents[i].type));
                icon.color = Color.white;
            }
            else
            {
                icon.enabled = false;
            }
        }
    }

    private void RefreshTileScanner()
    {
        if (tileScanner == null || orchestrator == null || worldGenerator == null || ownership == null)
        {
            return;
        }

        tileScanner.Refresh(orchestrator.HasSelection, orchestrator.SelectedCoord, worldGenerator, ownership, orchestrator);
    }

    private void RefreshActionConsole()
    {
        if (actionConsole == null || orchestrator == null || worldGenerator == null)
        {
            return;
        }

        string title = "NO SELECTION";
        if (orchestrator.TryGetSelectedArmy(out ArmyUnit selectedArmy, out float cooldownRemaining))
        {
            string status = orchestrator.DescribeSelectedArmyStatus(cooldownRemaining);
            title = $"ARMY {selectedArmy.strength}/{orchestrator.MaxArmyStrength} | {status}";
            actionConsole.Refresh(title, true, Array.Empty<ActionAvailability>(), Array.Empty<FiniteEarthActionType>());
            return;
        }

        if (orchestrator.HasSelection)
        {
            worldGenerator.TryGetTileType(orchestrator.SelectedCoord.ToVector3Int(), out TileType terrain);
            worldGenerator.TryGetBuildingType(orchestrator.SelectedCoord.ToVector3Int(), out BuildingType building);
            if (building != BuildingType.None)
            {
                title = building.GetDisplayName();
                if (building == BuildingType.Barracks)
                {
                    int capacity = Mathf.Max(1, orchestrator.OwnedBarracksCount);
                    title = $"{title.ToUpperInvariant()} | CAP {orchestrator.OwnedArmyCount}/{capacity}";
                }
            }
            else
            {
                title = terrain.GetDisplayName().ToUpperInvariant();
            }
        }

        actionConsole.Refresh(title, orchestrator.HasSelection, orchestrator.LastActionStates, consoleOrder);
    }

    private void RefreshActionWheel()
    {
        if (actionWheel == null || orchestrator == null || worldGenerator == null || ownership == null)
        {
            return;
        }

        actionWheel.Refresh(orchestrator, worldGenerator, ownership);
    }

    private void RefreshRightColumnPanels()
    {
        if (eventFeedPanel != null && notificationFeed != null)
        {
            eventFeedPanel.gameObject.SetActive(true);
        }

        if (miniMapPanel != null && miniMapPanel.gameObject.activeSelf)
        {
            miniMapPanel.gameObject.SetActive(false);
        }

        if (fieldLogClimateText != null)
        {
            fieldLogClimateText.text = BuildFieldLogClimateStatus();
        }
    }

    private string BuildFieldLogClimateStatus()
    {
        if (orchestrator == null || viewModel?.WorldState == null)
        {
            return ">> ACTIVE CLIMATE: CLEAR";
        }

        ClimateEventInstance[] activeEvents = orchestrator.GetActiveClimateEvents();
        if (activeEvents == null || activeEvents.Length == 0)
        {
            return ">> ACTIVE CLIMATE: CLEAR";
        }

        Dictionary<ClimateEventType, int> durations = new Dictionary<ClimateEventType, int>();
        int tick = viewModel.WorldState.tick;
        for (int i = 0; i < activeEvents.Length; i++)
        {
            ClimateEventInstance evt = activeEvents[i];
            if (evt == null)
            {
                continue;
            }

            int remaining = Mathf.Max(0, evt.endTick - tick);
            if (remaining <= 0)
            {
                continue;
            }

            if (durations.TryGetValue(evt.type, out int current))
            {
                durations[evt.type] = Mathf.Max(current, remaining);
            }
            else
            {
                durations.Add(evt.type, remaining);
            }
        }

        if (durations.Count == 0)
        {
            return ">> ACTIVE CLIMATE: CLEAR";
        }

        List<string> lines = new List<string>(Mathf.Min(3, durations.Count));
        const int maxVisibleLines = 2;
        int hiddenCount = 0;
        foreach (ClimateEventType type in Enum.GetValues(typeof(ClimateEventType)))
        {
            if (!durations.TryGetValue(type, out int remaining))
            {
                continue;
            }

            if (lines.Count < maxVisibleLines)
            {
                lines.Add($">> {BuildClimateEventShortLabel(type)} {remaining}C LEFT");
            }
            else
            {
                hiddenCount++;
            }
        }

        if (hiddenCount > 0)
        {
            lines.Add($">> +{hiddenCount} MORE");
        }

        return string.Join("\n", lines);
    }

    private static string BuildClimateEventShortLabel(ClimateEventType type)
    {
        return type switch
        {
            ClimateEventType.Heatwave => "HEATWAVE",
            ClimateEventType.Wildfire => "WILDFIRE",
            ClimateEventType.Flood => "FLOOD",
            ClimateEventType.IceMelt => "ICE MELT",
            ClimateEventType.DesertSpread => "DESERT SPREAD",
            _ => "CLIMATE"
        };
    }

    private void HandleActionRequested(FiniteEarthActionType actionType)
    {
        if (orchestrator == null)
        {
            return;
        }

        PlayFeedback(actionClickFeedback);
        orchestrator.RequestAction(actionType);
    }

    private void HandleActionExecuted(FiniteEarthActionType actionType, int count)
    {
        if (notificationFeed == null)
        {
            return;
        }

        string message = actionType switch
        {
            FiniteEarthActionType.Claim => "Tile Captured",
            FiniteEarthActionType.BuildSettlement => "Settlement Built",
            FiniteEarthActionType.BuildBarracks => "Barracks Built",
            FiniteEarthActionType.BuildIndustry => "Industry Built",
            FiniteEarthActionType.HarvestForest => "Forest Harvested",
            FiniteEarthActionType.Reforest => "Reforesting Started",
            FiniteEarthActionType.Farm => "Farm Established",
            FiniteEarthActionType.Irrigate => "Irrigation Complete",
            FiniteEarthActionType.Mine => "Mine Opened",
            FiniteEarthActionType.Restore => "Restoration Started",
            FiniteEarthActionType.SpawnArmy => "Army Trained",
            _ => "Action Completed"
        };

        notificationFeed.Push(message, accentTeal);
        PlayFeedback(notificationFeedback);
        SpawnPing(actionPingPrefab);
    }

    private void HandleClimateEvent(ClimateEventType type)
    {
        string message = type switch
        {
            ClimateEventType.Heatwave => "Heatwave Warning",
            ClimateEventType.Wildfire => "Wildfire Started",
            ClimateEventType.Flood => "Flood Event",
            ClimateEventType.IceMelt => "Ice Melt",
            ClimateEventType.DesertSpread => "Desert Spread",
            _ => "Climate Event"
        };

        notificationFeed?.Push(message, accentAmber);
        PlayFeedback(notificationFeedback);
        SpawnPing(climatePingPrefab);
        pendingClimateModalQueue.Enqueue(type);
        AdvanceClimateModalQueue();
    }

    private void HandleResourcePopupRequested(HexCoord coord, FiniteEarthResourcePool delta)
    {
        if (resourcePopups == null)
        {
            return;
        }

        resourcePopups.Push(coord, delta);
    }

    private void RefreshResourceRateSummary()
    {
        if (orchestrator != null && (nextRateSnapshotAt <= 0f || Time.unscaledTime >= nextRateSnapshotAt))
        {
            cachedRateSnapshot = orchestrator.GetResourceRateSnapshot();
            nextRateSnapshotAt = Time.unscaledTime + 0.25f;
        }

        ApplyResourceMeta(woodMetaText, 0f, false);
        ApplyResourceMeta(mineralsMetaText, cachedRateSnapshot.mineralsModifierPercent);
        ApplyResourceMeta(foodMetaText, cachedRateSnapshot.foodModifierPercent);
    }

    private void ApplyResourceMeta(TMP_Text target, float modifierPercent, bool visible = true)
    {
        if (target == null)
        {
            return;
        }

        if (!visible)
        {
            target.text = string.Empty;
            return;
        }

        target.text = FormatModifierLine(modifierPercent);
        target.color = ResolveModifierColor(modifierPercent);
    }

    private void ApplyRateModifierMeta(TMP_Text target, int perMinute, float modifierPercent, bool visible = true)
    {
        if (target == null)
        {
            return;
        }

        if (!visible)
        {
            target.text = string.Empty;
            return;
        }

        string colorHex = ColorUtility.ToHtmlStringRGB(ResolveModifierColor(modifierPercent));
        target.color = mutedTextColor;
        target.text = $"[{perMinute}/MIN] <color=#{colorHex}>{FormatModifierLine(modifierPercent)}</color>";
    }

    private string BuildResourceLine(string label, int amount)
    {
        return $"{label} {amount}";
    }

    private string BuildResourceLine(string label, int amount, int perMinute)
    {
        return $"{label} {amount} [{perMinute}/MIN]";
    }

    private string FormatModifierLine(float modifierPercent)
    {
        int rounded = Mathf.RoundToInt(modifierPercent);
        if (Mathf.Abs(rounded) <= 0)
        {
            return "+0%";
        }

        return rounded > 0 ? $"+{rounded}%" : $"{rounded}%";
    }

    private Color ResolveModifierColor(float modifierPercent)
    {
        if (modifierPercent > 0.01f)
        {
            return PositiveDeltaColor;
        }

        if (modifierPercent < -0.01f)
        {
            return NegativeDeltaColor;
        }

        return mutedTextColor;
    }

    private void AdvanceClimateModalQueue()
    {
        if (climateModalVisible || climateModalGroup == null || pendingClimateModalQueue.Count == 0)
        {
            return;
        }

        ShowClimateModal(pendingClimateModalQueue.Dequeue());
    }

    private void ShowClimateModal(ClimateEventType type)
    {
        if (climateModalRoot == null)
        {
            return;
        }

        climateModalRoot.SetAsLastSibling();
        climateModalTitle.text = BuildClimateEventTitle(type);
        climateModalBody.text = BuildClimateEventBody(type);

        if (climateModalIcon != null)
        {
            SetOptionalIconSprite(climateModalIcon, FiniteEarthIconLibrary.GetClimateIcon(type));
            climateModalIcon.color = Color.white;
        }

        SetClimateModalVisible(true);
    }

    private void HideClimateModal()
    {
        SetClimateModalVisible(false);
        AdvanceClimateModalQueue();
    }

    private void SetClimateModalVisible(bool visible)
    {
        climateModalVisible = visible;
        if (climateModalGroup == null || climateModalRoot == null)
        {
            return;
        }

        climateModalGroup.alpha = visible ? 1f : 0f;
        climateModalGroup.interactable = visible;
        climateModalGroup.blocksRaycasts = visible;
        climateModalRoot.gameObject.SetActive(visible);
    }

    private string BuildClimateEventTitle(ClimateEventType type)
    {
        return type switch
        {
            ClimateEventType.Heatwave => ":: CLIMATE ALERT :: HEATWAVE",
            ClimateEventType.Wildfire => ":: CLIMATE ALERT :: WILDFIRE",
            ClimateEventType.Flood => ":: CLIMATE ALERT :: FLOOD",
            ClimateEventType.IceMelt => ":: CLIMATE ALERT :: ICE MELT",
            ClimateEventType.DesertSpread => ":: CLIMATE ALERT :: DESERT SPREAD",
            _ => ":: CLIMATE ALERT"
        };
    }

    private string BuildClimateEventBody(ClimateEventType type)
    {
        string detail = orchestrator != null
            ? orchestrator.DescribeClimateEvent(type)
            : "Planetary conditions shifted this cycle.";
        return detail + "\n\nRESOURCE MODIFIERS ARE SHOWN UNDER THE TOP-BAR TOTALS.";
    }

    private static void PlayFeedback(Component feedback)
    {
        if (feedback == null)
        {
            return;
        }

        feedback.SendMessage("PlayFeedbacks", SendMessageOptions.DontRequireReceiver);
    }

    private void SuppressLegacyAsciiUi()
    {
        AsciiTutorialPopupPresenter tutorial = FindAnyObjectByType<AsciiTutorialPopupPresenter>();
        if (tutorial != null && tutorial.enabled)
        {
            tutorial.enabled = false;
        }

        GameObject popupRoot = GameObject.Find("AsciiTutorialPopup");
        if (popupRoot != null && popupRoot.activeSelf)
        {
            popupRoot.SetActive(false);
        }
    }

    private void EnsureFeedbackHooks(bool allowCreate)
    {
        if (!autoSetupFeedbackHooks)
        {
            return;
        }

        panelRevealFeedback = ResolveFeedbackHook(panelRevealFeedback, PanelRevealFeedbackObjectName, allowCreate);
        actionClickFeedback = ResolveFeedbackHook(actionClickFeedback, ActionClickFeedbackObjectName, allowCreate);
        notificationFeedback = ResolveFeedbackHook(notificationFeedback, NotificationFeedbackObjectName, allowCreate);
    }

    private Component ResolveFeedbackHook(Component current, string childName, bool allowCreate)
    {
        if (current != null)
        {
            Component upgraded = FindFeedbackPlayer(current.gameObject);
            return upgraded != null ? upgraded : current;
        }

        Transform child = transform.Find(childName);
        if (child == null && allowCreate)
        {
            GameObject childObject = new GameObject(childName);
            child = childObject.transform;
            child.SetParent(transform, false);
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            MarkSceneDirtyIfNeeded();
        }

        if (child == null)
        {
            return null;
        }

        Component player = FindFeedbackPlayer(child.gameObject);
        if (player != null)
        {
            return player;
        }

        if (allowCreate)
        {
            player = TryAddFeedbackPlayer(child.gameObject);
        }

        return player != null ? player : child;
    }

    private static Component FindFeedbackPlayer(GameObject target)
    {
        if (target == null)
        {
            return null;
        }

        Type playerType = ResolveFeedbackPlayerType();
        return playerType == null ? null : target.GetComponent(playerType);
    }

    private static Component TryAddFeedbackPlayer(GameObject target)
    {
        if (target == null)
        {
            return null;
        }

        Type playerType = ResolveFeedbackPlayerType();
        if (playerType == null)
        {
            return null;
        }

        Component existing = target.GetComponent(playerType);
        if (existing != null)
        {
            return existing;
        }

        Component created = target.AddComponent(playerType);
        return created;
    }

    private static Type ResolveFeedbackPlayerType()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type playerType = assemblies[i].GetType(MmfPlayerTypeName, false);
            if (playerType != null)
            {
                return playerType;
            }
        }

        return null;
    }

    [ContextMenu("Setup Feedback Hooks")]
    private void SetupFeedbackHooksContextMenu()
    {
        EnsureFeedbackHooks(true);
        MarkSceneDirtyIfNeeded();
    }

    private void MarkSceneDirtyIfNeeded()
    {
#if UNITY_EDITOR
        if (gameObject != null)
        {
            EditorUtility.SetDirty(gameObject);
            if (gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
        }
#endif
    }

    private void SpawnPing(GameObject prefab)
    {
        if (prefab == null || hudRoot == null)
        {
            return;
        }

        GameObject ping = Instantiate(prefab, hudRoot);
        ping.transform.SetAsLastSibling();
    }

    private RectTransform CreatePanel(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 anchoredPosition)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;

        Image bg = rect.gameObject.AddComponent<Image>();
        bg.color = panelColor;
        bg.sprite = solidSprite;
        bg.raycastTarget = false;

        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        Shadow shadow = rect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(2f, -2f);
        shadow.useGraphicAlpha = true;

        RectTransform trim = new GameObject("Trim", typeof(RectTransform)).GetComponent<RectTransform>();
        trim.SetParent(rect, false);
        trim.anchorMin = new Vector2(0f, 1f);
        trim.anchorMax = new Vector2(1f, 1f);
        trim.pivot = new Vector2(0.5f, 1f);
        trim.sizeDelta = new Vector2(0f, 3f);
        trim.anchoredPosition = Vector2.zero;

        Image trimImage = trim.gameObject.AddComponent<Image>();
        trimImage.sprite = solidSprite;
        trimImage.color = new Color(borderColor.r, borderColor.g, borderColor.b, 0.85f);
        trimImage.raycastTarget = false;

        return rect;
    }

    private TMP_Text CreateText(Transform parent, string name, string text, int size, TextAlignmentOptions alignment, Color color)
    {
        TMP_Text label = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        RectTransform rect = label.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        label.font = fontAsset;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = color;
        label.text = text ?? string.Empty;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;

        Shadow shadow = label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(1f, -1f);
        shadow.useGraphicAlpha = true;
        return label;
    }

    private Image CreateIcon(Transform parent, string name, Vector2 size, Color color)
    {
        Image icon = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
        RectTransform rect = icon.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        icon.sprite = solidSprite;
        icon.color = color;
        icon.raycastTarget = false;
        return icon;
    }

    private static void SetOptionalIconSprite(Image icon, Sprite sprite)
    {
        if (icon == null)
        {
            return;
        }

        icon.sprite = sprite;
        icon.enabled = sprite != null;
        icon.preserveAspect = true;
    }

    private static void SetAnchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 anchoredPosition)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
    }

    private void ApplyThemePreset()
    {
        if (!enforceAsciiCommandTableTheme)
        {
            return;
        }

        panelColor = new Color(0.01f, 0.07f, 0.06f, 0.94f);
        borderColor = new Color(0.19f, 0.78f, 0.61f, 0.95f);
        textColor = new Color(0.91f, 0.98f, 0.94f, 1f);
        mutedTextColor = new Color(0.58f, 0.78f, 0.69f, 1f);
        accentTeal = new Color(0.24f, 0.90f, 0.64f, 1f);
        accentAmber = new Color(0.95f, 0.74f, 0.30f, 1f);
    }

    private Button CreateToggleButton(Transform parent, string label)
    {
        RectTransform root = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(parent, false);
        root.sizeDelta = new Vector2(54f, 22f);

        Image bg = root.gameObject.AddComponent<Image>();
        bg.sprite = solidSprite;
        bg.color = new Color(0.02f, 0.08f, 0.07f, 0.94f);

        Outline outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(borderColor.r, borderColor.g, borderColor.b, 0.72f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        Button button = root.gameObject.AddComponent<Button>();

        TMP_Text text = CreateText(root, "Label", label, 13, TextAlignmentOptions.Center, mutedTextColor);
        SetAnchor(text.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), Vector2.zero);

        return button;
    }

    private static Sprite BuildHexSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color white = new Color(1f, 1f, 1f, 1f);
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.45f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - center.x);
                float dy = Mathf.Abs(y - center.y);
                float a = Mathf.Atan2(dy, dx);
                float maxR = radius * Mathf.Cos(Mathf.PI / 6f) / Mathf.Cos(Mathf.Repeat(a, Mathf.PI / 3f) - Mathf.PI / 6f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, dist <= maxR ? white : clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}

public class ResourcePopupPresenter : MonoBehaviour
{
    private sealed class PopupEntry
    {
        public RectTransform root;
        public CanvasGroup group;
        public Vector2 basePosition;
        public float createdAt;
    }

    [SerializeField] private RectTransform canvasRoot;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;
    [SerializeField] private Camera worldCamera;
    [SerializeField, Min(0.1f)] private float duration = 1.0f;
    [SerializeField, Min(1f)] private float risePixels = 34f;

    private readonly List<PopupEntry> entries = new List<PopupEntry>();
    private readonly Color gainColor = new Color(0.45f, 0.95f, 0.66f, 1f);
    private readonly Color lossColor = new Color(0.98f, 0.46f, 0.46f, 1f);

    public void Initialize(RectTransform root, TMP_FontAsset fontAsset, HexWorldGeneratorTilemap generator, Camera cameraRef)
    {
        canvasRoot = root;
        font = fontAsset;
        worldGenerator = generator;
        worldCamera = cameraRef != null ? cameraRef : Camera.main;
    }

    public void Push(HexCoord coord, FiniteEarthResourcePool delta)
    {
        if (canvasRoot == null || font == null || worldGenerator == null || delta.IsZero())
        {
            return;
        }

        RectTransform popupRoot = new GameObject("ResourcePopup", typeof(RectTransform)).GetComponent<RectTransform>();
        popupRoot.SetParent(canvasRoot, false);
        popupRoot.sizeDelta = new Vector2(220f, 34f);

        CanvasGroup group = popupRoot.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 1f;

        TMP_Text label = popupRoot.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = 18;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        label.richText = true;
        label.text = BuildDeltaMarkup(delta);

        Shadow shadow = label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
        shadow.effectDistance = new Vector2(1f, -1f);
        shadow.useGraphicAlpha = true;

        Vector2 anchored = WorldToCanvasPoint(coord);
        popupRoot.anchoredPosition = anchored;

        entries.Add(new PopupEntry
        {
            root = popupRoot,
            group = group,
            basePosition = anchored,
            createdAt = Time.unscaledTime
        });
    }

    private void Update()
    {
        if (entries.Count == 0)
        {
            return;
        }

        float now = Time.unscaledTime;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            PopupEntry entry = entries[i];
            if (entry == null || entry.root == null || entry.group == null)
            {
                entries.RemoveAt(i);
                continue;
            }

            float normalized = Mathf.Clamp01((now - entry.createdAt) / duration);
            entry.root.anchoredPosition = entry.basePosition + new Vector2(0f, Mathf.Lerp(0f, risePixels, normalized));
            entry.group.alpha = 1f - normalized;
            if (normalized >= 1f)
            {
                Destroy(entry.root.gameObject);
                entries.RemoveAt(i);
            }
        }
    }

    private Vector2 WorldToCanvasPoint(HexCoord coord)
    {
        if (canvasRoot == null || worldGenerator == null)
        {
            return Vector2.zero;
        }

        if (worldCamera == null)
        {
            worldCamera = Camera.main;
        }

        Vector3 worldPos = worldGenerator.GetCellCenterWorld(coord.ToVector3Int());
        Vector3 screenPos = worldCamera != null ? worldCamera.WorldToScreenPoint(worldPos) : worldPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, screenPos, null, out Vector2 localPoint);
        return localPoint + new Vector2(0f, 24f);
    }

    private string BuildDeltaMarkup(FiniteEarthResourcePool delta)
    {
        var parts = new List<string>(3);
        AppendPart(parts, delta.wood, "WOOD");
        AppendPart(parts, delta.food, "FOOD");
        AppendPart(parts, delta.minerals, "MIN");
        return string.Join("  ", parts);
    }

    private void AppendPart(List<string> parts, int amount, string label)
    {
        if (amount == 0)
        {
            return;
        }

        Color color = amount > 0 ? gainColor : lossColor;
        string html = ColorUtility.ToHtmlStringRGB(color);
        string sign = amount > 0 ? "+" : string.Empty;
        parts.Add($"<color=#{html}>{sign}{amount} {label}</color>");
    }
}
