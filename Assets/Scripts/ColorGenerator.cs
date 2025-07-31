using UnityEngine;

public class ColorGenerator
{
    public ColorSettings settings;
    Texture2D texture;
    const int textureResolution = 50;

    float elevationMin;
    float elevationMax;

    ShapeGenerator shapeGenerator;

    public ColorGenerator(ColorSettings settings, ShapeGenerator shapeGenerator)
    {
        this.settings = settings;
        this.shapeGenerator = shapeGenerator;
        texture = new Texture2D(textureResolution, 1);
    }

    public void UpdateElevation(float min, float max)
    {
        this.elevationMin = min;
        this.elevationMax = max;
        UpdateColors();
    }

    public void UpdateColors()
    {
        Color[] colors = new Color[textureResolution];

        if (settings?.biomeColorSettings?.biomes != null && settings.biomeColorSettings.biomes.Length > 0)
        {
            for (int i = 0; i < textureResolution; i++)
            {
                float heightPercent = i / (textureResolution - 1f);
                colors[i] = CalculateColorForHeight(heightPercent);
            }
        }
        else
        {
            // Fallback gradient from blue (low) to green (mid) to brown (high)
            for (int i = 0; i < textureResolution; i++)
            {
                float t = i / (textureResolution - 1f);
                if (t < 0.3f)
                {
                    // Ocean: Blue to light blue
                    colors[i] = Color.Lerp(new Color(0.1f, 0.3f, 0.8f), new Color(0.3f, 0.5f, 1f), t / 0.3f);
                }
                else if (t < 0.7f)
                {
                    // Plains: Light blue to green
                    float localT = (t - 0.3f) / 0.4f;
                    colors[i] = Color.Lerp(new Color(0.3f, 0.5f, 1f), new Color(0.2f, 0.7f, 0.2f), localT);
                }
                else
                {
                    // Mountains: Green to brown/white
                    float localT = (t - 0.7f) / 0.3f;
                    colors[i] = Color.Lerp(new Color(0.2f, 0.7f, 0.2f), new Color(0.8f, 0.6f, 0.4f), localT);
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply();

        // Note: PlanetVertexColor shader uses vertex colors, not textures
        // The texture generation is kept for potential future use or other materials
        if (settings?.planetMaterial != null)
        {
            settings.planetMaterial.SetTexture("_MainTex", texture);
        }
    }

    public Color CalculateColorForHeight(float heightPercent)
    {
        if (settings?.biomeColorSettings?.biomes == null || settings.biomeColorSettings.biomes.Length == 0)
        {
            // Fallback color calculation
            if (heightPercent < 0.3f) return new Color(0.1f, 0.3f, 0.8f); // Blue
            else if (heightPercent < 0.7f) return new Color(0.2f, 0.7f, 0.2f); // Green
            else return new Color(0.8f, 0.6f, 0.4f); // Brown
        }

        var biomeSettings = settings.biomeColorSettings;
        float biomeIndex = 0;
        int numBiomes = biomeSettings.biomes.Length;
        float blendRange = biomeSettings.blendAmount / 2f + 0.001f;

        // Calculate which biome we're in based on height
        for (int bi = 0; bi < numBiomes; bi++)
        {
            var biome = biomeSettings.biomes[bi];
            float dst = heightPercent - biome.startHeight;
            float weight = Mathf.InverseLerp(-blendRange, blendRange, dst);
            biomeIndex += weight * bi;
        }

        biomeIndex = Mathf.Clamp(biomeIndex, 0, numBiomes - 1);

        int biome1 = Mathf.FloorToInt(biomeIndex);
        int biome2 = Mathf.Min(biome1 + 1, numBiomes - 1);
        float blend = biomeIndex - biome1;

        Color color1 = biomeSettings.biomes[biome1].gradient.Evaluate(heightPercent);
        Color tint1 = biomeSettings.biomes[biome1].tint;
        color1 = Color.Lerp(color1, tint1, biomeSettings.biomes[biome1].tintPercent);

        Color color2 = biomeSettings.biomes[biome2].gradient.Evaluate(heightPercent);
        Color tint2 = biomeSettings.biomes[biome2].tint;
        color2 = Color.Lerp(color2, tint2, biomeSettings.biomes[biome2].tintPercent);

        Color finalColor = Color.Lerp(color1, color2, blend);

        return finalColor;
    }
}