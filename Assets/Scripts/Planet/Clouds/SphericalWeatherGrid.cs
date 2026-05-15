using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class SphericalWeatherGrid : IDisposable
{
    public int Resolution { get; }
    public Texture Texture => _activeTexture;

    public const float DeltaVisualizationScale = 16f;

    readonly float[] _condensation;
    readonly float[] _storm;
    readonly float[] _moistureSource;
    RenderTexture _activeTexture;
    RenderTexture _scratchTexture;

    static readonly int _weatherReadId = Shader.PropertyToID("_WeatherRead");
    static readonly int _weatherWriteId = Shader.PropertyToID("_WeatherWrite");
    static readonly int _resolutionId = Shader.PropertyToID("_Resolution");
    static readonly int _deltaTimeId = Shader.PropertyToID("_DeltaTime");
    static readonly int _stormThresholdId = Shader.PropertyToID("_StormThreshold");
    static readonly int _moistureSourceStrengthId = Shader.PropertyToID("_MoistureSourceStrength");
    static readonly int _dryAirEvaporationRateId = Shader.PropertyToID("_DryAirEvaporationRate");
    static readonly int _stormGrowthRateId = Shader.PropertyToID("_StormGrowthRate");
    static readonly int _stormDecayRateId = Shader.PropertyToID("_StormDecayRate");
    static readonly int _stormMoistureBiasId = Shader.PropertyToID("_StormMoistureBias");
    static readonly int _deltaVisualizationScaleId = Shader.PropertyToID("_DeltaVisualizationScale");
    static readonly int _weatherVisualRotationId = Shader.PropertyToID("_WeatherVisualRotation");

    SphericalWeatherGrid(
        int resolution,
        RenderTexture activeTexture,
        RenderTexture scratchTexture,
        float[] condensation,
        float[] storm,
        float[] moistureSource)
    {
        Resolution = resolution;
        _activeTexture = activeTexture;
        _scratchTexture = scratchTexture;
        _condensation = condensation;
        _storm = storm;
        _moistureSource = moistureSource;
    }

    public static SphericalWeatherGrid Generate(CloudSettings settings, int seed)
    {
        int resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(settings.WeatherResolution, 32, 512));
        int cellCount = resolution * resolution * 6;
        var condensation = new float[cellCount];
        var storm = new float[cellCount];
        var moistureSource = new float[cellCount];
        var stagingTexture = new Texture2DArray(resolution, resolution, 6, TextureFormat.RGBAHalf, false, true)
        {
            name = $"CloudWeather_{resolution}_{seed}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 1
        };

        var pixels = new Color[resolution * resolution];
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
                    float source = Mathf.Clamp01(latitudeWetness * 0.62f + climate * 0.38f);
                    float biomeBias = (latitudeWetness - 0.5f) * settings.BiomeInfluence;
                    float frontValue = Mathf.Clamp01(largeFronts * 0.82f + smallFronts * 0.18f + biomeBias);

                    float cellCondensation = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(coverageThreshold - edgeWidth, coverageThreshold + edgeWidth, frontValue));
                    cellCondensation = Mathf.Pow(cellCondensation, 1.08f);

                    float cellStorm = Mathf.SmoothStep(settings.StormThreshold, 1f, cellCondensation);
                    int gridIndex = GetIndex(face, x, y, resolution);
                    condensation[gridIndex] = cellCondensation;
                    storm[gridIndex] = cellStorm;
                    moistureSource[gridIndex] = source;
                    pixels[pixelIndex] = new Color(cellCondensation, cellStorm, source, 0.5f);
                }
            }

            stagingTexture.SetPixels(pixels, face);
        }

        stagingTexture.Apply(false, false);

        var activeTexture = CreateWeatherTexture(resolution, $"CloudWeatherActive_{resolution}_{seed}");
        var scratchTexture = CreateWeatherTexture(resolution, $"CloudWeatherScratch_{resolution}_{seed}");
        Graphics.CopyTexture(stagingTexture, activeTexture);
        Graphics.CopyTexture(stagingTexture, scratchTexture);

        if (Application.isPlaying)
            UnityEngine.Object.Destroy(stagingTexture);
        else
            UnityEngine.Object.DestroyImmediate(stagingTexture);

        return new SphericalWeatherGrid(resolution, activeTexture, scratchTexture, condensation, storm, moistureSource);
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

    public bool Advance(ComputeShader compute, CloudSettings settings, float deltaTime, Quaternion visualRotation)
    {
        if (compute == null || settings == null || !settings.EnableWeatherEvolution || deltaTime <= 0f)
            return false;

        int kernel = compute.FindKernel("CSEvolveWeather");
        compute.SetTexture(kernel, _weatherReadId, _activeTexture);
        compute.SetTexture(kernel, _weatherWriteId, _scratchTexture);
        compute.SetInt(_resolutionId, Resolution);
        compute.SetFloat(_deltaTimeId, deltaTime);
        compute.SetFloat(_stormThresholdId, settings.StormThreshold);
        compute.SetFloat(_moistureSourceStrengthId, settings.ActiveMoistureSourceStrength);
        compute.SetFloat(_dryAirEvaporationRateId, settings.ActiveDryAirEvaporationRate);
        compute.SetFloat(_stormGrowthRateId, settings.ActiveStormGrowthRate);
        compute.SetFloat(_stormDecayRateId, settings.ActiveStormDecayRate);
        compute.SetFloat(_stormMoistureBiasId, settings.StormMoistureBias);
        compute.SetFloat(_deltaVisualizationScaleId, DeltaVisualizationScale);
        compute.SetMatrix(_weatherVisualRotationId, Matrix4x4.Rotate(visualRotation));

        int groups = Mathf.CeilToInt(Resolution / 8f);
        compute.Dispatch(kernel, groups, groups, 6);
        (_activeTexture, _scratchTexture) = (_scratchTexture, _activeTexture);
        return true;
    }

    public void Dispose()
    {
        ReleaseTexture(ref _activeTexture);
        ReleaseTexture(ref _scratchTexture);
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
