#ifndef WATER_DEPTH_INCLUDED
#define WATER_DEPTH_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "WaterLevelField.hlsl"

// Both the surface and its prepass must measure the same shore. The broad, filtered shore field gives
// the height at the bed; the point-sampled wet-cell field cannot describe a sub-cell shoreline.
float MeasuredWaterColumn(float3 surfaceWS, float2 screenUv, float seaRadius,
    out float sceneValid, out float waterPath)
{
    float rawDepth = SampleSceneDepth(screenUv);
    #if UNITY_REVERSED_Z
        sceneValid = step(0.0001, rawDepth);
    #else
        sceneValid = 1.0 - step(0.9999, rawDepth);
    #endif

    waterPath = 100000.0;
    if (sceneValid <= 0.0)
        return 0.0;

    float3 sceneWS = ComputeWorldSpacePosition(screenUv, rawDepth, UNITY_MATRIX_I_VP);
    float bedOffset = distance(sceneWS, surfaceWS);
    // A distant bank cannot trim the water in front of it or generate shoreline foam there.
    sceneValid *= 1.0 - smoothstep(40.0, 160.0, bedOffset);
    float3 fromCenter = sceneWS - _PlanetCenter;
    float bedRadius = max(length(fromCenter), 0.0001);
    float column = ShoreSurfaceRadiusAt(fromCenter / bedRadius, seaRadius) - bedRadius;

    // Sky and a dry far bank are not a submerged receiver. Neither can supply transmitted bottom colour.
    if (column > 0.0)
        waterPath = bedOffset;
    return column;
}

void ClipWaterBackface(float3 surfaceWS, float seaRadius)
{
    float3 cameraOffset = _WorldSpaceCameraPos.xyz - _PlanetCenter;
    float cameraRadius = max(length(cameraOffset), 0.0001);
    float surfaceRadius = ShoreSurfaceRadiusAt(cameraOffset / cameraRadius, seaRadius);
    // Use this body's level, including perched lakes. Below it the underside must remain visible.
    if (cameraRadius - surfaceRadius > 0.5)
        clip(dot(_WorldSpaceCameraPos.xyz - surfaceWS, surfaceWS - _PlanetCenter));
}

#endif
