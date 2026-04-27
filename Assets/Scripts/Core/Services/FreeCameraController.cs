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

    float _yaw;
    float _pitch;
    bool _looking;
    float _lastPlanetRadius;
    Vector3 _lastPlanetCenter;

    Mouse _mouse;
    Keyboard _keyboard;

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
        _yaw = transform.eulerAngles.y;
        _pitch = transform.eulerAngles.x;
        _mouse = Mouse.current;
        _keyboard = Keyboard.current;

        var cam = GetComponent<Camera>();
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
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
            InitFromPlanet();
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
        _pitch -= mouseDelta.y * LookSensitivity * 0.1f;
        _pitch = Mathf.Clamp(_pitch, -90f, 90f);
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
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
        transform.position = center + Vector3.back * distance;
        transform.LookAt(center);

        _yaw = transform.eulerAngles.y;
        _pitch = transform.eulerAngles.x;

        MoveSpeed = radius * 0.5f;
        ScrollSpeed = radius * 2f;
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

        GUILayout.Label("WASD=Move, Shift=Fast, RMB=Look, QE=Up/Down, Space=Reset");
        GUILayout.EndArea();
    }
}
