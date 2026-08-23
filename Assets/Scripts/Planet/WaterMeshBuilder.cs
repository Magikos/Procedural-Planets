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
        public IClimateProvider ClimateProvider;
        public bool EnableFreezing;
        public float LakeFreezeStartTemperature01;
        public float LakeFreezeCompleteTemperature01;
        public float OceanFreezeStartTemperature01;
        public float OceanFreezeCompleteTemperature01;

        // Per-basin water surface heights from the spill solve. Null falls back to OceanLevel everywhere,
        // which is the single-shell behaviour this replaced.
        public WaterBodyMap Levels;
    }

    public struct BuildStats
    {
        public int WetVertices;
        public int MeshVertices;
        public int Triangles;
        public int OceanBodies;
        public int SmallBodies;
        public int FrozenBodies;
        public int PartiallyFrozenBodies;
        public int LiquidBodies;
        public float MaxDepth;
        public float MinWaterTemperature01;
        public float MaxWaterTemperature01;
        public float AverageWaterTemperature01;
        // Vertices whose body factor is neither lake nor ocean. The volume prepass packs shore01 and
        // body01 into one channel (shore01 * 0.45 + body01 * 0.55), which only decodes unambiguously
        // while body01 stays near 0 or 1 - see docs/design/2026-08-18-water-data-contract.md.
        public int AmbiguousBodyVertices;
    }

    /// <summary>Pre-computed water mesh data. Safe to produce on a background thread via <see cref="Compute"/>.</summary>
    public struct MeshData
    {
        public List<Vector3> Vertices;
        public List<Vector3> Normals;
        public List<Color> Colors;
        public List<int> Triangles;
        public BuildStats Stats;
    }

    struct WaterPoint
    {
        public bool IsOriginal;
        public int OriginalIndex;
        public int EdgeA;
        public int EdgeB;
        public Vector3 Direction;
        public float BodyFactor;
        public float Temperature01;
        // Height this point sits at. Carried rather than re-derived: a clip point is placed by the level of
        // the body it was clipped AGAINST, and looking it up again from the point own direction can land in
        // a neighbouring cell belonging to a different body at a different height.
        public float SurfaceLevel;
    }

    struct FaceWaterData
    {
        public bool[] Wet;
        public int[] ShoreDistanceCells;
        public float[] BodyFactor;
        public float[] Temperature01;
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

    /// <summary>
    /// Computes water mesh data without touching any Unity Mesh API.
    /// Safe to call from a background thread. Pass the result to <see cref="Apply"/>.
    /// </summary>
    public static MeshData Compute(IFaceMeshSampler[] faces, Settings settings, System.Action<float> onProgress = null)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        BuildStats stats = default;

        if (faces != null && faces.Length > 0)
        {
            float deepDepth = Mathf.Max(settings.DeepDepth, 0.001f);
            float shoreRange = Mathf.Max(settings.ShoreRange, 0.001f);
            GlobalWaterData waterData = BuildGlobalWaterData(faces, settings, ref stats);
            onProgress?.Invoke(0.45f); // global water graph + classification done — the heaviest phase
            var originalVertexCache = new Dictionary<int, int>();
            var edgeVertexCache = new Dictionary<ulong, int>();

            for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
            {
                IFaceMeshSampler face = faces[faceIndex];
                if (face?.UnitSpherePoints == null || face.Elevations == null)
                    continue;

                ProcessFace(
                    face,
                    waterData.Faces[faceIndex],
                    waterData.DepthMeters,
                    settings,
                    deepDepth,
                    shoreRange,
                    originalVertexCache,
                    edgeVertexCache,
                    vertices,
                    normals,
                    colors,
                    triangles,
                    ref stats);

                onProgress?.Invoke(0.45f + 0.55f * (faceIndex + 1) / faces.Length);
            }
        }

        foreach (Color c in colors)
            if (c.b > 0.05f && c.b < 0.95f) stats.AmbiguousBodyVertices++;

        onProgress?.Invoke(1f);
        return new MeshData
        {
            Vertices = vertices,
            Normals = normals,
            Colors = colors,
            Triangles = triangles,
            Stats = stats
        };
    }

    /// <summary>
    /// Applies pre-computed mesh data to Unity Mesh objects. Must be called on the main thread.
    /// </summary>
    public static void Apply(Mesh mesh, MeshData data)
    {
        if (mesh == null) return;

        mesh.Clear();
        mesh.indexFormat = data.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(data.Vertices);
        mesh.SetNormals(data.Normals);
        mesh.SetColors(data.Colors);
        mesh.SetTriangles(data.Triangles, 0, true);
        mesh.RecalculateBounds();
    }

    public static BuildStats Build(Mesh mesh, IFaceMeshSampler[] faces, Settings settings)
    {
        var data = Compute(faces, settings);
        Apply(mesh, data);
        return data.Stats;
    }

    static void ProcessFace(
        IFaceMeshSampler face,
        FaceWaterData faceData,
        float[] globalDepthMeters,
        Settings settings,
        float deepDepth,
        float shoreRange,
        Dictionary<int, int> originalVertexCache,
        Dictionary<ulong, int> edgeVertexCache,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Color> colors,
        List<int> triangles,
        ref BuildStats stats)
    {
        int resolution = face.Resolution;
        Vector3[] directions = face.UnitSpherePoints;
        float[] elevations = face.Elevations;
        int vertexCount = directions.Length;

        bool[] wet = faceData.Wet;
        int[] shoreDistanceCells = faceData.ShoreDistanceCells;
        float[] bodyFactor = faceData.BodyFactor;
        float[] temperature01 = faceData.Temperature01;
        int[] globalIndices = faceData.GlobalIndices;
        if (wet == null || shoreDistanceCells == null || bodyFactor == null || temperature01 == null || globalIndices == null)
            return;

        var clipped = new WaterPoint[4];
        float cellWorldSize = settings.PlanetRadius * Mathf.PI * 0.5f / Mathf.Max(resolution - 1, 1);
        // The mesh overlaps the waterline slightly so no gap can open between water and land. Those inland
        // vertices carry ZERO depth and zero shore, because that is what the water is there - nothing. They
        // used to claim 8 m of depth, which made the overlap fully opaque: the tint reached full strength
        // the instant the mesh started, and wherever the overlap was not buried by rising ground it read as
        // a solid sheet of water lying on the grass.
        const float shorelineEdgeDepth = 0f;
        const float shorelineEdgeShore = 0f;
        int addedMeshVertices = 0;
        int addedTriangles = 0;

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

        float LevelAtDirection(Vector3 dir) => settings.Levels != null
            ? settings.Levels.LevelAt(dir, settings.OceanLevel)
            : settings.OceanLevel;

        WaterPoint CreateOriginal(int index)
        {
            return new WaterPoint
            {
                IsOriginal = true,
                OriginalIndex = index,
                Direction = directions[index],
                BodyFactor = bodyFactor[index],
                Temperature01 = temperature01[index],
                SurfaceLevel = LevelAtDirection(directions[index])
            };
        }

        WaterPoint CreateIntersection(int a, int b)
        {
            bool aWet = wet[a];
            bool bWet = wet[b];
            // Clip against the level of whichever end is under water; the dry end belongs to no body, and its
            // level reads back as ground height, which would put the shoreline in the wrong place.
            Vector3 wetDirection = aWet ? directions[a] : directions[b];
            float clipLevel = LevelAtDirection(wetDirection);
            float t = Mathf.InverseLerp(elevations[a], elevations[b], clipLevel);
            // t is where the ground actually crosses the water level along this edge, so the outline it
            // traces is a real shoreline rather than a grid. The overlap must therefore be a SMALL fraction
            // of an edge: push it far and t saturates at Clamp01, the vertex snaps onto the dry grid corner,
            // and the whole outline collapses into an axis-aligned staircase of cell-sized squares.
            //
            // It used to be a distance, ~27.5 m against a ~41 m edge - 0.67 of an edge - so every crossing
            // past t = 0.33 clamped. That was almost all of them, and it is what put the square steps around
            // every lake. Any overhang that survives is trimmed per pixel against the depth buffer in
            // Ocean.shader, so this only has to be big enough to close the seam, not to hide anything.
            const float ShorelineOverlapEdgeFraction = 0.08f;

            if (aWet && !bWet)
                t += ShorelineOverlapEdgeFraction;
            else if (!aWet && bWet)
                t -= ShorelineOverlapEdgeFraction;

            Vector3 direction = Vector3.Lerp(directions[a], directions[b], Mathf.Clamp01(t)).normalized;
            return new WaterPoint
            {
                IsOriginal = false,
                EdgeA = a,
                EdgeB = b,
                Direction = direction,
                BodyFactor = Mathf.Max(bodyFactor[a], bodyFactor[b]),
                Temperature01 = aWet ? temperature01[a] : temperature01[b],
                SurfaceLevel = clipLevel
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
                    : Mathf.Max(0f, ((settings.Levels != null
                            ? settings.Levels.LevelAt(point.Direction, settings.OceanLevel)
                            : settings.OceanLevel)
                        - elevations[point.OriginalIndex]) * settings.PlanetRadius);
                float shore = shoreDistanceCells[point.OriginalIndex] == int.MaxValue
                    ? 1f
                    : Mathf.Clamp01(shoreDistanceCells[point.OriginalIndex] * cellWorldSize / shoreRange);

                int vertexIndex = AddVertex(point.Direction, depth, shore, point.BodyFactor, point.Temperature01, point.SurfaceLevel);
                originalVertexCache.Add(globalIndex, vertexIndex);
                return vertexIndex;
            }

            int globalA = globalIndices[point.EdgeA];
            int globalB = globalIndices[point.EdgeB];
            ulong edgeKey = MakeEdgeKey(globalA, globalB);
            if (edgeVertexCache.TryGetValue(edgeKey, out int edgeVertex))
                return edgeVertex;

            edgeVertex = AddVertex(point.Direction, shorelineEdgeDepth, shorelineEdgeShore, point.BodyFactor, point.Temperature01, point.SurfaceLevel);
            edgeVertexCache.Add(edgeKey, edgeVertex);
            return edgeVertex;
        }

        // level is the point own SurfaceLevel, carried from where it was decided. Within a basin every cell
        // shares one spill height so a lake stays flat; a clip point keeps the height of the body it was
        // clipped against instead of whatever cell its final direction happens to fall in.
        int AddVertex(Vector3 direction, float depth, float shore, float oceanFactor, float waterTemperature01, float level)
        {
            int vertexIndex = vertices.Count;
            vertices.Add(direction * (settings.PlanetRadius * (1f + level) + settings.SurfaceOffset));
            normals.Add(direction);
            colors.Add(new Color(
                Mathf.Clamp01(depth / deepDepth),
                shore,
                Mathf.Clamp01(oceanFactor),
                Mathf.Clamp01(waterTemperature01)));
            addedMeshVertices++;
            return vertexIndex;
        }
    }

    static GlobalWaterData BuildGlobalWaterData(IFaceMeshSampler[] faces, Settings settings, ref BuildStats stats)
    {
        var result = new GlobalWaterData { Faces = new FaceWaterData[faces.Length] };
        var globalIndicesByDirection = new Dictionary<DirectionKey, int>();
        var globalWet = new List<bool>();
        var globalDepthMeters = new List<float>();
        var globalDirections = new List<Vector3>();
        var globalTemperature01 = new List<float>();
        var globalTemperatureSampled = new List<bool>();

        for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            IFaceMeshSampler face = faces[faceIndex];
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
                Temperature01 = new float[vertexCount],
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
                    globalDirections.Add(directions[i]);
                    globalTemperature01.Add(0.5f);
                    globalTemperatureSampled.Add(false);
                }

                // The spill solve gives every direction the height water would stand at, and sets it equal to
                // the ground wherever water would drain away. So this one test finds the ocean, lakes below
                // sea level, and basins perched above it, without a special case for any of them.
                //
                // Only where the solve actually put water, though. A level the dilation ring merely carried
                // onto dry land describes where the waterline is, and CreateIntersection reads it from the
                // wet end for exactly that - but a carried height cannot decide that the cell it sits on is
                // itself submerged. Letting it try floods any dip finer than the 41 m grid, and each such
                // dip became a lone 41 m sheet of water lying on the grass beside a lake.
                float waterLevel;
                bool isWet;
                if (settings.Levels != null && settings.Levels.HasSolvedLevels)
                {
                    isWet = settings.Levels.TrySolvedLevelAt(directions[i], out waterLevel)
                            && elevations[i] < waterLevel;
                }
                else
                {
                    waterLevel = settings.OceanLevel;
                    isWet = elevations[i] < waterLevel;
                }
                float depth = isWet ? (waterLevel - elevations[i]) * settings.PlanetRadius : 0f;
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
                if (!globalTemperatureSampled[globalIndex] && settings.ClimateProvider != null)
                {
                    globalTemperature01[globalIndex] = settings.ClimateProvider
                        .Evaluate(globalDirections[globalIndex], settings.OceanLevel)
                        .Temperature01;
                    globalTemperatureSampled[globalIndex] = true;
                }
            }

            result.Faces[faceIndex] = faceData;
        }

        bool[] wet = globalWet.ToArray();
        var adjacency = BuildGlobalAdjacency(faces, result.Faces, wet.Length);
        var globalBodyFactor = new float[wet.Length];
        var globalEffectiveTemperature01 = globalTemperature01.ToArray();
        var globalShoreDistance = new int[wet.Length];
        for (int i = 0; i < globalShoreDistance.Length; i++)
            globalShoreDistance[i] = int.MaxValue;

        ClassifyWaterBodies(
            wet,
            adjacency,
            settings,
            globalBodyFactor,
            globalEffectiveTemperature01,
            ref stats);

        // Unify with the biome/scatter lake authority: force any vertex WaterBodyMap tags as lake water to
        // bodyFactor 0, so a lake the biome map treats as a lake also RENDERS as a lake (murky green, still,
        // lake freeze schedule) instead of blue ocean. WaterBodyMap is built earlier in gen (Planet.cs), so it is
        // available here; null => keep the mesh's own size-based classification.
        if (WaterBodyMap.Current != null)
            for (int i = 0; i < globalBodyFactor.Length; i++)
                if (globalWet[i] && WaterBodyMap.Current.Sample(globalDirections[i]) == WaterBodyMap.Water)
                    globalBodyFactor[i] = 0f;

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
                faceData.Temperature01[i] = globalEffectiveTemperature01[globalIndex];
                faceData.ShoreDistanceCells[i] = globalShoreDistance[globalIndex];
            }

            result.Faces[faceIndex] = faceData;
        }

        result.DepthMeters = globalDepthMeters.ToArray();
        return result;
    }

    static List<int>[] BuildGlobalAdjacency(IFaceMeshSampler[] faces, FaceWaterData[] faceData, int globalVertexCount)
    {
        var adjacency = new List<int>[globalVertexCount];

        for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            IFaceMeshSampler face = faces[faceIndex];
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

    static void ClassifyWaterBodies(
        bool[] wet,
        List<int>[] adjacency,
        Settings settings,
        float[] bodyFactor,
        float[] temperature01,
        ref BuildStats stats)
    {
        int count = wet.Length;
        var visited = new bool[count];
        var queue = new int[count];
        var component = new List<int>(count);
        int largeBodyThreshold = Mathf.Max(24, settings.OceanBodyVertexThreshold);
        float temperatureSum = 0f;
        int temperatureCount = 0;
        stats.MinWaterTemperature01 = 1f;

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

            float componentTemperature = 0f;
            for (int c = 0; c < component.Count; c++)
                componentTemperature += temperature01[component[c]];
            componentTemperature /= Mathf.Max(component.Count, 1);

            float componentFreezeSum = 0f;
            for (int c = 0; c < component.Count; c++)
            {
                int vertex = component[c];
                float effectiveTemperature = Mathf.Lerp(componentTemperature, temperature01[vertex], factor);
                bodyFactor[vertex] = factor;
                temperature01[vertex] = effectiveTemperature;
                temperatureSum += effectiveTemperature;
                temperatureCount++;
                stats.MinWaterTemperature01 = Mathf.Min(stats.MinWaterTemperature01, effectiveTemperature);
                stats.MaxWaterTemperature01 = Mathf.Max(stats.MaxWaterTemperature01, effectiveTemperature);
                componentFreezeSum += EvaluateFreezeFactor(effectiveTemperature, factor, settings);
            }

            float averageFreeze = componentFreezeSum / Mathf.Max(component.Count, 1);
            if (averageFreeze >= 0.95f)
                stats.FrozenBodies++;
            else if (averageFreeze > 0.05f)
                stats.PartiallyFrozenBodies++;
            else
                stats.LiquidBodies++;

            void EnqueueWetNeighbor(int neighbor)
            {
                if (!wet[neighbor] || visited[neighbor])
                    return;

                visited[neighbor] = true;
                queue[tail++] = neighbor;
            }
        }

        if (temperatureCount > 0)
            stats.AverageWaterTemperature01 = temperatureSum / temperatureCount;
        else
            stats.MinWaterTemperature01 = 0f;
    }

    // CPU mirror of EvaluateFreezeFactor in Includes/WaterDisplacement.hlsl, which is the source of truth.
    // The mesh build runs on a worker thread and decides ice coverage per body before any shader sees the
    // mesh, so this curve cannot be shared with HLSL - it can only be kept identical. Change one, change
    // both. Mathf.SmoothStep(0, 1, InverseLerp(cold, warm, t)) is exactly HLSL smoothstep(cold, warm, t).
    static float EvaluateFreezeFactor(float temperature01, float bodyFactor, Settings settings)
    {
        if (!settings.EnableFreezing)
            return 0f;

        float start = Mathf.Lerp(
            settings.LakeFreezeStartTemperature01,
            settings.OceanFreezeStartTemperature01,
            bodyFactor);
        float complete = Mathf.Lerp(
            settings.LakeFreezeCompleteTemperature01,
            settings.OceanFreezeCompleteTemperature01,
            bodyFactor);
        float cold = Mathf.Min(start, complete);
        float warm = Mathf.Max(start, complete);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cold, warm, temperature01));
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
