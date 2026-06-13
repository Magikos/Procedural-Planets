using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct WeatherGridStats
{
    public readonly int CellCount;
    public readonly int CloudyCellCount;
    public readonly int StormCellCount;
    public readonly int RainCandidateCellCount;
    public readonly int RainingCellCount;
    public readonly float AverageCondensation;
    public readonly float AverageStorm;
    public readonly float AverageMoistureSource;
    public readonly float AverageRainRate;
    public readonly float MaxCondensation;
    public readonly float MaxStorm;
    public readonly float MaxMoistureSource;
    public readonly float MaxRainRate;
    public readonly Vector3 StrongestStormDirection;
    public readonly float StrongestStormCondensation;
    public readonly float StrongestStorm;
    public readonly float StrongestStormMoistureSource;

    public WeatherGridStats(
        int cellCount,
        int cloudyCellCount,
        int stormCellCount,
        int rainCandidateCellCount,
        int rainingCellCount,
        float averageCondensation,
        float averageStorm,
        float averageMoistureSource,
        float averageRainRate,
        float maxCondensation,
        float maxStorm,
        float maxMoistureSource,
        float maxRainRate,
        Vector3 strongestStormDirection,
        float strongestStormCondensation,
        float strongestStorm,
        float strongestStormMoistureSource)
    {
        CellCount = cellCount;
        CloudyCellCount = cloudyCellCount;
        StormCellCount = stormCellCount;
        RainCandidateCellCount = rainCandidateCellCount;
        RainingCellCount = rainingCellCount;
        AverageCondensation = averageCondensation;
        AverageStorm = averageStorm;
        AverageMoistureSource = averageMoistureSource;
        AverageRainRate = averageRainRate;
        MaxCondensation = maxCondensation;
        MaxStorm = maxStorm;
        MaxMoistureSource = maxMoistureSource;
        MaxRainRate = maxRainRate;
        StrongestStormDirection = strongestStormDirection;
        StrongestStormCondensation = strongestStormCondensation;
        StrongestStorm = strongestStorm;
        StrongestStormMoistureSource = strongestStormMoistureSource;
    }
}

public sealed class SphericalWeatherGrid : IDisposable
{
    public int Resolution { get; }
    public Texture Texture => _activeTexture;
    public Texture DynamicsTexture => _dynamicsActiveTexture;

    public const float DeltaVisualizationScale = 16f;

    readonly float[] _condensation;
    readonly float[] _storm;
    readonly float[] _moistureSource;
    readonly float[] _humidity;
    readonly float[] _precipitationWater;
    readonly float[] _rainRate;
    RenderTexture _activeTexture;
    RenderTexture _scratchTexture;
    RenderTexture _dynamicsActiveTexture;
    RenderTexture _dynamicsScratchTexture;

    static readonly int _weatherReadId = Shader.PropertyToID("_WeatherRead");
    static readonly int _weatherWriteId = Shader.PropertyToID("_WeatherWrite");
    static readonly int _dynamicsReadId = Shader.PropertyToID("_DynamicsRead");
    static readonly int _dynamicsWriteId = Shader.PropertyToID("_DynamicsWrite");
    static readonly int _resolutionId = Shader.PropertyToID("_Resolution");
    static readonly int _deltaTimeId = Shader.PropertyToID("_DeltaTime");
    static readonly int _stormThresholdId = Shader.PropertyToID("_StormThreshold");
    static readonly int _moistureSourceStrengthId = Shader.PropertyToID("_MoistureSourceStrength");
    static readonly int _dryAirEvaporationRateId = Shader.PropertyToID("_DryAirEvaporationRate");
    static readonly int _stormGrowthRateId = Shader.PropertyToID("_StormGrowthRate");
    static readonly int _stormDecayRateId = Shader.PropertyToID("_StormDecayRate");
    static readonly int _stormMoistureBiasId = Shader.PropertyToID("_StormMoistureBias");
    static readonly int _stormSourceThresholdId = Shader.PropertyToID("_StormSourceThreshold");
    static readonly int _stormSourceSoftnessId = Shader.PropertyToID("_StormSourceSoftness");
    static readonly int _rainFormationThresholdId = Shader.PropertyToID("_RainFormationThreshold");
    static readonly int _rainFormationSoftnessId = Shader.PropertyToID("_RainFormationSoftness");
    static readonly int _rainCloudThresholdId = Shader.PropertyToID("_RainCloudThreshold");
    static readonly int _precipitationBuildRateId = Shader.PropertyToID("_PrecipitationBuildRate");
    static readonly int _precipitationDecayRateId = Shader.PropertyToID("_PrecipitationDecayRate");
    static readonly int _rainOutRateId = Shader.PropertyToID("_RainOutRate");
    static readonly int _humidityRecoveryRateId = Shader.PropertyToID("_HumidityRecoveryRate");
    static readonly int _condensationRainDrainId = Shader.PropertyToID("_CondensationRainDrain");
    static readonly int _deltaVisualizationScaleId = Shader.PropertyToID("_DeltaVisualizationScale");
    static readonly int _windDirectionId = Shader.PropertyToID(ShaderGlobalIds.WindDirection);
    static readonly int _stepAngleId = Shader.PropertyToID("_StepAngle");

