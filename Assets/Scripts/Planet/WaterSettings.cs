using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Water Settings")]
public class WaterSettings : ScriptableObject
{
    [Header("Body colour")]
    public Color ShallowBaseColor = new Color(0.20f, 0.76f, 0.82f, 1f);
    public Color DeepBaseColor = new Color(0.00f, 0.018f, 0.065f, 1f);
    public Color FoamColor = new Color(0.88f, 0.98f, 0.94f, 0.9f);

    // Distances below are authored against this radius and scaled by PlanetRadius / ReferenceRadius,
    // so a larger planet keeps the same apparent water scale.
    [Header("Scale reference")]
    public float ReferenceRadius = 5000f;

    [Header("Depth and shore")]
    public float ShallowDepth = 28f;
    public float DeepDepth = 360f;
    [Tooltip("Water depth in metres covered by contact foam at shores and submerged objects.")]
    public float ShoreFoamDepth = 0.65f;
    public float ShoreRange = 125f;

    [Tooltip("Water depth in metres at which the volume tint starts, and where it reaches full strength. " +
             "This is the shoreline feather: a depth ramp, so it widens by itself on a shallow bank.")]
    public float EdgeFadeStartMeters = 0.1f;
    public float EdgeFadeEndMeters = 6.5f;

    [Tooltip("Same, for the ocean. Much shallower than a lake: an ocean shelf stays a few metres deep a long "
           + "way out, and open water is turbid enough that you should not see its bed.")]
    public float OceanEdgeFadeEndMeters = 1.5f;

    [Header("Waves")]
    public float WaveAmplitude = 3.4f;
    public float WaveScale = 480f;
    public float WaveSpeed = 0.58f;
    public float WaveNormalStrength = 4.5f;
    // The vertex swell drives geometry, so a CPU-side wave-height query must evaluate these same
    // numbers. Authoring them here rather than on the material keeps that single-sourced.
    public float SwellAmplitude = 5.0f;
    public float SwellWavelength = 90f;

    [Header("Surface effects")]
    public float MotionStrength = 0.24f;
    public float SunGlitterIntensity = 1.45f;
    public float SunGlitterPower = 1400f;
    public float ShoreFoamIntensity = 1.0f;
    public float WhitecapIntensity = 1.08f;

    [Header("Transparency")]
    public float Alpha = 0.36f;
    public float ShallowColorBlend = 0.68f;
    public float DeepColorBlend = 0.88f;
    public float ShallowAlphaFactor = 0.14f;
    public float ShallowAlphaMin = 0.10f;
    public float DeepAlphaMin = 0.96f;

    [Header("Freezing (0-1 normalized temperature)")]
    public float LakeFreezeStartTemperature01 = 0.36f;
    public float LakeFreezeCompleteTemperature01 = 0.26f;
    public float OceanFreezeStartTemperature01 = 0.20f;
    public float OceanFreezeCompleteTemperature01 = 0.10f;

    [Header("Ice")]
    public float IceOpacity = 0.88f;
    public float IceRoughness = 0.72f;
    public float IceNormalStrength = 0.35f;
    public float IceBreakupScale = 95f;

    [Header("Underwater")]
    [Tooltip("Multiplies the ambient light the water column falls to at night. 1 is the level derived from " +
             "the world's shared night ambient; raise it if midnight underwater reads too dark to play in.")]
    public float UnderwaterNightScale = 1f;

    [Tooltip("Strength of the underwater sun shafts. Their per-channel colour stays authored in " +
             "Atmosphere.shader, so this moves how strong they are without changing what colour they are.")]
    public float UnderwaterShaftIntensity = 1f;

    // Consumed by WaterVolumeRenderFeature, which runs for editor cameras with no active world and so
    // reads the frozen DTO rather than the world settings service.
    [Header("Volume")]
    public float RefractionStrength = 0.30f;
    public float CausticIntensity = 0.18f;
    public float CausticDepth = 12f;
    public float CausticContrast = 1.35f;
    public float CausticPrismStrength = 0.12f;
}
