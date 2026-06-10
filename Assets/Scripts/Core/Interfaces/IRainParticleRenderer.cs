/// <summary>
/// Render-feature handoff for the world-anchored rain particle system. The
/// controller owns the persistent particle buffer and the material. Compute
/// dispatch happens in the controller's <c>Update</c> (engine-level, not
/// render-graph) so by the time the render feature draws each frame, the
/// buffer holds advanced particle state. The render feature only needs the
/// material and instance count to issue a procedural draw.
/// </summary>
public interface IRainParticleRenderer
{
    bool IsReadyToDraw { get; }

    /// <summary>Number of particle instances to draw.</summary>
    int ParticleCount { get; }

    /// <summary>Material whose vertex shader reads from the particle buffer.</summary>
    UnityEngine.Material Material { get; }
}
