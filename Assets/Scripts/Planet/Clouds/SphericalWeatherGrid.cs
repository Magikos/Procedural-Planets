using System;
using Unity.Collections;
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
    static readonly int _permutationsId = Shader.PropertyToID("_Permutations");
    static readonly int _frontScaleId = Shader.PropertyToID("_FrontScale");
    static readonly int _biomeInfluenceId = Shader.PropertyToID("_BiomeInfluence");
    static readonly int _coverageThresholdId = Shader.PropertyToID("_CoverageThreshold");
    static readonly int _edgeWidthId = Shader.PropertyToID("_EdgeWidth");
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

    /// <summary>
    /// Generates a weather grid by dispatching CSInitWeather, writing directly into the
    /// ping-pong RenderTextures. Eliminates the staging Texture2DArray upload path.
    /// CPU-side cell arrays start empty and are populated progressively via async GPU readback.
    /// </summary>
    public static async Awaitable<SphericalWeatherGrid> GenerateComputeAsync(
        ComputeShader compute, CloudDto settings, int seed,
        System.Threading.CancellationToken ct = default)
    {
        int resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(settings.WeatherResolution, 32, 512));
        int cellCount  = resolution * resolution * 6;

        var activeTexture          = CreateWeatherTexture(resolution, $"CloudWeatherActive_{resolution}_{seed}");
        var scratchTexture         = CreateWeatherTexture(resolution, $"CloudWeatherScratch_{resolution}_{seed}");
        var dynamicsActiveTexture  = CreateWeatherTexture(resolution, $"WeatherDynamicsActive_{resolution}_{seed}");
        var dynamicsScratchTexture = CreateWeatherTexture(resolution, $"WeatherDynamicsScratch_{resolution}_{seed}");

        // Build concatenated permutation table: [0..511]=front, [512..1023]=detail, [1024..1535]=climate.
        var frontData   = NoiseData.Create(seed);
        var detailData  = NoiseData.Create(seed + 7919);
        var climateData = NoiseData.Create(seed + 104729);
        var permArray   = new int[NoiseData.PermutationSize * 2 * 3];
        frontData.CopyPermutation(permArray,    0);
        detailData.CopyPermutation(permArray,  NoiseData.PermutationSize * 2);
        climateData.CopyPermutation(permArray, NoiseData.PermutationSize * 4);

        using var permBuffer = new ComputeBuffer(permArray.Length, sizeof(int));
        permBuffer.SetData(permArray);

        float coverageThreshold = Mathf.Lerp(0.84f, 0.18f, settings.InitialCoverage);
        float edgeWidth         = Mathf.Clamp(1f / Mathf.Max(1f, CloudConstants.FrontSharpness), 0.015f, 0.25f);

        int kernel = compute.FindKernel("CSInitWeather");
        compute.SetBuffer(kernel, _permutationsId,     permBuffer);
        compute.SetTexture(kernel, _weatherWriteId,    activeTexture);
        compute.SetTexture(kernel, _dynamicsWriteId,   dynamicsActiveTexture);
        compute.SetInt(_resolutionId,                  resolution);
        compute.SetFloat(_frontScaleId,                CloudConstants.FrontScale);
        compute.SetFloat(_biomeInfluenceId,            CloudConstants.BiomeInfluence);
        compute.SetFloat(_coverageThresholdId,         coverageThreshold);
        compute.SetFloat(_edgeWidthId,                 edgeWidth);
        compute.SetFloat(_stormSourceThresholdId,      CloudConstants.StormSourceThreshold);
        compute.SetFloat(_stormSourceSoftnessId,       CloudConstants.StormSourceSoftness);
        compute.SetFloat(_stormMoistureBiasId,         CloudConstants.StormMoistureBias);
        compute.SetFloat(_stormThresholdId,            settings.StormThreshold);
        compute.SetFloat(_rainFormationThresholdId,    CloudConstants.RainFormationThreshold);
        compute.SetFloat(_rainFormationSoftnessId,     CloudConstants.RainFormationSoftness);
        compute.SetFloat(_rainCloudThresholdId,        CloudConstants.RainCloudThreshold);

        int groups = Mathf.CeilToInt(resolution / 8f);
        compute.Dispatch(kernel, groups, groups, 6);

        // Seed the scratch (ping-pong twin) from the freshly written active texture.
        Graphics.CopyTexture(activeTexture,         scratchTexture);
        Graphics.CopyTexture(dynamicsActiveTexture, dynamicsScratchTexture);

        await Awaitable.NextFrameAsync(ct);

        // CPU cell arrays start empty. WeatherManager's async GPU readback populates them
        // progressively (one face per WeatherQueryCacheInterval). SampleWeather falls back
        // to InitialCoverage until readback completes, which is the existing null-grid path.
        return new SphericalWeatherGrid(
            resolution,
            activeTexture,
            scratchTexture,
            dynamicsActiveTexture,
            dynamicsScratchTexture,
            new float[cellCount],
            new float[cellCount],
            new float[cellCount],
            new float[cellCount],
            new float[cellCount],
            new float[cellCount]);
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
