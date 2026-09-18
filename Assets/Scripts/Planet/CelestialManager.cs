using UnityEngine;

public class CelestialManager : MonoBehaviour, ICelestialTimeController, IWorldServiceRegistrar,
    IWorldSettingsRegistrar, IEarlyInitialize, IWorldTeardown
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

    [Header("Shadows")]
    [Tooltip("Sun elevation (deg above the viewer's local horizon) at/above which cast shadows are full strength. Below it they fade toward ShadowGrazingStrength so grazing dawn/dusk shadows stop reading as hard black dashes across the ground.")]
    public float ShadowFadeElevationDeg = 22f;
    [Range(0f, 1f), Tooltip("Cast-shadow strength when the sun sits on the viewer's horizon. 1 = no fade; lower = softer grazing shadows. Distant shadows are kept — just lighter — so nothing reads as un-shadowed.")]
    public float ShadowGrazingStrength = 0.35f;

    public float MoonOrbitRadius => _planetRadius * (_moonSettings?.Distance ?? 0f);
    public float MoonCycleProgress => _moonCycleProgress;
    public bool IsMoonPhaseHeld { get; private set; }
    public MoonDto MoonSettingsSnapshot => _moonSettings;

    static readonly System.Type[] RequiredSettings = { typeof(MoonDto) };
    static readonly System.Type[] EarlyDeps = { typeof(SceneBootstrap) };
    public System.Collections.Generic.IReadOnlyList<System.Type> RequiredSettingsTypes => RequiredSettings;
    public System.Collections.Generic.IReadOnlyList<System.Type> EarlyDependencies => EarlyDeps;

    MoonDto _moonSettings;
    ISettingsService _settingsService;
    CelestialCommands _commands;
    Material _moonMaterial;
    Material[][] _moonDefaultMaterials;
    bool _initialized;
    bool _appearanceDirty;
    Vector3 _moonDirection;
    Vector3 _sunDirection = Vector3.up;
    Vector3? _sunDirectionOverride;

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
    public Vector3 SunDirection => _sunDirection;
    public bool IsSunDirectionOverridden => _sunDirectionOverride.HasValue;

    public bool IsDayAt(Vector3 worldPosition)
    {
        Vector3 center = PlanetCenter != null ? PlanetCenter.position : Vector3.zero;
        Vector3 surfaceNormal = (worldPosition - center).normalized;
        return Vector3.Dot(surfaceNormal, SunDirection) > 0f;
    }

    /// <summary>-1 = full moon, 0 = half, +1 = new moon.</summary>
    public float MoonPhase { get; private set; }

    /// <summary>0-7 discrete phase index.</summary>
    public int MoonPhaseIndex => MoonOrbit.PhaseIndex(_moonCycleProgress);

    /// <summary>0 at new moon, 1 at full moon. Useful for magic intensity.</summary>
    public float MoonFullness => (1f - MoonPhase) * 0.5f;

    /// <summary>0-1 progress through the current season cycle.</summary>
    public float SeasonProgress => 0f;

    public void RegisterWorldServices(IWorldContext context)
    {
        context.Register<ICelestialTimeController>(this);
    }

    public void RegisterWorldSettings(ISettingsService settings)
    {
        if (settings.IsRegistered<MoonDto>()) return;
        var source = Resources.Load<MoonSettings>("Settings/MoonSettings");
        if (source == null) throw new System.InvalidOperationException("Moon settings require Resources/Settings/MoonSettings.asset.");
        settings.Register(MoonDto.From(source));
    }

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
        EventBus<SettingsChangedEvent>.Listen(OnSettingsChanged);
        _commands = new CelestialCommands(this);
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        EventBus<SettingsChangedEvent>.Unlisten(OnSettingsChanged);
        _commands?.Dispose();
        _commands = null;
    }

    public async Awaitable EarlyInitialize(System.Threading.CancellationToken cancellationToken)
    {
        _settingsService = SettingsProvider.Get();
        _moonSettings = _settingsService.GetSettings<MoonDto>();
        if (!_moonSettings.TryValidate(out string error)) throw new System.InvalidOperationException(error);
        if (_moonSettings.Material == null) throw new System.InvalidOperationException("Moon settings require a material.");
        if (PlanetCenter == null) PlanetCenter = ServiceLocator.Get<IPlanet>().Transform;
        _moonCycleProgress = _moonSettings.StartPhase;
        _timeOfDay = float.IsFinite(StartTimeOfDay) ? Mathf.Repeat(StartTimeOfDay, 1f) : 0.25f;
        _initialized = true;
        _appearanceDirty = true;
        RefreshCelestials();
        await Awaitable.NextFrameAsync(cancellationToken);
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetRadius = evt.PlanetRadius;
        Shader.SetGlobalFloat(_starSeedId, ServiceLocator.Get<IPlanet>().Seed * 0.01f);
        if (_initialized) RefreshCelestials();
    }

    void OnSettingsChanged(SettingsChangedEvent evt)
    {
        if (!_initialized || evt.DtoType != typeof(MoonDto)) return;
        var next = _settingsService.GetSettings<MoonDto>();
        if (!next.TryValidate(out string error)) throw new System.InvalidOperationException(error);
        _moonSettings = next;
        _appearanceDirty = true;
        RefreshCelestials();
    }

    void Update()
    {
        if (!_initialized) return;
        float dt = FreezeTime ? 0f : Time.deltaTime;
        UpdateSun(dt);
        UpdateMoon(dt);
        UpdateAmbient();
        UpdateMoonShaderGlobals();
        FireEvents();
    }

    void RefreshCelestials()
    {
        UpdateSun(0f);
        UpdateMoon(0f);
        UpdateAmbient();
        UpdateMoonShaderGlobals();
        FireEvents();
    }

    public bool TryApplyMoonSettings(MoonDto next, out string error)
    {
        error = "Moon settings are not initialized.";
        if (!_initialized || next == null) return false;
        if (!next.TryValidate(out error)) return false;
        if (next.Material != _moonSettings.Material)
        {
            error = "The moon material cannot change during a world session.";
            return false;
        }
        _settingsService.Update(next);
        return true;
    }

    public bool TrySetMoonPhase(float progress)
    {
        if (!float.IsFinite(progress)) return false;
        _moonCycleProgress = Mathf.Repeat(progress, 1f);
        if (_initialized) RefreshCelestials();
        return true;
    }

    public void SetMoonPhaseHeld(bool held) => IsMoonPhaseHeld = held;

    public void ToggleTimeFrozen()
    {
        SetTimeFrozen(!FreezeTime);
    }

    public void SetTimeFrozen(bool frozen)
    {
        FreezeTime = frozen;
        if (!frozen) ResetSunDirection();
    }

    public bool TrySetSunDirection(Vector3 direction)
    {
        if (!_initialized || !SunLighting.TryNormalizeDirection(direction, out var normalized)) return false;
        _sunDirectionOverride = normalized;
        RefreshCelestials();
        return true;
    }

    public void ResetSunDirection()
    {
        _sunDirectionOverride = null;
        if (_initialized) RefreshCelestials();
    }

    public void SetTimeOfDay(float timeOfDay)
    {
        if (!float.IsFinite(timeOfDay)) throw new System.ArgumentOutOfRangeException(nameof(timeOfDay));
        _sunDirectionOverride = null;
        _timeOfDay = Mathf.Repeat(timeOfDay, 1f);
        if (_initialized) RefreshCelestials();
    }

    public bool TrySetLocalTimeOfDay(float localTimeOfDay)
    {
        if (PlanetCenter == null || !float.IsFinite(localTimeOfDay))
            return false;

        Camera cam = GetViewCamera();
        if (cam == null)
            return false;

        Vector3 camDir = (cam.transform.position - PlanetCenter.position).normalized;
        if (camDir.sqrMagnitude < 0.0001f)
            return false;

        Quaternion tilt = Quaternion.Euler(AxialTilt, 0f, 0f);
        Vector3 untilted = Quaternion.Inverse(tilt) * -camDir;
        Vector3 inPlane = new Vector3(untilted.x, untilted.y, 0f);
        if (inPlane.sqrMagnitude < 0.0001f)
            return false;

        inPlane.Normalize();
        float tNoon = Mathf.Atan2(inPlane.x, -inPlane.y) / (2f * Mathf.PI);
        if (tNoon < 0f)
            tNoon += 1f;

        localTimeOfDay = Mathf.Repeat(localTimeOfDay, 1f);
        SetTimeOfDay(tNoon + localTimeOfDay - 0.5f);
        return true;
    }

    void UpdateSun(float dt)
    {
        _timeOfDay = MoonOrbit.Advance(_timeOfDay, dt, DayLengthSeconds, 1f);

        Vector3 center = PlanetCenter != null ? PlanetCenter.position : Vector3.zero;
        _sunDirection = _sunDirectionOverride
            ?? MoonOrbit.Frame(_timeOfDay, float.IsFinite(AxialTilt) ? AxialTilt : 0f) * Vector3.up;
        SunLighting.Apply(_sunDirection, SunLight, center, _planetRadius);
        if (SunLight != null) UpdateShadowStrength(-_sunDirection, center);
    }

    // Fade the Sun's cast-shadow strength as it grazes the viewer's local horizon. At dawn/dusk the shadows
    // are extremely long and, with a hard low-cost far LOD, read as black dashes stippled across the ground;
    // softening them there (never to zero — distant shadows stay, just lighter) keeps the look grounded while
    // midday shadows stay crisp. Elevation is measured at the view camera so the fade tracks what's on screen.
    void UpdateShadowStrength(Vector3 sunDir, Vector3 center)
    {
        Camera cam = GetViewCamera();
        if (cam == null) return;
        Vector3 up = (cam.transform.position - center).normalized;
        float sinElevation = Vector3.Dot(up, -sunDir); // -sunDir points toward the sun; = sin(elevation)
        float fadeSin = Mathf.Sin(Mathf.Max(0f, ShadowFadeElevationDeg) * Mathf.Deg2Rad);
        float t = fadeSin > 1e-4f ? Mathf.Clamp01(sinElevation / fadeSin) : 1f; // 0 on horizon, 1 at/above fade elev
        SunLight.shadowStrength = Mathf.Lerp(Mathf.Clamp01(ShadowGrazingStrength), 1f, t);
    }

    void UpdateMoon(float dt)
    {
        if (_moonSettings == null) return;
        _moonCycleProgress = MoonOrbit.Advance(_moonCycleProgress, IsMoonPhaseHeld ? 0f : dt,
            DayLengthSeconds, _moonSettings.CycleDays);
        _moonDirection = MoonOrbit.Direction(_timeOfDay, _moonCycleProgress,
            float.IsFinite(AxialTilt) ? AxialTilt : 0f, _moonSettings.Inclination, _moonSettings.NodeAngle,
            out Vector3 orbitNormal);
        MoonPhase = Mathf.Clamp(Vector3.Dot(SunDirection, _moonDirection), -1f, 1f);
        if (MoonTransform == null) return;
        Vector3 center = PlanetCenter != null ? PlanetCenter.position : Vector3.zero;
        MoonTransform.SetPositionAndRotation(center + _moonDirection * MoonOrbitRadius,
            Quaternion.LookRotation(-_moonDirection, orbitNormal));
        SetMoonVisible(MoonOrbitRadius > 0f);
        UpdateMoonVisualPosition();
        UpdateMoonMaterial();
    }

    void LateUpdate()
    {
        if (_initialized && MoonTransform != null) UpdateMoonVisualPosition();
    }

    void UpdateMoonVisualPosition()
    {
        float diameter = 2f * MoonOrbit.VisualRadius(MoonOrbitRadius, _moonSettings.Diameter);
        Vector3 position = MoonTransform.position;
        Camera camera = GetViewCamera();
        if (camera != null)
        {
            Vector3 offset = position - camera.transform.position;
            float scale = MoonOrbit.ProjectionScale(offset.magnitude, diameter * 0.5f, camera.farClipPlane);
            position = camera.transform.position + offset * scale;
            diameter *= scale;
        }
        if (_moonRenderers != null)
            foreach (var renderer in _moonRenderers)
                if (renderer != null)
                {
                    renderer.transform.position = position;
                    renderer.transform.localScale = Vector3.one * diameter;
                }
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
        _moonDefaultMaterials = new Material[_moonRenderers.Length][];
        for (int i = 0; i < _moonRenderers.Length; i++)
        {
            _moonRendererDefaults[i] = _moonRenderers[i] != null && _moonRenderers[i].enabled;
            _moonDefaultMaterials[i] = _moonRenderers[i] != null ? _moonRenderers[i].sharedMaterials : null;
        }
        _appearanceDirty = true;
    }

    static readonly int MoonSunId = Shader.PropertyToID("_MoonSunDirection");
    static readonly int MoonTintId = Shader.PropertyToID("_BaseColor");
    static readonly int MoonBrightnessId = Shader.PropertyToID("_Brightness");
    static readonly int MoonDetailId = Shader.PropertyToID("_BumpScale");
    static readonly int MoonEarthshineId = Shader.PropertyToID("_Earthshine");

    void UpdateMoonMaterial()
    {
        if (_moonMaterial == null && _moonSettings.Material != null)
        {
            _moonMaterial = new Material(_moonSettings.Material) { name = "Moon (Runtime)" };
            _appearanceDirty = true;
        }
        if (_moonMaterial == null) return;
        if (_appearanceDirty)
        {
            foreach (var renderer in _moonRenderers)
                if (renderer != null) renderer.sharedMaterial = _moonMaterial;
            _moonMaterial.SetColor(MoonTintId, _moonSettings.Tint);
            _moonMaterial.SetFloat(MoonBrightnessId, _moonSettings.Brightness);
            _moonMaterial.SetFloat(MoonDetailId, _moonSettings.Detail);
            _moonMaterial.SetFloat(MoonEarthshineId, _moonSettings.Earthshine);
            _appearanceDirty = false;
        }
        _moonMaterial.SetVector(MoonSunId, SunDirection);
    }

    public void TeardownWorld()
    {
        _initialized = false;
        _sunDirectionOverride = null;
        if (_moonRenderers != null)
            for (int i = 0; i < _moonRenderers.Length; i++)
                if (_moonRenderers[i] != null) _moonRenderers[i].sharedMaterials = _moonDefaultMaterials[i];
        if (_moonMaterial != null) Destroy(_moonMaterial);
        _moonMaterial = null;
        _commands?.Dispose();
        _commands = null;
        Shader.SetGlobalVector(_moonParamsId, Vector4.zero);
        Shader.SetGlobalFloat(_moonIntensityId, 0f);
    }

    void OnDestroy() => TeardownWorld();

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

    Camera GetViewCamera()
    {
        if (_cachedMainCamera != null && _cachedMainCamera.isActiveAndEnabled)
            return _cachedMainCamera;

        _cachedMainCamera = Camera.main;
        return _cachedMainCamera;
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

        float moonInfluence = MoonTransform != null && PlanetCenter != null ? Mathf.Clamp01(-MoonPhase) : 0f;

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
