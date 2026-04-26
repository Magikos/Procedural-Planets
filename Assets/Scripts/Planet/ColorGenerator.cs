using UnityEngine;

public class ColorGenerator : IBiomeProvider, IColorProvider
{
    const int TextureResolution = 50;
    Texture2D _texture;
    ColorSettings _colorSettings;

    ITemperatureProvider _temperatureProvider;
    IMoistureProvider _moistureProvider;
    IBiomeRegistry _biomeRegistry;

    public void Configure(ColorSettings settings)
    {
        _colorSettings = settings;

        if (_colorSettings.BiomeSettings != null && _colorSettings.BiomeSettings.Registry != null)
        {
            _biomeRegistry = _colorSettings.BiomeSettings.Registry;
            _temperatureProvider = new TemperatureProvider(
                _colorSettings.BiomeSettings.TemperatureNoise,
                _colorSettings.BiomeSettings.TemperatureNoiseStrength);
            _moistureProvider = new MoistureProvider(_colorSettings.BiomeSettings.MoistureNoise);
        }

        int biomeCount = Mathf.Max(1, _biomeRegistry != null ? _biomeRegistry.BiomeCount : 1);
        if (_texture == null || _texture.width != TextureResolution * 2 || _texture.height != biomeCount)
            _texture = new Texture2D(TextureResolution * 2, biomeCount, TextureFormat.RGBA32, false);
    }

    public void Initialize(int seed)
    {
        _temperatureProvider?.Initialize(seed);
        _moistureProvider?.Initialize(seed + 100);
    }

    public void Initialize()
    {
        Initialize(0);
    }

    public void UpdateElevation(float min, float max)
    {
        _colorSettings.PlanetMaterial.SetVector("_ElevationMinMax", new Vector4(min, max));
    }

    public BiomeResult EvaluateBiome(Vector3 pointOnUnitSphere, float elevation)
    {
        if (_biomeRegistry == null)
            return new BiomeResult(BiomeType.Grassland, 0.5f, 0.5f);

        float temperature = _temperatureProvider.Evaluate(pointOnUnitSphere);
        float moisture = _moistureProvider.Evaluate(pointOnUnitSphere);

        return _biomeRegistry.Resolve(temperature, moisture, elevation);
    }

    public float BiomePercentFromPoint(Vector3 pointOnUnitSphere, float elevation)
    {
        if (_biomeRegistry == null) return 0f;

        var registry = _colorSettings.BiomeSettings.Registry;
        int totalBiomes = _biomeRegistry.BiomeCount;
        if (totalBiomes <= 1) return 0f;

        // Elevation overrides: Ocean=0, Beach=1, Mountain=2
        if (elevation < registry.OceanThreshold)
            return 0f / (totalBiomes - 1);
        if (elevation < registry.OceanThreshold + registry.BeachWidth)
            return 1f / (totalBiomes - 1);
        if (elevation > registry.MountainThreshold)
            return 2f / (totalBiomes - 1);

        float temperature = _temperatureProvider.Evaluate(pointOnUnitSphere);
        float moisture = _moistureProvider.Evaluate(pointOnUnitSphere);

        float tempCont = Mathf.Clamp01(temperature) * (registry.TemperatureSteps - 1);
        float moistCont = Mathf.Clamp01(moisture) * (registry.MoistureSteps - 1);

        int tempIdx = Mathf.Clamp(Mathf.FloorToInt(tempCont), 0, registry.TemperatureSteps - 1);
        int moistIdx = Mathf.Clamp(Mathf.FloorToInt(moistCont), 0, registry.MoistureSteps - 1);

        int gridIndex = tempIdx * registry.MoistureSteps + moistIdx + 3;
        return (float)gridIndex / (totalBiomes - 1);
    }

    public void UpdateColors()
    {
        if (_biomeRegistry == null) return;

        var registry = _colorSettings.BiomeSettings.Registry;
        int biomeCount = _biomeRegistry.BiomeCount;
        Color[] colors = new Color[_texture.width * biomeCount];

        // Resize texture if needed
        if (_texture.height != biomeCount)
            _texture = new Texture2D(TextureResolution * 2, biomeCount, TextureFormat.RGBA32, false);

        int colorIndex = 0;
        for (int b = 0; b < biomeCount; b++)
        {
            var def = registry.GetDefinitionByIndex(b);
            for (int i = 0; i < TextureResolution * 2; i++)
            {
                Color gradientColor;
                if (i < TextureResolution)
                {
                    gradientColor = _colorSettings.OceanColorGradient.Evaluate(i / (TextureResolution - 1f));
                }
                else
                {
                    gradientColor = def != null
                        ? def.ColorGradient.Evaluate((i - TextureResolution) / (TextureResolution - 1f))
                        : Color.magenta;
                }

                if (def != null)
                {
                    Color tint = def.TintColor;
                    colors[colorIndex] = gradientColor * (1 - def.TintPercent) + tint * def.TintPercent;
                }
                else
                {
                    colors[colorIndex] = gradientColor;
                }
                colorIndex++;
            }
        }

        _texture.SetPixels(colors);
        _texture.Apply();
        _colorSettings.PlanetMaterial.SetTexture("_Texture", _texture);
    }
}
