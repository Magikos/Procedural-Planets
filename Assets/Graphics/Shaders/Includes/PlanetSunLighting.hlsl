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

// Cast shadow for a lit surface, kept OUT of the ndl form-shading ramp. That ramp carries a high floor
// so a prop's own dark side stays coloured instead of collapsing to a black dot; routing the cast shadow
// through the same term let that floor leak into it, and a rock standing inside a tree's shadow held ~65%
// of its lit brightness while the ground under it dropped to ~24%, which reads as no shadow on the rock.
// shadedFloor is the surface's ambient level in full shadow; 0.25 matches what the terrain shader reaches.
float PlanetCastShadow(float shadowAtten, float daylight, float shadedFloor)
{
    return lerp(shadedFloor, 1.0, lerp(1.0, shadowAtten, daylight));
}

#endif
