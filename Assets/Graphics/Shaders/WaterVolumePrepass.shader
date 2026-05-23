Shader "Hidden/WaterVolumePrepass"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 waterData : TEXCOORD0;
            float forwardDepth : TEXCOORD1;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float3 positionVS = TransformWorldToView(positionWS);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.waterData = saturate(input.color.rgb);
            output.forwardDepth = max(-positionVS.z, 0.0);
            return output;
        }

        float4 Frag(Varyings input) : SV_Target
        {
            float depth01 = input.waterData.r;
            float shore01 = input.waterData.g;
            float body01 = input.waterData.b;
            return float4(input.forwardDepth, depth01, shore01, body01);
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

        Pass
        {
            Name "WaterVolumeLipPrepass"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.0
            ENDHLSL
        }
    }
}
