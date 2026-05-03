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

float _SunDiscSize;
float _SunDiscBlend;

int _DebugMode;
// 0 = final, 1 = min height01, 2 = Rayleigh density, 3 = Mie density,
// 4 = sun transmittance, 5 = atmosphere mask

float _StarSeed;
float _StarDensity;
float _StarBrightness;

// --- Procedural stars ---
// Hash-based star field: divide sky into grid cells, hash each cell to place a star.
// Deterministic from seed. Fixed positions on the celestial sphere.

float Hash31(float3 p)
{
    p = frac(p * float3(443.897, 441.423, 437.195));
    p += dot(p, p.yzx + 19.19);
    return frac((p.x + p.y) * p.z);
}

float2 Hash32(float3 p)
{
    p = frac(p * float3(443.897, 441.423, 437.195));
    p += dot(p, p.yzx + 19.19);
    return frac(float2((p.x + p.y) * p.z, (p.z + p.x) * p.y));
}

float3 ProceduralStars(float3 dir)
{
    // Convert direction to spherical coordinates for grid
    float theta = atan2(dir.z, dir.x); // [-pi, pi]
    float phi = asin(dir.y);           // [-pi/2, pi/2]

    // Grid resolution — higher = more potential star positions
    float gridScale = _StarDensity;
    float2 gridUV = float2(theta * (gridScale / MATH_PI), phi * (gridScale / (MATH_PI * 0.5)));
    float2 cellID = floor(gridUV);
    float2 cellUV = frac(gridUV) - 0.5;

    float3 starColor = 0;

    // Check this cell and neighbors to avoid edge clipping
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            float2 neighbor = float2(x, y);
            float2 id = cellID + neighbor;

            // Hash the cell + seed to decide if there's a star
            float3 hashInput = float3(id.x, id.y, _StarSeed);
            float starPresence = Hash31(hashInput);
            if (starPresence > 0.3) continue; // ~30% of cells have stars

            // Star position within cell
            float2 starOffset = Hash32(hashInput + 7.0) - 0.5;
            float2 delta = neighbor + starOffset - cellUV;

            // Distance to star center
            float dist = length(delta);

            // Star size varies with hash
            float sizeFactor = Hash31(hashInput + 13.0);
            float starRadius = lerp(0.01, 0.04, sizeFactor * sizeFactor);

            // Soft circular falloff
            float glow = saturate(1.0 - dist / starRadius);
            glow = glow * glow * glow;

            // Brightness: biased toward dim
            float brightness = lerp(0.2, 1.0, sizeFactor * sizeFactor) * _StarBrightness;

            // Subtle color variation
            float colorHash = Hash31(hashInput + 23.0);
            float3 tint = colorHash > 0.85 ? float3(0.85, 0.9, 1.0)   // blue-white
                         : colorHash < 0.1  ? float3(1.0, 0.92, 0.8)  // warm
                         : float3(1, 1, 1);                            // white

            starColor += glow * brightness * tint;
        }
    }

    return starColor;
}

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

    // Sun disc — render even if ray misses atmosphere
    float sunDot = dot(dir, dirToSun);
    float sunDisc = smoothstep(_SunDiscSize - _SunDiscBlend, _SunDiscSize, sunDot);
    float3 sunColor = sunDisc * float3(1.2, 1.1, 0.9) * _SunIntensity;

    if (missedAtmo)
    {
        float3 stars = ProceduralStars(dir);
        float3 sun = sunColor / (1.0 + sunColor);
        return sceneColor + stars + sun;
    }

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

    // Sun disc + stars — only on sky pixels
    bool hitGeometry = sceneDepth < (dstToAtmo + dstThroughAtmo);
    if (!hitGeometry)
    {
        // Stars: added to raw in-scattered light so bright daytime sky drowns them out
        float3 stars = ProceduralStars(dir) * viewTransmittance;
        // Re-tonemap the combined scatter + stars
        float3 scatterPlusStars = (inScattered + stars);
        result = sceneColor * viewTransmittance + scatterPlusStars / (1.0 + scatterPlusStars);

        float3 sun = sunColor * viewTransmittance;
        result += sun / (1.0 + sun);
    }

    return result;
}
