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

    [Header("Diagnostics")]
    [SerializeField] PlanetRecipe _recipe;

#if UNITY_EDITOR
    public PlanetSettings PlanetSettingsAsset => GetPlanetSettingsSource();
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

    static readonly int _planetCenterId = Shader.PropertyToID(ShaderGlobalIds.PlanetCenter);
    static readonly int _seaLevelRadiusId = Shader.PropertyToID(ShaderGlobalIds.SeaLevelRadius);
    static readonly int _densityOriginRadiusId = Shader.PropertyToID(ShaderGlobalIds.DensityOriginRadius);
    static readonly int _surfacePathDebugId = Shader.PropertyToID("_SurfacePathDebug");

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

    PlanetSettings GetPlanetSettingsSource() =>
        _recipe != null ? _recipe.PlanetSettings : _planetSettings;

    BiomeSettings GetBiomeSettingsSource() =>
        _recipe != null ? _recipe.BiomeSettingsSource : _planetSettings?.BiomeSettings;

    public void RegisterWorldSettings(ISettingsService settings)
    {
        PlanetSettings planetSource = GetPlanetSettingsSource();
        BiomeSettings biomeSource = GetBiomeSettingsSource();
        if (planetSource == null)
            throw new System.InvalidOperationException("Planet requires a PlanetSettings asset or PlanetRecipe.");
        if (biomeSource == null)
            throw new System.InvalidOperationException("PlanetSettings requires a BiomeSettings asset.");
        if (biomeSource.Registry == null)
            throw new System.InvalidOperationException("BiomeSettings requires a BiomeRegistry asset.");

        if (!settings.IsRegistered<PlanetDto>())
            settings.Register(_recipe != null ? _recipe.ToPlanetDto() : PlanetDto.From(planetSource));
        if (!settings.IsRegistered<BiomeDto>())
            settings.Register(_recipe != null ? _recipe.ToBiomeDto() : BiomeDto.From(biomeSource));
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

    async Awaitable InitializeAsync(IProgressHandle progress, CancellationToken ct)
    {
        progress?.Report(0f, "Resetting planet...");
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
        progress?.Report(0.15f, "Preparing biome regions...");
        await _colorGenerator.InitializeAsync(
            Seed,
            seedProvider.GetSeedForSystem("BiomeVoronoi"),
            new ProgressRangeHandle(progress, 0.15f, 0.7f),
            ct);

        progress?.Report(0.9f, "Preparing terrain renderer...");
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

        progress?.Report(1f, "Planet initialized.");
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
        if (GetPlanetSettingsSource() == null)
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
            var totalTimer = System.Diagnostics.Stopwatch.StartNew();
            var phaseTimer = System.Diagnostics.Stopwatch.StartNew();
            _progressHandle.Report(0f, "Initializing planet...");
            await InitializeAsync(new ProgressRangeHandle(_progressHandle, 0f, 0.1f), ct);
            long initializationMs = phaseTimer.ElapsedMilliseconds;
            phaseTimer.Restart();
            _progressHandle.Report(0.1f, "Generating terrain...");
            await GenerateMeshAsync(new ProgressRangeHandle(_progressHandle, 0.1f, 0.68f), ct);
            long terrainMs = phaseTimer.ElapsedMilliseconds;
            phaseTimer.Restart();
            if (this == null) return;
            _shapeGenerator.CommitElevationRange();
            _progressHandle.Report(0.78f, "Applying colors...");
            await GenerateColorsAsync(new ProgressRangeHandle(_progressHandle, 0.78f, 0.12f), ct);
            long colorsMs = phaseTimer.ElapsedMilliseconds;
            phaseTimer.Restart();
            if (this == null) return;
            _progressHandle.Report(0.9f, "Building climate map...");
            await BuildClimateMapAsync(new ProgressRangeHandle(_progressHandle, 0.9f, 0.04f), ct);
            long climateMs = phaseTimer.ElapsedMilliseconds;
            phaseTimer.Restart();
            _progressHandle.Report(0.94f, "Generating water...");
            await _waterSurface.GenerateAsync(
                _surfaceProvider?.GetFaceMeshSamplers(),
                _colorGenerator.ClimateProvider,
                PerFaceResolution,
                new ProgressRangeHandle(_progressHandle, 0.94f, 0.06f),
                ct);
            long waterMs = phaseTimer.ElapsedMilliseconds;
            _grass.Configure(_surfaceProvider as ChunkedSurfaceProvider,
                _colorGenerator.SurfaceArrays, Seed, _observerCamera, _terrainMaterial.Material);
            // Atmosphere is rendered by AtmosphereController + AtmosphereRenderFeature (post-process).

            var planet = SettingsProvider.GetSettings<PlanetDto>();
            float scaledRadius = planet.PlanetRadius * (1 + _shapeGenerator.ElevationMax);
            float seaLevelRadius = planet.PlanetRadius * (1 + planet.OceanLevel);
            _lastGeneratedRadius = scaledRadius;
            _lastSeaLevelRadius = seaLevelRadius;
            UploadCorePlanetShaderGlobals(seaLevelRadius);
            _progressHandle.Report(1f, "Planet ready");
            await Awaitable.NextFrameAsync(ct);
            EventBus<PlanetGeneratedEvent>.Raise(new PlanetGeneratedEvent(transform.position, scaledRadius, seaLevelRadius, _shapeGenerator.ElevationMin, _shapeGenerator.ElevationMax));
            Logger.Log(LogLevel.Debug, "Planet", $"Generated planet with seed {Seed}, mode {planet.Resolution}, perFaceResolution {PerFaceResolution}, radius {scaledRadius:F1}");
            Logger.Log(
                LogLevel.Debug,
                "Planet",
                $"Generation timings: initialize={initializationMs}ms, terrain={terrainMs}ms, " +
                $"colors={colorsMs}ms, climate={climateMs}ms, water={waterMs}ms, " +
                $"total={totalTimer.ElapsedMilliseconds}ms");
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

    void UploadCorePlanetShaderGlobals(float seaLevelRadius)
    {
        Shader.SetGlobalVector(_planetCenterId, transform.position);
        Shader.SetGlobalFloat(_seaLevelRadiusId, seaLevelRadius);
        Shader.SetGlobalFloat(_densityOriginRadiusId, seaLevelRadius);
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

    async Awaitable BuildClimateMapAsync(IProgressHandle progress, CancellationToken ct)
    {
        _climateMapGpuData?.Dispose();
        _climateMapGpuData = null;

        if (!SettingsProvider.IsRegistered<BiomeDto>() ||
            _colorGenerator?.ClimateProvider == null ||
            _surfaceProvider == null)
        {
            throw new System.InvalidOperationException(
                "Climate map generation requires biome settings, a climate provider, and a generated surface.");
        }

        BiomeDto biome = SettingsProvider.GetSettings<BiomeDto>();

        try
        {
            _climateMapGpuData = await ClimateMapGpuData.BuildAsync(
                _colorGenerator.ClimateProvider,
                _surfaceProvider.GetFaceMeshSamplers(),
                biome.ClimateMapResolution,
                biome.MinimumTemperatureCelsius,
                biome.MaximumTemperatureCelsius,
                Logger,
                progress,
                ct);
        }
        catch (System.OperationCanceledException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            Logger.LogException("Climate", ex);
            throw new System.InvalidOperationException(
                "GPU climate map generation failed.",
                ex);
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

    public bool TryPaintSurfacePathFromCamera(Ray worldRay, float radiusMeters, float strength, out string summary)
    {
        summary = "path paint requires a generated chunked planet";
        if (_surfaceProvider is not ChunkedSurfaceProvider chunkedProvider)
            return false;

        float maxDistance = Mathf.Max(_lastGeneratedRadius * 4f, 10000f);
        if (!TryRaycastSurface(worldRay, maxDistance, out PlanetSurfaceRaycastHit hit))
        {
            summary = "path paint missed the visible planet surface";
            return false;
        }

        Vector3 localDirection = transform.InverseTransformPoint(hit.Point).normalized;
        if (!chunkedProvider.TryPaintSurfaceStateDisc(
                localDirection,
                Mathf.Max(radiusMeters, 0.1f),
                Mathf.Clamp01(strength),
                out summary))
        {
            return false;
        }

        _grass.DisposeControllers();
        return true;
    }

    public bool TryPaintSurfacePathAtWorldPosition(Vector3 worldPosition, float radiusMeters, float strength, out string summary)
    {
        summary = "path paint requires a generated chunked planet";
        if (_surfaceProvider is not ChunkedSurfaceProvider chunkedProvider)
            return false;

        Vector3 localPoint = transform.InverseTransformPoint(worldPosition);
        if (localPoint.sqrMagnitude < 0.0001f)
        {
            summary = "path paint requires a position away from the planet center";
            return false;
        }

        bool painted = chunkedProvider.TryPaintSurfaceStateDisc(
            localPoint.normalized,
            Mathf.Max(radiusMeters, 0.1f),
            Mathf.Clamp01(strength),
            out summary);
        if (painted)
            _grass.DisposeControllers();
        return painted;
    }

    public bool TryPaintSurfacePathPatternAtWorldPosition(Vector3 worldPosition, float sizeMeters, float strength, out string summary)
    {
        summary = "path pattern requires a generated chunked planet";
        if (_surfaceProvider is not ChunkedSurfaceProvider chunkedProvider)
            return false;

        Vector3 localPoint = transform.InverseTransformPoint(worldPosition);
        if (localPoint.sqrMagnitude < 0.0001f)
        {
            summary = "path pattern requires a position away from the planet center";
            return false;
        }

        bool painted = chunkedProvider.TryPaintSurfaceStateTestPattern(
            localPoint.normalized,
            Mathf.Max(sizeMeters, 1f),
            Mathf.Clamp01(strength),
            out summary);
        if (painted)
            _grass.DisposeControllers();
        return painted;
    }

    public int ClearSurfacePaths()
    {
        if (_surfaceProvider is not ChunkedSurfaceProvider chunkedProvider)
            return 0;

        int cleared = chunkedProvider.ClearSurfaceStateMasks();
        if (cleared > 0)
            _grass.DisposeControllers();
        return cleared;
    }

    public string SurfacePathStatus()
    {
        if (_surfaceProvider is not ChunkedSurfaceProvider)
            return "path mask unavailable: active provider is not chunked";

        float debug = _terrainMaterial?.Material != null && _terrainMaterial.Material.HasProperty(_surfacePathDebugId)
            ? _terrainMaterial.Material.GetFloat(_surfacePathDebugId)
            : 0f;
        return $"path mask ready: R=paved, G=scorched; debug={(debug > 0.5f ? "hot-pink" : "off")}";
    }

    public string SetSurfacePathDebug(bool? enabled = null)
    {
        if (_terrainMaterial?.Material == null || !_terrainMaterial.Material.HasProperty(_surfacePathDebugId))
            return "path debug unavailable: terrain material has no _SurfacePathDebug";

        if (enabled.HasValue)
            _terrainMaterial.Material.SetFloat(_surfacePathDebugId, enabled.Value ? 1f : 0f);

        bool active = _terrainMaterial.Material.GetFloat(_surfacePathDebugId) > 0.5f;
        return $"path debug: {(active ? "hot-pink" : "off")}";
    }

    // --- Console commands -------------------------------------------------

    [ConsoleCommand("status", "Show active planet recipe, generated runtime, and diagnostic layout state.", MonoTargetType.Single)]
    string StatusCmd()
    {
        var sb = new System.Text.StringBuilder();
        PlanetSettings planetSource = GetPlanetSettingsSource();
        BiomeSettings biomeSource = GetBiomeSettingsSource();
        sb.Append("source=").Append(_recipe != null ? "recipe" : "planet-settings");
        sb.Append(", recipe=").Append(AssetName(_recipe));
        sb.Append(", planetSettings=").Append(AssetName(planetSource));
        sb.Append(", biomeSettings=").Append(AssetName(biomeSource));
        sb.Append(", diagnosticBiome=").Append(AssetName(_recipe != null ? _recipe.DiagnosticGridBiomeLayout : null));
        sb.Append(", diagnosticTerrain=").Append(AssetName(_recipe != null ? _recipe.DiagnosticTerrainLayout : null));
        sb.AppendLine();

        sb.Append("runtime: seed=").Append(Seed);
        sb.Append(", generating=").Append(_isGenerating);
        sb.Append(", renderMask=").Append(RenderMask);
        sb.Append(", perFaceResolution=").Append(PerFaceResolution);
        sb.Append(", generatedRadius=").Append(_lastGeneratedRadius.ToString("F2"));
        sb.Append(", seaLevelRadius=").Append(_lastSeaLevelRadius.ToString("F2"));
        sb.Append(", provider=").Append(_surfaceProvider != null ? _surfaceProvider.GetType().Name : "none");
        sb.AppendLine();

        if (TryGetSettings(out PlanetDto planet))
        {
            sb.Append("planetDto: radius=").Append(planet.PlanetRadius.ToString("F2"));
            sb.Append(", resolution=").Append(planet.Resolution);
            sb.Append(", maxDepth=").Append(planet.MaxChunkDepth);
            sb.Append(", oceans=").Append(planet.HasOceans);
            sb.Append(", oceanLevel=").Append(planet.OceanLevel.ToString("F3"));
            sb.Append(", diagnosticTerrain=").Append(DescribeDiagnosticTerrain(planet.DiagnosticTerrainLayout));
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("planetDto: unavailable");
        }

        if (TryGetSettings(out BiomeDto biome))
        {
            sb.Append("biomeDto: assignment=").Append(biome.AssignmentMode);
            sb.Append(", climateMap=").Append(biome.ClimateMapResolution);
            sb.Append(", voronoiSeeds=").Append(biome.VoronoiSeedCount);
            sb.Append(", diagnosticGrid=").Append(DescribeDiagnosticBiome(biome.DiagnosticGridLayout));
        }
        else
        {
            sb.Append("biomeDto: unavailable");
        }

        return sb.ToString();
    }

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

    static string AssetName(Object asset)
    {
        return asset != null ? asset.name : "none";
    }

    static bool TryGetSettings<T>(out T settings)
    {
        try
        {
            if (SettingsProvider.IsRegistered<T>())
            {
                settings = SettingsProvider.GetSettings<T>();
                return true;
            }
        }
        catch (System.Exception)
        {
        }

        settings = default;
        return false;
    }

    static string DescribeDiagnosticTerrain(DiagnosticTerrainLayoutDto layout)
    {
        if (layout == null)
            return "none";

        return $"face={(DiagnosticTerrainFace)layout.Face}, grid={layout.Columns}x{layout.Rows}, blend={layout.BlendWidth:F3}, fallback={(DiagnosticTerrainCell)layout.FallbackCell}";
    }

    static string DescribeDiagnosticBiome(DiagnosticGridBiomeLayoutDto layout)
    {
        if (layout == null)
            return "none";

        return $"face={(DiagnosticGridBiomeFace)layout.Face}, grid={layout.Columns}x{layout.Rows}, blend={layout.BlendWidth:F3}, fallbackId={layout.FallbackBiome}";
    }
}

[CommandPrefix("path")]
public static class SurfacePathDebugCommands
{
    [ConsoleCommand("paint", "Paint a soft paved path mask where the camera is aimed. Args: radiusMeters strength01.", MonoTargetType.Static)]
    public static string PaintCmd(float? radiusMeters = null, float? strength = null)
    {
        if (!TryGetPlanet(out Planet planet))
            return "path paint requires an active Planet";
        if (!TryGetCameraRay(out Ray ray, out string error))
            return error;

        float radius = Mathf.Clamp(radiusMeters ?? 5f, 0.25f, 250f);
        float alpha = Mathf.Clamp01(strength ?? 1f);
        return planet.TryPaintSurfacePathFromCamera(ray, radius, alpha, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("paint-here", "Paint a soft paved path mask under the camera. Args: radiusMeters strength01.", MonoTargetType.Static)]
    public static string PaintHereCmd(float? radiusMeters = null, float? strength = null)
    {
        if (!TryGetPlanet(out Planet planet))
            return "path paint-here requires an active Planet";
        if (!TryGetCameraTransform(out Transform cameraTransform, out string error))
            return error;

        float radius = Mathf.Clamp(radiusMeters ?? 8f, 0.25f, 250f);
        float alpha = Mathf.Clamp01(strength ?? 1f);
        return planet.TryPaintSurfacePathAtWorldPosition(cameraTransform.position, radius, alpha, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("pattern-here", "Paint deterministic path test patterns under the camera. Args: sizeMeters strength01.", MonoTargetType.Static)]
    public static string PatternHereCmd(float? sizeMeters = null, float? strength = null)
    {
        if (!TryGetPlanet(out Planet planet))
            return "path pattern-here requires an active Planet";
        if (!TryGetCameraTransform(out Transform cameraTransform, out string error))
            return error;

        float size = Mathf.Clamp(sizeMeters ?? 220f, 16f, 1000f);
        float alpha = Mathf.Clamp01(strength ?? 1f);
        return planet.TryPaintSurfacePathPatternAtWorldPosition(cameraTransform.position, size, alpha, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("clear", "Clear all painted surface path masks.", MonoTargetType.Static)]
    public static string ClearCmd()
    {
        if (!TryGetPlanet(out Planet planet))
            return "path clear requires an active Planet";

        int cleared = planet.ClearSurfacePaths();
        return $"cleared path masks on {cleared} chunks";
    }

    [ConsoleCommand("debug", "Toggle hot-pink path mask visualization.", MonoTargetType.Static)]
    public static string DebugCmd(bool? enabled = null)
    {
        return TryGetPlanet(out Planet planet)
            ? planet.SetSurfacePathDebug(enabled)
            : "path debug requires an active Planet";
    }

    [ConsoleCommand("status", "Show path mask runtime support status.", MonoTargetType.Static)]
    public static string StatusCmd()
    {
        return TryGetPlanet(out Planet planet)
            ? planet.SurfacePathStatus()
            : "path mask unavailable: no active Planet";
    }

    static bool TryGetPlanet(out Planet planet)
    {
        planet = null;
        if (ServiceLocator.TryGet(out IPlanet servicePlanet) && servicePlanet is Planet concrete)
        {
            planet = concrete;
            return true;
        }

        planet = Object.FindAnyObjectByType<Planet>();
        return planet != null;
    }

    static bool TryGetCameraRay(out Ray ray, out string error)
    {
        if (!TryGetCameraTransform(out Transform cameraTransform, out error))
        {
            ray = default;
            return false;
        }

        ray = new Ray(cameraTransform.position, cameraTransform.forward);
        error = null;
        return true;
    }

    static bool TryGetCameraTransform(out Transform cameraTransform, out string error)
    {
        cameraTransform = null;
        if (ServiceLocator.TryGet(out ICameraRigContext context) && context.CameraTransform != null)
            cameraTransform = context.CameraTransform;

        Camera camera = Camera.main;
        if (cameraTransform == null && camera != null)
            cameraTransform = camera.transform;

        error = cameraTransform == null ? "path paint requires an active camera" : null;
        return cameraTransform != null;
    }
}
