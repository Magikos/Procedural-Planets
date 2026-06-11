using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

sealed class ClimateCurveLut
{
    readonly float[] _samples;

    ClimateCurveLut(float[] samples)
    {
        _samples = samples;
    }

    public static ClimateCurveLut Bake(AnimationCurve curve, int resolution)
    {
        resolution = Mathf.Clamp(resolution, 16, 512);
        var samples = new float[resolution];
        float denominator = resolution - 1f;
        for (int i = 0; i < resolution; i++)
            samples[i] = Mathf.Clamp01(curve.Evaluate(i / denominator));
        return new ClimateCurveLut(samples);
    }

    public float Sample(float input01)
    {
        float position = Mathf.Clamp01(input01) * (_samples.Length - 1);
        int lower = Mathf.FloorToInt(position);
        int upper = Mathf.Min(lower + 1, _samples.Length - 1);
        return Mathf.LerpUnclamped(_samples[lower], _samples[upper], position - lower);
    }
}

readonly struct TemperatureClimateSample
{
    public readonly float FinalTemperature01;
    public readonly float Latitude01;
    public readonly float LatitudeTemperature01;
    public readonly float NoiseContribution;
    public readonly float AltitudeDrop;

    public TemperatureClimateSample(
        float finalTemperature01,
        float latitude01,
        float latitudeTemperature01,
        float noiseContribution,
        float altitudeDrop)
    {
        FinalTemperature01 = finalTemperature01;
        Latitude01 = latitude01;
        LatitudeTemperature01 = latitudeTemperature01;
        NoiseContribution = noiseContribution;
        AltitudeDrop = altitudeDrop;
    }
}

public class TemperatureProvider : ITemperatureProvider
{
    readonly NoiseSettings _noiseSettings;
    readonly ClimateCurveLut _latitudeCurve;
    readonly float _noiseStrength;
    readonly float _altitudeTemperatureDrop;
    readonly float _waterLevel;
    INoiseFilter _noiseFilter;
    float _maxValue;

    internal TemperatureProvider(
        NoiseSettings noiseSettings,
        ClimateCurveLut latitudeCurve,
        float noiseStrength,
        float altitudeTemperatureDrop,
        float waterLevel)
    {
        _noiseSettings = noiseSettings;
        _latitudeCurve = latitudeCurve;
        _noiseStrength = noiseStrength;
        _altitudeTemperatureDrop = altitudeTemperatureDrop;
        _waterLevel = waterLevel;
    }

    public void Initialize(int seed)
    {
        _noiseFilter = NoiseFilterFactory.CreateNoiseFilter(_noiseSettings, seed);

        float amp = 1f;
        _maxValue = 0f;
        for (int i = 0; i < _noiseSettings.Layers; i++)
        {
            _maxValue += amp;
            amp *= _noiseSettings.Persistence;
        }
        _maxValue *= _noiseSettings.Strength;
        if (_maxValue < 0.001f) _maxValue = 1f;
    }

    public float Evaluate(Vector3 pointOnUnitSphere)
    {
        return EvaluateClimate(pointOnUnitSphere, _waterLevel).FinalTemperature01;
    }

    internal TemperatureClimateSample EvaluateClimate(
        Vector3 pointOnUnitSphere,
        float elevation)
    {
        float latitude01 = CoordinateConverter.NormalizedLatitude(pointOnUnitSphere);
        float latitudeTemperature01 = _latitudeCurve.Sample(latitude01);
        float normalized = _noiseFilter.Evaluate(pointOnUnitSphere) / _maxValue;
        float noiseContribution = (normalized - 0.5f) * _noiseStrength;
        float landHeight = Mathf.Max(0f, elevation - _waterLevel);
        float altitudeDrop = landHeight * _altitudeTemperatureDrop;
        float finalTemperature = Mathf.Clamp01(
            latitudeTemperature01 + noiseContribution - altitudeDrop);

        return new TemperatureClimateSample(
            finalTemperature,
            latitude01,
            latitudeTemperature01,
            noiseContribution,
            altitudeDrop);
    }
}

public sealed class ClimateProvider : IClimateProvider
{
    readonly TemperatureProvider _temperatureProvider;
    readonly MoistureProvider _moistureProvider;
    readonly float _minimumTemperatureCelsius;
    readonly float _maximumTemperatureCelsius;

