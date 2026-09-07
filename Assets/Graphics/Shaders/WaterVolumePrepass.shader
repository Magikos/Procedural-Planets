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
        #include "Includes/WaterLevelField.hlsl"
        #include "Includes/WaterVolumeData.hlsl"
        #include "Includes/WaterDepth.hlsl"


        // Global, so deliberately NOT in a per-material buffer - a material property of the same name would
        // shadow it and the shader would silently read a stale planet radius.
        float _SeaLevelRadius;
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
            float3 positionWS : TEXCOORD2;
        };

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
            output.positionWS = positionWS;
            return output;
        }

        float4 EncodeWaterVolumeData(Varyings input)
        {
            float sceneValid;
            float waterPath;
            float column = MeasuredWaterColumn(input.positionWS, GetNormalizedScreenSpaceUV(input.positionCS),
                _SeaLevelRadius, sceneValid, waterPath);
            float measured01 = saturate(column / max(_DeepDepth, 0.001));

            // Open sky behind the water - the horizon - has no scene to measure against, and the reconstructed
            // position there is the far plane, which would read as immeasurably deep OR as zero depending on
            // the platform's depth convention. Keep the mesh's own baked depth for those pixels; it is coarse
            // but it is never nonsense.
            float depth01 = lerp(input.waterData.r, measured01, sceneValid);
            float shore01 = input.waterData.g;
            float body01 = input.waterData.b;
            float freezeFactor = EvaluateFreezeFactor(input.waterData.a, body01);
            uint kind = body01 >= 0.5 ? WATER_KIND_OCEAN : WATER_KIND_LAKE;
            return float4(input.forwardDepth, depth01, EncodeWaterShoreKind(shore01, kind), freezeFactor);
        }

        float4 Frag(Varyings input) : SV_Target
        {
            ClipWaterBackface(input.positionWS, _SeaLevelRadius);

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
