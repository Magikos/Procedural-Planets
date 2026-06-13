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

// Per-frame CPU timing sink for the major per-frame subsystems plus a whole-frame
// CPU/GPU number from the Unity FrameTimingManager. Subsystems wrap their per-frame
// work in `using (FrameTimingCounters.Measure(section))`; the scope is a readonly struct
// so `using` calls Dispose directly with no boxing or allocation.
//
// Frame rolling is lazy off Time.frameCount: the first Measure (or read) of a new frame
// snapshots the previous frame's accumulated ticks into the display buffer and zeroes the
// current buffer. Reads therefore always report the last fully-accumulated frame. All
// callers are main-thread per-frame Update code, so Time.frameCount access is safe.
public static class FrameTimingCounters
{
    const int SectionCount = 5;

    static readonly long[] _currentTicks = new long[SectionCount];
    static readonly long[] _lastTicks = new long[SectionCount];
    static readonly FrameTiming[] _frameTimings = new FrameTiming[1];
    static int _currentFrame = -1;

    static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

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

    static void RollIfNewFrame()
    {
        int frame = Time.frameCount;
        if (frame == _currentFrame)
            return;

        if (_currentFrame >= 0)
        {
            Array.Copy(_currentTicks, _lastTicks, SectionCount);
            CaptureWholeFrame();
        }

        Array.Clear(_currentTicks, 0, SectionCount);
        _currentFrame = frame;
    }

    static void CaptureWholeFrame()
    {
        FrameTimingManager.CaptureFrameTimings();
        uint count = FrameTimingManager.GetLatestTimings(1, _frameTimings);
        if (count == 0)
            return;

        LastCpuFrameMs = _frameTimings[0].cpuFrameTime;
        LastGpuFrameMs = _frameTimings[0].gpuFrameTime;
    }
}

// Frame-timing overlay + F10 sidecar contributor. Mirrors MemoryDebugModule: throttled
// formatting with cached label strings to stay allocation-free between refreshes.
public sealed class FrameTimingModule : IDebugModule, IDebugCaptureMetadataProvider, IDebugOverlayContributor
{
    public DebugModuleId Id => FrameTimingIds.Module;

    const float OverlayRefreshIntervalSeconds = 0.5f;

    string _cachedWholeFrame = "Frame: (gathering...)";
    string _cachedSurfaceVisibility;
    string _cachedWater;
    string _cachedClouds;
    string _cachedNearGrass;
    string _cachedChunkGrass;
    float _nextRefreshTime;

    public void Register(DebugRegistry registry)
    {
        registry.RegisterMetadataProvider(this);
        registry.RegisterOverlayContributor(this);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        RefreshStrings();
        sb.AppendLine("--- Frame Timing ---");
        sb.AppendLine(_cachedWholeFrame);
        sb.AppendLine(_cachedSurfaceVisibility);
        sb.AppendLine(_cachedWater);
        sb.AppendLine(_cachedClouds);
        sb.AppendLine(_cachedNearGrass);
        sb.AppendLine(_cachedChunkGrass);
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
        GUILayout.Label("Frame Timing (CPU per-section)");
        GUILayout.Label(_cachedSurfaceVisibility);
        GUILayout.Label(_cachedWater);
        GUILayout.Label(_cachedClouds);
        GUILayout.Label(_cachedNearGrass);
        GUILayout.Label(_cachedChunkGrass);
    }

    void RefreshStrings()
    {
        _cachedWholeFrame =
            $"Whole frame: CPU={FormatMs(FrameTimingCounters.LastCpuFrameMs)} GPU={FormatMs(FrameTimingCounters.LastGpuFrameMs)}";
        _cachedSurfaceVisibility = $"Surface/terrain: {FormatMs(FrameTimingCounters.GetSectionMs(FrameTimingSection.SurfaceVisibility))}";
        _cachedWater = $"Water:       {FormatMs(FrameTimingCounters.GetSectionMs(FrameTimingSection.Water))}";
        _cachedClouds = $"Clouds:      {FormatMs(FrameTimingCounters.GetSectionMs(FrameTimingSection.Clouds))}";
        _cachedNearGrass = $"Near grass:  {FormatMs(FrameTimingCounters.GetSectionMs(FrameTimingSection.NearGrass))}";
        _cachedChunkGrass = $"Chunk grass: {FormatMs(FrameTimingCounters.GetSectionMs(FrameTimingSection.ChunkGrass))}";
    }

    static string FormatMs(double ms)
    {
        if (ms < 0.0) return "?";
        return $"{ms:F2} ms";
    }
}
