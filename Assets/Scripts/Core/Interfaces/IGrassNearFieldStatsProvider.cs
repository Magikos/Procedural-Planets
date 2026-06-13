public enum GrassNearFieldDispatchReason
{
    None = 0,
    Initial = 1,
    FaceChanged = 2,
    PageChanged = 3,
    Forced = 4,
}

public struct GrassNearFieldStats
{
    public bool ControllerActive;
    public bool ShaderAvailable;
    public int GridWidth;
    public int GridHeight;
    public float Spacing;
    public float FullDensityDistance;
    public float DrawDistance;
    public float FadeBand;
    public float SuppressionRadius;
    public int PageCellSize;
    public int PageOriginCellU;
    public int PageOriginCellV;
    public int FaceIndex;
    public int FacesActive;
    public bool MultiFaceDispatchEnabled;
    public bool SeamRisk;
    public GrassNearFieldDispatchReason LastDispatchReason;
    public bool DispatchedThisFrame;
    public int DispatchesTotal;
    public int EmittedInstances;
    public int VisualBladesPerInstance;
    public int BladeVertexCount;
    public long CandidateCells;
    public long DensityRejectedCells;
    public long WaterRejectedCells;
    public long SlopeRejectedCells;
    public long DistanceRejectedCells;
    public long DistanceFadeRejectedCells;
    public long FrustumRejectedCells;
    public long FaceAreaRejectedCells;
    public long RangeBudgetRejectedCells;
    public long OverflowDropped;
    public int CapacityInstances;
    public float BufferMegabytes;
}

public interface IGrassNearFieldStatsProvider
{
    GrassNearFieldStats GetGrassNearFieldStats();
}
