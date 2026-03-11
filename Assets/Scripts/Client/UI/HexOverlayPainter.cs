using UnityEngine;
using UnityEngine.Tilemaps;

public class HexOverlayPainter : MonoBehaviour
{
    public enum OverlayMode
    {
        None,
        Influence,
        Resource,
        Ecosystem
    }

    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;
    [SerializeField] private OwnershipOverlayPointTop ownership;
    [SerializeField] private FiniteEarthGameOrchestrator orchestrator;
    [SerializeField] private string overlayTileResourcePath = "Tiles/Tile_Overlay";
    [SerializeField] private int influenceSortingOrder = 12;
    [SerializeField] private int resourceSortingOrder = 13;
    [SerializeField] private int ecosystemSortingOrder = 14;

    private Tile overlayTile;
    private Tilemap influenceLayer;
    private Tilemap resourceLayer;
    private Tilemap ecosystemLayer;
    private OverlayMode currentMode = OverlayMode.None;
    private float nextRefreshAt;
    private bool isInitialized;

    public bool IsInitialized => isInitialized;

    public void Initialize(HexWorldGeneratorTilemap generator, OwnershipOverlayPointTop ownershipOverlay, FiniteEarthGameOrchestrator gameOrchestrator)
    {
        if (generator == null || ownershipOverlay == null)
        {
            return;
        }

        worldGenerator = generator;
        ownership = ownershipOverlay;
        orchestrator = gameOrchestrator;
        EnsureLayers();
        UpdateLayerVisibility();
        RefreshOverlay(true);
        isInitialized = true;
    }

    public void SetMode(OverlayMode mode)
    {
        currentMode = mode;
        EnsureLayers();
        UpdateLayerVisibility();
        RefreshOverlay(true);
    }

    private void Update()
    {
        if (currentMode == OverlayMode.None)
        {
            return;
        }

        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }

        nextRefreshAt = Time.unscaledTime + 0.5f;
        RefreshOverlay(false);
    }

    private void RefreshOverlay(bool force)
    {
        if (worldGenerator == null || ownership == null)
        {
            return;
        }

        switch (currentMode)
        {
            case OverlayMode.Influence:
                PaintInfluence();
                break;
            case OverlayMode.Resource:
                PaintResources();
                break;
            case OverlayMode.Ecosystem:
                PaintEcosystem();
                break;
        }
    }

    private void PaintInfluence()
    {
        if (influenceLayer == null || overlayTile == null)
        {
            return;
        }

        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            bool owned = ownership.IsOwned(cell);
            int pressure = orchestrator != null ? orchestrator.GetCapturePressure(HexCoord.FromVector3Int(cell)) : 0;
            Color color = owned
                ? new Color(0.18f, 0.62f, 0.55f, 0.35f)
                : (pressure > 0 ? new Color(0.90f, 0.70f, 0.25f, 0.45f) : new Color(0f, 0f, 0f, 0f));

            if (color.a <= 0.01f)
            {
                influenceLayer.SetTile(cell, null);
                continue;
            }

            influenceLayer.SetTile(cell, overlayTile);
            influenceLayer.SetTileFlags(cell, TileFlags.None);
            influenceLayer.SetColor(cell, color);
        }
    }

    private void PaintResources()
    {
        if (resourceLayer == null || overlayTile == null)
        {
            return;
        }

        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            worldGenerator.TryGetTileType(cell, out TileType terrain);
            worldGenerator.TryGetBuildingType(cell, out BuildingType building);
            Color color = new Color(0f, 0f, 0f, 0f);

            if (terrain == TileType.Forest)
            {
                color = new Color(0.25f, 0.70f, 0.35f, 0.35f);
            }
            else if (terrain == TileType.Farmland)
            {
                color = new Color(0.75f, 0.70f, 0.30f, 0.35f);
            }
            else if (terrain == TileType.Mountain || terrain == TileType.Barren)
            {
                color = new Color(0.55f, 0.58f, 0.62f, 0.35f);
            }

            if (building == BuildingType.Industry)
            {
                color = new Color(0.55f, 0.60f, 0.70f, 0.45f);
            }

            if (color.a <= 0.01f)
            {
                resourceLayer.SetTile(cell, null);
                continue;
            }

            resourceLayer.SetTile(cell, overlayTile);
            resourceLayer.SetTileFlags(cell, TileFlags.None);
            resourceLayer.SetColor(cell, color);
        }
    }

    private void PaintEcosystem()
    {
        if (ecosystemLayer == null || overlayTile == null)
        {
            return;
        }

        foreach (Vector3Int cell in worldGenerator.EnumerateCells())
        {
            worldGenerator.TryGetTileType(cell, out TileType terrain);
            worldGenerator.TryGetBuildingType(cell, out BuildingType building);
            int carbon = terrain.GetCarbonValue() + building.GetCarbonModifier();
            float t = Mathf.Clamp01(carbon / 4f);
            Color color = Color.Lerp(new Color(0.20f, 0.70f, 0.45f, 0.35f), new Color(0.85f, 0.35f, 0.30f, 0.45f), t);

            ecosystemLayer.SetTile(cell, overlayTile);
            ecosystemLayer.SetTileFlags(cell, TileFlags.None);
            ecosystemLayer.SetColor(cell, color);
        }
    }

    private void EnsureLayers()
    {
        if (worldGenerator == null)
        {
            return;
        }

        Grid grid = worldGenerator.RuntimeGrid;
        if (grid == null)
        {
            return;
        }

        if (overlayTile == null)
        {
            overlayTile = Resources.Load<Tile>(overlayTileResourcePath);
            if (overlayTile == null)
            {
                overlayTile = ScriptableObject.CreateInstance<Tile>();
                overlayTile.sprite = BuildHexSprite(32);
                overlayTile.color = Color.white;
            }
        }

        influenceLayer = EnsureLayer(grid, "Overlay_Influence", influenceSortingOrder);
        resourceLayer = EnsureLayer(grid, "Overlay_Resource", resourceSortingOrder);
        ecosystemLayer = EnsureLayer(grid, "Overlay_Ecosystem", ecosystemSortingOrder);
    }

    private static Tilemap EnsureLayer(Grid grid, string name, int sortingOrder)
    {
        Transform existing = grid.transform.Find(name);
        Tilemap tilemap = existing != null ? existing.GetComponent<Tilemap>() : null;
        if (tilemap == null)
        {
            GameObject layer = new GameObject(name);
            layer.transform.SetParent(grid.transform, false);
            tilemap = layer.AddComponent<Tilemap>();
        }

        TilemapRenderer renderer = tilemap.GetComponent<TilemapRenderer>();
        if (renderer == null)
        {
            renderer = tilemap.gameObject.AddComponent<TilemapRenderer>();
        }

        renderer.sortingOrder = sortingOrder;
        renderer.sortOrder = TilemapRenderer.SortOrder.BottomLeft;
        renderer.mode = TilemapRenderer.Mode.Individual;
        tilemap.orientation = Tilemap.Orientation.XY;
        tilemap.tileAnchor = Vector3.zero;
        return tilemap;
    }

    private void UpdateLayerVisibility()
    {
        if (influenceLayer != null) influenceLayer.gameObject.SetActive(currentMode == OverlayMode.Influence);
        if (resourceLayer != null) resourceLayer.gameObject.SetActive(currentMode == OverlayMode.Resource);
        if (ecosystemLayer != null) ecosystemLayer.gameObject.SetActive(currentMode == OverlayMode.Ecosystem);
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
