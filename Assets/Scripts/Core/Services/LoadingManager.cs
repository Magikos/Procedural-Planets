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

        try
        {
            var currentScene = SceneManager.GetActiveScene();
            if (!currentScene.IsValid())
            {
                LoggerProvider.Get().Log(LogLevel.Error, "LoadingManager", "No valid active scene found during startup initialization. This should never happen at Start() — check your scene setup.");
                return;
            }

            SetOverlay(1f);
            await Awaitable.NextFrameAsync(cancellationToken); // let the opaque overlay render

            await InitializeAsync(currentScene, cancellationToken);

            await Awaitable.NextFrameAsync(cancellationToken);
            await Awaitable.NextFrameAsync(cancellationToken);

            await FadeInAsync(cancellationToken);
        }
        catch (System.OperationCanceledException) { } // expected during app teardown
        catch (System.Exception ex)
        {
            LoggerProvider.Get().LogException("LoadingManager", ex);
        }
        finally
        {
            SetOverlay(0f);
            _isTransitioning = false;
        }
    }

    public async Awaitable<bool> TransitionToSceneAsync(string sceneName, bool useOverlay = true, CancellationToken cancellationToken = default)
    {
        return await InternalTransitionToSceneAsync(sceneName, -1, useOverlay, cancellationToken);
    }

    public async Awaitable<bool> TransitionToSceneAsync(int buildIndex, bool useOverlay = true, CancellationToken cancellationToken = default)
    {
        return await InternalTransitionToSceneAsync(null, buildIndex, useOverlay, cancellationToken);
    }

    private async Awaitable<bool> InternalTransitionToSceneAsync(string sceneName, int buildIndex, bool useOverlay, CancellationToken cancellationToken)
    {
        if (_isTransitioning)
        {
            LoggerProvider.Get().Log(LogLevel.Warning, "LoadingManager", "Already transitioning. Please wait.");
            return false;
        }

        if (cancellationToken.CanBeCanceled)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
            return await RunTransitionAsync(sceneName, buildIndex, useOverlay, linkedCts.Token);
        }

        return await RunTransitionAsync(sceneName, buildIndex, useOverlay, _cancellationTokenSource.Token);
    }

    private async Awaitable<bool> RunTransitionAsync(string sceneName, int buildIndex, bool useOverlay, CancellationToken cancellationToken)
    {
        _isTransitioning = true;

        try
        {
            var oldScene = SceneManager.GetActiveScene();

            if (useOverlay)
                await FadeOutAsync(cancellationToken);

            Time.timeScale = 0f;
            var asyncOp = sceneName != null
                ? SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive)
                : SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
            asyncOp.allowSceneActivation = false;

            while (asyncOp.progress < 0.9f)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            asyncOp.allowSceneActivation = true;
            while (!asyncOp.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            var newScene = sceneName != null
                ? SceneManager.GetSceneByName(sceneName)
                : SceneManager.GetSceneByBuildIndex(buildIndex);

            if (!newScene.IsValid())
                throw new System.InvalidOperationException(
                    $"Scene '{sceneName ?? $"build index {buildIndex}"}' could not be found after async load. Verify it is added to Build Settings.");

            SceneManager.SetActiveScene(newScene);

            await InitializeAsync(newScene, cancellationToken);

            if (oldScene.IsValid() && oldScene != newScene)
            {
                var unloadOp = SceneManager.UnloadSceneAsync(oldScene);
                if (unloadOp != null)
                {
                    while (!unloadOp.isDone)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Awaitable.NextFrameAsync(cancellationToken);
                    }
                }
            }

            // Wait for two frames to ensure all pending operations settle before fading back in
            await Awaitable.NextFrameAsync(cancellationToken);
            await Awaitable.NextFrameAsync(cancellationToken);

            Time.timeScale = 1f;

            if (useOverlay)
                await FadeInAsync(cancellationToken);

            return true;
        }
        catch (System.OperationCanceledException) { return false; }
        catch (System.Exception ex)
        {
            LoggerProvider.Get().LogException("LoadingManager", ex);
            return false;
        }
        finally
        {
            Time.timeScale = 1f;
            SetOverlay(0f);
            _isTransitioning = false;
        }
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

    private async Awaitable InitializeAsync(Scene scene, CancellationToken cancellationToken)
    {
        // Include DontDestroyOnLoad objects (e.g. GameBootstrap) which Unity moves out
        // of the active scene during Awake — they live in LoadingManager's own scene.
        var rootObjects = scene.GetRootGameObjects().AsEnumerable();
        var ddolScene = gameObject.scene;
        if (ddolScene.IsValid() && ddolScene != scene)
            rootObjects = rootObjects.Concat(ddolScene.GetRootGameObjects());

        var allBehaviours = rootObjects
            .SelectMany(go => go.GetComponentsInChildren<MonoBehaviour>(true))
            .ToList();

        var tracker = new ProgressTracker();
        foreach (var reporter in allBehaviours.OfType<IProgressReporter>())
            tracker.Register(reporter);

        var earlyInitializers = allBehaviours
            .OfType<IEarlyInitialize>()
            .OrderByDescending(i => i.EarlyPriority)
            .ToList();

        LoggerProvider.Get().Log(LogLevel.Info, "LoadingManager", $"Early-initializing {earlyInitializers.Count} components");
        foreach (var initializer in earlyInitializers)
        {
            try
            {
                await initializer.EarlyInitialize(cancellationToken);
            }
            catch (System.Exception ex)
            {
                LoggerProvider.Get().LogException("LoadingManager", ex);
            }
        }

        var lateInitializers = allBehaviours
            .OfType<ILateInitialize>()
            .OrderByDescending(i => i.LatePriority)
            .ToList();

        LoggerProvider.Get().Log(LogLevel.Info, "LoadingManager", $"Late-initializing {lateInitializers.Count} components");
        foreach (var initializer in lateInitializers)
        {
            try
            {
                await initializer.LateInitialize(cancellationToken);
            }
            catch (System.Exception ex)
            {
                LoggerProvider.Get().LogException("LoadingManager", ex);
            }
        }

        tracker.Complete();
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

        ServiceLocator.Unregister<ILoadingManager>(this);
    }
}
