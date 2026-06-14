using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

// Terrain has no separate per-frame CPU loop: its LOD/visibility/mesh-activation CPU lives
// in the SurfaceProvider tick (SurfaceVisibility) and its render cost is GPU (whole-frame GPU).
// So there is no standalone Terrain section.
public enum FrameTimingSection
{
    SurfaceVisibility = 0,
    Water = 1,
    Clouds = 2,
    NearGrass = 3,
    ChunkGrass = 4,
}

public static class FrameTimingIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("frametiming");
}

public readonly struct FrameTimingStats
{
    public readonly double LastMs;
    public readonly double AverageMs;
    public readonly double P95Ms;
    public readonly int SampleCount;

    public FrameTimingStats(double lastMs, double averageMs, double p95Ms, int sampleCount)
    {
        LastMs = lastMs;
        AverageMs = averageMs;
        P95Ms = p95Ms;
        SampleCount = sampleCount;
    }
}

// Per-frame CPU timing sink for the major per-frame subsystems plus a whole-frame
// CPU/GPU number from the Unity FrameTimingManager. Subsystems wrap their per-frame
// work in `using (FrameTimingCounters.Measure(section))`; the scope is a readonly struct
// so `using` calls Dispose directly with no boxing or allocation.
//
// Frame rolling is lazy off Time.frameCount: the first Measure (or read) of a new frame
// snapshots the previous frame's accumulated ticks into the display buffer and rolling
// window, then zeroes the current buffer. Reads therefore report completed frames only.
// All callers are main-thread per-frame Update code, so Time.frameCount access is safe.
public static class FrameTimingCounters
{
    const int SectionCount = 5;
    const int RollingWindowSize = 120;
    const double MaxValidWholeFrameMs = 1000.0;

    static readonly long[] _currentTicks = new long[SectionCount];
    static readonly long[] _lastTicks = new long[SectionCount];
    static readonly FrameTiming[] _frameTimings = new FrameTiming[1];
    static readonly double[][] _sectionSamples = CreateSectionSamples();
    static readonly double[] _cpuFrameSamples = new double[RollingWindowSize];
    static readonly double[] _gpuFrameSamples = new double[RollingWindowSize];
    static readonly double[] _uninstrumentedCpuSamples = new double[RollingWindowSize];
    static readonly double[] _sortScratch = new double[RollingWindowSize];
    static int _currentFrame = -1;
    static int _sampleWriteIndex;
    static int _sampleCount;
    static int _validWholeFrameSampleCount;

    static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    public static int WindowCapacity => RollingWindowSize;
    public static int CompletedSampleCount
    {
        get
        {
            RollIfNewFrame();
            return _validWholeFrameSampleCount;
        }
    }
    public static double LastCpuFrameMs { get; private set; } = -1.0;
    public static double LastGpuFrameMs { get; private set; } = -1.0;

    public readonly struct Scope : IDisposable
    {
        readonly int _section;
        readonly long _start;

        internal Scope(FrameTimingSection section)
        {
            _section = (int)section;
            _start = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            _currentTicks[_section] += Stopwatch.GetTimestamp() - _start;
        }
    }

    public static Scope Measure(FrameTimingSection section)
    {
        RollIfNewFrame();
        return new Scope(section);
    }

    public static double GetSectionMs(FrameTimingSection section)
    {
        RollIfNewFrame();
        return _lastTicks[(int)section] * TicksToMs;
    }

    public static FrameTimingStats GetCpuFrameStats()
    {
        RollIfNewFrame();
        return BuildStats(_cpuFrameSamples, LastCpuFrameMs);
    }

    public static FrameTimingStats GetGpuFrameStats()
    {
        RollIfNewFrame();
        return BuildStats(_gpuFrameSamples, LastGpuFrameMs);
    }

    public static FrameTimingStats GetSectionStats(FrameTimingSection section)
    {
        RollIfNewFrame();
        return BuildStats(_sectionSamples[(int)section], GetSectionMs(section));
    }

    public static FrameTimingStats GetUninstrumentedCpuStats()
    {
        RollIfNewFrame();
        double last = LastCpuFrameMs;
        if (last >= 0.0)
        {
            for (int i = 0; i < SectionCount; i++)
                last -= _lastTicks[i] * TicksToMs;
            last = Math.Max(0.0, last);
        }

        return BuildStats(_uninstrumentedCpuSamples, last);
    }

