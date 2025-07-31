// Editor/GradientBaker.cs
using UnityEditor;
using UnityEngine;

public static class GradientBaker
{
    [MenuItem("Tools/Planet/Bake Biome Gradient")]
    public static void BakeBiomeGradient()
    {
        var planet = Selection.activeGameObject?.GetComponent<Planet>();
        if (planet == null || planet.colorSettings == null || planet.shapeSettings == null)
        {
            Debug.LogWarning("Select a Planet GameObject with both ShapeSettings and ColorSettings.");
            return;
        }

        var shapeGen = new ShapeGenerator(planet.shapeSettings);
        var colorGen = new ColorGenerator(planet.colorSettings, shapeGen);
        colorGen.UpdateColors();

        int width = 256;
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false);

        for (int x = 0; x < width; x++)
        {
            // t is the normalized height [0,1]
            float t = x / (float)(width - 1);
            // Ask the ColorGenerator for the biome–blended color
            Color col = colorGen.CalculateColorForHeight(t);
            tex.SetPixel(x, 0, col);
        }
        tex.Apply();

        string path = "Assets/PlanetBiomeGradient.png";
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)TextureImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Bilinear;
        imp.mipmapEnabled = false;
        EditorUtility.SetDirty(imp);
        AssetDatabase.WriteImportSettingsIfDirty(path);

        Debug.Log($"Baked biome gradient ({width}×1) to {path}");
    }
}
