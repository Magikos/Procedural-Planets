using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

public static class MemoryDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("memory");
}

public static class MemoryDebugCounters
{
    public static int LiveChunkTextureSets { get; private set; }
    public static int LiveChunkBiomeTextureSets { get; private set; }
    public static long ChunkBiomeTextureRawBytes { get; private set; }
    public static int LiveChunkSurfaceStateTextures { get; private set; }
    public static long ChunkSurfaceStateRawBytes { get; private set; }
    public static int FaceBiomeAtlasTextures { get; private set; }
    public static long FaceBiomeAtlasRawBytes { get; private set; }
    public static long RetainedChunkCpuBytes { get; private set; }

    public static void ReportLiveChunkTextureSets(int count)
    {
        LiveChunkTextureSets = count < 0 ? 0 : count;
    }

    public static void ReportChunkBiomeTextures(int setCount, long rawBytes)
    {
        LiveChunkBiomeTextureSets = setCount < 0 ? 0 : setCount;
        ChunkBiomeTextureRawBytes = rawBytes < 0L ? 0L : rawBytes;
    }

    public static void ReportChunkSurfaceStateTextures(int textureCount, long rawBytes)
    {
        LiveChunkSurfaceStateTextures = textureCount < 0 ? 0 : textureCount;
        ChunkSurfaceStateRawBytes = rawBytes < 0L ? 0L : rawBytes;
    }

    public static void AdjustFaceBiomeAtlases(int textureDelta, long rawBytesDelta)
    {
        FaceBiomeAtlasTextures = System.Math.Max(0, FaceBiomeAtlasTextures + textureDelta);
        FaceBiomeAtlasRawBytes = System.Math.Max(0L, FaceBiomeAtlasRawBytes + rawBytesDelta);
    }

    public static void ReportRetainedChunkCpuBytes(long bytes)
    {
        RetainedChunkCpuBytes = bytes < 0L ? 0L : bytes;
    }
}

// Lightweight memory overlay + F10 sidecar contributor. Reads only Unity Profiler counters
// (Mono heap, native allocator, graphics driver, GC) — each call is a single counter read on
// the order of nanoseconds, safe to invoke per-frame. Throttled formatting + cached label
// strings to keep OnGUI allocation-free between refreshes.
public sealed class MemoryDebugModule : IDebugModule, IDebugCaptureMetadataProvider, IDebugOverlayContributor
{
    public DebugModuleId Id => MemoryDebugIds.Module;

    const float OverlayRefreshIntervalSeconds = 0.5f;

    string _cachedSummary = "Memory: (gathering...)";
    string _cachedMono;
    string _cachedNative;
    string _cachedGfx;
    string _cachedGc;
    string _cachedTempAlloc;
    string _cachedChunkTextures;
    string _cachedChunkBiomeTextures;
    string _cachedChunkSurfaceStateTextures;
    string _cachedFaceBiomeAtlases;
    string _cachedChunkCpu;
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
        sb.AppendLine(_cachedChunkTextures);
        sb.AppendLine(_cachedChunkBiomeTextures);
        sb.AppendLine(_cachedChunkSurfaceStateTextures);
        sb.AppendLine(_cachedFaceBiomeAtlases);
        sb.AppendLine(_cachedChunkCpu);
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
        GUILayout.Label(_cachedChunkTextures);
        GUILayout.Label(_cachedChunkBiomeTextures);
        GUILayout.Label(_cachedChunkSurfaceStateTextures);
        GUILayout.Label(_cachedFaceBiomeAtlases);
        GUILayout.Label(_cachedChunkCpu);
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
        _cachedChunkTextures = $"Chunk texture sets (any): {MemoryDebugCounters.LiveChunkTextureSets}";
        _cachedChunkBiomeTextures =
            $"Chunk biome texture sets: {MemoryDebugCounters.LiveChunkBiomeTextureSets} " +
            $"(raw pixels/copy={FormatBytes(MemoryDebugCounters.ChunkBiomeTextureRawBytes)})";
        _cachedChunkSurfaceStateTextures =
            $"Chunk surface-state textures: {MemoryDebugCounters.LiveChunkSurfaceStateTextures} " +
            $"(raw pixels/copy={FormatBytes(MemoryDebugCounters.ChunkSurfaceStateRawBytes)})";
        _cachedFaceBiomeAtlases =
            $"Face biome atlas textures: {MemoryDebugCounters.FaceBiomeAtlasTextures} " +
            $"(raw pixels/copy={FormatBytes(MemoryDebugCounters.FaceBiomeAtlasRawBytes)})";
        _cachedChunkCpu = $"Chunk CPU arrays retained: {FormatBytes(MemoryDebugCounters.RetainedChunkCpuBytes)}";
    }

    static string FormatBytes(long bytes)
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
