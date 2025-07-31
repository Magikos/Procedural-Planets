using UnityEngine;
using UnityEditor;

public static class PlanetPresets
{
    [MenuItem("Tools/Planet Presets/Earth-like")]
    public static void CreateEarthLike()
    {
        var planet = Selection.activeGameObject?.GetComponent<Planet>();
        if (planet == null)
        {
            Debug.LogWarning("Select a Planet GameObject first!");
            return;
        }

        planet.resolution = 50;

        // Configure Shape Settings with realistic Earth-like terrain
        if (planet.shapeSettings != null)
        {
            planet.shapeSettings.planetRadius = 1000f; // 1km radius for testing

            // Create noise layers for realistic terrain
            planet.shapeSettings.noiseLayers = new ShapeSettings.NoiseLayer[3];

            // Base continental shape
            planet.shapeSettings.noiseLayers[0] = CreateNoiseLayer(
                NoiseSettings.FilterType.Simple,
                maxHeightMeters: 80f,
                featureSize: 50f,
                numLayers: 4,
                roughness: 2f,
                persistence: 0.5f,
                enabled: true,
                useMask: false,
                planetRadius: planet.shapeSettings.planetRadius
            );

            // Mountain ridges
            planet.shapeSettings.noiseLayers[1] = CreateNoiseLayer(
                NoiseSettings.FilterType.Ridged,
                maxHeightMeters: 200f,
                featureSize: 15f,
                numLayers: 3,
                roughness: 2.2f,
                persistence: 0.4f,
                enabled: true,
                useMask: true,
                planetRadius: planet.shapeSettings.planetRadius
            );

            // Fine detail
            planet.shapeSettings.noiseLayers[2] = CreateNoiseLayer(
                NoiseSettings.FilterType.Simple,
                maxHeightMeters: 25f,
                featureSize: 3f,
                numLayers: 3,
                roughness: 2.5f,
                persistence: 0.6f,
                enabled: true,
                useMask: false,
                planetRadius: planet.shapeSettings.planetRadius
            );
        }

        // Configure Color Settings for Earth-like biomes
        if (planet.colorSettings != null)
        {
            SetupEarthLikeColors(planet.colorSettings);
        }

        if (planet.lodSettings != null)
        {
            planet.lodSettings.maxLOD = 5;
            // Typical LOD distances for Earth-like planet
        }

        planet.GeneratePlanet();
        Debug.Log("Applied Earth-like preset with realistic terrain and colors to " + planet.name);
    }

    [MenuItem("Tools/Planet Presets/Rocky")]
    public static void CreateRocky()
    {
        var planet = Selection.activeGameObject?.GetComponent<Planet>();
        if (planet == null)
        {
            Debug.LogWarning("Select a Planet GameObject first!");
            return;
        }

        planet.resolution = 75;

        // Configure Shape Settings for rugged rocky terrain
        if (planet.shapeSettings != null)
        {
            planet.shapeSettings.planetRadius = 800f;

            planet.shapeSettings.noiseLayers = new ShapeSettings.NoiseLayer[4];

            // Base terrain
            planet.shapeSettings.noiseLayers[0] = CreateNoiseLayer(
                NoiseSettings.FilterType.Simple,
                maxHeightMeters: 100f,
                featureSize: 40f,
                numLayers: 4,
                roughness: 2f,
                persistence: 0.4f,
                enabled: true,
                useMask: false,
                planetRadius: planet.shapeSettings.planetRadius
            );

            // Major ridges
            planet.shapeSettings.noiseLayers[1] = CreateNoiseLayer(
                NoiseSettings.FilterType.Ridged,
                maxHeightMeters: 400f,
                featureSize: 20f,
                numLayers: 4,
                roughness: 2.1f,
                persistence: 0.3f,
                enabled: true,
                useMask: true,
                planetRadius: planet.shapeSettings.planetRadius,
                weightMultiplier: 0.9f
            );

            // Secondary ridges
            planet.shapeSettings.noiseLayers[2] = CreateNoiseLayer(
                NoiseSettings.FilterType.Ridged,
                maxHeightMeters: 150f,
                featureSize: 8f,
                numLayers: 2,
                roughness: 2.4f,
                persistence: 0.5f,
                enabled: true,
                useMask: true,
                planetRadius: planet.shapeSettings.planetRadius,
                weightMultiplier: 0.7f
            );

            // Fine rocky detail
            planet.shapeSettings.noiseLayers[3] = CreateNoiseLayer(
                NoiseSettings.FilterType.Simple,
                maxHeightMeters: 30f,
                featureSize: 2f,
                numLayers: 3,
                roughness: 2.8f,
                persistence: 0.6f,
                enabled: true,
                useMask: false,
                planetRadius: planet.shapeSettings.planetRadius
            );
        }

        // Configure Color Settings for rocky appearance
        if (planet.colorSettings != null)
        {
            SetupRockyColors(planet.colorSettings);
        }

        if (planet.lodSettings != null)
        {
            planet.lodSettings.maxLOD = 6; // Higher detail for rocky surfaces
        }

        planet.GeneratePlanet();
        Debug.Log("Applied Rocky preset with rugged terrain to " + planet.name);
    }

