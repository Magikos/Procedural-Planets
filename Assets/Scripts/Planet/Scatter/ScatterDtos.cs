using System.Collections.Generic;
using UnityEngine;

public sealed record ScatterPrototypeDto(
    string DisplayName,
    int SlotId,
    float SpacingMeters,
    BiomeType Biome,
    float BiomeBlendPower,
    float Weight,
    float MaxSlopeDegrees,
    float SlopeFadeDegrees,
    bool HasMinAltitude, float MinAltitudeMeters,
    bool HasMaxAltitude, float MaxAltitudeMeters,
    float MinWaterClearanceMeters,
    Vector2 ScaleRange,
    bool RandomYaw,
    ScatterInteraction Interaction,
    Material Material,
    Mesh[] LodMeshes,
    float[] LodEndDistances,
    bool CastShadows,
    bool ReceiveShadows)
{
    // Raw map only; ScatterLibraryDto.EnsureValid is the single validator (assets + overrides).
    public static ScatterPrototypeDto From(ScatterPrototype p) => new(
        p.DisplayName, p.SlotId, p.SpacingMeters, p.Biome, p.BiomeBlendPower, p.Weight,
        p.MaxSlopeDegrees, p.SlopeFadeDegrees,
        p.HasMinAltitude, p.MinAltitudeMeters, p.HasMaxAltitude, p.MaxAltitudeMeters,
        p.MinWaterClearanceMeters, p.ScaleRange, p.RandomYaw, p.Interaction,
        p.Material, p.LodMeshes ?? System.Array.Empty<Mesh>(),
        p.LodEndDistances ?? System.Array.Empty<float>(), p.CastShadows, p.ReceiveShadows);

    // True when this prototype has enough to draw. Render data is optional — a prototype with no
    // mesh/material is still placed (SP1), just not rendered (SP2).
    public bool CanRender => Material != null && LodMeshes.Length > 0 && LodMeshes[0] != null;
}

public sealed record ScatterLibraryDto(ScatterPrototypeDto[] Prototypes)
{
    public static ScatterLibraryDto From(ScatterLibrary src)
    {
        var protos = src != null ? src.Prototypes : null;
        if (protos == null)
        {
            var empty = new ScatterLibraryDto(System.Array.Empty<ScatterPrototypeDto>());
            empty.EnsureValid();
            return empty;
        }

        var dtos = new ScatterPrototypeDto[protos.Length];
        for (int i = 0; i < protos.Length; i++)
        {
            if (protos[i] == null)
                throw new System.InvalidOperationException($"ScatterLibrary has a null prototype at index {i}.");
            dtos[i] = ScatterPrototypeDto.From(protos[i]);
        }
        var dto = new ScatterLibraryDto(dtos);
        dto.EnsureValid();
        return dto;
    }

    // Sole authority on DTO invariants. Called from From (default assets) and from
    // ScatterField.Configure on the final, possibly-overridden DTO — world overrides replace the
    // registered DTO without going through From. An empty library is valid (a world with no props).
    public void EnsureValid()
    {
        if (Prototypes == null)
            throw new System.InvalidOperationException("ScatterLibraryDto has a null prototype array.");

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        var seen = new HashSet<int>();
        for (int i = 0; i < Prototypes.Length; i++)
        {
            var p = Prototypes[i];
            string who = p?.DisplayName ?? $"index {i}";
            void Fail(string why) => throw new System.InvalidOperationException($"Scatter prototype '{who}': {why}");

            if (p == null) Fail("is null.");
            if (p.SlotId < 0 || p.SlotId > ScatterId.MaxSlot) Fail($"SlotId {p.SlotId} out of range 0-{ScatterId.MaxSlot}.");
            if (!seen.Add(p.SlotId)) Fail($"duplicate SlotId {p.SlotId}.");

            if (!Finite(p.SpacingMeters) || p.SpacingMeters <= 0f) Fail("SpacingMeters must be finite and positive.");
            if (!Finite(p.BiomeBlendPower) || p.BiomeBlendPower <= 0f) Fail("BiomeBlendPower must be finite and positive.");
            if (!Finite(p.Weight) || p.Weight < 0f) Fail("Weight must be finite and non-negative.");
            if (!Finite(p.MaxSlopeDegrees) || p.MaxSlopeDegrees < 0f) Fail("MaxSlopeDegrees must be finite and non-negative.");
            if (!Finite(p.SlopeFadeDegrees) || p.SlopeFadeDegrees < 0f) Fail("SlopeFadeDegrees must be finite and non-negative.");
            if (p.MaxSlopeDegrees + p.SlopeFadeDegrees > 90f) Fail($"MaxSlope + fade ({p.MaxSlopeDegrees}+{p.SlopeFadeDegrees}) exceeds 90 deg.");
            if (!Finite(p.MinAltitudeMeters) || !Finite(p.MaxAltitudeMeters)) Fail("altitude bounds must be finite.");
            if (p.HasMinAltitude && p.HasMaxAltitude && p.MinAltitudeMeters > p.MaxAltitudeMeters)
                Fail($"min altitude {p.MinAltitudeMeters} > max {p.MaxAltitudeMeters}.");
            if (!Finite(p.MinWaterClearanceMeters) || p.MinWaterClearanceMeters < 0f) Fail("MinWaterClearanceMeters must be finite and non-negative.");
            if (!Finite(p.ScaleRange.x) || !Finite(p.ScaleRange.y) || p.ScaleRange.x <= 0f || p.ScaleRange.x > p.ScaleRange.y)
                Fail($"ScaleRange {p.ScaleRange} must be positive and non-inverted.");
            if (!System.Enum.IsDefined(typeof(BiomeType), p.Biome)) Fail($"undefined biome {(int)p.Biome}.");
            if (!System.Enum.IsDefined(typeof(ScatterInteraction), p.Interaction)) Fail($"undefined interaction {(int)p.Interaction}.");

            // Render data is optional; validate its shape only when meshes are assigned.
            if (p.LodMeshes.Length > 0)
            {
                if (p.LodEndDistances.Length != p.LodMeshes.Length)
                    Fail($"LodMeshes ({p.LodMeshes.Length}) and LodEndDistances ({p.LodEndDistances.Length}) length mismatch.");
                float prev = 0f;
                for (int m = 0; m < p.LodEndDistances.Length; m++)
                {
                    float d = p.LodEndDistances[m];
                    if (!Finite(d) || d <= prev) Fail($"LodEndDistances must be finite and strictly ascending; entry {m} = {d}.");
                    prev = d;
                }
            }
        }
    }
}
