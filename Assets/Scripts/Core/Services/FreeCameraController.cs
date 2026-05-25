using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

[CommandPrefix("camera")]
public class FreeCameraController : MonoBehaviour, ICameraRigContext, ICameraTeleportRegistry
{
    const string TeleportPlayerPrefsKey = "CameraTeleportLocations.v1";
    const string LastDebugCaptureName = "LastDebugCapture";
    const string LastDebugPrintAlias = "LastDebugPrint";
    const int MaxSavedTeleports = 64;
    const float BuiltInTeleportPlanetRadius = 5293.44f;

    static readonly BuiltInTeleport[] BuiltInTeleports =
    {
        new(
            "Grass Face Seam A",
            new Vector3(3412.10f, -3462.78f, -1654.32f),
            new Vector3(-0.1544f, -0.1294f, 0.9795f)),
        new(
            "Grass Face Seam B",
            new Vector3(3451.27f, -3433.97f, -1646.30f),
            new Vector3(-0.2632f, 0.0824f, 0.9612f)),
        new(
            "Terrain Texture Oblique",
            new Vector3(3565.51f, -3467.42f, -1998.33f),
            new Vector3(-0.3836f, 0.2236f, 0.8960f)),
    };

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
    Light _cachedSunLight;
    IWeatherProvider _cachedWeatherProvider;
    IPlanetSurfaceSampler _cachedPlanet;
    Vector3 _sunOrbitAxis = Vector3.forward;
    Vector3 _lastSunDirectionToSun;
    Camera _camera;
    readonly List<CameraTeleportLocation> _savedTeleports = new();
    readonly List<string> _teleportNameScratch = new();
    CameraTeleportLocation _lastDebugCapture;

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
        LoadTeleports();
        ServiceLocator.Register<ICameraRigContext>(this);
        ServiceLocator.Register<ICameraTeleportRegistry>(this);
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
        ServiceLocator.Unregister<ICameraTeleportRegistry>(this);
        ServiceLocator.Unregister<ICameraRigContext>(this);
    }

    void Start()
    {
        ConfigureCamera();
        Initialize();
    }

    void Initialize()
    {
        ServiceLocator.TryGet(out _cachedWeatherProvider);
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

        float scroll = input.Scroll.ReadValue<Vector2>().y;
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

        if (_cachedWeatherProvider == null)
            return;

        if (!_cachedWeatherProvider.TryFindStrongestPrecipitation(out Vector3 stormPosition, out _))
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
            float surfaceRadius = Mathf.Max(_lastPlanetRadius, _lastSeaLevelRadius);
            var planet = GetPlanet();
            if (planet != null && planet.TryGetSurfaceRadius(viewNormal.normalized, out float sampledRadius))
                surfaceRadius = Mathf.Max(sampledRadius, _lastSeaLevelRadius);

            transform.position = _lastPlanetCenter + viewNormal.normalized * (surfaceRadius + GetSurfaceClearance(_lastPlanetRadius));
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
        ScrollSpeed = Mathf.Max(1f, radius * 0.1f);
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

    [ConsoleCommand("teleport", "Teleport to LastDebugCapture or a saved camera location.", MonoTargetType.Single)]
    string TeleportCmd([CompletionSource(typeof(CameraTeleportNamesProvider))] string name)
    {
        if (!TryFindTeleport(name, out CameraTeleportLocation location))
            return $"unknown camera teleport: '{name}'";

        if (!TryApplyTeleport(location, out string error))
            return error;

        return $"camera teleported: {location.Name}";
    }

    [ConsoleCommand("save-teleport", "Save or overwrite the current camera position and rotation under a name.", MonoTargetType.Single)]
    string SaveTeleportCmd(string name)
    {
        name = NormalizeTeleportName(name);
        if (string.IsNullOrEmpty(name))
            return "camera teleport name cannot be empty";
        if (IsReservedTeleportName(name))
            return $"'{name}' is reserved";

        CameraTeleportLocation location = CaptureTeleport(name);
        int existing = FindSavedTeleportIndex(name);
        bool overwroteExisting = existing >= 0 || TryFindBuiltInTeleport(name, out _);
        if (existing >= 0)
            _savedTeleports[existing] = location;
        else
        {
            if (_savedTeleports.Count >= MaxSavedTeleports)
                _savedTeleports.RemoveAt(0);
            _savedTeleports.Add(location);
        }

        SaveTeleports();
        return $"camera teleport {(overwroteExisting ? "updated" : "saved")}: {name}";
    }

    [ConsoleCommand("remove-teleport", "Remove a saved camera location.", MonoTargetType.Single)]
    string RemoveTeleportCmd([CompletionSource(typeof(CameraTeleportNamesProvider))] string name)
    {
        name = NormalizeTeleportName(name);
        if (IsReservedTeleportName(name))
            return $"'{name}' is reserved and cannot be removed";

        int index = FindSavedTeleportIndex(name);
        if (index < 0)
        {
            if (TryFindBuiltInTeleport(name, out _))
                return $"'{name}' is built in; save the name to override it";
            return $"unknown camera teleport: '{name}'";
        }

        string removed = _savedTeleports[index].Name;
        _savedTeleports.RemoveAt(index);
        SaveTeleports();
        return $"camera teleport removed: {removed}";
    }

    [ConsoleCommand("teleports", "List reserved, saved, and built-in camera locations.", MonoTargetType.Single)]
    string TeleportsCmd()
    {
        IReadOnlyList<string> names = GetTeleportNames();
        return names.Count == 0
            ? "camera teleports: none"
            : $"camera teleports ({names.Count}):\n- {string.Join("\n- ", names)}";
    }

    [ConsoleCommand("surface-view", "Get or toggle surface-following view (vs orbit view).", MonoTargetType.Single)]
    string SurfaceViewCmd(bool? on = null)
    {
        if (on == null) return $"surface view: {SurfaceView}";
        if (on.Value != SurfaceView && _lastPlanetRadius > 0f)
            ToggleOrbitSurfaceView();
        return $"surface view: {SurfaceView}";
    }

    public IReadOnlyList<string> GetTeleportNames()
    {
        EnsureLastDebugCaptureImported();
        _teleportNameScratch.Clear();
        _teleportNameScratch.Add(LastDebugCaptureName);
        _teleportNameScratch.Add(LastDebugPrintAlias);
        for (int i = 0; i < _savedTeleports.Count; i++)
            _teleportNameScratch.Add(_savedTeleports[i].Name);
        for (int i = 0; i < BuiltInTeleports.Length; i++)
        {
            if (FindSavedTeleportIndex(BuiltInTeleports[i].Name) < 0)
                _teleportNameScratch.Add(BuiltInTeleports[i].Name);
        }
        return _teleportNameScratch;
    }

    public void RecordLastDebugCapture()
    {
        _lastDebugCapture = CaptureTeleport(LastDebugCaptureName);
        SaveTeleports();
    }

    CameraTeleportLocation CaptureTeleport(string name)
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

    bool TryFindTeleport(string name, out CameraTeleportLocation location)
    {
        location = null;
        name = NormalizeTeleportName(name);
        if (string.IsNullOrEmpty(name))
            return false;

        if (IsReservedTeleportName(name))
        {
            EnsureLastDebugCaptureImported();
            location = _lastDebugCapture;
            return location != null;
        }

        int index = FindSavedTeleportIndex(name);
        if (index >= 0)
        {
            location = _savedTeleports[index];
            return true;
        }

        return TryFindBuiltInTeleport(name, out location);
    }

    static bool TryFindBuiltInTeleport(string name, out CameraTeleportLocation location)
    {
        for (int i = 0; i < BuiltInTeleports.Length; i++)
        {
            BuiltInTeleport builtIn = BuiltInTeleports[i];
            if (!string.Equals(builtIn.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            Vector3 forward = builtIn.LocalForward.normalized;
            Vector3 up = Vector3.ProjectOnPlane(builtIn.LocalPosition.normalized, forward);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.right, forward);

            location = new CameraTeleportLocation
            {
                Name = builtIn.Name,
                Position = builtIn.LocalPosition,
                Rotation = Quaternion.LookRotation(forward, up.normalized),
                RelativeToTarget = true,
                SurfaceView = true,
                PlanetRadius = BuiltInTeleportPlanetRadius,
            };
            return true;
        }

        location = null;
        return false;
    }

    bool TryApplyTeleport(CameraTeleportLocation location, out string error)
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
            ScrollSpeed = Mathf.Max(1f, radius * 0.1f);
        }
        else
        {
            MoveSpeed = Mathf.Max(1f, radius * OrbitSpeedMultiplier);
            ScrollSpeed = Mathf.Max(5f, radius * 2f);
        }

        return true;
    }

    int FindSavedTeleportIndex(string name)
    {
        for (int i = 0; i < _savedTeleports.Count; i++)
        {
            if (string.Equals(_savedTeleports[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    static string NormalizeTeleportName(string name)
    {
        return string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
    }

    static bool IsReservedTeleportName(string name)
    {
        return string.Equals(name, LastDebugCaptureName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, LastDebugPrintAlias, StringComparison.OrdinalIgnoreCase);
    }

    void LoadTeleports()
    {
        string json = PlayerPrefs.GetString(TeleportPlayerPrefsKey, "");
        if (string.IsNullOrEmpty(json))
            return;

        try
        {
            CameraTeleportData data = JsonUtility.FromJson<CameraTeleportData>(json);
            _savedTeleports.Clear();
            if (data?.Locations != null)
            {
                for (int i = 0; i < data.Locations.Count && _savedTeleports.Count < MaxSavedTeleports; i++)
                {
                    CameraTeleportLocation location = data.Locations[i];
                    if (location != null && !string.IsNullOrWhiteSpace(location.Name)
                        && !IsReservedTeleportName(location.Name))
                    {
                        _savedTeleports.Add(location);
                    }
                }
            }
            _lastDebugCapture = data?.LastDebugCapture;
        }
        catch (Exception ex)
        {
            LoggerProvider.Log(LogLevel.Warning, "CameraTeleport", $"Load failed: {ex.Message}");
        }
    }

    void SaveTeleports()
    {
        try
        {
            var data = new CameraTeleportData
            {
                Locations = new List<CameraTeleportLocation>(_savedTeleports),
                LastDebugCapture = _lastDebugCapture,
            };
            PlayerPrefs.SetString(TeleportPlayerPrefsKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            LoggerProvider.Log(LogLevel.Warning, "CameraTeleport", $"Save failed: {ex.Message}");
        }
    }

    void EnsureLastDebugCaptureImported()
    {
        if (_lastDebugCapture != null)
            return;

        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return;
            string directory = Path.Combine(projectRoot, "local-only", "debug-screenshots");
            if (!Directory.Exists(directory))
                return;

            FileInfo newest = new DirectoryInfo(directory)
                .EnumerateFiles("F10-*.txt", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest == null)
                return;

            Vector3 position = default;
            Vector3 forward = default;
            bool surfaceView = false;
            bool hasPosition = false;
            bool hasForward = false;
            foreach (string line in File.ReadLines(newest.FullName))
            {
                if (line.StartsWith("Position:", StringComparison.Ordinal))
                    hasPosition = TryParseCaptureVector(line.Substring("Position:".Length), out position);
                else if (line.StartsWith("Forward:", StringComparison.Ordinal))
                    hasForward = TryParseCaptureVector(line.Substring("Forward:".Length), out forward);
                else if (line.StartsWith("Surface view:", StringComparison.Ordinal))
                    bool.TryParse(line.Substring("Surface view:".Length).Trim(), out surfaceView);
            }

            if (!hasPosition || !hasForward || forward.sqrMagnitude < 0.0001f)
                return;

            Vector3 center = TargetCenter != null ? TargetCenter.position : Vector3.zero;
            Vector3 up = Vector3.ProjectOnPlane((position - center).normalized, forward.normalized);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.up, forward.normalized);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.right, forward.normalized);

            Quaternion worldRotation = Quaternion.LookRotation(forward.normalized, up.normalized);
            bool relative = TargetCenter != null;
            _lastDebugCapture = new CameraTeleportLocation
            {
                Name = LastDebugCaptureName,
                Position = relative ? TargetCenter.InverseTransformPoint(position) : position,
                Rotation = relative ? Quaternion.Inverse(TargetCenter.rotation) * worldRotation : worldRotation,
                RelativeToTarget = relative,
                SurfaceView = surfaceView,
                PlanetRadius = _lastPlanetRadius,
            };
            SaveTeleports();
        }
        catch (Exception ex)
        {
            LoggerProvider.Log(LogLevel.Warning, "CameraTeleport", $"Could not import latest F10 pose: {ex.Message}");
        }
    }

    static bool TryParseCaptureVector(string text, out Vector3 value)
    {
        value = default;
        string[] parts = text.Split(',');
        if (parts.Length != 3)
            return false;

        NumberStyles style = NumberStyles.Float;
        CultureInfo culture = CultureInfo.InvariantCulture;
        if (!float.TryParse(parts[0].Trim(), style, culture, out float x)
            || !float.TryParse(parts[1].Trim(), style, culture, out float y)
            || !float.TryParse(parts[2].Trim(), style, culture, out float z))
        {
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }

    [Serializable]
    sealed class CameraTeleportLocation
    {
        public string Name;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool RelativeToTarget;
        public bool SurfaceView;
        public float PlanetRadius;
    }

    sealed class BuiltInTeleport
    {
        public readonly string Name;
        public readonly Vector3 LocalPosition;
        public readonly Vector3 LocalForward;

        public BuiltInTeleport(string name, Vector3 localPosition, Vector3 localForward)
        {
            Name = name;
            LocalPosition = localPosition;
            LocalForward = localForward;
        }
    }

    [Serializable]
    sealed class CameraTeleportData
    {
        public List<CameraTeleportLocation> Locations;
        public CameraTeleportLocation LastDebugCapture;
    }
}