    [MenuItem("Tools/Planet Presets/Gas Giant")]
    public static void CreateGasGiant()
    {
        var planet = Selection.activeGameObject?.GetComponent<Planet>();
        if (planet == null)
        {
            Debug.LogWarning("Select a Planet GameObject first!");
            return;
        }

        planet.resolution = 30; // Lower res for smoother surface

        // Configure Shape Settings for smooth gas giant
        if (planet.shapeSettings != null)
        {
            planet.shapeSettings.planetRadius = 2000f; // Larger for gas giant

            planet.shapeSettings.noiseLayers = new ShapeSettings.NoiseLayer[2];

            // Large-scale atmospheric bands
            planet.shapeSettings.noiseLayers[0] = CreateNoiseLayer(
                NoiseSettings.FilterType.Simple,
                maxHeightMeters: 20f, // Very subtle elevation changes
                featureSize: 100f, // Large atmospheric features
                numLayers: 2,
                roughness: 1.5f,
                persistence: 0.6f,
                enabled: true,
                useMask: false,
                planetRadius: planet.shapeSettings.planetRadius
            );

            // Fine atmospheric detail
            planet.shapeSettings.noiseLayers[1] = CreateNoiseLayer(
                NoiseSettings.FilterType.Simple,
                maxHeightMeters: 5f, // Very minimal surface variation
                featureSize: 30f,
                numLayers: 2,
                roughness: 1.8f,
                persistence: 0.4f,
                enabled: true,
                useMask: false,
                planetRadius: planet.shapeSettings.planetRadius
            );
        }

        // Configure Color Settings for gas giant bands
        if (planet.colorSettings != null)
        {
            SetupGasGiantColors(planet.colorSettings);
        }

        if (planet.lodSettings != null)
        {
            planet.lodSettings.maxLOD = 4; // Lower LOD for smooth gas giants
        }

        planet.GeneratePlanet();
        Debug.Log("Applied Gas Giant preset with atmospheric bands to " + planet.name);
    }

    // Validate menu items - only show when a Planet is selected
    [MenuItem("Tools/Planet Presets/Earth-like", true)]
    [MenuItem("Tools/Planet Presets/Rocky", true)]
    [MenuItem("Tools/Planet Presets/Gas Giant", true)]
    public static bool ValidatePresetMenus()
    {
        return Selection.activeGameObject?.GetComponent<Planet>() != null;
    }

