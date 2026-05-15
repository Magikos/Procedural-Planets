#pragma once

// Atmosphere v3 — Rayleigh + Mie scattering with brute-force sun ray marching
// No LUT. Sea level as density origin.

// --- Uniforms ---
float3 _SunParams;          // Directional sun (normalized direction)
float3 _PlanetCenter;
float _PlanetRadius;        // Sea level — ray intersection floor
float _DensityOriginRadius; // Same as _PlanetRadius — density height=0 at sea level
float _AtmosphereRadius;

int _ViewSteps;

float3 _RayleighScattering;
float _RayleighScaleHeight;

float _MieScatteringCoeff;
float _MieScaleHeight;
float _MieAnisotropy;

float _SunIntensity;

int _DebugMode;
// 0 = final, 1 = min height01, 2 = Rayleigh density, 3 = Mie density,
// 4 = sun transmittance, 5 = atmosphere mask

// --- Density ---

float DensityHeight(float3 pos)
{
    return length(pos) - _DensityOriginRadius;
}

float RayleighDensity(float height)
{
    return exp(-height / _RayleighScaleHeight);
}

float MieDensity(float height)
{
    return exp(-height / _MieScaleHeight);
}

// --- Phase functions ---

float RayleighPhase(float cosTheta)
{
    return (3.0 / (16.0 * MATH_PI)) * (1.0 + cosTheta * cosTheta);
}

float MiePhase(float cosTheta, float g)
{
    float gg = g * g;
    float denom = 1.0 + gg - 2.0 * g * cosTheta;
    return (3.0 / (8.0 * MATH_PI)) * ((1.0 - gg) * (1.0 + cosTheta * cosTheta))
         / ((2.0 + gg) * pow(abs(denom), 1.5));
}

// --- Sun ray optical depth (LUT-based) ---

TEXTURE2D(_BakedOpticalDepth);
SAMPLER(sampler_BakedOpticalDepth);

float2 SunOpticalDepth(float3 pos, float3 dirToSun)
{
    float height = length(pos) - _PlanetRadius;
    float height01 = saturate(height / (_AtmosphereRadius - _PlanetRadius));

    // Angle between ray direction and surface normal
    float3 normal = normalize(pos);
    float cosAngle = dot(normal, dirToSun);
    // Remap from [-1,1] to [0,1]: dot=1 (up) -> u=0, dot=-1 (down) -> u=1
    float uvX = (1.0 - cosAngle) * 0.5;

    float2 od = SAMPLE_TEXTURE2D_LOD(_BakedOpticalDepth, sampler_BakedOpticalDepth, float2(uvX, height01), 0).rg;
    return od;
}

// --- Main ---

float3 CalculateScattering(float3 start, float3 dir, float sceneDepth, float3 sceneColor)
{
    float3 origin = start - _PlanetCenter;
    float3 dirToSun = _SunParams.xyz;

    float2 hitAtmo = RaySphere(0, _AtmosphereRadius, origin, dir);
    bool missedAtmo = hitAtmo.y <= 0;

    if (missedAtmo)
        return sceneColor;

    float2 hitPlanet = RaySphere(0, _PlanetRadius, origin, dir);

    float dstToAtmo = hitAtmo.x;
    float dstThroughAtmo = hitAtmo.y;

    float maxDst = dstThroughAtmo;
    maxDst = min(maxDst, sceneDepth - dstToAtmo);
    if (hitPlanet.y > 0)
        maxDst = min(maxDst, hitPlanet.x - dstToAtmo);

    if (maxDst <= 0)
        return sceneColor;

    if (_DebugMode == 5)
        return float3(0.2, 0.5, 0.2);

    float stepSize = maxDst / (float)_ViewSteps;
    float3 rayPos = origin + dir * (dstToAtmo + stepSize * 0.5);

    float cosTheta = dot(dir, dirToSun);
    float phaseR = RayleighPhase(cosTheta);
    float phaseM = MiePhase(cosTheta, _MieAnisotropy);

    float3 totalRayleigh = 0;
    float3 totalMie = 0;
    float viewRayleighOD = 0;
    float viewMieOD = 0;
    float minHeight01 = 1;

    for (int i = 0; i < _ViewSteps; i++)
    {
        float height = DensityHeight(rayPos);
        float h01 = saturate(height / (_AtmosphereRadius - _DensityOriginRadius));
        minHeight01 = min(minHeight01, h01);

        float densityR = RayleighDensity(height) * stepSize;
        float densityM = MieDensity(height) * stepSize;

        viewRayleighOD += densityR;
        viewMieOD += densityM;

        float2 sunOD = SunOpticalDepth(rayPos, dirToSun);

        float3 totalOD = _RayleighScattering * (viewRayleighOD + sunOD.x)
                       + _MieScatteringCoeff * (viewMieOD + sunOD.y);

        float3 transmittance = exp(-totalOD);

        totalRayleigh += densityR * transmittance;
        totalMie += densityM * transmittance;

        rayPos += dir * stepSize;
    }

    // Debug modes
    if (_DebugMode == 1)
        return float3(minHeight01, minHeight01, minHeight01);

    float atmosphereThickness = _AtmosphereRadius - _DensityOriginRadius;

    if (_DebugMode == 2)
    {
        float vis = saturate(viewRayleighOD / atmosphereThickness);
        return float3(vis, vis, vis);
    }

    if (_DebugMode == 3)
    {
        float vis = saturate(viewMieOD / atmosphereThickness);
        return float3(vis, vis, vis);
    }

    if (_DebugMode == 4)
    {
        float3 midPos = origin + dir * (dstToAtmo + maxDst * 0.5);
        float2 sunOD = SunOpticalDepth(midPos, dirToSun);
        float3 sunT = exp(-_RayleighScattering * sunOD.x - _MieScatteringCoeff * sunOD.y);
        return sunT;
    }

    // Final scattering
    float3 rayleigh = phaseR * _RayleighScattering * totalRayleigh;
    float3 mie = phaseM * _MieScatteringCoeff * totalMie;
    float3 inScattered = (rayleigh + mie) * _SunIntensity;

    float3 viewTransmittance = exp(-_RayleighScattering * viewRayleighOD
                                   - _MieScatteringCoeff * viewMieOD);

    // Tone map only the atmosphere (in-scattered light), not the terrain
    float3 toneMappedScatter = inScattered / (1.0 + inScattered);
    float3 result = sceneColor * viewTransmittance + toneMappedScatter;

    return result;
}
