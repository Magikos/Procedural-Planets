using UnityEngine;
using UnityEditor;

public class PlanetSettingsHelper : EditorWindow
{
    private float planetRadius = 1000f;
    private int randomSeed = 0;
    private bool useRandomSeed = true;

    [MenuItem("Tools/Planet Settings Helper")]
    public static void ShowWindow()
    {
        GetWindow<PlanetSettingsHelper>("Planet Settings Helper");
    }

    void OnGUI()
    {
        GUILayout.Label("Planet Settings Helper", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // Planet settings
        GUILayout.Label("Planet Configuration", EditorStyles.boldLabel);
        planetRadius = EditorGUILayout.FloatField("Planet Radius", planetRadius);

        GUILayout.Space(5);
        useRandomSeed = EditorGUILayout.Toggle("Use Random Seed", useRandomSeed);
        if (!useRandomSeed)
        {
            randomSeed = EditorGUILayout.IntField("Seed", randomSeed);
        }

        GUILayout.Space(10);

        // Preset buttons
        GUILayout.Label("Planet Presets", EditorStyles.boldLabel);
        if (GUILayout.Button("🌍 Earth-like Planet"))
        {
            CreateEarthLikeSettings();
        }

        if (GUILayout.Button("🌙 Moon-like Planet"))
        {
            CreateMoonLikeSettings();
        }

        if (GUILayout.Button("🏔️ Mountainous Planet"))
        {
            CreateMountainousSettings();
        }

        if (GUILayout.Button("🏜️ Desert Planet"))
        {
            CreateDesertSettings();
        }

        GUILayout.Space(10);

        // Random generation
        GUILayout.Label("Random Generation", EditorStyles.boldLabel);
        if (GUILayout.Button("🎲 Generate Random Planet"))
        {
            CreateRandomSettings();
        }

        if (GUILayout.Button("🎲 Generate Random Continental"))
        {
            CreateRandomContinentalSettings();
        }

        if (GUILayout.Button("🎲 Generate Random Alien World"))
        {
            CreateRandomAlienSettings();
        }

        GUILayout.Space(10);
        GUILayout.Label("Instructions:", EditorStyles.boldLabel);
        GUILayout.Label("• Heights are in meters above planet surface");
        GUILayout.Label("• Feature sizes are in kilometers");
        GUILayout.Label("• Values automatically scale with planet radius", EditorStyles.wordWrappedLabel);
        GUILayout.Label("• Assign created settings to your Planet", EditorStyles.wordWrappedLabel);
    }

    void CreateEarthLikeSettings()
    {
        var seed = useRandomSeed ? Random.Range(0, 10000) : randomSeed;
        Random.InitState(seed);

        ShapeSettings settings = CreateInstance<ShapeSettings>();
        settings.planetRadius = planetRadius;
        settings.noiseLayers = new ShapeSettings.NoiseLayer[3];

        // Base continental shape
        settings.noiseLayers[0] = CreateNoiseLayer(
            NoiseSettings.FilterType.Simple,
            maxHeight: 80f, // 80 meters of base elevation
            featureSize: 50f, // 50km continental features
            numLayers: 4,
            roughness: 2f,
            persistence: 0.5f,
            enabled: true,
            useMask: false
        );

        // Mountain ridges
        settings.noiseLayers[1] = CreateNoiseLayer(
            NoiseSettings.FilterType.Ridged,
            maxHeight: 200f, // 200 meter mountains
            featureSize: 15f, // 15km mountain ranges
            numLayers: 3,
            roughness: 2.2f,
            persistence: 0.4f,
            enabled: true,
            useMask: true, // Use first layer as mask
            weightMultiplier: 0.8f
        );

        // Fine detail
        settings.noiseLayers[2] = CreateNoiseLayer(
            NoiseSettings.FilterType.Simple,
            maxHeight: 25f, // 25 meter hills
            featureSize: 3f, // 3km small features
            numLayers: 3,
            roughness: 2.5f,
            persistence: 0.6f,
            enabled: true,
            useMask: false
        );

        SaveAsset(settings, $"EarthLike_Planet_Seed{seed}.asset");
        Debug.Log($"Created Earth-like planet with seed {seed}!");
    }

    void CreateMoonLikeSettings()
    {
        var seed = useRandomSeed ? Random.Range(0, 10000) : randomSeed;
        Random.InitState(seed);

        ShapeSettings settings = CreateInstance<ShapeSettings>();
        settings.planetRadius = planetRadius;
        settings.noiseLayers = new ShapeSettings.NoiseLayer[2];

        // Base crater terrain
        settings.noiseLayers[0] = CreateNoiseLayer(
            NoiseSettings.FilterType.Simple,
            maxHeight: 150f, // 150 meter craters
            featureSize: 25f, // 25km crater fields
            numLayers: 5,
            roughness: 2f,
            persistence: 0.6f,
            enabled: true,
            useMask: false
        );

        // Fine crater detail
        settings.noiseLayers[1] = CreateNoiseLayer(
            NoiseSettings.FilterType.Simple,
            maxHeight: 30f, // 30 meter small craters
            featureSize: 5f, // 5km small features
            numLayers: 4,
            roughness: 2.3f,
            persistence: 0.5f,
            enabled: true,
            useMask: false
        );

        SaveAsset(settings, $"MoonLike_Planet_Seed{seed}.asset");
        Debug.Log($"Created Moon-like planet with seed {seed}!");
    }

    void CreateMountainousSettings()
    {
        var seed = useRandomSeed ? Random.Range(0, 10000) : randomSeed;
        Random.InitState(seed);

        ShapeSettings settings = CreateInstance<ShapeSettings>();
        settings.planetRadius = planetRadius;
        settings.noiseLayers = new ShapeSettings.NoiseLayer[3];

        // Base terrain
        settings.noiseLayers[0] = CreateNoiseLayer(
            NoiseSettings.FilterType.Simple,
            maxHeight: 100f,
            featureSize: 40f,
            numLayers: 4,
            roughness: 2f,
            persistence: 0.4f,
            enabled: true,
            useMask: false
        );

        // Major ridges
        settings.noiseLayers[1] = CreateNoiseLayer(
            NoiseSettings.FilterType.Ridged,
            maxHeight: 400f, // Tall mountains
            featureSize: 20f,
            numLayers: 4,
            roughness: 2.1f,
            persistence: 0.3f,
            enabled: true,
            useMask: true,
            weightMultiplier: 0.9f
        );

        // Secondary ridges
        settings.noiseLayers[2] = CreateNoiseLayer(
            NoiseSettings.FilterType.Ridged,
            maxHeight: 150f,
            featureSize: 8f,
            numLayers: 2,
            roughness: 2.4f,
            persistence: 0.5f,
            enabled: true,
            useMask: true,
            weightMultiplier: 0.7f
        );

        SaveAsset(settings, $"Mountainous_Planet_Seed{seed}.asset");
        Debug.Log($"Created Mountainous planet with seed {seed}!");
    }

    void CreateDesertSettings()
    {
        var seed = useRandomSeed ? Random.Range(0, 10000) : randomSeed;
        Random.InitState(seed);

        ShapeSettings settings = CreateInstance<ShapeSettings>();
        settings.planetRadius = planetRadius;
        settings.noiseLayers = new ShapeSettings.NoiseLayer[2];

        // Rolling dunes
        settings.noiseLayers[0] = CreateNoiseLayer(
            NoiseSettings.FilterType.Simple,
            maxHeight: 60f, // Gentle rolling terrain
            featureSize: 30f,
            numLayers: 3,
            roughness: 1.8f,
            persistence: 0.6f,
            enabled: true,
            useMask: false
        );

        // Sand dune details
        settings.noiseLayers[1] = CreateNoiseLayer(
            NoiseSettings.FilterType.Simple,
            maxHeight: 20f,
            featureSize: 2f, // Small dune features
            numLayers: 4,
            roughness: 2.2f,
            persistence: 0.5f,
            enabled: true,
            useMask: false
        );

        SaveAsset(settings, $"Desert_Planet_Seed{seed}.asset");
        Debug.Log($"Created Desert planet with seed {seed}!");
    }

    void CreateRandomSettings()
    {
        var seed = useRandomSeed ? Random.Range(0, 10000) : randomSeed;
        Random.InitState(seed);

        ShapeSettings settings = CreateInstance<ShapeSettings>();
        settings.planetRadius = planetRadius;

        int numLayers = Random.Range(2, 5);
        settings.noiseLayers = new ShapeSettings.NoiseLayer[numLayers];

        for (int i = 0; i < numLayers; i++)
        {
            settings.noiseLayers[i] = CreateNoiseLayer(
                Random.value > 0.4f ? NoiseSettings.FilterType.Simple : NoiseSettings.FilterType.Ridged,
                maxHeight: Random.Range(20f, 300f),
                featureSize: Random.Range(2f, 80f),
                numLayers: Random.Range(2, 6),
                roughness: Random.Range(1.5f, 3f),
                persistence: Random.Range(0.3f, 0.7f),
                enabled: true,
                useMask: i > 0 && Random.value > 0.5f,
                weightMultiplier: Random.Range(0.6f, 1f)
            );
        }

        SaveAsset(settings, $"Random_Planet_Seed{seed}.asset");
        Debug.Log($"Created Random planet with seed {seed}!");
    }

    void CreateRandomContinentalSettings()
    {
        var seed = useRandomSeed ? Random.Range(0, 10000) : randomSeed;
        Random.InitState(seed);

        ShapeSettings settings = CreateInstance<ShapeSettings>();
        settings.planetRadius = planetRadius;
        settings.noiseLayers = new ShapeSettings.NoiseLayer[3];

        // Always start with a continental base
        settings.noiseLayers[0] = CreateNoiseLayer(
            NoiseSettings.FilterType.Simple,
            maxHeight: Random.Range(40f, 120f),
            featureSize: Random.Range(30f, 80f),
            numLayers: Random.Range(3, 5),
            roughness: Random.Range(1.8f, 2.3f),
            persistence: Random.Range(0.4f, 0.6f),
            enabled: true,
            useMask: false
        );

        // Mountain layer
        settings.noiseLayers[1] = CreateNoiseLayer(
            NoiseSettings.FilterType.Ridged,
            maxHeight: Random.Range(100f, 400f),
            featureSize: Random.Range(10f, 30f),
            numLayers: Random.Range(2, 4),
            roughness: Random.Range(2f, 2.5f),
            persistence: Random.Range(0.3f, 0.5f),
            enabled: true,
            useMask: true,
            weightMultiplier: Random.Range(0.7f, 0.9f)
        );

        // Detail layer
        settings.noiseLayers[2] = CreateNoiseLayer(
            Random.value > 0.5f ? NoiseSettings.FilterType.Simple : NoiseSettings.FilterType.Ridged,
            maxHeight: Random.Range(15f, 80f),
            featureSize: Random.Range(2f, 15f),
            numLayers: Random.Range(2, 4),
            roughness: Random.Range(2f, 3f),
            persistence: Random.Range(0.4f, 0.7f),
            enabled: true,
            useMask: Random.value > 0.3f,
            weightMultiplier: Random.Range(0.5f, 0.8f)
        );

        SaveAsset(settings, $"RandomContinental_Seed{seed}.asset");
        Debug.Log($"Created Random Continental planet with seed {seed}!");
    }

    void CreateRandomAlienSettings()
    {
        var seed = useRandomSeed ? Random.Range(0, 10000) : randomSeed;
        Random.InitState(seed);

        ShapeSettings settings = CreateInstance<ShapeSettings>();
        settings.planetRadius = planetRadius;

        int numLayers = Random.Range(3, 6);
        settings.noiseLayers = new ShapeSettings.NoiseLayer[numLayers];

        // More extreme values for alien worlds
        for (int i = 0; i < numLayers; i++)
        {
            settings.noiseLayers[i] = CreateNoiseLayer(
                Random.value > 0.3f ? NoiseSettings.FilterType.Ridged : NoiseSettings.FilterType.Simple,
                maxHeight: Random.Range(50f, 500f), // More extreme heights
                featureSize: Random.Range(1f, 100f), // Wider range of features
                numLayers: Random.Range(2, 7),
                roughness: Random.Range(1.2f, 4f), // More extreme roughness
                persistence: Random.Range(0.2f, 0.8f),
                enabled: true,
                useMask: i > 0 && Random.value > 0.4f,
                weightMultiplier: Random.Range(0.4f, 1.2f)
            );
        }

        SaveAsset(settings, $"RandomAlien_Seed{seed}.asset");
        Debug.Log($"Created Random Alien world with seed {seed}!");
    }

    ShapeSettings.NoiseLayer CreateNoiseLayer(NoiseSettings.FilterType filterType, float maxHeight,
        float featureSize, int numLayers, float roughness, float persistence, bool enabled,
        bool useMask, float weightMultiplier = 0.8f)
    {
        var layer = new ShapeSettings.NoiseLayer();
        layer.enabled = enabled;
        layer.useFirstLayerAsMask = useMask;
        layer.noiseSettings = new NoiseSettings();
        layer.noiseSettings.filterType = filterType;

        if (filterType == NoiseSettings.FilterType.Simple)
        {
            layer.noiseSettings.simpleNoiseSettings = new NoiseSettings.SimpleNoiseSettings();
            var settings = layer.noiseSettings.simpleNoiseSettings;
            settings.maxHeightMeters = maxHeight;
            settings.featureSize = featureSize;
            settings.numLayers = numLayers;
            settings.roughness = roughness;
            settings.persistence = persistence;
            settings.centre = new Vector3(Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f));
            settings.minValue = Random.Range(0f, 0.3f);
        }
        else
        {
            layer.noiseSettings.ridgedNoiseSettings = new NoiseSettings.RidgedNoiseSettings();
            var settings = layer.noiseSettings.ridgedNoiseSettings;
            settings.maxHeightMeters = maxHeight;
            settings.featureSize = featureSize;
            settings.numLayers = numLayers;
            settings.roughness = roughness;
            settings.persistence = persistence;
            settings.centre = new Vector3(Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f));
            settings.minValue = Random.Range(0f, 0.3f);
            settings.weightMultiplier = weightMultiplier;
        }

        return layer;
    }

    void SaveAsset(ShapeSettings settings, string filename)
    {
        AssetDatabase.CreateAsset(settings, $"Assets/{filename}");
        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = settings;
    }
}
