using UnityEngine;

[CommandPrefix("time")]
public class CelestialManager : MonoBehaviour, ICelestialTimeController
{
    [Header("References")]
    public Light SunLight;
    public Transform MoonTransform;
    public Transform PlanetCenter;

    [Header("Sun")]
    [Tooltip("Real seconds per full day/night cycle")]
    public float DayLengthSeconds = 120f;
    [Range(-45f, 45f), Tooltip("Axial tilt in degrees — affects how high the sun gets")]
    public float AxialTilt = 23.5f;

    [Header("Moon")]
    [Tooltip("How many days per full moon cycle")]
    public float MoonCycleDays = 8f;
    [Tooltip("Distance from planet center (auto-set from planet radius)")]
    public float MoonOrbitRadius;
    [Range(0f, 15f), Tooltip("Moon orbital plane tilt relative to sun")]
    public float MoonInclination = 5f;

    [Header("State")]
    [Range(0f, 1f), Tooltip("Starting time of day: 0=midnight, 0.25=sunrise, 0.5=noon, 0.75=sunset")]
    public float StartTimeOfDay = 0.25f;

    [Header("Debug")]
    [Tooltip("Debug control used to inspect lighting and water reflections without the sun moving.")]
    public bool FreezeTime;

    [Header("Ambient Light")]
    [Range(0f, 1f)] public float AmbientMaxIntensity = 0.15f;
    [Range(0f, 0.1f)] public float AmbientMinIntensity = 0.03f;

    [Header("Stars")]
    [Range(10, 100)] public float StarDensity = 40f;
    [Range(0.5f, 5f)] public float StarBrightness = 1f;

    float _timeOfDay;
    float _moonCycleProgress;
    float _planetRadius;
    bool _wasDay = true;
    int _lastMoonPhaseIndex = -1;
    Transform _cachedMoonRoot;
    Renderer[] _moonRenderers;
    bool[] _moonRendererDefaults;
    Camera _cachedMainCamera;

    static readonly int _nightAmbientIntensityId = Shader.PropertyToID(ShaderGlobalIds.NightAmbientIntensity);
    static readonly int _starSeedId = Shader.PropertyToID(ShaderGlobalIds.StarSeed);
    static readonly int _starDensityId = Shader.PropertyToID(ShaderGlobalIds.StarDensity);
    static readonly int _starBrightnessId = Shader.PropertyToID(ShaderGlobalIds.StarBrightness);
    static readonly int _moonParamsId = Shader.PropertyToID(ShaderGlobalIds.MoonParams);
    static readonly int _moonIntensityId = Shader.PropertyToID(ShaderGlobalIds.MoonIntensity);
    const float MoonCausticMaxIntensity = 0.015f;

    public float TimeOfDay => _timeOfDay;
    public bool IsTimeFrozen => FreezeTime;
    public Vector3 SunDirection => SunLight != null ? -SunLight.transform.forward : Vector3.up;

    public bool IsDayAt(Vector3 worldPosition)
    {
        Vector3 center = PlanetCenter != null ? PlanetCenter.position : Vector3.zero;
        Vector3 surfaceNormal = (worldPosition - center).normalized;
        return Vector3.Dot(surfaceNormal, SunDirection) > 0f;
    }

    /// <summary>-1 = full moon, 0 = half, +1 = new moon.</summary>
    public float MoonPhase { get; private set; }

    /// <summary>0-7 discrete phase index.</summary>
    public int MoonPhaseIndex => Mathf.FloorToInt((_moonCycleProgress % 1f) * 8f) % 8;

    /// <summary>0 at new moon, 1 at full moon. Useful for magic intensity.</summary>
    public float MoonFullness => (1f - MoonPhase) * 0.5f;

    /// <summary>0-1 progress through the current season cycle.</summary>
    public float SeasonProgress => 0f;

