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

float PlanetSunSpecular(float3 normal, float3 sunDir, float3 viewDir, float smoothness)
{
    float3 halfDir = PlanetSafeNormalize(sunDir + viewDir, float3(0.0, 0.0, 0.0));
    float NoH = saturate(dot(normal, halfDir));
    float exponent = lerp(2.0, 256.0, smoothness * smoothness);
    return pow(NoH, exponent) * smoothness * saturate(dot(normal, sunDir));
}

float PlanetHorizonVisibility(float3 offset, float radius, float3 rayDir)
{
    if (radius <= 0.0) return 1.0;
    float cameraRadius = length(offset);
    if (cameraRadius <= radius * 1.002)
    {
        float3 localNormal = cameraRadius > 0.0001 ? offset / cameraRadius : float3(0.0, 1.0, 0.0);
        return smoothstep(-0.018, 0.032, dot(localNormal, rayDir));
    }

    float rayForward = dot(offset, rayDir);
    if (rayForward >= 0.0) return 1.0;
    float closestSq = max(dot(offset, offset) - rayForward * rayForward, 0.0);
    float horizonClearance = sqrt(closestSq) - radius;
    float horizonSoftness = max(radius * 0.00035, 0.35);
    return smoothstep(-horizonSoftness, horizonSoftness, horizonClearance);
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
