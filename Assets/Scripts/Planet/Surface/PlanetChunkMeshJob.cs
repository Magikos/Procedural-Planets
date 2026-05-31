using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// Burst-compiled chunk mesh generation (Phase A step 4). Mirrors TerrainFaceMeshJob but
// operates over a UV sub-region of a face rather than the whole face. Triangles are NOT
// written here — see ChunkTriangleTemplate (shared per-resolution array).
//
// Edge-fan handling: when EdgeFanMask has a bit set for a given edge, every other vertex along
// that edge gets snapped to the midpoint of its two same-edge neighbors. The midpoint is
// computed in WORLD space (after elevation displacement) so the snapped vertex lies exactly on
// the straight line between its neighbors — matching the coarser-LOD neighbor's edge geometry.
//
// Requires odd Resolution (e.g. 65). Even resolution would put a corner on an odd-x index,
// which would get snap-incorrectly handled.
[BurstCompile]
public struct PlanetChunkMeshJob : IJobParallelFor
{
    public const byte EdgeBitEast  = 0b0001;
    public const byte EdgeBitWest  = 0b0010;
    public const byte EdgeBitNorth = 0b0100;
    public const byte EdgeBitSouth = 0b1000;

    public int Resolution;
    public float3 FaceLocalUp;
    public float3 FaceAxisA;
    public float3 FaceAxisB;
    // Chunk sub-region within the face's [0,1]² UV space.
    public float2 UvOrigin;            // (u_min, v_min) of the chunk
    public float UvExtent;             // chunk's UV side length = 2 * chunk.UvHalfExtent
    public float PlanetRadius;
    public byte EdgeFanMask;           // bits ESNW — see EdgeBit* constants

    [ReadOnly] public NativeArray<NoiseFilterData> Filters;

    [WriteOnly] public NativeArray<float3> Vertices;
    [WriteOnly] public NativeArray<float3> UnitSpherePoints;
    [WriteOnly] public NativeArray<float> Elevations;

    public void Execute(int index)
    {
        int x = index % Resolution;
        int y = index / Resolution;

        // Detect if this vertex must be snapped along the X axis (interior odd vertex on the
        // North or South edge with a coarser neighbor) or the Y axis (interior odd vertex on
        // the East or West edge with a coarser neighbor).
        bool snapAlongX = false;
        bool snapAlongY = false;

        bool onNorth = (y == 0);
        bool onSouth = (y == Resolution - 1);
        bool onWest  = (x == 0);
        bool onEast  = (x == Resolution - 1);

        if (onNorth && (EdgeFanMask & EdgeBitNorth) != 0 && (x & 1) == 1 && x > 0 && x < Resolution - 1)
            snapAlongX = true;
        else if (onSouth && (EdgeFanMask & EdgeBitSouth) != 0 && (x & 1) == 1 && x > 0 && x < Resolution - 1)
            snapAlongX = true;
        else if (onWest && (EdgeFanMask & EdgeBitWest) != 0 && (y & 1) == 1 && y > 0 && y < Resolution - 1)
            snapAlongY = true;
        else if (onEast && (EdgeFanMask & EdgeBitEast) != 0 && (y & 1) == 1 && y > 0 && y < Resolution - 1)
            snapAlongY = true;

        if (snapAlongX)
        {
            // Average the world positions of the two same-edge neighbors at (x-1, y) and (x+1, y).
            ComputeVertexWorld(x - 1, y, out float3 prevWorld, out float3 prevSphere, out float prevElev);
            ComputeVertexWorld(x + 1, y, out float3 nextWorld, out float3 nextSphere, out float nextElev);
            float3 world = (prevWorld + nextWorld) * 0.5f;
            Vertices[index] = world;
            UnitSpherePoints[index] = math.normalize((prevSphere + nextSphere) * 0.5f);
            Elevations[index] = (prevElev + nextElev) * 0.5f;
            return;
        }

        if (snapAlongY)
        {
            ComputeVertexWorld(x, y - 1, out float3 prevWorld, out float3 prevSphere, out float prevElev);
            ComputeVertexWorld(x, y + 1, out float3 nextWorld, out float3 nextSphere, out float nextElev);
            float3 world = (prevWorld + nextWorld) * 0.5f;
            Vertices[index] = world;
            UnitSpherePoints[index] = math.normalize((prevSphere + nextSphere) * 0.5f);
            Elevations[index] = (prevElev + nextElev) * 0.5f;
            return;
        }

        ComputeVertexWorld(x, y, out float3 worldPos, out float3 spherePoint, out float elevation);
        Vertices[index] = worldPos;
        UnitSpherePoints[index] = spherePoint;
        Elevations[index] = elevation;
    }

