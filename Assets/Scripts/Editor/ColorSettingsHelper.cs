using UnityEngine;
using UnityEditor;

public class ColorSettingsHelper : EditorWindow
{
    [MenuItem("Tools/Color Settings Helper")]
    public static void ShowWindow()
    {
        GetWindow<ColorSettingsHelper>("Color Settings Helper");
    }

    void OnGUI()
    {
        GUILayout.Label("Color Settings Helper", EditorStyles.boldLabel);
        GUILayout.Space(10);

        if (GUILayout.Button("🌍 Create Earth-like Colors"))
        {
            CreateEarthLikeColors();
        }

        if (GUILayout.Button("🌙 Create Moon-like Colors"))
        {
            CreateMoonLikeColors();
        }

        if (GUILayout.Button("🏔️ Create Mountain Colors"))
        {
            CreateMountainColors();
        }

        if (GUILayout.Button("🏜️ Create Desert Colors"))
        {
            CreateDesertColors();
        }

        if (GUILayout.Button("👽 Create Alien Colors"))
        {
            CreateAlienColors();
        }

        GUILayout.Space(10);
        GUILayout.Label("Instructions:", EditorStyles.boldLabel);
        GUILayout.Label("• Creates ColorSettings with realistic biomes");
        GUILayout.Label("• Assign to your Planet's colorSettings field");
        GUILayout.Label("• Make sure you have a material assigned");
    }

    void CreateEarthLikeColors()
    {
        ColorSettings settings = CreateInstance<ColorSettings>();
        settings.biomeColorSettings = new ColorSettings.BiomeColorSettings();
        settings.biomeColorSettings.biomes = new ColorSettings.BiomeColorSettings.Biome[3];
        settings.biomeColorSettings.blendAmount = 0.1f;

        // Ocean biome (0-30% height)
        settings.biomeColorSettings.biomes[0] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[0].startHeight = 0f;
        settings.biomeColorSettings.biomes[0].tintPercent = 0.2f;
        settings.biomeColorSettings.biomes[0].tint = new Color(0.1f, 0.4f, 0.8f);
        settings.biomeColorSettings.biomes[0].gradient = CreateOceanGradient();

        // Plains biome (30-70% height)
        settings.biomeColorSettings.biomes[1] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[1].startHeight = 0.3f;
        settings.biomeColorSettings.biomes[1].tintPercent = 0.3f;
        settings.biomeColorSettings.biomes[1].tint = new Color(0.2f, 0.7f, 0.2f);
        settings.biomeColorSettings.biomes[1].gradient = CreatePlainsGradient();

        // Mountain biome (70%+ height)
        settings.biomeColorSettings.biomes[2] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[2].startHeight = 0.7f;
        settings.biomeColorSettings.biomes[2].tintPercent = 0.1f;
        settings.biomeColorSettings.biomes[2].tint = new Color(0.8f, 0.8f, 0.8f);
        settings.biomeColorSettings.biomes[2].gradient = CreateMountainGradient();

        SaveColorAsset(settings, "EarthLike_ColorSettings.asset");
        Debug.Log("Created Earth-like ColorSettings with ocean, plains, and mountain biomes!");
    }

    void CreateMoonLikeColors()
    {
        ColorSettings settings = CreateInstance<ColorSettings>();
        settings.biomeColorSettings = new ColorSettings.BiomeColorSettings();
        settings.biomeColorSettings.biomes = new ColorSettings.BiomeColorSettings.Biome[2];
        settings.biomeColorSettings.blendAmount = 0.05f;

        // Low areas (0-50% height)
        settings.biomeColorSettings.biomes[0] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[0].startHeight = 0f;
        settings.biomeColorSettings.biomes[0].tintPercent = 0.1f;
        settings.biomeColorSettings.biomes[0].tint = new Color(0.4f, 0.4f, 0.4f);
        settings.biomeColorSettings.biomes[0].gradient = CreateMoonLowGradient();

        // High areas (50%+ height)
        settings.biomeColorSettings.biomes[1] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[1].startHeight = 0.5f;
        settings.biomeColorSettings.biomes[1].tintPercent = 0.2f;
        settings.biomeColorSettings.biomes[1].tint = new Color(0.6f, 0.6f, 0.5f);
        settings.biomeColorSettings.biomes[1].gradient = CreateMoonHighGradient();

        SaveColorAsset(settings, "MoonLike_ColorSettings.asset");
        Debug.Log("Created Moon-like ColorSettings!");
    }

