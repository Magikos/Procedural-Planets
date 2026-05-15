using System;
using UnityEngine;

public sealed class SphericalWeatherGrid : IDisposable
{
    public int Resolution { get; }
    public Texture2DArray Texture { get; }

    readonly float[] _condensation;
    readonly float[] _storm;

    SphericalWeatherGrid(int resolution, Texture2DArray texture, float[] condensation, float[] storm)
    {
        Resolution = resolution;
        Texture = texture;
        _condensation = condensation;
        _storm = storm;
    }

    public static SphericalWeatherGrid Generate(CloudSettings settings, int seed)
    {
        int resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(settings.WeatherResolution, 32, 512));
        int cellCount = resolution * resolution * 6;
        var condensation = new float[cellCount];
        var storm = new float[cellCount];
        var texture = new Texture2DArray(resolution, resolution, 6, TextureFormat.RGBAHalf, false, true)
        {
            name = $"CloudWeather_{resolution}_{seed}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 1
        };

        var pixels = new Color[resolution * resolution];
        var frontNoise = new Noise(seed);
        var detailNoise = new Noise(seed + 7919);
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
                    float biomeBias = (latitudeWetness - 0.5f) * settings.BiomeInfluence;
                    float frontValue = Mathf.Clamp01(largeFronts * 0.82f + smallFronts * 0.18f + biomeBias);

                    float cellCondensation = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(coverageThreshold - edgeWidth, coverageThreshold + edgeWidth, frontValue));
                    cellCondensation = Mathf.Pow(cellCondensation, 1.08f);

                    float cellStorm = Mathf.SmoothStep(settings.StormThreshold, 1f, cellCondensation);
                    int gridIndex = GetIndex(face, x, y, resolution);
                    condensation[gridIndex] = cellCondensation;
                    storm[gridIndex] = cellStorm;
                    pixels[pixelIndex] = new Color(cellCondensation, cellStorm, latitudeWetness, 1f);
                }
            }

            texture.SetPixels(pixels, face);
        }

        texture.Apply(false, true);
        return new SphericalWeatherGrid(resolution, texture, condensation, storm);
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

    public void Dispose()
    {
        if (Texture == null) return;
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(Texture);
        else
            UnityEngine.Object.DestroyImmediate(Texture);
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
