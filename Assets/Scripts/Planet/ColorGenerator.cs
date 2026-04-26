using UnityEngine;

public class ColorGenerator : IBiomeProvider, IColorProvider
{
    ColorSettings _colorSettings;
    ITemperatureProvider _temperatureProvider;
    IMoistureProvider _moistureProvider;
    IBiomeRegistry _biomeRegistry;
    Color[] _biomeColors;

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

        BuildBiomeColorLookup();
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

    public void UpdateElevation(float min, float max) { }

    public BiomeResult EvaluateBiome(Vector3 pointOnUnitSphere, float elevation)
    {
        if (_biomeRegistry == null || _temperatureProvider == null || _moistureProvider == null)
            return new BiomeResult(BiomeType.Grassland, 0.5f, 0.5f);

        float temperature = _temperatureProvider.Evaluate(pointOnUnitSphere);
        float moisture = _moistureProvider.Evaluate(pointOnUnitSphere);

        return _biomeRegistry.Resolve(temperature, moisture, elevation);
    }

    public Color GetBiomeColor(Vector3 pointOnUnitSphere, float elevation)
    {
        if (_biomeRegistry == null || _temperatureProvider == null || _moistureProvider == null)
            return Color.magenta;

        var registry = _colorSettings.BiomeSettings.Registry;

        // Ocean
        if (elevation < registry.OceanThreshold)
            return _biomeColors[0];

        float temperature = _temperatureProvider.Evaluate(pointOnUnitSphere);
        float moisture = _moistureProvider.Evaluate(pointOnUnitSphere);

        // Mountain
        if (elevation > registry.MountainThreshold)
            return temperature < 0.4f ? _biomeColors[_biomeColors.Length - 1] : _biomeColors[_biomeColors.Length - 2];

        // Beach
        if (elevation < registry.OceanThreshold + registry.BeachWidth)
            return _biomeColors[1];

        // Grid biome with blending
        float tempCont = Mathf.Clamp01(temperature) * (registry.TemperatureSteps - 1);
        float moistCont = Mathf.Clamp01(moisture) * (registry.MoistureSteps - 1);

        int tempIdx = Mathf.Clamp(Mathf.FloorToInt(tempCont), 0, registry.TemperatureSteps - 1);
        int moistIdx = Mathf.Clamp(Mathf.FloorToInt(moistCont), 0, registry.MoistureSteps - 1);
        float tempFrac = tempCont - tempIdx;
        float moistFrac = moistCont - moistIdx;

        int primaryIdx = tempIdx * registry.MoistureSteps + moistIdx + 2;
        Color primary = _biomeColors[Mathf.Clamp(primaryIdx, 0, _biomeColors.Length - 1)];

        // Blend toward nearest neighbor
        float tempDist = Mathf.Abs(tempFrac - 0.5f);
        float moistDist = Mathf.Abs(moistFrac - 0.5f);

        int neighborIdx = primaryIdx;
        float frac = 0f;

        if (tempDist < moistDist)
        {
            if (moistFrac > 0.5f && moistIdx < registry.MoistureSteps - 1)
            {
                neighborIdx = primaryIdx + 1;
                frac = (moistFrac - 0.5f) * 2f;
            }
            else if (moistFrac < 0.5f && moistIdx > 0)
            {
                neighborIdx = primaryIdx - 1;
                frac = (0.5f - moistFrac) * 2f;
            }
        }
        else
        {
            if (tempFrac > 0.5f && tempIdx < registry.TemperatureSteps - 1)
            {
                neighborIdx = primaryIdx + registry.MoistureSteps;
                frac = (tempFrac - 0.5f) * 2f;
            }
            else if (tempFrac < 0.5f && tempIdx > 0)
            {
                neighborIdx = primaryIdx - registry.MoistureSteps;
                frac = (0.5f - tempFrac) * 2f;
            }
        }

        if (neighborIdx != primaryIdx)
        {
            Color neighbor = _biomeColors[Mathf.Clamp(neighborIdx, 0, _biomeColors.Length - 1)];
            return Color.Lerp(primary, neighbor, frac * 0.5f);
        }

        return primary;
    }

    public void UpdateColors() { }

    void BuildBiomeColorLookup()
    {
        if (_biomeRegistry == null) return;

        var registry = _colorSettings.BiomeSettings.Registry;
        int count = _biomeRegistry.BiomeCount;
        _biomeColors = new Color[count];

        for (int i = 0; i < count; i++)
        {
            var def = registry.GetDefinitionByIndex(i);
            if (def != null)
                _biomeColors[i] = def.ColorGradient.Evaluate(0.5f) * (1 - def.TintPercent) + def.TintColor * def.TintPercent;
            else
                _biomeColors[i] = Color.magenta;
        }
    }
}
