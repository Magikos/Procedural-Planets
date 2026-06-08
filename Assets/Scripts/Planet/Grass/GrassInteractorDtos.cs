using UnityEngine;

// DTOs for the grass interactor system. Per the project's settings DTO pattern
// ([[feedback-settings-dto-pattern]]), the registry packs IGrassInteractor instances
// into immutable snapshots before talking to the GPU. Consumer shaders read the GPU
// struct only; C# bookkeeping uses the snapshot.

/// <summary>
/// Immutable snapshot of a single interactor at a frame boundary. Built once per
/// upload from <see cref="IGrassInteractor"/>; consumed by the registry's pack step.
/// </summary>
public readonly struct GrassInteractorSnapshot
{
    public readonly Vector3 WorldPosition;
    public readonly float Radius;
    public readonly float Strength;
    public readonly float ReleaseSeconds;

    public GrassInteractorSnapshot(
        Vector3 worldPosition,
        float radius,
        float strength,
        float releaseSeconds)
    {
        WorldPosition = worldPosition;
        Radius = radius;
        Strength = strength;
        ReleaseSeconds = releaseSeconds;
    }

    /// <summary>Composition root: ONE place that knows IGrassInteractor's shape.</summary>
    public static GrassInteractorSnapshot From(IGrassInteractor source) => new(
        source.WorldPosition,
        Mathf.Max(0f, source.Radius),
        Mathf.Clamp(source.Strength, 0f, 2f),
        Mathf.Clamp(source.ReleaseSeconds, 0.05f, 5f));
}

/// <summary>
/// GPU-side layout. Matches <c>struct GrassInteractor</c> in
/// <c>Assets/Graphics/Shaders/Includes/GrassInteractors.hlsl</c> exactly.
/// 32 bytes (two float4s). Type field is reserved for slice 7 (persistent path cutting);
/// always 0 in this slice.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct GrassInteractorGpu
{
    public Vector4 PositionRadius;  // xyz = world pos, w = radius
    public Vector4 StrengthType;    // x = strength, y = type (0 = transient), z/w = reserved
}
