using System.Threading;
using UnityEngine;

public class GameBootstrap : MonoBehaviour, IEarlyInitialize
{
    [Header("Settings")]
    public int WorldSeed = 12345;

    ISeedProvider _seedProvider;
    IWorldActionManager _worldActionManager;
    IDebugCommandProvider _debugCommandProvider;

    public int EarlyPriority => 100;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (_seedProvider != null) ServiceLocator.Unregister<ISeedProvider>(_seedProvider);
        if (_worldActionManager != null) ServiceLocator.Unregister<IWorldActionManager>(_worldActionManager);
        if (_debugCommandProvider != null)
        {
            (_debugCommandProvider as System.IDisposable)?.Dispose();
            ServiceLocator.Unregister<IDebugCommandProvider>(_debugCommandProvider);
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

        ServiceLocator.Register<ISeedProvider>(_seedProvider);
        ServiceLocator.Register<IWorldActionManager>(_worldActionManager);
        ServiceLocator.Register<IDebugCommandProvider>(_debugCommandProvider);

        EnsureComponent<ShaderGlobalsController>();
        EnsureComponent<QualityController>();
        EnsureComponent<DebugInputRelay>();
        EnsureComponent<DebugCaptureController>();
        EnsureComponent<WaterWakeController>();

        logger.Log(LogLevel.Info, "Bootstrap", $"Services initialized. World seed: {WorldSeed}");

        await Awaitable.NextFrameAsync(cancellationToken);
    }

    private void EnsureComponent<T>() where T : Component
    {
        if (FindAnyObjectByType<T>() == null)
            gameObject.AddComponent<T>();
    }
}