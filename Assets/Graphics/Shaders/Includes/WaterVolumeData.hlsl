#ifndef WATER_VOLUME_DATA_INCLUDED
#define WATER_VOLUME_DATA_INCLUDED

// The volume prepass RT layout, in one place. Three shaders touch it - WaterVolumePrepass writes it,
// WaterVolume and Atmosphere read it - and nothing else can tell you when they disagree, because a
// mismatch shows up as a wrong body tint rather than an artefact.
//
//   R  forwardDepth   metres, distance to the DISPLACED surface. Always > 0 where the mesh rasterised;
//                     Atmosphere uses step(0.0001, R) as its "is there water here" test, so this channel
//                     must never be signed or biased.
//   G  depth01        0..1 water column depth.
//   B  shore + kind   packed, see below.
//   A  freezeFactor   0..1, 1 = fully frozen.
//
// B packs two values because the RT has four channels and the volume needs five. The pack is
//   round(shore01 * 511) * 4 + kind
// with a maximum of 2047, which a half float represents exactly (integers are exact to 2048). shore01
// keeps 9 bits and there are 4 body kinds.
//
// The bit split is deliberate. Coverage runs shore01 through smoothstep(0.0005, 0.018), a ramp that lives
// entirely in the bottom few percent of the channel, so shore01 needs its precision far more than the kind
// enum needs headroom. At 7 bits the shoreline feather would quantise to about five steps.
//
// This replaced shore01 * 0.45 + body01 * 0.55, which only decoded correctly while body01 was near 0 or 1
// and would have failed silently the day a third body kind existed.

#define WATER_KIND_LAKE   0u
#define WATER_KIND_OCEAN  1u
// 2 and 3 are reserved for river and waterfall (W13 / W14).

#define WATER_SHORE_QUANT 511.0

float EncodeWaterShoreKind(float shore01, uint kind)
{
    return round(saturate(shore01) * WATER_SHORE_QUANT) * 4.0 + (float)min(kind, 3u);
}

void DecodeWaterShoreKind(float packed, out float shore01, out uint kind)
{
    float p = max(packed, 0.0);
    kind = (uint)fmod(p, 4.0);
    shore01 = floor(p * 0.25) * (1.0 / WATER_SHORE_QUANT);
}

// Screen coverage of the water surface. Single source of truth - WaterVolume and Atmosphere both had their
// own copy of this expression, against the same channels, with no way to notice if one drifted.
//
// ponytail: the ocean term below reproduces the old packed value purely so coverage stays bit-identical
// through this change. The cleaner test is step(0.0001, data.r), since forwardDepth is non-zero exactly
// where the mesh rasterised and needs no per-kind floor - but that alters the shoreline feather on every
// body at once, so it wants its own change and its own visual pass.
float WaterVolumeCoverage(float4 data)
{
    float shore01;
    uint kind;
    DecodeWaterShoreKind(data.b, shore01, kind);
    float legacyShoreBody = shore01 * 0.45 + (kind == WATER_KIND_OCEAN ? 0.55 : 0.0);
    return smoothstep(0.0005, 0.018, max(saturate(data.g), saturate(legacyShoreBody)));
}

float WaterVolumeLakeMask(float4 data)
{
    float shore01;
    uint kind;
    DecodeWaterShoreKind(data.b, shore01, kind);
    return kind == WATER_KIND_LAKE ? 1.0 : 0.0;
}

#endif
