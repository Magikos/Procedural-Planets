using UnityEngine;

public class ShapeGenerator
{
    ShapeSettings settings;
    INoiseFilter[] noiseFilters;

    public float planetRadius => settings.planetRadius;

    public ShapeGenerator(ShapeSettings settings)
    {
        this.settings = settings;
        noiseFilters = new INoiseFilter[settings.noiseLayers.Length];

        // Calculate scaled values for all noise layers based on planet radius
        for (int i = 0; i < settings.noiseLayers.Length; i++)
        {
            var layer = settings.noiseLayers[i];
            if (layer.noiseSettings.filterType == NoiseSettings.FilterType.Simple)
            {
                layer.noiseSettings.simpleNoiseSettings.CalculateScaledValues(settings.planetRadius);
            }
            else if (layer.noiseSettings.filterType == NoiseSettings.FilterType.Ridged)
            {
                layer.noiseSettings.ridgedNoiseSettings.CalculateScaledValues(settings.planetRadius);
            }

            noiseFilters[i] = NoiseFilterFactory.CreateNoiseFilter(layer.noiseSettings);
        }
    }

    public float CalculateUnscaledElevation(Vector3 pointOnUnitSphere)
    {
        float firstLayerValue = 0;
        float elevation = 0;

        if (noiseFilters.Length > 0)
        {
            firstLayerValue = noiseFilters[0].Evaluate(pointOnUnitSphere);
            if (settings.noiseLayers[0].enabled)
            {
                elevation = firstLayerValue;
            }
        }

        for (int i = 1; i < noiseFilters.Length; i++)
        {
            if (settings.noiseLayers[i].enabled)
            {
                float mask = (settings.noiseLayers[i].useFirstLayerAsMask) ? firstLayerValue : 1;
                elevation += noiseFilters[i].Evaluate(pointOnUnitSphere) * mask;
            }
        }
        return elevation;
    }

    public float GetScaledElevation(float unscaledElevation)
    {
        return settings.planetRadius * (1 + unscaledElevation);
    }
}