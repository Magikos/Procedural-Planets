using Unity.Collections;

// Optional capability a ground sampler advertises when its elevation can be evaluated inside a Burst
// job — i.e. it is backed by the analytic noise stack (blittable NoiseFilterData + diagnostic terrain).
// The scatter tile cache uses this to run the placement gather in parallel Burst; a sampler that can't
// provide it (e.g. a future marching-cubes/SDF field) simply doesn't implement it and the cache falls
// back to the managed serial gather.
public interface IBurstElevationSource
{
    NativeArray<NoiseFilterData> BuildNoiseFilterData(Allocator allocator);
    NativeArray<byte> BuildDiagnosticCells(Allocator allocator);
    DiagnosticTerrainSettingsData DiagnosticData { get; }
    float PlanetRadius { get; }
}
