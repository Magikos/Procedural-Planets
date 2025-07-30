using UnityEngine;

public class ColorGenerator
{
    public ColorSettings settings;
    Texture2D texture;
    const int textureResolution = 50;

    ShapeGenerator shapeGenerator;

    public ColorGenerator(ColorSettings settings, ShapeGenerator shapeGenerator)
    {
        this.settings = settings;
        this.shapeGenerator = shapeGenerator;
        texture = new Texture2D(textureResolution, 1);
    }

    public void UpdateElevation(float min, float max)
    {
        // Update material with min/max if needed later
    }

    public void UpdateColors()
    {
        Color[] colors = new Color[textureResolution];
        for (int i = 0; i < textureResolution; i++)
        {
            // Use first biome's gradient for stub; fix fully later
            if (settings.biomeColorSettings.biomes.Length > 0)
            {
                colors[i] = settings.biomeColorSettings.biomes[0].gradient.Evaluate(i / (textureResolution - 1f));
            }
            else
            {
                colors[i] = Color.white; // Fallback
            }
        }
        texture.SetPixels(colors);
        texture.Apply();
        settings.planetMaterial.SetTexture("_texture", texture);
    }

    // TODO: Expand for full biomes when fixing colors
}