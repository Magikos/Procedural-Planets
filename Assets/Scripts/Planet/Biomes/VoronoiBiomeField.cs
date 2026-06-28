using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

sealed class VoronoiBiomeField : IBiomeAssignmentField
{
    const int PrimaryAtlasResolution = 512;
    const int CubeFaceCount = 6;

    struct Seed
    {
        public Vector3 Position;
        public byte BiomeId;
    }

    struct KdNode
    {
        public int SeedIndex;
        public int Left;
        public int Right;
        public byte Axis;
    }

    readonly Seed[] _seeds;
    readonly KdNode[] _nodes;
    readonly int _root;
    readonly Noise _warpX;
    readonly Noise _warpY;
    readonly Noise _warpZ;
    readonly float _warpStrength;
    readonly float _warpScale;
    readonly int _warpOctaves;
    readonly float _warpPersistence;
    readonly float _warpLacunarity;
    byte[][] _primaryAtlas;

    public int SeedCount => _seeds.Length;
    public int CleanupChanges { get; }
    public int DistinctBiomeCount { get; }
    public int LookupAtlasResolution => _primaryAtlas != null ? PrimaryAtlasResolution : 0;
    public long BuildMilliseconds { get; private set; }

    VoronoiBiomeField(
        Seed[] seeds,
        KdNode[] nodes,
        int root,
        int warpSeed,
        BiomeDto biome,
        int cleanupChanges,
        int distinctBiomeCount,
        long buildMilliseconds)
    {
        _seeds = seeds;
        _nodes = nodes;
        _root = root;
        _warpX = new Noise(warpSeed ^ unchecked((int)0x68BC21EBu));
        _warpY = new Noise(warpSeed ^ unchecked((int)0xA0F2EC75u));
        _warpZ = new Noise(warpSeed ^ unchecked((int)0x967A889Bu));
        _warpStrength = biome.VoronoiDomainWarpStrength;
        _warpScale = BiomeConstants.VoronoiDomainWarpScale;
        _warpOctaves = BiomeConstants.VoronoiDomainWarpOctaves;
        _warpPersistence = BiomeConstants.VoronoiDomainWarpPersistence;
        _warpLacunarity = BiomeConstants.VoronoiDomainWarpLacunarity;
        CleanupChanges = cleanupChanges;
        DistinctBiomeCount = distinctBiomeCount;
        BuildMilliseconds = buildMilliseconds;
    }

    public static VoronoiBiomeField Build(
        BiomeDto biome,
        IClimateProvider climateProvider,
        int seed,
        Action<float> onProgress = null,
        CancellationToken ct = default)
    {
        if (biome == null) throw new ArgumentNullException(nameof(biome));
        if (biome.Registry == null) throw new ArgumentException("BiomeDto.Registry is null.", nameof(biome));
        if (climateProvider == null) throw new ArgumentNullException(nameof(climateProvider));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ct.ThrowIfCancellationRequested();
        int seedCount = biome.VoronoiSeedCount;
        Seed[] seeds = BuildFibonacciSeeds(seedCount, biome.VoronoiSeedJitter, seed);
        onProgress?.Invoke(0.02f);
        AssignClimateBiomes(
            seeds,
            biome.Registry,
            climateProvider,
            BiomeConstants.VoronoiTemperatureWeight);
        ct.ThrowIfCancellationRequested();
        onProgress?.Invoke(0.05f);
        int cleanupChanges = CleanupBiomeAssignments(
            seeds,
            BiomeConstants.VoronoiCleanupIterations);
        ct.ThrowIfCancellationRequested();
        onProgress?.Invoke(0.08f);
        int distinctBiomeCount = CountDistinctBiomes(seeds);
        BuildKdTree(seeds, out KdNode[] nodes, out int root);
        onProgress?.Invoke(0.1f);
        var field = new VoronoiBiomeField(
            seeds,
            nodes,
            root,
            seed,
            biome,
            cleanupChanges,
            distinctBiomeCount,
            0);
        field.BuildPrimaryAtlas(
            value => onProgress?.Invoke(0.1f + value * 0.9f),
            ct);
        stopwatch.Stop();
        field.BuildMilliseconds = stopwatch.ElapsedMilliseconds;
        return field;
    }

