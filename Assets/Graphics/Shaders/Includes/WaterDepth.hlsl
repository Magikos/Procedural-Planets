#ifndef WATER_DEPTH_INCLUDED
#define WATER_DEPTH_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "WaterLevelField.hlsl"

// Both passes measure from the visible surface. The standing-water field does not describe an
// elevated river, and interpolating it across a mouth creates a false shoreline inside the channel.
float MeasuredWaterColumn(float3 surfaceWS, float2 screenUv,
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
    float column = length(surfaceWS - _PlanetCenter) - bedRadius;

    // Sky and a dry far bank are not a submerged receiver. Neither can supply transmitted bottom colour.
    if (column > 0.0)
        waterPath = bedOffset;
    return column;
}

#endif
