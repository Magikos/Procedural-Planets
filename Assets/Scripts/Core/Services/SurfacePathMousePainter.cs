using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class SurfacePathMousePainter : MonoBehaviour, ICameraLookBlocker
{
    const float RightDragPixels = 6f;
    const int PreviewSegments = 64;

    public float RadiusMeters = 14f;
    public float Strength = 1f;
    public float RegrowSeconds;
    public float SpacingMeters = 2f;
    public bool BrushActive;
    public SurfacePathShape Shape = SurfacePathShape.SoftDisc;

    readonly Vector3[] _previewPoints = new Vector3[PreviewSegments + 1];
    readonly List<Vector3> _strokePreviewPoints = new();

    bool _dragging;
    bool _hasLastPoint;
    SurfacePathOperation _strokeOperation;
    Vector3 _lastDirection;
    float _lastSurfaceRadius;
    LineRenderer _preview;
    LineRenderer _strokePreview;
    Material _previewMaterial;
    bool _strokeDirty;
    bool _rightTapCandidate;
    bool _rightCameraDrag;
    Vector2 _rightStartPosition;
    IPlanetSurfaceRaycaster _raycaster;
    ISurfacePathBrushService _pathBrush;
    Camera _camera;

    public static SurfacePathMousePainter Find() =>
        Object.FindAnyObjectByType<SurfacePathMousePainter>();

    public bool BlocksCameraLook => BrushActive && _rightTapCandidate && !_rightCameraDrag;

    public static SurfacePathMousePainter GetOrCreate()
    {
        SurfacePathMousePainter tool = Find();
        if (tool != null)
            return tool;

        GameObject go = new("Surface Path Mouse Painter");
        DontDestroyOnLoad(go);
        return go.AddComponent<SurfacePathMousePainter>();
    }

    public void SetBrush(float? radiusMeters, float? strength, float? regrowSeconds, float? spacingMeters)
    {
        if (radiusMeters.HasValue)
            RadiusMeters = Mathf.Clamp(radiusMeters.Value, 0.25f, 250f);
        if (strength.HasValue)
            Strength = Mathf.Clamp01(strength.Value);
        if (regrowSeconds.HasValue)
            RegrowSeconds = Mathf.Max(0f, regrowSeconds.Value);
        if (spacingMeters.HasValue)
            SpacingMeters = Mathf.Clamp(spacingMeters.Value, 0.25f, 100f);
    }

    public bool TrySetShape(string shape)
    {
        switch ((shape ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "soft":
            case "soft-disc":
            case "disc":
                Shape = SurfacePathShape.SoftDisc;
                return true;
            case "hard":
            case "hard-disc":
                Shape = SurfacePathShape.HardDisc;
                return true;
            case "square":
            case "hard-square":
                Shape = SurfacePathShape.HardSquare;
                return true;
            default:
                return false;
        }
    }

    public string Status() =>
        $"path mouse: {(BrushActive ? "armed" : "off")}, shape={ShapeName(Shape)}, P toggles, right-click cycles shape, wheel size, shift-wheel strength, left-drag paint, shift-drag erase, radius={RadiusMeters:F1}m, strength={Strength:F2}, regrow={RegrowSeconds:F0}s, spacing={SpacingMeters:F1}m";

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !InputAllowed())
        {
            ResetDrag();
            ResetRightMouseState();
            SetPreviewVisible(false);
            ClearStrokePreview();
            return;
        }

        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
        {
            BrushActive = !BrushActive;
            ResetDrag();
            ResetRightMouseState();
        }

        HandleShapeOrCameraLook(mouse);

        if (!BrushActive)
        {
            SetPreviewVisible(false);
            ClearStrokePreview();
            return;
        }

        bool shift = ShiftHeld();
        AdjustBrush(mouse, shift);
        UpdatePreview(mouse, shift);

        if (mouse.leftButton.wasPressedThisFrame)
        {
            _dragging = true;
            _hasLastPoint = false;
            _strokeOperation = shift ? SurfacePathOperation.Erase : SurfacePathOperation.Paint;
            PaintAtMouse(mouse, _strokeOperation, force: true);
            return;
        }

        if (mouse.leftButton.isPressed && _dragging)
        {
            PaintAtMouse(mouse, _strokeOperation, force: false);
            return;
        }

        if (mouse.leftButton.wasReleasedThisFrame)
            ResetDrag();
    }

    void OnEnable()
    {
        if (ServiceLocator.TryGet(out ICameraLookBlocker existing)
            && ServiceLocator.IsAlive(existing)
            && !ReferenceEquals(existing, this))
        {
            return;
        }

        ServiceLocator.Register<ICameraLookBlocker>(this);
    }

    void OnDisable()
    {
        ResetDrag();
        ResetRightMouseState();
        SetPreviewVisible(false);
        ClearStrokePreview();
        ServiceLocator.Unregister<ICameraLookBlocker>(this);
    }

    void OnDestroy()
    {
        ResetDrag();
        if (_previewMaterial != null)
            Destroy(_previewMaterial);
    }

    IConsoleService _cachedConsole;
    IInputMapService _cachedInput;

    bool InputAllowed()
    {
        if (!ServiceLocator.IsAlive(_cachedConsole))
        {
            _cachedConsole = null;
            ServiceLocator.TryGet(out _cachedConsole);
        }
        if (_cachedConsole != null && _cachedConsole.IsOpen)
            return false;

        if (!ServiceLocator.IsAlive(_cachedInput))
        {
            _cachedInput = null;
            ServiceLocator.TryGet(out _cachedInput);
        }
        if (_cachedInput != null && !_cachedInput.GameplayEnabled)
            return false;

        return true;
    }

    void PaintAtMouse(Mouse mouse, SurfacePathOperation operation, bool force)
    {
        if (!TryGetMouseHit(mouse, out Vector3 direction, out float surfaceRadius,
                out Vector3 hitPoint, out Vector3 hitNormal))
            return;

        if (!_hasLastPoint)
        {
            if (Paint(direction, operation))
                AddStrokePreviewPoint(hitPoint, hitNormal, operation);
            Remember(direction, surfaceRadius);
            return;
        }

        float radius = Mathf.Max(_lastSurfaceRadius, surfaceRadius, 1f);
        float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(_lastDirection, direction), -1f, 1f));
        float distance = angle * radius;
        float step = Mathf.Max(SpacingMeters, 0.25f);
        if (!force && distance < step)
            return;

        int segments = Mathf.Max(1, Mathf.CeilToInt(distance / step));
        bool painted = false;
        for (int i = 1; i <= segments; i++)
            painted |= Paint(Vector3.Slerp(_lastDirection, direction, i / (float)segments).normalized, operation);
        if (painted)
            AddStrokePreviewPoint(hitPoint, hitNormal, operation);

        Remember(direction, surfaceRadius);
    }

    bool Paint(Vector3 direction, SurfacePathOperation operation)
    {
        if (ResolvePathBrush()?.TryPaintSurfacePathBrushAtLocalDirection(direction, RadiusMeters, Strength,
                RegrowSeconds, Shape, operation, out _, saveImmediately: false) == true)
        {
            _strokeDirty = true;
            return true;
        }

        return false;
    }

    void Remember(Vector3 direction, float surfaceRadius)
    {
        _lastDirection = direction;
        _lastSurfaceRadius = surfaceRadius;
        _hasLastPoint = true;
    }

    void ResetDrag()
    {
        bool flush = _dragging && _strokeDirty;
        _dragging = false;
        _hasLastPoint = false;
        if (flush)
            ResolvePathBrush()?.FlushSurfacePathEdits();
        _strokeDirty = false;
        ClearStrokePreview();
    }

    IPlanetSurfaceRaycaster ResolveRaycaster()
    {
        if (!ServiceLocator.IsAlive(_raycaster))
            ServiceLocator.TryGet(out _raycaster);
        return _raycaster;
    }

    ISurfacePathBrushService ResolvePathBrush()
    {
        if (!ServiceLocator.IsAlive(_pathBrush))
            ServiceLocator.TryGet(out _pathBrush);
        return _pathBrush;
    }

    Camera ResolveCamera()
    {
        if (_camera == null || !_camera.isActiveAndEnabled)
            _camera = Camera.main;
        return _camera;
    }

    static bool ShiftHeld()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
    }

    void CycleShape()
    {
        Shape = Shape switch
        {
            SurfacePathShape.SoftDisc => SurfacePathShape.HardDisc,
            SurfacePathShape.HardDisc => SurfacePathShape.HardSquare,
            _ => SurfacePathShape.SoftDisc,
        };
    }

    void HandleShapeOrCameraLook(Mouse mouse)
    {
        if (!BrushActive)
        {
            ResetRightMouseState();
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            _rightTapCandidate = true;
            _rightCameraDrag = false;
            _rightStartPosition = mouse.position.ReadValue();
            ResetDrag();
            return;
        }

        if (mouse.rightButton.isPressed && _rightTapCandidate && !_rightCameraDrag)
        {
            Vector2 delta = mouse.position.ReadValue() - _rightStartPosition;
            if (delta.sqrMagnitude >= RightDragPixels * RightDragPixels)
            {
                _rightCameraDrag = true;
                _rightTapCandidate = false;
            }
        }

        if (mouse.rightButton.wasReleasedThisFrame)
        {
            if (_rightTapCandidate && !_rightCameraDrag)
                CycleShape();

            _rightTapCandidate = false;
            _rightCameraDrag = false;
        }
    }

    void ResetRightMouseState()
    {
        _rightTapCandidate = false;
        _rightCameraDrag = false;
    }

    void AdjustBrush(Mouse mouse, bool strengthMode)
    {
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f)
            return;

        if (strengthMode)
            Strength = Mathf.Clamp01(Strength + Mathf.Sign(scroll) * 0.05f);
        else
            RadiusMeters = Mathf.Clamp(RadiusMeters + Mathf.Sign(scroll), 0.25f, 250f);
    }

    void UpdatePreview(Mouse mouse, bool erasePreview)
    {
        if (!TryGetMouseHit(mouse, out Vector3 direction, out float surfaceRadius, out _, out _))
        {
            SetPreviewVisible(false);
            return;
        }

        ISurfacePathBrushService brush = ResolvePathBrush();
        if (brush == null)
        {
            SetPreviewVisible(false);
            return;
        }

        EnsurePreview();
        SetPreviewVisible(true);

        _preview.widthMultiplier = Mathf.Clamp(RadiusMeters * 0.035f, 0.05f, 1.5f);
        Color previewColor = erasePreview
            ? new Color(0.1f, 0.85f, 1f, Mathf.Lerp(0.35f, 0.9f, Strength))
            : new Color(1f, 0.15f, 1f, Mathf.Lerp(0.35f, 0.9f, Strength));
        _preview.startColor = previewColor;
        _preview.endColor = previewColor;
        if (_previewMaterial != null)
            _previewMaterial.color = previewColor;

        float lift = Mathf.Clamp(RadiusMeters * 0.12f, 1f, 5f);
        if (!brush.TryBuildSurfacePathBrushPreview(direction, surfaceRadius, RadiusMeters, Shape,
                _previewPoints, PreviewSegments, lift, out int count))
        {
            SetPreviewVisible(false);
            return;
        }

        _preview.positionCount = count;
        for (int i = 0; i < count; i++)
            _preview.SetPosition(i, _previewPoints[i]);
    }

    bool TryGetMouseHit(Mouse mouse, out Vector3 direction, out float surfaceRadius,
        out Vector3 hitPoint, out Vector3 hitNormal)
    {
        direction = default;
        surfaceRadius = 0f;
        hitPoint = default;
        hitNormal = default;

        Camera camera = ResolveCamera();
        IPlanetSurfaceRaycaster raycaster = ResolveRaycaster();
        ISurfacePathBrushService brush = ResolvePathBrush();
        if (camera == null || raycaster == null || brush == null)
            return false;

        Ray ray = camera.ScreenPointToRay(mouse.position.ReadValue());
        float maxDistance = Mathf.Max(camera.farClipPlane, 10000f);
        if (!raycaster.TryRaycastSurface(ray, maxDistance, out PlanetSurfaceRaycastHit hit))
            return false;
        if (!brush.TryGetSurfacePathLocalDirection(hit.Point, out direction))
            return false;

        surfaceRadius = hit.SurfaceRadius;
        hitPoint = hit.Point;
        hitNormal = hit.Normal.sqrMagnitude > 0.0001f ? hit.Normal.normalized : Vector3.up;
        return true;
    }

    void EnsurePreview()
    {
        if (_preview != null)
            return;

        GameObject go = new("Path Brush Preview");
        go.transform.SetParent(transform, false);
        _preview = go.AddComponent<LineRenderer>();
        _preview.useWorldSpace = true;
        _preview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _preview.receiveShadows = false;
        _preview.startColor = new Color(1f, 0.15f, 1f, 0.9f);
        _preview.endColor = _preview.startColor;

        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        _previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _previewMaterial.color = _preview.startColor;
        _previewMaterial.renderQueue = 5000;
        _preview.material = _previewMaterial;
    }

    void EnsureStrokePreview()
    {
        if (_strokePreview != null)
            return;

        EnsurePreview();
        GameObject go = new("Path Stroke Preview");
        go.transform.SetParent(transform, false);
        _strokePreview = go.AddComponent<LineRenderer>();
        _strokePreview.useWorldSpace = true;
        _strokePreview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _strokePreview.receiveShadows = false;
        _strokePreview.numCornerVertices = 8;
        _strokePreview.numCapVertices = 8;
        _strokePreview.material = _previewMaterial;
        _strokePreview.enabled = false;
    }

    void AddStrokePreviewPoint(Vector3 point, Vector3 normal, SurfacePathOperation operation)
    {
        EnsureStrokePreview();

        Vector3 lifted = point + normal * Mathf.Clamp(RadiusMeters * 0.08f, 0.75f, 4f);
        int last = _strokePreviewPoints.Count - 1;
        if (last >= 0 && Vector3.Distance(_strokePreviewPoints[last], lifted) < 0.05f)
            return;

        _strokePreviewPoints.Add(lifted);
        Color color = operation == SurfacePathOperation.Erase
            ? new Color(0.1f, 0.85f, 1f, 0.35f)
            : new Color(1f, 0.15f, 1f, 0.35f);
        _strokePreview.startColor = color;
        _strokePreview.endColor = color;
        _strokePreview.widthMultiplier = Mathf.Clamp(RadiusMeters * 2f, 0.25f, 40f);
        _strokePreview.positionCount = _strokePreviewPoints.Count;
        _strokePreview.SetPosition(_strokePreviewPoints.Count - 1, lifted);
        _strokePreview.enabled = _strokePreviewPoints.Count > 1;
    }

    void SetPreviewVisible(bool visible)
    {
        if (_preview != null)
            _preview.enabled = visible;
    }

    void ClearStrokePreview()
    {
        _strokePreviewPoints.Clear();
        if (_strokePreview == null)
            return;

        _strokePreview.positionCount = 0;
        _strokePreview.enabled = false;
    }

    static string ShapeName(SurfacePathShape shape)
    {
        switch (shape)
        {
            case SurfacePathShape.HardSquare:
                return "square";
            case SurfacePathShape.SoftDisc:
                return "soft-disc";
            default:
                return "hard-disc";
        }
    }
}
