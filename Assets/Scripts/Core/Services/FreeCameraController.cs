using UnityEngine;
using UnityEngine.InputSystem;

[CommandPrefix("camera")]
public class FreeCameraController : MonoBehaviour, ICameraRigContext, ICameraTeleportTarget,
    IWorldServiceRegistrar, IFreeCameraService
{
    [Header("Movement")]
    public float MoveSpeed = 10f;
    public float FastMultiplier = 3f;
    public float OrbitSpeedMultiplier = 0.5f;
    public float SurfaceSpeedMultiplier = 0.02f;

    [Header("Look")]
    public float LookSensitivity = 2f;

    [Header("Auto Position")]
    public float ViewDistanceMultiplier = 2.5f;
    public float SurfaceHeight = 2f;
    [Range(1f, 30f)] public float SurfaceSunriseOffsetDegrees = 8f;
    public bool AutoPositionOnGenerate = true;
    public Transform TargetCenter;

    float _lastPlanetRadius;
    float _lastSeaLevelRadius;
    float _lastElevationMin;
    float _lastElevationMax;
    Vector3 _lastPlanetCenter;

    IInputMapService _input;
    bool _looking;
    bool _skipNextDelta;
    bool _surfaceView;
    ICelestialTimeController _celestial;
    IPlanetSurfaceSampler _cachedPlanet;
    Vector3 _sunOrbitAxis = Vector3.forward;
    Vector3 _lastSunDirectionToSun;
    Camera _camera;
    CameraTeleportStore _teleports;

    public Transform CameraTransform => transform;
    public Camera CameraComponent => _camera != null ? _camera : _camera = GetComponent<Camera>();
    Transform ICameraRigContext.TargetCenter => TargetCenter;
    public bool SurfaceView => _surfaceView;
    public Vector3 PlanetCenter => _lastPlanetCenter;
    public float PlanetRadius => _lastPlanetRadius;
    public float SeaLevelRadius => _lastSeaLevelRadius;
    public float ElevationMin => _lastElevationMin;
    public float ElevationMax => _lastElevationMax;

    void Awake()
    {
        _camera = GetComponent<Camera>();
        _teleports = new CameraTeleportStore(this);
    }

    public void RegisterWorldServices(IWorldContext context)
    {
        context.Register<ICameraRigContext>(this);
        context.Register<IFreeCameraService>(this);
    }

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    }

    void OnDisable()
    {
        StopLooking();
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
    }

    void OnDestroy()
    {
        _teleports?.Dispose();
    }

    void Start()
    {
        ConfigureCamera();
        Initialize();
    }

    void Initialize()
    {
        ServiceLocator.TryGet(out _celestial);
        ServiceLocator.TryGet(out _cachedPlanet);
    }

    IInputMapService GetInput()
    {
        if (_input != null)
            return _input;

        ServiceLocator.TryGet(out _input);
        return _input;
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            StopLooking();
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _lastPlanetCenter = evt.PlanetCenter;
        _lastPlanetRadius = evt.PlanetRadius;
        _lastSeaLevelRadius = evt.SeaLevelRadius;
        _lastElevationMin = evt.ElevationMin;
        _lastElevationMax = evt.ElevationMax;
        if (_cachedPlanet == null)
            ServiceLocator.TryGet(out _cachedPlanet);
        UpdateSunOrbitAxis();

        if (AutoPositionOnGenerate)
            RepositionCamera(_lastPlanetCenter, _lastPlanetRadius);
    }

    void Update()
    {
        UpdateSunOrbitAxis();

        var input = GetInput();
        if (input == null)
            return;

        HandleLook(input);
        HandleMovement(input);
        HandleShortcuts(input);
    }

    void ConfigureCamera()
    {
        if (CameraComponent == null)
            return;

        CameraComponent.clearFlags = CameraClearFlags.SolidColor;
        CameraComponent.backgroundColor = Color.black;
        CameraComponent.farClipPlane = 100000f;
    }

    void HandleShortcuts(IInputMapService input)
    {
        if (input.ToggleOrbit.WasPerformedThisFrame() && _lastPlanetRadius > 0f)
            ToggleOrbitSurfaceView();

        if (input.FaceSun.WasPerformedThisFrame())
            FaceSun();

        if (input.FrameStorm.WasPerformedThisFrame())
        {
            if (ServiceLocator.TryGet<IWeatherProvider>(out var weather) &&
                weather.TryFindStrongestPrecipitation(out Vector3 stormPos, out _))
                FrameWorldTarget(stormPos);
        }
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
        if (_celestial == null)
            ServiceLocator.TryGet(out _celestial);
        return _celestial?.SunDirection ?? Vector3.up;
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

    void HandleLook(IInputMapService input)
    {
        bool rightMousePressed = input.LookHold.IsPressed();
        if (rightMousePressed && !_looking)
            StartLooking();
        else if (!rightMousePressed && _looking)
            StopLooking();

        if (!_looking)
            return;

        if (_skipNextDelta)
        {
            _skipNextDelta = false;
            input.Look.ReadValue<Vector2>();
            return;
        }

        Vector2 delta = input.Look.ReadValue<Vector2>();
        if (delta.sqrMagnitude < 0.001f)
            return;

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

    void HandleMovement(IInputMapService input)
    {
        float speed = MoveSpeed;
        if (input.Sprint.IsPressed())
            speed *= FastMultiplier;

        Vector2 moveAxis = input.Move.ReadValue<Vector2>();
        float verticalAxis = input.VerticalMove.ReadValue<float>();

        Vector3 move = transform.forward * moveAxis.y
                     + transform.right * moveAxis.x
                     + transform.up * verticalAxis;

        if (move.sqrMagnitude > 0.0001f)
            transform.position += move.normalized * speed * Time.deltaTime;

        float rollAxis = input.Roll.ReadValue<float>();
        if (Mathf.Abs(rollAxis) > 0.0001f)
            transform.Rotate(Vector3.forward, 60f * rollAxis * Time.deltaTime, Space.Self);

    }

    void FaceSun()
    {
        Vector3 toSun = GetSunDirectionToSun();
        if (_celestial == null)
            return;
        transform.rotation = Quaternion.LookRotation(toSun, GetStableViewUp(toSun));
    }

    public void FrameWorldTarget(Vector3 worldPosition)
    {
        if (_lastPlanetRadius <= 0f)
            return;

        Vector3 targetNormal = (worldPosition - _lastPlanetCenter).normalized;
        if (targetNormal.sqrMagnitude < 0.0001f)
            targetNormal = Vector3.up;

        if (_surfaceView)
        {
            Vector3 tangent = Vector3.Cross(targetNormal, GetSunDirectionToSun());
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(targetNormal, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(targetNormal, Vector3.right);

            tangent.Normalize();
            Vector3 viewNormal = Quaternion.AngleAxis(10f, tangent) * targetNormal;
            float surfaceRadius = Mathf.Max(_lastPlanetRadius, _lastSeaLevelRadius);
            var planet = GetPlanet();
            if (planet != null && planet.TryGetSurfaceRadius(viewNormal.normalized, out float sampledRadius))
                surfaceRadius = Mathf.Max(sampledRadius, _lastSeaLevelRadius);

            transform.position = _lastPlanetCenter + viewNormal.normalized * (surfaceRadius + GetSurfaceClearance(_lastPlanetRadius));
            Vector3 lookDir = (worldPosition - transform.position).normalized;
            transform.rotation = Quaternion.LookRotation(lookDir, viewNormal.normalized);
            MoveSpeed = Mathf.Max(0.25f, _lastPlanetRadius * SurfaceSpeedMultiplier);
        }
        else
        {
            float distance = Mathf.Max(_lastPlanetRadius * 1.85f, _lastPlanetRadius + 1000f);
            transform.position = _lastPlanetCenter + targetNormal * distance;
            Vector3 lookDir = (worldPosition - transform.position).normalized;
            transform.rotation = Quaternion.LookRotation(lookDir, GetStableViewUp(lookDir));
            MoveSpeed = Mathf.Max(1f, _lastPlanetRadius * OrbitSpeedMultiplier);
        }
    }

    IPlanetSurfaceSampler GetPlanet()
    {
        return _cachedPlanet;
    }

    void RepositionCamera(Vector3 center, float radius)
    {
        float distance = radius * ViewDistanceMultiplier;
        Vector3 toSun = GetSunDirectionToSun();

        transform.position = center + toSun.normalized * distance;
        Vector3 forward = (center - transform.position).normalized;
        transform.rotation = Quaternion.LookRotation(forward, GetStableViewUp(forward));

        MoveSpeed = Mathf.Max(1f, radius * OrbitSpeedMultiplier);
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

        float groundRadius = Mathf.Max(radius, _lastSeaLevelRadius);
        var planet = GetPlanet();
        if (planet != null && planet.TryGetSurfaceRadius(surfaceNormal, out float sampledRadius))
            groundRadius = Mathf.Max(sampledRadius, _lastSeaLevelRadius);

        transform.position = center + surfaceNormal * (groundRadius + GetSurfaceClearance(radius));

        Vector3 lookDir = Vector3.ProjectOnPlane(toSun, surfaceNormal);
        if (lookDir.sqrMagnitude < 0.01f)
            lookDir = Vector3.ProjectOnPlane(sunMotion, surfaceNormal);

        transform.rotation = Quaternion.LookRotation(lookDir.normalized, surfaceNormal);

        MoveSpeed = Mathf.Max(0.25f, radius * SurfaceSpeedMultiplier);
        _surfaceView = true;
    }

    float GetSurfaceClearance(float radius)
    {
        return Mathf.Max(SurfaceHeight, Mathf.Max(4f, radius * 0.0012f));
    }

    // --- Console commands -------------------------------------------------

    [ConsoleCommand("speed", "Get or set camera movement speed.", MonoTargetType.Single)]
    string SpeedCmd(float? value = null)
    {
        if (value == null) return $"camera speed: {MoveSpeed:F2}";
        MoveSpeed = Mathf.Max(0f, value.Value);
        return $"camera speed: {MoveSpeed:F2}";
    }

    [ConsoleCommand("sensitivity", "Get or set camera look sensitivity.", MonoTargetType.Single)]
    string SensitivityCmd(float? value = null)
    {
        if (value == null) return $"camera sensitivity: {LookSensitivity:F2}";
        LookSensitivity = Mathf.Max(0f, value.Value);
        return $"camera sensitivity: {LookSensitivity:F2}";
    }

    [ConsoleCommand("fast-multiplier", "Get or set the Shift-sprint speed multiplier.", MonoTargetType.Single)]
    string FastMultCmd(float? value = null)
    {
        if (value == null) return $"camera fast multiplier: {FastMultiplier:F2}";
        FastMultiplier = Mathf.Max(1f, value.Value);
        return $"camera fast multiplier: {FastMultiplier:F2}";
    }

    [ConsoleCommand("position", "Print the camera's current world position.", MonoTargetType.Single)]
    string PositionCmd()
    {
        Vector3 p = transform.position;
        return $"camera position: ({p.x:F2}, {p.y:F2}, {p.z:F2})";
    }

    [ConsoleCommand("look-at", "Set camera world position and aim at a world target.", MonoTargetType.Single)]
    string LookAtCmd(Vector3 position, Vector3 target)
    {
        Vector3 forward = target - position;
        if (forward.sqrMagnitude < 0.0001f)
            return "camera look-at target must differ from position";

        StopLooking();
        transform.position = position;
        Vector3 viewForward = forward.normalized;
        Vector3 viewUp = GetStableViewUp(viewForward);
        if (TargetCenter != null)
        {
            Vector3 radialUp = Vector3.ProjectOnPlane(position - TargetCenter.position, viewForward);
            if (radialUp.sqrMagnitude > 0.0001f)
                viewUp = radialUp.normalized;
        }
        transform.rotation = Quaternion.LookRotation(viewForward, viewUp);
        _surfaceView = false;
        _skipNextDelta = true;

        return $"camera look-at: position=({position.x:F2}, {position.y:F2}, {position.z:F2}) target=({target.x:F2}, {target.y:F2}, {target.z:F2})";
    }

    [ConsoleCommand("surface-view", "Get or toggle surface-following view (vs orbit view).", MonoTargetType.Single)]
    string SurfaceViewCmd(bool? on = null)
    {
        if (on == null) return $"surface view: {SurfaceView}";
        if (on.Value != SurfaceView && _lastPlanetRadius > 0f)
            ToggleOrbitSurfaceView();
        return $"surface view: {SurfaceView}";
    }

    public CameraTeleportLocation CaptureLocation(string name)
    {
        bool relative = TargetCenter != null;
        Vector3 position = relative
            ? TargetCenter.InverseTransformPoint(transform.position)
            : transform.position;
        Quaternion rotation = relative
            ? Quaternion.Inverse(TargetCenter.rotation) * transform.rotation
            : transform.rotation;

        return new CameraTeleportLocation
        {
            Name = name,
            Position = position,
            Rotation = rotation,
            RelativeToTarget = relative,
            SurfaceView = _surfaceView,
            PlanetRadius = _lastPlanetRadius,
        };
    }

    public bool TryApply(CameraTeleportLocation location, out string error)
    {
        error = null;
        if (location == null)
        {
            error = "camera teleport is unavailable";
            return false;
        }
        if (location.RelativeToTarget && TargetCenter == null)
        {
            error = "camera teleport requires a generated planet target";
            return false;
        }

        StopLooking();
        transform.position = location.RelativeToTarget
            ? TargetCenter.TransformPoint(location.Position)
            : location.Position;
        transform.rotation = location.RelativeToTarget
            ? TargetCenter.rotation * location.Rotation
            : location.Rotation;
        _surfaceView = location.SurfaceView;
        _skipNextDelta = true;

        float radius = _lastPlanetRadius > 0f ? _lastPlanetRadius : location.PlanetRadius;
        if (_surfaceView)
        {
            MoveSpeed = Mathf.Max(0.25f, radius * SurfaceSpeedMultiplier);
        }
        else
        {
            MoveSpeed = Mathf.Max(1f, radius * OrbitSpeedMultiplier);
        }

        return true;
    }

}
