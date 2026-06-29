using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class SurfacePathMousePainter : MonoBehaviour
{
    const float RightDragPixels = 6f;

    public float RadiusMeters = 14f;
    public float Strength = 1f;
    public float RegrowSeconds;
    public float SpacingMeters = 4f;
    public bool BrushActive;
    public SurfacePathShape Shape = SurfacePathShape.HardDisc;

    bool _dragging;
    bool _hasLastPoint;
    SurfacePathOperation _strokeOperation;
    Vector3 _lastDirection;
    float _lastSurfaceRadius;
    LineRenderer _preview;
    Material _previewMaterial;
    bool _strokeDirty;
    bool _lookHoldDisabled;
    bool _rightTapCandidate;
    bool _rightCameraDrag;
    Vector2 _rightStartPosition;

    public static SurfacePathMousePainter Find() =>
        Object.FindAnyObjectByType<SurfacePathMousePainter>();

    public static SurfacePathMousePainter GetOrCreate()
    {
        SurfacePathMousePainter tool = Find();
        if (tool != null)
            return tool;

        GameObject go = new("Surface Path Mouse Painter");
        return go.AddComponent<SurfacePathMousePainter>();
    }

    public static void EnsureAvailable()
    {
        if (Find() != null)
            return;

        GetOrCreate().BrushActive = false;
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

    public string Status() =>
        $"path mouse: {(BrushActive ? "armed" : "off")}, shape={ShapeName(Shape)}, P toggles, right-click shape, wheel size, shift-wheel strength, left-drag paint, shift-drag erase, radius={RadiusMeters:F1}m, strength={Strength:F2}, regrow={RegrowSeconds:F0}s, spacing={SpacingMeters:F1}m";

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !InputAllowed())
        {
            ResetDrag();
            SetPreviewVisible(false);
            return;
        }

        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
        {
            BrushActive = !BrushActive;
            ResetDrag();
        }

        HandleShapeOrCameraLook(mouse);
        SetCameraLookBlocked(BrushActive && !_rightCameraDrag);

        if (!BrushActive)
        {
            SetPreviewVisible(false);
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

    void OnDestroy()
    {
        ResetDrag();
        SetCameraLookBlocked(false);
        if (_previewMaterial != null)
            Destroy(_previewMaterial);
    }

    bool InputAllowed()
    {
        if (ServiceLocator.TryGet(out IConsoleService console) && console.IsOpen)
            return false;
        if (ServiceLocator.TryGet(out IInputMapService input) && !input.GameplayEnabled)
            return false;
        return true;
    }

    void PaintAtMouse(Mouse mouse, SurfacePathOperation operation, bool force)
    {
        if (!TryGetMouseHit(mouse, out Planet planet, out Vector3 direction, out float surfaceRadius, out _, out _))
            return;

        if (!_hasLastPoint)
        {
            Paint(planet, direction, operation);
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
        for (int i = 1; i <= segments; i++)
            Paint(planet, Vector3.Slerp(_lastDirection, direction, i / (float)segments).normalized, operation);

        Remember(direction, surfaceRadius);
    }

    void Paint(Planet planet, Vector3 direction, SurfacePathOperation operation)
    {
        if (planet.TryPaintSurfacePathBrushAtLocalDirection(direction, RadiusMeters, Strength, RegrowSeconds,
                Shape, operation, out _, saveImmediately: false))
        {
            _strokeDirty = true;
        }
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
            ResolvePlanet()?.FlushSurfacePathEdits();
        _strokeDirty = false;
    }

    static Planet ResolvePlanet()
    {
        if (ServiceLocator.TryGet(out IPlanet servicePlanet) && servicePlanet is Planet planet)
            return planet;
        return Object.FindAnyObjectByType<Planet>();
    }

    static bool ShiftHeld()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
    }

    void CycleShape()
    {
        Shape = Shape == SurfacePathShape.HardSquare
            ? SurfacePathShape.HardDisc
            : SurfacePathShape.HardSquare;
    }

    void HandleShapeOrCameraLook(Mouse mouse)
    {
        if (!BrushActive)
        {
            _rightTapCandidate = false;
            _rightCameraDrag = false;
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            _rightTapCandidate = true;
            _rightCameraDrag = false;
            _rightStartPosition = mouse.position.ReadValue();
            ResetDrag();
            SetCameraLookBlocked(true);
            return;
        }

        if (mouse.rightButton.isPressed && _rightTapCandidate && !_rightCameraDrag)
        {
            Vector2 delta = mouse.position.ReadValue() - _rightStartPosition;
            if (delta.sqrMagnitude >= RightDragPixels * RightDragPixels)
            {
                _rightCameraDrag = true;
                _rightTapCandidate = false;
                SetCameraLookBlocked(false);
            }
        }

        if (mouse.rightButton.wasReleasedThisFrame)
        {
            if (_rightTapCandidate && !_rightCameraDrag)
                CycleShape();

            _rightTapCandidate = false;
            _rightCameraDrag = false;
            SetCameraLookBlocked(true);
        }
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
        if (!TryGetMouseHit(mouse, out Planet planet, out Vector3 direction, out float surfaceRadius, out _, out Vector3 normal))
        {
            SetPreviewVisible(false);
            return;
        }

        EnsurePreview();
        SetPreviewVisible(true);

        bool square = Shape == SurfacePathShape.HardSquare;
        int segments = square ? 4 : 64;
        _preview.positionCount = segments + 1;
        _preview.widthMultiplier = Mathf.Clamp(RadiusMeters * 0.035f, 0.05f, 1.5f);
        Color previewColor = erasePreview
            ? new Color(0.1f, 0.85f, 1f, Mathf.Lerp(0.35f, 0.9f, Strength))
            : new Color(1f, 0.15f, 1f, Mathf.Lerp(0.35f, 0.9f, Strength));
        _preview.startColor = previewColor;
        _preview.endColor = previewColor;
        if (_previewMaterial != null)
            _previewMaterial.color = previewColor;

        if (square)
        {
            UpdateSquarePreview(planet, direction, surfaceRadius, normal);
            return;
        }

        UpdateDiscPreview(planet, direction, surfaceRadius, normal, segments);
    }

    void UpdateDiscPreview(Planet planet, Vector3 direction, float surfaceRadius, Vector3 centerNormal, int segments)
    {
        FaceSpaceCellRangeBuilder.DirectionToFaceUv(direction, out int face, out Vector2 faceUv);
        float metersPerUv = FaceSpaceCellRangeBuilder.ComputeMetersPerUV(face, faceUv, Mathf.Max(surfaceRadius, 0.001f));
        float radiusUv = Mathf.Max(0.00001f, RadiusMeters / metersPerUv);
        float worldScale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(planet.transform);
        float localRadius = surfaceRadius / Mathf.Max(worldScale, 0.0001f);
        float lift = Mathf.Clamp(RadiusMeters * 0.12f, 1f, 5f);

        for (int i = 0; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector2 uv = faceUv + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radiusUv;
            Vector3 local = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(face, uv) * localRadius;
            Vector3 world = planet.transform.TransformPoint(local);
            Vector3 normal = (world - planet.transform.position).sqrMagnitude > 0.0001f
                ? (world - planet.transform.position).normalized
                : centerNormal;
            _preview.SetPosition(i, world + normal * lift);
        }
    }

    void UpdateSquarePreview(Planet planet, Vector3 direction, float surfaceRadius, Vector3 centerNormal)
    {
        FaceSpaceCellRangeBuilder.DirectionToFaceUv(direction, out int face, out Vector2 faceUv);
        float metersPerUv = FaceSpaceCellRangeBuilder.ComputeMetersPerUV(face, faceUv, Mathf.Max(surfaceRadius, 0.001f));
        float halfUv = Mathf.Max(0.00001f, RadiusMeters / metersPerUv);
        float worldScale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(planet.transform);
        float localRadius = surfaceRadius / Mathf.Max(worldScale, 0.0001f);
        float lift = Mathf.Clamp(RadiusMeters * 0.12f, 1f, 5f);

        for (int i = 0; i <= 4; i++)
        {
            int corner = i & 3;
            float sx = (corner == 0 || corner == 3) ? -1f : 1f;
            float sy = corner < 2 ? -1f : 1f;
            Vector2 uv = faceUv + new Vector2(sx, sy) * halfUv;
            Vector3 local = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(face, uv) * localRadius;
            Vector3 world = planet.transform.TransformPoint(local);
            Vector3 normal = (world - planet.transform.position).sqrMagnitude > 0.0001f
                ? (world - planet.transform.position).normalized
                : centerNormal;
            _preview.SetPosition(i, world + normal * lift);
        }
    }

    static bool TryGetMouseHit(Mouse mouse, out Planet planet, out Vector3 direction,
        out float surfaceRadius, out Vector3 point, out Vector3 normal)
    {
        planet = ResolvePlanet();
        direction = default;
        surfaceRadius = 0f;
        point = default;
        normal = default;
        Camera camera = Camera.main;
        if (camera == null || planet == null)
            return false;

        Ray ray = camera.ScreenPointToRay(mouse.position.ReadValue());
        float maxDistance = Mathf.Max(planet.LastGeneratedRadius * 4f, 10000f);
        if (!planet.TryRaycastSurface(ray, maxDistance, out PlanetSurfaceRaycastHit hit))
            return false;

        point = hit.Point;
        direction = planet.transform.InverseTransformPoint(point).normalized;
        surfaceRadius = hit.SurfaceRadius;
        normal = (point - planet.transform.position).normalized;
        return normal.sqrMagnitude > 0.0001f;
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

    void SetPreviewVisible(bool visible)
    {
        if (_preview != null)
            _preview.enabled = visible;
    }

    void SetCameraLookBlocked(bool blocked)
    {
        if (!ServiceLocator.TryGet(out IInputMapService input) || input.LookHold == null)
            return;

        if (blocked)
        {
            if (!_lookHoldDisabled && input.LookHold.enabled)
            {
                input.LookHold.Disable();
                _lookHoldDisabled = true;
            }
            return;
        }

        if (_lookHoldDisabled)
        {
            input.LookHold.Enable();
            _lookHoldDisabled = false;
        }
    }

    static string ShapeName(SurfacePathShape shape)
    {
        switch (shape)
        {
            case SurfacePathShape.HardDisc:
                return "disc";
            case SurfacePathShape.HardSquare:
                return "square";
            default:
                return "disc";
        }
    }
}
