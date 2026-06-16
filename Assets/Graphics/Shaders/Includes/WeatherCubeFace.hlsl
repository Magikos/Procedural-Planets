#ifndef WEATHER_CUBE_FACE_INCLUDED
#define WEATHER_CUBE_FACE_INCLUDED

// Cube-face UV helpers shared by WeatherSampling.hlsl and CloudShadows.hlsl.
// Face layout: 0=+Y, 1=-Y, 2=-X, 3=+X, 4=+Z, 5=-Z.
// UV axes are the inverse of CoordinateConverter.CubeFaceToUnitSphere /
// SphericalWeatherGrid.CubeFaceToUnitSphere:
// axisA = (uy, uz, ux), axisB = cross(localUp, axisA).
float3 CubeFaceLocalUp(int face)
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

    float3 localUp = CubeFaceLocalUp(face);
    float3 axisA = float3(localUp.y, localUp.z, localUp.x);
    float3 axisB = cross(localUp, axisA);
    float major = max(abs(dot(direction, localUp)), 0.00001);
    float u = dot(direction, axisA) / major;
    float v = dot(direction, axisB) / major;
    uv = saturate(float2(u, v) * 0.5 + 0.5);
}

#endif
