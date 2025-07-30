using UnityEngine;

[System.Serializable]
public class NoiseSettings
{
    public enum FilterType { Simple, Ridged, };
    public FilterType filterType;

    //[ConditionalHide("filterType", 0)]
    public SimpleNoiseSettings simpleNoiseSettings;
    //[ConditionalHide("filterType", 1)]
    public RidgedNoiseSettings ridgedNoiseSettings;

    [System.Serializable]
    public class SimpleNoiseSettings
    {
        [Header("Height (in meters relative to planet surface)")]
        [Range(0, 500)] public float maxHeightMeters = 50f; // User-friendly height in meters

        [Header("Noise Parameters")]
        [Range(1, 8)] public int numLayers = 4;
        [Range(0.1f, 100f)] public float featureSize = 10f; // Size of features in km
        [Range(0.1f, 10f)] public float roughness = 2f;
        [Range(0.1f, 1f)] public float persistence = 0.5f;
        public Vector3 centre;
        [Range(0, 1)] public float minValue = 0f;

        // Internal calculated values (don't show in inspector)
        [HideInInspector] public float strength = 1f;
        [HideInInspector] public float baseRoughness = 1f;

        public void CalculateScaledValues(float planetRadius)
        {
            // Convert user-friendly values to internal values
            strength = maxHeightMeters / planetRadius; // Scale height relative to planet
            baseRoughness = 1f / (featureSize * 1000f / planetRadius); // Convert km to appropriate frequency
        }
    }

    [System.Serializable]
    public class RidgedNoiseSettings : SimpleNoiseSettings
    {
        [Range(0.1f, 2f)] public float weightMultiplier = 0.8f;
    }
}