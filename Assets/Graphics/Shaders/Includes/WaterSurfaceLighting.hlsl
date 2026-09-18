#ifndef WATER_SURFACE_LIGHTING_INCLUDED
#define WATER_SURFACE_LIGHTING_INCLUDED
#include "PlanetSunLighting.hlsl"
#include "WeatherSampling.hlsl"

float WaterSurfaceLight(float rippleSun, float daylight, float shadow, float nightAmbient)
{
    float nightLight = saturate(nightAmbient * 0.10 + 0.015);
    return lerp(nightLight, 0.46 + rippleSun * 0.54, daylight)
        * lerp(1.0, shadow, daylight * 0.45);
}

// Sky colour along the reflected ray. The previous constant could never match the real
// sky, so grazing water read as flat paint against a warm horizon. Collapses to the
// night constant at daylight 0 - the distant-water night floor below depends on that.
float3 EvaluateSkyReflection(float3 reflectDir, float3 upWS, float3 sunDir, float daylight)
{
    float upness = saturate(dot(reflectDir, upWS));
    float sunAlign = saturate(dot(reflectDir, sunDir));
    // Palette tracks the sky this atmosphere actually renders: teal above, warm gold near
    // the horizon. A neutral-grey horizon reflects as wet sand, not water.
    float3 zenithColor = float3(0.26, 0.44, 0.60);
    float3 horizonColor = float3(0.62, 0.60, 0.46);
    float3 dayColor = lerp(horizonColor, zenithColor, pow(upness, 0.55));
    dayColor += float3(0.40, 0.26, 0.09) * pow(sunAlign, 8.0) * 0.60;
    float4 weather = SampleWeather(upWS);
    float gloom = WeatherCloudGloom(upWS, weather.g);
    float cloudCover = saturate(max(weather.r, gloom));
    float cloudLuma = dot(dayColor, float3(0.2126, 0.7152, 0.0722));
    dayColor = lerp(dayColor, cloudLuma.xxx * (1.0 - gloom * 0.5), cloudCover);
    return lerp(float3(0.010, 0.018, 0.030), dayColor, daylight);
}

#endif