    void CreateMountainColors()
    {
        ColorSettings settings = CreateInstance<ColorSettings>();
        settings.biomeColorSettings = new ColorSettings.BiomeColorSettings();
        settings.biomeColorSettings.biomes = new ColorSettings.BiomeColorSettings.Biome[4];
        settings.biomeColorSettings.blendAmount = 0.15f;

        // Valleys (0-25%)
        settings.biomeColorSettings.biomes[0] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[0].startHeight = 0f;
        settings.biomeColorSettings.biomes[0].tintPercent = 0.3f;
        settings.biomeColorSettings.biomes[0].tint = new Color(0.1f, 0.5f, 0.2f);
        settings.biomeColorSettings.biomes[0].gradient = CreateValleyGradient();

        // Forest (25-50%)
        settings.biomeColorSettings.biomes[1] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[1].startHeight = 0.25f;
        settings.biomeColorSettings.biomes[1].tintPercent = 0.4f;
        settings.biomeColorSettings.biomes[1].tint = new Color(0.2f, 0.6f, 0.1f);
        settings.biomeColorSettings.biomes[1].gradient = CreateForestGradient();

        // Rock (50-80%)
        settings.biomeColorSettings.biomes[2] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[2].startHeight = 0.5f;
        settings.biomeColorSettings.biomes[2].tintPercent = 0.2f;
        settings.biomeColorSettings.biomes[2].tint = new Color(0.5f, 0.4f, 0.3f);
        settings.biomeColorSettings.biomes[2].gradient = CreateRockGradient();

        // Snow (80%+)
        settings.biomeColorSettings.biomes[3] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[3].startHeight = 0.8f;
        settings.biomeColorSettings.biomes[3].tintPercent = 0.1f;
        settings.biomeColorSettings.biomes[3].tint = Color.white;
        settings.biomeColorSettings.biomes[3].gradient = CreateSnowGradient();

        SaveColorAsset(settings, "Mountain_ColorSettings.asset");
        Debug.Log("Created Mountain ColorSettings with valley, forest, rock, and snow biomes!");
    }

    void CreateDesertColors()
    {
        ColorSettings settings = CreateInstance<ColorSettings>();
        settings.biomeColorSettings = new ColorSettings.BiomeColorSettings();
        settings.biomeColorSettings.biomes = new ColorSettings.BiomeColorSettings.Biome[2];
        settings.biomeColorSettings.blendAmount = 0.2f;

        // Low dunes (0-60%)
        settings.biomeColorSettings.biomes[0] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[0].startHeight = 0f;
        settings.biomeColorSettings.biomes[0].tintPercent = 0.3f;
        settings.biomeColorSettings.biomes[0].tint = new Color(0.9f, 0.7f, 0.4f);
        settings.biomeColorSettings.biomes[0].gradient = CreateDesertLowGradient();

        // High dunes (60%+)
        settings.biomeColorSettings.biomes[1] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[1].startHeight = 0.6f;
        settings.biomeColorSettings.biomes[1].tintPercent = 0.2f;
        settings.biomeColorSettings.biomes[1].tint = new Color(1f, 0.8f, 0.5f);
        settings.biomeColorSettings.biomes[1].gradient = CreateDesertHighGradient();

        SaveColorAsset(settings, "Desert_ColorSettings.asset");
        Debug.Log("Created Desert ColorSettings!");
    }

    void CreateAlienColors()
    {
        ColorSettings settings = CreateInstance<ColorSettings>();
        settings.biomeColorSettings = new ColorSettings.BiomeColorSettings();
        settings.biomeColorSettings.biomes = new ColorSettings.BiomeColorSettings.Biome[3];
        settings.biomeColorSettings.blendAmount = 0.3f;

        // Alien low (0-40%)
        settings.biomeColorSettings.biomes[0] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[0].startHeight = 0f;
        settings.biomeColorSettings.biomes[0].tintPercent = 0.5f;
        settings.biomeColorSettings.biomes[0].tint = new Color(0.8f, 0.2f, 0.8f);
        settings.biomeColorSettings.biomes[0].gradient = CreateAlienLowGradient();

        // Alien mid (40-70%)
        settings.biomeColorSettings.biomes[1] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[1].startHeight = 0.4f;
        settings.biomeColorSettings.biomes[1].tintPercent = 0.4f;
        settings.biomeColorSettings.biomes[1].tint = new Color(0.2f, 0.8f, 0.8f);
        settings.biomeColorSettings.biomes[1].gradient = CreateAlienMidGradient();

        // Alien high (70%+)
        settings.biomeColorSettings.biomes[2] = new ColorSettings.BiomeColorSettings.Biome();
        settings.biomeColorSettings.biomes[2].startHeight = 0.7f;
        settings.biomeColorSettings.biomes[2].tintPercent = 0.3f;
        settings.biomeColorSettings.biomes[2].tint = new Color(1f, 0.5f, 0.1f);
        settings.biomeColorSettings.biomes[2].gradient = CreateAlienHighGradient();

        SaveColorAsset(settings, "Alien_ColorSettings.asset");
        Debug.Log("Created Alien ColorSettings!");
    }

