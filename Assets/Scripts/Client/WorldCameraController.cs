using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

public class WorldCameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private HexWorldGeneratorTilemap worldGenerator;

    [Header("Startup")]
    [SerializeField] private bool frameOnStart = true;
    [SerializeField] private bool deriveZoomLimitsFromFrame = true;

    [Header("Pan")]
    [SerializeField] private float keyboardPanSpeed = 14f;
    [SerializeField] private float dragPanScale = 0.010f;

    [Header("Zoom")]
    [SerializeField] private float zoomStep = 0.02f;
    [SerializeField] private float minZoom = 6f;
    [SerializeField] private float maxZoom = 42f;

    [Header("Clamp")]
    [SerializeField] private bool clampToMapBounds = true;
    [SerializeField] private float worldPadding = 1.25f;

    private bool isDragging;
    private bool hasInitialFrame;
    private Vector2 lastPointerPosition;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (worldGenerator == null)
        {
            worldGenerator = FindAnyObjectByType<HexWorldGeneratorTilemap>();
        }
    }

    private void Start()
    {
        if (targetCamera == null || worldGenerator == null)
        {
            return;
        }

        if (frameOnStart && worldGenerator.IsGenerated)
        {
            ApplyInitialFrameAndZoomLimits();
        }
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            return;
        }

        if (!hasInitialFrame && frameOnStart && worldGenerator != null && worldGenerator.IsGenerated)
        {
            ApplyInitialFrameAndZoomLimits();
        }

        HandleZoom();
        HandleKeyboardPan();
        HandleDragPan();

        if (clampToMapBounds)
        {
            ClampToMapBounds();
        }
    }

    private void HandleZoom()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f)
        {
            return;
        }

        float nextSize = targetCamera.orthographicSize - (scroll * zoomStep);
        targetCamera.orthographicSize = Mathf.Clamp(nextSize, minZoom, maxZoom);
    }

    private void HandleKeyboardPan()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        Vector2 axis = Vector2.zero;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) axis.x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) axis.x += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) axis.y -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) axis.y += 1f;

        if (axis.sqrMagnitude < 0.001f)
        {
            return;
        }

        axis.Normalize();
        float zoomFactor = Mathf.Max(1f, targetCamera.orthographicSize * 0.35f);
        Vector3 delta = new Vector3(axis.x, axis.y, 0f) * (keyboardPanSpeed * zoomFactor * Time.unscaledDeltaTime);
        targetCamera.transform.position += delta;
    }

    private void HandleDragPan()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        bool dragPressed = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
        if (!dragPressed)
        {
            isDragging = false;
            return;
        }

        Vector2 pointer = mouse.position.ReadValue();
        if (!isDragging)
        {
            isDragging = true;
            lastPointerPosition = pointer;
            return;
        }

        Vector2 delta = pointer - lastPointerPosition;
        lastPointerPosition = pointer;

        float zoomFactor = Mathf.Max(1f, targetCamera.orthographicSize * 0.45f);
        targetCamera.transform.position += new Vector3(-delta.x, -delta.y, 0f) * (dragPanScale * zoomFactor);
    }

    private void ClampToMapBounds()
    {
        if (!TryGetWorldBounds(out Bounds worldBounds))
        {
            return;
        }

        Vector3 position = targetCamera.transform.position;
        float vertical = targetCamera.orthographicSize;
        float horizontal = vertical * targetCamera.aspect;

        float minX = worldBounds.min.x + horizontal - worldPadding;
        float maxX = worldBounds.max.x - horizontal + worldPadding;
        float minY = worldBounds.min.y + vertical - worldPadding;
        float maxY = worldBounds.max.y - vertical + worldPadding;

        if (minX > maxX)
        {
            position.x = worldBounds.center.x;
        }
        else
        {
            position.x = Mathf.Clamp(position.x, minX, maxX);
        }

        if (minY > maxY)
        {
            position.y = worldBounds.center.y;
        }
        else
        {
            position.y = Mathf.Clamp(position.y, minY, maxY);
        }

        targetCamera.transform.position = position;
    }

    private bool TryGetWorldBounds(out Bounds worldBounds)
    {
        worldBounds = default;

        if (worldGenerator == null || !worldGenerator.IsGenerated)
        {
            return false;
        }

        Tilemap terrain = worldGenerator.TerrainTilemap;
        if (terrain == null)
        {
            return false;
        }

        terrain.CompressBounds();
        Bounds local = terrain.localBounds;
        worldBounds = new Bounds(
            terrain.transform.TransformPoint(local.center),
            Vector3.Scale(local.size, terrain.transform.lossyScale));

        return worldBounds.size.x > 0.1f && worldBounds.size.y > 0.1f;
    }

    public void FocusOnCell(Vector3Int cell, bool zoomIn)
    {
        if (targetCamera == null || worldGenerator == null || !worldGenerator.IsGenerated)
        {
            return;
        }

        Vector3 center = worldGenerator.GetCellCenterWorld(cell);
        Vector3 cameraPos = targetCamera.transform.position;
        cameraPos.x = center.x;
        cameraPos.y = center.y;
        targetCamera.transform.position = cameraPos;

        if (zoomIn)
        {
            float desired = Mathf.Clamp(minZoom * 1.05f, minZoom, maxZoom);
            targetCamera.orthographicSize = desired;
        }

        if (clampToMapBounds)
        {
            ClampToMapBounds();
        }
    }

    private void ApplyInitialFrameAndZoomLimits()
    {
        if (targetCamera == null || worldGenerator == null || !worldGenerator.IsGenerated)
        {
            return;
        }

        worldGenerator.FrameCamera(targetCamera);
        hasInitialFrame = true;

        if (!deriveZoomLimitsFromFrame)
        {
            return;
        }

        float framed = Mathf.Max(2f, targetCamera.orthographicSize);
        minZoom = Mathf.Clamp(framed * 0.12f, 4f, 16f);
        maxZoom = Mathf.Max(minZoom + 3f, framed * 2.4f);
    }
}
