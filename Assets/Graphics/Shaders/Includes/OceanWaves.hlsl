#ifndef OCEAN_WAVES_INCLUDED
#define OCEAN_WAVES_INCLUDED

// Shared Gerstner ocean wave model.
//
// This is the DURABLE CONTRACT for water-surface motion. The same closed-form
// function is intended to be mirrored 1:1 in C# (WaterWaveModel) so boat buoyancy
// and swimming float-height queries on the CPU agree exactly with the GPU visuals.
// If you change the math here, change the C# port in lockstep.
//
// Why Gerstner (not FFT): a closed-form height(x,t) can be evaluated cheaply and
// deterministically on the CPU per-point (buoyancy) without GPU readback.
//
// IMPORTANT: waves are summed in a SHARED tangent frame supplied by the caller (one frame for
// the whole near-camera patch), NOT a per-point frame. A per-point frame is degenerate: the
// vector (point - planetCenter) is radial, so projecting it onto that same point's tangent axes
// is ~0 everywhere → every vertex gets the same phase → a flat sheet. Projecting onto a shared
// frame gives a horizontal coordinate that varies across the surface → real waves.

#include "Math.hlsl"

#define OCEAN_WAVE_COUNT 4

struct OceanSurfaceSample
{
    float3 positionWS;  // displaced world-space position
    float3 normalWS;    // analytic surface normal
    float  height;      // signed displacement along the frame up (meters)
    float  crest;       // 0..1 crest sharpness (horizontal pinch) — drives whitecap foam
};

float3 OceanWaveSafeNormalize(float3 v, float3 fallback)
{
    float lenSq = dot(v, v);
    return lenSq > 1e-12 ? v * rsqrt(lenSq) : fallback;
}

float2 OceanRotate2(float2 v, float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

// Build a wind-aligned tangent frame at a given up direction. Use ONE frame for a whole patch.
void BuildOceanFrame(float3 up, float3 windDirWS, out float3 frameA, out float3 frameB)
{
    float3 windProjected = windDirWS - up * dot(windDirWS, up);
    float3 fallback = OceanWaveSafeNormalize(
        cross(abs(up.y) < 0.92 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0), up), float3(1.0, 0.0, 0.0));
    frameA = OceanWaveSafeNormalize(windProjected, fallback);
    frameB = cross(up, frameA);
}

// Evaluate the Gerstner wave set at an undisplaced point on the sea-level sphere.
//   baseWorldPos        : point on the sea sphere (undisplaced)
//   planetCenter        : planet center (world)
//   frameUp/frameA/frameB : SHARED tangent frame for the patch (frameA wind-aligned)
//   time                : precision-safe time (_GameTime)
//   wind01              : wind strength 0..1
//   baseAmplitude       : crest-to-trough amplitude of the largest wave at ocean scale (meters)
//   baseWavelength      : wavelength of the largest wave at ocean scale (meters)
//   bodyScale01         : 0 = pond (tiny short ripples), 1 = open ocean (large long swell)
//   steepness           : 0..1 Gerstner steepness; clamped to avoid self-intersection
OceanSurfaceSample EvaluateOceanSurface(
    float3 baseWorldPos, float3 planetCenter,
    float3 frameUp, float3 frameA, float3 frameB,
    float time, float wind01,
    float baseAmplitude, float baseWavelength, float bodyScale01, float steepness)
{
    OceanSurfaceSample result;

    // Horizontal coordinate in the SHARED tangent plane — varies across the patch.
    float3 offset = baseWorldPos - planetCenter;
    float2 p = float2(dot(offset, frameA), dot(offset, frameB));

    // Pond vs ocean: small bodies get short, low ripples; oceans get long, tall swell.
    float bodyScale = saturate(bodyScale01);
    float amp = baseAmplitude * lerp(0.05, 1.0, bodyScale);
    float wavelength = baseWavelength * lerp(0.12, 1.0, bodyScale);
    float speedScale = lerp(0.55, 1.0, bodyScale) * lerp(0.65, 1.15, saturate(wind01));
    float steep = clamp(steepness, 0.0, 1.0) * lerp(0.45, 1.0, bodyScale);

    const float spread = 0.62; // radians of directional fan per wave

    float2 horizontal = 0.0;
    float heightSum = 0.0;
    float crestSum = 0.0;
    float3 normalLocal = float3(0.0, 0.0, 1.0); // (frameA, frameB, frameUp)

    [unroll]
    for (int i = 0; i < OCEAN_WAVE_COUNT; i++)
    {
        float fi = (float)i;
        float2 dir = OceanRotate2(float2(1.0, 0.0), (fi - 1.5) * spread);

        float layerWavelength = wavelength * pow(0.55, fi);
        float k = MATH_TAU / max(layerWavelength, 0.001);
        float a = amp * pow(0.62, fi);
        float phaseSpeed = sqrt(9.81 / k) * speedScale;
        float phase = k * dot(dir, p) + time * phaseSpeed * k;

        float c = cos(phase);
        float s = sin(phase);
        float Q = steep / max(k * a * OCEAN_WAVE_COUNT, 1e-4);

        horizontal += Q * a * dir * c;
        heightSum += a * s;
        crestSum += Q * k * a * s;

        float wa = k * a;
        normalLocal.x -= dir.x * wa * c;
        normalLocal.y -= dir.y * wa * c;
        normalLocal.z -= Q * wa * s;
    }

    float3 worldDisplacement = frameA * horizontal.x + frameB * horizontal.y + frameUp * heightSum;
    result.positionWS = baseWorldPos + worldDisplacement;
    result.normalWS = OceanWaveSafeNormalize(
        frameA * normalLocal.x + frameB * normalLocal.y + frameUp * normalLocal.z, frameUp);
    result.height = heightSum;
    result.crest = saturate(crestSum);
    return result;
}

#endif
