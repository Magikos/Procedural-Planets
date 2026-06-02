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
float4 _WeatherLightningParams;
float4 _WeatherLightningCell0;
float4 _WeatherLightningCell1;
float4 _WeatherLightningCell2;
float4 _WeatherLightningCell3;

// Map a normalised direction vector to a cube-face index and [0,1] UV.
// Face layout: 0=+Y, 1=-Y, 2=-X, 3=+X, 4=+Z, 5=-Z.
// UV axes are the inverse of CoordinateConverter.CubeFaceToUnitSphere /
// SphericalWeatherGrid.CubeFaceToUnitSphere:
// axisA = (uy, uz, ux), axisB = cross(localUp, axisA).
float3 WeatherCubeFaceLocalUp(int face)
{
    if (face == 0) return float3(0.0, 1.0, 0.0);
    if (face == 1) return float3(0.0, -1.0, 0.0);
    if (face == 2) return float3(-1.0, 0.0, 0.0);
    if (face == 3) return float3(1.0, 0.0, 0.0);
    if (face == 4) return float3(0.0, 0.0, 1.0);
    return float3(0.0, 0.0, -1.0);
}

void CubeFaceUv(float3 direction, out int face, out float2 uv)
{
    float3 absDirection = abs(direction);

    if (absDirection.y >= absDirection.x && absDirection.y >= absDirection.z)
    {
        face = direction.y > 0.0 ? 0 : 1;
    }
    else if (absDirection.x >= absDirection.y && absDirection.x >= absDirection.z)
    {
        face = direction.x > 0.0 ? 3 : 2;
    }
    else
    {
        face = direction.z > 0.0 ? 4 : 5;
    }

    float3 localUp = WeatherCubeFaceLocalUp(face);
    float3 axisA = float3(localUp.y, localUp.z, localUp.x);
    float3 axisB = cross(localUp, axisA);
    float major = max(abs(dot(direction, localUp)), 0.00001);
    float u = dot(direction, axisA) / major;
    float v = dot(direction, axisB) / major;
    uv = saturate(float2(u, v) * 0.5 + 0.5);
}

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

float WeatherLightningCell(float4 cell, float3 normal, float storm)
{
    if (cell.w <= 0.0)
        return 0.0;

    float3 direction = dot(cell.xyz, cell.xyz) > 0.0001
        ? normalize(cell.xyz)
        : float3(0.0, 1.0, 0.0);
    float stormMask = smoothstep(_WeatherLightningParams.z, 1.0, storm);
    float locationMask = smoothstep(_WeatherLightningParams.y, _WeatherLightningParams.x, dot(normal, direction));
    return cell.w * stormMask * pow(saturate(locationMask), 1.35);
}

float WeatherLightning(float3 normal, float storm)
{
    float lightning = WeatherLightningCell(_WeatherLightningCell0, normal, storm);
    lightning = max(lightning, WeatherLightningCell(_WeatherLightningCell1, normal, storm));
    lightning = max(lightning, WeatherLightningCell(_WeatherLightningCell2, normal, storm));
    lightning = max(lightning, WeatherLightningCell(_WeatherLightningCell3, normal, storm));
    return lightning;
}

#endif
