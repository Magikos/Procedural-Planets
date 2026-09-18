#ifndef WATER_CAMERA_INCLUDED
#define WATER_CAMERA_INCLUDED

float4 _WaterCameraPosition;
float4 _WaterCameraSurface; // moving surface radius, smoothed immersion, crossing pulse, reserved

bool IsWaterPresentationCamera(float3 cameraPositionWS)
{
    float3 offset = cameraPositionWS - _WaterCameraPosition.xyz;
    return _WaterCameraPosition.w > 0.5 && dot(offset, offset) < 0.0001;
}

float WaterCameraImmersion(float3 cameraPositionWS)
{
    return IsWaterPresentationCamera(cameraPositionWS) ? saturate(_WaterCameraSurface.y) : 0.0;
}

#endif
