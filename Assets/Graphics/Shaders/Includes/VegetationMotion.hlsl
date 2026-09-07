#ifndef VEGETATION_MOTION_INCLUDED
#define VEGETATION_MOTION_INCLUDED

float4 _VegetationPreviousWind; // direction xyz, speed w
float4 _VegetationPreviousTime; // Unity time, game time, wind strength, valid
float3 _VegetationPreviousCamera;

bool HasVegetationHistory(bool previous)
{
    return previous && _VegetationPreviousTime.w > 0.5;
}

#endif