    public ClimateProvider(BiomeSettings settings)
    {
        settings.EnsureClimateCurves();
        int resolution = settings.ClimateLutResolution;
        ClimateCurveLut temperatureCurve = ClimateCurveLut.Bake(
            settings.TemperatureLatitudeCurve, resolution);
        ClimateCurveLut moistureCurve = ClimateCurveLut.Bake(
            settings.MoistureLatitudeCurve, resolution);
        float waterLevel = BiomeConstants.OceanThreshold;

        _temperatureProvider = new TemperatureProvider(
            settings.TemperatureNoise,
            temperatureCurve,
            settings.TemperatureNoiseStrength,
            settings.AltitudeTemperatureDrop,
            waterLevel);
        _moistureProvider = new MoistureProvider(
            settings.MoistureNoise,
            moistureCurve,
            settings.MoistureLatitudeInfluence,
            settings.MoistureNoiseStrength);
        _minimumTemperatureCelsius = settings.MinimumTemperatureCelsius;
        _maximumTemperatureCelsius = settings.MaximumTemperatureCelsius;
    }

    public void Initialize(int seed)
    {
        _temperatureProvider.Initialize(seed);
        _moistureProvider.Initialize(seed + 100);
    }

    public ClimateSample Evaluate(Vector3 pointOnUnitSphere, float elevation)
    {
        TemperatureClimateSample temperature = _temperatureProvider.EvaluateClimate(
            pointOnUnitSphere, elevation);
        MoistureClimateSample moisture = _moistureProvider.EvaluateClimate(
            pointOnUnitSphere, temperature.Latitude01);

        return new ClimateSample(
            temperature.FinalTemperature01,
            TemperatureUnits.NormalizedToCelsius(
                temperature.FinalTemperature01,
                _minimumTemperatureCelsius,
                _maximumTemperatureCelsius),
            moisture.FinalMoisture01,
            elevation,
            temperature.Latitude01,
            temperature.LatitudeTemperature01,
            temperature.NoiseContribution,
            temperature.AltitudeDrop,
            moisture.LatitudeMoisture01,
            moisture.NoiseContribution,
            moisture.LegacyMoisture01);
    }
}

public sealed class ClimateMapGpuData : IDisposable
{
    const int FaceCount = 6;

    static readonly int ClimateMapId = Shader.PropertyToID("_ClimateMap");
    static readonly int ClimateMapResolutionId = Shader.PropertyToID("_ClimateMapResolution");
    static readonly int ClimateTemperatureRangeId =
        Shader.PropertyToID("_ClimateTemperatureRangeCelsius");

    Texture2DArray _texture;

    ClimateMapGpuData(Texture2DArray texture)
    {
        _texture = texture;
    }

    public Texture2DArray Texture => _texture;
    public int Resolution => _texture != null ? _texture.width : 0;

    public static ClimateMapGpuData Build(
        IClimateProvider climateProvider,
        IReadOnlyList<IFaceMeshSampler> faceSamplers,
        int resolution,
        float minimumTemperatureCelsius,
        float maximumTemperatureCelsius,
        ILogger logger)
    {
        if (climateProvider == null)
        {
            logger?.Log(LogLevel.Warning, "Climate",
                "Skipped GPU climate map because no climate provider was available.");
            return null;
        }
        if (faceSamplers == null || faceSamplers.Count < FaceCount)
        {
            logger?.Log(LogLevel.Warning, "Climate",
                "Skipped GPU climate map because six generated face samplers were not available.");
            return null;
        }

        resolution = Mathf.Clamp(resolution, 32, 512);
        var stopwatch = Stopwatch.StartNew();
        var facePixels = new Color[FaceCount][];

        Parallel.For(0, FaceCount, face =>
        {
            IFaceMeshSampler sampler = faceSamplers[face];
            var pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                float v = EdgeSnappedUv(y, resolution);
                int row = y * resolution;
                for (int x = 0; x < resolution; x++)
                {
                    float u = EdgeSnappedUv(x, resolution);
                    Vector3 direction = CoordinateConverter.CubeFaceToUnitSphere(
                        face,
                        new Vector2(u, v));
                    float elevation = SampleElevation(sampler, u, v);
                    ClimateSample climate = climateProvider.Evaluate(direction, elevation);
                    pixels[row + x] = new Color(
                        climate.Temperature01,
                        climate.Moisture01,
                        0f,
                        1f);
                }
            }

            facePixels[face] = pixels;
        });

