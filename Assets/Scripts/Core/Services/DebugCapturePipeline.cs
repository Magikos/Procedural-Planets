using System;
using System.Threading;
using UnityEngine;

internal interface IDebugCaptureModeContext
{
    bool IsActive { get; }
    DebugRegistry Registry { get; }
    DebugModeId CurrentModeId { get; }
    void ApplyDebugMode(DebugModeId id);
    void CycleDebugMode();
    DebugCaptureSetDefinition GetCurrentCaptureSet();
    DebugModeId[] GetCaptureModes();
    int CappedFrameRate { get; }
    int ProfilingFrameRate { get; }
    float TimedCaptureLocalTime { get; }
    bool ShowDetailedDebug { get; }
    bool IncludeMeshIntegrityInDebugCaptures { get; }
    ICelestialTimeController CelestialController { get; }
    Light SunLight { get; }
    IPrecipitationDebugControl PrecipitationController { get; }
    IWeatherProvider WeatherProvider { get; }
    ICameraRigContext CameraContext { get; }
    int ClimateMapResolutionId { get; }
}

sealed class DebugCapturePipeline
{
    static readonly bool SaveScreenshots = true;
    const int MaxScreenshotWidth = 960;
    const int MaxCaptureRuns = 6;
    const float ModeDelaySeconds = 0.12f;
    const float SampleTimeoutSeconds = 10f;
    const bool RestoreOffAfterCaptureSet = true;

    static readonly int DebugSuppressWeatherPassesId =
        Shader.PropertyToID(ShaderGlobalIds.DebugSuppressWeatherPasses);
    static readonly int CloudViewStepsId =
        Shader.PropertyToID(ShaderGlobalIds.CloudViewSteps);
    static readonly int CloudLightStepsId =
        Shader.PropertyToID(ShaderGlobalIds.CloudLightSteps);

    readonly IDebugCaptureModeContext _ctx;
    bool _running;

    public DebugCapturePipeline(IDebugCaptureModeContext ctx) => _ctx = ctx;

    public bool IsRunning => _running;

    public void TriggerCapture()
    {
        DebugCaptureSetDefinition captureSet = _ctx.GetCurrentCaptureSet();
        if (captureSet.Behavior == DebugCaptureSetBehavior.CurrentModeOnly)
        {
            _ctx.CycleDebugMode();
            QueueScreenshot();
            return;
        }

        if (!SaveScreenshots)
        {
            _ctx.CycleDebugMode();
            return;
        }

        QueueCapture(captureSet, _ctx.GetCaptureModes(), captureScreenshots: true);
    }

    public async Awaitable CaptureCurrentSetAsync(CancellationToken ct)
    {
        if (_ctx.Registry == null) return;
        if (_running)
            throw new InvalidOperationException("A debug capture is already running.");

        ct.ThrowIfCancellationRequested();
        DebugCaptureSetDefinition captureSet = _ctx.GetCurrentCaptureSet();

        if (captureSet.Behavior == DebugCaptureSetBehavior.CurrentModeOnly)
        {
            _ctx.CycleDebugMode();
            await CaptureScreenshotAsync(
                _ctx.CurrentModeId,
                _ctx.Registry.GetModeName(_ctx.CurrentModeId),
                ct);
            return;
        }

        if (!SaveScreenshots)
        {
            _ctx.CycleDebugMode();
            return;
        }

        await CaptureSequenceAsync(captureSet, _ctx.GetCaptureModes(), captureScreenshots: true, ct);
    }

    void QueueCapture(DebugCaptureSetDefinition captureSet, DebugModeId[] modes, bool captureScreenshots)
    {
        if (_running || !_ctx.IsActive || modes == null || modes.Length == 0)
            return;

        _ = CaptureSequenceAsync(captureSet, modes, captureScreenshots, CancellationToken.None);
    }

    void QueueScreenshot()
    {
        if (!SaveScreenshots || _running || !_ctx.IsActive)
            return;

        string modeName = _ctx.Registry.GetModeName(_ctx.CurrentModeId);
        _ = CaptureScreenshotAsync(_ctx.CurrentModeId, modeName, CancellationToken.None);
    }