    public static void Reset()
    {
        Array.Clear(_currentTicks, 0, SectionCount);
        Array.Clear(_lastTicks, 0, SectionCount);
        for (int i = 0; i < SectionCount; i++)
            Array.Clear(_sectionSamples[i], 0, RollingWindowSize);
        Array.Clear(_cpuFrameSamples, 0, RollingWindowSize);
        Array.Clear(_gpuFrameSamples, 0, RollingWindowSize);
        Array.Clear(_uninstrumentedCpuSamples, 0, RollingWindowSize);

        _currentFrame = -1;
        _sampleWriteIndex = 0;
        _sampleCount = 0;
        _validWholeFrameSampleCount = 0;
        LastCpuFrameMs = -1.0;
        LastGpuFrameMs = -1.0;
    }

    static void RollIfNewFrame()
    {
        int frame = Time.frameCount;
        if (frame == _currentFrame)
            return;

        if (_currentFrame >= 0)
        {
            Array.Copy(_currentTicks, _lastTicks, SectionCount);
            CaptureWholeFrame(out double cpuFrameMs, out double gpuFrameMs);
            RecordCompletedFrame(cpuFrameMs, gpuFrameMs);
        }

        Array.Clear(_currentTicks, 0, SectionCount);
        _currentFrame = frame;
    }

    static void CaptureWholeFrame(out double cpuFrameMs, out double gpuFrameMs)
    {
        cpuFrameMs = -1.0;
        gpuFrameMs = -1.0;
        FrameTimingManager.CaptureFrameTimings();
        uint count = FrameTimingManager.GetLatestTimings(1, _frameTimings);
        if (count == 0)
            return;

        LastCpuFrameMs = SanitizeWholeFrameMs(_frameTimings[0].cpuFrameTime);
        LastGpuFrameMs = SanitizeWholeFrameMs(_frameTimings[0].gpuFrameTime);
        cpuFrameMs = LastCpuFrameMs;
        gpuFrameMs = LastGpuFrameMs;
    }

    static void RecordCompletedFrame(double cpuFrameMs, double gpuFrameMs)
    {
        double accountedCpuMs = 0.0;
        for (int i = 0; i < SectionCount; i++)
        {
            double sectionMs = _lastTicks[i] * TicksToMs;
            _sectionSamples[i][_sampleWriteIndex] = sectionMs;
            accountedCpuMs += sectionMs;
        }

        _cpuFrameSamples[_sampleWriteIndex] = cpuFrameMs;
        _gpuFrameSamples[_sampleWriteIndex] = gpuFrameMs;
        _uninstrumentedCpuSamples[_sampleWriteIndex] = cpuFrameMs >= 0.0
            ? Math.Max(0.0, cpuFrameMs - accountedCpuMs)
            : -1.0;

        _sampleWriteIndex = (_sampleWriteIndex + 1) % RollingWindowSize;
        _sampleCount = Math.Min(_sampleCount + 1, RollingWindowSize);
        if (cpuFrameMs >= 0.0 && gpuFrameMs >= 0.0)
            _validWholeFrameSampleCount++;
    }

    static double SanitizeWholeFrameMs(double value)
    {
        return value > 0.0
            && value <= MaxValidWholeFrameMs
            && !double.IsNaN(value)
            && !double.IsInfinity(value)
                ? value
                : -1.0;
    }

    static FrameTimingStats BuildStats(double[] samples, double lastMs)
    {
        int validCount = 0;
        double total = 0.0;

        for (int i = 0; i < _sampleCount; i++)
        {
            double sample = samples[i];
            if (sample < 0.0 || double.IsNaN(sample) || double.IsInfinity(sample))
                continue;

            _sortScratch[validCount++] = sample;
            total += sample;
        }

        if (validCount == 0)
            return new FrameTimingStats(lastMs, -1.0, -1.0, 0);

        Array.Sort(_sortScratch, 0, validCount);
        int p95Index = Math.Max(0, (int)Math.Ceiling(validCount * 0.95) - 1);
        return new FrameTimingStats(
            lastMs,
            total / validCount,
            _sortScratch[p95Index],
            validCount);
    }

    static double[][] CreateSectionSamples()
    {
        var samples = new double[SectionCount][];
        for (int i = 0; i < SectionCount; i++)
            samples[i] = new double[RollingWindowSize];
        return samples;
    }
}

