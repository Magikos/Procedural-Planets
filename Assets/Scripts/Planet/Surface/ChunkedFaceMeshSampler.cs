using System.Collections.Generic;
using UnityEngine;

// Exposes a single per-face unit-sphere + elevation grid that WaterMeshBuilder consumes.
//
// Two construction modes:
//   - Root-only: wraps the depth-0 root chunk's CPU arrays directly (cheap, but limits
//     water-mesh resolution to ChunkResolution per face → coarse shorelines).
//   - Aggregated: stitches together all chunks at a fixed depth into one big grid. For a
//     face built to aggregateDepth=D with chunk resolution R, the stitched grid has
//     2^D × (R-1) + 1 vertices per axis. Depth 2 with R=97 → 385² per face (16× finer
//     shorelines than the root sampler at the cost of ~14 MB total across all 6 faces).
//
// Use the aggregated form when the water builder's shoreline quality matters; use the
// root-only form for smoke tests or low-memory configurations.
public sealed class ChunkedFaceMeshSampler : IFaceMeshSampler
{
    readonly PlanetChunk _rootChunk;
    readonly int _resolution;
    readonly Vector3[] _aggregatedUnitSpherePoints;
    readonly float[] _aggregatedElevations;

    public Vector3[] UnitSpherePoints => _aggregatedUnitSpherePoints ?? _rootChunk?.CpuUnitSpherePoints;
    public float[] Elevations => _aggregatedElevations ?? _rootChunk?.CpuElevations;
    public int Resolution => _resolution;

    // Root-only constructor (legacy / smoke-test path).
    public ChunkedFaceMeshSampler(PlanetChunk rootChunk, int resolution)
    {
        _rootChunk = rootChunk;
        _resolution = resolution;
    }

    // Aggregated constructor — stitches chunks at a fixed depth into one big per-face grid.
    // `chunksAtDepth` must contain every chunk on this face at exactly `aggregateDepth`
    // (2^aggregateDepth × 2^aggregateDepth chunks per face), each with populated CpuVertices.
    public ChunkedFaceMeshSampler(IReadOnlyList<PlanetChunk> chunksAtDepth, int chunkResolution, int aggregateDepth)
    {
        int chunksPerSide = 1 << aggregateDepth;
        int aggRes = chunksPerSide * (chunkResolution - 1) + 1;
        _resolution = aggRes;
        _aggregatedUnitSpherePoints = new Vector3[aggRes * aggRes];
        _aggregatedElevations = new float[aggRes * aggRes];

        if (chunksAtDepth == null) return;

        for (int i = 0; i < chunksAtDepth.Count; i++)
        {
            var chunk = chunksAtDepth[i];
            if (chunk == null || chunk.CpuUnitSpherePoints == null || chunk.CpuElevations == null) continue;
            if (chunk.DetailLevel != aggregateDepth) continue;

            // Chunk UV bounds map to integer offsets in the aggregated grid. Because chunks at
            // the same depth share boundary positions (deterministic noise → identical world
            // positions on shared edges), overlapping writes from neighbor chunks produce the
            // same value at the seam — safe to overwrite.
            float chunkUvMinU = chunk.UvCenter.x - chunk.UvHalfExtent;
            float chunkUvMinV = chunk.UvCenter.y - chunk.UvHalfExtent;
            int aggOriginX = Mathf.RoundToInt(chunkUvMinU * (aggRes - 1));
            int aggOriginY = Mathf.RoundToInt(chunkUvMinV * (aggRes - 1));

            for (int y = 0; y < chunkResolution; y++)
            {
                int aggY = aggOriginY + y;
                if (aggY < 0 || aggY >= aggRes) continue;
                for (int x = 0; x < chunkResolution; x++)
                {
                    int aggX = aggOriginX + x;
                    if (aggX < 0 || aggX >= aggRes) continue;
                    int srcIdx = y * chunkResolution + x;
                    int dstIdx = aggY * aggRes + aggX;
                    _aggregatedUnitSpherePoints[dstIdx] = chunk.CpuUnitSpherePoints[srcIdx];
                    _aggregatedElevations[dstIdx] = chunk.CpuElevations[srcIdx];
                }
            }
        }
    }
}
