Shader "Hidden/WaterVolumePrepass"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Includes/DebugModes.hlsl"
        #include "Includes/WaterDisplacement.hlsl"
        #include "Includes/WaterVolumeData.hlsl"

        TEXTURE2D(_CameraDepthTexture);
        SAMPLER(sampler_CameraDepthTexture);

        float _ShoreFoamSoftness;
        float _DeepDepth;
        int _OceanDebugMode;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float4 waterData : TEXCOORD0;
            float forwardDepth : TEXCOORD1;
        };

        float SceneDepthValid(float rawDepth)
        {
            #if UNITY_REVERSED_Z
                return step(0.0001, rawDepth);
            #else
                return 1.0 - step(0.9999, rawDepth);
            #endif
        }

        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float4 waterData = saturate(input.color);

            // Displace exactly as Ocean.shader does. forwardDepth is the distance the volume composite
            // measures its water column from, so it has to describe the surface the player actually sees;
            // sampling the undisplaced shell puts every column read out by up to the swell amplitude.
            float3 planetNormalWS = SafeNormalize(positionWS - _PlanetCenter, float3(0.0, 1.0, 0.0));
            float3 swellNormalWS;
            float swellHeight;
            positionWS = ComputeWaterVertexDisplacement(positionWS, planetNormalWS, waterData, swellNormalWS, swellHeight);

            float3 positionVS = TransformWorldToView(positionWS);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.waterData = waterData;
            output.forwardDepth = max(-positionVS.z, 0.0);
            return output;
        }


        float4 EncodeWaterVolumeData(Varyings input)
        {
            float depth01 = input.waterData.r;
            float shore01 = input.waterData.g;
            float body01 = input.waterData.b;
            float freezeFactor = EvaluateFreezeFactor(input.waterData.a, body01);
            uint kind = body01 >= 0.5 ? WATER_KIND_OCEAN : WATER_KIND_LAKE;
            return float4(input.forwardDepth, depth01, EncodeWaterShoreKind(shore01, kind), freezeFactor);
        }

        float4 Frag(Varyings input) : SV_Target
        {
            return EncodeWaterVolumeData(input);
        }

        ENDHLSL

        Pass
        {
            Name "WaterVolumePrepass"
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.0
            ENDHLSL
        }

    }
}
