Shader "Scatter/VertexColorLit"
{
    // Lit shader for scatter props. Synty low-poly bakes colour into vertex colours (the atlas is a
    // single flat swatch), so albedo = vertexColor * baseMap. A screen-space 4x4 Bayer dither fades
    // instances out as they approach the cull distance, hiding the LOD/gather pop-in.
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.1
        _FadeStart ("Fade Start Distance", Float) = 120
        _FadeEnd ("Fade End Distance", Float) = 150
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Smoothness;
            float _FadeStart;
            float _FadeEnd;
        CBUFFER_END

        // 4x4 Bayer matrix, normalised 0..1.
        static const float _Bayer4x4[16] = {
            0.0/16, 8.0/16, 2.0/16, 10.0/16,
            12.0/16, 4.0/16, 14.0/16, 6.0/16,
            3.0/16, 11.0/16, 1.0/16, 9.0/16,
            15.0/16, 7.0/16, 13.0/16, 5.0/16
        };

        // Discards the fragment progressively as the instance nears the cull distance.
        void DistanceDither(float3 positionWS, float4 screenPos)
        {
            float dist = distance(positionWS, _WorldSpaceCameraPos);
            float fade = saturate((dist - _FadeStart) / max(1e-3, _FadeEnd - _FadeStart)); // 0 near, 1 at cull
            float2 sp = (screenPos.xy / max(screenPos.w, 1e-4)) * _ScreenParams.xy;
            int2 pix = int2(fmod(sp, 4.0));
            float threshold = _Bayer4x4[pix.y * 4 + pix.x];
            clip(threshold - fade); // fade > threshold -> discard
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 color : COLOR;
                float fogFactor : TEXCOORD3;
                float4 screenPos : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color = IN.color;
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                OUT.screenPos = ComputeScreenPos(pos.positionCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                DistanceDither(IN.positionWS, IN.screenPos);

                // Albedo = base map * tint. Vertex colour is intentionally NOT used as albedo:
                // Synty low-poly stores a data mask there (blue), not display colour. With no base
                // map assigned the sampler returns white, so albedo = _BaseColor (flat tint).
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;
                half3 albedo = _BaseColor.rgb * tex;

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalize(IN.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogFactor;
                // Ambient / indirect: without bakedGI, surfaces facing away from the sun render black.
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = IN.positionHCS.xy / max(_ScreenParams.xy, 1.0);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo = albedo;
                surf.smoothness = _Smoothness;
                surf.occlusion = 1.0;
                surf.alpha = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surf);
                color.rgb = MixFog(color.rgb, IN.fogFactor);
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct SAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct SVaryings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            SVaryings shadowVert(SAttributes IN)
            {
                SVaryings OUT = (SVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 hcs = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    hcs.z = min(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    hcs.z = max(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionHCS = hcs;
                return OUT;
            }

            half4 shadowFrag(SVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
