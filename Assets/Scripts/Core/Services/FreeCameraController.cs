using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class FreeCameraController : MonoBehaviour
{
    [Header("Movement")]
    public float MoveSpeed = 10f;
    public float FastMultiplier = 3f;
    public float ScrollSpeed = 50f;
    public float OrbitSpeedMultiplier = 0.5f;
    public float SurfaceSpeedMultiplier = 0.02f;

    [Header("Look")]
    public float LookSensitivity = 2f;

    [Header("Auto Position")]
    public float ViewDistanceMultiplier = 2.5f;
    public float SurfaceHeight = 2f;
    [Range(1f, 30f)] public float SurfaceSunriseOffsetDegrees = 8f;
    public bool AutoPositionOnGenerate = true;

    [Header("Debug Info")]
    public bool ShowDebugOverlay = true;
    public Transform TargetCenter;

    float _lastPlanetRadius;
    float _lastElevationMin;
    float _lastElevationMax;
    Vector3 _lastPlanetCenter;

    Mouse _mouse;
    Keyboard _keyboard;
    bool _looking;
    bool _skipNextDelta;
    bool _surfaceView;
    Light _cachedSunLight;
    Vector3 _sunOrbitAxis = Vector3.forward;
    Vector3 _lastSunDirectionToSun;

    void OnEnable()
    {
        RefreshInputDevices();
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    }

    void OnDisable()
    {
        StopLooking();
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
    }

    void Start()
    {
        RefreshInputDevices();
        ConfigureCamera();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            RefreshInputDevices();
        else
            StopLooking();
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _lastPlanetCenter = evt.PlanetCenter;
        _lastPlanetRadius = evt.PlanetRadius;
        _lastElevationMin = evt.ElevationMin;
        _lastElevationMax = evt.ElevationMax;
        UpdateSunOrbitAxis();

        if (AutoPositionOnGenerate)
            RepositionCamera(_lastPlanetCenter, _lastPlanetRadius);
    }

    void Update()
    {
        RefreshInputDevices();
        UpdateSunOrbitAxis();

        HandleLook();
        HandleMovement();
        HandleShortcuts();
    }

    void ConfigureCamera()
    {
        var cam = GetComponent<Camera>();
        if (cam == null) return;

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.farClipPlane = 100000f;
    }

    void RefreshInputDevices()
    {
        _mouse = Mouse.current;
        _keyboard = Keyboard.current;
    }

    void HandleShortcuts()
    {
        if (WasKeyPressed(_keyboard?.spaceKey, KeyCode.Space) && _lastPlanetRadius > 0f)
            ToggleOrbitSurfaceView();

        if (WasKeyPressed(_keyboard?.backspaceKey, KeyCode.Backspace))
            FaceSun();

        if (WasKeyPressed(_keyboard?.rKey, KeyCode.R))
            FrameStrongestStorm();
    }

    void ToggleOrbitSurfaceView()
    {
        UpdateSunOrbitAxis();

        float distance = Vector3.Distance(transform.position, _lastPlanetCenter);
        bool nearSurface = _surfaceView || distance < _lastPlanetRadius * 1.25f;

        if (nearSurface)
            RepositionCamera(_lastPlanetCenter, _lastPlanetRadius);
        else
            PositionOnSurface(_lastPlanetCenter, _lastPlanetRadius);
    }

    Vector3 GetSunDirectionToSun()
    {
        if (_cachedSunLight == null)
            _cachedSunLight = FindSunLight();

        if (_cachedSunLight == null)
            return Vector3.up;

        return -_cachedSunLight.transform.forward.normalized;
    }

    void UpdateSunOrbitAxis()
    {
        Vector3 toSun = GetSunDirectionToSun();
        if (toSun.sqrMagnitude < 0.0001f)
            return;

        if (_lastSunDirectionToSun.sqrMagnitude > 0.0001f)
        {
            Vector3 axis = Vector3.Cross(_lastSunDirectionToSun.normalized, toSun.normalized);
            if (axis.sqrMagnitude > 0.00000001f)
                _sunOrbitAxis = axis.normalized;
        }

        _lastSunDirectionToSun = toSun.normalized;
    }

    Vector3 GetStableViewUp(Vector3 forward)
    {
        Vector3 up = Vector3.ProjectOnPlane(_sunOrbitAxis, forward);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.ProjectOnPlane(Vector3.up, forward);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.ProjectOnPlane(Vector3.right, forward);

        return up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
    }

    void HandleLook()
    {
        bool rightMousePressed = IsRightMousePressed();
        if (rightMousePressed && !_looking)
            StartLooking();
        else if (!rightMousePressed && _looking)
            StopLooking();

        if (!_looking) return;

        if (_skipNextDelta)
        {
            _skipNextDelta = false;
            ReadMouseDelta();
            return;
        }

        Vector2 delta = ReadMouseDelta();
        if (delta.sqrMagnitude < 0.001f) return;

        float yawAmount = delta.x * LookSensitivity * 0.1f;
        float pitchAmount = -delta.y * LookSensitivity * 0.1f;
        transform.Rotate(Vector3.up, yawAmount, Space.Self);
        transform.Rotate(Vector3.right, pitchAmount, Space.Self);
    }

    void StartLooking()
    {
        _looking = true;
        _skipNextDelta = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void StopLooking()
    {
        _looking = false;
        _skipNextDelta = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void HandleMovement()
    {
        float speed = MoveSpeed;
        if (IsFastPressed())
            speed *= FastMultiplier;

        Vector3 move = Vector3.zero;
        if (IsKeyPressed(_keyboard?.wKey, KeyCode.W)) move += transform.forward;
        if (IsKeyPressed(_keyboard?.sKey, KeyCode.S)) move -= transform.forward;
        if (IsKeyPressed(_keyboard?.aKey, KeyCode.A)) move -= transform.right;
        if (IsKeyPressed(_keyboard?.dKey, KeyCode.D)) move += transform.right;
        if (IsKeyPressed(_keyboard?.eKey, KeyCode.E)) move += transform.up;
        if (IsKeyPressed(_keyboard?.qKey, KeyCode.Q)) move -= transform.up;

        if (move.sqrMagnitude > 0.0001f)
            transform.position += move.normalized * speed * Time.deltaTime;

        float rollSpeed = 60f;
        if (IsKeyPressed(_keyboard?.zKey, KeyCode.Z))
            transform.Rotate(Vector3.forward, rollSpeed * Time.deltaTime, Space.Self);
        if (IsKeyPressed(_keyboard?.cKey, KeyCode.C))
            transform.Rotate(Vector3.forward, -rollSpeed * Time.deltaTime, Space.Self);

        float scroll = ReadScroll();
        if (Mathf.Abs(scroll) > 0.001f)
            transform.position += transform.forward * scroll * ScrollSpeed * Time.deltaTime;
    }

    void FaceSun()
    {
        if (_cachedSunLight == null)
            _cachedSunLight = FindSunLight();
        if (_cachedSunLight == null)
            return;

        Vector3 toSun = GetSunDirectionToSun();
        transform.rotation = Quaternion.LookRotation(toSun, GetStableViewUp(toSun));
    }

    void FrameStrongestStorm()
    {
        if (_lastPlanetRadius <= 0f)
            return;

        if (!ServiceLocator.TryGet<IWeatherProvider>(out var weather))
            return;

        if (!weather.TryFindStrongestPrecipitation(out Vector3 stormPosition, out _))
            return;

        Vector3 stormNormal = (stormPosition - _lastPlanetCenter).normalized;
        if (stormNormal.sqrMagnitude < 0.0001f)
            stormNormal = Vector3.up;

        if (_surfaceView)
        {
            Vector3 tangent = Vector3.Cross(stormNormal, GetSunDirectionToSun());
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(stormNormal, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(stormNormal, Vector3.right);

            tangent.Normalize();
            Vector3 viewNormal = Quaternion.AngleAxis(10f, tangent) * stormNormal;
            transform.position = _lastPlanetCenter + viewNormal.normalized * (_lastPlanetRadius + SurfaceHeight);
            Vector3 lookDir = (stormPosition - transform.position).normalized;
            transform.rotation = Quaternion.LookRotation(lookDir, viewNormal.normalized);
            MoveSpeed = Mathf.Max(0.25f, _lastPlanetRadius * SurfaceSpeedMultiplier);
            ScrollSpeed = Mathf.Max(1f, _lastPlanetRadius * 0.1f);
        }
        else
        {
            float distance = Mathf.Max(_lastPlanetRadius * 1.85f, _lastPlanetRadius + 1000f);
            transform.position = _lastPlanetCenter + stormNormal * distance;
            Vector3 lookDir = (stormPosition - transform.position).normalized;
            transform.rotation = Quaternion.LookRotation(lookDir, GetStableViewUp(lookDir));
            MoveSpeed = Mathf.Max(1f, _lastPlanetRadius * OrbitSpeedMultiplier);
            ScrollSpeed = Mathf.Max(5f, _lastPlanetRadius * 2f);
        }
    }

    Light FindSunLight()
    {
        var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type == LightType.Directional)
                return lights[i];
        }

        return null;
    }

    void RepositionCamera(Vector3 center, float radius)
    {
        float distance = radius * ViewDistanceMultiplier;
        Vector3 toSun = GetSunDirectionToSun();

        transform.position = center + toSun.normalized * distance;
        Vector3 forward = (center - transform.position).normalized;
        transform.rotation = Quaternion.LookRotation(forward, GetStableViewUp(forward));

        MoveSpeed = Mathf.Max(1f, radius * OrbitSpeedMultiplier);
        ScrollSpeed = Mathf.Max(5f, radius * 2f);
        _surfaceView = false;
    }

    void PositionOnSurface(Vector3 center, float radius)
    {
        Vector3 toSun = GetSunDirectionToSun();
        Vector3 sunMotion = Vector3.Cross(_sunOrbitAxis, toSun);
        if (sunMotion.sqrMagnitude < 0.0001f)
            sunMotion = Vector3.Cross(Vector3.up, toSun);
        if (sunMotion.sqrMagnitude < 0.0001f)
            sunMotion = Vector3.Cross(Vector3.right, toSun);

        sunMotion.Normalize();
        float offsetRadians = SurfaceSunriseOffsetDegrees * Mathf.Deg2Rad;
        Vector3 surfaceNormal = (sunMotion * Mathf.Cos(offsetRadians) - toSun * Mathf.Sin(offsetRadians)).normalized;

        float avgElevation = (_lastElevationMin + _lastElevationMax) * 0.5f;
        float baseRadius = Mathf.Abs(_lastElevationMax) > 0.0001f ? radius / (1f + _lastElevationMax) : radius;
        float groundRadius = baseRadius * (1f + avgElevation);

        transform.position = center + surfaceNormal * (groundRadius + SurfaceHeight);

        Vector3 lookDir = Vector3.ProjectOnPlane(toSun, surfaceNormal);
        if (lookDir.sqrMagnitude < 0.01f)
            lookDir = Vector3.ProjectOnPlane(sunMotion, surfaceNormal);

        transform.rotation = Quaternion.LookRotation(lookDir.normalized, surfaceNormal);

        MoveSpeed = Mathf.Max(0.25f, radius * SurfaceSpeedMultiplier);
        ScrollSpeed = Mathf.Max(1f, radius * 0.1f);
        _surfaceView = true;
    }

    bool IsFastPressed()
    {
        return IsKeyPressed(_keyboard?.leftShiftKey, KeyCode.LeftShift)
            || IsKeyPressed(_keyboard?.rightShiftKey, KeyCode.RightShift);
    }

    bool IsRightMousePressed()
    {
        if (_mouse != null && _mouse.rightButton.isPressed)
            return true;

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(1);
#else
        return false;
#endif
    }

    Vector2 ReadMouseDelta()
    {
        if (_mouse != null)
            return _mouse.delta.ReadValue();

#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 20f;
#else
        return Vector2.zero;
#endif
    }

    float ReadScroll()
    {
        if (_mouse != null)
            return _mouse.scroll.ReadValue().y;

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mouseScrollDelta.y * 120f;
#else
        return 0f;
#endif
    }

    static bool IsKeyPressed(KeyControl key, KeyCode legacyKey)
    {
        if (key != null && key.isPressed)
            return true;

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(legacyKey);
#else
        return false;
#endif
    }

    static bool WasKeyPressed(ButtonControl key, KeyCode legacyKey)
    {
        if (key != null && key.wasPressedThisFrame)
            return true;

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(legacyKey);
#else
        return false;
#endif
    }

    void OnGUI()
    {
        if (!ShowDebugOverlay) return;

        GUILayout.BeginArea(new Rect(10, 10, 380, 210));
        GUILayout.Label("Debug Camera");
        GUILayout.Label($"Position: {transform.position.x:F1}, {transform.position.y:F1}, {transform.position.z:F1}");
        GUILayout.Label($"FPS: {1f / Time.unscaledDeltaTime:F0}");

        if (TargetCenter != null)
        {
            Vector3 dirToSurface = (transform.position - TargetCenter.position).normalized;
            var (lat, lon) = CoordinateConverter.UnitSphereToLatLong(dirToSurface);
            GUILayout.Label($"Lat: {lat * Mathf.Rad2Deg:F1}\u00b0 Lon: {lon * Mathf.Rad2Deg:F1}\u00b0");

            float distToCenter = Vector3.Distance(transform.position, TargetCenter.position);
            GUILayout.Label($"Distance to center: {distToCenter:F1}");
        }

        GUILayout.Label("RMB=Look, WASD=Move, Shift=Fast, QE=Up/Down, ZC=Roll");
        GUILayout.Label("Space=Toggle Orbit/Surface, Backspace=Face Sun, R=Frame Storm");

        if (_cachedSunLight == null)
            _cachedSunLight = FindSunLight();
        if (_cachedSunLight != null && _lastPlanetRadius > 0f)
        {
            Vector3 sd = -_cachedSunLight.transform.forward;
            float sunElevation = Vector3.Dot(sd, (transform.position - _lastPlanetCenter).normalized);
            GUILayout.Label($"Sun elevation: {Mathf.Asin(sunElevation) * Mathf.Rad2Deg:F1}\u00b0");
        }

        GUILayout.EndArea();
    }
}