        TextureFormat format = SystemInfo.SupportsTextureFormat(TextureFormat.RGHalf)
            ? TextureFormat.RGHalf
            : TextureFormat.RGBAHalf;
        var texture = new Texture2DArray(
            resolution,
            resolution,
            FaceCount,
            format,
            true,
            true)
        {
            name = "Planet Climate Map",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 0,
            hideFlags = HideFlags.DontSave,
        };

        for (int face = 0; face < FaceCount; face++)
            texture.SetPixels(facePixels[face], face, 0);
        texture.Apply(true, true);

        Shader.SetGlobalTexture(ClimateMapId, texture);
        Shader.SetGlobalFloat(ClimateMapResolutionId, resolution);
        Shader.SetGlobalVector(
            ClimateTemperatureRangeId,
            new Vector4(
                minimumTemperatureCelsius,
                maximumTemperatureCelsius,
                maximumTemperatureCelsius - minimumTemperatureCelsius,
                0f));

        stopwatch.Stop();
        int bytesPerPixel = format == TextureFormat.RGHalf ? 4 : 8;
        long baseBytes = (long)resolution * resolution * FaceCount * bytesPerPixel;
        long approximateBytesWithMips = baseBytes * 4L / 3L;
        logger?.Log(
            LogLevel.Debug,
            "Climate",
            $"Built GPU climate map: {resolution}x{resolution}x6 {format}, " +
            $"~{approximateBytesWithMips / (1024f * 1024f):F2} MiB, " +
            $"{stopwatch.ElapsedMilliseconds} ms");

        return new ClimateMapGpuData(texture);
    }

    public void Dispose()
    {
        if (_texture == null)
            return;

        if (Shader.GetGlobalTexture(ClimateMapId) == _texture)
        {
            Shader.SetGlobalTexture(ClimateMapId, (Texture)null);
            Shader.SetGlobalFloat(ClimateMapResolutionId, 0f);
        }

        UnityEngine.Object.Destroy(_texture);
        _texture = null;
    }

    static float SampleElevation(IFaceMeshSampler sampler, float u, float v)
    {
        if (sampler?.Elevations == null || sampler.Resolution < 2)
            return 0f;

        int resolution = sampler.Resolution;
        float x = Mathf.Clamp01(u) * (resolution - 1);
        float y = Mathf.Clamp01(v) * (resolution - 1);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, resolution - 1);
        int y1 = Mathf.Min(y0 + 1, resolution - 1);
        float tx = x - x0;
        float ty = y - y0;
        float a = Mathf.Lerp(
            sampler.Elevations[y0 * resolution + x0],
            sampler.Elevations[y0 * resolution + x1],
            tx);
        float b = Mathf.Lerp(
            sampler.Elevations[y1 * resolution + x0],
            sampler.Elevations[y1 * resolution + x1],
            tx);
        return Mathf.Lerp(a, b, ty);
    }

    static float EdgeSnappedUv(int index, int resolution)
    {
        if (index <= 0) return 0f;
        if (index >= resolution - 1) return 1f;
        return (index + 0.5f) / resolution;
    }
}

readonly struct VoronoiBiomeSample
{
    public readonly byte PrimaryId;
    public readonly byte SecondaryId;
    public readonly float SecondaryWeight;

    public VoronoiBiomeSample(byte primaryId, byte secondaryId, float secondaryWeight)
    {
        PrimaryId = primaryId;
        SecondaryId = secondaryId;
        SecondaryWeight = secondaryId != primaryId ? Mathf.Clamp01(secondaryWeight) : 0f;
    }
}

