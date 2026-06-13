using System.Collections.Generic;
using UnityEngine;

struct WaterDebugStats
{
    public bool Valid;
    public int Vertices;
    public int Triangles;
    public float DepthMin, DepthMax, DepthAvg;
    public float ShoreMin, ShoreMax, ShoreAvg;
    public float BodyMin, BodyMax, BodyAvg;
    public float TemperatureMin, TemperatureMax, TemperatureAvg;
    public float MotionMaskAvg, MotionMaskMax, MotionMaskSample;
    public float NormalMaskAvg, NormalMaskMax, NormalMaskSample;
    public float SampleDepth, SampleShore, SampleBody;
    public float SampleTemperature;
    public float MotionEligiblePercent;
    public float NormalEligiblePercent;
}

struct MeshIntegrityStats
{
    public int DegenerateTriangles;
    public int BoundaryEdges;
    public int NonManifoldEdges;
    public int OpenEdgeVertices;
    public float RadiusErrorAvgMeters;
    public float RadiusErrorMaxMeters;
}

// CPU analysis of the water mesh for the water debug surface: the vertex-color histogram + camera
// sample (Compute) and the topology/radius integrity checks (TryAnalyzeMeshIntegrity). The histogram
// aggregates are mesh-static and cached; only the camera-aligned sample is recomputed per call.
sealed class WaterMeshAnalysis
{
    WaterMeshStatsCache _meshStatsCache;

