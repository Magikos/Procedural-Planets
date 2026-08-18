using Unity.Mathematics;

// Turns uniform scatter density into groves, clearings and stragglers (design: docs/design/2026-08-15-forest-clumping.md).
//
// Density today is uniform within a biome: every hectare of Forest gets statistically the same number of trees.
// That amount is correct — it reads as forest and stays walkable — but it is correct EVERYWHERE, so the world has
// no thickets, no meadows and no forest edge. This multiplies a spatial field into densityKeep so the average
// stays roughly what it was while the local density varies.
//
// TWO fields, per Bryan: a biome-wide OPENNESS that every prototype obeys (so a clearing is a clearing for
// everything, not a gap one species quietly fills), and a per-GROUP grove field on top (so the wooded parts
// still separate into oak stands and fir stands). Openness runs at a larger scale than groves, so groves live
// inside wooded regions rather than fighting them.
//
// Burst-safe by construction: pure float math on Unity.Mathematics.snoise, no managed state, so the CPU gather
// (ScatterField) and the Burst gather (ScatterGatherJob) can call the identical function and stay in parity.
public static class ScatterClumping
{
    // Openness features are this many times larger than grove features. Clearings should read as landscape;
    // groves as stands within it.
    const float OpennessScaleMultiple = 3.2f;

    // Below this the world is open, above it fully wooded. These are tuned for MEAN KEEP, not by eye: this is a
    // redistribution, so the average has to stay near 1 or enabling clumping just deletes most of the world's
    // trees. The first values tried (0.42/0.62 with a 0.25 floor) measured a mean of 0.33 — a two-thirds cull.
    const float OpenLo = 0.28f;
    const float OpenHi = 0.46f;

    // Groves never fully empty a wooded area on their own — that is openness's job. This is the floor a
    // prototype keeps outside its own groves, which is what leaves scattered stragglers between stands.
    const float GroveFloor = 0.55f;

    // How much the land itself decides where clearings fall. Flat ground biases open, slopes bias wooded, which
    // puts meadows in the hollows and flats where they read as intentional (and where you would want to build)
    // instead of noise dropping one on a cliff face.
    const float TerrainInfluence = 0.22f;

