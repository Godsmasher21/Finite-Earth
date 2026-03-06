using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class ActionInputController : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;
    [SerializeField] private bool includeRightClickSelection;
    [SerializeField] private bool includeIntermediateLineCells = true;

    public event Action<HexCoord> TileSelected;
    public event Action<HexCoord[]> TilesSelected;
    public event Action<HexCoord[]> SelectionPreviewChanged;
    public event Action<FiniteEarthActionType> ActionHotkeyPressed;

    private static readonly HexCoord[] EmptySelection = Array.Empty<HexCoord>();

    private bool isDragTracking;
    private bool hasLastDragCell;
    private Vector3Int lastDragCell;

    private HexCoord[] lastPreviewSelection = EmptySelection;
    private readonly List<HexCoord> dragSelectedCoords = new List<HexCoord>();
    private readonly HashSet<long> dragSelectedKeys = new HashSet<long>();

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
    }

    private void Update()
    {
        HandleTileSelection();
        HandleHotkeys();
    }

    private void HandleTileSelection()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || mainCamera == null || worldGenerator == null)
        {
            return;
        }

        bool rightClickPressed = includeRightClickSelection && mouse.rightButton.wasPressedThisFrame;
        if (rightClickPressed && !IsPointerOverUi())
        {
            TrySelectSingleAt(mouse.position.ReadValue());
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            BeginDragSelection(mouse.position.ReadValue());
            return;
        }

        if (isDragTracking && mouse.leftButton.isPressed)
        {
            ContinueDragSelection(mouse.position.ReadValue());
            return;
        }

        if (!isDragTracking || !mouse.leftButton.wasReleasedThisFrame)
        {
            return;
        }

        EndDragSelection(mouse.position.ReadValue());
    }

    private void BeginDragSelection(Vector2 pointerScreen)
    {
        ClearDragSelection();

        if (IsPointerOverUi())
        {
            isDragTracking = false;
            ClearSelectionPreview();
            return;
        }

        isDragTracking = true;

        if (TryGetCell(pointerScreen, out Vector3Int cell))
        {
            AddCellToDragSelection(cell);
            lastDragCell = cell;
            hasLastDragCell = true;
        }

        EmitSelectionPreview(BuildDragSelectionArray());
    }

    private void ContinueDragSelection(Vector2 pointerScreen)
    {
        if (IsPointerOverUi())
        {
            return;
        }

        if (!TryGetCell(pointerScreen, out Vector3Int currentCell))
        {
            return;
        }

        if (!hasLastDragCell)
        {
            AddCellToDragSelection(currentCell);
            lastDragCell = currentCell;
            hasLastDragCell = true;
            EmitSelectionPreview(BuildDragSelectionArray());
            return;
        }

        if (currentCell == lastDragCell)
        {
            return;
        }

        if (includeIntermediateLineCells)
        {
            AddLineCells(lastDragCell, currentCell);
        }
        else
        {
            AddCellToDragSelection(currentCell);
        }

        lastDragCell = currentCell;
        EmitSelectionPreview(BuildDragSelectionArray());
    }

    private void EndDragSelection(Vector2 releaseScreen)
    {
        isDragTracking = false;

        HexCoord[] selected = BuildDragSelectionArray();
        ClearSelectionPreview();

        if (selected.Length == 0)
        {
            if (!IsPointerOverUi())
            {
                TrySelectSingleAt(releaseScreen);
            }

            ClearDragSelection();
            return;
        }

        if (selected.Length == 1)
        {
            TileSelected?.Invoke(selected[0]);
            ClearDragSelection();
            return;
        }

        TilesSelected?.Invoke(selected);
        ClearDragSelection();
    }

    private void HandleHotkeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.digit1Key.wasPressedThisFrame) ActionHotkeyPressed?.Invoke(FiniteEarthActionType.Claim);
        if (keyboard.digit2Key.wasPressedThisFrame) ActionHotkeyPressed?.Invoke(FiniteEarthActionType.BuildSettlement);
        if (keyboard.digit3Key.wasPressedThisFrame) ActionHotkeyPressed?.Invoke(FiniteEarthActionType.BuildIndustry);
        if (keyboard.digit4Key.wasPressedThisFrame) ActionHotkeyPressed?.Invoke(FiniteEarthActionType.HarvestForest);
        if (keyboard.digit5Key.wasPressedThisFrame) ActionHotkeyPressed?.Invoke(FiniteEarthActionType.Reforest);
        if (keyboard.digit6Key.wasPressedThisFrame) ActionHotkeyPressed?.Invoke(FiniteEarthActionType.Farm);
        if (keyboard.digit7Key.wasPressedThisFrame) ActionHotkeyPressed?.Invoke(FiniteEarthActionType.Irrigate);
        if (keyboard.digit8Key.wasPressedThisFrame) ActionHotkeyPressed?.Invoke(FiniteEarthActionType.Mine);
        if (keyboard.digit9Key.wasPressedThisFrame) ActionHotkeyPressed?.Invoke(FiniteEarthActionType.Restore);
    }

    private static bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private bool TryGetCell(Vector2 screenPosition, out Vector3Int cell)
    {
        cell = default;
        return worldGenerator != null
            && mainCamera != null
            && worldGenerator.TryGetCellUnderScreenPoint(mainCamera, screenPosition, out cell);
    }

    private void TrySelectSingleAt(Vector2 screenPosition)
    {
        if (TryGetCell(screenPosition, out Vector3Int cell))
        {
            TileSelected?.Invoke(HexCoord.FromVector3Int(cell));
        }
    }

    private void AddCellToDragSelection(Vector3Int cell)
    {
        if (cell.x < 0 || cell.y < 0 || cell.x >= worldGenerator.Width || cell.y >= worldGenerator.Height)
        {
            return;
        }

        long key = PackKey(cell.x, cell.y);
        if (!dragSelectedKeys.Add(key))
        {
            return;
        }

        dragSelectedCoords.Add(HexCoord.FromVector3Int(cell));
    }

    private void AddLineCells(Vector3Int startCell, Vector3Int endCell)
    {
        int steps = Mathf.Max(1, HexWorldGeneratorTilemap.HexDistance(startCell, endCell));
        Vector3 startCube = OddrToCube(startCell);
        Vector3 endCube = OddrToCube(endCell);

        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0f : (float)i / steps;
            Vector3 cube = Vector3.Lerp(startCube, endCube, t);
            Vector3Int roundedCube = CubeRound(cube);
            Vector3Int cell = CubeToOddr(roundedCube);
            AddCellToDragSelection(cell);
        }
    }

    private HexCoord[] BuildDragSelectionArray()
    {
        if (dragSelectedCoords.Count == 0)
        {
            return EmptySelection;
        }

        return dragSelectedCoords.ToArray();
    }

    private void ClearDragSelection()
    {
        dragSelectedCoords.Clear();
        dragSelectedKeys.Clear();
        hasLastDragCell = false;
    }

    private static Vector3 OddrToCube(Vector3Int offset)
    {
        int cubeX = offset.x - ((offset.y - (offset.y & 1)) / 2);
        int cubeZ = offset.y;
        int cubeY = -cubeX - cubeZ;
        return new Vector3(cubeX, cubeY, cubeZ);
    }

    private static Vector3Int CubeToOddr(Vector3Int cube)
    {
        int row = cube.z;
        int col = cube.x + ((cube.z - (cube.z & 1)) / 2);
        return new Vector3Int(col, row, 0);
    }

    private static Vector3Int CubeRound(Vector3 cube)
    {
        int rx = Mathf.RoundToInt(cube.x);
        int ry = Mathf.RoundToInt(cube.y);
        int rz = Mathf.RoundToInt(cube.z);

        float xDiff = Mathf.Abs(rx - cube.x);
        float yDiff = Mathf.Abs(ry - cube.y);
        float zDiff = Mathf.Abs(rz - cube.z);

        if (xDiff > yDiff && xDiff > zDiff)
        {
            rx = -ry - rz;
        }
        else if (yDiff > zDiff)
        {
            ry = -rx - rz;
        }
        else
        {
            rz = -rx - ry;
        }

        return new Vector3Int(rx, ry, rz);
    }

    private static long PackKey(int q, int r)
    {
        return ((long)q << 32) ^ (uint)r;
    }

    private void EmitSelectionPreview(HexCoord[] preview)
    {
        preview ??= EmptySelection;
        if (AreSameSelection(lastPreviewSelection, preview))
        {
            return;
        }

        if (preview.Length == 0)
        {
            lastPreviewSelection = EmptySelection;
            SelectionPreviewChanged?.Invoke(EmptySelection);
            return;
        }

        var copy = new HexCoord[preview.Length];
        Array.Copy(preview, copy, preview.Length);
        lastPreviewSelection = copy;
        SelectionPreviewChanged?.Invoke(copy);
    }

    private void ClearSelectionPreview()
    {
        EmitSelectionPreview(EmptySelection);
    }

    private static bool AreSameSelection(HexCoord[] a, HexCoord[] b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].q != b[i].q || a[i].r != b[i].r)
            {
                return false;
            }
        }

        return true;
    }
}
