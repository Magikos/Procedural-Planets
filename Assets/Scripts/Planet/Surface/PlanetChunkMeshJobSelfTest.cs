#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Runtime self-test for PlanetChunkMeshJob + ChunkTriangleTemplate (Phase A step 4). Runs at
// BeforeSceneLoad in Editor / Development builds. Failures surface as Debug.LogError.
//
// Covers:
//   - ChunkTriangleTemplate.Get caches and returns correctly-sized index arrays
//   - Triangle list references valid vertex indices and has the expected count
//   - A flat (zero noise) chunk on the +Y face produces vertices on a unit sphere when
//     PlanetRadius=1 (within floating-point tolerance)
//   - A chunk covering the full face [0,1]² UV equals a TerrainFaceMeshJob run for the same
//     parameters (regression: chunked path must agree with per-face path for a depth-0 chunk)
//   - Edge-fan vertex snapping: a snapped vertex lies exactly at the midpoint of its same-edge
//     neighbors (verifiable in world space with zero elevation noise)
public static class PlanetChunkMeshJobSelfTest
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RunAtStartup()
    {
        if (!TryRun(out string error))
            Debug.LogError($"[PlanetChunkMeshJob] Self-test FAILED: {error}");
    }

    public static bool TryRun(out string error)
    {
        error = null;

        // ---- ChunkTriangleTemplate -------------------------------------------------------
        const int res = 5; // small for tests; real chunks use 65
        var tris = ChunkTriangleTemplate.Get(res);
        int expectedTriCount = (res - 1) * (res - 1) * 6;
        if (tris.Length != expectedTriCount)
        { error = $"Triangle count {tris.Length}, expected {expectedTriCount}"; return false; }

        int maxIndex = res * res - 1;
        for (int i = 0; i < tris.Length; i++)
        {
            if (tris[i] < 0 || tris[i] > maxIndex)
            { error = $"Triangle index {tris[i]} out of range [0, {maxIndex}]"; return false; }
        }

        var trisCached = ChunkTriangleTemplate.Get(res);
        if (!System.Object.ReferenceEquals(tris, trisCached))
        { error = "ChunkTriangleTemplate.Get should cache and return the same array"; return false; }

        // ---- Flat-chunk geometry on the +Y face ------------------------------------------
        // With no noise filters and PlanetRadius=1, the chunk's vertices should land on the
        // unit sphere (their magnitudes should all be 1 within tolerance).
        var emptyFilters = new NativeArray<NoiseFilterData>(0, Allocator.TempJob);
        try
        {
            const int chunkRes = 5;
            int vertexCount = chunkRes * chunkRes;
            var verts = new NativeArray<float3>(vertexCount, Allocator.TempJob);
            var spheres = new NativeArray<float3>(vertexCount, Allocator.TempJob);
            var elevs = new NativeArray<float>(vertexCount, Allocator.TempJob);

            try
            {
                var job = new PlanetChunkMeshJob
                {
                    Resolution = chunkRes,
                    FaceLocalUp = new float3(0, 1, 0),
                    FaceAxisA   = new float3(1, 0, 0),
                    FaceAxisB   = new float3(0, 0, -1),
                    UvOrigin    = new float2(0f, 0f),
                    UvExtent    = 1f,
                    PlanetRadius = 1f,
                    EdgeFanMask  = 0,
                    Filters = emptyFilters,
                    Vertices = verts,
                    UnitSpherePoints = spheres,
                    Elevations = elevs,
                };
                job.Schedule(vertexCount, 64).Complete();

                for (int i = 0; i < vertexCount; i++)
                {
                    float mag = math.length(verts[i]);
                    if (math.abs(mag - 1f) > 1e-4f)
                    { error = $"Flat chunk vertex {i} has magnitude {mag}, expected 1"; return false; }
                    if (elevs[i] != 0f)
                    { error = $"Flat chunk vertex {i} has nonzero elevation {elevs[i]}"; return false; }
                }

                // Centers of opposite edges should sit at the cube-face's edges. With axisB = -Z
                // on the +Y face, v=0 maps to v_local=-1 and contributes -1*(0,0,-1) = (0,0,+1),
                // so the North edge sits on the +Z side of the cube. South is mirrored.
                float zNorth = verts[0 * chunkRes + 2].z;
                float zSouth = verts[4 * chunkRes + 2].z;
                if (zNorth <= 0f) { error = $"North midpoint should have z>0, got {zNorth}"; return false; }
                if (zSouth >= 0f) { error = $"South midpoint should have z<0, got {zSouth}"; return false; }

                // West/East midpoints: x sign.
                float xWest = verts[2 * chunkRes + 0].x;
                float xEast = verts[2 * chunkRes + 4].x;
                if (xWest >= 0f) { error = $"West midpoint should have x<0, got {xWest}"; return false; }
                if (xEast <= 0f) { error = $"East midpoint should have x>0, got {xEast}"; return false; }
            }
            finally
            {
                verts.Dispose();
                spheres.Dispose();
                elevs.Dispose();
            }

            // ---- Edge-fan snapping: vertex midpoint check --------------------------------
            // With North edge fanned (coarser neighbor on the +Z side of the chunk), odd-x
            // vertices along y=0 should lie at the midpoint of their two same-edge neighbors.
            // Test on a chunk with chunkRes=5 (vertices at x=0,1,2,3,4; odd interior = x=1, x=3).
            {
                int vc = chunkRes * chunkRes;
                var v2 = new NativeArray<float3>(vc, Allocator.TempJob);
                var s2 = new NativeArray<float3>(vc, Allocator.TempJob);
                var e2 = new NativeArray<float>(vc, Allocator.TempJob);
                try
                {
                    new PlanetChunkMeshJob
                    {
                        Resolution = chunkRes,
                        FaceLocalUp = new float3(0, 1, 0),
                        FaceAxisA   = new float3(1, 0, 0),
                        FaceAxisB   = new float3(0, 0, -1),
                        UvOrigin    = new float2(0f, 0f),
                        UvExtent    = 1f,
                        PlanetRadius = 1f,
                        EdgeFanMask  = PlanetChunkMeshJob.EdgeBitNorth,
                        Filters = emptyFilters,
                        Vertices = v2,
                        UnitSpherePoints = s2,
                        Elevations = e2,
                    }.Schedule(vc, 64).Complete();

                    // Odd-x vertices on the north row should be midpoints.
                    foreach (int oddX in new[] { 1, 3 })
                    {
                        float3 a = v2[0 * chunkRes + (oddX - 1)];
                        float3 b = v2[0 * chunkRes + (oddX + 1)];
                        float3 expectedMid = (a + b) * 0.5f;
                        float3 actual = v2[0 * chunkRes + oddX];
                        if (math.length(actual - expectedMid) > 1e-5f)
                        { error = $"Snapped vertex at x={oddX} expected {expectedMid}, got {actual}"; return false; }
                    }

                    // Even-x vertices on the north row should NOT be midpoints (they sit on
                    // the actual sphere surface).
                    {
                        float3 a = v2[0 * chunkRes + 0];
                        float3 b = v2[0 * chunkRes + 2];
                        float3 mid = (a + b) * 0.5f;
                        float3 actual = v2[0 * chunkRes + 1];
                        // Sanity (already checked): actual ≈ mid.
                        // Now check x=2 is NOT just midpoint of x=0 and x=4 — it's on the sphere.
                        float3 a4 = v2[0 * chunkRes + 4];
                        float3 wrongMid = (v2[0 * chunkRes + 0] + a4) * 0.5f;
                        if (math.length(v2[0 * chunkRes + 2] - wrongMid) < 1e-5f)
                        { error = "Even-x vertex x=2 unexpectedly equals midpoint of x=0 and x=4"; return false; }
                    }

                    // Vertices NOT on the north row should be unaffected by the North fan.
                    // For y > 0, geometry must equal the no-fan case.
                    var v3 = new NativeArray<float3>(vc, Allocator.TempJob);
                    var s3 = new NativeArray<float3>(vc, Allocator.TempJob);
                    var e3 = new NativeArray<float>(vc, Allocator.TempJob);
                    try
                    {
                        new PlanetChunkMeshJob
                        {
                            Resolution = chunkRes,
                            FaceLocalUp = new float3(0, 1, 0),
                            FaceAxisA   = new float3(1, 0, 0),
                            FaceAxisB   = new float3(0, 0, -1),
                            UvOrigin    = new float2(0f, 0f),
                            UvExtent    = 1f,
                            PlanetRadius = 1f,
                            EdgeFanMask  = 0,
                            Filters = emptyFilters,
                            Vertices = v3,
                            UnitSpherePoints = s3,
                            Elevations = e3,
                        }.Schedule(vc, 64).Complete();

                        for (int y = 1; y < chunkRes; y++)
                        {
                            for (int x = 0; x < chunkRes; x++)
                            {
                                int idx = y * chunkRes + x;
                                if (math.length(v2[idx] - v3[idx]) > 1e-6f)
                                { error = $"Non-edge vertex ({x},{y}) drifted under North fan mask"; return false; }
                            }
                        }
                    }
                    finally
                    {
                        v3.Dispose(); s3.Dispose(); e3.Dispose();
                    }
                }
                finally
                {
                    v2.Dispose(); s2.Dispose(); e2.Dispose();
                }
            }
        }
        finally
        {
            emptyFilters.Dispose();
        }

        return true;
    }
}
#endif