sealed class VoronoiBiomeField
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
        BiomeSettings settings,
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
        _warpStrength = settings.VoronoiDomainWarpStrength;
        _warpScale = settings.VoronoiDomainWarpScale;
        _warpOctaves = settings.VoronoiDomainWarpOctaves;
        _warpPersistence = settings.VoronoiDomainWarpPersistence;
        _warpLacunarity = settings.VoronoiDomainWarpLacunarity;
        CleanupChanges = cleanupChanges;
        DistinctBiomeCount = distinctBiomeCount;
        BuildMilliseconds = buildMilliseconds;
    }

    public static VoronoiBiomeField Build(
        BiomeSettings settings,
        BiomeRegistry registry,
        IClimateProvider climateProvider,
        int seed)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (climateProvider == null) throw new ArgumentNullException(nameof(climateProvider));

        settings.EnsureClimateCurves();
        var stopwatch = Stopwatch.StartNew();
        int seedCount = settings.VoronoiSeedCount;
        Seed[] seeds = BuildFibonacciSeeds(seedCount, settings.VoronoiSeedJitter, seed);
        AssignClimateBiomes(
            seeds,
            registry,
            climateProvider,
            settings.VoronoiTemperatureWeight);
        int cleanupChanges = CleanupBiomeAssignments(
            seeds,
            settings.VoronoiCleanupIterations);
        int distinctBiomeCount = CountDistinctBiomes(seeds);
        BuildKdTree(seeds, out KdNode[] nodes, out int root);
        var field = new VoronoiBiomeField(
            seeds,
            nodes,
            root,
            seed,
            settings,
            cleanupChanges,
            distinctBiomeCount,
            0);
        field.BuildPrimaryAtlas();
        stopwatch.Stop();
        field.BuildMilliseconds = stopwatch.ElapsedMilliseconds;
        return field;
    }

    public VoronoiBiomeSample Evaluate(Vector3 pointOnUnitSphere)
    {
        Vector3 query = DomainWarp(pointOnUnitSphere.normalized);
        int primarySeedIndex = FindNearest(query);
        if (primarySeedIndex < 0)
            return new VoronoiBiomeSample(0, 0, 0f);

        byte primaryId = _seeds[primarySeedIndex].BiomeId;
        float primaryDistanceSq = (_seeds[primarySeedIndex].Position - query).sqrMagnitude;
        int secondarySeedIndex = FindNearestDifferentBiome(query, primaryId);
        if (secondarySeedIndex < 0)
            return new VoronoiBiomeSample(primaryId, primaryId, 0f);

        byte secondaryId = _seeds[secondarySeedIndex].BiomeId;
        float secondaryDistanceSq = (_seeds[secondarySeedIndex].Position - query).sqrMagnitude;
        float primaryDistance = Mathf.Sqrt(Mathf.Max(primaryDistanceSq, 0f));
        float secondaryDistance = Mathf.Sqrt(Mathf.Max(secondaryDistanceSq, 0f));
        float denominator = primaryDistance + secondaryDistance;
        float secondaryWeight = denominator > 0.000001f
            ? primaryDistance / denominator
            : 0.5f;

        return new VoronoiBiomeSample(primaryId, secondaryId, secondaryWeight);
    }

    public byte EvaluatePrimaryId(Vector3 pointOnUnitSphere)
    {
        if (_primaryAtlas != null)
        {
            DirectionToAtlasFaceUv(pointOnUnitSphere.normalized, out int face, out Vector2 uv);
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

    void BuildPrimaryAtlas()
    {
        var atlas = new byte[CubeFaceCount][];
        for (int face = 0; face < CubeFaceCount; face++)
            atlas[face] = new byte[PrimaryAtlasResolution * PrimaryAtlasResolution];

        Parallel.For(0, CubeFaceCount, face =>
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
            }
        });

        _primaryAtlas = atlas;
    }

    // Exact inverse of CoordinateConverter.CubeFaceToUnitSphere. The older
    // CoordinateConverter.UnitSphereToCubeFace uses a different UV orientation and cannot
    // address an atlas generated with the current cube-face basis.
    static void DirectionToAtlasFaceUv(Vector3 direction, out int face, out Vector2 uv)
    {
        float absX = Mathf.Abs(direction.x);
        float absY = Mathf.Abs(direction.y);
        float absZ = Mathf.Abs(direction.z);

        Vector3 localUp;
        if (absY >= absX && absY >= absZ)
        {
            face = direction.y >= 0f ? 0 : 1;
            localUp = direction.y >= 0f ? Vector3.up : Vector3.down;
        }
        else if (absX >= absY && absX >= absZ)
        {
            face = direction.x >= 0f ? 3 : 2;
            localUp = direction.x >= 0f ? Vector3.right : Vector3.left;
        }
        else
        {
            face = direction.z >= 0f ? 4 : 5;
            localUp = direction.z >= 0f ? Vector3.forward : Vector3.back;
        }

        Vector3 axisA = new Vector3(localUp.y, localUp.z, localUp.x);
        Vector3 axisB = Vector3.Cross(localUp, axisA);
        float major = Mathf.Max(Mathf.Abs(Vector3.Dot(direction, localUp)), 0.00001f);
        float u = Vector3.Dot(direction, axisA) / major;
        float v = Vector3.Dot(direction, axisB) / major;
        uv = new Vector2(
            Mathf.Clamp01(u * 0.5f + 0.5f),
            Mathf.Clamp01(v * 0.5f + 0.5f));
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
        BiomeRegistry registry,
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
                BiomeDefinition definition = registry.GridEntries != null &&
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
