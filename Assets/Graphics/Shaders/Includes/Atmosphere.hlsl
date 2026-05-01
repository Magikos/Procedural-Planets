#pragma once

// Wavelength-based atmospheric scattering.
// Loop structure from Fluid Planet (proven working).
// Final normalization by atmosphereThickness (from Geographical Adventures).

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

float _SunDiscSize;
float _SunDiscBlend;

float3 _NightAmbient;

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
    float2 hitInfo = RaySphere(_PlanetCenter, _AtmosphereRadius, rayOrigin, rayDir);
    float dstToAtmosphere = hitInfo.x;
    float fullAtmosphereRayLength = hitInfo.y;

    float dstToSurface = sceneDepth - dstToAtmosphere;
    bool hitGeometry = dstToSurface < fullAtmosphereRayLength;
    float dstThroughAtmosphere = hitGeometry ? dstToSurface : fullAtmosphereRayLength;

    if (dstThroughAtmosphere <= 0)
    {
        float sunDot = dot(rayDir, _DirToSun);
        float sunDisc = smoothstep(_SunDiscSize - _SunDiscBlend, _SunDiscSize, sunDot);
        return sceneColor + sunDisc * float3(1.2, 1.1, 0.9);
    }

    float blueNoise = SAMPLE_TEXTURE2D(_BlueNoise, sampler_BlueNoise, SquareUV(uv) * _DitherScale).r;
    blueNoise = (blueNoise - 0.5) * _DitherStrength;

    const float epsilon = 0.0001;
    float3 pointInAtmosphere = rayOrigin + rayDir * (dstToAtmosphere + epsilon);
    float rayLength = dstThroughAtmosphere - epsilon * 2;

    float stepSize = rayLength / (_NumInScatteringPoints - 1);
    float3 inScatterPoint = pointInAtmosphere;
    float3 inScatteredLight = 0;
    float3 transmittance = 1;

    // Exact Fluid Planet loop structure
    [loop]
    for (int i = 0; i < _NumInScatteringPoints; i++)
    {
        float sunRayOpticalDepth = OpticalDepthBaked(inScatterPoint, _DirToSun);
        float localDensity = DensityAtPoint(inScatterPoint);
        float viewRayOpticalDepth = OpticalDepthBaked2(pointInAtmosphere, rayDir, stepSize * i);

        // Raw LUT optical depths — no step size multiplication here
        transmittance = exp(-(sunRayOpticalDepth + viewRayOpticalDepth) * _ScatteringCoefficients);

        inScatteredLight += localDensity * transmittance;
        inScatterPoint += rayDir * stepSize;
    }

    // Normalize by atmosphere thickness instead of planet radius
    float atmosphereThickness = _AtmosphereRadius - _PlanetRadius;
    inScatteredLight *= _ScatteringCoefficients * _Intensity * stepSize / atmosphereThickness;
    inScatteredLight += blueNoise * 0.01;

    // Surface attenuation: transmittance from loop (Fluid Planet approach)
    float3 finalColor = sceneColor * transmittance + inScatteredLight;

    // Night ambient
    float sunFacing = dot(normalize(rayOrigin - _PlanetCenter), _DirToSun);
    finalColor += _NightAmbient * (1 - saturate(sunFacing * 2));

    // Sun disc
    if (!hitGeometry)
    {
        float sunDot = dot(rayDir, _DirToSun);
        float sunDisc = smoothstep(_SunDiscSize - _SunDiscBlend, _SunDiscSize, sunDot);
        finalColor += sunDisc * float3(1.2, 1.1, 0.9);
    }

    return finalColor;
}
