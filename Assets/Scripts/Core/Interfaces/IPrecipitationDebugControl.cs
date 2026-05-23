using UnityEngine;

public interface IPrecipitationDebugControl
{
    bool PrecipitationRenderingEnabled { get; set; }
    bool LocalPrecipitationParticlesEnabled { get; }
    bool ShouldRenderLocalParticles(Camera camera);
}
