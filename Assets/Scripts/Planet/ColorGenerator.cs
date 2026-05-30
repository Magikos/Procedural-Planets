using UnityEngine;

public class ColorGenerator : IBiomeProvider
{
    BiomeSettings _biomeSettings;
    ITemperatureProvider _temperatureProvider;
    IMoistureProvider _moistureProvider;
    IBiomeRegistry _biomeRegistry;
    Color[] _biomeColors;

    public void Configure(BiomeSettings settings)
    {
        _biomeSettings = settings;

        if (_biomeSettings != null && _biomeSettings.Registry != null)
        {
            _biomeRegistry = _biomeSettings.Registry;
            _temperatureProvider = new TemperatureProvider(
                _biomeSettings.TemperatureNoise,
                _biomeSettings.TemperatureNoiseStrength);
            _moistureProvider = new MoistureProvider(_biomeSettings.MoistureNoise);
        }

        BuildBiomeColorLookup();
    }

    public void Initialize(int seed)
    {
        _temperatureProvider?.Initialize(seed);
        _moistureProvider?.Initialize(seed + 100);
    }

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
        return EvaluateBiomeColor(pointOnUnitSphere, elevation, out _, out _, out _);
    }

    public Color GetBiomeColorAndData(Vector3 pointOnUnitSphere, float elevation, out Vector4 biomeData)
    {
        Color color = EvaluateBiomeColor(pointOnUnitSphere, elevation, out float temperature, out float moisture, out int primaryBiomeIndex);
        float latitude01 = Mathf.Abs(pointOnUnitSphere.y);
        float biomeCount = _biomeColors != null && _biomeColors.Length > 0 ? _biomeColors.Length : 1f;
        biomeData = new Vector4(temperature, moisture, primaryBiomeIndex / biomeCount, latitude01);
        return color;
    }

    // Shared color-resolution path. Returns the final blended color and writes back the raw
    // signals (temperature, moisture, primary biome index) used by the diagnostic surface.
    Color EvaluateBiomeColor(Vector3 pointOnUnitSphere, float elevation,
        out float temperature, out float moisture, out int primaryBiomeIndex)
    {
        temperature = 0f;
        moisture = 0f;
        primaryBiomeIndex = 0;

        if (_biomeRegistry == null || _temperatureProvider == null || _moistureProvider == null)
            return Color.magenta;

        var registry = _biomeSettings.Registry;

        if (elevation < registry.OceanThreshold)
        {
            primaryBiomeIndex = 0;
            return _biomeColors[0];
        }

        temperature = _temperatureProvider.Evaluate(pointOnUnitSphere);
        moisture = _moistureProvider.Evaluate(pointOnUnitSphere);

        if (elevation > registry.MountainThreshold)
        {
            primaryBiomeIndex = temperature < 0.4f ? _biomeColors.Length - 1 : _biomeColors.Length - 2;
            return _biomeColors[primaryBiomeIndex];
        }

        if (elevation < registry.OceanThreshold + registry.BeachWidth)
        {
            primaryBiomeIndex = 1;
            return _biomeColors[1];
        }

        float tempCont = Mathf.Clamp01(temperature) * (registry.TemperatureSteps - 1);
        float moistCont = Mathf.Clamp01(moisture) * (registry.MoistureSteps - 1);

        int tempIdx = Mathf.Clamp(Mathf.FloorToInt(tempCont), 0, registry.TemperatureSteps - 1);
        int moistIdx = Mathf.Clamp(Mathf.FloorToInt(moistCont), 0, registry.MoistureSteps - 1);
        float tempFrac = tempCont - tempIdx;
        float moistFrac = moistCont - moistIdx;

        int primaryIdx = tempIdx * registry.MoistureSteps + moistIdx + 2;
        primaryBiomeIndex = Mathf.Clamp(primaryIdx, 0, _biomeColors.Length - 1);
        Color primary = _biomeColors[primaryBiomeIndex];

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

    void BuildBiomeColorLookup()
    {
        if (_biomeRegistry == null) return;

        var registry = _biomeSettings.Registry;
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
