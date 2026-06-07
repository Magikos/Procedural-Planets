Shader "Hidden/WaterVolumePrepass"
{
    Properties
    {
        _FreezingEnabled ("Freezing Enabled", Range(0, 1)) = 1
        _LakeFreezeStart ("Lake Freeze Start", Range(0, 1)) = 0.36
        _LakeFreezeComplete ("Lake Freeze Complete", Range(0, 1)) = 0.26
        _OceanFreezeStart ("Ocean Freeze Start", Range(0, 1)) = 0.20
        _OceanFreezeComplete ("Ocean Freeze Complete", Range(0, 1)) = 0.10
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Includes/DebugModes.hlsl"

        TEXTURE2D(_CameraDepthTexture);
        SAMPLER(sampler_CameraDepthTexture);

        float _ShoreFoamSoftness;
        float _DeepDepth;
        float _FreezingEnabled;
        float _LakeFreezeStart;
        float _LakeFreezeComplete;
        float _OceanFreezeStart;
        float _OceanFreezeComplete;
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
            float3 positionVS = TransformWorldToView(positionWS);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.waterData = saturate(input.color);
            output.forwardDepth = max(-positionVS.z, 0.0);
            return output;
        }

        float EvaluateFreezeFactor(float temperature01, float body01)
        {
            float start = lerp(_LakeFreezeStart, _OceanFreezeStart, body01);
            float complete = lerp(_LakeFreezeComplete, _OceanFreezeComplete, body01);
            float cold = min(start, complete);
            float warm = max(start, complete);
            return saturate((1.0 - smoothstep(cold, max(warm, cold + 0.0001), temperature01)) * _FreezingEnabled);
        }

        float4 EncodeWaterVolumeData(Varyings input)
        {
            float depth01 = input.waterData.r;
            float shore01 = input.waterData.g;
            float body01 = input.waterData.b;
            float freezeFactor = EvaluateFreezeFactor(input.waterData.a, body01);
            return float4(input.forwardDepth, depth01, shore01, freezeFactor);
        }

        float4 Frag(Varyings input) : SV_Target
        {
            return EncodeWaterVolumeData(input);
        }

        float4 FragRelaxedLip(Varyings input) : SV_Target
        {
            if (_OceanDebugMode == DEBUG_VOLUME_LIP_RAW_PINK)
                return float4(input.forwardDepth, 1.0, 0.0, -1.0);

            float2 uv = input.positionCS.xy / max(_ScaledScreenParams.xy, float2(1.0, 1.0));
            float rawSceneDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
            float sceneValid = SceneDepthValid(rawSceneDepth);
            float sceneForwardDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
            float shoreRange = max(_ShoreFoamSoftness, 0.0);
            float deepDepth = max(_DeepDepth, 0.0);
            float encodedLipDepthMeters = saturate(input.waterData.r) * max(deepDepth, 1.0);
            float minimumSlack = max(max(shoreRange * 1.25, deepDepth * 0.45), 45.0);
            float maximumSlack = max(max(shoreRange * 2.0, deepDepth * 0.75), 90.0);
            float depthSlack = min(max(minimumSlack, encodedLipDepthMeters * 1.10), maximumSlack);

            float noOpaqueScene = 1.0 - sceneValid;
            float lipInFrontOfScene = step(input.forwardDepth, sceneForwardDepth + depthSlack);

            if (_OceanDebugMode == DEBUG_VOLUME_LIP_DEPTH_GATE)
            {
                float accepted = max(noOpaqueScene, lipInFrontOfScene);
                return accepted > 0.5
                    ? float4(input.forwardDepth, 1.0, 0.0, -1.0)
                    : float4(input.forwardDepth, 0.0, 1.0, -2.0);
            }

            clip(max(noOpaqueScene, lipInFrontOfScene) - 0.5);

            if (_OceanDebugMode == DEBUG_VOLUME_LIP_PINK)
                return float4(input.forwardDepth, 1.0, 0.0, -1.0);

            return EncodeWaterVolumeData(input);
        }

        float4 FragLipSceneDebug(Varyings input) : SV_Target
        {
            return float4(1.0, 0.0, 1.0, 0.72);
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
            #pragma fragment FragRelaxedLip
            #pragma target 4.0
            ENDHLSL
        }

        Pass
        {
            Name "WaterVolumeLipSceneDebug"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragLipSceneDebug
            #pragma target 4.0
            ENDHLSL
        }
    }
}
