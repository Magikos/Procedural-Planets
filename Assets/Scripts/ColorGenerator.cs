using System;
using UnityEngine;
public class ColorGenerator
{
    const int textureResolution = 50;
    Texture2D _texture;
    ColorSettings _colorSettings;
    INoiseFilter _biomeNoiseFilter;

    public void UpdateSettings(ColorSettings colorSettings)
    {
        _colorSettings = colorSettings;

        int biomeCount = Mathf.Max(1, _colorSettings.BiomeSettings.Biomes.Length);
        if (_texture == null || _texture.width != textureResolution || _texture.height != biomeCount) _texture = new Texture2D(textureResolution * 2, biomeCount, TextureFormat.RGBA32, false);
        _biomeNoiseFilter = NoiseFilterFactory.CreateNoiseFilter(_colorSettings.BiomeSettings.NoiseSettings);
    }

    public void UpdateElevation(MinMax elevationMinMax)
    {
        _colorSettings.PlanetMaterial.SetVector("_ElevationMinMax", new Vector4(elevationMinMax.Min, elevationMinMax.Max));
    }

    public float BiomePercentFromPoint(Vector3 pointOnUnitySphere)
    {
        float heightPercent = (pointOnUnitySphere.y + 1) / 2;
        heightPercent += (_biomeNoiseFilter.Evaluate(pointOnUnitySphere) - _colorSettings.BiomeSettings.NoiseOffset) * _colorSettings.BiomeSettings.NoiseStrength;

        float biomeIndex = 0;
        int biomeCount = _colorSettings.BiomeSettings.Biomes.Length;
        float blendRange = _colorSettings.BiomeSettings.BlendAmount * .5f + .001f;

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
            for (int i = 0; i < textureResolution * 2; i++)
            {
                Color gradientColor;
                if (i < textureResolution)
                {
                    gradientColor = _colorSettings.OceanColorGradient.Evaluate(i / (textureResolution - 1f));
                }
                else
                {
                    gradientColor = biome.ColorGradient.Evaluate((i - textureResolution) / (textureResolution - 1f));
                }

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