    // Gradient creation methods
    Gradient CreateOceanGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[3];
        colorKeys[0] = new GradientColorKey(new Color(0.05f, 0.2f, 0.6f), 0f);  // Deep blue
        colorKeys[1] = new GradientColorKey(new Color(0.2f, 0.4f, 0.8f), 0.5f);  // Ocean blue
        colorKeys[2] = new GradientColorKey(new Color(0.4f, 0.6f, 1f), 1f);      // Light blue
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    Gradient CreatePlainsGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[3];
        colorKeys[0] = new GradientColorKey(new Color(0.3f, 0.5f, 0.2f), 0f);    // Dark green
        colorKeys[1] = new GradientColorKey(new Color(0.4f, 0.7f, 0.3f), 0.5f);  // Green
        colorKeys[2] = new GradientColorKey(new Color(0.5f, 0.8f, 0.4f), 1f);    // Light green
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    Gradient CreateMountainGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[3];
        colorKeys[0] = new GradientColorKey(new Color(0.4f, 0.3f, 0.2f), 0f);    // Brown
        colorKeys[1] = new GradientColorKey(new Color(0.6f, 0.5f, 0.4f), 0.5f);  // Light brown
        colorKeys[2] = new GradientColorKey(new Color(0.9f, 0.9f, 0.9f), 1f);    // Snow white
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    Gradient CreateMoonLowGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[2];
        colorKeys[0] = new GradientColorKey(new Color(0.2f, 0.2f, 0.2f), 0f);
        colorKeys[1] = new GradientColorKey(new Color(0.4f, 0.4f, 0.4f), 1f);
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    Gradient CreateMoonHighGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[2];
        colorKeys[0] = new GradientColorKey(new Color(0.5f, 0.5f, 0.4f), 0f);
        colorKeys[1] = new GradientColorKey(new Color(0.7f, 0.7f, 0.6f), 1f);
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    // Add more gradient creation methods for other biomes...
    Gradient CreateValleyGradient() { return CreatePlainsGradient(); }
    Gradient CreateForestGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[2];
        colorKeys[0] = new GradientColorKey(new Color(0.1f, 0.4f, 0.1f), 0f);
        colorKeys[1] = new GradientColorKey(new Color(0.2f, 0.6f, 0.2f), 1f);
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    Gradient CreateRockGradient() { return CreateMountainGradient(); }
    Gradient CreateSnowGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[2];
        colorKeys[0] = new GradientColorKey(new Color(0.8f, 0.8f, 0.9f), 0f);
        colorKeys[1] = new GradientColorKey(Color.white, 1f);
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    Gradient CreateDesertLowGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[2];
        colorKeys[0] = new GradientColorKey(new Color(0.7f, 0.5f, 0.3f), 0f);
        colorKeys[1] = new GradientColorKey(new Color(0.9f, 0.7f, 0.4f), 1f);
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    Gradient CreateDesertHighGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[2];
        colorKeys[0] = new GradientColorKey(new Color(0.9f, 0.7f, 0.4f), 0f);
        colorKeys[1] = new GradientColorKey(new Color(1f, 0.9f, 0.6f), 1f);
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    Gradient CreateAlienLowGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[2];
        colorKeys[0] = new GradientColorKey(new Color(0.6f, 0.1f, 0.6f), 0f);
        colorKeys[1] = new GradientColorKey(new Color(0.8f, 0.3f, 0.8f), 1f);
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    Gradient CreateAlienMidGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[2];
        colorKeys[0] = new GradientColorKey(new Color(0.1f, 0.6f, 0.6f), 0f);
        colorKeys[1] = new GradientColorKey(new Color(0.3f, 0.8f, 0.8f), 1f);
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    Gradient CreateAlienHighGradient()
    {
        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[2];
        colorKeys[0] = new GradientColorKey(new Color(0.8f, 0.3f, 0.1f), 0f);
        colorKeys[1] = new GradientColorKey(new Color(1f, 0.6f, 0.2f), 1f);
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    void SaveColorAsset(ColorSettings settings, string filename)
    {
        AssetDatabase.CreateAsset(settings, $"Assets/{filename}");
        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = settings;
    }
}
