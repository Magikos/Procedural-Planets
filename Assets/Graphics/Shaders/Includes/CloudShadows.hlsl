#ifndef CLOUD_SHADOWS_INCLUDED
#define CLOUD_SHADOWS_INCLUDED

#include "Math.hlsl"
#include "WeatherCubeFace.hlsl"

TEXTURE2D_ARRAY(_CloudWeatherMap);
SAMPLER(sampler_CloudWeatherMap);
TEXTURE3D(_CloudShapeNoise);
SAMPLER(sampler_CloudShapeNoise);

float3 _CloudPlanetCenter;
float _CloudInnerRadius;
float _CloudOuterRadius;
int _CloudWeatherResolution;
float4x4 _CloudWeatherRotation;
float _CloudNoiseScale;
float _CloudWindAngle;
float4 _CloudShapeWeights;
float _CloudDensityThreshold;
float _CloudShapeSharpness;
float4 _CloudShadowParams;
float3 _WindDirection;
float _WindSpeedMps;
float _WindStrength01;
float _WaterFocusMode;

float4 SampleCloudShadowWeather(float3 direction)
{
    float3 weatherDirection = mul((float3x3)_CloudWeatherRotation, direction);
    direction = dot(weatherDirection, weatherDirection) > 0.0001 ? normalize(weatherDirection) : direction;

    int face;
    float2 uv;
    CubeFaceUv(direction, face, uv);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_CloudWeatherMap, sampler_CloudWeatherMap, uv, face, 0);
}

float WeightedCloudShadowNoise(float4 noise, float4 weights)
{
    return dot(noise, weights / max(dot(weights, float4(1.0, 1.0, 1.0, 1.0)), 0.0001));
}

float SampleCloudShadowDensity(float3 worldPos)
{
    float3 fromCenter = worldPos - _CloudPlanetCenter;
    float radius = length(fromCenter);
    float3 direction = fromCenter / max(radius, 0.0001);
    float4 weather = SampleCloudShadowWeather(direction);
    float condensation = weather.r;

    if (condensation <= 0.001)
        return 0.0;

    // Match Cloud.shader: advect the shape noise along the local windTangent so shadows track
    // the moving clouds.
    float3 windAxis = cross(direction, _WindDirection);
    float windAxisLen = length(windAxis);
    float3 advectedPos = windAxisLen > 1e-5
        ? RotateAroundAxis(fromCenter, windAxis / windAxisLen, -_CloudWindAngle) + _CloudPlanetCenter
        : worldPos;
    float3 shapePos = advectedPos * _CloudNoiseScale;
    float shapeFBM = WeightedCloudShadowNoise(SAMPLE_TEXTURE3D_LOD(_CloudShapeNoise, sampler_CloudShapeNoise, shapePos, 0), _CloudShapeWeights);

    float cloudShape = shapeFBM * condensation;
    float density = saturate((cloudShape - _CloudDensityThreshold) * _CloudShapeSharpness);
    float stormBoost = lerp(1.0, max(_CloudShadowParams.z, 0.5), weather.g);
    return density * condensation * stormBoost;
}

float CloudShadowFactor(float3 worldPos, float3 sunDir, float localSun)
{
    if (_WaterFocusMode > 0.5)
        return 1.0;

    if (_CloudWeatherResolution <= 0 || _CloudOuterRadius <= _CloudInnerRadius || _CloudShadowParams.x <= 0.0)
        return 1.0;

    float horizonFadeDistance = max(_CloudShadowParams.w, 0.001);
    float horizonFade = smoothstep(0.01, horizonFadeDistance, localSun);
    if (horizonFade <= 0.0)
        return 1.0;

    float radius = length(worldPos - _CloudPlanetCenter);
    float2 outerHit = RaySphere(_CloudPlanetCenter, _CloudOuterRadius, worldPos, sunDir);
    if (outerHit.y <= 0.0)
        return 1.0;

    float shadowStart = 0.0;
    if (radius < _CloudInnerRadius)
    {
        float2 innerHit = RaySphere(_CloudPlanetCenter, _CloudInnerRadius, worldPos, sunDir);
        if (innerHit.y <= 0.0)
            return 1.0;

        shadowStart = innerHit.x + innerHit.y;
    }

    float shadowEnd = outerHit.x + outerHit.y;
    float shadowLength = max(shadowEnd - shadowStart, 0.0);
    if (shadowLength <= 0.0)
        return 1.0;

    float shadowDensity = 0.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float t = (i + 0.5) / 3.0;
        float3 samplePos = worldPos + sunDir * (shadowStart + shadowLength * t);
        shadowDensity += SampleCloudShadowDensity(samplePos);
    }
    shadowDensity /= 3.0;

    float softness = max(_CloudShadowParams.y, 0.001);
    float shadow = smoothstep(0.0, softness, shadowDensity);
    shadow *= saturate(_CloudShadowParams.x) * horizonFade;
    return 1.0 - shadow;
}

#endif