    public BiomeAssignmentSample Evaluate(Vector3 pointOnUnitSphere)
    {
        Vector3 query = DomainWarp(pointOnUnitSphere.normalized);
        int primarySeedIndex = FindNearest(query);
        if (primarySeedIndex < 0)
            return new BiomeAssignmentSample(0, 0, 0f);

        byte primaryId = _seeds[primarySeedIndex].BiomeId;
        float primaryDistanceSq = (_seeds[primarySeedIndex].Position - query).sqrMagnitude;
        int secondarySeedIndex = FindNearestDifferentBiome(query, primaryId);
        if (secondarySeedIndex < 0)
            return new BiomeAssignmentSample(primaryId, primaryId, 0f);

        byte secondaryId = _seeds[secondarySeedIndex].BiomeId;
        float secondaryDistanceSq = (_seeds[secondarySeedIndex].Position - query).sqrMagnitude;
        float primaryDistance = Mathf.Sqrt(Mathf.Max(primaryDistanceSq, 0f));
        float secondaryDistance = Mathf.Sqrt(Mathf.Max(secondaryDistanceSq, 0f));
        float denominator = primaryDistance + secondaryDistance;
        float secondaryWeight = denominator > 0.000001f
            ? primaryDistance / denominator
            : 0.5f;

        return new BiomeAssignmentSample(primaryId, secondaryId, secondaryWeight);
    }

    public byte EvaluatePrimaryId(Vector3 pointOnUnitSphere)
    {
        if (_primaryAtlas != null)
        {
            CoordinateConverter.UnitSphereToCubeFaceUvExact(pointOnUnitSphere.normalized, out int face, out Vector2 uv);
            int x = Mathf.Clamp(
                Mathf.FloorToInt(uv.x * PrimaryAtlasResolution),
                0,
                PrimaryAtlasResolution - 1);
            int y = Mathf.Clamp(
                Mathf.FloorToInt(uv.y * PrimaryAtlasResolution),
                0,
                PrimaryAtlasResolution - 1);
            return _primaryAtlas[face][y * PrimaryAtlasResolution + x];
        }

        return EvaluatePrimaryIdExact(pointOnUnitSphere);
    }

    byte EvaluatePrimaryIdExact(Vector3 pointOnUnitSphere)
    {
        Vector3 query = DomainWarp(pointOnUnitSphere.normalized);
        int seedIndex = FindNearest(query);
        return seedIndex >= 0 ? _seeds[seedIndex].BiomeId : (byte)0;
    }

    void BuildPrimaryAtlas(Action<float> onProgress, CancellationToken ct)
    {
        var atlas = new byte[CubeFaceCount][];
        for (int face = 0; face < CubeFaceCount; face++)
            atlas[face] = new byte[PrimaryAtlasResolution * PrimaryAtlasResolution];

        int completedRows = 0;
        int totalRows = CubeFaceCount * PrimaryAtlasResolution;
        var options = new ParallelOptions { CancellationToken = ct };
        Parallel.For(0, CubeFaceCount, options, face =>
        {
            byte[] faceIds = atlas[face];
            for (int y = 0; y < PrimaryAtlasResolution; y++)
            {
                float v = EdgeSnappedUv(y);
                int row = y * PrimaryAtlasResolution;
                for (int x = 0; x < PrimaryAtlasResolution; x++)
                {
                    float u = EdgeSnappedUv(x);
                    Vector3 direction = CoordinateConverter.CubeFaceToUnitSphere(
                        face,
                        new Vector2(u, v));
                    faceIds[row + x] = EvaluatePrimaryIdExact(direction);
                }

                int rows = Interlocked.Increment(ref completedRows);
                if ((rows & 15) == 0 || rows == totalRows)
                    onProgress?.Invoke((float)rows / totalRows);
            }
        });

        _primaryAtlas = atlas;
    }

