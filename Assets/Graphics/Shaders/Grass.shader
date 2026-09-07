Shader "Planet/Grass"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            // WaterVolume composites before transparents. Draw grass immediately after
            // that composite, write depth, then let the ocean surface depth-test against it.
            "Queue" = "Transparent-10"
        }

        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "GrassForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex GrassVertex
            #pragma fragment GrassFragment
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Includes/GrassRendering.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ZWrite Off
            ColorMask RG
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex GrassVertex
            #pragma fragment GrassFragment
            #define GRASS_MOTION_PASS
            #include "Includes/GrassRendering.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
