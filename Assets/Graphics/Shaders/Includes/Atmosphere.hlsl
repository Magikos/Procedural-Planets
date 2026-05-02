#pragma once

// Atmospheric scattering based on URP-Atmosphere (Kai Angulo / ShaderToy port).
// Key difference from Solar System approach: incremental optical depth in loop,
// 3-channel LUT at world scale, phase functions, no /planetRadius normalization.

TEXTURE2D(_BakedOpticalDepth);
SAMPLER(sampler_BakedOpticalDepth);

TEXTURE2D(_BlueNoise);
SAMPLER(sampler_BlueNoise);

float3 _DirToSun;
float3 _PlanetCenter;

float _PlanetRadius;
float _AtmosphereRadius;

int _NumInScatteringPoints;

// Scattering coefficients (raw physical values, tuned for planet scale)
float3 _RayleighScattering;
float3 _MieScattering;
float _MieG;
float3 _AbsorptionBeta;

// Density falloffs
float _RayleighFalloff;
float _MieFalloff;
float _HeightAbsorption;

float _Intensity;

float _DitherStrength;
float _DitherScale;

float _SunDiscSize;
float _SunDiscBlend;

float3 _NightAmbient;
float3 _AmbientBeta;

// 3-channel density: Rayleigh, Mie, Ozone
float3 DensityAtPoint(float3 position)
{
    float height = length(position) - _PlanetRadius;
    float height01 = height / (_AtmosphereRadius - _PlanetRadius);

    float rayleighDensity = exp(-height01 * _RayleighFalloff) * (1 - height01);
    float mieDensity = exp(-height01 * _MieFalloff) * (1 - height01);

    float denom = (_HeightAbsorption + height01);
    float ozoneDensity = (1.0 / (denom * denom + 1.0)) * rayleighDensity;

    return float3(rayleighDensity, mieDensity, ozoneDensity);
}

// Sun ray optical depth from baked LUT (3-channel: Rayleigh, Mie, Ozone)
float3 OpticalDepthBaked(float3 rayOrigin, float3 rayDir)
{
    float rayLen = length(rayOrigin);
    float height = rayLen - _PlanetRadius;
    float height01 = saturate(height / (_AtmosphereRadius - _PlanetRadius));
    float3 normal = rayOrigin / rayLen;
    float uvX = 1 - (dot(normal, rayDir) * 0.5 + 0.5);
    return SAMPLE_TEXTURE2D(_BakedOpticalDepth, sampler_BakedOpticalDepth, float2(uvX, height01)).xyz;
}

float2 SquareUV(float2 uv)
{
    return float2(uv.x * _ScreenParams.x, uv.y * _ScreenParams.y) / 1000.0;
}

float3 CalculateScattering(float3 rayOrigin, float3 rayDir, float sceneDepth, float3 sceneColor, float2 uv)
{
    // Shift to planet-center-relative coordinates
    float3 start = rayOrigin - _PlanetCenter;

    float2 hitInfo = RaySphere(0, _AtmosphereRadius, start, rayDir);
    float dstToAtmosphere = hitInfo.x;
    float dstThroughAtmosphere = hitInfo.y;

    // Ray end = min(atmosphere exit, scene geometry)
    float rayEnd = min(dstToAtmosphere + dstThroughAtmosphere, sceneDepth);
    float rayStart = max(dstToAtmosphere, 0.0);

    if (rayStart >= rayEnd)
    {
        // Miss — sun disc in deep space
        float sunDot = dot(rayDir, _DirToSun);
        float sunDisc = smoothstep(_SunDiscSize - _SunDiscBlend, _SunDiscSize, sunDot);
        return sceneColor + sunDisc * float3(1.2, 1.1, 0.9);
    }

    bool hitGeometry = sceneDepth < dstToAtmosphere + dstThroughAtmosphere;

    // Phase functions
    float mu = dot(rayDir, _DirToSun);
    float mumu = mu * mu;
    float gg = _MieG * _MieG;

    // Rayleigh phase: 3/(16π) * (1 + cos²θ)
    float phaseRay = 3.0 / (50.2654824574) * (1.0 + mumu);

    // Mie phase: Henyey-Greenstein
    float phaseMie = 3.0 / (25.1327412287) * ((1.0 - gg) * (mumu + 1.0))
        / (pow(abs(1.0 + gg - 2.0 * mu * _MieG), 1.5) * (2.0 + gg));

    // Block Mie glow when geometry is in front of atmosphere exit
    phaseMie = hitGeometry ? 0.0 : phaseMie;

    float stepSize = (rayEnd - rayStart) / float(_NumInScatteringPoints);
    float3 inScatterPoint = start + rayDir * (rayStart + stepSize * 0.5);

    // Accumulators
    float3 totalRay = 0;
    float3 totalMie = 0;
    float3 opticalDepth = 0;

    [loop]
    for (int i = 0; i < _NumInScatteringPoints; i++)
    {
        // 3-channel density at this point, scaled by step size
        float3 density = DensityAtPoint(inScatterPoint) * stepSize;

        // Accumulate view ray optical depth incrementally
        opticalDepth += density;

        // Sun ray optical depth from baked LUT
        float3 lightOpticalDepth = OpticalDepthBaked(inScatterPoint, _DirToSun);

        // Attenuation: how much light survives from sun → this point → camera
        float3 attenuation = exp(
            -_RayleighScattering * (opticalDepth.x + lightOpticalDepth.x)
            - _MieScattering * (opticalDepth.y + lightOpticalDepth.y)
            - _AbsorptionBeta * (opticalDepth.z + lightOpticalDepth.z)
        );

        // Accumulate scattered light (Rayleigh and Mie separately)
        totalRay += density.x * attenuation;
        totalMie += density.y * attenuation;

        inScatterPoint += rayDir * stepSize;
    }

    // Scene opacity from accumulated view ray optical depth
    float3 opacity = exp(
        -(_RayleighScattering * opticalDepth.x
        + _MieScattering * opticalDepth.y
        + _AbsorptionBeta * opticalDepth.z)
    );

    // Combine with phase functions
    float3 rayleigh = phaseRay * _RayleighScattering * totalRay;
    float3 mie = phaseMie * _MieScattering * totalMie;
    float3 ambient = opticalDepth.x * _AmbientBeta * 0.00001;

    float3 finalColor = (rayleigh + mie + ambient) * _Intensity + sceneColor * opacity;

    // Blue noise dithering
    float blueNoise = SAMPLE_TEXTURE2D(_BlueNoise, sampler_BlueNoise, SquareUV(uv) * _DitherScale).r;
    finalColor += (blueNoise - 0.5) * _DitherStrength * 0.01;

    // Night ambient
    float sunFacing = dot(normalize(rayOrigin - _PlanetCenter), _DirToSun);
    finalColor += _NightAmbient * (1 - saturate(sunFacing * 2));

    // Sun disc (only on sky pixels)
    if (!hitGeometry)
    {
        float sunDot = dot(rayDir, _DirToSun);
        float sunDisc = smoothstep(_SunDiscSize - _SunDiscBlend, _SunDiscSize, sunDot);
        finalColor += sunDisc * float3(1.2, 1.1, 0.9);
    }

    return finalColor;
}
