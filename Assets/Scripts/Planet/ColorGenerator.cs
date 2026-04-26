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
        if (_texture == null || _texture.width != 4 || _texture.height != biomeCount)
        {
            _texture = new Texture2D(4, biomeCount, TextureFormat.RGBA32, false);
            _texture.filterMode = FilterMode.Point;
            _texture.wrapMode = TextureWrapMode.Clamp;
        }
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
        if (_biomeRegistry == null || _temperatureProvider == null || _moistureProvider == null)
            return new BiomeResult(BiomeType.Grassland, 0.5f, 0.5f);

        float temperature = _temperatureProvider.Evaluate(pointOnUnitSphere);
        float moisture = _moistureProvider.Evaluate(pointOnUnitSphere);

        return _biomeRegistry.Resolve(temperature, moisture, elevation);
    }

    public float BiomePercentFromPoint(Vector3 pointOnUnitSphere, float elevation)
    {
        if (_biomeRegistry == null || _temperatureProvider == null || _moistureProvider == null)
            return 0f;

        var registry = _colorSettings.BiomeSettings.Registry;
        int totalBiomes = _biomeRegistry.BiomeCount;
        if (totalBiomes <= 1) return 0f;

        if (elevation < registry.OceanThreshold)
            return 0f / (totalBiomes - 1);

        float temperature = _temperatureProvider.Evaluate(pointOnUnitSphere);
        float moisture = _moistureProvider.Evaluate(pointOnUnitSphere);

        // Continuous grid position (fractional = between biome rows)
        float tempCont = Mathf.Clamp01(temperature) * (registry.TemperatureSteps - 1);
        float moistCont = Mathf.Clamp01(moisture) * (registry.MoistureSteps - 1);

        int tempIdx = Mathf.Clamp(Mathf.FloorToInt(tempCont), 0, registry.TemperatureSteps - 1);
        int moistIdx = Mathf.Clamp(Mathf.FloorToInt(moistCont), 0, registry.MoistureSteps - 1);
        float tempFrac = tempCont - tempIdx;
        float moistFrac = moistCont - moistIdx;

        // Base grid index + fractional blend toward neighbor
        float gridIndex = tempIdx * registry.MoistureSteps + moistIdx;

        // Blend along the dominant axis
        int neighborOffset = 0;
        float frac = 0f;
        if (Mathf.Abs(tempFrac - 0.5f) < Mathf.Abs(moistFrac - 0.5f))
        {
            // Moisture is closer to a boundary
            if (moistFrac > 0.5f && moistIdx < registry.MoistureSteps - 1)
            {
                neighborOffset = 1;
                frac = (moistFrac - 0.5f) * 2f;
            }
            else if (moistFrac < 0.5f && moistIdx > 0)
            {
                neighborOffset = -1;
                frac = (0.5f - moistFrac) * 2f;
            }
        }
        else
        {
            // Temperature is closer to a boundary
            if (tempFrac > 0.5f && tempIdx < registry.TemperatureSteps - 1)
            {
                neighborOffset = registry.MoistureSteps;
                frac = (tempFrac - 0.5f) * 2f;
            }
            else if (tempFrac < 0.5f && tempIdx > 0)
            {
                neighborOffset = -registry.MoistureSteps;
                frac = (0.5f - tempFrac) * 2f;
            }
        }

        // Smooth blend: interpolate between current and neighbor grid row
        float blendedIndex = gridIndex + 2;
        if (neighborOffset != 0)
        {
            float neighborIndex = gridIndex + neighborOffset + 2;
            blendedIndex = Mathf.Lerp(blendedIndex, neighborIndex, frac * 0.5f);
        }

        float gridPercent = blendedIndex / (totalBiomes - 1);

        // Mountain override at high elevation
        if (elevation > registry.MountainThreshold)
        {
            int gridCount = registry.GridEntries != null ? registry.GridEntries.Length : 0;
            float mountainIdx = temperature < 0.4f ? gridCount + 3f : gridCount + 2f;
            return mountainIdx / (totalBiomes - 1);
        }

        // Beach override at sea level
        if (elevation < registry.OceanThreshold + registry.BeachWidth)
            return 1f / (totalBiomes - 1);

        return gridPercent;
    }

    public void UpdateColors()
    {
        if (_biomeRegistry == null) return;

        var registry = _colorSettings.BiomeSettings.Registry;
        int biomeCount = _biomeRegistry.BiomeCount;
        int width = 4;

        if (_texture == null || _texture.width != width || _texture.height != biomeCount)
        {
            _texture = new Texture2D(width, biomeCount, TextureFormat.RGBA32, false);
            _texture.filterMode = FilterMode.Point;
            _texture.wrapMode = TextureWrapMode.Clamp;
        }

        Color[] colors = new Color[width * biomeCount];
        for (int b = 0; b < biomeCount; b++)
        {
            var def = registry.GetDefinitionByIndex(b);
            Color c;
            if (def != null)
                c = def.ColorGradient.Evaluate(0.5f) * (1 - def.TintPercent) + def.TintColor * def.TintPercent;
            else
                c = Color.magenta;

            for (int x = 0; x < width; x++)
                colors[b * width + x] = c;
        }

        _texture.SetPixels(colors);
        _texture.Apply();
        _colorSettings.PlanetMaterial.SetTexture("_Texture", _texture);

        // Debug: log texture row colors
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== TEXTURE ROWS ===");
        for (int b = 0; b < biomeCount; b++)
        {
            var def = registry.GetDefinitionByIndex(b);
            string name = def != null ? def.name : "NULL";
            sb.AppendLine($"Row {b}: ({colors[b].r:F2}, {colors[b].g:F2}, {colors[b].b:F2}) = {name}");
        }
        Debug.Log(sb.ToString());
    }
}
