using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

public class HexHoverController : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;
    [SerializeField] private TooltipPresenter tooltip;
    [SerializeField] private bool showHoverTooltip = false;
    [SerializeField] private Color hoverColor = new Color(0.25f, 0.85f, 0.75f, 0.92f);
    [SerializeField] private int sortingOrder = 29;

    private Tilemap hoverTilemap;
    private Tile hoverTile;
    private Vector3Int lastCell;
    private bool hasHover;

    public void SetTooltip(TooltipPresenter tooltipPresenter)
    {
        tooltip = tooltipPresenter;
    }

    private void Awake()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (worldGenerator == null)
        {
            worldGenerator = FindAnyObjectByType<HexWorldGeneratorTilemap>();
        }

        EnsureHoverLayer();
    }

    private void Update()
    {
        if (worldGenerator == null || mainCamera == null)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            ClearHover();
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            ClearHover();
            return;
        }

        Vector2 screenPos = mouse.position.ReadValue();
        if (!worldGenerator.TryGetCellUnderScreenPoint(mainCamera, screenPos, out Vector3Int cell))
        {
            ClearHover();
            return;
        }

        if (!worldGenerator.HasTile(cell))
        {
            ClearHover();
            return;
        }

        if (!hasHover || cell != lastCell)
        {
            SetHover(cell);
        }

        UpdateTooltip(cell, screenPos);
    }

    private void SetHover(Vector3Int cell)
    {
        if (hoverTilemap == null || hoverTile == null)
        {
            return;
        }

        if (hasHover)
        {
            hoverTilemap.SetTile(lastCell, null);
        }

        hoverTilemap.SetTile(cell, hoverTile);
        hoverTilemap.SetTileFlags(cell, TileFlags.None);
        hoverTilemap.SetColor(cell, hoverColor);
        lastCell = cell;
        hasHover = true;
    }

    private void ClearHover()
    {
        if (!hasHover || hoverTilemap == null)
        {
            return;
        }

        hoverTilemap.SetTile(lastCell, null);
        hasHover = false;
        tooltip?.Hide();
    }

    private void UpdateTooltip(Vector3Int cell, Vector2 screenPos)
    {
        if (!showHoverTooltip || tooltip == null || worldGenerator == null)
        {
            return;
        }

        worldGenerator.TryGetTileType(cell, out TileType terrain);
        string text = $"{terrain.GetDisplayName()}  Q{cell.x} R{cell.y}";
        tooltip.Show(text, screenPos);
    }

    private void EnsureHoverLayer()
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

        Transform existing = grid.transform.Find("Hover");
        if (existing != null)
        {
            hoverTilemap = existing.GetComponent<Tilemap>();
        }

        if (hoverTilemap == null)
        {
            GameObject layer = new GameObject("Hover");
            layer.transform.SetParent(grid.transform, false);
            hoverTilemap = layer.AddComponent<Tilemap>();
        }

        TilemapRenderer renderer = hoverTilemap.GetComponent<TilemapRenderer>();
        if (renderer == null)
        {
            renderer = hoverTilemap.gameObject.AddComponent<TilemapRenderer>();
        }

        renderer.sortingOrder = sortingOrder;
        renderer.sortOrder = TilemapRenderer.SortOrder.BottomLeft;
        renderer.mode = TilemapRenderer.Mode.Individual;

        if (hoverTile == null)
        {
            hoverTile = ScriptableObject.CreateInstance<Tile>();
            hoverTile.sprite = BuildHexOutlineSprite(44, 3);
            hoverTile.color = Color.white;
        }
    }

    private static Sprite BuildHexOutlineSprite(int size, int thickness)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color white = new Color(1f, 1f, 1f, 1f);
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float outerRadius = size * 0.46f;
        float innerRadius = Mathf.Max(0f, outerRadius - Mathf.Max(1, thickness));

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - center.x);
                float dy = Mathf.Abs(y - center.y);
                float a = Mathf.Atan2(dy, dx);
                float angle = Mathf.Repeat(a, Mathf.PI / 3f) - Mathf.PI / 6f;
                float outer = outerRadius * Mathf.Cos(Mathf.PI / 6f) / Mathf.Cos(angle);
                float inner = innerRadius * Mathf.Cos(Mathf.PI / 6f) / Mathf.Cos(angle);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, dist <= outer && dist >= inner ? white : clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
