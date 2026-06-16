using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

public static class MemoryDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("memory");
}

// Lightweight memory overlay + F10 sidecar contributor. Reads only Unity Profiler counters
// (Mono heap, native allocator, graphics driver, GC) — each call is a single counter read on
// the order of nanoseconds, safe to invoke per-frame. Throttled formatting + cached label
// strings to keep OnGUI allocation-free between refreshes.
public sealed class MemoryDebugModule : IDebugModule, IDebugCaptureMetadataProvider, IDebugOverlayContributor
{
    public DebugModuleId Id => MemoryDebugIds.Module;

    const float OverlayRefreshIntervalSeconds = 0.5f;

    static readonly List<IMemoryReporter> _reporters = new();

    public static void Register(IMemoryReporter reporter)
    {
        if (reporter != null && !_reporters.Contains(reporter))
            _reporters.Add(reporter);
    }

    public static void Unregister(IMemoryReporter reporter)
    {
        _reporters.Remove(reporter);
    }

    string _cachedSummary = "Memory: (gathering...)";
    string _cachedMono;
    string _cachedNative;
    string _cachedGfx;
    string _cachedGc;
    string _cachedTempAlloc;
    string _cachedSubsystemReport;
    float _nextRefreshTime;

    public void Register(DebugRegistry registry)
    {
        registry.RegisterMetadataProvider(this);
        registry.RegisterOverlayContributor(this);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        RefreshStrings(force: true);
        sb.AppendLine("--- Memory ---");
        sb.AppendLine(_cachedSummary);
        sb.AppendLine(_cachedMono);
        sb.AppendLine(_cachedNative);
        sb.AppendLine(_cachedGfx);
        sb.AppendLine(_cachedGc);
        sb.AppendLine(_cachedTempAlloc);
        if (_cachedSubsystemReport != null)
            sb.Append(_cachedSubsystemReport);
    }

    public void DrawOverlay(DebugRuntimeState state)
    {
        if (!state.ShowDetailedDebug)
            return;

        if (Time.unscaledTime >= _nextRefreshTime)
        {
            RefreshStrings(force: false);
            _nextRefreshTime = Time.unscaledTime + OverlayRefreshIntervalSeconds;
        }

        GUILayout.Space(6);
        GUILayout.Label("Memory Debug");
        GUILayout.Label(_cachedSummary);
        GUILayout.Label(_cachedMono);
        GUILayout.Label(_cachedNative);
        GUILayout.Label(_cachedGfx);
        GUILayout.Label(_cachedGc);
        GUILayout.Label(_cachedTempAlloc);
        if (_cachedSubsystemReport != null)
            GUILayout.Label(_cachedSubsystemReport);
    }

    void RefreshStrings(bool force)
    {
        long monoUsed = Profiler.GetMonoUsedSizeLong();
        long monoHeap = Profiler.GetMonoHeapSizeLong();
        long nativeUsed = Profiler.GetTotalAllocatedMemoryLong();
        long nativeReserved = Profiler.GetTotalReservedMemoryLong();
        long nativeUnused = Profiler.GetTotalUnusedReservedMemoryLong();
        long gfx = Profiler.GetAllocatedMemoryForGraphicsDriver();
        long gcTotal = System.GC.GetTotalMemory(false);
        long tempAlloc = Profiler.GetTempAllocatorSize();

        long grandTotal = nativeReserved + monoHeap + gfx;

        _cachedSummary = $"Total (native+mono+gfx): {FormatBytes(grandTotal)}";
        _cachedMono = $"Mono: used={FormatBytes(monoUsed)} heap={FormatBytes(monoHeap)}";
        _cachedNative = $"Native: used={FormatBytes(nativeUsed)} reserved={FormatBytes(nativeReserved)} unused={FormatBytes(nativeUnused)}";
        _cachedGfx = $"Graphics driver: {FormatBytes(gfx)}";
        _cachedGc = $"GC tracked: {FormatBytes(gcTotal)}";
        _cachedTempAlloc = $"Temp allocator: {FormatBytes(tempAlloc)}";

        if (_reporters.Count > 0)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _reporters.Count; i++)
                _reporters[i].AppendMemoryReport(sb);
            _cachedSubsystemReport = sb.ToString();
        }
        else
        {
            _cachedSubsystemReport = null;
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0L) return "?";
        const long kb = 1024L;
        const long mb = kb * 1024L;
        const long gb = mb * 1024L;
        if (bytes >= gb) return $"{bytes / (double)gb:F2} GB";
        if (bytes >= mb) return $"{bytes / (double)mb:F1} MB";
        if (bytes >= kb) return $"{bytes / (double)kb:F1} KB";
        return $"{bytes} B";
    }
}
