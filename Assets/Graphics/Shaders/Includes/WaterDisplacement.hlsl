#ifndef WATER_DISPLACEMENT_INCLUDED
#define WATER_DISPLACEMENT_INCLUDED

#include "Math.hlsl"        // _GameTime
#include "PlanetWind.hlsl"  // _WindDirection, _WindStrength01

// The visible surface (Ocean.shader) and the volume prepass (WaterVolumePrepass.shader) rasterise the SAME
// water mesh from the same vertex colours. When only one of them displaces, the prepass depth describes a
// surface the player never sees, and every volume read taken against it - column thickness, refraction,
// caustics, shore foam - misregisters by up to the full swell amplitude. Both include this file and call
// ComputeWaterVertexDisplacement, so there is one displacement and one freeze curve.
//
// These are shader GLOBALS rather than per-material properties because two separate materials must agree
// on them exactly. PlanetWaterSurface publishes them from WaterDto; the names live in
// ShaderGlobalIds.Water.cs. A material that re-declares any of them shadows the global and silently
// desynchronises the two surfaces again.
float3 _PlanetCenter;
float _SwellAmplitude;
float _SwellWavelength;
float _WaveSpeed;
float _FreezingEnabled;
float _LakeFreezeStart;
float _LakeFreezeComplete;
float _OceanFreezeStart;
float _OceanFreezeComplete;

float3 SafeNormalize(float3 value, float3 fallback)
{
    float lenSq = dot(value, value);
    return lenSq > 0.000001 ? value * rsqrt(lenSq) : fallback;
}

float2 SafeNormalize2(float2 value, float2 fallback)
{
    float lenSq = dot(value, value);
    return lenSq > 0.000001 ? value * rsqrt(lenSq) : fallback;
}

// Wave-space basis. axisA follows the wind so swell travels downwind; axisB completes the pair. Both are
// world-space and constant over the sphere, which is what makes the wave field continuous across faces.
void BuildPlanetWaveAxes(out float3 axisA, out float3 axisB)
{
    float3 windWS = SafeNormalize(_WindDirection, float3(1.0, 0.0, 0.0));
    float3 referenceAxis = abs(dot(windWS, float3(0.0, 1.0, 0.0))) < 0.92
        ? float3(0.0, 1.0, 0.0)
        : float3(0.0, 0.0, 1.0);

    axisA = windWS;
    axisB = SafeNormalize(cross(referenceAxis, axisA), float3(0.0, 0.0, 1.0));
}

// Single source of truth for the swell-energy gating components.
// Used by ComputeOceanSwell AND the WaveEnergy debug view; they MUST stay in sync.
// openWater01: 0 in ponds, 1 in open ocean (gates pond-vs-ocean amplitude).
// deepWater01: 0 in shore shallows, 1 in deeper water (gates how far swell reaches in).
// shoreFade  : 0 right at the coastline, 1 outside a thin calm band.
void EvaluateSwellGating(float depth01, float shore01, float body01,
    out float openWater01, out float deepWater01, out float shoreFade)
{
    openWater01 = smoothstep(0.30, 0.85, body01);
    deepWater01 = smoothstep(0.003, 0.035, depth01);
    shoreFade   = smoothstep(0.005, 0.040, shore01);
}

float EvaluateSurfaceWave(float2 positionTS, float2 directionTS, float wavelength, float speed, float amplitude, float phase, out float2 gradientTS)
{
    float k = 6.28318530718 / max(wavelength, 0.001);
    float theta = dot(positionTS, directionTS) * k + _GameTime * speed + phase;
    float waveSin = sin(theta);
    float waveCos = cos(theta);
    gradientTS = directionTS * (waveCos * amplitude * k);
    return waveSin * amplitude;
}

