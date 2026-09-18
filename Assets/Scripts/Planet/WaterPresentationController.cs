using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Local presentation only. Actor poses and still-water queries remain owned by gameplay.
[DisallowMultipleComponent]
public sealed class WaterPresentationController : MonoBehaviour
{
    const int MaxRipples = 24;
    readonly Vector4[] _origins = new Vector4[MaxRipples];
    readonly Vector4[] _normals = new Vector4[MaxRipples];
    readonly Vector4[] _parameters = new Vector4[MaxRipples];
    readonly float[] _created = new float[MaxRipples];
    readonly Collider[] _colliders = new Collider[64];
    readonly Dictionary<Rigidbody, WaterContactTracker> _bodies = new();
    readonly HashSet<Rigidbody> _seen = new();
    readonly List<Rigidbody> _stale = new();
    readonly WaterImmersionState _immersion = new();
    readonly WaterReflectionCapture _reflection = new();
    static readonly int CameraPositionId = Shader.PropertyToID(ShaderGlobalIds.WaterCameraPosition);
    static readonly int CameraSurfaceId = Shader.PropertyToID(ShaderGlobalIds.WaterCameraSurface);
    static readonly int CountId = Shader.PropertyToID(ShaderGlobalIds.WaterRippleCount);
    static readonly int OriginsId = Shader.PropertyToID(ShaderGlobalIds.WaterRippleOrigins);
    static readonly int NormalsId = Shader.PropertyToID(ShaderGlobalIds.WaterRippleNormals);
    static readonly int ParamsId = Shader.PropertyToID(ShaderGlobalIds.WaterRippleParams);
    static readonly int TimeId = Shader.PropertyToID(ShaderGlobalIds.GameTime);
    IWaterQueryService _water;
    IWeatherProvider _weather;
    IClimateSampler _climate;
    Transform _planet;
    WaterDto _settings;
    PlanetDto _planetSettings;
    Camera _camera;
    WaterListenerFilter _audio;
    bool _ownsAudio;
    WaterSplashParticles _splashes;
    Vector3 _lastCamera;
    int _count, _cameraFrame = -1;
    bool _ready;

    public int ActiveRipples => _count;
    public int SplashCount { get; private set; }
    public float Immersion => _immersion.Immersion;
    public int ReflectionCaptures => _reflection.CompletedCaptures;
    public int ReflectionResolution => _reflection.Resolution;

