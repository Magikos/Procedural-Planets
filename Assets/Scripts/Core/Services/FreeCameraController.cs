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
    [Tooltip("Multiplier for camera distance from planet surface. 2.5 = 2.5x planet radius from center.")]
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
    float _yaw;
    float _pitch;

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
    }

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
        InitFromPlanet();
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
                InitFromPlanet();
        }

        // Backspace: face the sun from current position
        if (_keyboard.backspaceKey.wasPressedThisFrame)
        {
            var sunLight = FindAnyObjectByType<Light>();
            if (sunLight != null && sunLight.type == LightType.Directional)
            {
                Vector3 toSun = -sunLight.transform.forward;
                Vector3 up = (_lastPlanetRadius > 0f)
                    ? (transform.position - _lastPlanetCenter).normalized
                    : Vector3.up;
                transform.rotation = Quaternion.LookRotation(toSun, up);
                _pitch = 0;
            }
        }
    }

    void HandleLook()
    {
        if (_mouse.rightButton.wasPressedThisFrame)
        {
            _looking = true;
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

        Vector2 mouseDelta = _mouse.delta.ReadValue();
        _yaw += mouseDelta.x * LookSensitivity * 0.1f;
        _pitch = Mathf.Clamp(_pitch - mouseDelta.y * LookSensitivity * 0.1f, -89f, 89f);

        // Determine "up" — surface normal when near planet, world up otherwise
        Vector3 up = (_lastPlanetRadius > 0f)
            ? (transform.position - _lastPlanetCenter).normalized
            : Vector3.up;

        // Build rotation: yaw around surface up, then pitch
        // Find a stable right vector from the current forward projected onto the horizon plane
        Vector3 baseForward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (baseForward.sqrMagnitude < 0.01f)
            baseForward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;

        Quaternion yawRot = Quaternion.AngleAxis(_yaw, up);
        Vector3 yawedForward = yawRot * baseForward;
        Vector3 right = Vector3.Cross(up, yawedForward).normalized;
        Quaternion pitchRot = Quaternion.AngleAxis(_pitch, right);
        Vector3 finalForward = pitchRot * yawedForward;

        transform.rotation = Quaternion.LookRotation(finalForward, up);
        _yaw = 0; // consumed — yaw is relative per frame
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

        // Shift+A/D = roll
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

    void InitFromPlanet()
    {
        if (!AutoPositionOnGenerate) return;
        RepositionCamera(_lastPlanetCenter, _lastPlanetRadius);
    }

    void RepositionCamera(Vector3 center, float radius)
    {
        float distance = radius * ViewDistanceMultiplier;

        // Position camera on the sunlit side by finding the directional light
        Vector3 viewDir = Vector3.back; // default fallback
        var sunLight = FindAnyObjectByType<Light>();
        if (sunLight != null && sunLight.type == LightType.Directional)
            viewDir = sunLight.transform.forward; // light forward = direction light travels = away from sun

        transform.position = center + viewDir * distance;
        transform.LookAt(center);

        MoveSpeed = radius * 0.5f;
        ScrollSpeed = radius * 2f;
    }

    void PositionOnSurface(Vector3 center, float radius)
    {
        // Find the sun direction to position camera where sunrise is visible
        Vector3 sunDir = Vector3.up;
        var sunLight = FindAnyObjectByType<Light>();
        if (sunLight != null && sunLight.type == LightType.Directional)
            sunDir = -sunLight.transform.forward;

        // Place camera on the terminator (90° from sun) so the sun is at the horizon
        Vector3 toSun = sunDir.normalized;
        Vector3 perpendicular = Vector3.Cross(toSun, Vector3.up).normalized;
        if (perpendicular.sqrMagnitude < 0.01f)
            perpendicular = Vector3.Cross(toSun, Vector3.forward).normalized;

        // Use average elevation to approximate actual ground level
        // radius is max elevation; scale down to average terrain height
        float avgElevation = (_lastElevationMin + _lastElevationMax) * 0.5f;
        float baseRadius = radius / (1 + _lastElevationMax); // recover base planet radius
        float groundRadius = baseRadius * (1 + avgElevation);

        Vector3 surfaceNormal = perpendicular;
        Vector3 surfacePos = center + surfaceNormal * (groundRadius + 2f);
        transform.position = surfacePos;

        // Look toward the sun (which should be at/near the horizon from this position)
        Vector3 lookDir = Vector3.ProjectOnPlane(toSun, surfaceNormal).normalized;
        if (lookDir.sqrMagnitude < 0.01f)
            lookDir = Vector3.ProjectOnPlane(Vector3.up, surfaceNormal).normalized;
        // Tilt up slightly to see the sky
        lookDir = Vector3.Slerp(lookDir, surfaceNormal, 0.1f).normalized;
        transform.rotation = Quaternion.LookRotation(lookDir, surfaceNormal);

        _pitch = 0;
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
            GUILayout.Label($"Lat: {lat * Mathf.Rad2Deg:F1}° Lon: {lon * Mathf.Rad2Deg:F1}°");

            float distToCenter = Vector3.Distance(transform.position, TargetCenter.position);
            GUILayout.Label($"Distance to center: {distToCenter:F1}");
        }

        GUILayout.Label("WASD=Move, Shift+W/S=Fast, Shift+A/D=Roll, QE=Up/Down");
        GUILayout.Label("Space=Orbit, Ctrl+Space=Surface, Backspace=Face Sun");

        // Show time of day if CelestialManager exists (found via brute search since Core can't reference Planet)
        var sunLight = FindAnyObjectByType<Light>();
        if (sunLight != null && sunLight.type == LightType.Directional)
        {
            Vector3 sunDir = -sunLight.transform.forward;
            float sunElevation = Vector3.Dot(sunDir, (transform.position - _lastPlanetCenter).normalized);
            GUILayout.Label($"Sun elevation: {Mathf.Asin(sunElevation) * Mathf.Rad2Deg:F1}°");
        }

        GUILayout.EndArea();
    }
}
