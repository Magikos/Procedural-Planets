using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

public class LoadingManager : MonoBehaviour, ILoadingManager
{
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private bool _isTransitioning = false;
    private Material _overlayMaterial;
    private float _overlayAlpha;
    private float _targetProgress;
    private float _displayProgress;
    private int _lastDisplayedPercent = -1;
    private SDFTextRenderer _messageRenderer;
    private SDFTextRenderer _percentRenderer;
    private bool _hasFatalFailure;

    private const float FadeDuration = 0.35f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateInstance()
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

        if (!ServiceLocator.HasActiveWorld)
            ServiceLocator.ActivateWorld(new WorldContext());
    }

    void OnEnable()
    {
        EventBus<ProgressEvent>.Listen(OnProgressEvent);
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    void OnDisable()
    {
        EventBus<ProgressEvent>.Unlisten(OnProgressEvent);
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    private void CreateOverlayMaterial()
    {
        if (_overlayMaterial != null) return;

        var shader = Shader.Find("Hidden/LoadingOverlay");
        if (shader == null)
        {
            LoggerProvider.Get().Log(LogLevel.Error, "LoadingManager", "Hidden/LoadingOverlay shader not found. Ensure Assets/Graphics/Shaders/LoadingOverlay.shader is in the project.");
            return;
        }
        _overlayMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        // Set up SDF text renderers for progress labels.
        // The SDFFontAsset must be in Assets/Resources/ with the name "DefaultFont".
        // Text is silently skipped if the asset is missing (graceful degradation).
        var font = Resources.Load<SDFFontAsset>("DefaultFont");
        _messageRenderer = new SDFTextRenderer(font);
        _percentRenderer = new SDFTextRenderer(font);
    }

    // Injected at the end of every camera render via RenderPipelineManager.
    // Draws a fullscreen black quad (with optional progress bar) on the main camera only,
    // followed by the SDF text label (if a font asset is available).
    private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (_overlayAlpha <= 0.001f) return;
        if (_overlayMaterial == null) return;
        if (cam != Camera.main) return;

        _displayProgress = Mathf.Lerp(_displayProgress, _targetProgress, Time.unscaledDeltaTime * 8f);

        _overlayMaterial.SetFloat("_Alpha", _overlayAlpha);
        _overlayMaterial.SetFloat("_Progress", _displayProgress);

        var cmd = new CommandBuffer { name = "LoadingOverlay" };
        try
        {
            // 1. Fullscreen overlay + progress bar.
            cmd.DrawProcedural(Matrix4x4.identity, _overlayMaterial, 0, MeshTopology.Triangles, 3);

            // 2. Message text — centred above the bar (baseline y=0.530, bar top y=0.513).
            if (_messageRenderer != null)
            {
                _messageRenderer.SetAlpha(_overlayAlpha);
                _messageRenderer.Draw(cmd);
            }

            // 3. Percentage — right of the bar, vertically centred on it.
            if (_percentRenderer != null)
            {
                int pct = Mathf.RoundToInt(_displayProgress * 100);
                if (pct != _lastDisplayedPercent)
                {
                    _percentRenderer.SetText($"{pct}%", 0.862f, 0.341f, 0.025f);
                    _lastDisplayedPercent = pct;
                }
                _percentRenderer.SetAlpha(_overlayAlpha);
                _percentRenderer.Draw(cmd);
            }

            ctx.ExecuteCommandBuffer(cmd);
            ctx.Submit();
        }
        finally
        {
            cmd.Release();
        }
    }

    void Start()
    {
        CreateOverlayMaterial();
        if (_isTransitioning) return;
        _ = InitializeCurrentSceneAsync(_cancellationTokenSource.Token);
    }

    private async Awaitable InitializeCurrentSceneAsync(CancellationToken cancellationToken)
    {
        _isTransitioning = true;
        bool initialized = false;

        try
        {
            var currentScene = SceneManager.GetActiveScene();
            if (!currentScene.IsValid())
                throw new System.InvalidOperationException(
                    "No valid active scene found during startup initialization. Check the scene setup.");

            SetOverlay(1f);
            await Awaitable.NextFrameAsync(cancellationToken); // let the opaque overlay render

            await InitializeAsync(currentScene, includePersistentObjects: true, cancellationToken);
            EventBus<WorldReadyEvent>.Raise(new WorldReadyEvent(ServiceLocator.GetWorld()));

            await Awaitable.NextFrameAsync(cancellationToken);
            await Awaitable.NextFrameAsync(cancellationToken);

            await FadeInAsync(cancellationToken);
            initialized = true;
        }
        catch (System.OperationCanceledException) { } // expected during app teardown
        catch (System.Exception ex)
        {
            if (ServiceLocator.HasActiveWorld)
                TeardownWorldSafely(ServiceLocator.GetWorld(), "startup failure");
            EnterFatalFailure("Startup initialization failed.", ex);
        }
        finally
        {
            if (initialized)
                SetOverlay(0f);
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

    private async Awaitable<bool> RunTransitionAsync(
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
                await FadeOutAsync(cancellationToken);

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

            // Wait for two frames to ensure all pending operations settle before fading back in
            await Awaitable.NextFrameAsync();
            await Awaitable.NextFrameAsync();

            Time.timeScale = 1f;

            if (useOverlay)
                await FadeInAsync(CancellationToken.None);

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
                SetOverlay(0f);
            }
            _isTransitioning = false;
        }
    }

    private static async Awaitable AbortCommittedTransitionAsync(
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

    private static void TeardownWorldSafely(IWorldContext world, string context)
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

    private static async Awaitable UnloadSceneAsync(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        var unloadOp = SceneManager.UnloadSceneAsync(scene);
        if (unloadOp == null)
            return;

        while (!unloadOp.isDone)
            await Awaitable.NextFrameAsync();
    }

    private static void SetSceneRootsActive(Scene scene, bool active)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            roots[i].SetActive(active);
    }

    private async Awaitable FadeInAsync(CancellationToken cancellationToken)
    {
        float elapsed = 0f;
        while (elapsed < FadeDuration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _overlayAlpha = Mathf.Clamp01(1f - elapsed / FadeDuration);
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            await Awaitable.NextFrameAsync(cancellationToken);
        }

        _overlayAlpha = 0f;
    }

    private async Awaitable FadeOutAsync(CancellationToken cancellationToken)
    {
        float elapsed = 0f;
        while (elapsed < FadeDuration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _overlayAlpha = Mathf.Clamp01(elapsed / FadeDuration);
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            await Awaitable.NextFrameAsync(cancellationToken);
        }

        _overlayAlpha = 1f;
    }

    private void SetOverlay(float alpha) => _overlayAlpha = alpha;

    private async Awaitable InitializeAsync(
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
            // Priority remains a deterministic tiebreaker while dependency migration is in progress.
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

    private static void TrackWorldInitializer<TInitializer>(Scene scene, TInitializer initializer)
    {
        if (initializer is not Component component || component.gameObject.scene != scene)
            return;

        ServiceLocator.GetWorld().TrackInitializer(initializer);
    }

    private void EnterFatalFailure(string message, System.Exception exception)
    {
        _hasFatalFailure = true;
        if (ServiceLocator.HasActiveWorld)
            ServiceLocator.GetWorld().Cancel();
        Time.timeScale = 0f;
        SetOverlay(1f);
        _messageRenderer?.SetText(message, 0.5f, 0.375f, 0.025f, TextAnchor.UpperCenter);
        LoggerProvider.Get().LogException("LoadingManager", exception);
    }

    private void OnProgressEvent(ProgressEvent evt)
    {
        _targetProgress = Mathf.Clamp01(evt.Progress);

        // Message text: centred horizontally, baseline just above the bar border.
        // Bar is at y=[0.340, 0.360], border top = 0.363, gap ≈ 12 px at 1080p.
        // Percentage text is updated every frame in OnEndCameraRendering to stay in sync with the animated bar.
        if (!string.IsNullOrEmpty(evt.Message))
            _messageRenderer?.SetText(evt.Message, 0.5f, 0.375f, 0.025f, TextAnchor.UpperCenter);
    }

    private void OnDestroy()
    {
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        if (_overlayMaterial != null)
        {
            Destroy(_overlayMaterial);
            _overlayMaterial = null;
        }

        _messageRenderer?.Dispose();
        _messageRenderer = null;

        _percentRenderer?.Dispose();
        _percentRenderer = null;

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