    public void Initialize(IWaterQueryService water, Transform planet)
    {
        _water = water;
        _planet = planet;
        ResolveWorld();
        _splashes ??= new WaterSplashParticles(transform);
        _ready = water != null && planet != null;
    }

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnGenerated);
        EventBus<SettingsChangedEvent>.Listen(OnSettingsChanged);
        RenderPipelineManager.beginCameraRendering += OnCamera;
    }

    void OnGenerated(PlanetGeneratedEvent evt) { ResolveWorld(); _count = 0; _bodies.Clear(); _cameraFrame = -1; }
    void OnSettingsChanged(SettingsChangedEvent evt) => RefreshSettings();
    void ResolveWorld()
    {
        ServiceLocator.TryGet(out _weather);
        ServiceLocator.TryGet(out _climate);
        RefreshSettings();
        _camera = Camera.main;
    }
    void RefreshSettings()
    {
        SettingsProvider.TryGetFrozen(out _settings);
        SettingsProvider.TryGetFrozen(out _planetSettings);
    }

    void Update()
    {
        if (!_ready || _settings == null || _planetSettings == null) return;
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;
        float dt = Time.deltaTime;
        for (int i = _count - 1; i >= 0; i--)
        {
            float age = Time.time - _created[i];
            if (age >= _parameters[i].y)
            {
                int last = --_count;
                _origins[i] = _origins[last]; _normals[i] = _normals[last];
                _parameters[i] = _parameters[last]; _created[i] = _created[last];
            }
            else _origins[i].w = age;
        }
        foreach (var source in WaterInteractor.Active)
        {
            if (source == null || !source.isActiveAndEnabled) continue;
            if ((source.transform.position - _camera.transform.position).sqrMagnitude > 6400f)
            { source.Contact.Reset(); continue; }
            SampleContact(source.Contact, source.transform.position, source.Radius, dt);
        }
        _seen.Clear();
        int count = Physics.OverlapSphereNonAlloc(_camera.transform.position, 50f, _colliders, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Rigidbody body = _colliders[i].attachedRigidbody;
            if (body == null || !_seen.Add(body) || body.GetComponent<WaterInteractor>() != null) continue;
            if (!_bodies.TryGetValue(body, out var state))
            {
                if (_bodies.Count >= 64) continue;
                _bodies.Add(body, state = new WaterContactTracker());
            }
            SampleContact(state, body.worldCenterOfMass, Mathf.Clamp(_colliders[i].bounds.extents.magnitude * .5f, .1f, 4f), dt);
        }
        _stale.Clear();
        foreach (var pair in _bodies) if (pair.Key == null || !_seen.Contains(pair.Key)) _stale.Add(pair.Key);
        foreach (var body in _stale) _bodies.Remove(body);
        _splashes?.Tick(_planet.position, dt);
        PublishRipples();
    }

    void SampleContact(WaterContactTracker tracker, Vector3 position, float radius, float dt)
    {
        if (!TryMovingSurface(position, out var sample, out float liquid) || liquid < .1f)
        { tracker.Reset(); return; }
        if (tracker.Step(position, sample, radius, dt, out var impulse)) Emit(impulse);
    }

    public void Emit(WaterImpulse impulse)
    {
        if (!CharacterMath.IsFinite(impulse.Position) || !CharacterMath.IsFinite(impulse.Normal)
            || !float.IsFinite(impulse.Strength) || !float.IsFinite(impulse.Radius) || impulse.Radius <= 0f) return;
        int limit = QualityController.WaterProfile.RippleLimit;
        _count = Mathf.Min(_count, limit);
        int index;
        if (_count < limit) index = _count++;
        else
        {
            index = 0;
            for (int i = 1; i < _count; i++) if (_created[i] < _created[index]) index = i;
        }
        _origins[index] = new Vector4(impulse.Position.x, impulse.Position.y, impulse.Position.z, 0f);
        _normals[index] = new Vector4(impulse.Normal.x, impulse.Normal.y, impulse.Normal.z, Mathf.Clamp01(impulse.Strength));
        _parameters[index] = new Vector4(impulse.Radius, 3f, impulse.Splash ? .75f : .24f, 1.6f);
        _created[index] = Time.time;
        if (impulse.Splash) { SplashCount++; _splashes?.Emit(impulse); }
    }

    void PublishRipples()
    {
        Shader.SetGlobalInt(CountId, Mathf.Min(_count, QualityController.WaterProfile.RippleLimit));
        Shader.SetGlobalVectorArray(OriginsId, _origins);
        Shader.SetGlobalVectorArray(NormalsId, _normals);
        Shader.SetGlobalVectorArray(ParamsId, _parameters);
    }

    bool TryMovingSurface(Vector3 position, out WaterSample sample, out float liquid)
    {
        liquid = 1f;
        sample = default;
        if (_water == null || !_water.TryGetWaterSurface(position, out var still) || still.BodyDepth <= 0f) return false;
        if (_planetSettings.EnableFrozenWater && _climate != null && _climate.TrySampleClimate(position, out var climate))
        {
            float start = still.IsOcean ? _settings.OceanFreezeStartTemperature01 : _settings.LakeFreezeStartTemperature01;
            float end = still.IsOcean ? _settings.OceanFreezeCompleteTemperature01 : _settings.LakeFreezeCompleteTemperature01;
            liquid = WaterMotion.Smooth(Mathf.Min(start, end), Mathf.Max(start, end), climate.Temperature01);
        }
        float energy = _weather != null ? Mathf.Max(_weather.WindStrength01, _weather.GetStormIntensity(position)) : 0f;
        float height = WaterMotion.Height(still, _planet.position, _settings,
            _weather != null ? _weather.WindDirection : Vector3.right, energy, Shader.GetGlobalFloat(TimeId),
            _settings.DistanceScale(_planetSettings.PlanetRadius), liquid);
        sample = new WaterSample(still.SurfacePoint + still.Normal * height, still.Normal,
            still.SignedDepth + height, Mathf.Max(0f, still.BodyDepth + height), still.BodyId, still.IsOcean);
        return true;
    }

    void OnCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!_ready || camera != _camera || _settings == null || _planetSettings == null) return;
        Vector3 position = camera.transform.position;
        bool hasWater = TryMovingSurface(position, out var surface, out float liquid);
        float depth = hasWater ? surface.SignedDepth : -100f;
        if (_cameraFrame != Time.frameCount)
        {
            bool reset = _cameraFrame < 0 || (position - _lastCamera).sqrMagnitude > 400f;
            _immersion.Tick(depth, Time.deltaTime, reset);
            _cameraFrame = Time.frameCount;
            _lastCamera = position;
            if (hasWater && depth < 2f) _reflection.Tick(surface, position, QualityController.WaterProfile, Time.time);
            else _reflection.Dispose();
        }
        float surfaceRadius = hasWater ? Vector3.Distance(surface.SurfacePoint, _planet.position) : 0f;
        Shader.SetGlobalVector(CameraPositionId, new Vector4(position.x, position.y, position.z, hasWater ? 1f : 0f));
        Shader.SetGlobalVector(CameraSurfaceId, new Vector4(surfaceRadius, _immersion.Immersion, _immersion.Transition, 0f));
        if (_audio == null)
        {
            AudioListener listener = camera.GetComponentInChildren<AudioListener>();
            if (listener != null)
            {
                _audio = listener.GetComponent<WaterListenerFilter>();
                _ownsAudio = _audio == null;
                if (_audio == null) _audio = listener.gameObject.AddComponent<WaterListenerFilter>();
            }
        }
        if (_audio != null) _audio.SetImmersion(_immersion.Immersion);
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnGenerated);
        EventBus<SettingsChangedEvent>.Unlisten(OnSettingsChanged);
        RenderPipelineManager.beginCameraRendering -= OnCamera;
        Shader.SetGlobalVector(CameraPositionId, Vector4.zero);
        Shader.SetGlobalVector(CameraSurfaceId, Vector4.zero);
        Shader.SetGlobalInt(CountId, 0);
        _reflection.Dispose();
        if (_audio != null) _audio.SetImmersion(0f);
    }
    void OnDestroy()
    {
        _splashes?.Dispose();
        if (_ownsAudio && _audio != null) Destroy(_audio);
    }
}