    /// <param name="dir">Unit direction from planet centre — the placement candidate, in local space.</param>
    /// <param name="planetWorldRadius">Sets the metres-per-radian conversion so PatchScale is real metres.</param>
    /// <param name="clumpiness">0 = today's uniform placement, bit for bit. 1 = full grove/clearing contrast.</param>
    /// <param name="patchScaleMeters">Grove diameter, roughly. Trees want 150-400 m; flower colonies 20-60 m.</param>
    /// <param name="groupSeed">Per-species (NOT per-prototype) so a species' variants share one grove field.</param>
    /// <param name="biomeSeed">Per-biome, so every prototype in a biome agrees on where the clearings are.</param>
    /// <param name="slopeCos">dot(surfaceNormal, dir): 1 = flat, lower = steeper.</param>
    // MEASURED mean keep (6000 samples, so authoring knows what it costs): clumpiness 0 -> 1.00 (bit-identical
    // to before), 0.5 -> 0.80, 1.0 -> 0.60 with 15% of the surface genuinely open. It redistributes but does not
    // fully preserve the total: at 1.0 the world carries ~40% fewer props. Raise the prototype's Weight by
    // roughly 1/mean if a biome should keep its old headcount. Compensating inside here does not work — the
    // caller clamps densityKeep to 1, so the boost would be eaten in exactly the dense areas that need it.
    /// <param name="shadePreference">
    /// Where in the openness field this prototype wants to sit. 0 = the old behaviour, thinning in clearings
    /// like everything else. Negative = prefers the OPEN ground between stands (meadow flowers). Positive =
    /// concentrates into the densest wood (mushrooms, shade plants).
    /// </param>
    public static float Keep(float3 dir, float planetWorldRadius, float clumpiness, float patchScaleMeters,
        uint groupSeed, uint biomeSeed, float slopeCos, float shadePreference = 0f)
    {
        if (clumpiness <= 0f) return 1f; // ships inert: untouched placement until a prototype opts in

        // A feature of N metres subtends N/R radians, so N metres of pattern is R/N in unit-sphere frequency.
        float groveFreq = planetWorldRadius / math.max(patchScaleMeters, 1f);
        float openFreq = groveFreq / OpennessScaleMultiple;

        float3 groveOffset = SeedOffset(groupSeed);
        float3 openOffset = SeedOffset(biomeSeed);

        float openness = Unit(noise.snoise(dir * openFreq + openOffset));
        float grove = Unit(noise.snoise(dir * groveFreq + groveOffset));

        // Flat ground pushes toward open, steep ground toward wooded. slopeCos is already the surface normal
        // against the radial, so this costs nothing extra to sample.
        openness += (slopeCos - 0.85f) * TerrainInfluence;

        // An open-preferring prototype MIRRORS the openness field rather than inverting the result. Keeping
        // (1 - wooded) would site it correctly but also gut its density, because the field averages well above
        // half; mirroring the input keeps the same statistics and only moves WHERE it sits. Since every
        // prototype in a biome reads one shared field, a mirrored flower lands precisely in the gaps between
        // the trees and bushes that read it the normal way.
        float pref = math.clamp(shadePreference, -1f, 1f);
        float wooded = math.smoothstep(OpenLo, OpenHi, openness);
        if (pref < 0f)
        {
            // Blend toward the MIRRORED field's response, not toward the mirrored input. Lerping the input
            // is degenerate at the halfway point — a 50/50 mix of a field and its own inverse is a constant,
            // so -0.5 measured as exactly zero siting signal and the scale crossed over in the wrong place.
            // Blending the response keeps -1..0 monotonic: -1 sits fully in the open, 0 behaves as before.
            float openResponse = math.smoothstep(OpenLo, OpenHi, 1f - openness);
            wooded = math.lerp(wooded, openResponse, -pref);
        }
        // Shade-seekers squeeze toward the densest wood. This DOES cost density — squaring a 0..1 field halves
        // its mean — which is correct for mushrooms, and their Weight carries the compensation.
        if (pref > 0f) wooded = math.lerp(wooded, wooded * wooded, pref);

        // A prototype that states a shade preference is asking to be sited RELATIVE TO TREE COVER, and the
        // grove field fights that: it is per-species noise with full 0..1 swing, while `wooded` is saturated at
        // 1 across most of the surface, so left alone the grove term carries nearly all the variance and the
        // shared openness signal is invisible. Measured: correlation against a neutral prototype was -0.002 at
        // every preference, i.e. the setting did nothing to siting. Fade the grove out as the preference
        // strengthens so the shared field is what actually places these props.
        float groveKeep = math.lerp(math.lerp(GroveFloor, 1f, grove), 1f, math.abs(pref));
        float keep = wooded * groveKeep;

        return math.lerp(1f, keep, math.saturate(clumpiness));
    }

    // snoise returns roughly -1..1; place it in 0..1 without clipping the tails to flat regions.
    static float Unit(float n) => math.saturate(n * 0.5f + 0.5f);

    // Spreads a seed across a large offset so two fields never correlate. Deterministic and Burst-safe —
    // deliberately not Random, because placement must be a pure function of position for the tile cache.
    static float3 SeedOffset(uint seed)
    {
        return new float3(
            (seed & 0xFFu) * 0.6131f,
            ((seed >> 8) & 0xFFu) * 0.7919f,
            ((seed >> 16) & 0xFFu) * 0.4327f) + 17.13f;
    }

    // Stable across processes (String.GetHashCode is randomised per run, which would move every grove between
    // sessions). Same FNV-1a the tree variant seeds use.
    public static uint GroupSeedFor(string key)
    {
        unchecked
        {
            uint h = 2166136261u;
            if (key != null) foreach (char c in key) { h ^= c; h *= 16777619u; }
            return h;
        }
    }
}