    static float EdgeSnappedUv(int index)
    {
        if (index == 0) return 0f;
        if (index == PrimaryAtlasResolution - 1) return 1f;
        return (index + 0.5f) / PrimaryAtlasResolution;
    }

    Vector3 DomainWarp(Vector3 point)
    {
        if (_warpStrength <= 0f)
            return point;

        Vector3 samplePoint = point * _warpScale;
        Vector3 warp = new Vector3(
            FractalNoise(_warpX, samplePoint),
            FractalNoise(_warpY, samplePoint),
            FractalNoise(_warpZ, samplePoint));
        warp -= point * Vector3.Dot(warp, point);
        return (point + warp * _warpStrength).normalized;
    }

    float FractalNoise(Noise noise, Vector3 point)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float value = 0f;
        float amplitudeSum = 0f;
        for (int octave = 0; octave < _warpOctaves; octave++)
        {
            value += noise.Evaluate(point * frequency) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= _warpPersistence;
            frequency *= _warpLacunarity;
        }
        return amplitudeSum > 0f ? value / amplitudeSum : 0f;
    }

    int FindNearest(Vector3 query)
    {
        int bestIndex = -1;
        float bestDistanceSq = float.PositiveInfinity;
        FindNearestRecursive(_root, query, ref bestIndex, ref bestDistanceSq);
        return bestIndex;
    }

    void FindNearestRecursive(
        int nodeIndex,
        Vector3 query,
        ref int bestIndex,
        ref float bestDistanceSq)
    {
        if (nodeIndex < 0) return;

        KdNode node = _nodes[nodeIndex];
        Seed seed = _seeds[node.SeedIndex];
        float distanceSq = (seed.Position - query).sqrMagnitude;
        if (distanceSq < bestDistanceSq ||
            (Mathf.Approximately(distanceSq, bestDistanceSq) && node.SeedIndex < bestIndex))
        {
            bestDistanceSq = distanceSq;
            bestIndex = node.SeedIndex;
        }

        float delta = AxisValue(query, node.Axis) - AxisValue(seed.Position, node.Axis);
        int near = delta <= 0f ? node.Left : node.Right;
        int far = delta <= 0f ? node.Right : node.Left;
        FindNearestRecursive(near, query, ref bestIndex, ref bestDistanceSq);
        if (delta * delta <= bestDistanceSq)
            FindNearestRecursive(far, query, ref bestIndex, ref bestDistanceSq);
    }

    int FindNearestDifferentBiome(Vector3 query, byte excludedBiomeId)
    {
        int bestIndex = -1;
        float bestDistanceSq = float.PositiveInfinity;
        FindNearestDifferentRecursive(
            _root, query, excludedBiomeId, ref bestIndex, ref bestDistanceSq);
        return bestIndex;
    }

    void FindNearestDifferentRecursive(
        int nodeIndex,
        Vector3 query,
        byte excludedBiomeId,
        ref int bestIndex,
        ref float bestDistanceSq)
    {
        if (nodeIndex < 0) return;

        KdNode node = _nodes[nodeIndex];
        Seed seed = _seeds[node.SeedIndex];
        float distanceSq = (seed.Position - query).sqrMagnitude;
        if (seed.BiomeId != excludedBiomeId &&
            (distanceSq < bestDistanceSq ||
             (Mathf.Approximately(distanceSq, bestDistanceSq) && node.SeedIndex < bestIndex)))
        {
            bestDistanceSq = distanceSq;
            bestIndex = node.SeedIndex;
        }

        float delta = AxisValue(query, node.Axis) - AxisValue(seed.Position, node.Axis);
        int near = delta <= 0f ? node.Left : node.Right;
        int far = delta <= 0f ? node.Right : node.Left;
        FindNearestDifferentRecursive(
            near, query, excludedBiomeId, ref bestIndex, ref bestDistanceSq);
        if (delta * delta <= bestDistanceSq)
        {
            FindNearestDifferentRecursive(
                far, query, excludedBiomeId, ref bestIndex, ref bestDistanceSq);
        }
    }

    static Seed[] BuildFibonacciSeeds(int count, float jitter, int seed)
    {
        var result = new Seed[count];
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
        float averageSpacing = Mathf.Sqrt(4f * Mathf.PI / count);
        uint randomState = unchecked((uint)seed);
        if (randomState == 0u) randomState = 0x6D2B79F5u;

        for (int i = 0; i < count; i++)
        {
            float y = 1f - ((i + 0.5f) / count) * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float theta = goldenAngle * i;
            Vector3 position = new Vector3(
                Mathf.Cos(theta) * radius,
                y,
                Mathf.Sin(theta) * radius);

            Vector3 randomVector = new Vector3(
                NextSigned(ref randomState),
                NextSigned(ref randomState),
                NextSigned(ref randomState));
            Vector3 tangent = randomVector - position * Vector3.Dot(randomVector, position);
            if (tangent.sqrMagnitude > 0.000001f)
            {
                tangent.Normalize();
                float magnitude = Next01(ref randomState);
                position = (position + tangent * (averageSpacing * 0.5f * jitter * magnitude)).normalized;
            }

            result[i] = new Seed { Position = position };
        }

        return result;
    }

    static void AssignClimateBiomes(
        Seed[] seeds,
        BiomeRegistryDto registry,
        IClimateProvider climateProvider,
        float temperatureWeight)
    {
        int tempSteps = Mathf.Max(registry.TemperatureSteps, 1);
        int moistureSteps = Mathf.Max(registry.MoistureSteps, 1);
        int targetCount = tempSteps * moistureSteps;
        var targetTemperature = new float[targetCount];
        var targetMoisture = new float[targetCount];
        var targetBiomeId = new byte[targetCount];
        byte fallback = registry.GetSliceIdForBiomeType(BiomeType.Grassland);

        for (int t = 0; t < tempSteps; t++)
        {
            for (int m = 0; m < moistureSteps; m++)
            {
                int index = t * moistureSteps + m;
                targetTemperature[index] = (t + 0.5f) / tempSteps;
                targetMoisture[index] = (m + 0.5f) / moistureSteps;
                BiomeDefinitionDto definition = registry.GridEntries != null &&
                    index < registry.GridEntries.Length
                    ? registry.GridEntries[index]
                    : null;
                targetBiomeId[index] = definition != null
                    ? (byte)(index + 2)
                    : fallback;
            }
        }

        for (int i = 0; i < seeds.Length; i++)
        {
            ClimateSample climate = climateProvider.Evaluate(
                seeds[i].Position,
                BiomeConstants.OceanThreshold);
            float bestDistance = float.PositiveInfinity;
            byte bestBiomeId = fallback;
            for (int target = 0; target < targetCount; target++)
            {
                float dt = climate.Temperature01 - targetTemperature[target];
                float dm = climate.Moisture01 - targetMoisture[target];
                float distance = dt * dt * temperatureWeight + dm * dm;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestBiomeId = targetBiomeId[target];
                }
            }

            Seed assigned = seeds[i];
            assigned.BiomeId = bestBiomeId;
            seeds[i] = assigned;
        }
    }

    static int CleanupBiomeAssignments(Seed[] seeds, int iterations)
    {
        if (iterations <= 0 || seeds.Length < 9)
            return 0;

        var nextIds = new byte[seeds.Length];
        var nearestIndices = new int[8];
        var nearestDistances = new float[8];
        int totalChanges = 0;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            int iterationChanges = 0;
            for (int i = 0; i < seeds.Length; i++)
            {
                FindNearestEight(seeds, i, nearestIndices, nearestDistances);
                byte majorityId = seeds[i].BiomeId;
                int majorityCount = 0;
                for (int n = 0; n < nearestIndices.Length; n++)
                {
                    int neighborIndex = nearestIndices[n];
                    if (neighborIndex < 0) continue;
                    byte candidateId = seeds[neighborIndex].BiomeId;
                    int count = 0;
                    for (int k = 0; k < nearestIndices.Length; k++)
                    {
                        int otherIndex = nearestIndices[k];
                        if (otherIndex >= 0 && seeds[otherIndex].BiomeId == candidateId)
                            count++;
                    }

                    if (count > majorityCount ||
                        (count == majorityCount && candidateId < majorityId))
                    {
                        majorityCount = count;
                        majorityId = candidateId;
                    }
                }

                byte currentId = seeds[i].BiomeId;
                nextIds[i] = majorityCount >= 6 ? majorityId : currentId;
                if (nextIds[i] != currentId)
                    iterationChanges++;
            }

            for (int i = 0; i < seeds.Length; i++)
            {
                Seed updated = seeds[i];
                updated.BiomeId = nextIds[i];
                seeds[i] = updated;
            }

            totalChanges += iterationChanges;
            if (iterationChanges == 0)
                break;
        }

        return totalChanges;
    }

    static void FindNearestEight(
        Seed[] seeds,
        int sourceIndex,
        int[] nearestIndices,
        float[] nearestDistances)
    {
        for (int i = 0; i < nearestIndices.Length; i++)
        {
            nearestIndices[i] = -1;
            nearestDistances[i] = float.PositiveInfinity;
        }

        Vector3 source = seeds[sourceIndex].Position;
        for (int candidate = 0; candidate < seeds.Length; candidate++)
        {
            if (candidate == sourceIndex) continue;
            float distanceSq = (seeds[candidate].Position - source).sqrMagnitude;
            int insert = nearestDistances.Length - 1;
            if (distanceSq >= nearestDistances[insert]) continue;
            while (insert > 0 && distanceSq < nearestDistances[insert - 1])
                insert--;
            for (int shift = nearestDistances.Length - 1; shift > insert; shift--)
            {
                nearestDistances[shift] = nearestDistances[shift - 1];
                nearestIndices[shift] = nearestIndices[shift - 1];
            }
            nearestDistances[insert] = distanceSq;
            nearestIndices[insert] = candidate;
        }
    }

    static int CountDistinctBiomes(Seed[] seeds)
    {
        var seen = new bool[256];
        int count = 0;
        for (int i = 0; i < seeds.Length; i++)
        {
            byte id = seeds[i].BiomeId;
            if (seen[id]) continue;
            seen[id] = true;
            count++;
        }
        return count;
    }

    static void BuildKdTree(Seed[] seeds, out KdNode[] nodes, out int root)
    {
        var indices = new int[seeds.Length];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;
        nodes = new KdNode[seeds.Length];
        int nextNode = 0;
        root = BuildKdRecursive(seeds, indices, 0, indices.Length, 0, nodes, ref nextNode);
    }

    static int BuildKdRecursive(
        Seed[] seeds,
        int[] indices,
        int start,
        int length,
        int depth,
        KdNode[] nodes,
        ref int nextNode)
    {
        if (length <= 0) return -1;

        byte axis = (byte)(depth % 3);
        Array.Sort(
            indices,
            start,
            length,
            Comparer<int>.Create((a, b) =>
            {
                int comparison = AxisValue(seeds[a].Position, axis)
                    .CompareTo(AxisValue(seeds[b].Position, axis));
                return comparison != 0 ? comparison : a.CompareTo(b);
            }));

        int medianOffset = length / 2;
        int median = start + medianOffset;
        int nodeIndex = nextNode++;
        int left = BuildKdRecursive(
            seeds, indices, start, medianOffset, depth + 1, nodes, ref nextNode);
        int right = BuildKdRecursive(
            seeds,
            indices,
            median + 1,
            length - medianOffset - 1,
            depth + 1,
            nodes,
            ref nextNode);
        nodes[nodeIndex] = new KdNode
        {
            SeedIndex = indices[median],
            Left = left,
            Right = right,
            Axis = axis,
        };
        return nodeIndex;
    }

    static float AxisValue(Vector3 value, byte axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    static uint NextUInt(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    static float Next01(ref uint state)
    {
        return (NextUInt(ref state) & 0x00FFFFFFu) / 16777215f;
    }

    static float NextSigned(ref uint state)
    {
        return Next01(ref state) * 2f - 1f;
    }
}
