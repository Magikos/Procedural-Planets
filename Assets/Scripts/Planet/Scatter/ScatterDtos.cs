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
    float ConformToSlope,
    bool HasMinAltitude, float MinAltitudeMeters,
    bool HasMaxAltitude, float MaxAltitudeMeters,
    float MinWaterClearanceMeters,
    bool OnWater,
    Vector2 ScaleRange,
    bool RandomYaw,
    ScatterInteraction Interaction,
    ScatterPartDto[] Parts,
    Texture2D BakedImpostorAtlas = null,
    Texture2D BakedImpostorNormal = null,
    Mesh StumpMesh = null,
    Material StumpMaterial = null,
    string ImpostorShareKey = null,
    float Clumpiness = 0f,
    float PatchScaleMeters = 250f,
    float ShadePreference = 0f)
{
    // Which grove field this prototype draws from. Keyed on the SPECIES (ImpostorShareKey) rather than the
    // prototype, so the per-instance variants of one species share one field — otherwise a grove of variant 0
    // lands in a clearing of variant 1 and the whole effect averages back out to uniform.
    public uint ClumpGroupSeed => ScatterClumping.GroupSeedFor(ImpostorShareKey ?? DisplayName);

    // The trunk = the first drawable part (Synty scatter-tree convention). Used for stumps and fallen logs so
    // foliage (which splays flat when the tree lies down) is excluded.
    public ScatterPartDto TrunkPart
    {
        get
        {
            if (Parts != null)
                foreach (ScatterPartDto p in Parts)
                    if (p != null && p.CanRender) return p;
            return null;
        }
    }

    // The stump's fallback material (the trunk) when StumpMaterial is unset.
    public Material TrunkMaterial => TrunkPart?.Material;

    // Raw map only; ScatterLibraryDto.EnsureValid is the single validator (assets + overrides).
    public static ScatterPrototypeDto From(ScatterPrototype p) => new(
        p.DisplayName, p.SlotId, p.SpacingMeters, p.Biome, p.BiomeBlendPower, p.Weight,
        p.MaxSlopeDegrees, p.SlopeFadeDegrees, p.ConformToSlope,
        p.HasMinAltitude, p.MinAltitudeMeters, p.HasMaxAltitude, p.MaxAltitudeMeters,
        p.MinWaterClearanceMeters, p.OnWater, p.ScaleRange, p.RandomYaw, p.Interaction,
        BuildParts(p), p.BakedImpostorAtlas, p.BakedImpostorNormal, p.StumpMesh, p.StumpMaterial,
        null, p.Clumpiness, p.PatchScaleMeters, p.ShadePreference);

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

    // World-unit radius of the ground-contact footprint: the XZ half-extent of the LOWEST part (the one that
    // touches the ground). Placement sinks a partially-conformed prop by baseRadius*sin(tilt) so a wide base
    // doesn't float its downhill edge on a slope. Uses the lowest part, not the widest, so a tree's broad
    // canopy never counts — only the trunk that actually meets the surface. 0 (no mesh) => no sink.
    public float GroundContactRadius()
    {
        float lowestMinY = float.MaxValue;
        float radius = 0f;
        foreach (var part in Parts)
        {
            if (part?.LodMeshes == null || part.LodMeshes.Length == 0 || part.LodMeshes[0] == null) continue;
            Bounds b = part.LodMeshes[0].bounds;
            if (b.min.y < lowestMinY)
            {
                lowestMinY = b.min.y;
                radius = Mathf.Max(b.extents.x, b.extents.z);
            }
        }
        return radius;
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

    // Far-field impostor policy, derived (not authored). This value splits the two reach classes rather than
    // gating cards on or off: it sits just below the bush/rock cull band (120/250) so mid-size props reach far
    // like trees, while ground clutter (flowers, mushrooms, grass at <=90) keeps its authored radius. Kept
    // here so gather (ScatterField) and draw (ScatterRenderer) agree on which prototypes reach far and how far.
    const float ImpostorMinMeshCull = 120f;
    // Impostors reach this multiple of the mesh cull. The incremental tile cache makes far tiles cheap to
    // draw, so the tree line pushes well toward the horizon. Cost is the gather: FarGatherRadius scales
    // with this, so the gathered disc area grows as the square — higher = a farther tree line but a slower
    // cold fill (near-first ordering keeps the foreground fast; the far ring trickles in).
    const float ImpostorRangeMultiplier = 4.5f;

    // Where the last mesh band starts dithering out, as a fraction of its cull. Proportional, not a fixed
    // width: a 45 m flower cannot dither over the same 40 m a 250 m rock can without being half-transparent
    // for most of the range it is visible at. ScatterLodBatcher.FadeStartFor uses the same fraction for a
    // prototype with no impostor, so the mesh dissolves over the same relative window either way.
    public const float MeshFadeFraction = 0.85f;

    // The card has to take over while the mesh still resolves. Below ~36 px tall a mesh's own alpha-cutout
    // silhouette is mip-dominated and stops agreeing with itself - a beach reed at 8 px covers 2.2x the area
    // it does at 192 px, a dead tree at 17 px covers 0.55x - so whatever the card does, the swap steps.
    // Measured card/mesh coverage at the swap over all 61 prototypes with a card: on the authored distance
    // the ratio ran 0.80 / 1.08 / 1.13 (p10 / median / p90) with 13 prototypes stepping over 15%; handing
    // over at a fixed 36 px it runs 0.98 / 1.00 / 1.00 with 2. The value is a measured minimum, not a round
    // number - 32 px and 40 px both give 6-8 failures because a mesh's mip aliasing resonates with size.
    // It also stops a 0.7 m wildflower being drawn as a mesh out to 120 m, where it is 5 px tall and costs a
    // mesh draw to look like a smear.
    const float MeshHandoverPixels = 36f;

    // 1080 / (2 tan 30): screen pixels per metre of prop height, per metre of distance, at 1080p and a 60
    // degree vertical FOV. The handover is a fixed DISTANCE derived from this, not a live screen
    // measurement - gather radii and the GPU band buffers are built once at configure, so a per-frame size
    // would rebuild them on every zoom. A taller screen only makes props hand over ABOVE 36 px, which is
    // the safe way to be wrong.
    const float ReferencePixelsPerMetre = 935f;

    // Two classes of card, not one gate. FAR-REACHING props (mesh cull >= ImpostorMinMeshCull: trees, big
    // rocks) get a card that pushes their silhouette ImpostorRangeMultiplier x past the mesh - that is what
    // builds the tree line. GROUND CLUTTER (flowers, mushrooms, grass, coral) gets a card that only replaces
    // the far part of its OWN authored band: same gather radius, same draw distance, but the mesh stops at
    // MeshHandoverPixels instead of smearing out to a 6 px alpha-cutout blur. That is cheaper than the mesh it
    // replaces, so the short-range exclusion this rule used to make was costing draws, not saving them.
    bool FarReaching => MaxCullDistance >= ImpostorMinMeshCull;
    public bool HasImpostor => CanRender && (FarReaching || MeshHandoverDistance < MaxCullDistance);

    // Where a prop framed by these LOD0 meshes falls to MeshHandoverPixels tall. Public so the generated-prop
    // injections can lay their LOD boundaries inside the band that actually draws: a tier authored past this
    // never renders, because the card has already taken over.
    public static float HandoverDistanceFor(params Mesh[] lod0Meshes)
    {
        Bounds b = default;
        bool first = true;
        foreach (Mesh m in lod0Meshes)
        {
            if (m == null) continue;
            if (first) { b = m.bounds; first = false; } else b.Encapsulate(m.bounds);
        }
        if (first) return float.MaxValue;
        Vector3 v = b.size;
        return HandoverDistanceForSize(Mathf.Max(v.y, Mathf.Max(v.x, v.z)));
    }

    static float HandoverDistanceForSize(float sizeMeters) =>
        sizeMeters > 0f ? sizeMeters * ReferencePixelsPerMetre / MeshHandoverPixels : float.MaxValue;

    float MeshHandoverDistance => HandoverDistanceForSize(BoundsSizeMeters);

    // Largest dimension of the union of the drawable parts' LOD0 bounds - what decides the prop's height on
    // screen, and the same measure ScatterImpostorBaker frames its card by.
    public float BoundsSizeMeters
    {
        get
        {
            Bounds b = default;
            bool first = true;
            foreach (var part in Parts)
            {
                if (!part.CanRender) continue;
                Bounds pb = part.LodMeshes[0].bounds;
                if (first) { b = pb; first = false; } else b.Encapsulate(pb);
            }
            if (first) return 0f;
            Vector3 v = b.size;
            return Mathf.Max(v.y, Mathf.Max(v.x, v.z));
        }
    }

    // Where the mesh tier actually stops. A prototype with no card keeps its authored cull - shortening
    // that would only delete the prop early. One with a card hands over at MeshHandoverPixels, and never
    // later than the authored cull, so this can only ever REDUCE mesh draw distance.
    public float MeshCullDistance =>
        HasImpostor ? Mathf.Min(MaxCullDistance, MeshHandoverDistance) : MaxCullDistance;
    // The card takes over exactly where the mesh culls. It deliberately does NOT ramp in underneath the
    // still-solid mesh: a card is a square billboard and a tree is not, so every threshold the ramp clips
    // outside the mesh silhouette shows as a dithered ghost canopy beside the solid one for the whole band.
    public float ImpostorStartDistance => MeshCullDistance;
    public float ImpostorEndDistance =>
        !HasImpostor ? 0f : MaxCullDistance * (FarReaching ? ImpostorRangeMultiplier : 1f);

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
            if (!Finite(p.ConformToSlope) || p.ConformToSlope < 0f || p.ConformToSlope > 1f) Fail($"ConformToSlope {p.ConformToSlope} must be in 0..1.");
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