// Source of truth for the freeze curve. WaterMeshBuilder.EvaluateFreezeFactor is a CPU mirror of this -
// it decides ice coverage per body during the mesh build, before any shader runs, so it cannot share the
// code and can only be kept identical. Change one, change both.
float EvaluateFreezeFactor(float temperature01, float body01)
{
    float start = lerp(_LakeFreezeStart, _OceanFreezeStart, body01);
    float complete = lerp(_LakeFreezeComplete, _OceanFreezeComplete, body01);
    float cold = min(start, complete);
    float warm = max(start, complete);
    float freeze = 1.0 - smoothstep(cold, max(warm, cold + 0.0001), temperature01);
    return saturate(freeze * _FreezingEnabled);
}

// Radial swell height and the surface normal that follows it. Three plane waves in the wind-aligned
// tangent basis; no horizontal displacement, so the surface stays a height field over the sphere and a
// CPU height query needs no inversion.
void ComputeOceanSwell(float3 positionWS, float3 planetNormal, float depth01, float shore01, float body01,
    out float swellHeight, out float3 swellNormal)
{
    swellHeight = 0.0;
    swellNormal = planetNormal;

    float openWater01, deepWater01, shoreFade;
    EvaluateSwellGating(depth01, shore01, body01, openWater01, deepWater01, shoreFade);
    float wind01 = _WindStrength01;
    float energy = saturate(0.5 + wind01 * 0.5) * deepWater01 * shoreFade;
    if (energy <= 0.001)
        return;

    float3 localPosition = positionWS - _PlanetCenter;
    float3 waveAxisA;
    float3 waveAxisB;
    BuildPlanetWaveAxes(waveAxisA, waveAxisB);
    float2 positionTS = float2(dot(localPosition, waveAxisA), dot(localPosition, waveAxisB));

    float2 windTS = float2(1.0, 0.0);
    float2 crossTS = float2(0.0, 1.0);
    float wavelength = max(_SwellWavelength, 24.0) * lerp(0.42, 1.0, openWater01);
    float amplitude = max(_SwellAmplitude, 0.0) * lerp(0.10, 1.0, openWater01) * energy;
    float timeScale = max(_WaveSpeed, 0.001);

    float2 gradientTS = float2(0.0, 0.0);
    float2 g;
    float height = 0.0;
    height += EvaluateSurfaceWave(positionTS, windTS, wavelength, timeScale * 0.5, amplitude * 0.58, 0.00, g); gradientTS += g;
    height += EvaluateSurfaceWave(positionTS, SafeNormalize2(windTS * 0.78 + crossTS * 0.45, windTS), wavelength * 0.68, timeScale * -0.62, amplitude * 0.30, 1.70, g); gradientTS += g;
    height += EvaluateSurfaceWave(positionTS, SafeNormalize2(windTS * 0.40 - crossTS * 0.74, windTS), wavelength * 0.46, timeScale * 0.78, amplitude * 0.16, 3.10, g); gradientTS += g;

    swellHeight = height;

    // Surface normal from the height gradient, projected into the local tangent plane.
    float3 slopeWS = waveAxisA * gradientTS.x + waveAxisB * gradientTS.y;
    slopeWS = slopeWS - planetNormal * dot(slopeWS, planetNormal);
    swellNormal = SafeNormalize(planetNormal - slopeWS, planetNormal);
}

// The whole vertex-stage displacement, including the freeze lock. Every shader that rasterises the water
// mesh calls exactly this, so the surfaces cannot drift apart.
float3 ComputeWaterVertexDisplacement(float3 positionWS, float3 planetNormalWS, float4 waterData,
    out float3 swellNormalWS, out float swellHeight)
{
    float freezeFactor = EvaluateFreezeFactor(waterData.a, waterData.b);
    ComputeOceanSwell(positionWS, planetNormalWS, waterData.r, waterData.g, waterData.b, swellHeight, swellNormalWS);

    float liquidContribution = 1.0 - freezeFactor;
    swellHeight *= liquidContribution;
    swellNormalWS = SafeNormalize(lerp(planetNormalWS, swellNormalWS, liquidContribution), planetNormalWS);
    return positionWS + planetNormalWS * swellHeight; // radial displacement -> real 3D waves on the mesh
}

#endif