    // Helper method to create a noise layer
    static ShapeSettings.NoiseLayer CreateNoiseLayer(
        NoiseSettings.FilterType filterType,
        float maxHeightMeters,
        float featureSize,
        int numLayers,
        float roughness,
        float persistence,
        bool enabled,
        bool useMask,
        float planetRadius,
        float weightMultiplier = 0.8f)
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
            settings.maxHeightMeters = maxHeightMeters;
            settings.featureSize = featureSize;
            settings.numLayers = numLayers;
            settings.roughness = roughness;
            settings.persistence = persistence;
            settings.centre = new Vector3(Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f));
            settings.minValue = Random.Range(0f, 0.3f);

            // Calculate scaled values
            settings.CalculateScaledValues(planetRadius);
        }
        else
        {
            layer.noiseSettings.ridgedNoiseSettings = new NoiseSettings.RidgedNoiseSettings();
            var settings = layer.noiseSettings.ridgedNoiseSettings;
            settings.maxHeightMeters = maxHeightMeters;
            settings.featureSize = featureSize;
            settings.numLayers = numLayers;
            settings.roughness = roughness;
            settings.persistence = persistence;
            settings.centre = new Vector3(Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f));
            settings.minValue = Random.Range(0f, 0.3f);
            settings.weightMultiplier = weightMultiplier;

            // Calculate scaled values
            settings.CalculateScaledValues(planetRadius);
        }

        return layer;
    }

    // Helper method to setup Earth-like colors
    static void SetupEarthLikeColors(ColorSettings colorSettings)
    {
        if (colorSettings.biomeColorSettings == null)
        {
            colorSettings.biomeColorSettings = new ColorSettings.BiomeColorSettings();
        }

        var biomeSettings = colorSettings.biomeColorSettings;
        biomeSettings.biomes = new ColorSettings.BiomeColorSettings.Biome[3];

        // Ocean biome (low elevations)
        biomeSettings.biomes[0] = new ColorSettings.BiomeColorSettings.Biome();
        biomeSettings.biomes[0].startHeight = 0f;
        biomeSettings.biomes[0].tintPercent = 0.2f;
        biomeSettings.biomes[0].tint = new Color(0.3f, 0.5f, 1f); // Blue tint
        biomeSettings.biomes[0].gradient = CreateOceanGradient();

        // Plains biome (medium elevations)
        biomeSettings.biomes[1] = new ColorSettings.BiomeColorSettings.Biome();
        biomeSettings.biomes[1].startHeight = 0.3f;
        biomeSettings.biomes[1].tintPercent = 0.3f;
        biomeSettings.biomes[1].tint = new Color(0.3f, 0.8f, 0.3f); // Green tint
        biomeSettings.biomes[1].gradient = CreatePlainsGradient();

        // Mountain biome (high elevations)
        biomeSettings.biomes[2] = new ColorSettings.BiomeColorSettings.Biome();
        biomeSettings.biomes[2].startHeight = 0.6f;
        biomeSettings.biomes[2].tintPercent = 0.4f;
        biomeSettings.biomes[2].tint = new Color(0.7f, 0.6f, 0.5f); // Brown tint
        biomeSettings.biomes[2].gradient = CreateMountainGradient();

        // Setup biome blending
        biomeSettings.blendAmount = 0.5f;
        biomeSettings.noiseOffset = 0f;
        biomeSettings.noiseStrength = 0.1f;
    }

    static Gradient CreateOceanGradient()
    {
        Gradient gradient = new Gradient();
        var colors = new GradientColorKey[3];
        colors[0] = new GradientColorKey(new Color(0.1f, 0.2f, 0.5f), 0f); // Deep blue
        colors[1] = new GradientColorKey(new Color(0.2f, 0.4f, 0.8f), 0.5f); // Blue
        colors[2] = new GradientColorKey(new Color(0.4f, 0.6f, 1f), 1f); // Light blue

        var alphas = new GradientAlphaKey[2];
        alphas[0] = new GradientAlphaKey(1f, 0f);
        alphas[1] = new GradientAlphaKey(1f, 1f);

        gradient.SetKeys(colors, alphas);
        return gradient;
    }

    static Gradient CreatePlainsGradient()
    {
        Gradient gradient = new Gradient();
        var colors = new GradientColorKey[3];
        colors[0] = new GradientColorKey(new Color(0.2f, 0.5f, 0.2f), 0f); // Dark green
        colors[1] = new GradientColorKey(new Color(0.3f, 0.7f, 0.3f), 0.5f); // Green
        colors[2] = new GradientColorKey(new Color(0.5f, 0.8f, 0.4f), 1f); // Light green

        var alphas = new GradientAlphaKey[2];
        alphas[0] = new GradientAlphaKey(1f, 0f);
        alphas[1] = new GradientAlphaKey(1f, 1f);

        gradient.SetKeys(colors, alphas);
        return gradient;
    }

    static Gradient CreateMountainGradient()
    {
        Gradient gradient = new Gradient();
        var colors = new GradientColorKey[4];
        colors[0] = new GradientColorKey(new Color(0.4f, 0.3f, 0.2f), 0f); // Brown
        colors[1] = new GradientColorKey(new Color(0.5f, 0.4f, 0.3f), 0.3f); // Light brown
        colors[2] = new GradientColorKey(new Color(0.6f, 0.6f, 0.6f), 0.7f); // Gray
        colors[3] = new GradientColorKey(new Color(0.9f, 0.9f, 0.9f), 1f); // White (snow)

        var alphas = new GradientAlphaKey[2];
        alphas[0] = new GradientAlphaKey(1f, 0f);
        alphas[1] = new GradientAlphaKey(1f, 1f);

        gradient.SetKeys(colors, alphas);
        return gradient;
    }

    // Helper method to setup Rocky planet colors
    static void SetupRockyColors(ColorSettings colorSettings)
    {
        if (colorSettings.biomeColorSettings == null)
        {
            colorSettings.biomeColorSettings = new ColorSettings.BiomeColorSettings();
        }

        var biomeSettings = colorSettings.biomeColorSettings;
        biomeSettings.biomes = new ColorSettings.BiomeColorSettings.Biome[3];

        // Low rocky terrain
        biomeSettings.biomes[0] = new ColorSettings.BiomeColorSettings.Biome();
        biomeSettings.biomes[0].startHeight = 0f;
        biomeSettings.biomes[0].tintPercent = 0.3f;
        biomeSettings.biomes[0].tint = new Color(0.6f, 0.4f, 0.3f); // Reddish brown
        biomeSettings.biomes[0].gradient = CreateRockyLowGradient();

        // Mid rocky terrain
        biomeSettings.biomes[1] = new ColorSettings.BiomeColorSettings.Biome();
        biomeSettings.biomes[1].startHeight = 0.4f;
        biomeSettings.biomes[1].tintPercent = 0.4f;
        biomeSettings.biomes[1].tint = new Color(0.5f, 0.4f, 0.3f); // Dark brown
        biomeSettings.biomes[1].gradient = CreateRockyMidGradient();

        // High rocky peaks
        biomeSettings.biomes[2] = new ColorSettings.BiomeColorSettings.Biome();
        biomeSettings.biomes[2].startHeight = 0.7f;
        biomeSettings.biomes[2].tintPercent = 0.2f;
        biomeSettings.biomes[2].tint = new Color(0.4f, 0.4f, 0.4f); // Gray
        biomeSettings.biomes[2].gradient = CreateRockyHighGradient();

        biomeSettings.blendAmount = 0.3f;
        biomeSettings.noiseOffset = 0f;
        biomeSettings.noiseStrength = 0.05f;
    }

    // Helper method to setup Gas Giant colors
    static void SetupGasGiantColors(ColorSettings colorSettings)
    {
        if (colorSettings.biomeColorSettings == null)
        {
            colorSettings.biomeColorSettings = new ColorSettings.BiomeColorSettings();
        }

        var biomeSettings = colorSettings.biomeColorSettings;
        biomeSettings.biomes = new ColorSettings.BiomeColorSettings.Biome[4];

        // Lower atmosphere bands (darker)
        biomeSettings.biomes[0] = new ColorSettings.BiomeColorSettings.Biome();
        biomeSettings.biomes[0].startHeight = 0f;
        biomeSettings.biomes[0].tintPercent = 0.4f;
        biomeSettings.biomes[0].tint = new Color(0.8f, 0.6f, 0.3f); // Orange-brown
        biomeSettings.biomes[0].gradient = CreateGasGiantLowGradient();

        // Mid atmosphere bands
        biomeSettings.biomes[1] = new ColorSettings.BiomeColorSettings.Biome();
        biomeSettings.biomes[1].startHeight = 0.25f;
        biomeSettings.biomes[1].tintPercent = 0.5f;
        biomeSettings.biomes[1].tint = new Color(1f, 0.8f, 0.4f); // Yellow-orange
        biomeSettings.biomes[1].gradient = CreateGasGiantMidGradient();

        // Upper atmosphere bands
        biomeSettings.biomes[2] = new ColorSettings.BiomeColorSettings.Biome();
        biomeSettings.biomes[2].startHeight = 0.5f;
        biomeSettings.biomes[2].tintPercent = 0.6f;
        biomeSettings.biomes[2].tint = new Color(1f, 0.9f, 0.6f); // Light yellow
        biomeSettings.biomes[2].gradient = CreateGasGiantHighGradient();

        // Storm systems (highest "elevation")
        biomeSettings.biomes[3] = new ColorSettings.BiomeColorSettings.Biome();
        biomeSettings.biomes[3].startHeight = 0.8f;
        biomeSettings.biomes[3].tintPercent = 0.3f;
        biomeSettings.biomes[3].tint = new Color(0.9f, 0.9f, 1f); // Pale blue-white
        biomeSettings.biomes[3].gradient = CreateGasGiantStormGradient();

        biomeSettings.blendAmount = 0.8f; // High blending for smooth bands
        biomeSettings.noiseOffset = 0f;
        biomeSettings.noiseStrength = 0.2f;
    }

    // Rocky planet gradients
    static Gradient CreateRockyLowGradient()
    {
        Gradient gradient = new Gradient();
        var colors = new GradientColorKey[3];
        colors[0] = new GradientColorKey(new Color(0.4f, 0.25f, 0.15f), 0f); // Dark red-brown
        colors[1] = new GradientColorKey(new Color(0.6f, 0.35f, 0.2f), 0.5f); // Red-brown
        colors[2] = new GradientColorKey(new Color(0.7f, 0.45f, 0.25f), 1f); // Light red-brown

        var alphas = new GradientAlphaKey[2];
        alphas[0] = new GradientAlphaKey(1f, 0f);
        alphas[1] = new GradientAlphaKey(1f, 1f);

        gradient.SetKeys(colors, alphas);
        return gradient;
    }

    static Gradient CreateRockyMidGradient()
    {
        Gradient gradient = new Gradient();
        var colors = new GradientColorKey[3];
        colors[0] = new GradientColorKey(new Color(0.35f, 0.3f, 0.25f), 0f); // Dark brown
        colors[1] = new GradientColorKey(new Color(0.5f, 0.4f, 0.3f), 0.5f); // Medium brown
        colors[2] = new GradientColorKey(new Color(0.6f, 0.5f, 0.4f), 1f); // Light brown

        var alphas = new GradientAlphaKey[2];
        alphas[0] = new GradientAlphaKey(1f, 0f);
        alphas[1] = new GradientAlphaKey(1f, 1f);

        gradient.SetKeys(colors, alphas);
        return gradient;
    }

    static Gradient CreateRockyHighGradient()
    {
        Gradient gradient = new Gradient();
        var colors = new GradientColorKey[3];
        colors[0] = new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 0f); // Dark gray
        colors[1] = new GradientColorKey(new Color(0.5f, 0.5f, 0.5f), 0.5f); // Medium gray
        colors[2] = new GradientColorKey(new Color(0.7f, 0.7f, 0.7f), 1f); // Light gray

        var alphas = new GradientAlphaKey[2];
        alphas[0] = new GradientAlphaKey(1f, 0f);
        alphas[1] = new GradientAlphaKey(1f, 1f);

        gradient.SetKeys(colors, alphas);
        return gradient;
    }

    // Gas Giant gradients
    static Gradient CreateGasGiantLowGradient()
    {
        Gradient gradient = new Gradient();
        var colors = new GradientColorKey[3];
        colors[0] = new GradientColorKey(new Color(0.6f, 0.4f, 0.2f), 0f); // Dark orange-brown
        colors[1] = new GradientColorKey(new Color(0.8f, 0.5f, 0.25f), 0.5f); // Orange-brown
        colors[2] = new GradientColorKey(new Color(0.9f, 0.6f, 0.3f), 1f); // Light orange-brown

        var alphas = new GradientAlphaKey[2];
        alphas[0] = new GradientAlphaKey(1f, 0f);
        alphas[1] = new GradientAlphaKey(1f, 1f);

        gradient.SetKeys(colors, alphas);
        return gradient;
    }

    static Gradient CreateGasGiantMidGradient()
    {
        Gradient gradient = new Gradient();
        var colors = new GradientColorKey[3];
        colors[0] = new GradientColorKey(new Color(0.8f, 0.6f, 0.3f), 0f); // Orange
        colors[1] = new GradientColorKey(new Color(1f, 0.7f, 0.4f), 0.5f); // Yellow-orange
        colors[2] = new GradientColorKey(new Color(1f, 0.8f, 0.5f), 1f); // Light yellow-orange

        var alphas = new GradientAlphaKey[2];
        alphas[0] = new GradientAlphaKey(1f, 0f);
        alphas[1] = new GradientAlphaKey(1f, 1f);

        gradient.SetKeys(colors, alphas);
        return gradient;
    }

    static Gradient CreateGasGiantHighGradient()
    {
        Gradient gradient = new Gradient();
        var colors = new GradientColorKey[3];
        colors[0] = new GradientColorKey(new Color(1f, 0.8f, 0.5f), 0f); // Yellow-orange
        colors[1] = new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0.5f); // Light yellow
        colors[2] = new GradientColorKey(new Color(1f, 0.95f, 0.8f), 1f); // Pale yellow

        var alphas = new GradientAlphaKey[2];
        alphas[0] = new GradientAlphaKey(1f, 0f);
        alphas[1] = new GradientAlphaKey(1f, 1f);

        gradient.SetKeys(colors, alphas);
        return gradient;
    }

    static Gradient CreateGasGiantStormGradient()
    {
        Gradient gradient = new Gradient();
        var colors = new GradientColorKey[3];
        colors[0] = new GradientColorKey(new Color(0.8f, 0.8f, 0.9f), 0f); // Light blue-gray
        colors[1] = new GradientColorKey(new Color(0.9f, 0.9f, 0.95f), 0.5f); // Very light blue-white
        colors[2] = new GradientColorKey(new Color(1f, 1f, 1f), 1f); // Pure white

        var alphas = new GradientAlphaKey[2];
        alphas[0] = new GradientAlphaKey(1f, 0f);
        alphas[1] = new GradientAlphaKey(1f, 1f);

        gradient.SetKeys(colors, alphas);
        return gradient;
    }
}
