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
float4 _PrecipitationParams;

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

float WeatherPrecipitationSignal(float3 direction, float storm)
{
    float softness = max(_PrecipitationParams.z, 0.0001);
    float rainGate = smoothstep(_PrecipitationParams.y, min(1.0, _PrecipitationParams.y + softness), storm);
    return saturate(SampleDynamics(direction).b) * rainGate;
}

float WeatherCloudGloomFromRain(float storm, float precipitationSignal)
{
    return max(saturate(storm), saturate(precipitationSignal));
}

float WeatherCloudGloom(float3 direction, float storm)
{
    return WeatherCloudGloomFromRain(storm, WeatherPrecipitationSignal(direction, storm));
}

#endif
