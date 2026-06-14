using System.Threading;
using UnityEngine;

[DefaultExecutionOrder(-9000)]
public class GameBootstrap : MonoBehaviour, IEarlyInitialize
{
    static GameBootstrap _instance;

    IDebugCommandProvider _debugCommandProvider;
    IGrassQualitySettings _grassQualitySettings;
    IInputMapService _inputMapService;
    bool _ownsGrassQualitySettings;

    public int EarlyPriority => 100;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureComponent<EventBusProcessor>();
    }

    void OnDestroy()
    {
        if (_instance != this)
            return;

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
        _instance = null;
    }

    public async Awaitable EarlyInitialize(CancellationToken cancellationToken)
    {
        if (_instance != this || _debugCommandProvider != null)
            return;

        var logger = ServiceLocator.Get<ILogger>();

        _debugCommandProvider = new DebugCommandProvider();
        _inputMapService = new InputMapService();

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

        DebugConsoleBootstrap.Initialize();

        logger.Log(LogLevel.Info, "Bootstrap", "Global services initialized.");

        await Awaitable.NextFrameAsync(cancellationToken);
    }

    private void EnsureComponent<T>() where T : Component
    {
        if (FindAnyObjectByType<T>() == null)
            gameObject.AddComponent<T>();
    }
}
