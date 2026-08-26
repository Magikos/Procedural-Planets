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
        typeof(WaterDto),
        typeof(ScatterLibraryDto),
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
    ScatterField _scatter;
    ScatterRenderer _scatterRenderer;
    PlanetWaterSurface _waterSurface;
    WaterQueryService _waterQuery;
    PlanetTerrainMaterial _terrainMaterial;
    SurfaceEditController _surfaceEdits;
    ScatterHarvestStore _harvestStore;
    WorldDeltaLog _deltaLog;
    InventoryService _inventory;
    HarvestInteractor _harvestInteractor;
    StumpRenderer _stumpRenderer;
    LogRenderer _logRenderer;
    TreeFallSystem _treeFall;
    ChopFxSystem _chopFx;
    ScatterDebugReporter _scatterDebug;
    TreePreview _treePreview;
    CreatureResidencyService _creatures;
    CreatureView _creatureView;
    ThreatRegistry _threats;

    static readonly int _planetCenterId = Shader.PropertyToID(ShaderGlobalIds.PlanetCenter);
    static readonly int _seaLevelRadiusId = Shader.PropertyToID(ShaderGlobalIds.SeaLevelRadius);
    static readonly int _densityOriginRadiusId = Shader.PropertyToID(ShaderGlobalIds.DensityOriginRadius);

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
        EnsureRuntimeOwners();
        context.Register<IPlanet>(this);
        context.Register<IPlanetSurfaceSampler>(this);
        context.Register<IWaterQueryService>(_waterQuery);
        context.Register<IPlanetSurfaceRaycaster>(this);
        context.Register<ISurfacePathBrushService>(_surfaceEdits);
        context.Register(_surfaceEdits);
        context.Register<IWorldDeltaLog>(_deltaLog);
        context.Register<IClimateSampler>(this);
        context.Register<IGrassRuntimeControl>(this);
        context.Register<IGrassNearFieldStatsProvider>(_grass);
        context.Register<IScatterDebugReport>(_scatterDebug);
        context.Register(_creatures);
        context.Register(_threats);

        // Harvest interactor (POC): picker + verb wired to this world's scatter cache, harvest store, and
        // inventory. The ScatterLibraryDto is NOT registered yet at world-service registration (it registers
        // before settings freeze), so resolve it lazily — the interactor is only used post-generation, when
        // the DTO exists. IsRegistered avoids the throw if a harvest is somehow attempted before then.
        // Never let the POC harvest wiring crash boot — register defensively; if anything is off, harvesting
        // is simply disabled this session and the reason is logged.
        try
        {
            // Build ONCE and register the same stable instance each call — RegisterWorldServices runs more
            // than once, and the context throws on re-registering a different instance (a fresh `new` each
            // call is exactly what crashed boot). Deps are stable (cache/store/inventory) and the library is
            // resolved lazily, so one interactor is correct across worlds.
            if (_harvestInteractor == null)
            {
                System.Func<ScatterLibraryDto> libraryFn = () =>
                    SettingsProvider.IsRegistered<ScatterLibraryDto>() ? SettingsProvider.GetSettings<ScatterLibraryDto>() : null;
                ScatterTileCache scatterCache = _scatterRenderer.Cache;
                var picker = new ScatterPicker(scatterCache, libraryFn);
                var harvest = new HarvestService(
                    pick => _harvestStore.RecordStump(pick),
                    (proto, id) => scatterCache.RemoveInstance(proto, id),
                    (item, count) => _inventory.Add(item, count),
                    proto =>
                    {
                        ScatterLibraryDto lib = libraryFn();
                        return lib?.Prototypes != null && (uint)proto < (uint)lib.Prototypes.Length
                            ? new ProtoHarvestInfo(lib.Prototypes[proto].Interaction, lib.Prototypes[proto].DisplayName)
                            : default;
                    },
                    id => _harvestStore.RecordDug(id));
                _harvestInteractor = new HarvestInteractor(picker, harvest, _harvestStore, Logger);
            }
            context.Register(_harvestInteractor);
        }
        catch (System.Exception e)
        {
            LoggerProvider.LogException("Harvest", e);
            Logger.Log(LogLevel.Warning, "Harvest", "Harvest interactor not registered; harvesting disabled this session.");
        }
    }

    void EnsureRuntimeOwners()
    {
        EnsureGrassCoordinator();
        _scatter ??= new ScatterField(transform, new AnalyticGroundSampler(_shapeGenerator), _colorGenerator);
        _scatterRenderer ??= new ScatterRenderer(_scatter, transform);
        _waterSurface ??= new PlanetWaterSurface(transform);
        _waterQuery ??= new WaterQueryService(transform);
        _terrainMaterial ??= new PlanetTerrainMaterial(Logger);
        _surfaceEdits ??= new SurfaceEditController(transform, Logger, () => _grass.InvalidateSurfaceMasks());
        _deltaLog ??= new WorldDeltaLog(Logger);
        _harvestStore ??= new ScatterHarvestStore(Logger);
        _inventory ??= new InventoryService();
        _stumpRenderer ??= new StumpRenderer(_harvestStore, transform,
            () => SettingsProvider.IsRegistered<ScatterLibraryDto>() ? SettingsProvider.GetSettings<ScatterLibraryDto>() : null);
        _logRenderer ??= new LogRenderer(_harvestStore, transform);
        _treeFall ??= new TreeFallSystem(transform,
            () => SettingsProvider.IsRegistered<ScatterLibraryDto>() ? SettingsProvider.GetSettings<ScatterLibraryDto>() : null,
            _harvestStore);
        _chopFx ??= new ChopFxSystem(transform);
        _scatterDebug ??= new ScatterDebugReporter();
        _treePreview ??= new TreePreview(transform);
        _creatures ??= new CreatureResidencyService(transform, this, Logger);
        _creatureView ??= new CreatureView(transform);
        _threats ??= new ThreatRegistry();
        _scatterRenderer.Cache.SetHarvestStore(_harvestStore);
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

    WaterSettings GetWaterSettingsSource() =>
        _recipe != null ? _recipe.WaterSettingsSource : _planetSettings?.WaterSettings;

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
        if (!settings.IsRegistered<WaterDto>())
        {
            WaterSettings waterSource = GetWaterSettingsSource();
            if (waterSource == null)
                throw new System.InvalidOperationException(
                    "Planet requires a WaterSettings asset on PlanetSettings or PlanetRecipe.");
            settings.Register(_recipe != null ? _recipe.ToWaterDto() : WaterDto.From(waterSource));
        }
        if (!settings.IsRegistered<ScatterLibraryDto>())
        {
            var scatterLib = Resources.Load<ScatterLibrary>("Settings/ScatterLibrary");
            if (scatterLib != null)
            {
                // ApplyAll is the single composition point, shared with the runtime toggle path. Composing the
                // chain here separately is what once dropped plants from every booted world.
                settings.Register(TreeInjection.ApplyAll(ScatterLibraryDto.From(scatterLib)));
            }
            else
            {
                // Scatter is optional decoration: a missing library is a valid world with no props,
                // so boot with an empty DTO instead of failing the whole scene.
                Logger.Log(LogLevel.Info, "Scatter", "No Resources/Settings/ScatterLibrary asset; scatter disabled.");
                settings.Register(new ScatterLibraryDto(System.Array.Empty<ScatterPrototypeDto>()));
            }
        }
        if (!settings.IsRegistered<CreatureLibraryDto>())
        {
            var creatureLib = Resources.Load<CreatureLibrary>("Settings/CreatureLibrary");
            if (creatureLib == null)
                Logger.Log(LogLevel.Info, "Creature",
                    "No Resources/Settings/CreatureLibrary asset; using the built-in placeholder species.");
            settings.Register(CreatureLibraryDto.From(creatureLib));
        }
        if (!settings.IsRegistered<FactionRelationsDto>())
            settings.Register(FactionRelationsDto.From(
                Resources.Load<FactionRelations>("Settings/FactionRelations")));
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
        _treeFall?.Dispose();
        _treeFall = null;
        _chopFx?.Dispose();
        _chopFx = null;
        _treePreview?.Dispose();
        _treePreview = null;
        _logRenderer?.Dispose();
        _logRenderer = null;
        _stumpRenderer?.Dispose();
        _stumpRenderer = null;
        _scatterRenderer?.Dispose();
        _scatterRenderer = null;
        _scatter?.Dispose();
        _scatter = null;
        _scatterDebug = null; // world-scoped registration; the context drops it on teardown
        _creatureView?.Dispose();
        _creatureView = null;
        _threats?.Clear();
        _threats = null;
        _creatures?.Dispose();
        _creatures = null;
        _climateMapGpuData?.Dispose();
        _climateMapGpuData = null;
        _surfaceProvider?.Dispose();
        _surfaceProvider = null;
        _perFaceProvider = null;
        _deltaLog?.Dispose();   // flushes to the platter; a chop must survive a quit
        _deltaLog = null;
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
        _scatterRenderer?.Render(_observerCamera);
        _stumpRenderer?.Render(_observerCamera);
        _logRenderer?.Render(_observerCamera);
        _surfaceEdits?.TickRegrowth();

        // The residency service is authority code and must never see a camera. This is the boundary: the host
        // resolves an observer POSITION here and hands over a Vector3, which is the same thing a dedicated
        // server would pass from a player's transform.
        if (_creatures != null)
        {
            _creatures.Tick(_observerCamera.transform.position, Time.deltaTime);
            _creatureView?.Sync(_creatures.Live, _creatures.Library);
        }
    }

    async Awaitable InitializeAsync(IProgressHandle progress, CancellationToken ct)
    {
        progress?.Report(0f, "Resetting planet...");
        _grass.DisposeControllers();
        _scatter?.Reset();
        _scatterRenderer?.Reset();
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

            // Identify the water bodies (WaterBodyMap) before the biome bake + scatter read them, so lakes
            // biome + scatter differently from the ocean. Pure heightfield sampling, so it runs off the main
            // thread; self-limiting (only small flood-fill components become lakes).
            var lakeGround = new AnalyticGroundSampler(_shapeGenerator);
            float lakeBaseRadius = lakeGround.PlanetRadius;
            WaterBodyMap.Current = null; // a cancelled generate must not leave the previous world visible
            await Awaitable.BackgroundThreadAsync();
            WaterBodyMap waterBodies = WaterBodyMap.Build(lakeGround, lakeBaseRadius, BiomeConstants.OceanThreshold);
            await Awaitable.MainThreadAsync();
            if (this == null) return;
            WaterBodyMap.Current = waterBodies;
            if (waterBodies?.Bodies != null)
            {
                Logger.Log(LogLevel.Debug, "Planet",
                    $"Water bodies: {waterBodies.Bodies.CountOf(WaterBodyKind.Lake)} lake(s), " +
                    $"{waterBodies.Bodies.CountOf(WaterBodyKind.Ocean)} ocean(s), catalog v{waterBodies.Bodies.Version}.");
                if (waterBodies.SeamAsymmetryCount > 0)
                    Logger.Log(LogLevel.Warning, "Planet",
                        $"WaterBodyMap seam adjacency asymmetric in {waterBodies.SeamAsymmetryCount} case(s); a body may split at a cube seam.");
            }
            long lakeMs = phaseTimer.ElapsedMilliseconds;
            phaseTimer.Restart();

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
            phaseTimer.Restart();
            var finalizeStep = System.Diagnostics.Stopwatch.StartNew();
            _grass.Configure(_surfaceProvider as ChunkedSurfaceProvider,
                _colorGenerator.SurfaceArrays, Seed, _observerCamera, _terrainMaterial.Material);
            long grassMs = finalizeStep.ElapsedMilliseconds;
            finalizeStep.Restart();
            // Atmosphere is rendered by AtmosphereController + AtmosphereRenderFeature (post-process).

            var planet = SettingsProvider.GetSettings<PlanetDto>();
            float scaledRadius = planet.PlanetRadius * (1 + _shapeGenerator.ElevationMax);
            float seaLevelRadius = planet.PlanetRadius * (1 + planet.OceanLevel);
            _lastGeneratedRadius = scaledRadius;
            _lastSeaLevelRadius = seaLevelRadius;
            UploadCorePlanetShaderGlobals(seaLevelRadius);
            _progressHandle.Report(1f, "Planet ready");
            long shaderGlobalsMs = finalizeStep.ElapsedMilliseconds;
            finalizeStep.Restart();
            await Awaitable.NextFrameAsync(ct);
            // After the last cancellable await: a cancelled generation never publishes readiness,
            // so scatter is only configured for a generation that actually reached this point.
            _deltaLog.Open(System.IO.Path.Combine(Application.persistentDataPath, "ProceduralPlanets"), "world-" + Seed);
            _harvestStore.Configure(Seed, _deltaLog);
            long harvestMs = finalizeStep.ElapsedMilliseconds;
            finalizeStep.Restart();
            // Surface edits read the same log, so they configure after it opens rather than before.
            _surfaceEdits.Configure(_surfaceProvider as ChunkedSurfaceProvider, _terrainMaterial.Material, Seed, _deltaLog);
            int replayedSurfaceEdits = _surfaceEdits.ReplayStamps(clearFirst: false);
            if (replayedSurfaceEdits > 0)
                Logger.Log(LogLevel.Debug, "Planet", $"Replayed {replayedSurfaceEdits} saved surface edit(s).");
            long surfaceEditsMs = finalizeStep.ElapsedMilliseconds;
            finalizeStep.Restart();
            _scatter.Configure(Seed, planet.PlanetRadius, seaLevelRadius, planet.HasOceans);
            // Same body map and surface offset the water mesh was built from, so a query and the surface it
            // describes cannot disagree.
            _waterQuery.Configure(WaterBodyMap.Current, new AnalyticGroundSampler(_shapeGenerator),
                planet.PlanetRadius, planet.OceanLevel,
                WaterMeshBuilder.SurfaceOffsetFor(planet.PlanetRadius));
            long scatterMs = finalizeStep.ElapsedMilliseconds;
            finalizeStep.Restart();
            _scatterRenderer.Configure();
            // Creature residency reads the same log the harvest store does, so it configures after it opens.
            // The view is dropped with it: DestroyChildren already took its bodies, and a new world's
            // creatures are different animals in different places.
            _creatureView.Dispose();
            // Same IBiomeProvider the terrain bake and scatter placement read, so a creature cannot disagree
            // with the ground about which biome it is standing in.
            _creatures.Configure(Seed, _deltaLog, _colorGenerator, _threats, planet.PlanetRadius, seaLevelRadius);
            long scatterRendererMs = finalizeStep.ElapsedMilliseconds;
            finalizeStep.Restart();
            EventBus<PlanetGeneratedEvent>.Raise(new PlanetGeneratedEvent(transform.position, scaledRadius, seaLevelRadius, _shapeGenerator.ElevationMin, _shapeGenerator.ElevationMax));
            long generatedEventMs = finalizeStep.ElapsedMilliseconds;
            long finalizeMs = phaseTimer.ElapsedMilliseconds;
            Logger.Log(
                LogLevel.Debug,
                "Planet",
                $"Finalize timings: grass={grassMs}ms, surfaceEdits={surfaceEditsMs}ms, " +
                $"shaderGlobals={shaderGlobalsMs}ms, harvest={harvestMs}ms, scatter={scatterMs}ms, " +
                $"scatterRenderer={scatterRendererMs}ms, generatedEvent={generatedEventMs}ms, " +
                $"total={finalizeMs}ms");
            Logger.Log(LogLevel.Debug, "Planet", $"Generated planet with seed {Seed}, mode {planet.Resolution}, perFaceResolution {PerFaceResolution}, maxChunkDepth {planet.MaxChunkDepth}, burst {Unity.Burst.BurstCompiler.IsEnabled}, radius {scaledRadius:F1}");
            Logger.Log(
                LogLevel.Debug,
                "Planet",
                $"Generation timings: initialize={initializationMs}ms, terrain={terrainMs}ms, " +
                $"lake={lakeMs}ms, colors={colorsMs}ms, climate={climateMs}ms, water={waterMs}ms, " +
                $"finalize={finalizeMs}ms, total={totalTimer.ElapsedMilliseconds}ms");
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

    public void Rebuild() => _grass.Rebuild();

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

    // Redo only the water: solve the bodies again and rebuild the surface against the terrain that is
    // already generated. Everything upstream - the shape generator, the chunk provider and its face
    // samplers - is untouched and still valid, so this skips the terrain mesh, the biome bake, the grass
    // atlases and the scatter configure that dominate a full generate.
    //
    // Exists because verifying a water change through planet.generate costs minutes, and almost every
    // question in this area is settled by numbers the solve produces rather than by a fresh planet. Biome
    // colours and scatter placement do NOT update here: they bake against the water map during generation,
    // so a change that moves a shoreline still needs a full generate to be seen in the ground.
    [ConsoleCommand("rebuild-water", "Re-solve water bodies and rebuild the water mesh against the existing terrain. Much faster than planet.generate; does not re-bake biome colours or scatter.", MonoTargetType.Single)]
    async Awaitable<string> RebuildWaterCmd(CancellationToken ct = default)
    {
        if (IsGenerating)
            throw new System.InvalidOperationException("planet generation already in progress");
        if (_shapeGenerator == null || _surfaceProvider == null || _waterSurface == null)
            return "planet.rebuild-water: no generated planet to rebuild against — run planet.generate first";

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var ground = new AnalyticGroundSampler(_shapeGenerator);
        float baseRadius = ground.PlanetRadius;

        WaterBodyMap.Current = null;
        await Awaitable.BackgroundThreadAsync();
        WaterBodyMap rebuilt = WaterBodyMap.Build(ground, baseRadius, BiomeConstants.OceanThreshold);
        await Awaitable.MainThreadAsync();
        if (this == null) return "planet.rebuild-water: planet destroyed mid-rebuild";
        WaterBodyMap.Current = rebuilt;
        long solveMs = timer.ElapsedMilliseconds;

        await _waterSurface.GenerateAsync(
            _surfaceProvider.GetFaceMeshSamplers(),
            _colorGenerator?.ClimateProvider,
            PerFaceResolution,
            null,
            ct);

        WaterBodyCatalog catalog = rebuilt?.Bodies;
        return catalog == null
            ? $"planet.rebuild-water: no bodies solved ({timer.ElapsedMilliseconds} ms)"
            : $"planet.rebuild-water: {catalog.CountOf(WaterBodyKind.Lake)} lake(s), {catalog.CountOf(WaterBodyKind.Ocean)} ocean(s), " +
              $"catalog v{catalog.Version}, seam {rebuilt.SeamAsymmetryCount}, drained {rebuilt.DrainedBasinCount} — " +
              $"solve {solveMs} ms, total {timer.ElapsedMilliseconds} ms";
    }

    static string AssetName(Object asset)
    {
        return asset != null ? asset.name : "none";
    }

    static bool TryGetSettings<T>(out T settings)
    {
        if (SettingsProvider.IsRegistered<T>())
        {
            try
            {
                settings = SettingsProvider.GetSettings<T>();
                return true;
            }
            catch (System.Exception e)
            {
                // Registered but failed to resolve is a real lifecycle fault (disposed / misregistered
                // service), not the ordinary "no such setting" case the IsRegistered check already covers.
                LoggerProvider.Log(LogLevel.Warning, "Planet",
                    $"settings {typeof(T).Name} is registered but failed to resolve: {e.Message}");
            }
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
