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
        if (_keyboard.aKey.isPressed) move -= transform.right;
        if (_keyboard.dKey.isPressed) move += transform.right;
        if (_keyboard.eKey.isPressed) move += transform.up;
        if (_keyboard.qKey.isPressed) move -= transform.up;

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
        // Place in the sun's orbital plane (XY) so the full sunrise->noon->sunset arc is visible.
        // Sun orbits in XY: noon at +Y, sunrise from one horizon, sunset to the other.
        // Camera at +X on the surface, looking along +Y (toward where sun is at noon),
        // tilted up ~30° to see the sky.
        Vector3 surfaceNormal = Vector3.right; // +X on the equator of the sun's orbit
        Vector3 surfacePos = center + surfaceNormal * (radius + 2f);
        transform.position = surfacePos;

        // "Up" is the surface normal (+X), "forward" looks along the horizon in the sun's orbital plane
        // Look toward +Y (where the sun will be at noon), tilted up 30° from horizon
        Vector3 horizonForward = Vector3.up; // toward noon sun position
        Vector3 forward = Vector3.Slerp(horizonForward, surfaceNormal, 0.15f).normalized; // ~30° above horizon
        transform.rotation = Quaternion.LookRotation(forward, surfaceNormal);

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

        GUILayout.Label("WASD=Move, Shift=Fast, RMB=Look, QE=Up/Down, Space=Reset, Ctrl+Space=Surface");

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
