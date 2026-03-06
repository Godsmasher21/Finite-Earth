using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AsciiHudPresenter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameStateViewModel viewModel;
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;
    [SerializeField] private OwnershipOverlayPointTop ownership;
    [SerializeField] private FiniteEarthGameOrchestrator orchestrator;

    [Header("Runtime HUD")]
    [SerializeField] private bool autoCreateRuntimeHud = true;
    [SerializeField] private Vector2 hudSize = new Vector2(900f, 144f);
    [SerializeField] private Vector2 hudOffset = new Vector2(18f, -18f);
    [SerializeField] private bool fitHeightToContent = true;
    [SerializeField] private float minHudHeight = 92f;
    [SerializeField] private float maxHudHeight = 180f;

    [Header("ASCII Theme")]
    [SerializeField] private Color panelColor = new Color(0.01f, 0.03f, 0.03f, 0.88f);
    [SerializeField] private Color borderColor = new Color(0.22f, 0.95f, 0.61f, 1f);
    [SerializeField] private Color textColor = new Color(0.90f, 0.98f, 0.94f, 1f);
    [SerializeField] private int hudFontSize = 16;
    [SerializeField] private float resourceDeltaDuration = 0.85f;
    [SerializeField] private float resourceDeltaRisePixels = 12f;
    [SerializeField] private Color resourceGainColor = new Color(0.35f, 0.95f, 0.62f, 1f);
    [SerializeField] private Color resourceLossColor = new Color(1f, 0.45f, 0.45f, 1f);
    [SerializeField] private Color resourceMixedColor = new Color(1f, 0.82f, 0.45f, 1f);

    private Text hudText;
    private Text resourceDeltaText;
    private RectTransform hudRootRect;
    private RectTransform resourceDeltaRect;
    private Font runtimeFont;
    private float nextRefreshAt;
    private float lastAppliedHudHeight = -1f;
    private FiniteEarthResourcePool lastResources;
    private bool hasLastResources;
    private float resourceDeltaRemaining;
    private Color activeResourceDeltaColor;
    private readonly Vector2 resourceDeltaBaseOffset = new Vector2(10f, -58f);
    private const int HudInnerWidth = 84;

    private void Awake()
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

        ResolveHudReferencesIfPrewired();

        if (hudText == null && autoCreateRuntimeHud)
        {
            CreateRuntimeHud();
        }

        EnsureResourceDeltaText();
        InitializeLastResourceSnapshot();
    }

    private void OnEnable()
    {
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
        if (orchestrator == null)
        {
            orchestrator = FindAnyObjectByType<FiniteEarthGameOrchestrator>();
        }

        TickResourceDeltaAnimation();

        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }

        nextRefreshAt = Time.unscaledTime + 0.25f;
        RefreshHud();
    }

    private void HandleResolutionApplied(ActionResolution _)
    {
        RefreshHud();
    }

    private void CreateRuntimeHud()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
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
        }

        GameObject hudRoot = new GameObject("AsciiTopHud");
        hudRoot.transform.SetParent(canvas.transform, false);

        RectTransform hudRect = hudRoot.AddComponent<RectTransform>();
        hudRootRect = hudRect;
        hudRect.anchorMin = new Vector2(0f, 1f);
        hudRect.anchorMax = new Vector2(0f, 1f);
        hudRect.pivot = new Vector2(0f, 1f);
        hudRect.anchoredPosition = hudOffset;
        hudRect.sizeDelta = hudSize;

        Image bg = hudRoot.AddComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = false;

        Outline outline = hudRoot.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        runtimeFont = ResolveRuntimeFont();

        GameObject textObject = new GameObject("HudText");
        textObject.transform.SetParent(hudRoot.transform, false);
        hudText = textObject.AddComponent<Text>();
        hudText.font = runtimeFont;
        hudText.fontSize = Mathf.Clamp(hudFontSize, 12, 28);
        hudText.supportRichText = false;
        hudText.color = textColor;
        hudText.alignment = TextAnchor.UpperLeft;
        hudText.horizontalOverflow = HorizontalWrapMode.Overflow;
        hudText.verticalOverflow = VerticalWrapMode.Truncate;
        hudText.raycastTarget = false;
        hudText.lineSpacing = 0.95f;

        RectTransform textRect = hudText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 8f);
        textRect.offsetMax = new Vector2(-10f, -8f);

        EnsureResourceDeltaText();
        RefreshHud();
    }

    private void RefreshHud()
    {
        if (hudText == null)
        {
            return;
        }

        if (hudRootRect == null)
        {
            ResolveHudReferencesIfPrewired();
        }

        int forest = worldGenerator != null ? worldGenerator.CountTilesOfType(TileType.Forest) : 0;
        int carbon = worldGenerator != null ? worldGenerator.CalculateCarbonScore() : 0;
        int owned = ownership != null ? ownership.GetOwnedCount() : 0;
        int tick = viewModel != null && viewModel.WorldState != null ? viewModel.WorldState.tick : 0;
        FiniteEarthResourcePool resources = viewModel != null && viewModel.PlayerState != null
            ? viewModel.PlayerState.resources
            : default;
        TrackResourceDelta(resources);
        string wallet = viewModel != null && viewModel.PlayerState != null
            ? ShortWallet(viewModel.PlayerState.walletAddress)
            : "local";
        float cycleLeft = orchestrator != null ? orchestrator.CycleRemainingSeconds : 0f;
        string cycleText = FormatTimer(cycleLeft);
        string modeText = orchestrator != null && orchestrator.UsesLocalCycleClock ? "LOCAL" : "SERVER";

        string border = "+" + new string('-', HudInnerWidth) + "+";
        string line2 = BuildHudLine($"FOREST {forest,5} | CARBON {carbon,6} | OWNED {owned,4} | TICK {tick,5}");
        string line3 = BuildHudLine($"WOOD {resources.wood,4} | FOOD {resources.food,4} | ORE {resources.minerals,4}");
        string line4 = BuildHudLine($"CYCLE {cycleText} ({modeText}) | WALLET {wallet}");

        hudText.text = $"{border}\n{line2}\n{line3}\n{line4}\n{border}";
        AdjustHudHeightToContent();
    }

    private void ResolveHudReferencesIfPrewired()
    {
        if (hudText == null)
        {
            hudText = GetComponentInChildren<Text>(true);
        }

        if (hudRootRect != null || hudText == null)
        {
            return;
        }

        if (hudText.transform.parent is RectTransform parentRect)
        {
            hudRootRect = parentRect;
            if (resourceDeltaText == null)
            {
                Text[] texts = parentRect.GetComponentsInChildren<Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] == null || texts[i] == hudText)
                    {
                        continue;
                    }

                    if (string.Equals(texts[i].name, "ResourceDeltaText", StringComparison.OrdinalIgnoreCase))
                    {
                        resourceDeltaText = texts[i];
                        resourceDeltaRect = texts[i].rectTransform;
                        break;
                    }
                }
            }

            return;
        }

        RectTransform ownRect = GetComponent<RectTransform>();
        if (ownRect != null)
        {
            hudRootRect = ownRect;
        }
    }

    private Font ResolveRuntimeFont()
    {
        Font dynamic = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "JetBrains Mono", "Courier New", "Lucida Console" }, 18);
        if (dynamic != null)
        {
            return dynamic;
        }

        Font builtIn = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (builtIn != null)
        {
            return builtIn;
        }

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
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

    private static string BuildHudLine(string body)
    {
        string text = body ?? string.Empty;
        if (text.Length > HudInnerWidth)
        {
            text = text.Substring(0, HudInnerWidth);
        }

        return "|" + text.PadRight(HudInnerWidth) + "|";
    }

    private void EnsureResourceDeltaText()
    {
        if (resourceDeltaText != null)
        {
            return;
        }

        if (hudRootRect == null)
        {
            ResolveHudReferencesIfPrewired();
        }

        if (hudRootRect == null)
        {
            return;
        }

        if (runtimeFont == null)
        {
            runtimeFont = ResolveRuntimeFont();
        }

        GameObject deltaObject = new GameObject("ResourceDeltaText");
        deltaObject.transform.SetParent(hudRootRect, false);

        resourceDeltaText = deltaObject.AddComponent<Text>();
        resourceDeltaText.font = runtimeFont;
        resourceDeltaText.fontSize = Mathf.Clamp(hudFontSize - 1, 11, 26);
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
        resourceDeltaRect.sizeDelta = new Vector2(Mathf.Max(220f, hudSize.x - 24f), 24f);
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
            parts.Add($"{(woodDelta > 0 ? "+" : string.Empty)}{woodDelta}🪵");
        }

        if (foodDelta != 0)
        {
            parts.Add($"{(foodDelta > 0 ? "+" : string.Empty)}{foodDelta}🍞");
        }

        if (oreDelta != 0)
        {
            parts.Add($"{(oreDelta > 0 ? "+" : string.Empty)}{oreDelta}⛏️");
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

    private void AdjustHudHeightToContent()
    {
        if (!fitHeightToContent || hudText == null || hudRootRect == null)
        {
            return;
        }

        float desiredHeight = Mathf.Clamp(hudText.preferredHeight + 20f, minHudHeight, maxHudHeight);
        if (Mathf.Abs(lastAppliedHudHeight - desiredHeight) < 0.5f)
        {
            return;
        }

        hudRootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, desiredHeight);
        LayoutElement layout = hudRootRect.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.minHeight = desiredHeight;
            layout.preferredHeight = desiredHeight;
            layout.flexibleHeight = 0f;
        }

        lastAppliedHudHeight = desiredHeight;
    }
}