    SphericalWeatherGrid(
        int resolution,
        RenderTexture activeTexture,
        RenderTexture scratchTexture,
        RenderTexture dynamicsActiveTexture,
        RenderTexture dynamicsScratchTexture,
        float[] condensation,
        float[] storm,
        float[] moistureSource,
        float[] humidity,
        float[] precipitationWater,
        float[] rainRate)
    {
        Resolution = resolution;
        _activeTexture = activeTexture;
        _scratchTexture = scratchTexture;
        _dynamicsActiveTexture = dynamicsActiveTexture;
        _dynamicsScratchTexture = dynamicsScratchTexture;
        _condensation = condensation;
        _storm = storm;
        _moistureSource = moistureSource;
        _humidity = humidity;
        _precipitationWater = precipitationWater;
        _rainRate = rainRate;
    }

    // Holds the CPU-side computation result before any Unity texture API is called.
    // All fields are plain C# arrays — safe to produce on a background thread.
    struct GridComputeData
    {
        public int Resolution;
        public float[] Condensation, Storm, MoistureSource, Humidity, PrecipitationWater, RainRate;
        public Color[][] PixelsByFace;
        public Color[][] DynamicsPixelsByFace;
    }

    // Schedules the parallel Burst job + allocates the NativeArrays it writes into.
    // Must be called on the main thread (Job scheduling requirement).
    static WeatherJobState ScheduleGridJob(CloudDto settings, int seed)
    {
        int resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(settings.WeatherResolution, 32, 512));
        int cellCount = resolution * resolution * 6;

        var state = new WeatherJobState
        {
            Resolution = resolution,
            Condensation = new NativeArray<float>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            Storm = new NativeArray<float>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            MoistureSource = new NativeArray<float>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            Humidity = new NativeArray<float>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            PrecipitationWater = new NativeArray<float>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            RainRate = new NativeArray<float>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            WeatherPixels = new NativeArray<float4>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            DynamicsPixels = new NativeArray<float4>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
        };

        float coverageThreshold = Mathf.Lerp(0.84f, 0.18f, settings.InitialCoverage);
        float edgeWidth = Mathf.Clamp(1f / Mathf.Max(1f, CloudConstants.FrontSharpness), 0.015f, 0.25f);

        var job = new WeatherGridGenerationJob
        {
            Resolution = resolution,
            FrontNoise = NoiseData.Create(seed),
            DetailNoise = NoiseData.Create(seed + 7919),
            ClimateNoise = NoiseData.Create(seed + 104729),
            FrontScale = CloudConstants.FrontScale,
            BiomeInfluence = CloudConstants.BiomeInfluence,
            CoverageThreshold = coverageThreshold,
            EdgeWidth = edgeWidth,
            StormSourceThreshold = CloudConstants.StormSourceThreshold,
            StormSourceSoftness = CloudConstants.StormSourceSoftness,
            StormMoistureBias = CloudConstants.StormMoistureBias,
            StormThreshold = settings.StormThreshold,
            RainFormationThreshold = CloudConstants.RainFormationThreshold,
            RainFormationSoftness = CloudConstants.RainFormationSoftness,
            RainCloudThreshold = CloudConstants.RainCloudThreshold,
            Condensation = state.Condensation,
            Storm = state.Storm,
            MoistureSource = state.MoistureSource,
            Humidity = state.Humidity,
            PrecipitationWater = state.PrecipitationWater,
            RainRate = state.RainRate,
            WeatherPixels = state.WeatherPixels,
            DynamicsPixels = state.DynamicsPixels,
        };

