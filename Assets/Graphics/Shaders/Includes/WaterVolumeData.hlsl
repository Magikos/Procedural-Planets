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

// RGB absorption of water, as the exponent for ONE extinction unit. Consumers that measure path in metres
// divide by WATER_ABSORPTION_UNIT_METRES; consumers that already hold a normalised 0..1 extinction use it
// directly. Red goes first and blue survives, and every consumer has to use the same ratio or the volume,
// the far waterline and Snell's window disagree about what colour deep water is.
#define WATER_ABSORPTION float3(3.80, 1.75, 0.58)
#define WATER_ABSORPTION_UNIT_METRES 40.0

// Refractive index of water, which fixes the critical angle asin(1/n) = 48.75 degrees. That cone is
// Snell's window, and the same number bends the ray back out through the surface.
#define WATER_IOR 1.333

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

float _WaterEdgeFadeStart;
float _WaterEdgeFadeEnd;
float _WaterEdgeFadeEndOcean;

// Screen coverage of the water surface. Single source of truth - WaterVolume and Atmosphere both had their
// own copy of this expression, against the same channels, with no way to notice if one drifted.
//
// DEPTH alone drives this, which is the whole shoreline feather. Water is transparent where it is shallow,
// so the tint has to fade out as the bed rises; a depth ramp also widens by itself on a shallow bank and
// tightens on a steep one, which no distance ramp can do.
//
// It used to be smoothstep(0.0005, 0.018, max(depth01, shore01 * 0.45 + isOcean * 0.55)). The shore01 term
// was a bug: it crossed the top of the ramp about five metres out, where the water is roughly a metre deep,
// so it short-circuited the depth term and snapped every lake edge to full opacity - the hard waterline.
//
// The ocean term was NOT a bug, and removing it outright made the seabed visible across whole bays. A
// constant 0.55 sits far above the ramp, so ocean coverage was 1 everywhere; that was hiding the fact that
// an ocean shelf stays under a few metres deep for a very long way out. So the ocean keeps a feather, it
// just reaches full strength in about 1.5 m of depth instead of a lake's 6.5 m. That is a real difference
// rather than a fudge - open water is turbid and you cannot see its bed, a lake is clear and you can.
float WaterVolumeCoverage(float4 data)
{
    float shore01;
    uint kind;
    DecodeWaterShoreKind(data.b, shore01, kind);
    float fadeEnd = kind == WATER_KIND_OCEAN ? _WaterEdgeFadeEndOcean : _WaterEdgeFadeEnd;
    return smoothstep(_WaterEdgeFadeStart, fadeEnd, saturate(data.g));
}

float WaterVolumeLakeMask(float4 data)
{
    float shore01;
    uint kind;
    DecodeWaterShoreKind(data.b, shore01, kind);
    return kind == WATER_KIND_LAKE ? 1.0 : 0.0;
}

#endif
