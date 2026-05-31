using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class TerrainFace : IFaceMeshSampler
{
    Mesh _mesh;
    int _resolution;
    Vector3 _localUp;
    Vector3 _axisA;
    Vector3 _axisB;

    ITerrainProvider _terrainProvider;
    Vector3[] _unitSpherePoints;
    float[] _elevations;

    Vector3[] _pendingVertices;
    int[] _pendingTriangles;
    Color[] _pendingColors;
    // Per-vertex raw biome diagnostics (temp, moisture, primaryBiomeIdNormalized, latitude01).
    // Built alongside _pendingColors and uploaded to mesh UV channel 2 so the planet shader
    // can drive biome debug visualizations without re-evaluating noise.
    Vector4[] _pendingBiomeData;
    // Per-vertex magnitude cache for O(1) bilinear surface-radius lookups (camera clamping).
    // Built once in CompleteMeshDataJob; replaces the prior 98K-vertex linear search per query.
    float[] _vertexRadii;

    public int Resolution => _resolution;
    public Vector3[] UnitSpherePoints => _unitSpherePoints;
    public float[] Elevations => _elevations;

    public TerrainFace(ITerrainProvider terrainProvider, Mesh mesh, int resolution, Vector3 localUp)
    {
        _terrainProvider = terrainProvider;
        _mesh = mesh;
        _resolution = resolution;
        _localUp = localUp;

        _axisA = new Vector3(_localUp.y, _localUp.z, _localUp.x);
        _axisB = Vector3.Cross(_localUp, _axisA);
    }

    // Schedules the Burst mesh-generation job for this face. The returned state owns the
    // NativeArrays the job writes into; call CompleteMeshDataJob() to wait, copy out, and
    // dispose. The caller-owned `filters` NativeArray is shared (read-only) across all faces.
    public TerrainFaceJobState ScheduleMeshDataJob(NativeArray<NoiseFilterData> filters, float planetRadius)
    {
        int vertexCount = _resolution * _resolution;
        int triangleCount = (_resolution - 1) * (_resolution - 1) * 6;

        var state = new TerrainFaceJobState
        {
            Resolution = _resolution,
            Vertices = new NativeArray<float3>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            UnitSpherePoints = new NativeArray<float3>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            Elevations = new NativeArray<float>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            Triangles = new NativeArray<int>(triangleCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
        };

        var job = new TerrainFaceMeshJob
        {
            Resolution = _resolution,
            LocalUp = new float3(_localUp.x, _localUp.y, _localUp.z),
            AxisA = new float3(_axisA.x, _axisA.y, _axisA.z),
            AxisB = new float3(_axisB.x, _axisB.y, _axisB.z),
            PlanetRadius = planetRadius,
            Filters = filters,
            Vertices = state.Vertices,
            UnitSpherePoints = state.UnitSpherePoints,
            Elevations = state.Elevations,
            Triangles = state.Triangles,
        };

        state.Handle = job.Schedule(vertexCount, 256);
        return state;
    }

    // Completes the scheduled job, copies its output into the managed mesh arrays expected by
    // ApplyMeshData/CalculateColors, and disposes the job state.
    public void CompleteMeshDataJob(TerrainFaceJobState state)
    {
        state.Handle.Complete();

        int vertexCount = state.Vertices.Length;
        _pendingVertices = new Vector3[vertexCount];
        _unitSpherePoints = new Vector3[vertexCount];
        _elevations = new float[vertexCount];
        _pendingTriangles = new int[state.Triangles.Length];

        // float3 / Vector3 share the same 12-byte layout; Reinterpret avoids per-element conversion.
        var vertsAsVec3 = state.Vertices.Reinterpret<Vector3>(sizeof(float) * 3);
        var spheresAsVec3 = state.UnitSpherePoints.Reinterpret<Vector3>(sizeof(float) * 3);
        NativeArray<Vector3>.Copy(vertsAsVec3, _pendingVertices, vertexCount);
        NativeArray<Vector3>.Copy(spheresAsVec3, _unitSpherePoints, vertexCount);
        NativeArray<float>.Copy(state.Elevations, _elevations, vertexCount);
        NativeArray<int>.Copy(state.Triangles, _pendingTriangles, state.Triangles.Length);

        _vertexRadii = new float[vertexCount];
        for (int i = 0; i < vertexCount; i++)
            _vertexRadii[i] = _pendingVertices[i].magnitude;

        state.Dispose();
    }

    // Bilinear sample of the per-vertex radius at face-UV (u, v in [0, 1]). Callers map a
    // world direction to (face, uv) via CoordinateConverter and dispatch to the matching face.
    public bool TrySampleSurfaceRadius(Vector2 uv, out float radius)
    {
        radius = 0f;
        if (_vertexRadii == null || _vertexRadii.Length == 0) return false;

        float gx = Mathf.Clamp01(uv.x) * (_resolution - 1);
        float gy = Mathf.Clamp01(uv.y) * (_resolution - 1);
        int x0 = Mathf.FloorToInt(gx);
        int y0 = Mathf.FloorToInt(gy);
        int x1 = Mathf.Min(x0 + 1, _resolution - 1);
        int y1 = Mathf.Min(y0 + 1, _resolution - 1);
        float fx = gx - x0;
        float fy = gy - y0;

        float r00 = _vertexRadii[x0 + y0 * _resolution];
        float r10 = _vertexRadii[x1 + y0 * _resolution];
        float r01 = _vertexRadii[x0 + y1 * _resolution];
        float r11 = _vertexRadii[x1 + y1 * _resolution];
        radius = Mathf.Lerp(Mathf.Lerp(r00, r10, fx), Mathf.Lerp(r01, r11, fx), fy);
        return radius > 0f;
    }

    public void ApplyMeshData()
    {
        _mesh.Clear();
        _mesh.vertices = _pendingVertices;
        _mesh.triangles = _pendingTriangles;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    /// <summary>Computes vertex colors from biome data. Safe to call from a background thread.</summary>
    public void CalculateColors(IBiomeProvider biomeProvider)
    {
        if (_unitSpherePoints == null || _elevations == null) return;

        int count = _unitSpherePoints.Length;
        _pendingColors = new Color[count];
        _pendingBiomeData = new Vector4[count];
        for (int i = 0; i < count; i++)
            _pendingColors[i] = biomeProvider.GetBiomeColorAndData(_unitSpherePoints[i], _elevations[i], out _pendingBiomeData[i]);
    }

    /// <summary>Uploads previously computed colors to the mesh. Must be called on the main thread.</summary>
    public void ApplyColors()
    {
        if (_pendingColors != null)
            _mesh.colors = _pendingColors;
        // UV channel 2 carries raw biome diagnostics — see PlanetVertexColor.shader debug branches.
        if (_pendingBiomeData != null)
            _mesh.SetUVs(2, _pendingBiomeData);
    }

    public void UpdateColors(IBiomeProvider biomeProvider)
    {
        CalculateColors(biomeProvider);
        ApplyColors();
    }
}

public struct TerrainFaceJobState
{
    public int Resolution;
    public JobHandle Handle;
    public NativeArray<float3> Vertices;
    public NativeArray<float3> UnitSpherePoints;
    public NativeArray<float> Elevations;
    public NativeArray<int> Triangles;

    public void Dispose()
    {
        if (Vertices.IsCreated) Vertices.Dispose();
        if (UnitSpherePoints.IsCreated) UnitSpherePoints.Dispose();
        if (Elevations.IsCreated) Elevations.Dispose();
        if (Triangles.IsCreated) Triangles.Dispose();
    }
}

[BurstCompile]
public struct TerrainFaceMeshJob : IJobParallelFor
{
    public int Resolution;
    public float3 LocalUp;
    public float3 AxisA;
    public float3 AxisB;
    public float PlanetRadius;

    [ReadOnly] public NativeArray<NoiseFilterData> Filters;

    [WriteOnly] public NativeArray<float3> Vertices;
    [WriteOnly] public NativeArray<float3> UnitSpherePoints;
    [WriteOnly] public NativeArray<float> Elevations;

    // Triangles are written in 6-int strides at indices computed from (x, y); writes from
    // different vertices never overlap, but the index isn't the iteration index, so we relax
    // the parallel-for restriction. Safety holds via the strict (x, y) → triangle-stride map.
    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> Triangles;

    public void Execute(int index)
    {
        int x = index % Resolution;
        int y = index / Resolution;

        float invRm1 = 1f / (Resolution - 1);
        float2 percent = new float2(x * invRm1, y * invRm1);
        float3 pointOnUnitCube = LocalUp + (percent.x - 0.5f) * 2f * AxisA + (percent.y - 0.5f) * 2f * AxisB;
        float3 pointOnUnitSphere = math.normalize(pointOnUnitCube);

        float elevation = EvaluateElevation(pointOnUnitSphere);

        UnitSpherePoints[index] = pointOnUnitSphere;
        Elevations[index] = elevation;
        Vertices[index] = pointOnUnitSphere * (PlanetRadius * (1f + elevation));

        if (x < Resolution - 1 && y < Resolution - 1)
        {
            int triIdx = (x + y * (Resolution - 1)) * 6;
            Triangles[triIdx]     = index;
            Triangles[triIdx + 1] = index + Resolution + 1;
            Triangles[triIdx + 2] = index + Resolution;
            Triangles[triIdx + 3] = index;
            Triangles[triIdx + 4] = index + 1;
            Triangles[triIdx + 5] = index + Resolution + 1;
        }
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
