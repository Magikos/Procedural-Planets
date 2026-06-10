#ifndef CLIMATE_SAMPLING_INCLUDED
#define CLIMATE_SAMPLING_INCLUDED

TEXTURE2D_ARRAY(_ClimateMap);
SAMPLER(sampler_ClimateMap);
float _ClimateMapResolution;
float4 _ClimateTemperatureRangeCelsius;

float3 ClimateCubeFaceLocalUp(int face)
{
    if (face == 0) return float3(0.0, 1.0, 0.0);
    if (face == 1) return float3(0.0, -1.0, 0.0);
    if (face == 2) return float3(-1.0, 0.0, 0.0);
    if (face == 3) return float3(1.0, 0.0, 0.0);
    if (face == 4) return float3(0.0, 0.0, 1.0);
    return float3(0.0, 0.0, -1.0);
}

void ClimateCubeFaceUv(float3 direction, out int face, out float2 uv)
{
    direction = normalize(direction);
    float3 absDirection = abs(direction);

    if (absDirection.y >= absDirection.x && absDirection.y >= absDirection.z)
        face = direction.y > 0.0 ? 0 : 1;
    else if (absDirection.x >= absDirection.y && absDirection.x >= absDirection.z)
        face = direction.x > 0.0 ? 3 : 2;
    else
        face = direction.z > 0.0 ? 4 : 5;

    float3 localUp = ClimateCubeFaceLocalUp(face);
    float3 axisA = float3(localUp.y, localUp.z, localUp.x);
    float3 axisB = cross(localUp, axisA);
    float major = max(abs(dot(direction, localUp)), 0.00001);
    uv = saturate(float2(
        dot(direction, axisA),
        dot(direction, axisB)) / major * 0.5 + 0.5);
}

float2 SampleClimate01(float3 direction)
{
    if (_ClimateMapResolution < 1.0)
        return float2(0.5, 0.5);

    int face;
    float2 uv;
    ClimateCubeFaceUv(direction, face, uv);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(
        _ClimateMap,
        sampler_ClimateMap,
        uv,
        face,
        0).rg;
}

float ClimateTemperatureCelsius(float temperature01)
{
    return lerp(
        _ClimateTemperatureRangeCelsius.x,
        _ClimateTemperatureRangeCelsius.y,
        saturate(temperature01));
}

#endif