    // Inline computation shared by the normal path and the snap-neighbor lookups.
    void ComputeVertexWorld(int x, int y, out float3 worldPos, out float3 spherePoint, out float elevation)
    {
        float invRm1 = 1f / (Resolution - 1);
        // UV inside the chunk's local [0,1]² sub-region:
        float localU = x * invRm1;
        float localV = y * invRm1;
        // Map into the face's full [0,1]² UV, then to (-1, 1) for cube positioning.
        float faceU = UvOrigin.x + localU * UvExtent;
        float faceV = UvOrigin.y + localV * UvExtent;
        float u_local = faceU * 2f - 1f;
        float v_local = faceV * 2f - 1f;

        float3 pointOnUnitCube = FaceLocalUp + u_local * FaceAxisA + v_local * FaceAxisB;
        spherePoint = math.normalize(pointOnUnitCube);
        elevation = EvaluateElevation(spherePoint);
        worldPos = spherePoint * (PlanetRadius * (1f + elevation));
    }

    float EvaluateElevation(float3 point)
    {
        int count = Filters.Length;
        if (count == 0) return 0f;

        var first = Filters[0];
        float firstLayerValue = NoiseFilterEvaluator.Evaluate(ref first, point);
        float elevation = first.Enabled != 0 ? firstLayerValue : 0f;

        for (int i = 1; i < count; i++)
        {
            var f = Filters[i];
            if (f.Enabled == 0) continue;
            float mask = f.UseFirstLayerAsMask != 0 ? firstLayerValue : 1f;
            elevation += NoiseFilterEvaluator.Evaluate(ref f, point) * mask;
        }

        return elevation;
    }
}

// Second-pass Burst job that derives true terrain-aware vertex normals from the displaced
// vertex positions written by PlanetChunkMeshJob. Runs as a chained dependency: the mesh job
// completes first, then this job reads the resulting Vertices array.
//
// Normals = cross(u-tangent, v-tangent) computed from central-differenced neighbor positions.
// Edge vertices use one-sided differences (neighbor index clamped). Outward orientation is
// enforced by flipping if the result disagrees with the vertex direction.
//
// Falling back to unit-sphere-direction normals (which the previous design used) gives a
// smooth "perfect sphere" look that doesn't react to terrain elevation — valleys and ridges
// blend into the surface. True normals from cross-products let lighting bring out the
// underlying noise displacement.
[BurstCompile]
public struct PlanetChunkNormalsJob : IJobParallelFor
{
    public int Resolution;
    [ReadOnly] public NativeArray<float3> Vertices;
    [WriteOnly] public NativeArray<float3> Normals;

    public void Execute(int index)
    {
        int x = index % Resolution;
        int y = index / Resolution;

        int xm = math.max(x - 1, 0);
        int xp = math.min(x + 1, Resolution - 1);
        int ym = math.max(y - 1, 0);
        int yp = math.min(y + 1, Resolution - 1);

        float3 self = Vertices[index];
        float3 tu = Vertices[y * Resolution + xp] - Vertices[y * Resolution + xm];
        float3 tv = Vertices[yp * Resolution + x] - Vertices[ym * Resolution + x];

        float3 n = math.cross(tu, tv);
        float lenSq = math.lengthsq(n);
        if (lenSq < 1e-12f)
        {
            // Degenerate (coincident neighbors). Fall back to vertex direction.
            n = math.normalize(self);
        }
        else
        {
            n = n * math.rsqrt(lenSq);
        }

        // Outward-facing normal should point in same hemisphere as the vertex (planet center
        // is at origin in local space). Flip if not.
        if (math.dot(n, self) < 0f) n = -n;

        Normals[index] = n;
    }
}

// Lifecycle wrapper for the chunk mesh job — holds the NativeArrays and JobHandle the same
// way TerrainFaceJobState does for the per-face path. The owning code disposes via Dispose().
public struct PlanetChunkMeshJobState
{
    public int Resolution;
    public JobHandle Handle;
    public NativeArray<float3> Vertices;
    public NativeArray<float3> UnitSpherePoints;
    public NativeArray<float> Elevations;
    public NativeArray<float3> Normals;

    public void Dispose()
    {
        if (Vertices.IsCreated) Vertices.Dispose();
        if (UnitSpherePoints.IsCreated) UnitSpherePoints.Dispose();
        if (Elevations.IsCreated) Elevations.Dispose();
        if (Normals.IsCreated) Normals.Dispose();
    }
}
