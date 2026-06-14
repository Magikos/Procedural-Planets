using System.Threading;
using UnityEngine;
using UnityEngine.Serialization;

[CommandPrefix("planet")]
public class Planet : MonoBehaviour, IPlanet, IPlanetSurfaceSampler, IPlanetSurfaceRaycaster,
    IClimateSampler, IGrassRuntimeControl, IEarlyInitialize, ILateInitialize, IProgressReporter,
    IWorldServiceRegistrar, IWorldSettingsRegistrar, IWorldTeardown
{
    static readonly System.Type[] RequiredSettings =
    {
        typeof(PlanetDto),
        typeof(BiomeDto),
    };

    public enum FaceRenderMask { All, Top, Bottom, Left, Right, Front, Back }

    [Range(2, 256), FormerlySerializedAs("Resolution"),
     Tooltip("Per-face vertex resolution when PlanetSettings.Resolution == Low.")]
    public int PerFaceResolution = 10;
    public FaceRenderMask RenderMask = FaceRenderMask.All;

    [SerializeField] PlanetSettings _planetSettings;

#if UNITY_EDITOR
    public PlanetSettings PlanetSettingsAsset => _planetSettings;
#endif

    // Read reflectively by PlanetEditor through SerializedObject.FindProperty.
#pragma warning disable CS0414
    [SerializeField, HideInInspector] bool _settingsFoldout = true;
#pragma warning restore CS0414
    [SerializeField, HideInInspector] float _lastGeneratedRadius;
    [SerializeField, HideInInspector] float _lastSeaLevelRadius;

    ShapeGenerator _shapeGenerator = new ShapeGenerator();
    ColorGenerator _colorGenerator = new ColorGenerator();
    ClimateMapGpuData _climateMapGpuData;
    IPlanetSurfaceProvider _surfaceProvider;
    // Typed reference to the Low-mode provider for legacy color iteration over TerrainFaces.
    // Null when running under chunked or GPU surface providers.
    PerFaceSurfaceProvider _perFaceProvider;
    PlanetGrassCoordinator _grass;
    PlanetWaterSurface _waterSurface;
    PlanetTerrainMaterial _terrainMaterial;

    CancellationTokenSource _cts;
    bool _isGenerating;
    bool _worldTornDown;
    readonly ProgressHandle _progressHandle = new ProgressHandle();

    public bool IsGenerating => _isGenerating;
    public ShapeGenerator ShapeGenerator => _shapeGenerator;
    public float LastGeneratedRadius => _lastGeneratedRadius;
    public float LastSeaLevelRadius => _lastSeaLevelRadius;
    public int Seed { get; private set; }
    public Transform Transform => transform;
    public string ReporterName => "Planet";
    public int StepCount => 5;
    public IProgressHandle ProgressHandle => _progressHandle;

    ILogger Logger => LoggerProvider.Get();

    void Awake()
    {
        EnsureRuntimeOwners();
    }

    public void RegisterWorldServices(IWorldContext context)
    {
        EnsureGrassCoordinator();
        context.Register<IPlanet>(this);
        context.Register<IPlanetSurfaceSampler>(this);
        context.Register<IPlanetSurfaceRaycaster>(this);
        context.Register<IClimateSampler>(this);
        context.Register<IGrassRuntimeControl>(this);
        context.Register<IGrassNearFieldStatsProvider>(_grass);
    }

    void EnsureRuntimeOwners()
    {
        EnsureGrassCoordinator();
        _waterSurface ??= new PlanetWaterSurface(transform);
        _terrainMaterial ??= new PlanetTerrainMaterial(Logger);
    }

    void EnsureGrassCoordinator()
    {
        _grass ??= new PlanetGrassCoordinator(transform, this, Logger);
    }

    public System.Collections.Generic.IReadOnlyList<System.Type> RequiredSettingsTypes => RequiredSettings;

    public void RegisterWorldSettings(ISettingsService settings)
    {
        if (_planetSettings == null)
            throw new System.InvalidOperationException("Planet requires a PlanetSettings asset.");
        if (_planetSettings.BiomeSettings == null)
            throw new System.InvalidOperationException("PlanetSettings requires a BiomeSettings asset.");
        if (_planetSettings.BiomeSettings.Registry == null)
            throw new System.InvalidOperationException("BiomeSettings requires a BiomeRegistry asset.");

        if (!settings.IsRegistered<PlanetDto>())
            settings.Register(PlanetDto.From(_planetSettings));
        if (!settings.IsRegistered<BiomeDto>())
            settings.Register(BiomeDto.From(_planetSettings.BiomeSettings));
    }

    public async Awaitable EarlyInitialize(CancellationToken cancellationToken)
    {
        // Force-init grass interactor shader globals before any grass shader runs.
        // Without this, _GrassInteractorCount is uninitialized and
        // SampleGrassInteractorBend reads garbage from an unbound StructuredBuffer,
        // displacing every blade by a random amount (visible as a smudgy green wash
        // with no per-blade detail). Foundational, fast, no dependencies — fits the
        // Early phase, separate from LateInitialize's planet generation.
        GrassInteractorRegistry.Initialize();
        await Awaitable.NextFrameAsync(cancellationToken);
    }

    void OnDestroy()
    {
        TeardownWorld();
    }

    public void TeardownWorld()
    {
        if (_worldTornDown)
            return;

        _worldTornDown = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _grass?.Dispose();
        _climateMapGpuData?.Dispose();
        _climateMapGpuData = null;
        _surfaceProvider?.Dispose();
        _surfaceProvider = null;
        _perFaceProvider = null;
        _colorGenerator?.Dispose();
        _waterSurface?.Dispose();
        _terrainMaterial?.Dispose();
        GrassInteractorRegistry.DisposeBuffer();
    }

    void LateUpdate()
    {
        // Pack any registered IGrassInteractor instances into the shader globals.
        // Cheap when idle (no interactors → just sets count = 0).
        GrassInteractorRegistry.UploadPerFrame();
    }

    // Camera reference cached so we don't pay Camera.main's GameObject.FindWithTag every frame.
    Camera _observerCamera;

    void Update()
    {
        if (_surfaceProvider == null || _isGenerating) return;
        if (_observerCamera == null || !_observerCamera.isActiveAndEnabled)
            _observerCamera = Camera.main;
        if (_observerCamera == null) return;
        _surfaceProvider.Tick(_observerCamera.transform.position, _observerCamera);
        _grass.Tick(_observerCamera);
    }

    void Initialize()
    {
        _grass.DisposeControllers();
        _climateMapGpuData?.Dispose();
        _climateMapGpuData = null;
        DestroyChildren();

        ISeedProvider seedProvider = ServiceLocator.Get<ISeedProvider>();
        Seed = seedProvider.GetSeedForSystem("Planet");

        _surfaceProvider?.Dispose();
        _surfaceProvider = null;
        _perFaceProvider = null;
        _waterSurface.NotifyChildrenDestroyed();

        PlanetDto planet = SettingsProvider.GetSettings<PlanetDto>();
        BiomeDto biomeDto = SettingsProvider.GetSettings<BiomeDto>();

        var shapeSettings = planet.BuildShapeSettings();
        _shapeGenerator.Configure(shapeSettings);
        _shapeGenerator.Initialize(Seed);
        _colorGenerator.Configure(biomeDto);
        _colorGenerator.Initialize(
            Seed,
            seedProvider.GetSeedForSystem("BiomeVoronoi"));

        _terrainMaterial.EnsureRuntime(planet.PlanetMaterial);
        _terrainMaterial.Configure(_grass);

        switch (planet.Resolution)
        {
            case PlanetResolution.Low:
                _perFaceProvider = new PerFaceSurfaceProvider(
                    transform, _shapeGenerator, PerFaceResolution, _terrainMaterial.Material, RenderMask);
                _surfaceProvider = _perFaceProvider;
                break;
            case PlanetResolution.High:
                _surfaceProvider = new ChunkedSurfaceProvider(
                    transform, _shapeGenerator, _terrainMaterial.Material, RenderMask,
                    planet.MaxChunkDepth);
                // Pre-cache mode: all chunks at all depths <= MaxChunkDepth are generated up
                // front during the loading bar. Runtime Tick is a cheap visibility filter; no
                // mesh jobs run at runtime. Per-vertex colors stay disabled until Phase B.
                break;
            default:
                throw new System.ArgumentOutOfRangeException();
        }
    }

    void DestroyChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
    }

    public async Awaitable LateInitialize(CancellationToken cancellationToken)
    {
        await GeneratePlanetAsync(cancellationToken);
    }

    /// <summary>
    /// Generates the planet mesh, colors, and water. Safe to call directly for editor/runtime
    /// regeneration; LoadingManager calls this via <see cref="LateInitialize"/>.
    /// </summary>
    public async Awaitable GeneratePlanetAsync(CancellationToken externalToken = default)
    {
        if (_planetSettings == null)
        {
            Logger.Log(LogLevel.Warning, "Planet", "PlanetSettings is not assigned.");
            return;
        }

        if (_isGenerating) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        CancellationTokenSource linkedCts = null;
        CancellationToken ct = _cts.Token;
        if (externalToken.CanBeCanceled)
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, _cts.Token);
            ct = linkedCts.Token;
        }

        try
        {
            _isGenerating = true;
            _progressHandle.Report(0f, "Initializing planet...");
            Initialize();
            _progressHandle.Report(0.1f, "Generating terrain...");
            await GenerateMeshAsync(new ProgressRangeHandle(_progressHandle, 0.1f, 0.7f), ct);
            if (this == null) return;
            _shapeGenerator.CommitElevationRange();
            _progressHandle.Report(0.8f, "Applying colors...");
            await GenerateColorsAsync(new ProgressRangeHandle(_progressHandle, 0.8f, 0.1f), ct);
            if (this == null) return;
            BuildClimateMap();
            _progressHandle.Report(0.9f, "Generating water...");
            await _waterSurface.GenerateAsync(
                _surfaceProvider?.GetFaceMeshSamplers(),
                _colorGenerator.ClimateProvider,
                PerFaceResolution,
                new ProgressRangeHandle(_progressHandle, 0.9f, 0.1f),
                ct);
            _grass.Configure(_surfaceProvider as ChunkedSurfaceProvider,
                _colorGenerator.SurfaceArrays, Seed, _observerCamera, _terrainMaterial.Material);
            // Atmosphere is rendered by AtmosphereController + AtmosphereRenderFeature (post-process).

            var planet = SettingsProvider.GetSettings<PlanetDto>();
            float scaledRadius = planet.PlanetRadius * (1 + _shapeGenerator.ElevationMax);
            float seaLevelRadius = planet.PlanetRadius * (1 + planet.OceanLevel);
            _lastGeneratedRadius = scaledRadius;
            _lastSeaLevelRadius = seaLevelRadius;
            _progressHandle.Report(1f, "Planet ready");
            await Awaitable.NextFrameAsync(ct);
            EventBus<PlanetGeneratedEvent>.Raise(new PlanetGeneratedEvent(transform.position, scaledRadius, seaLevelRadius, _shapeGenerator.ElevationMin, _shapeGenerator.ElevationMax));
            Logger.Log(LogLevel.Debug, "Planet", $"Generated planet with seed {Seed}, mode {planet.Resolution}, perFaceResolution {PerFaceResolution}, radius {scaledRadius:F1}");
        }
        catch (System.OperationCanceledException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            Logger.LogException("Planet", ex);
            throw;
        }
        finally
        {
            _isGenerating = false;
            linkedCts?.Dispose();
        }
    }

    async Awaitable GenerateMeshAsync(IProgressHandle progress, CancellationToken ct)
    {
        await _surfaceProvider.GenerateAsync(progress, ct);
    }

    void GenerateColors()
    {
        if (_perFaceProvider == null) return;
        foreach (var face in _perFaceProvider.TerrainFaces)
            face.UpdateColors(_colorGenerator);
    }

    async Awaitable GenerateColorsAsync(IProgressHandle progress, CancellationToken ct)
    {
        if (_surfaceProvider == null) return;
        await _surfaceProvider.GenerateColorsAsync(_colorGenerator, progress, ct);
    }

    void BuildClimateMap()
    {
        _climateMapGpuData?.Dispose();
        _climateMapGpuData = null;

        if (!SettingsProvider.IsRegistered<BiomeDto>() ||
            _colorGenerator?.ClimateProvider == null ||
            _surfaceProvider == null)
        {
            return;
        }

        BiomeDto biome = SettingsProvider.GetSettings<BiomeDto>();

        try
        {
            _climateMapGpuData = ClimateMapGpuData.Build(
                _colorGenerator.ClimateProvider,
                _surfaceProvider.GetFaceMeshSamplers(),
                biome.ClimateMapResolution,
                biome.MinimumTemperatureCelsius,
                biome.MaximumTemperatureCelsius,
                Logger);
        }
        catch (System.Exception ex)
        {
            Logger.LogException("Climate", ex);
            Logger.Log(
                LogLevel.Warning,
                "Climate",
                "GPU climate map bake failed; CPU climate queries remain available.");
        }
    }

    public GrassRuntimeState GetGrassRuntimeState() => _grass.GetGrassRuntimeState();

    public void SetGrassEnabled(bool enabled) => _grass.SetGrassEnabled(enabled);

    public void SetGrassLayerEnabled(GrassRenderLayer layer, bool enabled) =>
        _grass.SetGrassLayerEnabled(layer, enabled);

    public bool TryGetSurfaceRadius(Vector3 worldUnitDirection, out float surfaceRadius)
    {
        surfaceRadius = 0f;

        if (_surfaceProvider == null || worldUnitDirection.sqrMagnitude < 0.0001f)
            return false;

        // Provider operates in planet-local space; Planet wraps with transform math so the
        // provider stays Unity-transform-agnostic.
        Vector3 localDirection = transform.InverseTransformDirection(worldUnitDirection).normalized;
        if (!_surfaceProvider.TryGetLocalSurfaceRadius(localDirection, out float localRadius))
            return false;

        float scale = Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z));
        surfaceRadius = localRadius * Mathf.Max(scale, 0.0001f);
        return true;
    }

    public bool TrySampleClimate(Vector3 worldPosition, out ClimateSample sample)
    {
        sample = default;
        if (_surfaceProvider == null ||
            _colorGenerator?.ClimateProvider == null ||
            !SettingsProvider.IsRegistered<PlanetDto>())
        {
            return false;
        }

        var planet = SettingsProvider.GetSettings<PlanetDto>();
        if (planet.PlanetRadius <= 0f)
            return false;

        Vector3 localPoint = transform.InverseTransformPoint(worldPosition);
        if (localPoint.sqrMagnitude < 0.0001f)
            return false;

        Vector3 localDirection = localPoint.normalized;
        if (!_surfaceProvider.TryGetLocalSurfaceRadius(localDirection, out float localRadius))
            return false;

        float elevation = localRadius / planet.PlanetRadius - 1f;
        sample = _colorGenerator.ClimateProvider.Evaluate(localDirection, elevation);
        return true;
    }

    public bool TryRaycastSurface(Ray worldRay, float maxDistance, out PlanetSurfaceRaycastHit hit)
    {
        hit = default;
        if (_surfaceProvider is not ChunkedSurfaceProvider chunkedProvider)
            return false;

        float scale = Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z));
        scale = Mathf.Max(scale, 0.0001f);
        Vector3 localOrigin = transform.InverseTransformPoint(worldRay.origin);
        Vector3 localDirection = transform.InverseTransformDirection(worldRay.direction).normalized;
        if (localDirection.sqrMagnitude < 0.0001f)
            return false;

        float localMaxDistance = Mathf.Max(0f, maxDistance) / scale;
        if (!chunkedProvider.TryRaycastVisibleSurface(new Ray(localOrigin, localDirection), localMaxDistance,
                out Vector3 localPoint, out Vector3 localNormal, out _))
            return false;

        Vector3 worldPoint = transform.TransformPoint(localPoint);
        float worldDistance = Vector3.Distance(worldRay.origin, worldPoint);
        if (maxDistance > 0f && worldDistance > maxDistance)
            return false;

        Vector3 worldNormal = transform.TransformDirection(localNormal).normalized;
        if (worldNormal.sqrMagnitude < 0.0001f)
            worldNormal = (worldPoint - transform.position).normalized;

        hit = new PlanetSurfaceRaycastHit
        {
            Point = worldPoint,
            Normal = worldNormal,
            Distance = worldDistance,
            SurfaceRadius = Vector3.Distance(worldPoint, transform.position),
        };
        return true;
    }

    sealed class ProgressRangeHandle : IProgressHandle
    {
        readonly IProgressHandle _inner;
        readonly float _start;
        readonly float _length;

        public float CurrentProgress { get; private set; }
        public string CurrentMessage { get; private set; } = string.Empty;

        public ProgressRangeHandle(IProgressHandle inner, float start, float length)
        {
            _inner = inner;
            _start = start;
            _length = length;
        }

        public void Report(float progress, string message = "")
        {
            CurrentProgress = _start + Mathf.Clamp01(progress) * _length;
            CurrentMessage = message ?? string.Empty;
            _inner?.Report(CurrentProgress, CurrentMessage);
        }
    }

    // --- Console commands -------------------------------------------------

    [ConsoleCommand("seed", "Get the current world seed, or set a new one. Does NOT auto-regenerate — run 'planet.generate' to apply.", MonoTargetType.Single)]
    string SeedCmd(int? newSeed = null)
    {
        if (newSeed == null) return $"current planet seed: {Seed} (world seed: {ServiceLocator.Get<ISeedProvider>().WorldSeed})";
        ServiceLocator.Get<ISeedProvider>().SetWorldSeed(newSeed.Value);
        return $"world seed set to {newSeed.Value}. Run 'planet.generate' to apply.";
    }

    [ConsoleCommand("resolution", "Get or set per-face vertex resolution (range 2-256, low-mode only). Does NOT auto-regenerate — run 'planet.generate' to apply.", MonoTargetType.Single)]
    string ResolutionCmd(int? value = null)
    {
        if (value == null) return $"per-face resolution: {PerFaceResolution}";
        PerFaceResolution = Mathf.Clamp(value.Value, 2, 256);
        return $"per-face resolution: {PerFaceResolution}. Run 'planet.generate' to apply.";
    }

    [ConsoleCommand("generate", "Regenerate the planet (async, cancellable). Optionally set seed and/or radius first.", MonoTargetType.Single)]
    async Awaitable GenerateCmd(int? seed = null, float? radius = null, CancellationToken ct = default)
    {
        if (IsGenerating)
            throw new System.InvalidOperationException("planet generation already in progress");

        if (seed.HasValue)
            ServiceLocator.Get<ISeedProvider>().SetWorldSeed(seed.Value);

        if (radius.HasValue && SettingsProvider.IsRegistered<PlanetDto>())
        {
            var dto = SettingsProvider.GetSettings<PlanetDto>();
            SettingsProvider.Update(dto with { PlanetRadius = Mathf.Max(100f, radius.Value) });
        }

        await GeneratePlanetAsync(ct);
    }
}
