using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

sealed class LoadingProgressBarOverlay : ILoadingOverlay
{
    const float FadeDuration = 0.35f;

    Material _material;
    float _alpha;
    float _targetProgress;
    float _displayProgress;
    int _lastDisplayedPercent = -1;
    SDFTextRenderer _messageRenderer;
    SDFTextRenderer _percentRenderer;

    public LoadingProgressBarOverlay()
    {
        var shader = Shader.Find("Hidden/LoadingOverlay");
        if (shader == null)
        {
            LoggerProvider.Get().Log(LogLevel.Error, "LoadingOverlay",
                "Hidden/LoadingOverlay shader not found. Ensure Assets/Graphics/Shaders/LoadingOverlay.shader is in the project.");
        }
        else
        {
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var font = Resources.Load<SDFFontAsset>("DefaultFont");
            _messageRenderer = new SDFTextRenderer(font);
            _percentRenderer = new SDFTextRenderer(font);
        }

        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    public void SetAlpha(float alpha) => _alpha = alpha;

    public void SetProgress(float value, string message)
    {
        _targetProgress = Mathf.Clamp01(value);
        if (!string.IsNullOrEmpty(message))
            _messageRenderer?.SetText(message, 0.5f, 0.375f, 0.025f, TextAnchor.UpperCenter);
    }

    public void ShowFatalError(string message)
    {
        _alpha = 1f;
        _messageRenderer?.SetText(message, 0.5f, 0.375f, 0.025f, TextAnchor.UpperCenter);
    }

    public async Awaitable FadeInAsync(CancellationToken ct)
    {
        float elapsed = 0f;
        while (elapsed < FadeDuration)
        {
            ct.ThrowIfCancellationRequested();
            _alpha = Mathf.Clamp01(1f - elapsed / FadeDuration);
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            await Awaitable.NextFrameAsync(ct);
        }

        _alpha = 0f;
    }

    public async Awaitable FadeOutAsync(CancellationToken ct)
    {
        float elapsed = 0f;
        while (elapsed < FadeDuration)
        {
            ct.ThrowIfCancellationRequested();
            _alpha = Mathf.Clamp01(elapsed / FadeDuration);
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            await Awaitable.NextFrameAsync(ct);
        }

        _alpha = 1f;
    }

    public void Dispose()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

        if (_material != null)
        {
            UnityEngine.Object.Destroy(_material);
            _material = null;
        }

        _messageRenderer?.Dispose();
        _messageRenderer = null;
        _percentRenderer?.Dispose();
        _percentRenderer = null;
    }

    void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (_alpha <= 0.001f) return;
        if (_material == null) return;
        if (cam != Camera.main) return;

        _displayProgress = Mathf.Lerp(_displayProgress, _targetProgress, Time.unscaledDeltaTime * 8f);

        _material.SetFloat("_Alpha", _alpha);
        _material.SetFloat("_Progress", _displayProgress);

        var cmd = new CommandBuffer { name = "LoadingOverlay" };
        try
        {
            cmd.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);

            if (_messageRenderer != null)
            {
                _messageRenderer.SetAlpha(_alpha);
                _messageRenderer.Draw(cmd);
            }

            if (_percentRenderer != null)
            {
                int pct = Mathf.RoundToInt(_displayProgress * 100);
                if (pct != _lastDisplayedPercent)
                {
                    _percentRenderer.SetText($"{pct}%", 0.862f, 0.341f, 0.025f);
                    _lastDisplayedPercent = pct;
                }
                _percentRenderer.SetAlpha(_alpha);
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
}
