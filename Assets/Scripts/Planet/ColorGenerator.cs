using UnityEngine;

public class ColorGenerator : IBiomeProvider, IColorProvider
{
    const int TextureResolution = 50;
    Texture2D _texture;
    ColorSettings _colorSettings;
    INoiseFilter _biomeNoiseFilter;

    public Texture2D BiomeTexture => _texture;

    public void Initialize(ColorSettings settings, int seed)
    {
        _colorSettings = settings;

        int biomeCount = Mathf.Max(1, _colorSettings.BiomeSettings.Biomes.Length);
        if (_texture == null || _texture.width != TextureResolution * 2 || _texture.height != biomeCount)
            _texture = new Texture2D(TextureResolution * 2, biomeCount, TextureFormat.RGBA32, false);

        _biomeNoiseFilter = NoiseFilterFactory.CreateNoiseFilter(_colorSettings.BiomeSettings.NoiseSettings, seed);
    }

    public void UpdateElevation(MinMax elevationMinMax)
    {
        _colorSettings.PlanetMaterial.SetVector("_ElevationMinMax",
            new Vector4(elevationMinMax.Min, elevationMinMax.Max));
    }

    public BiomeResult EvaluateBiome(Vector3 pointOnUnitSphere, float elevation)
    {
        float heightPercent = (pointOnUnitSphere.y + 1) / 2;
        heightPercent += (_biomeNoiseFilter.Evaluate(pointOnUnitSphere)
            - _colorSettings.BiomeSettings.NoiseOffset) * _colorSettings.BiomeSettings.NoiseStrength;

        int biomeCount = _colorSettings.BiomeSettings.Biomes.Length;
        float blendRange = _colorSettings.BiomeSettings.BlendAmount * 0.5f + 0.001f;

        float biomeIndex = 0;
        for (int i = 0; i < biomeCount; i++)
        {
            float distanceToBiomeStart = heightPercent - _colorSettings.BiomeSettings.Biomes[i].StartHeight;
            float weight = Mathf.InverseLerp(-blendRange, blendRange, distanceToBiomeStart);
            biomeIndex *= 1 - weight;
            biomeIndex += i * weight;
        }

        int primaryIndex = Mathf.Clamp(Mathf.RoundToInt(biomeIndex), 0, biomeCount - 1);
        return new BiomeResult((BiomeType)primaryIndex, heightPercent, 0f);
    }

    public float BiomePercentFromPoint(Vector3 pointOnUnitSphere)
    {
        float heightPercent = (pointOnUnitSphere.y + 1) / 2;
        heightPercent += (_biomeNoiseFilter.Evaluate(pointOnUnitSphere)
            - _colorSettings.BiomeSettings.NoiseOffset) * _colorSettings.BiomeSettings.NoiseStrength;

        float biomeIndex = 0;
        int biomeCount = _colorSettings.BiomeSettings.Biomes.Length;
        float blendRange = _colorSettings.BiomeSettings.BlendAmount * 0.5f + 0.001f;

        for (int i = 0; i < biomeCount; i++)
        {
            float distanceToBiomeStart = heightPercent - _colorSettings.BiomeSettings.Biomes[i].StartHeight;
            float weight = Mathf.InverseLerp(-blendRange, blendRange, distanceToBiomeStart);
            biomeIndex *= 1 - weight;
            biomeIndex += i * weight;
        }

        return biomeIndex / Mathf.Max(1, biomeCount - 1);
    }

    public void UpdateColors()
    {
        Color[] colors = new Color[_texture.width * _texture.height];
        int colorIndex = 0;
        foreach (var biome in _colorSettings.BiomeSettings.Biomes)
        {
            for (int i = 0; i < TextureResolution * 2; i++)
            {
                Color gradientColor;
                if (i < TextureResolution)
                    gradientColor = _colorSettings.OceanColorGradient.Evaluate(i / (TextureResolution - 1f));
                else
                    gradientColor = biome.ColorGradient.Evaluate((i - TextureResolution) / (TextureResolution - 1f));

                Color tintColor = biome.TintColor;
                colors[colorIndex] = gradientColor * (1 - biome.TintPercent) + tintColor * biome.TintPercent;
                colorIndex++;
            }
        }

        _texture.SetPixels(colors);
        _texture.Apply();
        _colorSettings.PlanetMaterial.SetTexture("_Texture", _texture);
    }
}
