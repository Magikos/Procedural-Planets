using System.Collections.Generic;
using UnityEngine;

// Immutable snapshot of one renderable part (material + LOD mesh chain). CastShadows/ReceiveShadows
// are per part so a trunk can cast while its cutout foliage does not.
public sealed record ScatterPartDto(
    Material Material,
    Mesh[] LodMeshes,
    float[] LodEndDistances,
    bool CastShadows,
    bool ReceiveShadows)
{
    public bool CanRender => Material != null && LodMeshes.Length > 0 && LodMeshes[0] != null;
    public float MaxCullDistance => LodEndDistances.Length > 0 ? LodEndDistances[LodEndDistances.Length - 1] : 0f;
}

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
    ScatterPartDto[] Parts)
{
    // Raw map only; ScatterLibraryDto.EnsureValid is the single validator (assets + overrides).
    public static ScatterPrototypeDto From(ScatterPrototype p) => new(
        p.DisplayName, p.SlotId, p.SpacingMeters, p.Biome, p.BiomeBlendPower, p.Weight,
        p.MaxSlopeDegrees, p.SlopeFadeDegrees,
        p.HasMinAltitude, p.MinAltitudeMeters, p.HasMaxAltitude, p.MaxAltitudeMeters,
        p.MinWaterClearanceMeters, p.ScaleRange, p.RandomYaw, p.Interaction,
        BuildParts(p));

    static ScatterPartDto[] BuildParts(ScatterPrototype p)
    {
        if (p.Parts != null && p.Parts.Length > 0)
        {
            var parts = new ScatterPartDto[p.Parts.Length];
            for (int i = 0; i < p.Parts.Length; i++)
            {
                var sp = p.Parts[i];
                parts[i] = new ScatterPartDto(
                    sp?.Material,
                    sp?.LodMeshes ?? System.Array.Empty<Mesh>(),
                    sp?.LodEndDistances ?? System.Array.Empty<float>(),
                    sp?.CastShadows ?? true,
                    sp?.ReceiveShadows ?? true);
            }
            return parts;
        }
        // Legacy single-material fallback for prototype assets authored before Parts existed.
        if (p.Material != null || (p.LodMeshes != null && p.LodMeshes.Length > 0))
            return new[]
            {
                new ScatterPartDto(
                    p.Material,
                    p.LodMeshes ?? System.Array.Empty<Mesh>(),
                    p.LodEndDistances ?? System.Array.Empty<float>(),
                    p.CastShadows, p.ReceiveShadows)
            };
        return System.Array.Empty<ScatterPartDto>();
    }

    // True when any part has enough to draw. Render data is optional — a prototype with no drawable
    // part is still placed (SP1), just not rendered (SP2).
    public bool CanRender
    {
        get { foreach (var part in Parts) if (part.CanRender) return true; return false; }
    }

    // Farthest cull across drawable parts — the prototype's mesh-LOD draw radius.
    public float MaxCullDistance
    {
        get { float m = 0f; foreach (var part in Parts) if (part.CanRender && part.MaxCullDistance > m) m = part.MaxCullDistance; return m; }
    }

    // Far-field impostor policy, derived (not authored): a prototype whose mesh cull reaches
    // >= ImpostorMinMeshCull gets a billboard tier past its mesh cull out to ImpostorRangeMultiplier x
    // that cull. The cutoff sits just below the bush/rock cull band (120/250) so mid-size props reach far
    // like trees; tiny ground clutter (flowers, mushrooms, grass at <=90) stays short-range. Kept here so
    // gather (ScatterField) and draw (ScatterRenderer) agree on which prototypes reach far and how far.
    const float ImpostorMinMeshCull = 120f;
    // Impostors reach this multiple of the mesh cull. The incremental tile cache makes far tiles cheap to
    // draw, so the tree line pushes well toward the horizon. Cost is the gather: FarGatherRadius scales
    // with this, so the gathered disc area grows as the square — higher = a farther tree line but a slower
    // cold fill (near-first ordering keeps the foreground fast; the far ring trickles in).
    const float ImpostorRangeMultiplier = 3.0f;

    public bool HasImpostor => CanRender && MaxCullDistance >= ImpostorMinMeshCull;
    public float ImpostorStartDistance => MaxCullDistance * 0.85f; // cross-fade in over the mesh dither-out band
    public float ImpostorEndDistance => HasImpostor ? MaxCullDistance * ImpostorRangeMultiplier : 0f;

    // How far the placement gather must reach for this prototype: its impostor end if it has one, else
    // its mesh cull. 0 for placement-only prototypes (no drawable part).
    public float FarGatherRadius => HasImpostor ? ImpostorEndDistance : MaxCullDistance;
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
            if (p.Parts == null) Fail("Parts array is null.");

            // Render data is optional; validate the LOD shape of each part that has meshes assigned.
            for (int k = 0; k < p.Parts.Length; k++)
            {
                var part = p.Parts[k];
                if (part == null) Fail($"part {k} is null.");
                if (part.LodMeshes.Length == 0) continue;
                if (part.LodEndDistances.Length != part.LodMeshes.Length)
                    Fail($"part {k}: LodMeshes ({part.LodMeshes.Length}) and LodEndDistances ({part.LodEndDistances.Length}) length mismatch.");
                float prev = 0f;
                for (int m = 0; m < part.LodEndDistances.Length; m++)
                {
                    float d = part.LodEndDistances[m];
                    if (!Finite(d) || d <= prev) Fail($"part {k}: LodEndDistances must be finite and strictly ascending; entry {m} = {d}.");
                    prev = d;
                }
            }
        }
    }
}