    public WaterDebugStats Compute(Renderer waterRenderer, ICameraRigContext cameraContext, out Mesh analyzedMesh)
    {
        analyzedMesh = null;

        if (waterRenderer == null || !waterRenderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
            return default;

        Mesh mesh = filter.sharedMesh;
        Color[] colors = mesh.colors;
        if (colors == null || colors.Length == 0)
            return default;

        Vector3[] vertices = mesh.vertices;
        int count = Mathf.Min(colors.Length, vertices != null ? vertices.Length : colors.Length);
        if (count <= 0)
            return default;

        // The mesh-static aggregates (min/max/avg/percent) only change when the underlying mesh
        // does, so recompute them lazily and reuse for all subsequent refreshes.
        if (_meshStatsCache.Source != mesh)
            _meshStatsCache = RecomputeMeshStats(mesh, colors, count);

        // Camera-direction-best vertex stays per-refresh; it's now a single dot product per
        // vertex instead of the full histogram pass.
        Vector3 localCamera = cameraContext != null ? waterRenderer.transform.InverseTransformPoint(cameraContext.CameraTransform.position) : Vector3.zero;
        Vector3 localCameraDir = localCamera.sqrMagnitude > 0.0001f ? localCamera.normalized : Vector3.up;
        int sampleIndex = 0;
        float bestAlignment = -2f;
        if (vertices != null)
        {
            for (int i = 0; i < count; i++)
            {
                if (vertices[i].sqrMagnitude <= 0.0001f) continue;
                float alignment = Vector3.Dot(vertices[i].normalized, localCameraDir);
                if (alignment > bestAlignment)
                {
                    bestAlignment = alignment;
                    sampleIndex = i;
                }
            }
        }

        var stats = new WaterDebugStats
        {
            Valid = true,
            Vertices = _meshStatsCache.Vertices,
            Triangles = _meshStatsCache.Triangles,
            DepthMin = _meshStatsCache.DepthMin,
            DepthMax = _meshStatsCache.DepthMax,
            DepthAvg = _meshStatsCache.DepthAvg,
            ShoreMin = _meshStatsCache.ShoreMin,
            ShoreMax = _meshStatsCache.ShoreMax,
            ShoreAvg = _meshStatsCache.ShoreAvg,
            BodyMin = _meshStatsCache.BodyMin,
            BodyMax = _meshStatsCache.BodyMax,
            BodyAvg = _meshStatsCache.BodyAvg,
            TemperatureMin = _meshStatsCache.TemperatureMin,
            TemperatureMax = _meshStatsCache.TemperatureMax,
            TemperatureAvg = _meshStatsCache.TemperatureAvg,
            MotionMaskAvg = _meshStatsCache.MotionMaskAvg,
            MotionMaskMax = _meshStatsCache.MotionMaskMax,
            NormalMaskAvg = _meshStatsCache.NormalMaskAvg,
            NormalMaskMax = _meshStatsCache.NormalMaskMax,
            MotionEligiblePercent = _meshStatsCache.MotionEligiblePercent,
            NormalEligiblePercent = _meshStatsCache.NormalEligiblePercent,
        };

        Color sample = colors[Mathf.Clamp(sampleIndex, 0, colors.Length - 1)];
        stats.SampleDepth = Mathf.Clamp01(sample.r);
        stats.SampleShore = Mathf.Clamp01(sample.g);
        stats.SampleBody = Mathf.Clamp01(sample.b);
        stats.SampleTemperature = Mathf.Clamp01(sample.a);
        stats.MotionMaskSample = FocusMotionMask(stats.SampleDepth, stats.SampleShore, stats.SampleBody);
        stats.NormalMaskSample = FocusNormalMask(stats.SampleDepth, stats.SampleShore, stats.SampleBody);

        analyzedMesh = mesh;
        return stats;
    }

    static WaterMeshStatsCache RecomputeMeshStats(Mesh mesh, Color[] colors, int count)
    {
        var cache = new WaterMeshStatsCache
        {
            Source = mesh,
            Vertices = mesh.vertexCount,
            Triangles = mesh.subMeshCount > 0 ? (int)(mesh.GetIndexCount(0) / 3) : 0,
            DepthMin = 1f,
            ShoreMin = 1f,
            BodyMin = 1f,
            TemperatureMin = 1f,
        };

        int motionEligible = 0;
        int normalEligible = 0;
        for (int i = 0; i < count; i++)
        {
            Color c = colors[i];
            float depth = Mathf.Clamp01(c.r);
            float shore = Mathf.Clamp01(c.g);
            float body = Mathf.Clamp01(c.b);
            float temperature = Mathf.Clamp01(c.a);
            float motionMask = FocusMotionMask(depth, shore, body);
            float normalMask = FocusNormalMask(depth, shore, body);

            cache.DepthMin = Mathf.Min(cache.DepthMin, depth);
            cache.DepthMax = Mathf.Max(cache.DepthMax, depth);
            cache.DepthAvg += depth;
            cache.ShoreMin = Mathf.Min(cache.ShoreMin, shore);
            cache.ShoreMax = Mathf.Max(cache.ShoreMax, shore);
            cache.ShoreAvg += shore;
            cache.BodyMin = Mathf.Min(cache.BodyMin, body);
            cache.BodyMax = Mathf.Max(cache.BodyMax, body);
            cache.BodyAvg += body;
            cache.TemperatureMin = Mathf.Min(cache.TemperatureMin, temperature);
            cache.TemperatureMax = Mathf.Max(cache.TemperatureMax, temperature);
            cache.TemperatureAvg += temperature;
            cache.MotionMaskAvg += motionMask;
            cache.MotionMaskMax = Mathf.Max(cache.MotionMaskMax, motionMask);
            cache.NormalMaskAvg += normalMask;
            cache.NormalMaskMax = Mathf.Max(cache.NormalMaskMax, normalMask);

            if (motionMask > 0.05f) motionEligible++;
            if (normalMask > 0.05f) normalEligible++;
        }

        float invCount = 1f / count;
        cache.DepthAvg *= invCount;
        cache.ShoreAvg *= invCount;
        cache.BodyAvg *= invCount;
        cache.TemperatureAvg *= invCount;
        cache.MotionMaskAvg *= invCount;
        cache.NormalMaskAvg *= invCount;
        cache.MotionEligiblePercent = motionEligible * 100f * invCount;
        cache.NormalEligiblePercent = normalEligible * 100f * invCount;

        return cache;
    }

    struct WaterMeshStatsCache
    {
        public Mesh Source;
        public int Vertices;
        public int Triangles;
        public float DepthMin, DepthMax, DepthAvg;
        public float ShoreMin, ShoreMax, ShoreAvg;
        public float BodyMin, BodyMax, BodyAvg;
        public float TemperatureMin, TemperatureMax, TemperatureAvg;
        public float MotionMaskAvg, MotionMaskMax;
        public float NormalMaskAvg, NormalMaskMax;
        public float MotionEligiblePercent;
        public float NormalEligiblePercent;
    }

    public static bool TryAnalyzeMeshIntegrity(Mesh mesh, Transform meshTransform, Vector3 planetCenter, float expectedRadius, out MeshIntegrityStats stats)
    {
        stats = default;
        if (mesh == null)
            return false;

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length < 3)
            return false;

        Dictionary<ulong, int> edgeUse = new Dictionary<ulong, int>(triangles.Length);
        HashSet<int> openVertices = new HashSet<int>();

        int validTriangles = 0;
        float radiusErrorSum = 0f;
        int radiusSamples = 0;

        for (int i = 0; i <= triangles.Length - 3; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                continue;

            validTriangles++;

            Vector3 a = vertices[i0];
            Vector3 b = vertices[i1];
            Vector3 c = vertices[i2];
            float areaSq4 = Vector3.Cross(b - a, c - a).sqrMagnitude;
            if (areaSq4 <= 0.0000000001f)
                stats.DegenerateTriangles++;

            IncrementEdgeCount(edgeUse, i0, i1);
            IncrementEdgeCount(edgeUse, i1, i2);
            IncrementEdgeCount(edgeUse, i2, i0);
        }

        foreach (KeyValuePair<ulong, int> kv in edgeUse)
        {
            if (kv.Value == 1)
            {
                stats.BoundaryEdges++;
                DecodeEdge(kv.Key, out int ea, out int eb);
                openVertices.Add(ea);
                openVertices.Add(eb);
            }
            else if (kv.Value > 2)
            {
                stats.NonManifoldEdges++;
            }
        }

        stats.OpenEdgeVertices = openVertices.Count;

        if (expectedRadius > 0f && meshTransform != null)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = meshTransform.TransformPoint(vertices[i]);
                float radius = Vector3.Distance(world, planetCenter);
                float error = Mathf.Abs(radius - expectedRadius);
                radiusErrorSum += error;
                stats.RadiusErrorMaxMeters = Mathf.Max(stats.RadiusErrorMaxMeters, error);
                radiusSamples++;
            }
        }

        if (radiusSamples > 0)
            stats.RadiusErrorAvgMeters = radiusErrorSum / radiusSamples;

        return validTriangles > 0;
    }

    static void IncrementEdgeCount(Dictionary<ulong, int> edgeUse, int a, int b)
    {
        ulong key = EncodeEdge(a, b);
        edgeUse.TryGetValue(key, out int count);
        edgeUse[key] = count + 1;
    }

    static ulong EncodeEdge(int a, int b)
    {
        uint min = (uint)Mathf.Min(a, b);
        uint max = (uint)Mathf.Max(a, b);
        return ((ulong)min << 32) | max;
    }

    static void DecodeEdge(ulong key, out int a, out int b)
    {
        a = (int)(key >> 32);
        b = (int)(key & 0xFFFFFFFFu);
    }

    static float FocusMotionMask(float depth, float shore, float body)
    {
        float depthRelease = SmoothStep(0.02f, 0.18f, depth);
        float shoreRelease = SmoothStep(0.02f, 0.18f, shore) * 0.58f;
        return body * Mathf.Clamp01(Mathf.Max(depthRelease, shoreRelease));
    }

    static float FocusNormalMask(float depth, float shore, float body)
    {
        float depthRelease = SmoothStep(0.012f, 0.10f, depth);
        float shoreRelease = SmoothStep(0.012f, 0.10f, shore) * 0.72f;
        return body * Mathf.Clamp01(Mathf.Max(depthRelease, shoreRelease));
    }

    static float SmoothStep(float edge0, float edge1, float value)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, value));
    }
}
