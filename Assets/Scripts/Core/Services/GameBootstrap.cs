using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    [Header("Settings")]
    public int WorldSeed = 12345;
    public LogLevel MinLogLevel = LogLevel.Debug;

    void Awake()
    {
        var logger = new UnityLogger(MinLogLevel);
        ServiceLocator.Register<ILogger>(logger);

        var seedProvider = new SeedProvider(WorldSeed);
        ServiceLocator.Register<ISeedProvider>(seedProvider);

        var actionManager = new WorldActionManager(logger);
        ServiceLocator.Register<WorldActionManager>(actionManager);

        logger.Log(LogLevel.Info, "Bootstrap", $"Services initialized. World seed: {WorldSeed}");
    }

    void OnDestroy()
    {
        ServiceLocator.Clear();
    }
}
