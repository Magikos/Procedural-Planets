// Mutable per-frame telemetry for the grass placement renderer. GrassPlacementController writes these
// counters as it walks residency and renders chunks; GetGrassDebugStats composes them with the
// controller's static config into the GrassDebugStats snapshot read by the grass debug module.
sealed class GrassPlacementStats
{
    // Indices into the per-chunk stats buffer written by BiomeGrassPlace.compute.
    const int StatCandidateLanes = 0;
    const int StatDensityRejectedLanes = 1;
    const int StatShapeRejectedLanes = 2;
    const int StatStateRejectedLanes = 3;
    const int StatWaterRejectedLanes = 4;
    const int StatSlopeRejectedLanes = 5;
    const int StatDistanceRejectedLanes = 6;
    const int StatDistanceFadeRejectedLanes = 7;
    const int StatFrustumRejectedLanes = 8;
    const int StatVisibleLanes = 9;
    const int StatCandidateBlades = 10;
    const int StatDensityRejectedBlades = 11;
    const int StatSlopeRejectedBlades = 12;
    const int StatEmittedBlades = 13;
    const int StatOverflowRejectedBlades = 14;
    const int StatInnerFadeRejectedBlades = 15;

    // Residency snapshot (set in RefreshResidency).
    public int VisibleChunks;
    public int BufferedResidencyChunks;

    // Placement dispatch count (managed by CreateRuntime / RedispatchAllPlacements).
    public int PlacementDispatches;

    // Per-tick render walk (cleared by ResetPerTick each Tick).
    public int DrawCalls;
    public int BladeInstances;
    public int ResidentBladeInstances;
    public int ResidentChunksWithInstances;
    public int ChunksWithInstances;
    public int FineTrackedChunks;
    public int CoarseTrackedChunks;
    public int FineChunksWithInstances;
    public int CoarseChunksWithInstances;
    public int ChunksWithStats;
    public int ChunkInstanceMin;
    public int ChunkInstanceMax;
    public long CandidateLanes;
    public long DensityRejectedLanes;
    public long ShapeRejectedLanes;
    public long StateRejectedLanes;
    public long WaterRejectedLanes;
    public long SlopeRejectedLanes;
    public long DistanceRejectedLanes;
    public long DistanceFadeRejectedLanes;
    public long FrustumRejectedLanes;
    public long VisibleLanes;
    public long CandidateBlades;
    public long DensityRejectedBlades;
    public long SlopeRejectedBlades;
    public long InnerFadeRejectedBlades;
    public long EmittedBlades;
    public long OverflowRejectedBlades;
    public long BufferBytes;
    public int OldChunkSuppressedCount;

    public void ResetPerTick()
    {
        DrawCalls = 0;
        BladeInstances = 0;
        ResidentBladeInstances = 0;
        ResidentChunksWithInstances = 0;
        ChunksWithInstances = 0;
        FineTrackedChunks = 0;
        CoarseTrackedChunks = 0;
        FineChunksWithInstances = 0;
        CoarseChunksWithInstances = 0;
        ChunksWithStats = 0;
        ChunkInstanceMin = 0;
        ChunkInstanceMax = 0;
        CandidateLanes = 0;
        DensityRejectedLanes = 0;
        ShapeRejectedLanes = 0;
        StateRejectedLanes = 0;
        WaterRejectedLanes = 0;
        SlopeRejectedLanes = 0;
        DistanceRejectedLanes = 0;
        DistanceFadeRejectedLanes = 0;
        FrustumRejectedLanes = 0;
        VisibleLanes = 0;
        CandidateBlades = 0;
        DensityRejectedBlades = 0;
        SlopeRejectedBlades = 0;
        InnerFadeRejectedBlades = 0;
        EmittedBlades = 0;
        OverflowRejectedBlades = 0;
        BufferBytes = 0L;
        OldChunkSuppressedCount = 0;
    }

    public void Accumulate(GrassChunkRuntime runtime)
    {
        if (runtime == null || !runtime.HasStats)
            return;

        ChunksWithStats++;
        CandidateLanes += runtime.GetStat(StatCandidateLanes);
        DensityRejectedLanes += runtime.GetStat(StatDensityRejectedLanes);
        ShapeRejectedLanes += runtime.GetStat(StatShapeRejectedLanes);
        StateRejectedLanes += runtime.GetStat(StatStateRejectedLanes);
        WaterRejectedLanes += runtime.GetStat(StatWaterRejectedLanes);
        SlopeRejectedLanes += runtime.GetStat(StatSlopeRejectedLanes);
        DistanceRejectedLanes += runtime.GetStat(StatDistanceRejectedLanes);
        DistanceFadeRejectedLanes += runtime.GetStat(StatDistanceFadeRejectedLanes);
        FrustumRejectedLanes += runtime.GetStat(StatFrustumRejectedLanes);
        VisibleLanes += runtime.GetStat(StatVisibleLanes);
        CandidateBlades += runtime.GetStat(StatCandidateBlades);
        DensityRejectedBlades += runtime.GetStat(StatDensityRejectedBlades);
        SlopeRejectedBlades += runtime.GetStat(StatSlopeRejectedBlades);
        InnerFadeRejectedBlades += runtime.GetStat(StatInnerFadeRejectedBlades);
        EmittedBlades += runtime.GetStat(StatEmittedBlades);
        OverflowRejectedBlades += runtime.GetStat(StatOverflowRejectedBlades);
    }
}
