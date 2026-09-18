#ifndef WATER_DISPLACEMENT_INCLUDED
#define WATER_DISPLACEMENT_INCLUDED

#include "Math.hlsl"        // _GameTime
#include "PlanetWind.hlsl"  // _WindDirection, _WindStrength01
#include "WeatherSampling.hlsl"

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
#ifndef PLANET_CENTER_DECLARED
#define PLANET_CENTER_DECLARED
float3 _PlanetCenter;
#endif
float _SwellAmplitude;
float _SwellWavelength;
float _WaveSpeed;
// The fragment field's size and height. Promoted out of Ocean.shader's Properties block: a material
// property of either name shadows the global wherever that material is bound, so the surface and the
// atmosphere would have banded their waves off different numbers without noticing.
float _WaveAmplitude;
float _WaveScale;
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

float WaterWeatherEnergy(float3 direction)
{
    float storm = SampleWeather(direction).g;
    return saturate(max(_WindStrength01, WeatherCloudGloom(direction, storm)));
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

float3 WaterGradientWS(float2 gradient, float3 axisA, float3 axisB, float3 planetNormal)
{
    // Preserve the projected axes' lengths. Normalizing them amplifies slopes near the wave poles.
    float3 slope = axisA * gradient.x + axisB * gradient.y;
    return slope - planetNormal * dot(slope, planetNormal);
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
    // Wind points toward travel. Keep crossing waves downwind too, including negative-X wave vectors.
    float travelSign = directionTS.x < 0.0 ? -1.0 : 1.0;
    float theta = dot(positionTS, directionTS) * k - _GameTime * abs(speed) * travelSign + phase;
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
    float wind01 = WaterWeatherEnergy(planetNormal);
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
    float3 slopeWS = WaterGradientWS(gradientTS, waveAxisA, waveAxisB, planetNormal);
    swellNormal = SafeNormalize(planetNormal - slopeWS, planetNormal);
}

struct WaterRippleParams
{
    float openWater01;
    float deepWater01;
    float weatherEnergy;
    float waveEnergy;
    float chaos01;
    float scale;
    float amplitude;
    float timeScale;
};

// The fragment field's size and energy, from the water-data channels and the wind. Hoisted alongside the
// field itself: a second consumer that derived these on its own would band its waves off a different size
// and never look wrong enough to notice.
//
// These gates are deliberately NOT EvaluateSwellGating's. That one gates the vertex swell and reaches
// further inshore; this one is the narrower detail gate, and Ocean.shader uses both, for different things.
WaterRippleParams EvaluateRippleParameters(float depth01, float body01, float3 direction)
{
    WaterRippleParams params;
    float wind01 = WaterWeatherEnergy(direction);
    params.openWater01 = smoothstep(0.42, 0.88, body01);
    params.deepWater01 = smoothstep(0.035, 0.22, depth01);
    params.weatherEnergy = saturate(wind01 * 0.92 + params.openWater01 * 0.08);

    // A pond ripples in the faintest breeze. The old smoothstep(0.34, 0.88) returned 0 for any normal wind
    // (0.10 typical), pinning lakes to the 0.035 floor - roughly a millimetre of detail amplitude, i.e. a
    // dead mirror.
    float lakeWind01 = smoothstep(0.02, 0.55, wind01);
    float lakeEnergy = lerp(0.16, 0.60, lakeWind01) * lerp(0.35, 1.0, params.deepWater01);
    float oceanEnergy = lerp(0.48, 1.0, params.deepWater01) * lerp(0.80, 1.20, params.weatherEnergy);
    params.waveEnergy = saturate(lerp(lakeEnergy, oceanEnergy, params.openWater01));
    params.chaos01 = saturate(wind01 * lerp(0.36, 0.86, params.openWater01) + params.openWater01 * 0.10);

    // Feature size must follow body size: 480 m detail on a ~350 m pond is a handful of features across the
    // whole thing. Amplitude scales with it so wave STEEPNESS (amplitude/wavelength) stays constant -
    // shortening wavelength alone multiplies slope by the same factor and the surface reads as crumpled
    // foil rather than water.
    float bodyWaveScale = lerp(0.10, 1.0, params.openWater01);
    params.scale = max(_WaveScale, 12.0) * bodyWaveScale;
    params.amplitude = max(_WaveAmplitude, 0.0) * bodyWaveScale;
    params.timeScale = max(_WaveSpeed, 0.001);
    return params;
}

struct WaterRippleField
{
    float2 wavePos;           // domain-warped sample position for the long waves
    float2 detailPos;         // and for the short ones; breakup and foam noise key off it
    float2 detailPosCross;    // second short-wave position, drifting across the wind
    float detailScale;        // shortest wavelength in play, for resolve and antialiasing decisions
    float height;             // long-wave height
    float detailHeight;       // short-wave height
    float2 gradientTS;        // long-wave slope
    float2 detailGradientTS;  // short-wave slope
    float waveTime;
};

// The FRAGMENT-stage wave field: a domain warp, four long waves and three short ones.
//
// Distinct from ComputeOceanSwell, which is the VERTEX displacement - that one moves the mesh and has to
// stay a plain height field so a CPU height query needs no inversion. This one only shades. Both live here
// for the same reason: anything that has to agree with the water surface must evaluate the identical waves,
// and a second copy of them drifts the moment either is tuned.
//
// The SHORT waves are the ones that matter to a consumer outside Ocean.shader. Focusing goes as surface
// curvature and curvature as 1/wavelength squared, so the 90 m swell focuses about 300 m down while a 2 m
// ripple focuses about 14 m down - which is why caustics are sharp on a shallow bed, and why underwater
// shafts band off these and not off the swell.
//
// EvaluateRippleParameters supplies the same scale and amplitude to every consumer.
WaterRippleField ComputeWaterRipple(float2 positionTS, float2 windTS, float2 crossTS,
    float scale, float amplitude, float timeScale, float waveEnergy, float weatherEnergy, float chaos01)
{
    WaterRippleField field;
    field.waveTime = _GameTime * timeScale;

    float2 warpDirA = SafeNormalize2(windTS * 0.21 + crossTS * 0.98, crossTS);
    float2 warpDirB = SafeNormalize2(windTS * -0.76 + crossTS * 0.65, crossTS);
    float2 domainWarp = float2(
        sin(dot(positionTS, warpDirA) / max(scale * 1.70, 1.0) + field.waveTime * 0.34),
        sin(dot(positionTS, warpDirB) / max(scale * 1.23, 1.0) - field.waveTime * 0.27));
    domainWarp *= scale * lerp(0.018, 0.095, chaos01);
    field.wavePos = positionTS + domainWarp;

    float2 detailDrift = windTS * (field.waveTime * scale * lerp(0.004, 0.018, weatherEnergy))
        + crossTS * (sin(field.waveTime * 0.31) * scale * lerp(0.004, 0.016, chaos01));
    field.detailPos = positionTS + domainWarp * lerp(1.35, 2.85, chaos01) - detailDrift;
    float2 crossDrift = crossTS * (field.waveTime * scale * lerp(0.006, 0.028, weatherEnergy))
        + windTS * (sin(field.waveTime * 0.37 + 1.7) * scale * lerp(0.005, 0.020, chaos01));
    field.detailPosCross = positionTS - domainWarp * lerp(0.85, 2.25, chaos01) + crossDrift;

    field.gradientTS = float2(0.0, 0.0);
    field.detailGradientTS = float2(0.0, 0.0);
    field.height = 0.0;
    field.detailHeight = 0.0;

    float2 gradient;
    float swellStrength = amplitude * waveEnergy;
    float detailStrength = amplitude * waveEnergy * lerp(0.62, 1.48, weatherEnergy) * 0.1;

    field.height += EvaluateSurfaceWave(field.wavePos, windTS, scale * 1.18, timeScale * 0.18, swellStrength * 0.19, 0.00, gradient);
    field.gradientTS += gradient;
    field.height += EvaluateSurfaceWave(field.wavePos, SafeNormalize2(windTS * 0.70 + crossTS * (0.24 + chaos01 * 0.18), windTS), scale * 0.58, timeScale * -0.24, swellStrength * 0.085, 1.70, gradient);
    field.gradientTS += gradient;
    field.height += EvaluateSurfaceWave(field.wavePos, SafeNormalize2(windTS * 0.34 - crossTS * 0.68, windTS), scale * 0.25, timeScale * 0.36, swellStrength * 0.040, 3.10, gradient);
    field.gradientTS += gradient;
    field.height += EvaluateSurfaceWave(field.wavePos, SafeNormalize2(windTS * -0.22 + crossTS * 0.98, crossTS), scale * 0.13, timeScale * -0.48, swellStrength * 0.018, 5.40, gradient);
    field.gradientTS += gradient;

    // Shore-height views need sub-metre ripples. Scale height with wavelength to preserve steepness.
    field.detailScale = clamp(scale * lerp(0.0038, 0.0026, chaos01), 0.75, 2.6);
    field.detailHeight += EvaluateSurfaceWave(field.detailPos, SafeNormalize2(windTS * 0.54 + crossTS * 0.84, windTS), field.detailScale * 0.88, timeScale * 0.82, detailStrength * 0.028, 0.80, gradient);
    field.detailGradientTS += gradient;
    field.detailHeight += EvaluateSurfaceWave(field.detailPosCross, SafeNormalize2(windTS * -0.28 + crossTS * 0.96, crossTS), field.detailScale * 0.61, timeScale * -1.10, detailStrength * 0.020, 2.40, gradient);
    field.detailGradientTS += gradient;
    field.detailHeight += EvaluateSurfaceWave(field.detailPosCross, SafeNormalize2(windTS * 0.91 - crossTS * 0.42, windTS), field.detailScale * 0.42, timeScale * 1.38, detailStrength * 0.014, 4.90, gradient);
    field.detailGradientTS += gradient;

    return field;
}

// Shared capillary detail for both sides of the interface. Fade features smaller than a pixel.
float3 WaterMicroSlope(float3 localPosition, float3 planetNormal, float detailScale, float timeScale,
    float distanceToSurface)
{
    float3 axisA, axisB;
    BuildPlanetWaveAxes(axisA, axisB);
    float microScale = max(detailScale * 0.12, 0.08);
    float3 p = localPosition / microScale - axisA * (_GameTime * timeScale * 0.26);
    float3 slope = float3(ValueNoise3D(p), ValueNoise3D(p + 19.1), ValueNoise3D(p + 43.7)) * 2.0 - 1.0;
    slope -= planetNormal * dot(slope, planetNormal);
    float footprint = 2.0 * distanceToSurface / max(abs(UNITY_MATRIX_P._m11) * _ScreenParams.y, 1.0);
    return slope * (1.0 - smoothstep(microScale, microScale * 3.0, footprint));
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

// Match the visible surface and volume prepass, including the river-mouth swell ramp.
float3 ComputeWaterMeshDisplacement(float3 positionWS, float3 up, float4 color, float2 blend,
    out float4 waterData, out float3 normalWS, out float height)
{
    waterData = saturate(color);
    float river = step(1.5, color.a);
    float swellWeight = river > .5 ? smoothstep(0, .35, blend.x) : 1.0;
    if (swellWeight == 0.0)
    {
        normalWS = up;
        height = 0.0;
        return positionWS;
    }
    float3 displaced = ComputeWaterVertexDisplacement(positionWS, up, waterData, normalWS, height);
    height *= swellWeight;
    normalWS = SafeNormalize(lerp(up, normalWS, swellWeight), up);
    return lerp(positionWS, displaced, swellWeight);
}

#endif