    async Awaitable CaptureSequenceAsync(
        DebugCaptureSetDefinition captureSet,
        DebugModeId[] modes,
        bool captureScreenshots,
        CancellationToken ct)
    {
        _running = true;
        DebugScreenshotFiles.RecordLastCaptureCamera();
        DebugModeId restoreMode = RestoreOffAfterCaptureSet
            ? _ctx.Registry.DefaultModeId
            : _ctx.CurrentModeId;
        bool timedCapture = captureSet.TimingSamplesPerMode > 0;
        int restoreFrameRate = Application.targetFrameRate;
        int restoreVSync = QualitySettings.vSyncCount;
        float restoreSuppressWeather = Shader.GetGlobalFloat(DebugSuppressWeatherPassesId);
        int restoreCloudViewSteps = Shader.GetGlobalInt(CloudViewStepsId);
        int restoreCloudLightSteps = Shader.GetGlobalInt(CloudLightStepsId);
        bool suppressWeatherPasses = captureSet.Id == DebugCoreIds.PerformanceWaterVolumeStages;
        bool cloudStepCapture = captureSet.Id == DebugCoreIds.PerformanceCloudSteps;
        ICelestialTimeController celestial = _ctx.CelestialController;
        float restoreTimeOfDay = celestial != null ? celestial.TimeOfDay : 0f;
        bool restoreTimeFrozen = celestial != null && celestial.IsTimeFrozen;
        LoggerProvider.Log(LogLevel.Debug, "DebugCapture",
            $"F10 start. Modes={modes.Length}, CaptureScreenshots={captureScreenshots}");

        try
        {
            if (timedCapture)
            {
                if (celestial == null)
                    throw new InvalidOperationException(
                        "Timed debug captures require an ICelestialTimeController.");

                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate =
                    Mathf.Max(_ctx.ProfilingFrameRate, _ctx.CappedFrameRate + 1);
                Shader.SetGlobalFloat(DebugSuppressWeatherPassesId,
                    suppressWeatherPasses ? 1f : 0f);
                celestial.SetTimeFrozen(true);
                if (!celestial.TrySetLocalTimeOfDay(_ctx.TimedCaptureLocalTime))
                    throw new InvalidOperationException(
                        "Timed debug capture could not set local celestial time.");

                await WaitForModeRenderAsync(ct);
            }

            for (int i = 0; i < modes.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                DebugModeDefinition mode = _ctx.Registry.GetMode(modes[i]);
                string modeName = mode.Name;
                _ctx.ApplyDebugMode(mode.Id);
                if (cloudStepCapture)
                    ApplyCloudStepBenchmark(mode.Id.LocalId, restoreCloudViewSteps, restoreCloudLightSteps);
                LoggerProvider.Log(LogLevel.Debug, "DebugCapture",
                    $"F10 step {i + 1}/{modes.Length}: mode {mode.Id}:{modeName}");

                await WaitForModeRenderAsync(ct);
                if (captureSet.TimingSamplesPerMode > 0)
                {
                    FrameTimingCounters.Reset();
                    await WaitForTimingSamplesAsync(captureSet.TimingSamplesPerMode, ct);
                }

                if (captureScreenshots)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        SaveScreenshot(mode.Id, modeName);
                    }
                    catch (Exception ex)
                    {
                        LoggerProvider.LogException("DebugCapture", ex);
                    }
                }
            }
        }
        finally
        {
            _ctx.ApplyDebugMode(restoreMode);
            if (timedCapture)
            {
                if (celestial != null)
                {
                    celestial.SetTimeOfDay(restoreTimeOfDay);
                    celestial.SetTimeFrozen(restoreTimeFrozen);
                }

                Application.targetFrameRate = restoreFrameRate;
                QualitySettings.vSyncCount = restoreVSync;
                Shader.SetGlobalFloat(DebugSuppressWeatherPassesId, restoreSuppressWeather);
                Shader.SetGlobalInt(CloudViewStepsId, restoreCloudViewSteps);
                Shader.SetGlobalInt(CloudLightStepsId, restoreCloudLightSteps);
            }

            _running = false;
            LoggerProvider.Log(LogLevel.Debug, "DebugCapture", "F10 end.");
        }
    }

    async Awaitable CaptureScreenshotAsync(DebugModeId modeId, string modeName, CancellationToken ct)
    {
        _running = true;
        DebugScreenshotFiles.RecordLastCaptureCamera();

        try
        {
            await WaitForModeRenderAsync(ct);
            ct.ThrowIfCancellationRequested();
            SaveScreenshot(modeId, modeName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggerProvider.LogException("DebugCapture", ex);
        }
        finally
        {
            _running = false;
        }
    }

    void SaveScreenshot(DebugModeId modeId, string modeName)
    {
        Texture2D source = null;
        Texture2D resized = null;

        try
        {
            source = ScreenCapture.CaptureScreenshotAsTexture();
            resized = DebugScreenshotFiles.Downsample(source, MaxScreenshotWidth);

            string directory = DebugScreenshotFiles.GetDirectory();
            System.IO.Directory.CreateDirectory(directory);

            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string safeModeName = DebugScreenshotFiles.SanitizeFilePart(modeName);
            string safeModeId = DebugScreenshotFiles.SanitizeFilePart(modeId.ToString());
            string scriptPrefix = ConsoleScriptRuntime.GetCaptureFilePrefix();
            string baseName = string.IsNullOrEmpty(scriptPrefix)
                ? $"F10-{safeModeId}-{safeModeName}-{timestamp}"
                : $"F10-{scriptPrefix}-{safeModeId}-{safeModeName}-{timestamp}";
            string imagePath = System.IO.Path.Combine(directory, baseName + ".png");
            string metadataPath = System.IO.Path.Combine(directory, baseName + ".txt");

            System.IO.File.WriteAllBytes(imagePath, resized.EncodeToPNG());
            System.IO.File.WriteAllText(metadataPath, BuildMetadata(
                modeId, modeName,
                source.width, source.height,
                resized.width, resized.height,
                imagePath));

            int modesPerRun = _ctx.GetCaptureModes().Length;
            int keepFiles = Mathf.Max(1, MaxCaptureRuns) * Mathf.Max(1, modesPerRun) * 2;
            DebugScreenshotFiles.Prune(directory, keepFiles);

            LoggerProvider.Log(LogLevel.Debug, "DebugCapture",
                $"Saved F10 debug screenshot: {imagePath}");
        }
        finally
        {
            if (source != null) UnityEngine.Object.Destroy(source);
            if (resized != null && resized != source) UnityEngine.Object.Destroy(resized);
        }
    }

    string BuildMetadata(
        DebugModeId modeId, string modeName,
        int sourceWidth, int sourceHeight,
        int savedWidth, int savedHeight,
        string imagePath)
    {
        var inputs = new DebugCaptureMetadataInputs(
            _ctx.Registry,
            _ctx.GetCurrentCaptureSet(),
            _ctx.CameraContext,
            _ctx.CelestialController,
            _ctx.SunLight,
            _ctx.PrecipitationController,
            _ctx.WeatherProvider,
            _ctx.ClimateMapResolutionId,
            _ctx.ShowDetailedDebug,
            _ctx.IncludeMeshIntegrityInDebugCaptures);
        return DebugCaptureMetadataBuilder.Build(
            inputs, modeId, modeName,
            sourceWidth, sourceHeight, savedWidth, savedHeight, imagePath);
    }

    static async Awaitable WaitForModeRenderAsync(CancellationToken ct)
    {
        await Awaitable.NextFrameAsync(ct);
        if (ModeDelaySeconds > 0f)
            await WaitUnscaledAsync(ModeDelaySeconds, ct);
        await Awaitable.NextFrameAsync(ct);
        await Awaitable.EndOfFrameAsync();
        ct.ThrowIfCancellationRequested();
    }

    static async Awaitable WaitForTimingSamplesAsync(int requiredSamples, CancellationToken ct)
    {
        float timeoutAt = Time.realtimeSinceStartup + SampleTimeoutSeconds;
        while (FrameTimingCounters.CompletedSampleCount < requiredSamples)
        {
            ct.ThrowIfCancellationRequested();
            if (Time.realtimeSinceStartup >= timeoutAt)
            {
                LoggerProvider.Log(LogLevel.Warning, "DebugCapture",
                    $"Timed capture reached {FrameTimingCounters.CompletedSampleCount}/{requiredSamples} samples before timeout.");
                break;
            }
            await Awaitable.NextFrameAsync(ct);
        }
        await Awaitable.EndOfFrameAsync();
    }

    static async Awaitable WaitUnscaledAsync(float seconds, CancellationToken ct)
    {
        float endTime = Time.unscaledTime + Mathf.Max(0f, seconds);
        while (Time.unscaledTime < endTime)
            await Awaitable.NextFrameAsync(ct);
    }

    static void ApplyCloudStepBenchmark(int mode, int baselineViewSteps, int baselineLightSteps)
    {
        switch (mode)
        {
            case DebugModeConstants.PerformanceCloud72x8:
                Shader.SetGlobalInt(CloudViewStepsId, 72);
                Shader.SetGlobalInt(CloudLightStepsId, 8);
                break;
            case DebugModeConstants.PerformanceCloud48x8:
                Shader.SetGlobalInt(CloudViewStepsId, 48);
                Shader.SetGlobalInt(CloudLightStepsId, 8);
                break;
            case DebugModeConstants.PerformanceCloud72x4:
                Shader.SetGlobalInt(CloudViewStepsId, 72);
                Shader.SetGlobalInt(CloudLightStepsId, 4);
                break;
            case DebugModeConstants.PerformanceCloud48x4:
                Shader.SetGlobalInt(CloudViewStepsId, 48);
                Shader.SetGlobalInt(CloudLightStepsId, 4);
                break;
            default:
                Shader.SetGlobalInt(CloudViewStepsId, Mathf.Max(1, baselineViewSteps));
                Shader.SetGlobalInt(CloudLightStepsId, Mathf.Max(1, baselineLightSteps));
                break;
        }
    }
}
