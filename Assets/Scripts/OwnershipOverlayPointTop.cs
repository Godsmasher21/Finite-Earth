using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

public class OwnershipOverlayPointTop : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Tilemap overlayTilemap;
    [FormerlySerializedAs("baseTile")]
    [SerializeField] private TileBase ownedTile;
    [SerializeField] private string ownedTileResourcePath = "Tiles/Tile_Owned";
    [SerializeField] private string ownedTileName = "Tile_Owned";
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;
    [SerializeField] private int ownershipSortingOrder = 5;
    [SerializeField] private int selectionSortingOrder = 30;

    [Header("Selection Layer")]
    [SerializeField] private Tilemap selectionTilemap;
    [SerializeField] private TileBase selectionTile;
    [SerializeField] private Color selectedTint = new Color(1.00f, 0.82f, 0.18f, 1.00f);
    [SerializeField] private float selectionPulseSpeed = 3.2f;

    [Header("Map Size (syncs from generator)")]
    [SerializeField] private int width = 24;
    [SerializeField] private int height = 15;

    [Header("Owned Visual")]
    [SerializeField] private bool useTerrainShapeForOwnedOverlay = true;
    [SerializeField] private Color ownedTint = new Color(0.04f, 0.62f, 0.96f, 0.26f);
    [SerializeField] private bool multiplyOwnedTileByTint = false;
    [SerializeField] private bool enforceHighContrastOwnedOverlay = true;
    [SerializeField] private Color highContrastOwnedTint = new Color(0.08f, 0.72f, 1.00f, 0.44f);
    [SerializeField] private bool preferDedicatedOwnedTileVisual = false;
    [SerializeField] private bool enforceReadableOverlayTheme = true;

    [Header("Starting Territory")]
    [SerializeField] private bool createStartingTerritory = true;
    [SerializeField] private bool useUniversalSpawnSlots = true;
    [SerializeField, Min(0)] private int spawnSlotIndex = 0;
    [SerializeField] private bool randomSpawn = true;
    [SerializeField] private Vector2Int fallbackStartPos = new Vector2Int(2, 2);
    [SerializeField] private int starterOwnedRadius = 0;
    [SerializeField] private bool placeStarterSettlement = true;

    private bool[,] owned;
    private readonly List<Vector3Int> selectedCells = new List<Vector3Int>();

    public bool HasSelection => selectedCells.Count > 0;
    public Vector3Int SelectedCell => selectedCells.Count > 0 ? selectedCells[0] : default;

    private void Awake()
    {
        if (worldGenerator == null)
        {
            worldGenerator = FindAnyObjectByType<HexWorldGeneratorTilemap>();
        }

        ApplyReadableOverlayTheme();
        Initialize(worldGenerator);
    }

    private void Update()
    {
        if (selectedCells.Count == 0 || selectionTilemap == null)
        {
            return;
        }

        float pulse = 0.82f + Mathf.Sin(Time.unscaledTime * selectionPulseSpeed) * 0.18f;
        Color color = GetAnimatedSelectionColor(pulse);
        for (int i = 0; i < selectedCells.Count; i++)
        {
            Vector3Int cell = selectedCells[i];
            selectionTilemap.SetColor(cell, color);
        }
    }

    public void Initialize(HexWorldGeneratorTilemap generator)
    {
        ApplyReadableOverlayTheme();
        worldGenerator = generator;
        EnsureOverlayLayers();
        EnsureOwnedTileReference();
        SyncBoundsFromGenerator();
        EnsureOwnedArray();
        RefreshOverlay();
        RefreshSelection();
    }

    public Vector3Int CreateStartingTerritory()
    {
        ClearOwned();

        Vector3Int spawnCell = new Vector3Int(fallbackStartPos.x, fallbackStartPos.y, 0);

        if (useUniversalSpawnSlots && worldGenerator != null && worldGenerator.TryGetSpawnCell(spawnSlotIndex, out Vector3Int universalSpawnCell))
        {
            spawnCell = universalSpawnCell;
        }
        else if (randomSpawn && worldGenerator != null)
        {
            worldGenerator.TryGetRandomSpawnCell(out spawnCell);
        }

        if (!InBounds(spawnCell))
        {
            spawnCell = new Vector3Int(
                Mathf.Clamp(spawnCell.x, 0, Mathf.Max(0, width - 1)),
                Mathf.Clamp(spawnCell.y, 0, Mathf.Max(0, height - 1)),
                0);
        }

        if (!createStartingTerritory)
        {
            return spawnCell;
        }

        SetOwned(spawnCell, true);

        if (starterOwnedRadius > 0 && worldGenerator != null)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector3Int cell = new Vector3Int(x, y, 0);

                    if (HexWorldGeneratorTilemap.HexDistance(spawnCell, cell) <= starterOwnedRadius
                        && worldGenerator.TryGetTileType(cell, out TileType terrainType)
                        && terrainType.IsClaimable())
                    {
                        SetOwned(cell, true);
                    }
                }
            }
        }

        if (placeStarterSettlement && worldGenerator != null)
        {
            if (worldGenerator.TryGetTileType(spawnCell, out TileType terrainType) && terrainType != TileType.Plains)
            {
                worldGenerator.TrySetTileType(spawnCell, TileType.Plains);
            }

            worldGenerator.TrySetBuildingType(spawnCell, BuildingType.Settlement);
        }

        SetSelectedCell(spawnCell);
        RefreshOverlay();
        return spawnCell;
    }

    public void SetOwned(Vector3Int cell, bool isOwned)
    {
        EnsureOwnedArray();

        if (!InBounds(cell))
        {
            return;
        }

        owned[cell.x, cell.y] = isOwned;
    }

    public bool IsOwned(Vector3Int cell)
    {
        EnsureOwnedArray();
        return InBounds(cell) && owned[cell.x, cell.y];
    }

    public bool HasAnyOwnedTiles()
    {
        EnsureOwnedArray();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (owned[x, y])
                {
                    return true;
                }
            }
        }

        return false;
    }

    public int GetOwnedCount()
    {
        int count = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (owned[x, y])
                {
                    count++;
                }
            }
        }

        return count;
    }

    public bool TryGetAnyOwnedCell(out Vector3Int cell)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!owned[x, y])
                {
                    continue;
                }

                cell = new Vector3Int(x, y, 0);
                return true;
            }
        }

        cell = default;
        return false;
    }

    public bool IsAdjacentToOwned(Vector3Int cell)
    {
        Vector3Int[] neighbors = HexWorldGeneratorTilemap.GetNeighborsPointTop(cell);

        for (int i = 0; i < neighbors.Length; i++)
        {
            Vector3Int neighbor = neighbors[i];

            if (InBounds(neighbor) && owned[neighbor.x, neighbor.y])
            {
                return true;
            }
        }

        return false;
    }

    public void SetSelectedCell(Vector3Int cell)
    {
        selectedCells.Clear();
        if (InBounds(cell))
        {
            selectedCells.Add(cell);
        }
        RefreshSelection();
    }

    public void SetSelectedCells(IReadOnlyList<Vector3Int> cells)
    {
        selectedCells.Clear();
        if (cells == null)
        {
            RefreshSelection();
            return;
        }

        var unique = new HashSet<long>();
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3Int cell = cells[i];
            if (!InBounds(cell))
            {
                continue;
            }

            long key = (((long)cell.x) << 32) ^ (uint)cell.y;
            if (unique.Add(key))
            {
                selectedCells.Add(cell);
            }
        }

        RefreshSelection();
    }

    public void ClearSelection()
    {
        selectedCells.Clear();
        RefreshSelection();
    }

    public void ResetOwnership()
    {
        ClearOwned();
        RefreshOverlay();
    }

    [ContextMenu("Refresh Overlay")]
    public void RefreshOverlay()
    {
        EnsureOverlayLayers();
        SyncBoundsFromGenerator();
        EnsureOwnedArray();

        if (overlayTilemap == null)
        {
            return;
        }

        overlayTilemap.ClearAllTiles();
        Color overlayColor = GetOwnedOverlayColor();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!owned[x, y])
                {
                    continue;
                }

                Vector3Int cell = new Vector3Int(x, y, 0);
                overlayTilemap.SetTile(cell, ResolveOwnedOverlayTile(cell));
                overlayTilemap.SetTileFlags(cell, TileFlags.None);
                overlayTilemap.SetColor(cell, overlayColor);
            }
        }

        overlayTilemap.RefreshAllTiles();
    }

    private void RefreshSelection()
    {
        EnsureOverlayLayers();

        if (selectionTilemap == null)
        {
            return;
        }

        selectionTilemap.ClearAllTiles();

        if (selectedCells.Count == 0)
        {
            return;
        }

        Color color = GetAnimatedSelectionColor(1f);
        for (int i = 0; i < selectedCells.Count; i++)
        {
            Vector3Int cell = selectedCells[i];
            if (!InBounds(cell))
            {
                continue;
            }

            selectionTilemap.SetTile(cell, ResolveSelectionTile(cell));
            selectionTilemap.SetTileFlags(cell, TileFlags.None);
            selectionTilemap.SetColor(cell, color);
        }
        selectionTilemap.RefreshAllTiles();
    }

    private void ClearOwned()
    {
        EnsureOwnedArray();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                owned[x, y] = false;
            }
        }
    }

    private void SyncBoundsFromGenerator()
    {
        if (worldGenerator == null || !worldGenerator.IsGenerated)
        {
            return;
        }

        width = Mathf.Max(1, worldGenerator.Width);
        height = Mathf.Max(1, worldGenerator.Height);
    }

    private void EnsureOwnedArray()
    {
        if (owned != null && owned.GetLength(0) == width && owned.GetLength(1) == height)
        {
            return;
        }

        var resized = new bool[Mathf.Max(1, width), Mathf.Max(1, height)];

        if (owned != null)
        {
            int copyWidth = Mathf.Min(width, owned.GetLength(0));
            int copyHeight = Mathf.Min(height, owned.GetLength(1));

            for (int y = 0; y < copyHeight; y++)
            {
                for (int x = 0; x < copyWidth; x++)
                {
                    resized[x, y] = owned[x, y];
                }
            }
        }

        owned = resized;
    }

    private bool InBounds(Vector3Int cell)
    {
        return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
    }

    private void EnsureOverlayLayers()
    {
        if (worldGenerator == null)
        {
            return;
        }

        if (!worldGenerator.IsGenerated)
        {
            worldGenerator.Generate();
        }

        if (!useTerrainShapeForOwnedOverlay)
        {
            EnsureOwnedTileReference();
        }

        if (overlayTilemap == null)
        {
            overlayTilemap = EnsureOverlayLayer("OwnershipOverlay", ownershipSortingOrder);
        }

        if (selectionTilemap == null)
        {
            selectionTilemap = EnsureOverlayLayer("SelectionOverlay", selectionSortingOrder);
        }

        if (selectionTile == null)
        {
            Tile runtimeSelectionTile = ScriptableObject.CreateInstance<Tile>();
            runtimeSelectionTile.sprite = BuildHexOutlineSprite(44, 4);
            runtimeSelectionTile.color = Color.white;
            selectionTile = runtimeSelectionTile;
        }
    }

    private Tilemap EnsureOverlayLayer(string layerName, int sortingOrder)
    {
        Transform existing = worldGenerator.RuntimeGrid != null
            ? worldGenerator.RuntimeGrid.transform.Find(layerName)
            : null;

        Tilemap createdTilemap = existing != null ? existing.GetComponent<Tilemap>() : null;

        if (createdTilemap == null)
        {
            var layerObject = new GameObject(layerName);
            layerObject.transform.SetParent(worldGenerator.RuntimeGrid.transform, false);
            createdTilemap = layerObject.AddComponent<Tilemap>();
        }

        createdTilemap.orientation = Tilemap.Orientation.XY;
        createdTilemap.tileAnchor = Vector3.zero;

        TilemapRenderer renderer = createdTilemap.GetComponent<TilemapRenderer>();

        if (renderer == null)
        {
            renderer = createdTilemap.gameObject.AddComponent<TilemapRenderer>();
        }

        renderer.sortingOrder = sortingOrder;
        renderer.sortOrder = TilemapRenderer.SortOrder.BottomLeft;
        renderer.mode = TilemapRenderer.Mode.Individual;
        return createdTilemap;
    }

    private TileBase ResolveOwnedOverlayTile(Vector3Int cell)
    {
        if (useTerrainShapeForOwnedOverlay && worldGenerator != null && worldGenerator.TerrainTilemap != null)
        {
            TileBase terrainTile = worldGenerator.TerrainTilemap.GetTile(cell);

            if (terrainTile != null)
            {
                return terrainTile;
            }
        }

        if (!useTerrainShapeForOwnedOverlay && ownedTile != null)
        {
            return ownedTile;
        }

        if (preferDedicatedOwnedTileVisual && ownedTile != null)
        {
            return ownedTile;
        }

        return worldGenerator != null ? worldGenerator.FallbackTile : null;
    }

    private TileBase ResolveSelectionTile(Vector3Int cell)
    {
        return selectionTile != null ? selectionTile : ResolveOwnedOverlayTile(cell);
    }

    private Color GetOwnedOverlayColor()
    {
        if (enforceHighContrastOwnedOverlay)
        {
            return highContrastOwnedTint;
        }

        if (useTerrainShapeForOwnedOverlay)
        {
            Color tintColor = ownedTint;
            tintColor.a = Mathf.Clamp01(tintColor.a);
            return tintColor;
        }

        if (ownedTile != null && !multiplyOwnedTileByTint)
        {
            return Color.white;
        }

        Color tint = ownedTint;
        tint.a = Mathf.Clamp01(tint.a);
        return tint;
    }

    private Color GetAnimatedSelectionColor(float pulse)
    {
        if (selectionTile != null)
        {
            return new Color(
                selectedTint.r,
                selectedTint.g,
                selectedTint.b,
                Mathf.Clamp01(selectedTint.a * pulse));
        }

        Color color = selectedTint;
        color.a *= pulse;
        return color;
    }

    private void EnsureOwnedTileReference()
    {
        if (ownedTile != null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(ownedTileResourcePath))
        {
            ownedTile = Resources.Load<TileBase>(ownedTileResourcePath);

            if (ownedTile != null)
            {
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(ownedTileName))
        {
            TileBase[] loadedTiles = Resources.FindObjectsOfTypeAll<TileBase>();

            for (int i = 0; i < loadedTiles.Length; i++)
            {
                TileBase candidate = loadedTiles[i];

                if (candidate != null && candidate.name == ownedTileName)
                {
                    ownedTile = candidate;
                    return;
                }
            }
        }

        if (worldGenerator != null && worldGenerator.FallbackTile != null)
        {
            ownedTile = worldGenerator.FallbackTile;
        }
    }

    private void ApplyReadableOverlayTheme()
    {
        if (!enforceReadableOverlayTheme)
        {
            return;
        }

        useTerrainShapeForOwnedOverlay = true;
        preferDedicatedOwnedTileVisual = false;
        enforceHighContrastOwnedOverlay = true;
        ownedTint = new Color(0.04f, 0.62f, 0.96f, 0.26f);
        highContrastOwnedTint = new Color(0.08f, 0.72f, 1.00f, 0.44f);
        selectedTint = new Color(1.00f, 0.82f, 0.18f, 1.00f);
    }

    private static Sprite BuildHexOutlineSprite(int size, int thickness)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color white = new Color(1f, 1f, 1f, 1f);
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float outerRadius = size * 0.47f;
        float innerRadius = outerRadius - Mathf.Max(1f, thickness);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - center.x);
                float dy = Mathf.Abs(y - center.y);
                float angle = Mathf.Atan2(dy, dx);
                float outerMax = outerRadius * Mathf.Cos(Mathf.PI / 6f) / Mathf.Cos(Mathf.Repeat(angle, Mathf.PI / 3f) - Mathf.PI / 6f);
                float innerMax = innerRadius * Mathf.Cos(Mathf.PI / 6f) / Mathf.Cos(Mathf.Repeat(angle, Mathf.PI / 3f) - Mathf.PI / 6f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                bool insideOuter = dist <= outerMax;
                bool insideInner = dist <= innerMax;
                tex.SetPixel(x, y, insideOuter && !insideInner ? white : clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
