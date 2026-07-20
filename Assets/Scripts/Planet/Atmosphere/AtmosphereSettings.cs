using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Atmosphere Settings")]
public class AtmosphereSettings : ScriptableObject
{
    [Header("Scale")]
    [Range(1.01f, 1.5f)]
    public float AtmosphereScale = 1.1f;

    [Header("Ray March")]
    [Range(4, 32)] public int ViewSteps = 16;
    [Range(4, 16)] public int SunSteps = 8;

    [Header("Scattering")]
    [Range(1f, 100f)] public float SunIntensity = 17f;

    [Header("Rayleigh (Sky Color)")]
    public Vector3 RayleighScattering = new Vector3(1.2e-3f, 2.8e-3f, 6.9e-3f);
    [Range(0.01f, 0.5f), Tooltip("Fraction of atmosphere thickness")]
    public float RayleighScaleHeight = 0.08f;

    [Header("Mie (Haze / Sun Glow)")]
    [Range(0f, 0.1f)] public float MieScattering = 0.002f;
    [Range(0.01f, 0.5f), Tooltip("Fraction of atmosphere thickness")]
    public float MieScaleHeight = 0.02f;
    [Range(0f, 0.99f)] public float MieAnisotropy = 0.76f;

    [Header("Terrain Aerial Perspective")]
    [Min(0f), Tooltip("Non-water terrain closer than this remains fully clear. Sky and water are unaffected.")]
    public float TerrainClarityDistance = 175f;
    [Min(1f), Tooltip("Existing atmospheric scattering is fully restored by this terrain distance.")]
    public float TerrainAtmosphereDistance = 1600f;

    [Header("Sun Disc")]
    [Range(0.99f, 0.9999f)] public float SunDiscSize = 0.9995f;
    public const float SunDiscBlendMin = 0.0001f;
    public const float SunDiscBlendMax = 0.05f;
    [Range(SunDiscBlendMin, SunDiscBlendMax)] public float SunDiscBlend = 0.002f;
    // 0 = no hard disc (sun represented only by atmosphere glow + god rays; behind cloud it
    // vanishes instead of bleeding through as a hard circle). 1 = full disc. Middle = a dim
    // soft disc that still centres the clear-sky sun without the bad cloud bleed-through.
    [Range(0f, 2f)] public float SunDiscIntensity = 0.25f;

    [Header("Sun Aureole (low-sun bloom)")]
    // Soft radial glow around the sun that fills in at low sun angles, where single-scatter
    // Mie collapses from sun extinction and leaves the disc a bare circle. Weighted by low-sun
    // factor so it fades out by midday (Mie already blooms there). 0 = off.
    [Range(0f, 4f)] public float SunAureoleStrength = 2f;
    [Range(1f, 200f)] public float SunAureolePower = 200f;

    [Header("Light Shafts")]
    public bool EnableLightShafts = true;
    [Range(0, 32)] public int LightShaftSamples = 20;
    [Range(0f, 3f)] public float LightShaftStrength = 1.5f;
    [Range(0.1f, 2f)] public float LightShaftDensity = 0.78f;
    [Range(0.7f, 0.99f)] public float LightShaftDecay = 0.95f;
    [Range(0f, 0.5f)] public float LightShaftWeight = 0.09f;
    [Range(0f, 2f)] public float LightShaftExposure = 0.68f;
    [Range(0f, 1.5f)] public float LightShaftThreshold = 0.32f;

    [Header("Optical Depth LUT")]
    [Range(64, 512)] public int BakeTextureSize = 256;
    [Range(16, 128)] public int BakeSteps = 64;

    [Header("Debug")]
    [Range(0, 5)] public int DebugMode = 0;
}
