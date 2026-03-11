using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class ArmyOverlayPointTop : MonoBehaviour
{
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;
    [SerializeField] private string layerName = "Armies";
    [SerializeField] private int sortingOrder = 20;

    private Tilemap armyTilemap;
    private Tile armyTile;
    private readonly HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();

    private void Awake()
    {
        if (worldGenerator == null)
        {
            worldGenerator = FindAnyObjectByType<HexWorldGeneratorTilemap>();
        }

        EnsureArmyLayer();
    }

    public void RenderArmies(IReadOnlyList<ArmyUnit> armies, System.Func<string, Color> resolveColor)
    {
        if (armyTilemap == null)
        {
            EnsureArmyLayer();
        }

        if (armyTilemap == null || armies == null)
        {
            return;
        }

        foreach (Vector3Int cell in occupiedCells)
        {
            armyTilemap.SetTile(cell, null);
        }
        occupiedCells.Clear();

        for (int i = 0; i < armies.Count; i++)
        {
            ArmyUnit unit = armies[i];
            Vector3Int cell = unit.coord.ToVector3Int();
            if (!worldGenerator.HasTile(cell))
            {
                continue;
            }

            armyTilemap.SetTile(cell, armyTile);
            armyTilemap.SetColor(cell, resolveColor != null ? resolveColor(unit.ownerWallet) : Color.white);
            occupiedCells.Add(cell);
        }
    }

    private void EnsureArmyLayer()
    {
        if (worldGenerator == null)
        {
            return;
        }

        Grid runtimeGrid = worldGenerator.RuntimeGrid;
        if (runtimeGrid == null)
        {
            return;
        }

        Transform existing = runtimeGrid.transform.Find(layerName);
        if (existing != null)
        {
            armyTilemap = existing.GetComponent<Tilemap>();
        }

        if (armyTilemap == null)
        {
            var layerObject = new GameObject(layerName);
            layerObject.transform.SetParent(runtimeGrid.transform, false);
            armyTilemap = layerObject.AddComponent<Tilemap>();
        }

        armyTilemap.orientation = Tilemap.Orientation.XY;
        armyTilemap.tileAnchor = Vector3.zero;

        TilemapRenderer renderer = armyTilemap.GetComponent<TilemapRenderer>();
        if (renderer == null)
        {
            renderer = armyTilemap.gameObject.AddComponent<TilemapRenderer>();
        }
        renderer.sortingOrder = sortingOrder;
        renderer.sortOrder = TilemapRenderer.SortOrder.BottomLeft;
        renderer.mode = TilemapRenderer.Mode.Individual;

        if (armyTile == null)
        {
            armyTile = ScriptableObject.CreateInstance<Tile>();
            armyTile.sprite = BuildDotSprite();
            armyTile.color = Color.white;
        }
    }

    private static Sprite BuildDotSprite()
    {
        const int size = 6;
        Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color white = new Color(1f, 1f, 1f, 1f);

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.24f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center.x;
                float dy = y - center.y;
                float dist = Mathf.Sqrt((dx * dx) + (dy * dy));
                tex.SetPixel(x, y, dist <= radius ? white : clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
    }
}
