using System.Threading;
using UnityEngine;

public class SceneBootstrap : MonoBehaviour, IEarlyInitialize
{
    [Header("Settings")]
    public int WorldSeed = 12345;

    ISeedProvider _seedProvider;
    IWorldActionManager _worldActionManager;

    public int EarlyPriority => 50;

    void OnDestroy()
    {
        if (_seedProvider != null) ServiceLocator.Unregister<ISeedProvider>(_seedProvider);
        if (_worldActionManager != null) ServiceLocator.Unregister<IWorldActionManager>(_worldActionManager);
    }

    public async Awaitable EarlyInitialize(CancellationToken cancellationToken)
    {
        var logger = ServiceLocator.Get<ILogger>();

        _seedProvider = new SeedProvider(WorldSeed);
        _worldActionManager = new WorldActionManager(logger);

        ServiceLocator.Register<ISeedProvider>(_seedProvider);
        ServiceLocator.Register<IWorldActionManager>(_worldActionManager);

        EnsureComponent<WaterWakeController>();
        EnsureComponent<ScaleReferenceMarkers>();

        logger.Log(LogLevel.Info, "Bootstrap", $"Scene services initialized. World seed: {WorldSeed}");

        await Awaitable.NextFrameAsync(cancellationToken);
    }

    private void EnsureComponent<T>() where T : Component
    {
        if (FindAnyObjectByType<T>() == null)
            gameObject.AddComponent<T>();
    }
}
