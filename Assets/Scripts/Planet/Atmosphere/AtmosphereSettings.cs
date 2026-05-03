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

    [Header("Sun Disc")]
    [Range(0.99f, 0.9999f)] public float SunDiscSize = 0.9995f;
    [Range(0.0001f, 0.01f)] public float SunDiscBlend = 0.002f;

    [Header("Optical Depth LUT")]
    [Range(64, 512)] public int BakeTextureSize = 256;
    [Range(16, 128)] public int BakeSteps = 64;

    [Header("Debug")]
    [Range(0, 5)] public int DebugMode = 0;

    [Header("Stars")]
    [Range(10, 100)] public float StarDensity = 40f;
    [Range(0.5f, 5f)] public float StarBrightness = 2f;
}
