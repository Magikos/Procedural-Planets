using System.Threading;
using UnityEngine;

public class GameBootstrap : MonoBehaviour, IEarlyInitialize
{
    [Header("Settings")]
    public int WorldSeed = 12345;

    ISeedProvider _seedProvider;
    IWorldActionManager _worldActionManager;
    IDebugCommandProvider _debugCommandProvider;
    IGrassQualitySettings _grassQualitySettings;
    IInputMapService _inputMapService;
    bool _ownsGrassQualitySettings;

    public int EarlyPriority => 100;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (_seedProvider != null) ServiceLocator.Unregister<ISeedProvider>(_seedProvider);
        if (_worldActionManager != null) ServiceLocator.Unregister<IWorldActionManager>(_worldActionManager);
        if (_ownsGrassQualitySettings && _grassQualitySettings != null)
            ServiceLocator.Unregister<IGrassQualitySettings>(_grassQualitySettings);
        if (_debugCommandProvider != null)
        {
            (_debugCommandProvider as System.IDisposable)?.Dispose();
            ServiceLocator.Unregister<IDebugCommandProvider>(_debugCommandProvider);
        }
        if (_inputMapService != null)
        {
            (_inputMapService as System.IDisposable)?.Dispose();
            ServiceLocator.Unregister<IInputMapService>(_inputMapService);
        }

        EventBusRegistry.ClearAll();
        EventBusProcessor.ClearProcessors();
    }

    public async Awaitable EarlyInitialize(CancellationToken cancellationToken)
    {
        var logger = ServiceLocator.Get<ILogger>();

        _seedProvider = new SeedProvider(WorldSeed);
        _worldActionManager = new WorldActionManager(logger);
        _debugCommandProvider = new DebugCommandProvider();
        _inputMapService = new InputMapService();

        ServiceLocator.Register<ISeedProvider>(_seedProvider);
        ServiceLocator.Register<IWorldActionManager>(_worldActionManager);
        ServiceLocator.Register<IDebugCommandProvider>(_debugCommandProvider);
        ServiceLocator.Register<IInputMapService>(_inputMapService);
        if (!ServiceLocator.TryGet(out _grassQualitySettings))
        {
            _grassQualitySettings = new DefaultGrassQualitySettings();
            ServiceLocator.Register<IGrassQualitySettings>(_grassQualitySettings);
            _ownsGrassQualitySettings = true;
        }

        EnsureComponent<ShaderGlobalsController>();
        EnsureComponent<QualityController>();
        EnsureComponent<DebugInputRelay>();
        EnsureComponent<DebugCaptureController>();
        EnsureComponent<WaterWakeController>();
        EnsureComponent<ScaleReferenceMarkers>();

        DebugConsoleBootstrap.Initialize();

        logger.Log(LogLevel.Info, "Bootstrap", $"Services initialized. World seed: {WorldSeed}");

        await Awaitable.NextFrameAsync(cancellationToken);
    }

    private void EnsureComponent<T>() where T : Component
    {
        if (FindAnyObjectByType<T>() == null)
            gameObject.AddComponent<T>();
    }
}
