using UnityEngine;

public interface IPrecipitationDebugControl
{
    bool PrecipitationRenderingEnabled { get; set; }
    bool LocalPrecipitationParticlesEnabled { get; }
    bool ShouldRenderLocalParticles(Camera camera);

    // Diagnostic surface so WeatherManager can publish precipitation state through
    // the registered service instead of FindAnyObjectByType<PrecipitationController>.
    bool IsRenderingEnabled { get; }
    float StormThreshold { get; }
    float LocalParticleRadius { get; }
    float LocalMaxCameraAltitude { get; }
    int DustParticleCount { get; }
    int SnowParticleCount { get; }
    int WeatherParticleProofMode { get; }
    string WeatherParticleSettingsSummary { get; }
}