// Frame-timing overlay + F10 sidecar contributor. Mirrors MemoryDebugModule: throttled
// formatting with cached label strings to stay allocation-free between refreshes.
public sealed class FrameTimingModule : IDebugModule, IDebugCaptureMetadataProvider, IDebugOverlayContributor
{
    public DebugModuleId Id => FrameTimingIds.Module;

    const float OverlayRefreshIntervalSeconds = 0.5f;

    string _cachedWholeFrame = "Frame: (gathering...)";
    string _cachedWholeWindow = "Rolling window: (gathering...)";
    string _cachedSurfaceVisibility;
    string _cachedWater;
    string _cachedClouds;
    string _cachedNearGrass;
    string _cachedChunkGrass;
    string _cachedUninstrumentedCpu;
    float _nextRefreshTime;

    public void Register(DebugRegistry registry)
    {
        FrameTimingCounters.Reset();
        registry.RegisterMetadataProvider(this);
        registry.RegisterOverlayContributor(this);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        RefreshStrings();
        sb.AppendLine("--- Frame Timing ---");
        sb.AppendLine(_cachedWholeFrame);
        sb.AppendLine(_cachedWholeWindow);
        sb.AppendLine(_cachedSurfaceVisibility);
        sb.AppendLine(_cachedWater);
        sb.AppendLine(_cachedClouds);
        sb.AppendLine(_cachedNearGrass);
        sb.AppendLine(_cachedChunkGrass);
        sb.AppendLine(_cachedUninstrumentedCpu);
    }

    public void DrawOverlay(DebugRuntimeState state)
    {
        if (!state.ShowDetailedDebug)
            return;

        if (Time.unscaledTime >= _nextRefreshTime)
        {
            RefreshStrings();
            _nextRefreshTime = Time.unscaledTime + OverlayRefreshIntervalSeconds;
        }

        GUILayout.Space(6);
        GUILayout.Label($"Frame Timing ({FrameTimingCounters.WindowCapacity}-frame rolling window)");
        GUILayout.Label(_cachedWholeWindow);
        GUILayout.Label(_cachedSurfaceVisibility);
        GUILayout.Label(_cachedWater);
        GUILayout.Label(_cachedClouds);
        GUILayout.Label(_cachedNearGrass);
        GUILayout.Label(_cachedChunkGrass);
        GUILayout.Label(_cachedUninstrumentedCpu);
    }

    void RefreshStrings()
    {
        FrameTimingStats cpu = FrameTimingCounters.GetCpuFrameStats();
        FrameTimingStats gpu = FrameTimingCounters.GetGpuFrameStats();
        _cachedWholeFrame =
            $"Whole frame: CPU={FormatMs(cpu.LastMs)} GPU={FormatMs(gpu.LastMs)}";
        _cachedWholeWindow =
            $"Rolling: CPU {FormatWindow(cpu)}; GPU {FormatWindow(gpu)}";
        _cachedSurfaceVisibility = $"Surface/terrain CPU: {FormatWindow(FrameTimingCounters.GetSectionStats(FrameTimingSection.SurfaceVisibility))}";
        _cachedWater = $"Water CPU:          {FormatWindow(FrameTimingCounters.GetSectionStats(FrameTimingSection.Water))}";
        _cachedClouds = $"Clouds CPU:         {FormatWindow(FrameTimingCounters.GetSectionStats(FrameTimingSection.Clouds))}";
        _cachedNearGrass = $"Near grass CPU:     {FormatWindow(FrameTimingCounters.GetSectionStats(FrameTimingSection.NearGrass))}";
        _cachedChunkGrass = $"Chunk grass CPU:    {FormatWindow(FrameTimingCounters.GetSectionStats(FrameTimingSection.ChunkGrass))}";
        _cachedUninstrumentedCpu = $"Uninstrumented CPU: {FormatWindow(FrameTimingCounters.GetUninstrumentedCpuStats())}";
    }

    static string FormatMs(double ms)
    {
        if (ms < 0.0) return "?";
        return $"{ms:F2} ms";
    }

    static string FormatWindow(FrameTimingStats stats)
    {
        if (stats.SampleCount == 0)
            return "gathering...";

        return $"avg={FormatMs(stats.AverageMs)}, p95={FormatMs(stats.P95Ms)}, last={FormatMs(stats.LastMs)}, n={stats.SampleCount}";
    }
}
