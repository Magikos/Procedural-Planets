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
float _WeatherCloudTypeTest;

#include "WeatherCubeFace.hlsl"
#include "WeatherLightning.hlsl"
#include "WeatherThreshold.hlsl"

float WeatherCloudConvectivity(float3 direction, float temperature01)
{
    if (_WeatherCloudTypeTest > 0.5)
    {
        float3 weatherDirection = mul((float3x3)_CloudWeatherRotation, direction);
        int face;
        float2 uv;
        CubeFaceUv(weatherDirection, face, uv);
        return uv.x < 0.34 ? 0.0 : 1.0;
    }
    return smoothstep(0.2, 0.6, temperature01);
}

// Bilinear filtering reconstructs this grid with a kink at every cell edge - it is C0, not C1. Cloud.shader
// then thresholds the field hard (`saturate((cloudShape - threshold) * 8)`), and a hard gate on a piecewise
// linear field traces the cells: straight-edged, stair-stepped cloud silhouettes. At 30.7 m per cell that is
// invisible overhead and unmistakable at the horizon, where a cell projects across many pixels and the
// contour reads as rectangular blocks sitting on the skyline.
//
// Smoothing the interpolation weights fixes the reconstruction rather than the gate. Quintic weights are
// flat at both ends, so the result is C1 across cell edges and the contour has no facets to follow. Costs
// one frac/floor and no extra taps, which matters because this runs inside the raymarch inner loop.
//
// The alternative - softening _CloudShapeSharpness - would work too and is wrong: that value is authored,
// and lowering it trades away every crisp cloud edge in the sky to hide an artifact at the horizon.
float2 WeatherSmoothCellUv(Texture2DArray tex, float2 uv)
{
    float width, height, elements, levels;
    tex.GetDimensions(0, width, height, elements, levels);
    float2 texel = uv * width - 0.5;
    float2 cell = floor(texel);
    float2 f = texel - cell;
    f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    return (cell + f + 0.5) / width;
}

float4 SampleWeather(float3 direction)
{
    float3 weatherDirection = mul((float3x3)_CloudWeatherRotation, direction);
    direction = dot(weatherDirection, weatherDirection) > 0.0001 ? normalize(weatherDirection) : direction;

    int face;
    float2 uv;
    CubeFaceUv(direction, face, uv);
    uv = WeatherSmoothCellUv(_CloudWeatherMap, uv);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_CloudWeatherMap, sampler_CloudWeatherMap, uv, face, 0);
}

float4 SampleDynamics(float3 direction)
{
    float3 weatherDirection = mul((float3x3)_CloudWeatherRotation, direction);
    direction = dot(weatherDirection, weatherDirection) > 0.0001 ? normalize(weatherDirection) : direction;

    int face;
    float2 uv;
    CubeFaceUv(direction, face, uv);
    uv = WeatherSmoothCellUv(_WeatherDynamicsMap, uv);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_WeatherDynamicsMap, sampler_WeatherDynamicsMap, uv, face, 0);
}

float WeatherPrecipitationSignal(float3 direction, float storm)
{
    float softness = max(_PrecipitationParams.z, 0.0001);
    float rainGate = WeatherThreshold(_PrecipitationParams.y, min(1.0, _PrecipitationParams.y + softness), storm);
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
