using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class WaterMeshBuilder
{
    public struct Settings
    {
        public float PlanetRadius;
        public float OceanLevel;
        public float DeepDepth;
        public float ShoreRange;
        public float SurfaceOffset;
        public int OceanBodyVertexThreshold;
    }

    public struct BuildStats
    {
        public int WetVertices;
        public int MeshVertices;
        public int Triangles;
        public int VolumeLipVertices;
        public int VolumeLipTriangles;
        public int OceanBodies;
        public int SmallBodies;
        public float MaxDepth;
    }

    struct WaterPoint
    {
        public bool IsOriginal;
        public int OriginalIndex;
        public int EdgeA;
        public int EdgeB;
        public Vector3 Direction;
        public Vector3 VolumeLipDirection;
        public float BodyFactor;
    }

    struct FaceWaterData
    {
        public bool[] Wet;
        public int[] ShoreDistanceCells;
        public float[] BodyFactor;
        public int[] GlobalIndices;
    }

    sealed class GlobalWaterData
    {
        public FaceWaterData[] Faces;
        public float[] DepthMeters;
    }

    struct DirectionKey : System.IEquatable<DirectionKey>
    {
        const float Scale = 1000000f;

        readonly int _x;
        readonly int _y;
        readonly int _z;

        public DirectionKey(Vector3 direction)
        {
            _x = Mathf.RoundToInt(direction.x * Scale);
            _y = Mathf.RoundToInt(direction.y * Scale);
            _z = Mathf.RoundToInt(direction.z * Scale);
        }

        public bool Equals(DirectionKey other)
        {
            return _x == other._x && _y == other._y && _z == other._z;
        }

        public override bool Equals(object obj)
        {
            return obj is DirectionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _x;
                hash = hash * 31 + _y;
                hash = hash * 31 + _z;
                return hash;
            }
        }
    }

    public static BuildStats Build(Mesh mesh, TerrainFace[] faces, Settings settings)
    {
        return Build(mesh, null, faces, settings);
    }

    public static BuildStats Build(Mesh mesh, Mesh volumeLipMesh, TerrainFace[] faces, Settings settings)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        var volumeLipVertices = volumeLipMesh != null ? new List<Vector3>() : null;
        var volumeLipNormals = volumeLipMesh != null ? new List<Vector3>() : null;
        var volumeLipColors = volumeLipMesh != null ? new List<Color>() : null;
        var volumeLipTriangles = volumeLipMesh != null ? new List<int>() : null;
        BuildStats stats = default;

        if (mesh == null || faces == null || faces.Length == 0)
        {
            mesh?.Clear();
            volumeLipMesh?.Clear();
            return stats;
        }

        float waterRadius = settings.PlanetRadius * (1f + settings.OceanLevel) + settings.SurfaceOffset;
        float deepDepth = Mathf.Max(settings.DeepDepth, 0.001f);
        float shoreRange = Mathf.Max(settings.ShoreRange, 0.001f);
        GlobalWaterData waterData = BuildGlobalWaterData(faces, settings, ref stats);
        var originalVertexCache = new Dictionary<int, int>();
        var edgeVertexCache = new Dictionary<ulong, int>();
        var volumeLipInnerVertexCache = new Dictionary<ulong, int>();
        var volumeLipOuterVertexCache = new Dictionary<ulong, int>();

        for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            TerrainFace face = faces[faceIndex];
            if (face?.UnitSpherePoints == null || face.Elevations == null)
                continue;

            ProcessFace(
                face,
                waterData.Faces[faceIndex],
                waterData.DepthMeters,
                settings,
                waterRadius,
                deepDepth,
                shoreRange,
                originalVertexCache,
                edgeVertexCache,
                volumeLipInnerVertexCache,
                volumeLipOuterVertexCache,
                vertices,
                normals,
                colors,
                triangles,
                volumeLipVertices,
                volumeLipNormals,
                volumeLipColors,
                volumeLipTriangles,
                ref stats);
        }

        mesh.Clear();
        mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();

        if (volumeLipMesh != null)
        {
            volumeLipMesh.Clear();
            volumeLipMesh.indexFormat = volumeLipVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            volumeLipMesh.SetVertices(volumeLipVertices);
            volumeLipMesh.SetNormals(volumeLipNormals);
            volumeLipMesh.SetColors(volumeLipColors);
            volumeLipMesh.SetTriangles(volumeLipTriangles, 0, true);
            volumeLipMesh.RecalculateBounds();
        }

        return stats;
    }

    static void ProcessFace(
        TerrainFace face,
        FaceWaterData faceData,
        float[] globalDepthMeters,
        Settings settings,
        float waterRadius,
        float deepDepth,
        float shoreRange,
        Dictionary<int, int> originalVertexCache,
        Dictionary<ulong, int> edgeVertexCache,
        Dictionary<ulong, int> volumeLipInnerVertexCache,
        Dictionary<ulong, int> volumeLipOuterVertexCache,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Color> colors,
        List<int> triangles,
        List<Vector3> volumeLipVertices,
        List<Vector3> volumeLipNormals,
        List<Color> volumeLipColors,
        List<int> volumeLipTriangles,
        ref BuildStats stats)
    {
        int resolution = face.Resolution;
        Vector3[] directions = face.UnitSpherePoints;
        float[] elevations = face.Elevations;
        int vertexCount = directions.Length;

        bool[] wet = faceData.Wet;
        int[] shoreDistanceCells = faceData.ShoreDistanceCells;
        float[] bodyFactor = faceData.BodyFactor;
        int[] globalIndices = faceData.GlobalIndices;
        if (wet == null || shoreDistanceCells == null || bodyFactor == null || globalIndices == null)
            return;

        var clipped = new WaterPoint[4];
        float cellWorldSize = settings.PlanetRadius * Mathf.PI * 0.5f / Mathf.Max(resolution - 1, 1);
        float shorelineOverlapMeters = Mathf.Clamp(shoreRange * 0.22f, settings.PlanetRadius * 0.0012f, settings.PlanetRadius * 0.0075f);
        float volumeLipMeters = Mathf.Clamp(shoreRange * 0.55f, shorelineOverlapMeters * 1.65f, settings.PlanetRadius * 0.012f);
        float shorelineEdgeDepth = Mathf.Clamp(shorelineOverlapMeters * 0.30f, settings.PlanetRadius * 0.00015f, deepDepth * 0.06f);
        float volumeLipDepth = Mathf.Clamp(volumeLipMeters * 0.24f, shorelineEdgeDepth, deepDepth * 0.08f);
        float shorelineEdgeShore = Mathf.Clamp01(shorelineOverlapMeters * 0.45f / shoreRange);
        int addedMeshVertices = 0;
        int addedTriangles = 0;
        int addedVolumeLipVertices = 0;
        int addedVolumeLipTriangles = 0;

        for (int y = 0; y < resolution - 1; y++)
        {
            for (int x = 0; x < resolution - 1; x++)
            {
                int i00 = x + y * resolution;
                int i10 = i00 + 1;
                int i01 = i00 + resolution;
                int i11 = i01 + 1;

                AddClippedTriangle(i00, i11, i01);
                AddClippedTriangle(i00, i10, i11);
            }
        }

        stats.MeshVertices += addedMeshVertices;
        stats.Triangles += addedTriangles;
        stats.VolumeLipVertices += addedVolumeLipVertices;
        stats.VolumeLipTriangles += addedVolumeLipTriangles;

        void AddClippedTriangle(int i0, int i1, int i2)
        {
            int count = 0;
            ClipEdge(i2, i0, clipped, ref count);
            ClipEdge(i0, i1, clipped, ref count);
            ClipEdge(i1, i2, clipped, ref count);

            if (count < 3)
                return;

            int first = GetOrAddPoint(clipped[0]);
            for (int i = 1; i < count - 1; i++)
            {
                triangles.Add(first);
                triangles.Add(GetOrAddPoint(clipped[i]));
                triangles.Add(GetOrAddPoint(clipped[i + 1]));
                addedTriangles++;
            }

            AddVolumeLipSegment(clipped, count);
        }

        void ClipEdge(int previous, int current, WaterPoint[] output, ref int count)
        {
            bool previousWet = wet[previous];
            bool currentWet = wet[current];

            if (currentWet)
            {
                if (!previousWet)
                    output[count++] = CreateIntersection(previous, current);

                output[count++] = CreateOriginal(current);
            }
            else if (previousWet)
            {
                output[count++] = CreateIntersection(previous, current);
            }
        }

        WaterPoint CreateOriginal(int index)
        {
            return new WaterPoint
            {
                IsOriginal = true,
                OriginalIndex = index,
                Direction = directions[index],
                BodyFactor = bodyFactor[index]
            };
        }

        WaterPoint CreateIntersection(int a, int b)
        {
            float t = Mathf.InverseLerp(elevations[a], elevations[b], settings.OceanLevel);
            bool aWet = wet[a];
            bool bWet = wet[b];
            float edgeAngleRadians = Vector3.Angle(directions[a], directions[b]) * Mathf.Deg2Rad;
            float edgeWorldLength = Mathf.Max(edgeAngleRadians * settings.PlanetRadius, cellWorldSize * 0.25f);
            float overlapT = Mathf.Clamp01(shorelineOverlapMeters / edgeWorldLength);

            if (aWet && !bWet)
                t += overlapT;
            else if (!aWet && bWet)
                t -= overlapT;

            Vector3 direction = Vector3.Lerp(directions[a], directions[b], Mathf.Clamp01(t)).normalized;
            float volumeLipT = shorelineOverlapMeters > 0.0f
                ? t + (t - Mathf.InverseLerp(elevations[a], elevations[b], settings.OceanLevel)) * ((volumeLipMeters - shorelineOverlapMeters) / shorelineOverlapMeters)
                : t;
            Vector3 volumeLipDirection = Vector3.Lerp(directions[a], directions[b], Mathf.Clamp01(volumeLipT)).normalized;
            return new WaterPoint
            {
                IsOriginal = false,
                EdgeA = a,
                EdgeB = b,
                Direction = direction,
                VolumeLipDirection = volumeLipDirection,
                BodyFactor = Mathf.Max(bodyFactor[a], bodyFactor[b])
            };
        }

        int GetOrAddPoint(WaterPoint point)
        {
            if (point.IsOriginal)
            {
                int globalIndex = globalIndices[point.OriginalIndex];
                if (originalVertexCache.TryGetValue(globalIndex, out int cached))
                    return cached;

                float depth = globalIndex >= 0 && globalIndex < globalDepthMeters.Length
                    ? globalDepthMeters[globalIndex]
                    : Mathf.Max(0f, (settings.OceanLevel - elevations[point.OriginalIndex]) * settings.PlanetRadius);
                float shore = shoreDistanceCells[point.OriginalIndex] == int.MaxValue
                    ? 1f
                    : Mathf.Clamp01(shoreDistanceCells[point.OriginalIndex] * cellWorldSize / shoreRange);

                int vertexIndex = AddVertex(point.Direction, depth, shore, point.BodyFactor);
                originalVertexCache.Add(globalIndex, vertexIndex);
                return vertexIndex;
            }

            int globalA = globalIndices[point.EdgeA];
            int globalB = globalIndices[point.EdgeB];
            ulong edgeKey = MakeEdgeKey(globalA, globalB);
            if (edgeVertexCache.TryGetValue(edgeKey, out int edgeVertex))
                return edgeVertex;

            edgeVertex = AddVertex(point.Direction, shorelineEdgeDepth, shorelineEdgeShore, point.BodyFactor);
            edgeVertexCache.Add(edgeKey, edgeVertex);
            return edgeVertex;
        }

        void AddVolumeLipSegment(WaterPoint[] points, int count)
        {
            if (volumeLipTriangles == null)
                return;

            int firstEdgePoint = -1;
            int secondEdgePoint = -1;
            for (int i = 0; i < count; i++)
            {
                if (points[i].IsOriginal)
                    continue;

                if (firstEdgePoint < 0)
                    firstEdgePoint = i;
                else
                    secondEdgePoint = i;
            }

            if (firstEdgePoint < 0 || secondEdgePoint < 0)
                return;

            int innerA = GetOrAddVolumeLipPoint(points[firstEdgePoint], false);
            int outerA = GetOrAddVolumeLipPoint(points[firstEdgePoint], true);
            int innerB = GetOrAddVolumeLipPoint(points[secondEdgePoint], false);
            int outerB = GetOrAddVolumeLipPoint(points[secondEdgePoint], true);

            if (innerA < 0 || outerA < 0 || innerB < 0 || outerB < 0)
                return;

            volumeLipTriangles.Add(innerA);
            volumeLipTriangles.Add(outerA);
            volumeLipTriangles.Add(outerB);
            volumeLipTriangles.Add(innerA);
            volumeLipTriangles.Add(outerB);
            volumeLipTriangles.Add(innerB);
            addedVolumeLipTriangles += 2;
        }

        int GetOrAddVolumeLipPoint(WaterPoint point, bool outer)
        {
            int globalA = globalIndices[point.EdgeA];
            int globalB = globalIndices[point.EdgeB];
            ulong edgeKey = MakeEdgeKey(globalA, globalB);
            Dictionary<ulong, int> cache = outer ? volumeLipOuterVertexCache : volumeLipInnerVertexCache;
            if (cache.TryGetValue(edgeKey, out int cached))
                return cached;

            Vector3 direction = outer ? point.VolumeLipDirection : point.Direction;
            int vertexIndex = AddVolumeLipVertex(direction, volumeLipDepth, shorelineEdgeShore, point.BodyFactor);
            cache.Add(edgeKey, vertexIndex);
            return vertexIndex;
        }

        int AddVertex(Vector3 direction, float depth, float shore, float oceanFactor)
        {
            int vertexIndex = vertices.Count;
            vertices.Add(direction * waterRadius);
            normals.Add(direction);
            colors.Add(new Color(
                Mathf.Clamp01(depth / deepDepth),
                shore,
                Mathf.Clamp01(oceanFactor),
                1f));
            addedMeshVertices++;
            return vertexIndex;
        }

        int AddVolumeLipVertex(Vector3 direction, float depth, float shore, float oceanFactor)
        {
            if (volumeLipVertices == null)
                return -1;

            int vertexIndex = volumeLipVertices.Count;
            volumeLipVertices.Add(direction * waterRadius);
            volumeLipNormals.Add(direction);
            volumeLipColors.Add(new Color(
                Mathf.Clamp01(depth / deepDepth),
                shore,
                Mathf.Clamp01(oceanFactor),
                1f));
            addedVolumeLipVertices++;
            return vertexIndex;
        }
    }

    static GlobalWaterData BuildGlobalWaterData(TerrainFace[] faces, Settings settings, ref BuildStats stats)
    {
        var result = new GlobalWaterData { Faces = new FaceWaterData[faces.Length] };
        var globalIndicesByDirection = new Dictionary<DirectionKey, int>();
        var globalWet = new List<bool>();
        var globalDepthMeters = new List<float>();

        for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            TerrainFace face = faces[faceIndex];
            if (face?.UnitSpherePoints == null || face.Elevations == null)
                continue;

            Vector3[] directions = face.UnitSpherePoints;
            float[] elevations = face.Elevations;
            int vertexCount = Mathf.Min(directions.Length, elevations.Length);
            var faceData = new FaceWaterData
            {
                Wet = new bool[vertexCount],
                ShoreDistanceCells = new int[vertexCount],
                BodyFactor = new float[vertexCount],
                GlobalIndices = new int[vertexCount]
            };

            for (int i = 0; i < vertexCount; i++)
            {
                var key = new DirectionKey(directions[i]);
                if (!globalIndicesByDirection.TryGetValue(key, out int globalIndex))
                {
                    globalIndex = globalWet.Count;
                    globalIndicesByDirection.Add(key, globalIndex);
                    globalWet.Add(false);
                    globalDepthMeters.Add(0f);
                }

                bool isWet = elevations[i] < settings.OceanLevel;
                float depth = Mathf.Max(0f, (settings.OceanLevel - elevations[i]) * settings.PlanetRadius);
                faceData.GlobalIndices[i] = globalIndex;
                faceData.Wet[i] = isWet;
                faceData.ShoreDistanceCells[i] = int.MaxValue;

                if (!isWet)
                    continue;

                stats.WetVertices++;
                stats.MaxDepth = Mathf.Max(stats.MaxDepth, depth);
                globalWet[globalIndex] = true;
                if (depth > globalDepthMeters[globalIndex])
                    globalDepthMeters[globalIndex] = depth;
            }

            result.Faces[faceIndex] = faceData;
        }

        bool[] wet = globalWet.ToArray();
        var adjacency = BuildGlobalAdjacency(faces, result.Faces, wet.Length);
        var globalBodyFactor = new float[wet.Length];
        var globalShoreDistance = new int[wet.Length];
        for (int i = 0; i < globalShoreDistance.Length; i++)
            globalShoreDistance[i] = int.MaxValue;

        ClassifyWaterBodies(wet, adjacency, settings.OceanBodyVertexThreshold, globalBodyFactor, ref stats);
        ComputeShoreDistance(wet, adjacency, globalShoreDistance);

        for (int faceIndex = 0; faceIndex < result.Faces.Length; faceIndex++)
        {
            FaceWaterData faceData = result.Faces[faceIndex];
            if (faceData.GlobalIndices == null)
                continue;

            for (int i = 0; i < faceData.GlobalIndices.Length; i++)
            {
                int globalIndex = faceData.GlobalIndices[i];
                faceData.BodyFactor[i] = globalBodyFactor[globalIndex];
                faceData.ShoreDistanceCells[i] = globalShoreDistance[globalIndex];
            }

            result.Faces[faceIndex] = faceData;
        }

        result.DepthMeters = globalDepthMeters.ToArray();
        return result;
    }

    static List<int>[] BuildGlobalAdjacency(TerrainFace[] faces, FaceWaterData[] faceData, int globalVertexCount)
    {
        var adjacency = new List<int>[globalVertexCount];

        for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            TerrainFace face = faces[faceIndex];
            int[] globalIndices = faceData[faceIndex].GlobalIndices;
            if (face == null || globalIndices == null)
                continue;

            int resolution = face.Resolution;
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int index = x + y * resolution;
                    if (index >= globalIndices.Length)
                        continue;

                    if (x < resolution - 1)
                        AddEdge(globalIndices[index], globalIndices[index + 1]);
                    if (y < resolution - 1)
                        AddEdge(globalIndices[index], globalIndices[index + resolution]);
                }
            }
        }

        return adjacency;

        void AddEdge(int a, int b)
        {
            if (a == b)
                return;

            // No HashSet dedup: seam vertices shared across faces may produce duplicate adjacency
            // entries, but all BFS callers handle duplicates correctly via visited/distance checks.
            if (adjacency[a] == null)
                adjacency[a] = new List<int>(6);
            if (adjacency[b] == null)
                adjacency[b] = new List<int>(6);

            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }
    }

    static ulong MakeEdgeKey(int a, int b)
    {
        uint min = (uint)Mathf.Min(a, b);
        uint max = (uint)Mathf.Max(a, b);
        return ((ulong)min << 32) | max;
    }

    static void ClassifyWaterBodies(bool[] wet, List<int>[] adjacency, int oceanBodyVertexThreshold, float[] bodyFactor, ref BuildStats stats)
    {
        int count = wet.Length;
        var visited = new bool[count];
        var queue = new int[count];
        var component = new List<int>(count);
        int largeBodyThreshold = Mathf.Max(24, oceanBodyVertexThreshold);

        for (int i = 0; i < count; i++)
        {
            if (!wet[i] || visited[i])
                continue;

            component.Clear();
            int head = 0;
            int tail = 0;
            visited[i] = true;
            queue[tail++] = i;

            while (head < tail)
            {
                int current = queue[head++];
                component.Add(current);
                List<int> neighbors = adjacency[current];
                if (neighbors == null)
                    continue;

                for (int n = 0; n < neighbors.Count; n++)
                    EnqueueWetNeighbor(neighbors[n]);
            }

            float factor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(largeBodyThreshold * 0.25f, largeBodyThreshold, component.Count));
            if (factor >= 0.65f)
                stats.OceanBodies++;
            else
                stats.SmallBodies++;

            for (int c = 0; c < component.Count; c++)
                bodyFactor[component[c]] = factor;

            void EnqueueWetNeighbor(int neighbor)
            {
                if (!wet[neighbor] || visited[neighbor])
                    return;

                visited[neighbor] = true;
                queue[tail++] = neighbor;
            }
        }
    }

    static void ComputeShoreDistance(bool[] wet, List<int>[] adjacency, int[] distance)
    {
        int count = wet.Length;
        var queue = new int[count];
        int head = 0;
        int tail = 0;

        for (int i = 0; i < count; i++)
        {
            if (!wet[i] || !HasDryNeighbor(i))
                continue;

            distance[i] = 0;
            queue[tail++] = i;
        }

        while (head < tail)
        {
            int current = queue[head++];
            int nextDistance = distance[current] + 1;
            List<int> neighbors = adjacency[current];
            if (neighbors == null)
                continue;

            for (int n = 0; n < neighbors.Count; n++)
                TryVisit(neighbors[n], nextDistance);
        }

        bool HasDryNeighbor(int index)
        {
            List<int> neighbors = adjacency[index];
            if (neighbors == null)
                return false;

            for (int n = 0; n < neighbors.Count; n++)
            {
                if (!wet[neighbors[n]])
                    return true;
            }

            return false;
        }

        void TryVisit(int neighbor, int nextDistance)
        {
            if (!wet[neighbor] || nextDistance >= distance[neighbor])
                return;

            distance[neighbor] = nextDistance;
            queue[tail++] = neighbor;
        }
    }
}
