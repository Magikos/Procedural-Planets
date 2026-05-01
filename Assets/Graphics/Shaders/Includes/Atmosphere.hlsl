#pragma once

// Wavelength-based atmospheric scattering.
// Based on Sebastian Lague's Solar System project.
// Key difference from previous implementation: divides inScatteredLight by planetRadius
// for scale-independent scattering that works at any planet radius.

TEXTURE2D(_BakedOpticalDepth);
SAMPLER(sampler_BakedOpticalDepth);

TEXTURE2D(_BlueNoise);
SAMPLER(sampler_BlueNoise);

float3 _DirToSun;
float3 _PlanetCenter;

float _PlanetRadius;
float _AtmosphereRadius;

int _NumInScatteringPoints;
float _DensityFalloff;
float3 _ScatteringCoefficients;
float _Intensity;

float _DitherStrength;
float _DitherScale;

// Sun disc
float _SunDiscSize;    // cos(angular radius), e.g. 0.9998
float _SunDiscBlend;   // softness of edge

float DensityAtPoint(float3 samplePoint)
{
    float heightAboveSurface = length(samplePoint - _PlanetCenter) - _PlanetRadius;
    float height01 = heightAboveSurface / (_AtmosphereRadius - _PlanetRadius);
    return exp(-height01 * _DensityFalloff) * (1 - height01);
}

float OpticalDepthBaked(float3 rayOrigin, float3 rayDir)
{
    float height = length(rayOrigin - _PlanetCenter) - _PlanetRadius;
    float height01 = saturate(height / (_AtmosphereRadius - _PlanetRadius));
    float uvX = 1 - (dot(normalize(rayOrigin - _PlanetCenter), rayDir) * 0.5 + 0.5);
    return SAMPLE_TEXTURE2D(_BakedOpticalDepth, sampler_BakedOpticalDepth, float2(uvX, height01)).r;
}

// Bidirectional optical depth sampling for view ray segments.
// Blends forward/backward lookups to handle rays that pass through
// the densest part of the atmosphere (near the planet surface).
float OpticalDepthBaked2(float3 rayOrigin, float3 rayDir, float rayLength)
{
    float3 endPoint = rayOrigin + rayDir * rayLength;
    float d = dot(rayDir, normalize(rayOrigin - _PlanetCenter));

    const float blendStrength = 1.5;
    float w = saturate(d * blendStrength + 0.5);

    float d1 = OpticalDepthBaked(rayOrigin, rayDir) - OpticalDepthBaked(endPoint, rayDir);
    float d2 = OpticalDepthBaked(endPoint, -rayDir) - OpticalDepthBaked(rayOrigin, -rayDir);

    return lerp(d2, d1, w);
}

float2 SquareUV(float2 uv)
{
    float scale = 1000;
    return float2(uv.x * _ScreenParams.x / scale, uv.y * _ScreenParams.y / scale);
}

float3 CalculateScattering(float3 rayOrigin, float3 rayDir, float sceneDepth, float3 sceneColor, float2 uv)
{
    rayOrigin -= _PlanetCenter;
    float3 planetCenter = 0; // origin-relative

    float2 hitInfo = RaySphere(planetCenter, _AtmosphereRadius, rayOrigin, rayDir);
    float dstToAtmosphere = hitInfo.x;
    float dstThroughAtmosphere = min(hitInfo.y, sceneDepth - dstToAtmosphere);

    if (dstThroughAtmosphere <= 0)
    {
        // Sun disc for rays that miss atmosphere entirely (deep space)
        float sunDot = dot(rayDir, _DirToSun);
        float sunDisc = smoothstep(_SunDiscSize - _SunDiscBlend, _SunDiscSize, sunDot);
        return sceneColor + sunDisc * float3(1.2, 1.1, 0.9);
    }

    // Blue noise dithering to reduce banding
    float blueNoise = SAMPLE_TEXTURE2D(_BlueNoise, sampler_BlueNoise, SquareUV(uv) * _DitherScale).r;
    blueNoise = (blueNoise - 0.5) * _DitherStrength;

    const float epsilon = 0.0001;
    float3 pointInAtmosphere = rayOrigin + rayDir * (dstToAtmosphere + epsilon);
    float rayLength = dstThroughAtmosphere - epsilon * 2;

    float stepSize = rayLength / (_NumInScatteringPoints - 1);
    float3 inScatterPoint = pointInAtmosphere;
    float3 inScatteredLight = 0;
    float viewRayOpticalDepth = 0;

    [loop]
    for (int i = 0; i < _NumInScatteringPoints; i++)
    {
        float sunRayOpticalDepth = OpticalDepthBaked(inScatterPoint, _DirToSun);
        float localDensity = DensityAtPoint(inScatterPoint);
        viewRayOpticalDepth = OpticalDepthBaked2(pointInAtmosphere, rayDir, stepSize * i);
        float3 transmittance = exp(-(sunRayOpticalDepth + viewRayOpticalDepth) * _ScatteringCoefficients);

        inScatteredLight += localDensity * transmittance;
        inScatterPoint += rayDir * stepSize;
    }

    // Division by _PlanetRadius makes scattering scale-independent
    inScatteredLight *= _ScatteringCoefficients * _Intensity * stepSize / _PlanetRadius;
    inScatteredLight += blueNoise * 0.01;

    // Attenuate scene color through atmosphere (Beer-Lambert)
    // Divide by _PlanetRadius to match normalized-scale LUT values
    float3 opacity = exp(-viewRayOpticalDepth * _ScatteringCoefficients / _PlanetRadius);
    float3 finalColor = sceneColor * opacity + inScatteredLight;

    // Sun disc (visible through atmosphere)
    float sunDot = dot(rayDir, _DirToSun);
    float sunDisc = smoothstep(_SunDiscSize - _SunDiscBlend, _SunDiscSize, sunDot);
    // Only show sun disc when looking at sky (not through terrain)
    float isSky = sceneDepth > dstToAtmosphere + dstThroughAtmosphere - 1;
    finalColor += sunDisc * float3(1.2, 1.1, 0.9) * isSky;

    return finalColor;
}
