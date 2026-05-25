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

        var debugCommandProvider = new DebugCommandProvider();
        ServiceLocator.Register<IDebugCommandProvider>(debugCommandProvider);

        EnsureComponent<ShaderGlobalsController>();
        EnsureComponent<QualityController>();
        EnsureComponent<DebugInputRelay>();
        EnsureComponent<DebugCaptureController>();

        AddOptionalComponent("WaterWakeController");

        logger.Log(LogLevel.Info, "Bootstrap", $"Services initialized. World seed: {WorldSeed}");
    }

    void OnDestroy()
    {
        ServiceLocator.Clear();
    }

    void AddOptionalComponent(string typeName)
    {
        System.Type componentType = System.Type.GetType(typeName);
        if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            return;

        if (gameObject.GetComponent(componentType) == null)
            gameObject.AddComponent(componentType);
    }

    void EnsureComponent<T>() where T : Component
    {
        if (FindAnyObjectByType<T>() == null)
            gameObject.AddComponent<T>();
    }
}
