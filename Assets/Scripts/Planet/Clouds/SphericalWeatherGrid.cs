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
    static readonly int _deltaTimeId = Shader.PropertyToID("_DeltaTime");
    static readonly int _stormThresholdId = Shader.PropertyToID("_StormThreshold");
    static readonly int _moistureSourceStrengthId = Shader.PropertyToID("_MoistureSourceStrength");
    static readonly int _dryAirEvaporationRateId = Shader.PropertyToID("_DryAirEvaporationRate");
    static readonly int _stormGrowthRateId = Shader.PropertyToID("_StormGrowthRate");
    static readonly int _stormDecayRateId = Shader.PropertyToID("_StormDecayRate");
    static readonly int _stormMoistureBiasId = Shader.PropertyToID("_StormMoistureBias");
    static readonly int _rainFormationThresholdId = Shader.PropertyToID("_RainFormationThreshold");
    static readonly int _rainFormationSoftnessId = Shader.PropertyToID("_RainFormationSoftness");
    static readonly int _precipitationBuildRateId = Shader.PropertyToID("_PrecipitationBuildRate");
    static readonly int _precipitationDecayRateId = Shader.PropertyToID("_PrecipitationDecayRate");
    static readonly int _rainOutRateId = Shader.PropertyToID("_RainOutRate");
    static readonly int _humidityRecoveryRateId = Shader.PropertyToID("_HumidityRecoveryRate");
    static readonly int _condensationRainDrainId = Shader.PropertyToID("_CondensationRainDrain");
    static readonly int _deltaVisualizationScaleId = Shader.PropertyToID("_DeltaVisualizationScale");
    static readonly int _weatherVisualRotationId = Shader.PropertyToID("_WeatherVisualRotation");

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

    public static SphericalWeatherGrid Generate(CloudSettings settings, int seed)
    {
        int resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(settings.WeatherResolution, 32, 512));
        int cellCount = resolution * resolution * 6;
        var condensation = new float[cellCount];
        var storm = new float[cellCount];
        var moistureSource = new float[cellCount];
        var humidity = new float[cellCount];
        var precipitationWater = new float[cellCount];
        var rainRate = new float[cellCount];
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

        var pixels = new Color[resolution * resolution];
        var dynamicsPixels = new Color[resolution * resolution];
        var frontNoise = new Noise(seed);
        var detailNoise = new Noise(seed + 7919);
        var climateNoise = new Noise(seed + 104729);
        float coverageThreshold = Mathf.Lerp(0.84f, 0.18f, settings.InitialCoverage);
        float edgeWidth = Mathf.Clamp(1f / Mathf.Max(1f, settings.FrontSharpness), 0.015f, 0.25f);

        for (int face = 0; face < 6; face++)
        {
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int pixelIndex = x + y * resolution;
                    Vector2 uv = new Vector2((x + 0.5f) / resolution, (y + 0.5f) / resolution);
                    Vector3 direction = CoordinateConverter.CubeFaceToUnitSphere(face, uv);

                    float largeFronts = Fbm(frontNoise, direction * settings.FrontScale, 5, 2.05f, 0.54f);
                    float smallFronts = Fbm(detailNoise, direction * settings.FrontScale * 3.25f, 3, 2.2f, 0.48f);
                    float latitudeWetness = 1f - CoordinateConverter.NormalizedLatitude(direction);
                    float climate = Fbm(climateNoise, direction * 2.35f, 4, 2.1f, 0.52f);
                    float biomeBias = (latitudeWetness - 0.5f) * settings.BiomeInfluence;
                    float frontValue = Mathf.Clamp01(largeFronts * 0.82f + smallFronts * 0.18f + biomeBias);
                    float humidAir = Mathf.Clamp01(latitudeWetness * 0.62f + climate * 0.38f);

                    float cellCondensation = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(coverageThreshold - edgeWidth, coverageThreshold + edgeWidth, frontValue));
                    cellCondensation = Mathf.Pow(cellCondensation, 1.08f);
                    float source = Mathf.Clamp01(cellCondensation * 0.92f + humidAir * 0.08f);

                    float cellStorm = Mathf.SmoothStep(settings.StormThreshold, 1f, cellCondensation);
                    float initialPrecipitation = Mathf.SmoothStep(
                        settings.RainFormationThreshold,
                        Mathf.Min(1f, settings.RainFormationThreshold + settings.RainFormationSoftness),
                        cellStorm) * Mathf.SmoothStep(0.52f, 0.92f, cellCondensation) * humidAir * 0.35f;
                    float initialRainRate = Mathf.Clamp01(initialPrecipitation * cellStorm);
                    int gridIndex = GetIndex(face, x, y, resolution);
                    condensation[gridIndex] = cellCondensation;
                    storm[gridIndex] = cellStorm;
                    moistureSource[gridIndex] = source;
                    humidity[gridIndex] = humidAir;
                    precipitationWater[gridIndex] = initialPrecipitation;
                    rainRate[gridIndex] = initialRainRate;
                    pixels[pixelIndex] = new Color(cellCondensation, cellStorm, source, 0.5f);
                    dynamicsPixels[pixelIndex] = new Color(humidAir, initialPrecipitation, initialRainRate, humidAir);
                }
            }

            stagingTexture.SetPixels(pixels, face);
            dynamicsStagingTexture.SetPixels(dynamicsPixels, face);
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
            condensation,
            storm,
            moistureSource,
            humidity,
            precipitationWater,
            rainRate);
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

    public bool Advance(ComputeShader compute, CloudSettings settings, float deltaTime, Quaternion visualRotation)
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
        compute.SetFloat(_moistureSourceStrengthId, settings.ActiveMoistureSourceStrength);
        compute.SetFloat(_dryAirEvaporationRateId, settings.ActiveDryAirEvaporationRate);
        compute.SetFloat(_stormGrowthRateId, settings.ActiveStormGrowthRate);
        compute.SetFloat(_stormDecayRateId, settings.ActiveStormDecayRate);
        compute.SetFloat(_stormMoistureBiasId, settings.StormMoistureBias);
        compute.SetFloat(_rainFormationThresholdId, settings.RainFormationThreshold);
        compute.SetFloat(_rainFormationSoftnessId, settings.RainFormationSoftness);
        compute.SetFloat(_precipitationBuildRateId, settings.PrecipitationBuildRate);
        compute.SetFloat(_precipitationDecayRateId, settings.PrecipitationDecayRate);
        compute.SetFloat(_rainOutRateId, settings.RainOutRate);
        compute.SetFloat(_humidityRecoveryRateId, settings.HumidityRecoveryRate);
        compute.SetFloat(_condensationRainDrainId, settings.CondensationRainDrain);
        compute.SetFloat(_deltaVisualizationScaleId, DeltaVisualizationScale);
        compute.SetMatrix(_weatherVisualRotationId, Matrix4x4.Rotate(visualRotation));

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
        var faceUv = CoordinateConverter.UnitSphereToCubeFace(direction);
        face = faceUv.face;
        x = Mathf.Clamp(Mathf.FloorToInt(faceUv.uv.x * Resolution), 0, Resolution - 1);
        y = Mathf.Clamp(Mathf.FloorToInt(faceUv.uv.y * Resolution), 0, Resolution - 1);
    }

    static int GetIndex(int face, int x, int y, int resolution)
    {
        return face * resolution * resolution + x + y * resolution;
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

    static float Fbm(Noise noise, Vector3 point, int octaves, float lacunarity, float persistence)
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
