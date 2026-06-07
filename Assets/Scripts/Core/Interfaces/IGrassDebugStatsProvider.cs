public struct GrassDebugStats
{
    public bool ControllerActive;
    public bool ShaderAvailable;
    public bool SmokeRenderer;
    public int VisibleChunks;
    public int TrackedChunks;
    public int DrawCalls;
    public int BladeInstances;
    public int MaxChunkDepth;
    public int MinChunkDepthForBlades;
    public int MaxCoarseLodOffsetForBlades;
    public int MaxBladesPerLane;
    public int VisualBladesPerInstance;
    public int BladeVertexCount;
    public float DensityMultiplier;
    public float MaxRenderDistance;
    public float DistanceFadeStart;
    public float CullDistanceJitter01;
    public int SurfaceAtlasResolution;
    public float BufferMegabytes;
    public int PlacementDispatches;
    public int ChunksWithInstances;
    public int ChunksWithStats;
    public int ChunkInstanceMin;
    public int ChunkInstanceMax;
    public float ChunkInstanceAverage;
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
    public long EmittedBlades;
    public long OverflowRejectedBlades;
    public int OldChunkSuppressedCount;
}

public interface IGrassDebugStatsProvider
{
    GrassDebugStats GetGrassDebugStats();
}

public enum GrassRenderLayer
{
    Near,
    Mid,
    Chunk,
    Blanket,
}

public struct GrassRuntimeState
{
    public bool MasterEnabled;
    public bool NearFieldRequested;
    public bool NearFieldActive;
    public bool MidFieldRequested;
    public bool MidFieldActive;
    public bool ChunkPathRequested;
    public bool ChunkPathActive;
    public bool BlanketRequested;
    public bool BlanketActive;
}

public interface IGrassRuntimeControl
{
    GrassRuntimeState GetGrassRuntimeState();
    void SetGrassEnabled(bool enabled);
    void SetGrassLayerEnabled(GrassRenderLayer layer, bool enabled);
}
