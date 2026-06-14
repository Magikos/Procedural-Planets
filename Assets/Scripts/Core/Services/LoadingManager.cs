using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingManager : MonoBehaviour, ILoadingManager
{
    CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    bool _isTransitioning;
    bool _hasFatalFailure;
    ILoadingOverlay _overlay;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void CreateInstance()
    {
        if (FindAnyObjectByType<LoadingManager>() != null) return;

        var go = new GameObject("[LoadingManager]");
        go.AddComponent<LoadingManager>();
    }

    void Awake()
    {
        if (ServiceLocator.TryGet<ILoadingManager>(out _))
        {
            Destroy(gameObject);
            return;
        }

        ServiceLocator.Register<ILoadingManager>(this);
        DontDestroyOnLoad(gameObject);

        // Create the default overlay immediately so it can paint before any IEarlyInitialize runs.
        // A scene-placed ILoadingOverlay registered in ServiceLocator will replace it in Start().
        _overlay = new LoadingProgressBarOverlay();

        if (!ServiceLocator.HasActiveWorld)
            ServiceLocator.ActivateWorld(new WorldContext());
    }

    void OnEnable()
    {
        EventBus<ProgressEvent>.Listen(OnProgressEvent);
    }

    void OnDisable()
    {
        EventBus<ProgressEvent>.Unlisten(OnProgressEvent);
    }

    void Start()
    {
        // Allow a scene-placed ILoadingOverlay (registered in its own Awake) to take over.
        if (ServiceLocator.TryGet<ILoadingOverlay>(out var sceneOverlay) && sceneOverlay != _overlay)
        {
            _overlay.Dispose();
            _overlay = sceneOverlay;
        }

        if (_isTransitioning) return;
        _ = InitializeCurrentSceneAsync(_cancellationTokenSource.Token);
    }

    async Awaitable InitializeCurrentSceneAsync(CancellationToken cancellationToken)
    {
        _isTransitioning = true;
        bool initialized = false;

        try
        {
            var currentScene = SceneManager.GetActiveScene();
            if (!currentScene.IsValid())
                throw new System.InvalidOperationException(
                    "No valid active scene found during startup initialization. Check the scene setup.");

            _overlay.SetAlpha(1f);
            await Awaitable.NextFrameAsync(cancellationToken);

            await InitializeAsync(currentScene, includePersistentObjects: true, cancellationToken);
            EventBus<WorldReadyEvent>.Raise(new WorldReadyEvent(ServiceLocator.GetWorld()));

            await Awaitable.NextFrameAsync(cancellationToken);
            await Awaitable.NextFrameAsync(cancellationToken);

            await _overlay.FadeInAsync(cancellationToken);
            initialized = true;
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex)
        {
            if (ServiceLocator.HasActiveWorld)
                TeardownWorldSafely(ServiceLocator.GetWorld(), "startup failure");
            EnterFatalFailure("Startup initialization failed.", ex);
        }
        finally
        {
            if (initialized)
                _overlay.SetAlpha(0f);
            _isTransitioning = false;
        }
    }

    public async Awaitable<bool> TransitionToSceneAsync(string sceneName, bool useOverlay = true, CancellationToken cancellationToken = default)
    {
        return await TransitionToWorldAsync(
            WorldLoadRequest.ForScene(sceneName), useOverlay, cancellationToken);
    }

    public async Awaitable<bool> TransitionToSceneAsync(int buildIndex, bool useOverlay = true, CancellationToken cancellationToken = default)
    {
        return await TransitionToWorldAsync(
            WorldLoadRequest.ForBuildIndex(buildIndex), useOverlay, cancellationToken);
    }

    public async Awaitable<bool> TransitionToWorldAsync(
        WorldLoadRequest request,
        bool useOverlay = true,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new System.ArgumentNullException(nameof(request));
        if (request.SettingsSchemaVersion != WorldLoadRequest.CurrentSettingsSchemaVersion)
            throw new System.InvalidOperationException(
                $"Settings schema {request.SettingsSchemaVersion} is not supported; " +
                $"expected {WorldLoadRequest.CurrentSettingsSchemaVersion}. Migrate save data before loading.");
        if (_hasFatalFailure)
            return false;

        if (_isTransitioning)
        {
            LoggerProvider.Get().Log(LogLevel.Warning, "LoadingManager", "Already transitioning. Please wait.");
            return false;
        }

        if (cancellationToken.CanBeCanceled)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
            return await RunTransitionAsync(request, useOverlay, linkedCts.Token);
        }

        return await RunTransitionAsync(request, useOverlay, _cancellationTokenSource.Token);
    }

    async Awaitable<bool> RunTransitionAsync(
        WorldLoadRequest request,
        bool useOverlay,
        CancellationToken cancellationToken)
    {
        _isTransitioning = true;
        Scene oldScene = default;
        Scene newScene = default;
        IWorldContext oldWorld = null;
        IWorldContext newWorld = null;
        bool worldSwapCommitted = false;
        bool newSceneLoaded = false;

        try
        {
            oldScene = SceneManager.GetActiveScene();

            if (useOverlay)
                await _overlay.FadeOutAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            Time.timeScale = 0f;
            var asyncOp = request.SceneName != null
                ? SceneManager.LoadSceneAsync(request.SceneName, LoadSceneMode.Additive)
                : SceneManager.LoadSceneAsync(request.BuildIndex, LoadSceneMode.Additive);
            if (asyncOp == null)
                throw new System.InvalidOperationException(
                    $"Scene '{request.SceneName ?? $"build index {request.BuildIndex}"}' could not begin loading.");
            asyncOp.allowSceneActivation = false;

            while (asyncOp.progress < 0.9f)
                await Awaitable.NextFrameAsync();

            oldWorld = ServiceLocator.GetWorld();
            oldWorld.Cancel();
            SetSceneRootsActive(oldScene, false);
            oldWorld.Teardown();
            ServiceLocator.DeactivateWorld(oldWorld);

            newWorld = new WorldContext(request);
            ServiceLocator.ActivateWorld(newWorld);
            worldSwapCommitted = true;

            asyncOp.allowSceneActivation = true;
            while (!asyncOp.isDone)
                await Awaitable.NextFrameAsync();

            newScene = request.SceneName != null
                ? SceneManager.GetSceneByName(request.SceneName)
                : SceneManager.GetSceneByBuildIndex(request.BuildIndex);

            if (!newScene.IsValid())
                throw new System.InvalidOperationException(
                    $"Scene '{request.SceneName ?? $"build index {request.BuildIndex}"}' could not be found after async load. Verify it is added to Build Settings.");

            newSceneLoaded = true;
            SceneManager.SetActiveScene(newScene);

            await InitializeAsync(newScene, includePersistentObjects: false, newWorld.LifetimeToken);

            if (oldScene.IsValid() && oldScene != newScene)
                await UnloadSceneAsync(oldScene);

            oldWorld.Dispose();
            oldWorld = null;
            EventBus<WorldReadyEvent>.Raise(new WorldReadyEvent(newWorld));

            await Awaitable.NextFrameAsync();
            await Awaitable.NextFrameAsync();

            Time.timeScale = 1f;

            if (useOverlay)
                await _overlay.FadeInAsync(CancellationToken.None);

            return true;
        }
        catch (System.OperationCanceledException)
        {
            if (worldSwapCommitted)
                await AbortCommittedTransitionAsync(oldScene, oldWorld, newScene, newWorld, newSceneLoaded);
            return false;
        }
        catch (System.Exception ex)
        {
            if (worldSwapCommitted)
                await AbortCommittedTransitionAsync(oldScene, oldWorld, newScene, newWorld, newSceneLoaded);
            EnterFatalFailure("World transition failed.", ex);
            return false;
        }
        finally
        {
            if (!_hasFatalFailure)
            {
                Time.timeScale = 1f;
                _overlay.SetAlpha(0f);
            }
            _isTransitioning = false;
        }
    }

    static async Awaitable AbortCommittedTransitionAsync(
        Scene oldScene,
        IWorldContext oldWorld,
        Scene newScene,
        IWorldContext newWorld,
        bool newSceneLoaded)
    {
        newWorld?.Cancel();
        TeardownWorldSafely(newWorld, "failed new world");
        TeardownWorldSafely(oldWorld, "released old world");

        if (newSceneLoaded)
            await UnloadSceneAsync(newScene);
        if (oldScene.IsValid() && oldScene.isLoaded)
            await UnloadSceneAsync(oldScene);

        if (newWorld != null)
        {
            ServiceLocator.DeactivateWorld(newWorld);
            newWorld.Dispose();
        }

        oldWorld?.Dispose();
    }

    static void TeardownWorldSafely(IWorldContext world, string context)
    {
        if (world == null || world.IsDisposed)
            return;

        try
        {
            world.Teardown();
        }
        catch (System.Exception ex)
        {
            LoggerProvider.Get().LogException(
                "LoadingManager",
                new System.InvalidOperationException($"World teardown failed during {context}.", ex));
        }
    }

    static async Awaitable UnloadSceneAsync(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        var unloadOp = SceneManager.UnloadSceneAsync(scene);
        if (unloadOp == null)
            return;

        while (!unloadOp.isDone)
            await Awaitable.NextFrameAsync();
    }

    static void SetSceneRootsActive(Scene scene, bool active)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            roots[i].SetActive(active);
    }

    async Awaitable InitializeAsync(
        Scene scene,
        bool includePersistentObjects,
        CancellationToken cancellationToken)
    {
        var rootObjects = scene.GetRootGameObjects().AsEnumerable();
        if (includePersistentObjects)
        {
            var persistentScene = gameObject.scene;
            if (persistentScene.IsValid() && persistentScene != scene)
                rootObjects = rootObjects.Concat(persistentScene.GetRootGameObjects());
        }

        var allBehaviours = rootObjects
            .SelectMany(go => go.GetComponentsInChildren<MonoBehaviour>(true))
            .ToList();

        var tracker = new ProgressTracker();
        foreach (var reporter in allBehaviours.OfType<IProgressReporter>())
            tracker.Register(reporter);

        bool completed = false;
        try
        {
            var earlyInitializers = allBehaviours
                .OfType<IEarlyInitialize>()
                .OrderByDescending(i => i.EarlyPriority)
                .ToList();
            var earlyGraph = new InitGraph<IEarlyInitialize>(earlyInitializers, i => i.EarlyDependencies);

            LoggerProvider.Get().Log(LogLevel.Info, "LoadingManager", $"Early-initializing {earlyGraph.Order.Count} components");
            foreach (var initializer in earlyGraph.Order)
            {
                try
                {
                    TrackWorldInitializer(scene, initializer);
                    await initializer.EarlyInitialize(cancellationToken);
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
                catch (System.Exception ex)
                {
                    throw new System.InvalidOperationException(
                        $"Early initialization failed in {initializer.GetType().Name}.", ex);
                }
            }

            var lateInitializers = allBehaviours
                .OfType<ILateInitialize>()
                .OrderByDescending(i => i.LatePriority)
                .ToList();
            var lateGraph = new InitGraph<ILateInitialize>(lateInitializers, i => i.LateDependencies);

            LoggerProvider.Get().Log(LogLevel.Info, "LoadingManager", $"Late-initializing {lateGraph.Order.Count} components");
            foreach (var initializer in lateGraph.Order)
            {
                try
                {
                    TrackWorldInitializer(scene, initializer);
                    await initializer.LateInitialize(cancellationToken);
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
                catch (System.Exception ex)
                {
                    throw new System.InvalidOperationException(
                        $"Late initialization failed in {initializer.GetType().Name}.", ex);
                }
            }

            tracker.Complete();
            completed = true;
        }
        finally
        {
            if (!completed)
                tracker.Abort();
        }
    }

    static void TrackWorldInitializer<TInitializer>(Scene scene, TInitializer initializer)
    {
        if (initializer is not Component component || component.gameObject.scene != scene)
            return;

        ServiceLocator.GetWorld().TrackInitializer(initializer);
    }

    void EnterFatalFailure(string message, System.Exception exception)
    {
        _hasFatalFailure = true;
        if (ServiceLocator.HasActiveWorld)
            ServiceLocator.GetWorld().Cancel();
        Time.timeScale = 0f;
        _overlay.ShowFatalError(message);
        LoggerProvider.Get().LogException("LoadingManager", exception);
    }

    void OnProgressEvent(ProgressEvent evt)
    {
        _overlay?.SetProgress(evt.Progress, evt.Message);
    }

    void OnDestroy()
    {
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        _overlay?.Dispose();
        _overlay = null;

        if (ServiceLocator.HasActiveWorld)
        {
            IWorldContext world = ServiceLocator.GetWorld();
            ServiceLocator.DeactivateWorld(world);
            TeardownWorldSafely(world, "loading manager shutdown");
            try
            {
                world.Dispose();
            }
            catch (System.Exception ex)
            {
                LoggerProvider.Get().LogException("LoadingManager", ex);
            }
        }

        ServiceLocator.Unregister<ILoadingManager>(this);
    }
}