        state.Handle = job.Schedule(cellCount, 256);
        JobHandle.ScheduleBatchedJobs();
        return state;
    }

    // Copies job results out of NativeArrays into the managed arrays expected by
    // UploadGridData and the SphericalWeatherGrid runtime accessors, then disposes
    // the NativeArrays. Must be called after the job's handle has completed.
    static GridComputeData CopyOutAndDispose(WeatherJobState state)
    {
        int resolution = state.Resolution;
        int facePixelCount = resolution * resolution;
        int cellCount = facePixelCount * 6;

        var condensation = new float[cellCount];
        var storm = new float[cellCount];
        var moistureSource = new float[cellCount];
        var humidity = new float[cellCount];
        var precipitationWater = new float[cellCount];
        var rainRate = new float[cellCount];
        var pixelsByFace = new Color[6][];
        var dynamicsPixelsByFace = new Color[6][];

        NativeArray<float>.Copy(state.Condensation, condensation, cellCount);
        NativeArray<float>.Copy(state.Storm, storm, cellCount);
        NativeArray<float>.Copy(state.MoistureSource, moistureSource, cellCount);
        NativeArray<float>.Copy(state.Humidity, humidity, cellCount);
        NativeArray<float>.Copy(state.PrecipitationWater, precipitationWater, cellCount);
        NativeArray<float>.Copy(state.RainRate, rainRate, cellCount);

        // float4 ↔ Color are bit-identical (4 floats); use Reinterpret + face-slice copy.
        var weatherAsColor = state.WeatherPixels.Reinterpret<Color>(sizeof(float) * 4);
        var dynamicsAsColor = state.DynamicsPixels.Reinterpret<Color>(sizeof(float) * 4);
        for (int face = 0; face < 6; face++)
        {
            var weatherFace = new Color[facePixelCount];
            var dynamicsFace = new Color[facePixelCount];
            NativeArray<Color>.Copy(weatherAsColor, face * facePixelCount, weatherFace, 0, facePixelCount);
            NativeArray<Color>.Copy(dynamicsAsColor, face * facePixelCount, dynamicsFace, 0, facePixelCount);
            pixelsByFace[face] = weatherFace;
            dynamicsPixelsByFace[face] = dynamicsFace;
        }

        state.Dispose();

        return new GridComputeData
        {
            Resolution = resolution,
            Condensation = condensation,
            Storm = storm,
            MoistureSource = moistureSource,
            Humidity = humidity,
            PrecipitationWater = precipitationWater,
            RainRate = rainRate,
            PixelsByFace = pixelsByFace,
            DynamicsPixelsByFace = dynamicsPixelsByFace
        };
    }

    struct WeatherJobState
    {
        public int Resolution;
        public JobHandle Handle;
        public NativeArray<float> Condensation;
        public NativeArray<float> Storm;
        public NativeArray<float> MoistureSource;
        public NativeArray<float> Humidity;
        public NativeArray<float> PrecipitationWater;
        public NativeArray<float> RainRate;
        public NativeArray<float4> WeatherPixels;
        public NativeArray<float4> DynamicsPixels;

        public void Dispose()
        {
            if (Condensation.IsCreated) Condensation.Dispose();
            if (Storm.IsCreated) Storm.Dispose();
            if (MoistureSource.IsCreated) MoistureSource.Dispose();
            if (Humidity.IsCreated) Humidity.Dispose();
            if (PrecipitationWater.IsCreated) PrecipitationWater.Dispose();
            if (RainRate.IsCreated) RainRate.Dispose();
            if (WeatherPixels.IsCreated) WeatherPixels.Dispose();
            if (DynamicsPixels.IsCreated) DynamicsPixels.Dispose();
        }
    }

    [BurstCompile]
    struct WeatherGridGenerationJob : IJobParallelFor
    {
        public int Resolution;

        public NoiseData FrontNoise;
        public NoiseData DetailNoise;
        public NoiseData ClimateNoise;

        public float FrontScale;
        public float BiomeInfluence;
        public float CoverageThreshold;
        public float EdgeWidth;
        public float StormSourceThreshold;
        public float StormSourceSoftness;
        public float StormMoistureBias;
        public float StormThreshold;
        public float RainFormationThreshold;
        public float RainFormationSoftness;
        public float RainCloudThreshold;

        [WriteOnly] public NativeArray<float> Condensation;
        [WriteOnly] public NativeArray<float> Storm;
        [WriteOnly] public NativeArray<float> MoistureSource;
        [WriteOnly] public NativeArray<float> Humidity;
        [WriteOnly] public NativeArray<float> PrecipitationWater;
        [WriteOnly] public NativeArray<float> RainRate;
        [WriteOnly] public NativeArray<float4> WeatherPixels;
        [WriteOnly] public NativeArray<float4> DynamicsPixels;

        public void Execute(int index)
        {
            int faceCellCount = Resolution * Resolution;
            int face = index / faceCellCount;
            int faceIndex = index - face * faceCellCount;
            int x = faceIndex % Resolution;
            int y = faceIndex / Resolution;

            // Edge cells snap their noise-sample UV to the exact cube edge (0 or 1).
            // Reason: at face boundaries, an adjacent face's edge cell sees the SAME 3D
            // direction when both sides snap to the shared edge, so noise returns identical
            // values on both faces. Without snapping, each face samples the noise ~0.5/res
            // inside its boundary at a slightly different direction, producing the small
            // value mismatch that bilinear filtering in the cloud-shadow shader amplifies
            // into a visible rectangular artifact at the cube-face seam.
            float u = EdgeSnappedUv(x, Resolution);
            float v = EdgeSnappedUv(y, Resolution);
            float3 direction = CubeFaceToUnitSphere(face, new float2(u, v));

            float largeFronts = Fbm(ref FrontNoise, direction * FrontScale, 5, 2.05f, 0.54f);
            float smallFronts = Fbm(ref DetailNoise, direction * FrontScale * 3.25f, 3, 2.2f, 0.48f);
            float latitudeWetness = 1f - NormalizedLatitude(direction);
            float climate = Fbm(ref ClimateNoise, direction * 2.35f, 4, 2.1f, 0.52f);
            float biomeBias = (latitudeWetness - 0.5f) * BiomeInfluence;
            float frontValue = math.saturate(largeFronts * 0.82f + smallFronts * 0.18f + biomeBias);
            float humidAir = math.saturate(latitudeWetness * 0.62f + climate * 0.38f);

            float cellCondensation = math.smoothstep(0f, 1f,
                Unlerp(CoverageThreshold - EdgeWidth, CoverageThreshold + EdgeWidth, frontValue));
            cellCondensation = math.pow(cellCondensation, 1.08f);
            float source = math.saturate(cellCondensation * 0.92f + humidAir * 0.08f);

            float stormSource = math.smoothstep(
                StormSourceThreshold,
                math.min(1f, StormSourceThreshold + StormSourceSoftness),
                source);
            stormSource = math.lerp(1f - StormMoistureBias, 1f, stormSource) * stormSource;
            float cellStorm = math.smoothstep(StormThreshold, 1f, cellCondensation) * stormSource;
            float initialPrecipitation = math.smoothstep(
                RainFormationThreshold,
                math.min(1f, RainFormationThreshold + RainFormationSoftness),
                cellStorm) * math.smoothstep(
                RainCloudThreshold,
                math.min(1f, RainCloudThreshold + 0.2f),
                cellCondensation) * humidAir * 0.22f;
            float initialRainRate = math.saturate(initialPrecipitation * cellStorm);

            Condensation[index] = cellCondensation;
            Storm[index] = cellStorm;
            MoistureSource[index] = source;
            Humidity[index] = humidAir;
            PrecipitationWater[index] = initialPrecipitation;
            RainRate[index] = initialRainRate;
            WeatherPixels[index] = new float4(cellCondensation, cellStorm, source, 0.5f);
            DynamicsPixels[index] = new float4(humidAir, initialPrecipitation, initialRainRate, humidAir);
        }

        static float EdgeSnappedUv(int idx, int resolution)
        {
            if (idx == 0) return 0f;
            if (idx == resolution - 1) return 1f;
            return (idx + 0.5f) / resolution;
        }

        static float3 CubeFaceLocalUp(int face)
        {
            // Matches CoordinateConverter.CubeFaceDirections in face order:
            // 0 up, 1 down, 2 left, 3 right, 4 forward, 5 back.
            switch (face)
            {
                case 0:  return new float3(0f, 1f, 0f);
                case 1:  return new float3(0f, -1f, 0f);
                case 2:  return new float3(-1f, 0f, 0f);
                case 3:  return new float3(1f, 0f, 0f);
                case 4:  return new float3(0f, 0f, 1f);
                default: return new float3(0f, 0f, -1f);
            }
        }

        static float3 CubeFaceToUnitSphere(int face, float2 uv)
        {
            float u = uv.x * 2f - 1f;
            float v = uv.y * 2f - 1f;
            float3 localUp = CubeFaceLocalUp(face);
            float3 axisA = new float3(localUp.y, localUp.z, localUp.x);
            float3 axisB = math.cross(localUp, axisA);
            float3 pointOnCube = localUp + u * axisA + v * axisB;
            return math.normalize(pointOnCube);
        }

        static float NormalizedLatitude(float3 p)
        {
            return math.abs(math.asin(math.clamp(p.y, -1f, 1f))) / (math.PI * 0.5f);
        }

        static float Unlerp(float a, float b, float x)
        {
            float d = b - a;
            return d != 0f ? math.saturate((x - a) / d) : 0f;
        }

        static float Fbm(ref NoiseData noise, float3 point, int octaves, float lacunarity, float persistence)
        {
            float sum = 0f;
            float amplitude = 1f;
            float amplitudeSum = 0f;
            float frequency = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float value = noise.Evaluate(point * frequency) * 0.5f + 0.5f;
                sum += value * amplitude;
                amplitudeSum += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            return amplitudeSum > 0f ? sum / amplitudeSum : 0f;
        }
    }

    /// <summary>
    /// Creates Unity textures from pre-computed grid data and constructs the grid object.
    /// Must be called on the main thread.
    /// </summary>
    static SphericalWeatherGrid UploadGridData(GridComputeData data, int seed)
    {
        int resolution = data.Resolution;

        var stagingTexture = new Texture2DArray(resolution, resolution, 6, TextureFormat.RGBAHalf, false, true)
        {
            name = $"CloudWeather_{resolution}_{seed}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 1
        };
        var dynamicsStagingTexture = new Texture2DArray(resolution, resolution, 6, TextureFormat.RGBAHalf, false, true)
        {
            name = $"WeatherDynamics_{resolution}_{seed}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 1
        };

        for (int face = 0; face < 6; face++)
        {
            stagingTexture.SetPixels(data.PixelsByFace[face], face);
            dynamicsStagingTexture.SetPixels(data.DynamicsPixelsByFace[face], face);
        }

        stagingTexture.Apply(false, false);
        dynamicsStagingTexture.Apply(false, false);

        var activeTexture = CreateWeatherTexture(resolution, $"CloudWeatherActive_{resolution}_{seed}");
        var scratchTexture = CreateWeatherTexture(resolution, $"CloudWeatherScratch_{resolution}_{seed}");
        var dynamicsActiveTexture = CreateWeatherTexture(resolution, $"WeatherDynamicsActive_{resolution}_{seed}");
        var dynamicsScratchTexture = CreateWeatherTexture(resolution, $"WeatherDynamicsScratch_{resolution}_{seed}");
        Graphics.CopyTexture(stagingTexture, activeTexture);
        Graphics.CopyTexture(stagingTexture, scratchTexture);
        Graphics.CopyTexture(dynamicsStagingTexture, dynamicsActiveTexture);
        Graphics.CopyTexture(dynamicsStagingTexture, dynamicsScratchTexture);

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(stagingTexture);
            UnityEngine.Object.Destroy(dynamicsStagingTexture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(stagingTexture);
            UnityEngine.Object.DestroyImmediate(dynamicsStagingTexture);
        }

        return new SphericalWeatherGrid(
            resolution,
            activeTexture,
            scratchTexture,
            dynamicsActiveTexture,
            dynamicsScratchTexture,
            data.Condensation,
            data.Storm,
            data.MoistureSource,
            data.Humidity,
            data.PrecipitationWater,
            data.RainRate);
    }

    /// <summary>
    /// Generates a weather grid asynchronously. Cell-value computation runs on Burst-compiled
    /// worker threads via <see cref="WeatherGridGenerationJob"/>; the main thread yields between
    /// frames until the job completes, then uploads textures.
    /// </summary>
    public static async Awaitable<SphericalWeatherGrid> GenerateAsync(
        CloudDto settings, int seed, System.Threading.CancellationToken ct = default)
    {
        // Job scheduling and texture upload both require the main thread.
        var state = ScheduleGridJob(settings, seed);
        try
        {
            while (!state.Handle.IsCompleted)
            {
                if (ct.IsCancellationRequested)
                {
                    state.Handle.Complete();
                    state.Dispose();
                    ct.ThrowIfCancellationRequested();
                }
                await Awaitable.NextFrameAsync();
            }
            state.Handle.Complete();
        }
        catch
        {
            state.Dispose();
            throw;
        }
        var data = CopyOutAndDispose(state);
        return UploadGridData(data, seed);
    }

    public static SphericalWeatherGrid Generate(CloudDto settings, int seed)
    {
        var state = ScheduleGridJob(settings, seed);
        state.Handle.Complete();
        var data = CopyOutAndDispose(state);
        return UploadGridData(data, seed);
    }

    public float GetCondensation(Vector3 worldPosition, Vector3 planetCenter, Quaternion sampleRotation)
    {
        GetCell(worldPosition, planetCenter, sampleRotation, out int face, out int x, out int y);
        return _condensation[GetIndex(face, x, y, Resolution)];
    }

    public float GetStorm(Vector3 worldPosition, Vector3 planetCenter, Quaternion sampleRotation)
    {
        GetCell(worldPosition, planetCenter, sampleRotation, out int face, out int x, out int y);
        return _storm[GetIndex(face, x, y, Resolution)];
    }

    public void GetWeatherCell(
        Vector3 worldPosition,
        Vector3 planetCenter,
        Quaternion sampleRotation,
        out float condensation,
        out float storm,
        out float moistureSource)
    {
        GetCell(worldPosition, planetCenter, sampleRotation, out int face, out int x, out int y);
        int index = GetIndex(face, x, y, Resolution);
        condensation = _condensation[index];
        storm = _storm[index];
        moistureSource = _moistureSource[index];
    }

    public void ApplyWeatherFaceReadback(int face, NativeArray<Color> pixels)
    {
        if (face < 0 || face >= 6)
            return;

        int faceCellCount = Resolution * Resolution;
        if (pixels.Length < faceCellCount)
            return;

        int baseIndex = face * faceCellCount;
        for (int i = 0; i < faceCellCount; i++)
        {
            Color pixel = pixels[i];
            _condensation[baseIndex + i] = Mathf.Clamp01(pixel.r);
            _storm[baseIndex + i] = Mathf.Clamp01(pixel.g);
            _moistureSource[baseIndex + i] = Mathf.Clamp01(pixel.b);
        }
    }

    public void ApplyDynamicsFaceReadback(int face, NativeArray<Color> pixels)
    {
        if (face < 0 || face >= 6)
            return;

        int faceCellCount = Resolution * Resolution;
        if (pixels.Length < faceCellCount)
            return;

        int baseIndex = face * faceCellCount;
        for (int i = 0; i < faceCellCount; i++)
        {
            Color pixel = pixels[i];
            _humidity[baseIndex + i] = Mathf.Clamp01(pixel.r);
            _precipitationWater[baseIndex + i] = Mathf.Clamp01(pixel.g);
            _rainRate[baseIndex + i] = Mathf.Clamp01(pixel.b);
        }
    }

    public bool TryFindStrongestStorm(
        out Vector3 weatherDirection,
        out float condensation,
        out float storm,
        out float moistureSource)
    {
        weatherDirection = Vector3.up;
        condensation = 0f;
        storm = 0f;
        moistureSource = 0f;

        if (_condensation.Length == 0 || _storm.Length == 0)
            return false;

        int bestIndex = -1;
        float bestScore = -1f;
        int faceCellCount = Resolution * Resolution;
        for (int i = 0; i < _storm.Length; i++)
        {
            float score = _storm[i] * 0.82f + _condensation[i] * 0.18f;
            if (score <= bestScore)
                continue;

            bestScore = score;
            bestIndex = i;
        }

        if (bestIndex < 0)
            return false;

        int face = bestIndex / faceCellCount;
        int faceIndex = bestIndex - face * faceCellCount;
        int x = faceIndex % Resolution;
        int y = faceIndex / Resolution;
        weatherDirection = CoordinateConverter.CubeFaceToUnitSphere(
            face,
            new Vector2((x + 0.5f) / Resolution, (y + 0.5f) / Resolution));
        condensation = _condensation[bestIndex];
        storm = _storm[bestIndex];
        moistureSource = _moistureSource[bestIndex];
        return true;
    }

    public WeatherGridStats CalculateStats(float cloudyThreshold, float stormThreshold, float rainThreshold)
    {
        int count = _condensation.Length;
        if (count == 0)
            return new WeatherGridStats(0, 0, 0, 0, 0, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, Vector3.up, 0f, 0f, 0f);

        double condensationSum = 0;
        double stormSum = 0;
        double moistureSum = 0;
        double rainSum = 0;
        float maxCondensation = 0f;
        float maxStorm = 0f;
        float maxMoisture = 0f;
        float maxRain = 0f;
        int cloudyCells = 0;
        int stormCells = 0;
        int rainCandidates = 0;
        int rainingCells = 0;
        int bestIndex = 0;
        float bestScore = -1f;

        for (int i = 0; i < count; i++)
        {
            float condensation = _condensation[i];
            float storm = _storm[i];
            float moisture = _moistureSource[i];
            float rain = _rainRate[i];

            condensationSum += condensation;
            stormSum += storm;
            moistureSum += moisture;
            rainSum += rain;
            maxCondensation = Mathf.Max(maxCondensation, condensation);
            maxStorm = Mathf.Max(maxStorm, storm);
            maxMoisture = Mathf.Max(maxMoisture, moisture);
            maxRain = Mathf.Max(maxRain, rain);

            if (condensation >= cloudyThreshold)
                cloudyCells++;
            if (storm >= stormThreshold)
                stormCells++;
            if (storm >= rainThreshold && condensation >= cloudyThreshold)
                rainCandidates++;
            if (rain >= 0.01f)
                rainingCells++;

            float score = storm * 0.82f + condensation * 0.18f;
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        int faceCellCount = Resolution * Resolution;
        int face = bestIndex / faceCellCount;
        int faceIndex = bestIndex - face * faceCellCount;
        int x = faceIndex % Resolution;
        int y = faceIndex / Resolution;
        Vector3 strongestDirection = CoordinateConverter.CubeFaceToUnitSphere(
            face,
            new Vector2((x + 0.5f) / Resolution, (y + 0.5f) / Resolution));

        float invCount = 1f / count;
        return new WeatherGridStats(
            count,
            cloudyCells,
            stormCells,
            rainCandidates,
            rainingCells,
            (float)condensationSum * invCount,
            (float)stormSum * invCount,
            (float)moistureSum * invCount,
            (float)rainSum * invCount,
            maxCondensation,
            maxStorm,
            maxMoisture,
            maxRain,
            strongestDirection,
            _condensation[bestIndex],
            _storm[bestIndex],
            _moistureSource[bestIndex]);
    }

    public bool Advance(ComputeShader compute, CloudDto settings, float deltaTime, Vector3 windDirection, float stepAngle)
    {
        if (compute == null || settings == null || !settings.EnableWeatherEvolution || deltaTime <= 0f)
            return false;

        int kernel = compute.FindKernel("CSEvolveWeather");
        compute.SetTexture(kernel, _weatherReadId, _activeTexture);
        compute.SetTexture(kernel, _weatherWriteId, _scratchTexture);
        compute.SetTexture(kernel, _dynamicsReadId, _dynamicsActiveTexture);
        compute.SetTexture(kernel, _dynamicsWriteId, _dynamicsScratchTexture);
        compute.SetInt(_resolutionId, Resolution);
        compute.SetFloat(_deltaTimeId, deltaTime);
        compute.SetFloat(_stormThresholdId, settings.StormThreshold);
        compute.SetFloat(_moistureSourceStrengthId, CloudConstants.MoistureSourceStrength);
        compute.SetFloat(_dryAirEvaporationRateId, CloudConstants.DryAirEvaporationRate);
        compute.SetFloat(_stormGrowthRateId, CloudConstants.StormGrowthRate);
        compute.SetFloat(_stormDecayRateId, CloudConstants.StormDecayRate);
        compute.SetFloat(_stormMoistureBiasId, CloudConstants.StormMoistureBias);
        compute.SetFloat(_stormSourceThresholdId, CloudConstants.StormSourceThreshold);
        compute.SetFloat(_stormSourceSoftnessId, CloudConstants.StormSourceSoftness);
        compute.SetFloat(_rainFormationThresholdId, CloudConstants.RainFormationThreshold);
        compute.SetFloat(_rainFormationSoftnessId, CloudConstants.RainFormationSoftness);
        compute.SetFloat(_rainCloudThresholdId, CloudConstants.RainCloudThreshold);
        compute.SetFloat(_precipitationBuildRateId, CloudConstants.PrecipitationBuildRate);
        compute.SetFloat(_precipitationDecayRateId, CloudConstants.PrecipitationDecayRate);
        compute.SetFloat(_rainOutRateId, CloudConstants.RainOutRate);
        compute.SetFloat(_humidityRecoveryRateId, CloudConstants.HumidityRecoveryRate);
        compute.SetFloat(_condensationRainDrainId, CloudConstants.CondensationRainDrain);
        compute.SetFloat(_deltaVisualizationScaleId, DeltaVisualizationScale);
        compute.SetVector(_windDirectionId, windDirection);
        compute.SetFloat(_stepAngleId, stepAngle);

        int groups = Mathf.CeilToInt(Resolution / 8f);
        compute.Dispatch(kernel, groups, groups, 6);
        (_activeTexture, _scratchTexture) = (_scratchTexture, _activeTexture);
        (_dynamicsActiveTexture, _dynamicsScratchTexture) = (_dynamicsScratchTexture, _dynamicsActiveTexture);
        return true;
    }

    public void Dispose()
    {
        ReleaseTexture(ref _activeTexture);
        ReleaseTexture(ref _scratchTexture);
        ReleaseTexture(ref _dynamicsActiveTexture);
        ReleaseTexture(ref _dynamicsScratchTexture);
    }

    void GetCell(Vector3 worldPosition, Vector3 planetCenter, Quaternion sampleRotation, out int face, out int x, out int y)
    {
        Vector3 fromCenter = worldPosition - planetCenter;
        Vector3 direction = fromCenter.sqrMagnitude > 0.000001f ? fromCenter.normalized : Vector3.up;
        direction = sampleRotation * direction;
        var faceUv = UnitSphereToWeatherCubeFace(direction);
        face = faceUv.face;
        x = Mathf.Clamp(Mathf.FloorToInt(faceUv.uv.x * Resolution), 0, Resolution - 1);
        y = Mathf.Clamp(Mathf.FloorToInt(faceUv.uv.y * Resolution), 0, Resolution - 1);
    }

    // Inverse of CubeFaceToUnitSphere above. Do not use CoordinateConverter.UnitSphereToCubeFace
    // here: its UV orientation is not the inverse of this weather grid's face axes.
    static (int face, Vector2 uv) UnitSphereToWeatherCubeFace(Vector3 direction)
    {
        float absX = Mathf.Abs(direction.x);
        float absY = Mathf.Abs(direction.y);
        float absZ = Mathf.Abs(direction.z);

        int face;
        if (absY >= absX && absY >= absZ)
            face = direction.y > 0f ? 0 : 1;
        else if (absX >= absY && absX >= absZ)
            face = direction.x > 0f ? 3 : 2;
        else
            face = direction.z > 0f ? 4 : 5;

        Vector3 localUp = face switch
        {
            0 => Vector3.up,
            1 => Vector3.down,
            2 => Vector3.left,
            3 => Vector3.right,
            4 => Vector3.forward,
            _ => Vector3.back,
        };
        Vector3 axisA = new(localUp.y, localUp.z, localUp.x);
        Vector3 axisB = Vector3.Cross(localUp, axisA);
        float major = Mathf.Max(Mathf.Abs(Vector3.Dot(direction, localUp)), 0.00001f);
        float u = Vector3.Dot(direction, axisA) / major;
        float v = Vector3.Dot(direction, axisB) / major;
        return (face, new Vector2(Mathf.Clamp01(u * 0.5f + 0.5f), Mathf.Clamp01(v * 0.5f + 0.5f)));
    }

    static int GetIndex(int face, int x, int y, int resolution)
    {
        return face * resolution * resolution + x + y * resolution;
    }

    // For cells on a face edge, returns the exact cube-edge UV (0 or 1). For interior cells,
    // returns the standard cell-centre UV. This makes the noise direction identical for the
    // edge cells of two adjacent faces sharing that cube edge - so their generated values
    // match, and the shader's bilinear filter doesn't have a value mismatch to amplify.
    static float EdgeSnappedUv(int index, int resolution)
    {
        if (index == 0) return 0f;
        if (index == resolution - 1) return 1f;
        return (index + 0.5f) / resolution;
    }

    static RenderTexture CreateWeatherTexture(int resolution, string name)
    {
        var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.ARGBHalf, 0)
        {
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = 6,
            enableRandomWrite = true,
            msaaSamples = 1,
            useMipMap = false
        };

        var texture = new RenderTexture(desc)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 1
        };
        texture.Create();
        return texture;
    }

    static void ReleaseTexture(ref RenderTexture texture)
    {
        if (texture == null) return;

        texture.Release();
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(texture);
        else
            UnityEngine.Object.DestroyImmediate(texture);

        texture = null;
    }

}
