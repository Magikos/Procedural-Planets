using UnityEngine;

/// <summary>
/// Anything in the world that can push grass aside implements this. The grass shader
/// reads the registry's packed buffer each frame; it doesn't know or care about the
/// concrete consumer types (debug sphere today, player character later, projectiles /
/// animals / magic AOEs eventually). All implementers self-register with
/// <see cref="GrassInteractorRegistry"/> on enable.
/// </summary>
public interface IGrassInteractor
{
    /// <summary>World-space position. Read fresh each upload (interactors are dynamic).</summary>
    Vector3 WorldPosition { get; }

    /// <summary>Radius of the bend effect in meters. Smoothstepped falloff to zero at this distance.</summary>
    float Radius { get; }

    /// <summary>Bend strength multiplier, typically 0-1. Scales with radius in the shader.</summary>
    float Strength { get; }

    /// <summary>Seconds for grass at the previous position to recover after this interactor moves away.</summary>
    float ReleaseSeconds { get; }

    /// <summary>If false, this interactor is skipped during packing (no GPU slot consumed).</summary>
    bool IsActive { get; }
}
