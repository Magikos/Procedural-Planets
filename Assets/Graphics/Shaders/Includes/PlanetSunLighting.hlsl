#ifndef PLANET_SUN_LIGHTING_INCLUDED
#define PLANET_SUN_LIGHTING_INCLUDED

float3 PlanetSafeNormalize(float3 value, float3 fallback)
{
    float lenSq = dot(value, value);
    return lenSq > 1e-8 ? value * rsqrt(lenSq) : fallback;
}

float3 PlanetSunDirection(float3 sunParams, float3 fallback)
{
    return PlanetSafeNormalize(sunParams, fallback);
}

float PlanetDaylightFromLocalSun(float localSun)
{
    return smoothstep(-0.08, 0.18, localSun);
}

float PlanetDaylight(float3 planetNormal, float3 sunDir)
{
    return PlanetDaylightFromLocalSun(dot(planetNormal, sunDir));
}

float PlanetSurfaceDirectFromLocalSun(float localSun)
{
    return smoothstep(-0.04, 0.18, localSun);
}

float PlanetSurfaceDirect(float3 surfaceNormal, float3 sunDir)
{
    return PlanetSurfaceDirectFromLocalSun(dot(surfaceNormal, sunDir));
}

float PlanetNightAmbient(float nightAmbientIntensity)
{
    return max(nightAmbientIntensity, 0.035);
}

#endif
