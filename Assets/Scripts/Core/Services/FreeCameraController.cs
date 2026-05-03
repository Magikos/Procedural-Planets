using UnityEngine;
using UnityEngine.InputSystem;

public class FreeCameraController : MonoBehaviour
{
    [Header("Movement")]
    public float MoveSpeed = 10f;
    public float FastMultiplier = 3f;
    public float ScrollSpeed = 50f;

    [Header("Look")]
    public float LookSensitivity = 2f;

    [Header("Auto Position")]
    public float ViewDistanceMultiplier = 2.5f;
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

    void OnEnable() => EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    void OnDisable() => EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);

    void Start()
    {
        _mouse = Mouse.current;
        _keyboard = Keyboard.current;

        var cam = GetComponent<Camera>();
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.farClipPlane = 100000f;
        }
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _lastPlanetCenter = evt.PlanetCenter;
        _lastPlanetRadius = evt.PlanetRadius;
        _lastElevationMin = evt.ElevationMin;
        _lastElevationMax = evt.ElevationMax;
        if (AutoPositionOnGenerate)
            RepositionCamera(_lastPlanetCenter, _lastPlanetRadius);
    }

    void Update()
    {
        if (_mouse == null || _keyboard == null) return;

        HandleLook();
        HandleMovement();

        if (_keyboard.spaceKey.wasPressedThisFrame && _lastPlanetRadius > 0f)
        {
            if (_keyboard.leftCtrlKey.isPressed)
                PositionOnSurface(_lastPlanetCenter, _lastPlanetRadius);
            else
                RepositionCamera(_lastPlanetCenter, _lastPlanetRadius);
        }

        if (_keyboard.backspaceKey.wasPressedThisFrame)
        {
            var sunLight = FindAnyObjectByType<Light>();
            if (sunLight != null && sunLight.type == LightType.Directional)
            {
                Vector3 toSun = -sunLight.transform.forward;
                transform.rotation = Quaternion.LookRotation(toSun, GetUp());
            }
        }
    }

    Vector3 GetUp()
    {
        return (_lastPlanetRadius > 0f)
            ? (transform.position - _lastPlanetCenter).normalized
            : Vector3.up;
    }

    void HandleLook()
    {
        if (_mouse.rightButton.wasPressedThisFrame)
        {
            _looking = true;
            _skipNextDelta = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (_mouse.rightButton.wasReleasedThisFrame)
        {
            _looking = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (!_looking) return;

        if (_skipNextDelta)
        {
            _skipNextDelta = false;
            _mouse.delta.ReadValue(); // consume the snap delta
            return;
        }

        Vector2 delta = _mouse.delta.ReadValue();
        if (delta.sqrMagnitude < 0.001f) return;

        float yawAmount = delta.x * LookSensitivity * 0.1f;
        float pitchAmount = -delta.y * LookSensitivity * 0.1f;

        Vector3 up = GetUp();

        // Yaw: rotate around up axis
        transform.RotateAround(transform.position, up, yawAmount);

        // Pitch: rotate around local right axis, clamped
        Vector3 right = transform.right;
        float currentPitch = Vector3.SignedAngle(
            Vector3.ProjectOnPlane(transform.forward, up).normalized,
            transform.forward, right);

        float newPitch = Mathf.Clamp(currentPitch + pitchAmount, -89f, 89f);
        float actualPitch = newPitch - currentPitch;
        transform.RotateAround(transform.position, right, actualPitch);
    }

    void HandleMovement()
    {
        float speed = MoveSpeed;
        if (_keyboard.leftShiftKey.isPressed) speed *= FastMultiplier;

        Vector3 move = Vector3.zero;
        if (_keyboard.wKey.isPressed) move += transform.forward;
        if (_keyboard.sKey.isPressed) move -= transform.forward;
        if (!_keyboard.leftShiftKey.isPressed && _keyboard.aKey.isPressed) move -= transform.right;
        if (!_keyboard.leftShiftKey.isPressed && _keyboard.dKey.isPressed) move += transform.right;
        if (_keyboard.eKey.isPressed) move += transform.up;
        if (_keyboard.qKey.isPressed) move -= transform.up;

        float rollSpeed = 60f;
        if (_keyboard.leftShiftKey.isPressed && _keyboard.aKey.isPressed)
            transform.Rotate(Vector3.forward, rollSpeed * Time.deltaTime, Space.Self);
        if (_keyboard.leftShiftKey.isPressed && _keyboard.dKey.isPressed)
            transform.Rotate(Vector3.forward, -rollSpeed * Time.deltaTime, Space.Self);

        transform.position += move.normalized * speed * Time.deltaTime;

        float scroll = _mouse.scroll.ReadValue().y;
        if (scroll != 0)
            transform.position += transform.forward * scroll * ScrollSpeed * Time.deltaTime;
    }

    void RepositionCamera(Vector3 center, float radius)
    {
        float distance = radius * ViewDistanceMultiplier;

        Vector3 viewDir = Vector3.back;
        var sunLight = FindAnyObjectByType<Light>();
        if (sunLight != null && sunLight.type == LightType.Directional)
            viewDir = sunLight.transform.forward;

        transform.position = center + viewDir * distance;
        transform.LookAt(center);

        MoveSpeed = radius * 0.5f;
        ScrollSpeed = radius * 2f;
    }

    void PositionOnSurface(Vector3 center, float radius)
    {
        Vector3 sunDir = Vector3.up;
        var sunLight = FindAnyObjectByType<Light>();
        if (sunLight != null && sunLight.type == LightType.Directional)
            sunDir = -sunLight.transform.forward;

        Vector3 toSun = sunDir.normalized;
        Vector3 perpendicular = Vector3.Cross(toSun, Vector3.up).normalized;
        if (perpendicular.sqrMagnitude < 0.01f)
            perpendicular = Vector3.Cross(toSun, Vector3.forward).normalized;

        float avgElevation = (_lastElevationMin + _lastElevationMax) * 0.5f;
        float baseRadius = (_lastElevationMax != 0f) ? radius / (1 + _lastElevationMax) : radius;
        float groundRadius = baseRadius * (1 + avgElevation);

        Vector3 surfaceNormal = perpendicular;
        transform.position = center + surfaceNormal * (groundRadius + 2f);

        Vector3 lookDir = Vector3.ProjectOnPlane(toSun, surfaceNormal).normalized;
        if (lookDir.sqrMagnitude < 0.01f)
            lookDir = Vector3.ProjectOnPlane(Vector3.up, surfaceNormal).normalized;
        lookDir = Vector3.Slerp(lookDir, surfaceNormal, 0.1f).normalized;
        transform.rotation = Quaternion.LookRotation(lookDir, surfaceNormal);

        MoveSpeed = radius * 0.02f;
        ScrollSpeed = radius * 0.1f;
    }

    void OnGUI()
    {
        if (!ShowDebugOverlay) return;

        GUILayout.BeginArea(new Rect(10, 10, 350, 200));
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

        GUILayout.Label("WASD=Move, Shift+W/S=Fast, Shift+A/D=Roll, QE=Up/Down");
        GUILayout.Label("Space=Orbit, Ctrl+Space=Surface, Backspace=Face Sun");

        var sunLight = FindAnyObjectByType<Light>();
        if (sunLight != null && sunLight.type == LightType.Directional)
        {
            Vector3 sd = -sunLight.transform.forward;
            float sunElevation = Vector3.Dot(sd, (transform.position - _lastPlanetCenter).normalized);
            GUILayout.Label($"Sun elevation: {Mathf.Asin(sunElevation) * Mathf.Rad2Deg:F1}\u00b0");
        }

        GUILayout.EndArea();
    }
}
