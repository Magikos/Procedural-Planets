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
            float3 positionWS : TEXCOORD2;
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
            output.positionWS = positionWS;
            return output;
        }

        // How deep the water is AT THIS PIXEL, measured against the depth buffer rather than read from the
        // mesh's baked vertex depth.
        //
        // The water mesh cannot resolve a shoreline finer than its own triangles, so a vertex-baked depth
        // quantises the whole shoreline feather to the mesh edges - which is why the feather used to snap to
        // triangle contours no matter how it was tuned. The depth buffer knows exactly where the bed is under
        // every pixel, so the feather now follows the real waterline and is independent of mesh resolution.
        //
        // Measured RADIALLY, because down on a planet is toward its centre, not along world -Y.
        float MeasuredWaterColumn(Varyings input, out float sceneValid)
        {
            float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS);
            float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, screenUv).r;
            sceneValid = SceneDepthValid(rawDepth);

            float3 sceneWS = ComputeWorldSpacePosition(screenUv, rawDepth, UNITY_MATRIX_I_VP);
            float3 up = SafeNormalize(input.positionWS - _PlanetCenter, float3(0.0, 1.0, 0.0));
            return max(dot(input.positionWS - sceneWS, up), 0.0);
        }


        float4 EncodeWaterVolumeData(Varyings input)
        {
            float sceneValid;
            float measured01 = saturate(MeasuredWaterColumn(input, sceneValid) / max(_DeepDepth, 0.001));

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
