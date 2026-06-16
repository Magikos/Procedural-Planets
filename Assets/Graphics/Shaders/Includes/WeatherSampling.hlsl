#ifndef WEATHER_SAMPLING_INCLUDED
#define WEATHER_SAMPLING_INCLUDED

// Shared weather map sampling helpers used by Cloud.shader and Precipitation.shader.
// All required uniforms and resources are declared in this file.

// Declared here so all resource and uniform references in the function bodies below are in scope.
TEXTURE2D_ARRAY(_CloudWeatherMap);
SAMPLER(sampler_CloudWeatherMap);
TEXTURE2D_ARRAY(_WeatherDynamicsMap);
SAMPLER(sampler_WeatherDynamicsMap);
float4x4 _CloudWeatherRotation;

#include "WeatherCubeFace.hlsl"
#include "WeatherLightning.hlsl"

float4 SampleWeather(float3 direction)
{
    float3 weatherDirection = mul((float3x3)_CloudWeatherRotation, direction);
    direction = dot(weatherDirection, weatherDirection) > 0.0001 ? normalize(weatherDirection) : direction;

    int face;
    float2 uv;
    CubeFaceUv(direction, face, uv);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_CloudWeatherMap, sampler_CloudWeatherMap, uv, face, 0);
}

float4 SampleDynamics(float3 direction)
{
    float3 weatherDirection = mul((float3x3)_CloudWeatherRotation, direction);
    direction = dot(weatherDirection, weatherDirection) > 0.0001 ? normalize(weatherDirection) : direction;

    int face;
    float2 uv;
    CubeFaceUv(direction, face, uv);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_WeatherDynamicsMap, sampler_WeatherDynamicsMap, uv, face, 0);
}

#endif
