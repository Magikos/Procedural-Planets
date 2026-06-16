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
            ServiceLocator.Unregister<IDebugCommandProvider>(_debugCommandProvider);
        if (_inputMapService != null)
            ServiceLocator.Unregister<IInputMapService>(_inputMapService);

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

        // These MonoBehaviours must exist before any scene content runs. They cannot
        // be placed in the scene directly because GameBootstrap uses DontDestroyOnLoad
        // and must own the full debug/input/render lifecycle.
        EnsureComponent<ShaderGlobalsController>();   // writes _GameTime and transient debug globals each LateUpdate
        EnsureComponent<QualityController>();         // reads IGrassQualitySettings and pushes quality shader globals
        EnsureComponent<DebugInputRelay>();           // routes F-key presses to EventBus<DebugCommandRequestedEvent>
        EnsureComponent<DebugCaptureController>();    // orchestrates F10 captures and the debug overlay

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
