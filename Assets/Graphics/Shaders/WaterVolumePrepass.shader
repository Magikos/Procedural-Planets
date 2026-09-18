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
            float2 blend : TEXCOORD1;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float4 waterData : TEXCOORD0;
            float forwardDepth : TEXCOORD1;
            float3 positionWS : TEXCOORD2;
            float river : TEXCOORD3;
            float mouth : TEXCOORD4;
            float baseRadius : TEXCOORD5;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.baseRadius = length(positionWS - _PlanetCenter) - _WaterSurfaceOffset;
            float4 waterData;

            // Displace exactly as Ocean.shader does. forwardDepth is the distance the volume composite
            // measures its water column from, so it has to describe the surface the player actually sees;
            // sampling the undisplaced shell puts every column read out by up to the swell amplitude.
            float3 planetNormalWS = SafeNormalize(positionWS - _PlanetCenter, float3(0.0, 1.0, 0.0));
            float3 swellNormalWS;
            float swellHeight;
            positionWS = ComputeWaterMeshDisplacement(positionWS, planetNormalWS, input.color, input.blend, waterData, swellNormalWS, swellHeight);

            float3 positionVS = TransformWorldToView(positionWS);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.waterData = waterData;
            output.river = input.color.a > 1.0 ? 1.0 : 0.0;
            output.mouth = input.blend.y > .5 && input.blend.y < 1.5 ? 1.0 : 0.0;
            output.forwardDepth = max(-positionVS.z, 0.0);
            output.positionWS = positionWS;
            return output;
        }

        float4 EncodeWaterVolumeData(Varyings input)
        {
            float sceneValid;
            float waterPath;
            float column = MeasuredWaterColumn(input.positionWS, GetNormalizedScreenSpaceUV(input.positionCS),
                sceneValid, waterPath);
            float measured01 = saturate(column / max(_DeepDepth, 0.001));

            // Open sky behind the water - the horizon - has no scene to measure against, and the reconstructed
            // position there is the far plane, which would read as immeasurably deep OR as zero depending on
            // the platform's depth convention. Keep the mesh's own baked depth for those pixels; it is coarse
            // but it is never nonsense.
            float depth01 = lerp(input.waterData.r, measured01, sceneValid);
            float shore01 = input.waterData.g;
            float body01 = input.waterData.b;
            float freezeFactor = EvaluateFreezeFactor(input.waterData.a, body01);
            uint kind = body01 >= .5 ? WATER_KIND_OCEAN : WATER_KIND_LAKE;
            if (input.river > .5) kind |= WATER_KIND_RIVER;
            return float4(input.forwardDepth, depth01, EncodeWaterShoreKind(shore01, kind), freezeFactor);
        }

        float4 Frag(Varyings input) : SV_Target
        {
            if (input.mouth > .5)
            {
                clip(_WaterLevelRes - 1);
                float3 direction = normalize(input.positionWS - _PlanetCenter);
                int face; float2 uv;
                WaterLevelFaceUv(direction, _WaterLevelRes, face, uv);
                float level = SAMPLE_TEXTURE2D_ARRAY_LOD(_WaterLevelTex, sampler_WaterLevelTex, uv, face, 0).r;
                clip(level - WATER_LEVEL_NO_WATER_MAX);
                float standingRadius = _WaterLevelBaseRadius * (1.0 + level) + _WaterSurfaceOffset;
                clip(max(_SwellAmplitude, 0.0) + 1.25 - abs(length(input.positionWS - _PlanetCenter) - standingRadius));
            }
            float sceneDepth = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS)), _ZBufferParams);
            clip(sceneDepth - input.forwardDepth);

            if (input.river < .5 && _RiverActive != 0)
            {
                float3 direction = normalize(input.positionWS - _PlanetCenter);
                int face; float2 uv;
                WaterLevelFaceUv(direction, max(_WaterLevelRes, 1), face, uv);
                float level = SAMPLE_TEXTURE2D_ARRAY_LOD(_WaterLevelTex, sampler_WaterLevelTex, uv, face, 0).r;
                bool standing = _WaterLevelRes > 0 && level > WATER_LEVEL_NO_WATER_MAX;
                if (RiverBankBelow(direction, input.baseRadius, standing)) discard;
            }

            return EncodeWaterVolumeData(input);
        }

        ENDHLSL

        Pass
        {
            Name "WaterVolumePrepass"
            Cull Off
            ZWrite On
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