    void Awake()
    {
        ServiceLocator.Register<ICelestialTimeController>(this);
    }

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
        _timeOfDay = StartTimeOfDay;
        UpdateSun(0f);
        UpdateMoon(0f);
        UpdateAmbient();
        UpdateMoonShaderGlobals();
    }

    void OnDestroy()
    {
        ServiceLocator.Unregister<ICelestialTimeController>(this);
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        Initialize();
    }

    void Initialize()
    {
        if (PlanetCenter == null)
        {
            IPlanet planetService = ServiceLocator.Get<IPlanet>();
            PlanetCenter = planetService.Transform;
        }

        if (PlanetCenter == null) return;

        IPlanet planet = ServiceLocator.Get<IPlanet>();
        if (planet == null || planet.LastGeneratedRadius <= 0f) return;

        _planetRadius = planet.LastGeneratedRadius;
        MoonOrbitRadius = _planetRadius * 3f;

        Shader.SetGlobalFloat(_starSeedId, planet.Seed * 0.01f);
    }

    void Update()
    {
        float dt = FreezeTime ? 0f : Time.deltaTime;
        if (DayLengthSeconds > 0f)
        {
            UpdateSun(dt);
            UpdateMoon(dt);
        }
        else
        {
            UpdateMoonVisibility();
        }

        UpdateAmbient();
        UpdateMoonShaderGlobals();
        FireEvents();
    }

    public void ToggleTimeFrozen()
    {
        FreezeTime = !FreezeTime;
    }

    // --- Console commands -------------------------------------------------

    [ConsoleCommand("freeze", "Get or set sun freeze state. No-arg reads, true/false sets.", MonoTargetType.Single)]
    string FreezeCmd(bool? frozen = null)
    {
        if (frozen == null) return $"sun frozen: {FreezeTime}";
        FreezeTime = frozen.Value;
        return $"sun frozen: {FreezeTime}";
    }

    [ConsoleCommand("speed", "Get or set day length in real seconds.", MonoTargetType.Single)]
    string SpeedCmd(float? value = null)
    {
        if (value == null) return $"day length: {DayLengthSeconds:F1}s";
        DayLengthSeconds = Mathf.Max(0.1f, value.Value);
        return $"day length: {DayLengthSeconds:F1}s";
    }

    [ConsoleCommand("set-local", "Set time of day relative to the camera position (0=midnight, 0.25=sunrise, 0.5=noon, 0.75=sunset).", MonoTargetType.Single)]
    string SetLocalCmd(float fraction)
    {
        if (PlanetCenter == null) return "no planet center set";
        Camera cam = GetViewCamera();
        if (cam == null) return "no camera available";

        Vector3 camDir = (cam.transform.position - PlanetCenter.position).normalized;
        if (camDir.sqrMagnitude < 0.0001f) return "camera at planet center — local time undefined";

        // Un-tilt: the sun orbits in the world XY plane in the un-tilted frame.
        Quaternion tilt = Quaternion.Euler(AxialTilt, 0f, 0f);
        Vector3 untilted = Quaternion.Inverse(tilt) * camDir;

        // Project onto the sun's orbital plane (XY). At the poles, no projection exists → local time undefined.
        Vector3 inPlane = new Vector3(untilted.x, untilted.y, 0f);
        if (inPlane.sqrMagnitude < 0.0001f) return "camera is at a celestial pole — local time undefined";
        inPlane.Normalize();

        // Inverse of UpdateSun(): sunDir = (sin(2πT), -cos(2πT), 0); sun overhead when sunDir == camDir.
        float tNoon = Mathf.Atan2(inPlane.x, -inPlane.y) / (2f * Mathf.PI);
        if (tNoon < 0f) tNoon += 1f;

        fraction = ((fraction % 1f) + 1f) % 1f;
        _timeOfDay = (tNoon + (fraction - 0.5f) + 1f) % 1f;
        UpdateSun(0f);
        return $"local time at camera: {fraction:F2} (global time-of-day = {_timeOfDay:F2})";
    }

    [ConsoleCommand("moon-phase", "Get or set discrete moon phase index (0-7). Wraps modulo 8.", MonoTargetType.Single)]
    string MoonPhaseCmd(int? index = null)
    {
        if (index == null) return $"moon phase: {MoonPhaseIndex} (cycle progress {_moonCycleProgress:F2})";
        int i = ((index.Value % 8) + 8) % 8;
        _moonCycleProgress = (i + 0.5f) / 8f;  // place in middle of phase bucket
        UpdateMoon(0f);
        return $"moon phase: {MoonPhaseIndex}";
    }

    void UpdateSun(float dt)
    {
        _timeOfDay = (_timeOfDay + dt / DayLengthSeconds) % 1f;

        float sunAngle = _timeOfDay * 360f;
        Vector3 center = PlanetCenter != null ? PlanetCenter.position : Vector3.zero;

        Quaternion tilt = Quaternion.Euler(AxialTilt, 0f, 0f);
        Vector3 sunDir = tilt * new Vector3(
            Mathf.Sin(sunAngle * Mathf.Deg2Rad),
            -Mathf.Cos(sunAngle * Mathf.Deg2Rad),
            0f
        );

        if (SunLight != null)
        {
            SunLight.transform.position = center - sunDir * (_planetRadius > 0 ? _planetRadius * 10f : 1000f);
            SunLight.transform.LookAt(center);
        }
    }

    void UpdateMoon(float dt)
    {
        if (MoonTransform == null)
            return;

        if (MoonOrbitRadius <= 0f)
        {
            SetMoonVisible(false);
            return;
        }

        float moonDayLength = DayLengthSeconds * MoonCycleDays;
        if (moonDayLength <= 0f) return;

        _moonCycleProgress = (_moonCycleProgress + dt / moonDayLength) % 1f;

        float moonAngle = _moonCycleProgress * 360f;
        Vector3 center = PlanetCenter != null ? PlanetCenter.position : Vector3.zero;

        Quaternion inclination = Quaternion.Euler(MoonInclination, 0f, 0f);
        Vector3 moonDir = inclination * new Vector3(
            Mathf.Sin(moonAngle * Mathf.Deg2Rad),
            0f,
            Mathf.Cos(moonAngle * Mathf.Deg2Rad)
        );

        MoonTransform.position = center + moonDir * MoonOrbitRadius;
        MoonTransform.LookAt(center);

        Vector3 toMoon = (MoonTransform.position - center).normalized;
        MoonPhase = Vector3.Dot(SunDirection, toMoon);
        UpdateMoonVisibility();
    }

    void CacheMoonRenderers()
    {
        if (MoonTransform == null)
        {
            _cachedMoonRoot = null;
            _moonRenderers = null;
            _moonRendererDefaults = null;
            return;
        }

        if (_cachedMoonRoot == MoonTransform && _moonRenderers != null)
            return;

        _cachedMoonRoot = MoonTransform;
        _moonRenderers = MoonTransform.GetComponentsInChildren<Renderer>(true);
        _moonRendererDefaults = new bool[_moonRenderers.Length];
        for (int i = 0; i < _moonRenderers.Length; i++)
            _moonRendererDefaults[i] = _moonRenderers[i] != null && _moonRenderers[i].enabled;
    }

    void SetMoonVisible(bool visible)
    {
        CacheMoonRenderers();
        if (_moonRenderers == null)
            return;

        for (int i = 0; i < _moonRenderers.Length; i++)
        {
            if (_moonRenderers[i] != null)
                _moonRenderers[i].enabled = visible && _moonRendererDefaults[i];
        }
    }

    void UpdateMoonVisibility()
    {
        if (MoonTransform == null)
            return;

        Camera viewCamera = GetViewCamera();
        if (viewCamera == null || PlanetCenter == null || _planetRadius <= 0f)
        {
            SetMoonVisible(MoonOrbitRadius > 0f);
            return;
        }

        Vector3 center = PlanetCenter.position;
        Vector3 cameraOffset = viewCamera.transform.position - center;
        float cameraRadius = cameraOffset.magnitude;
        if (cameraRadius <= 0.0001f)
        {
            SetMoonVisible(false);
            return;
        }

        Vector3 toMoon = MoonTransform.position - viewCamera.transform.position;
        float moonDistance = toMoon.magnitude;
        if (moonDistance <= 0.0001f)
        {
            SetMoonVisible(false);
            return;
        }

        Vector3 moonDir = toMoon / moonDistance;
        bool visible;
        if (cameraRadius <= _planetRadius * 1.15f)
        {
            Vector3 localUp = cameraOffset / cameraRadius;
            visible = Vector3.Dot(localUp, moonDir) > -0.025f;
        }
        else
        {
            visible = !RayIntersectsPlanet(viewCamera.transform.position, moonDir, moonDistance);
        }

        SetMoonVisible(visible);
    }

    Camera GetViewCamera()
    {
        if (_cachedMainCamera != null && _cachedMainCamera.isActiveAndEnabled)
            return _cachedMainCamera;

        _cachedMainCamera = Camera.main;
        return _cachedMainCamera;
    }

    bool RayIntersectsPlanet(Vector3 rayOrigin, Vector3 rayDirection, float maxDistance)
    {
        Vector3 oc = rayOrigin - PlanetCenter.position;
        float radius = Mathf.Max(_planetRadius, 0f);
        float b = Vector3.Dot(oc, rayDirection);
        float c = Vector3.Dot(oc, oc) - radius * radius;
        float discriminant = b * b - c;
        if (discriminant <= 0f)
            return false;

        float root = Mathf.Sqrt(discriminant);
        float nearHit = -b - root;
        float farHit = -b + root;
        const float epsilon = 0.05f;
        return (nearHit > epsilon && nearHit < maxDistance)
            || (farHit > epsilon && farHit < maxDistance);
    }

    void FireEvents()
    {
        Vector3 center = PlanetCenter != null ? PlanetCenter.position : Vector3.zero;
        bool isDay = IsDayAt(center + Vector3.forward * _planetRadius);
        if (isDay != _wasDay)
        {
            _wasDay = isDay;
            EventBus<DayNightChangedEvent>.Raise(new DayNightChangedEvent(_timeOfDay, isDay));
        }

        int phaseIdx = MoonPhaseIndex;
        if (phaseIdx != _lastMoonPhaseIndex)
        {
            _lastMoonPhaseIndex = phaseIdx;
            EventBus<MoonPhaseChangedEvent>.Raise(new MoonPhaseChangedEvent(MoonPhase));
        }
    }

    void UpdateAmbient()
    {
        if (SunLight == null) return;

        // Moon influence: how close is the moon to the anti-sun direction?
        float moonInfluence = 0f;
        if (MoonTransform != null && PlanetCenter != null)
        {
            Vector3 center = PlanetCenter.position;
            Vector3 toMoon = (MoonTransform.position - center).normalized;
            float alignment = Vector3.Dot(toMoon, SunDirection);
            // Moon opposite sun (full moon) = alignment ~ -1 → influence = 1
            // Moon same side as sun (new moon) = alignment ~ +1 → influence = 0
            moonInfluence = Mathf.Clamp01(-alignment);
        }

        float intensity = Mathf.Lerp(AmbientMinIntensity, AmbientMaxIntensity, moonInfluence);
        Shader.SetGlobalFloat(_nightAmbientIntensityId, intensity);

        Shader.SetGlobalFloat(_starDensityId, StarDensity);
        Shader.SetGlobalFloat(_starBrightnessId, StarBrightness);
    }

    void UpdateMoonShaderGlobals()
    {
        if (MoonTransform == null || PlanetCenter == null || MoonOrbitRadius <= 0f)
        {
            Shader.SetGlobalVector(_moonParamsId, Vector4.zero);
            Shader.SetGlobalFloat(_moonIntensityId, 0f);
            return;
        }

        Vector3 toMoon = MoonTransform.position - PlanetCenter.position;
        if (toMoon.sqrMagnitude <= 0.0001f)
        {
            Shader.SetGlobalVector(_moonParamsId, Vector4.zero);
            Shader.SetGlobalFloat(_moonIntensityId, 0f);
            return;
        }

        Shader.SetGlobalVector(_moonParamsId, toMoon.normalized);
        Shader.SetGlobalFloat(_moonIntensityId, Mathf.Clamp01(MoonFullness) * MoonCausticMaxIntensity);
    }
}
