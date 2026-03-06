using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class HexWorldGeneratorTilemap : MonoBehaviour
{
    [Header("Terrain Layer")]
    [SerializeField] private Tilemap tilemap;
    [SerializeField] private TileBase baseTile;
    [SerializeField] private TileTypeColors typeColors;

    [Header("Universal World File")]
    [SerializeField] private TextAsset universalWorldFile;
    [SerializeField] private string universalWorldResourcePath = "Worlds/universal-world";

    [Header("Building Layer")]
    [SerializeField] private Tilemap buildingTilemap;
    [SerializeField, Min(1)] private int settlementRadius = 3;

    [Header("Runtime Grid")]
    [SerializeField] private Grid runtimeGrid;
    [SerializeField] private string gridObjectName = "RuntimeHexGrid";
    
    [Header("Tile Alignment")]
    [SerializeField] private Vector3 terrainTileAnchor = Vector3.zero;
    [SerializeField] private Vector3 buildingTileAnchor = Vector3.zero;
    [SerializeField] private Vector2 buildingWorldOffset = Vector2.zero;

    [Header("Selection Picking")]
    [SerializeField] private Vector2 selectionProbeOffset = new Vector2(0f, 0.22f);
    [SerializeField, Range(0.4f, 1.2f)] private float selectionRadiusMultiplier = 0.7f;

    [Header("Fixed Map (Top Row -> Bottom Row)")]
    [TextArea(8, 30)]
    [SerializeField] private string fixedMapLayout = "";

    [Header("Procedural World")]
    [SerializeField] private bool useProceduralDefaultWorld = true;
    [SerializeField, Min(24)] private int proceduralWidth = 176;
    [SerializeField, Min(16)] private int proceduralHeight = 116;
    [SerializeField] private int proceduralSeed = 14001;
    [SerializeField, Range(0f, 1f)] private float coastWaterBias = 0.54f;
    [SerializeField, Range(0f, 1f)] private float interiorVariation = 0.48f;
    [SerializeField, Range(0.04f, 0.20f)] private float targetMountainShare = 0.08f;
    [SerializeField, Range(0.08f, 0.35f)] private float targetForestShare = 0.22f;
    [SerializeField, Range(0.06f, 0.24f)] private float targetDesertShare = 0.12f;
    [SerializeField, Range(0.03f, 0.15f)] private float targetBarrenShare = 0.07f;
    [SerializeField, Range(0.02f, 0.12f)] private float targetDeforestedShare = 0.05f;
    [SerializeField, Min(2)] private int generatedSpawnCount = 10;

    private TileType[,] typeMap;
    private BuildingType[,] buildingMap;
    private Vector3Int[] generatedSpawnPoints = Array.Empty<Vector3Int>();
    private UniversalWorldFileData cachedWorldFileData;
    private bool isGenerated;

    public bool IsGenerated => isGenerated;
    public int Width => typeMap == null ? 0 : typeMap.GetLength(0);
    public int Height => typeMap == null ? 0 : typeMap.GetLength(1);
    public int SettlementRadius => settlementRadius;
    public Tilemap TerrainTilemap => tilemap;
    public Tilemap BuildingTilemap => buildingTilemap;
    public Grid RuntimeGrid => runtimeGrid;
    public TileBase FallbackTile => baseTile != null ? baseTile : (typeColors != null ? typeColors.defaultTile : null);
    public string WorldId => useProceduralDefaultWorld
        ? "finite-earth-procedural-large"
        : (GetUniversalWorldFileData()?.worldId ?? "finite-earth-local");

    private static readonly Vector3Int[] EvenRowNeighbors =
    {
        new Vector3Int(+1,  0, 0),
        new Vector3Int( 0, +1, 0),
        new Vector3Int(-1, +1, 0),
        new Vector3Int(-1,  0, 0),
        new Vector3Int(-1, -1, 0),
        new Vector3Int( 0, -1, 0),
    };

    private static readonly Vector3Int[] OddRowNeighbors =
    {
        new Vector3Int(+1,  0, 0),
        new Vector3Int(+1, +1, 0),
        new Vector3Int( 0, +1, 0),
        new Vector3Int(-1,  0, 0),
        new Vector3Int( 0, -1, 0),
        new Vector3Int(+1, -1, 0),
    };

    private void Awake()
    {
        EnsureRuntimeGridAndLayers();
    }

    private void Start()
    {
        Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        isGenerated = false;
        EnsureRuntimeGridAndLayers();

        TileType[,] parsedMap = null;
        if (useProceduralDefaultWorld)
        {
            parsedMap = GenerateProceduralMap();
        }
        else if (!TryParseFixedMap(out parsedMap))
        {
            Debug.LogError("HexWorldGeneratorTilemap: failed to parse map layout.");
            return;
        }

        if (parsedMap == null)
        {
            Debug.LogError("HexWorldGeneratorTilemap: map source returned null.");
            return;
        }

        typeMap = parsedMap;
        buildingMap = new BuildingType[Width, Height];
        generatedSpawnPoints = BuildProceduralSpawnPoints();

        tilemap.ClearAllTiles();
        buildingTilemap.ClearAllTiles();

        RepaintAllTerrain();
        RepaintAllBuildings();
        isGenerated = true;
    }

    public IEnumerable<Vector3Int> EnumerateCells()
    {
        if (typeMap == null)
        {
            yield break;
        }

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                yield return new Vector3Int(x, y, 0);
            }
        }
    }

    public bool HasTile(Vector3Int cell)
    {
        return typeMap != null && InBounds(cell);
    }

    public bool TryGetCellUnderScreenPoint(Camera camera, Vector2 screenPosition, out Vector3Int cell)
    {
        cell = default;

        if (camera == null || tilemap == null || typeMap == null)
        {
            return false;
        }

        float depth = Mathf.Abs(camera.transform.position.z - tilemap.transform.position.z);
        Vector3 world = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
        world.z = tilemap.transform.position.z;
        return TryGetNearestCellToWorldPoint(world, out cell);
    }

    public Vector3 GetCellCenterWorld(Vector3Int cell)
    {
        return tilemap == null ? Vector3.zero : tilemap.GetCellCenterWorld(cell);
    }

    public bool TryGetTileType(Vector3Int cell, out TileType type)
    {
        type = default;

        if (typeMap == null || !InBounds(cell))
        {
            return false;
        }

        type = typeMap[cell.x, cell.y];
        return true;
    }

    public bool TrySetTileType(Vector3Int cell, TileType newType)
    {
        if (typeMap == null || !InBounds(cell) || !newType.IsTerrain())
        {
            return false;
        }

        typeMap[cell.x, cell.y] = newType;
        PaintTerrainCell(cell, newType);
        tilemap.RefreshTile(cell);
        return true;
    }

    public bool TryGetBuildingType(Vector3Int cell, out BuildingType buildingType)
    {
        buildingType = BuildingType.None;

        if (buildingMap == null || !InBounds(cell))
        {
            return false;
        }

        buildingType = buildingMap[cell.x, cell.y];
        return true;
    }

    public bool HasBuilding(Vector3Int cell)
    {
        return TryGetBuildingType(cell, out BuildingType buildingType) && buildingType != BuildingType.None;
    }

    public bool HasAnySettlement()
    {
        if (buildingMap == null)
        {
            return false;
        }

        foreach (Vector3Int cell in EnumerateCells())
        {
            if (buildingMap[cell.x, cell.y] == BuildingType.Settlement)
            {
                return true;
            }
        }

        return false;
    }

    public bool TrySetBuildingType(Vector3Int cell, BuildingType buildingType)
    {
        if (buildingMap == null || !InBounds(cell))
        {
            return false;
        }

        buildingMap[cell.x, cell.y] = buildingType;
        PaintBuildingCell(cell, buildingType);
        buildingTilemap.RefreshTile(cell);
        return true;
    }

    public bool TryRemoveBuilding(Vector3Int cell)
    {
        return TrySetBuildingType(cell, BuildingType.None);
    }

    public bool TryGetRandomCellOfType(TileType terrainType, out Vector3Int cell)
    {
        cell = default;

        if (typeMap == null)
        {
            return false;
        }

        var candidates = new List<Vector3Int>();

        foreach (Vector3Int candidate in EnumerateCells())
        {
            if (typeMap[candidate.x, candidate.y] == terrainType)
            {
                candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        cell = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        return true;
    }

    public bool TryGetRandomSpawnCell(out Vector3Int cell)
    {
        cell = default;

        if (typeMap == null)
        {
            return false;
        }

        var candidates = new List<Vector3Int>();

        foreach (Vector3Int candidate in EnumerateCells())
        {
            if (typeMap[candidate.x, candidate.y] != TileType.Plains)
            {
                continue;
            }

            if (candidate.x < 2 || candidate.x >= Width - 2 || candidate.y < 2 || candidate.y >= Height - 2)
            {
                continue;
            }

            candidates.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            return TryGetRandomCellOfType(TileType.Plains, out cell);
        }

        cell = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        return true;
    }

    public bool TryGetSpawnCell(int slotIndex, out Vector3Int cell)
    {
        cell = default;

        if (generatedSpawnPoints != null && generatedSpawnPoints.Length > 0)
        {
            int index = Mathf.Abs(slotIndex) % generatedSpawnPoints.Length;

            for (int offset = 0; offset < generatedSpawnPoints.Length; offset++)
            {
                Vector3Int candidate = generatedSpawnPoints[(index + offset) % generatedSpawnPoints.Length];
                if (!InBounds(candidate))
                {
                    continue;
                }

                if (!typeMap[candidate.x, candidate.y].IsClaimable())
                {
                    continue;
                }

                cell = candidate;
                return true;
            }
        }

        UniversalWorldFileData worldFileData = GetUniversalWorldFileData();

        if (worldFileData != null && worldFileData.spawnPoints != null && worldFileData.spawnPoints.Length > 0)
        {
            int normalizedSlot = Mathf.Abs(slotIndex);

            for (int offset = 0; offset < worldFileData.spawnPoints.Length; offset++)
            {
                UniversalSpawnPoint spawnPoint = worldFileData.spawnPoints[(normalizedSlot + offset) % worldFileData.spawnPoints.Length];
                Vector3Int candidate = new Vector3Int(spawnPoint.x, spawnPoint.y, 0);

                if (!InBounds(candidate))
                {
                    continue;
                }

                if (!typeMap[candidate.x, candidate.y].IsClaimable())
                {
                    continue;
                }

                cell = candidate;
                return true;
            }
        }

        return TryGetRandomSpawnCell(out cell);
    }

    public bool HasAdjacentTerrainType(Vector3Int cell, TileType requiredType)
    {
        Vector3Int[] neighbors = GetNeighborsPointTop(cell);

        for (int i = 0; i < neighbors.Length; i++)
        {
            Vector3Int neighbor = neighbors[i];

            if (!InBounds(neighbor))
            {
                continue;
            }

            if (typeMap[neighbor.x, neighbor.y] == requiredType)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsWithinSettlementRadius(Vector3Int cell, int radius = -1)
    {
        if (buildingMap == null || !InBounds(cell))
        {
            return false;
        }

        int effectiveRadius = radius > 0 ? radius : settlementRadius;

        foreach (Vector3Int candidate in EnumerateCells())
        {
            if (buildingMap[candidate.x, candidate.y] != BuildingType.Settlement)
            {
                continue;
            }

            if (HexDistance(candidate, cell) <= effectiveRadius)
            {
                return true;
            }
        }

        return false;
    }

    public int CountTilesOfType(TileType type)
    {
        int count = 0;

        foreach (Vector3Int cell in EnumerateCells())
        {
            if (typeMap[cell.x, cell.y] == type)
            {
                count++;
            }
        }

        return count;
    }

    public int CalculateCarbonScore()
    {
        int score = 0;

        foreach (Vector3Int cell in EnumerateCells())
        {
            score += typeMap[cell.x, cell.y].GetCarbonValue();
            score += buildingMap[cell.x, cell.y].GetCarbonModifier();
        }

        return score;
    }

    public void FrameCamera(Camera targetCamera)
    {
        if (targetCamera == null || tilemap == null || !isGenerated)
        {
            return;
        }

        tilemap.CompressBounds();
        Bounds bounds = tilemap.localBounds;
        Vector3 center = tilemap.transform.TransformPoint(bounds.center);

        float paddedWidth = bounds.size.x + 2.5f;
        float paddedHeight = bounds.size.y + 2.5f;
        float sizeFromHeight = paddedHeight * 0.6f;
        float sizeFromWidth = paddedWidth / Mathf.Max(1f, targetCamera.aspect * 2f);

        targetCamera.transform.position = new Vector3(center.x, center.y, targetCamera.transform.position.z);
        targetCamera.orthographic = true;
        targetCamera.orthographicSize = Mathf.Max(4.5f, Mathf.Max(sizeFromHeight, sizeFromWidth));
    }

    public static Vector3Int[] GetNeighborsPointTop(Vector3Int cell)
    {
        bool oddRow = (cell.y & 1) == 1;
        Vector3Int[] deltas = oddRow ? OddRowNeighbors : EvenRowNeighbors;
        var neighbors = new Vector3Int[6];

        for (int i = 0; i < deltas.Length; i++)
        {
            neighbors[i] = cell + deltas[i];
        }

        return neighbors;
    }

    public static int HexDistance(Vector3Int a, Vector3Int b)
    {
        Vector3Int cubeA = OddrToCube(a);
        Vector3Int cubeB = OddrToCube(b);

        return Mathf.Max(
            Mathf.Abs(cubeA.x - cubeB.x),
            Mathf.Abs(cubeA.y - cubeB.y),
            Mathf.Abs(cubeA.z - cubeB.z));
    }

    private static Vector3Int OddrToCube(Vector3Int offset)
    {
        int q = offset.x - ((offset.y - (offset.y & 1)) / 2);
        int x = q;
        int z = offset.y;
        int y = -x - z;
        return new Vector3Int(x, y, z);
    }

    private bool InBounds(Vector3Int cell)
    {
        return typeMap != null
            && cell.x >= 0 && cell.x < Width
            && cell.y >= 0 && cell.y < Height;
    }

    private bool TryParseFixedMap(out TileType[,] parsedMap)
    {
        parsedMap = null;

        string source = ResolveMapLayoutSource();

        string[] rawLines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var lines = new List<string>();

        for (int i = 0; i < rawLines.Length; i++)
        {
            string trimmed = rawLines[i].Trim();

            if (trimmed.Length > 0)
            {
                lines.Add(trimmed);
            }
        }

        if (lines.Count == 0)
        {
            return false;
        }

        int mapHeight = lines.Count;
        int mapWidth = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            mapWidth = Mathf.Max(mapWidth, lines[i].Length);
        }

        parsedMap = new TileType[mapWidth, mapHeight];

        for (int row = 0; row < mapHeight; row++)
        {
            string line = lines[row];
            int y = mapHeight - 1 - row;

            for (int x = 0; x < mapWidth; x++)
            {
                char symbol = x < line.Length ? char.ToUpperInvariant(line[x]) : 'P';
                parsedMap[x, y] = CharToTerrainType(symbol);
            }
        }

        return true;
    }

    private static TileType CharToTerrainType(char symbol)
    {
        switch (symbol)
        {
            case 'F':
                return TileType.Forest;
            case 'P':
                return TileType.Plains;
            case 'M':
                return TileType.Mountain;
            case 'W':
                return TileType.Water;
            case 'E':
                return TileType.Desert;
            case 'B':
                return TileType.Barren;
            case 'D':
                return TileType.DeforestedForest;
            case 'A':
                return TileType.Farmland;
            default:
                return TileType.Plains;
        }
    }

    private string ResolveMapLayoutSource()
    {
        UniversalWorldFileData worldFileData = GetUniversalWorldFileData();

        if (worldFileData != null && worldFileData.rows != null && worldFileData.rows.Length > 0)
        {
            return string.Join("\n", worldFileData.rows);
        }

        return string.IsNullOrWhiteSpace(fixedMapLayout)
            ? GetDefaultMapLayout()
            : fixedMapLayout;
    }

    private UniversalWorldFileData GetUniversalWorldFileData()
    {
        if (cachedWorldFileData != null)
        {
            return cachedWorldFileData;
        }

        TextAsset sourceFile = universalWorldFile;

        if (sourceFile == null && !string.IsNullOrWhiteSpace(universalWorldResourcePath))
        {
            sourceFile = Resources.Load<TextAsset>(universalWorldResourcePath);
        }

        if (sourceFile == null || string.IsNullOrWhiteSpace(sourceFile.text))
        {
            return null;
        }

        cachedWorldFileData = JsonUtility.FromJson<UniversalWorldFileData>(sourceFile.text);

        if (cachedWorldFileData == null || cachedWorldFileData.rows == null || cachedWorldFileData.rows.Length == 0)
        {
            Debug.LogWarning("HexWorldGeneratorTilemap: universal world file is missing rows. Falling back to the local map layout.");
            cachedWorldFileData = null;
        }

        return cachedWorldFileData;
    }

    private static string GetDefaultMapLayout()
    {
        return
            "WWWWWWWWWWWWWWWWWWWWWWWW\n" +
            "WWWWWWWWWWWWWWWWWWWWWWWW\n" +
            "WWPPPPPFFFFFPPPPPPPPWWWW\n" +
            "WPPPPPFFFFFFPPPPPEPPPWWW\n" +
            "WPPPPFFFFFFFPPPPPEEPPWWW\n" +
            "WPPPPFFFMFFFPPPPPEEPPWWW\n" +
            "WPPPPPPMMMMPPPPPEEEPPWWW\n" +
            "WPPPPPPMMMPPPPPPEEEPPWWW\n" +
            "WPPPPPPPPPPPAAAPPEEEPPWW\n" +
            "WPPPPPPPPPPAAAAPPPEEPPWW\n" +
            "WPPBBPPPPPPAAAPPPPPPPPWW\n" +
            "WPPBBBPPPPPPPPPPPPPPPPWW\n" +
            "WWPPPPPDDPPPPPPPPPPPPWWW\n" +
            "WWWWPPPPPPPPPPPPPPWWWWWW\n" +
            "WWWWWWWWWWWWWWWWWWWWWWWW";
    }

    private void RepaintAllTerrain()
    {
        foreach (Vector3Int cell in EnumerateCells())
        {
            PaintTerrainCell(cell, typeMap[cell.x, cell.y]);
        }

        tilemap.RefreshAllTiles();
    }

    private void RepaintAllBuildings()
    {
        foreach (Vector3Int cell in EnumerateCells())
        {
            PaintBuildingCell(cell, buildingMap[cell.x, cell.y]);
        }

        buildingTilemap.RefreshAllTiles();
    }

    private void PaintTerrainCell(Vector3Int cell, TileType type)
    {
        TileBase resolvedTile = typeColors != null ? typeColors.GetTile(type, baseTile) : baseTile;
        tilemap.SetTile(cell, resolvedTile);
        tilemap.SetTileFlags(cell, TileFlags.None);
        tilemap.SetColor(cell, Color.white);
        tilemap.SetTransformMatrix(cell, Matrix4x4.identity);
    }

    private void PaintBuildingCell(Vector3Int cell, BuildingType buildingType)
    {
        if (buildingType == BuildingType.None)
        {
            buildingTilemap.SetTile(cell, null);
            buildingTilemap.SetTransformMatrix(cell, Matrix4x4.identity);
            return;
        }

        TileBase resolvedTile = typeColors != null ? typeColors.GetTile(buildingType.ToTileType(), null) : null;
        buildingTilemap.SetTile(cell, resolvedTile);
        buildingTilemap.SetTileFlags(cell, TileFlags.None);
        buildingTilemap.SetColor(cell, Color.white);
        Matrix4x4 matrix = Matrix4x4.TRS(
            new Vector3(buildingWorldOffset.x, buildingWorldOffset.y, 0f),
            Quaternion.identity,
            Vector3.one);
        buildingTilemap.SetTransformMatrix(cell, matrix);
    }

    private void EnsureRuntimeGridAndLayers()
    {
        if (runtimeGrid == null)
        {
            Transform existing = transform.Find(gridObjectName);

            if (existing != null)
            {
                runtimeGrid = existing.GetComponent<Grid>();
            }

            if (runtimeGrid == null)
            {
                var gridObject = new GameObject(gridObjectName);
                gridObject.transform.SetParent(transform, false);
                runtimeGrid = gridObject.AddComponent<Grid>();
            }
        }

        runtimeGrid.cellLayout = GridLayout.CellLayout.Hexagon;
        runtimeGrid.cellSwizzle = GridLayout.CellSwizzle.XYZ;
        runtimeGrid.cellSize = new Vector3(0.8659766f, 1f, 0f);

        if (tilemap == null)
        {
            tilemap = EnsureTilemapLayer("Terrain", 0);
        }

        if (buildingTilemap == null)
        {
            buildingTilemap = EnsureTilemapLayer("Buildings", 10);
        }

        tilemap.tileAnchor = terrainTileAnchor;
        buildingTilemap.tileAnchor = buildingTileAnchor;
    }

    private Tilemap EnsureTilemapLayer(string layerName, int sortingOrder)
    {
        Transform existing = runtimeGrid.transform.Find(layerName);
        Tilemap createdTilemap = existing != null ? existing.GetComponent<Tilemap>() : null;

        if (createdTilemap == null)
        {
            var layerObject = new GameObject(layerName);
            layerObject.transform.SetParent(runtimeGrid.transform, false);
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

    private bool TryGetNearestCellToWorldPoint(Vector3 worldPoint, out Vector3Int cell)
    {
        cell = default;

        if (tilemap == null || typeMap == null)
        {
            return false;
        }

        float bestDistanceSquared = float.PositiveInfinity;
        bool foundCell = false;

        foreach (Vector3Int candidate in EnumerateCells())
        {
            Vector3 center = GetSelectionProbeWorld(candidate);
            center.z = worldPoint.z;
            float distanceSquared = (center - worldPoint).sqrMagnitude;

            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            cell = candidate;
            foundCell = true;
        }

        if (!foundCell)
        {
            return false;
        }

        float maxSelectableDistance = GetMaxSelectableDistance();
        return bestDistanceSquared <= maxSelectableDistance * maxSelectableDistance;
    }

    private float GetMaxSelectableDistance()
    {
        Vector2 referenceSize = GetReferenceTileWorldSize();
        return Mathf.Max(referenceSize.x * 0.5f, referenceSize.y * 0.5f) * selectionRadiusMultiplier + 0.05f;
    }

    private Vector2 GetReferenceTileWorldSize()
    {
        if (FallbackTile is Tile tileAsset && tileAsset.sprite != null)
        {
            Vector2 spriteSize = tileAsset.sprite.bounds.size;

            if (spriteSize.x > 0.01f && spriteSize.y > 0.01f)
            {
                return spriteSize;
            }
        }

        if (runtimeGrid != null)
        {
            return new Vector2(
                Mathf.Max(runtimeGrid.cellSize.x, 0.01f),
                Mathf.Max(runtimeGrid.cellSize.y, 0.01f));
        }

        return Vector2.one;
    }

    private Vector3 GetSelectionProbeWorld(Vector3Int cell)
    {
        Vector3 center = tilemap.GetCellCenterWorld(cell);
        Vector2 referenceSize = GetReferenceTileWorldSize();
        center.x += selectionProbeOffset.x * referenceSize.x;
        center.y += selectionProbeOffset.y * referenceSize.y;
        return center;
    }

    private TileType[,] GenerateProceduralMap()
    {
        int width = Mathf.Max(24, proceduralWidth);
        int height = Mathf.Max(16, proceduralHeight);
        int total = width * height;

        int seed = proceduralSeed != 0 ? proceduralSeed : Environment.TickCount;
        UnityEngine.Random.State previousState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(seed);

        float noiseOffsetA = UnityEngine.Random.Range(-1000f, 1000f);
        float noiseOffsetB = UnityEngine.Random.Range(-1000f, 1000f);
        float noiseOffsetC = UnityEngine.Random.Range(-1000f, 1000f);
        float noiseOffsetD = UnityEngine.Random.Range(-1000f, 1000f);

        var map = new TileType[width, height];
        var landCells = new List<ProceduralCell>(total);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = width <= 1 ? 0f : x / (float)(width - 1);
                float ny = height <= 1 ? 0f : y / (float)(height - 1);
                float edgeDistance = Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(ny, 1f - ny));
                float coastal = Mathf.Clamp01((0.13f - edgeDistance) / 0.13f);

                float continentNoise = FractalNoise((x + noiseOffsetA) / 30f, (y + noiseOffsetB) / 30f, 4, 2f, 0.5f);
                float ridgeNoise = FractalNoise((x + noiseOffsetC) / 18f, (y + noiseOffsetD) / 18f, 3, 2f, 0.58f);
                float elevation = Mathf.Clamp01((continentNoise * 0.7f) + (ridgeNoise * 0.3f));

                float moistureNoise = FractalNoise((x + noiseOffsetB) / 22f, (y + noiseOffsetA) / 22f, 4, 2f, 0.52f);
                float rainBand = 1f - Mathf.Abs((ny * 2f) - 1f);
                float moisture = Mathf.Clamp01((moistureNoise * 0.72f) + (rainBand * 0.28f));

                float heatNoise = FractalNoise((x - noiseOffsetD) / 26f, (y - noiseOffsetC) / 26f, 3, 2f, 0.5f);
                float latitudeHeat = 1f - Mathf.Abs((ny * 2f) - 1f);
                float heat = Mathf.Clamp01((latitudeHeat * 0.65f) + (heatNoise * 0.35f));

                float rugged = FractalNoise((x + noiseOffsetD) / 9.5f, (y + noiseOffsetC) / 9.5f, 2, 2f, 0.5f);

                float waterScore =
                    (coastal * coastWaterBias * 0.68f) +
                    ((1f - elevation) * 0.24f) +
                    ((0.5f - ridgeNoise) * interiorVariation * 0.22f);

                bool forceWater = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                bool isWater = forceWater || waterScore >= 0.66f;

                int index = (y * width) + x;
                var cell = new ProceduralCell(index, x, y, elevation, moisture, heat, rugged);
                if (isWater)
                {
                    map[x, y] = TileType.Water;
                }
                else
                {
                    map[x, y] = TileType.Plains;
                    landCells.Add(cell);
                }
            }
        }

        int landCount = landCells.Count;
        if (landCount <= 0)
        {
            UnityEngine.Random.state = previousState;
            return map;
        }

        var assigned = new HashSet<int>();
        int mountainCount = Mathf.Clamp(Mathf.RoundToInt(landCount * targetMountainShare), 1, landCount / 4);
        int desertCount = Mathf.Clamp(Mathf.RoundToInt(landCount * targetDesertShare), 1, landCount / 3);
        int forestCount = Mathf.Clamp(Mathf.RoundToInt(landCount * targetForestShare), 2, landCount / 2);
        int barrenCount = Mathf.Clamp(Mathf.RoundToInt(landCount * targetBarrenShare), 1, landCount / 4);
        int deforestedCount = Mathf.Clamp(Mathf.RoundToInt(landCount * targetDeforestedShare), 1, landCount / 5);

        AssignBiomeTopN(
            landCells,
            assigned,
            map,
            mountainCount,
            c => (c.elevation * 0.72f) + (c.rugged * 0.28f),
            TileType.Mountain);

        AssignBiomeTopN(
            landCells,
            assigned,
            map,
            desertCount,
            c => (c.heat * 0.62f) + ((1f - c.moisture) * 0.30f) + ((1f - c.elevation) * 0.08f),
            TileType.Desert);

        AssignBiomeTopN(
            landCells,
            assigned,
            map,
            forestCount,
            c => (c.moisture * 0.72f) + ((1f - c.heat) * 0.18f) + ((1f - c.rugged) * 0.10f),
            TileType.Forest);

        AssignBiomeTopN(
            landCells,
            assigned,
            map,
            barrenCount,
            c => ((1f - c.moisture) * 0.54f) + (c.rugged * 0.28f) + (c.heat * 0.18f),
            TileType.Barren);

        AssignBiomeTopN(
            landCells,
            assigned,
            map,
            deforestedCount,
            c => (c.moisture * 0.30f) + ((1f - c.moisture) * 0.26f) + (c.heat * 0.24f) + (c.rugged * 0.20f),
            TileType.DeforestedForest);

        CarveHydrology(map, width, height, landCells, seed);
        SmoothBiomeTransitions(map, width, height, 2);

        Debug.Log(
            $"HexWorldGeneratorTilemap: generated procedural world {width}x{height} " +
            $"(land={landCount}, forest={CountType(map, TileType.Forest)}, mountain={CountType(map, TileType.Mountain)}, " +
            $"desert={CountType(map, TileType.Desert)}, barren={CountType(map, TileType.Barren)}, deforested={CountType(map, TileType.DeforestedForest)}).");

        UnityEngine.Random.state = previousState;
        return map;
    }

    private Vector3Int[] BuildProceduralSpawnPoints()
    {
        if (!useProceduralDefaultWorld || typeMap == null || Width <= 0 || Height <= 0)
        {
            return Array.Empty<Vector3Int>();
        }

        var plains = new List<Vector3Int>();

        for (int y = 2; y < Height - 2; y++)
        {
            for (int x = 2; x < Width - 2; x++)
            {
                TileType type = typeMap[x, y];
                if (type == TileType.Plains || type == TileType.Forest)
                {
                    plains.Add(new Vector3Int(x, y, 0));
                }
            }
        }

        if (plains.Count == 0)
        {
            return Array.Empty<Vector3Int>();
        }

        int spawnCount = Mathf.Clamp(generatedSpawnCount, 2, Mathf.Min(16, plains.Count));
        var selected = new List<Vector3Int>(spawnCount);
        var used = new HashSet<int>();

        float centerX = (Width - 1) * 0.5f;
        float centerY = (Height - 1) * 0.5f;
        float radiusX = Width * 0.34f;
        float radiusY = Height * 0.34f;

        for (int i = 0; i < spawnCount; i++)
        {
            float angle = (Mathf.PI * 2f * i) / spawnCount;
            Vector2 target = new Vector2(
                centerX + Mathf.Cos(angle) * radiusX,
                centerY + Mathf.Sin(angle) * radiusY);

            int bestIndex = -1;
            float bestDistance = float.PositiveInfinity;

            for (int p = 0; p < plains.Count; p++)
            {
                if (used.Contains(p))
                {
                    continue;
                }

                Vector3Int candidate = plains[p];
                float dx = candidate.x - target.x;
                float dy = candidate.y - target.y;
                float distance = (dx * dx) + (dy * dy);

                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestIndex = p;
            }

            if (bestIndex < 0)
            {
                break;
            }

            selected.Add(plains[bestIndex]);
            used.Add(bestIndex);
        }

        return selected.ToArray();
    }

    private static void CarveHydrology(
        TileType[,] map,
        int width,
        int height,
        List<ProceduralCell> landCells,
        int seed)
    {
        if (map == null || landCells == null || landCells.Count == 0 || width < 12 || height < 8)
        {
            return;
        }

        var rng = new System.Random(seed ^ 0x5F3759DF);
        var highlandCandidates = new List<ProceduralCell>();
        var basinCandidates = new List<ProceduralCell>();

        for (int i = 0; i < landCells.Count; i++)
        {
            ProceduralCell cell = landCells[i];
            TileType terrain = map[cell.x, cell.y];
            bool interior = IsInterior(cell.x, cell.y, width, height, 3);

            if (interior && (terrain == TileType.Mountain || cell.elevation >= 0.61f || cell.rugged >= 0.60f))
            {
                highlandCandidates.Add(cell);
            }

            if (IsInterior(cell.x, cell.y, width, height, 4)
                && terrain != TileType.Mountain
                && cell.elevation <= 0.60f
                && cell.moisture >= 0.42f)
            {
                basinCandidates.Add(cell);
            }
        }

        if (highlandCandidates.Count == 0)
        {
            highlandCandidates.AddRange(landCells);
        }

        if (basinCandidates.Count == 0)
        {
            basinCandidates.AddRange(landCells);
        }

        int riverCount = Mathf.Clamp(Mathf.RoundToInt((width * height) / 4200f), 2, 8);
        int lakeCount = Mathf.Clamp(Mathf.RoundToInt((width * height) / 7200f), 2, 6);
        int sourceSpacing = Mathf.Max(8, Mathf.RoundToInt(Mathf.Min(width, height) * 0.16f));
        int lakeSpacing = Mathf.Max(6, Mathf.RoundToInt(Mathf.Min(width, height) * 0.11f));

        var usedRiverSources = new List<Vector3Int>();
        int carvedRiverCount = 0;

        for (int i = 0; i < riverCount; i++)
        {
            if (!TryPickSpacedCell(highlandCandidates, usedRiverSources, sourceSpacing, rng, out Vector3Int source))
            {
                break;
            }

            Vector3Int mouth = PickRiverMouth(source, width, height, rng);
            if (CarveRiverPath(map, width, height, source, mouth, rng))
            {
                carvedRiverCount++;
            }
        }

        if (carvedRiverCount <= 1)
        {
            lakeCount++;
        }

        var usedLakeCenters = new List<Vector3Int>();
        for (int i = 0; i < lakeCount; i++)
        {
            if (!TryPickSpacedCell(basinCandidates, usedLakeCenters, lakeSpacing, rng, out Vector3Int center))
            {
                break;
            }

            int radiusRoll = rng.Next(100);
            int radius = radiusRoll < 18 ? 3 : (radiusRoll < 58 ? 2 : 1);
            CarveLake(map, width, height, center, radius, rng);
        }
    }

    private static bool CarveRiverPath(
        TileType[,] map,
        int width,
        int height,
        Vector3Int source,
        Vector3Int mouth,
        System.Random rng)
    {
        if (!InBounds(source.x, source.y, width, height))
        {
            return false;
        }

        Vector3Int current = source;
        var visited = new HashSet<int>();
        int maxSteps = Mathf.Max(width, height) * 3;
        int lakePulse = Mathf.Max(10, (width + height) / 20);

        for (int step = 0; step < maxSteps; step++)
        {
            if (!InBounds(current.x, current.y, width, height))
            {
                break;
            }

            int currentIndex = (current.y * width) + current.x;
            if (!visited.Add(currentIndex) && step > 4)
            {
                break;
            }

            CarveRiverCell(map, width, height, current, rng);

            if (HexDistance(current, mouth) <= 1 || (step > 8 && IsAdjacentToWater(map, width, height, current)))
            {
                CarveRiverCell(map, width, height, mouth, rng);
                return true;
            }

            if (step > 0 && (step % lakePulse) == 0 && IsInterior(current.x, current.y, width, height, 4))
            {
                int radius = rng.Next(100) < 28 ? 2 : 1;
                CarveLake(map, width, height, current, radius, rng);
            }

            Vector3Int next = SelectRiverStep(map, width, height, current, mouth, visited, rng);
            if (next == current)
            {
                break;
            }

            current = next;
        }

        return false;
    }

    private static Vector3Int SelectRiverStep(
        TileType[,] map,
        int width,
        int height,
        Vector3Int current,
        Vector3Int mouth,
        HashSet<int> visited,
        System.Random rng)
    {
        Vector3Int[] neighbors = GetNeighborsPointTop(current);
        Vector3Int best = current;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < neighbors.Length; i++)
        {
            Vector3Int candidate = neighbors[i];
            if (!InBounds(candidate.x, candidate.y, width, height))
            {
                continue;
            }

            int index = (candidate.y * width) + candidate.x;
            if (visited.Contains(index) && rng.NextDouble() > 0.16)
            {
                continue;
            }

            TileType terrain = map[candidate.x, candidate.y];
            float score = HexDistance(candidate, mouth) * 1.12f;
            score += DistanceToEdge(candidate.x, candidate.y, width, height) * 0.07f;
            score += GetRiverTerrainPenalty(terrain);
            score += (float)((rng.NextDouble() * 0.80) - 0.35);

            if (terrain == TileType.Water)
            {
                score -= 2.8f;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static void CarveRiverCell(
        TileType[,] map,
        int width,
        int height,
        Vector3Int cell,
        System.Random rng)
    {
        if (!InBounds(cell.x, cell.y, width, height))
        {
            return;
        }

        map[cell.x, cell.y] = TileType.Water;

        Vector3Int[] neighbors = GetNeighborsPointTop(cell);
        bool widened = false;

        for (int i = 0; i < neighbors.Length; i++)
        {
            Vector3Int n = neighbors[i];
            if (!InBounds(n.x, n.y, width, height))
            {
                continue;
            }

            TileType terrain = map[n.x, n.y];
            if (terrain == TileType.Water)
            {
                continue;
            }

            if (!widened && IsInterior(n.x, n.y, width, height, 2) && rng.NextDouble() < 0.08)
            {
                map[n.x, n.y] = TileType.Water;
                widened = true;
                continue;
            }

            if ((terrain == TileType.Desert || terrain == TileType.Barren) && rng.NextDouble() < 0.48)
            {
                map[n.x, n.y] = TileType.Plains;
                continue;
            }

            if (terrain == TileType.DeforestedForest && rng.NextDouble() < 0.22)
            {
                map[n.x, n.y] = TileType.Forest;
            }
        }
    }

    private static void CarveLake(
        TileType[,] map,
        int width,
        int height,
        Vector3Int center,
        int radius,
        System.Random rng)
    {
        int safeRadius = Mathf.Clamp(radius, 1, 3);
        if (!IsInterior(center.x, center.y, width, height, safeRadius + 2))
        {
            return;
        }

        int minX = Mathf.Max(1, center.x - (safeRadius + 1));
        int maxX = Mathf.Min(width - 2, center.x + (safeRadius + 1));
        int minY = Mathf.Max(1, center.y - (safeRadius + 1));
        int maxY = Mathf.Min(height - 2, center.y + (safeRadius + 1));

        Vector3Int centerCell = new Vector3Int(center.x, center.y, 0);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                int distance = HexDistance(centerCell, cell);

                if (distance <= safeRadius)
                {
                    map[x, y] = TileType.Water;
                }
                else if (distance == safeRadius + 1)
                {
                    TileType terrain = map[x, y];
                    if ((terrain == TileType.Desert || terrain == TileType.Barren) && rng.NextDouble() < 0.64)
                    {
                        map[x, y] = TileType.Plains;
                    }
                    else if (terrain == TileType.Plains && rng.NextDouble() < 0.26)
                    {
                        map[x, y] = TileType.Forest;
                    }
                }
            }
        }
    }

    private static Vector3Int PickRiverMouth(Vector3Int source, int width, int height, System.Random rng)
    {
        int left = source.x;
        int right = Mathf.Max(0, width - 1 - source.x);
        int bottom = source.y;
        int top = Mathf.Max(0, height - 1 - source.y);

        int minDistance = Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom));
        var options = new List<int>(4);
        if (left <= minDistance + 2) options.Add(0);
        if (right <= minDistance + 2) options.Add(1);
        if (top <= minDistance + 2) options.Add(2);
        if (bottom <= minDistance + 2) options.Add(3);

        if (options.Count == 0)
        {
            options.Add(rng.Next(4));
        }

        int side = options[rng.Next(options.Count)];
        switch (side)
        {
            case 0:
                return new Vector3Int(0, Mathf.Clamp(source.y + rng.Next(-height / 8, (height / 8) + 1), 0, height - 1), 0);
            case 1:
                return new Vector3Int(width - 1, Mathf.Clamp(source.y + rng.Next(-height / 8, (height / 8) + 1), 0, height - 1), 0);
            case 2:
                return new Vector3Int(Mathf.Clamp(source.x + rng.Next(-width / 8, (width / 8) + 1), 0, width - 1), height - 1, 0);
            default:
                return new Vector3Int(Mathf.Clamp(source.x + rng.Next(-width / 8, (width / 8) + 1), 0, width - 1), 0, 0);
        }
    }

    private static bool TryPickSpacedCell(
        List<ProceduralCell> candidates,
        List<Vector3Int> used,
        int minSpacing,
        System.Random rng,
        out Vector3Int selected)
    {
        selected = default;
        if (candidates == null || candidates.Count == 0)
        {
            return false;
        }

        int attempts = Mathf.Min(96, candidates.Count * 2);
        for (int i = 0; i < attempts; i++)
        {
            ProceduralCell sample = candidates[rng.Next(candidates.Count)];
            Vector3Int candidateCell = new Vector3Int(sample.x, sample.y, 0);
            if (GetNearestDistance(candidateCell, used) >= minSpacing)
            {
                used.Add(candidateCell);
                selected = candidateCell;
                return true;
            }
        }

        int bestDistance = int.MinValue;
        Vector3Int best = default;
        for (int i = 0; i < candidates.Count; i++)
        {
            ProceduralCell sample = candidates[i];
            Vector3Int candidateCell = new Vector3Int(sample.x, sample.y, 0);
            int distance = GetNearestDistance(candidateCell, used);

            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = candidateCell;
            }
        }

        if (bestDistance >= 0)
        {
            used.Add(best);
            selected = best;
            return true;
        }

        return false;
    }

    private static int GetNearestDistance(Vector3Int cell, List<Vector3Int> used)
    {
        if (used == null || used.Count == 0)
        {
            return int.MaxValue;
        }

        int nearest = int.MaxValue;
        for (int i = 0; i < used.Count; i++)
        {
            int distance = HexDistance(cell, used[i]);
            if (distance < nearest)
            {
                nearest = distance;
            }
        }

        return nearest;
    }

    private static bool IsAdjacentToWater(TileType[,] map, int width, int height, Vector3Int cell)
    {
        Vector3Int[] neighbors = GetNeighborsPointTop(cell);
        for (int i = 0; i < neighbors.Length; i++)
        {
            Vector3Int n = neighbors[i];
            if (!InBounds(n.x, n.y, width, height))
            {
                continue;
            }

            if (map[n.x, n.y] == TileType.Water)
            {
                return true;
            }
        }

        return false;
    }

    private static int DistanceToEdge(int x, int y, int width, int height)
    {
        int left = x;
        int right = Mathf.Max(0, width - 1 - x);
        int bottom = y;
        int top = Mathf.Max(0, height - 1 - y);
        return Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom));
    }

    private static float GetRiverTerrainPenalty(TileType terrain)
    {
        switch (terrain)
        {
            case TileType.Water:
                return -3.0f;
            case TileType.Plains:
                return 0.08f;
            case TileType.Forest:
                return 0.18f;
            case TileType.DeforestedForest:
                return 0.24f;
            case TileType.Farmland:
                return 0.33f;
            case TileType.Desert:
                return 0.44f;
            case TileType.Barren:
                return 0.50f;
            case TileType.Mountain:
                return 0.68f;
            default:
                return 0.20f;
        }
    }

    private static bool IsInterior(int x, int y, int width, int height, int margin)
    {
        return x >= margin && y >= margin && x < width - margin && y < height - margin;
    }

    private static bool InBounds(int x, int y, int width, int height)
    {
        return x >= 0 && y >= 0 && x < width && y < height;
    }

    private static int AssignBiomeTopN(
        List<ProceduralCell> cells,
        HashSet<int> assigned,
        TileType[,] map,
        int requestedCount,
        Func<ProceduralCell, float> scoreFunc,
        TileType biome)
    {
        if (requestedCount <= 0 || cells == null || cells.Count == 0)
        {
            return 0;
        }

        var candidates = new List<ProceduralCell>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            ProceduralCell cell = cells[i];
            if (assigned.Contains(cell.index))
            {
                continue;
            }

            candidates.Add(cell);
        }

        candidates.Sort((a, b) => scoreFunc(b).CompareTo(scoreFunc(a)));

        int assignedCount = 0;
        int max = Mathf.Min(requestedCount, candidates.Count);
        for (int i = 0; i < max; i++)
        {
            ProceduralCell cell = candidates[i];
            if (assigned.Add(cell.index))
            {
                map[cell.x, cell.y] = biome;
                assignedCount++;
            }
        }

        return assignedCount;
    }

    private static void SmoothBiomeTransitions(TileType[,] map, int width, int height, int passes)
    {
        if (map == null || width <= 0 || height <= 0 || passes <= 0)
        {
            return;
        }

        TileType[] ruggedTypes = { TileType.Mountain, TileType.Barren, TileType.DeforestedForest };

        for (int pass = 0; pass < passes; pass++)
        {
            var updates = new List<(int x, int y, TileType next)>();

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    TileType current = map[x, y];
                    if (current == TileType.Water)
                    {
                        continue;
                    }

                    int similarNeighbors = 0;
                    int forestNeighbors = 0;
                    int plainNeighbors = 0;
                    int ruggedNeighbors = 0;

                    Vector3Int[] neighbors = GetNeighborsPointTop(new Vector3Int(x, y, 0));
                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        Vector3Int n = neighbors[i];
                        if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height)
                        {
                            continue;
                        }

                        TileType nType = map[n.x, n.y];
                        if (nType == current)
                        {
                            similarNeighbors++;
                        }

                        if (nType == TileType.Forest) forestNeighbors++;
                        if (nType == TileType.Plains) plainNeighbors++;

                        for (int r = 0; r < ruggedTypes.Length; r++)
                        {
                            if (nType == ruggedTypes[r])
                            {
                                ruggedNeighbors++;
                                break;
                            }
                        }
                    }

                    if (similarNeighbors >= 2)
                    {
                        continue;
                    }

                    TileType next = current;

                    if (forestNeighbors >= 3)
                    {
                        next = TileType.Forest;
                    }
                    else if (ruggedNeighbors >= 3)
                    {
                        next = TileType.Barren;
                    }
                    else if (plainNeighbors >= 3)
                    {
                        next = TileType.Plains;
                    }

                    if (next != current && next != TileType.Water)
                    {
                        updates.Add((x, y, next));
                    }
                }
            }

            for (int i = 0; i < updates.Count; i++)
            {
                (int x, int y, TileType next) = updates[i];
                map[x, y] = next;
            }
        }
    }

    private static int CountType(TileType[,] map, TileType type)
    {
        int width = map.GetLength(0);
        int height = map.GetLength(1);
        int count = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (map[x, y] == type)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static float FractalNoise(float x, float y, int octaves, float lacunarity, float persistence)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float sum = 0f;
        float max = 0f;

        for (int i = 0; i < octaves; i++)
        {
            sum += Mathf.PerlinNoise(x * frequency, y * frequency) * amplitude;
            max += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return max > 0.0001f ? sum / max : 0f;
    }

    private readonly struct ProceduralCell
    {
        public readonly int index;
        public readonly int x;
        public readonly int y;
        public readonly float elevation;
        public readonly float moisture;
        public readonly float heat;
        public readonly float rugged;

        public ProceduralCell(int index, int x, int y, float elevation, float moisture, float heat, float rugged)
        {
            this.index = index;
            this.x = x;
            this.y = y;
            this.elevation = elevation;
            this.moisture = moisture;
            this.heat = heat;
            this.rugged = rugged;
        }
    }

    [Serializable]
    private sealed class UniversalWorldFileData
    {
        public string worldId = "finite-earth-alpha";
        public string displayName = "Finite Earth Alpha";
        public string[] rows = Array.Empty<string>();
        public UniversalSpawnPoint[] spawnPoints = Array.Empty<UniversalSpawnPoint>();
    }

    [Serializable]
    private sealed class UniversalSpawnPoint
    {
        public string id = "spawn";
        public int x;
        public int y;
    }
}
