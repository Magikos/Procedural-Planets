#if defined(FOLIAGE_MOTION_PASS)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
#endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVaryings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float leafMask : TEXCOORD3;
                float4 screenPos : TEXCOORD4;
                float appear : TEXCOORD5;
                #if defined(FOLIAGE_MOTION_PASS)
                    float4 currentCS : TEXCOORD6;
                    float4 previousCS : TEXCOORD7;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DNVaryings dnVert(DNAttributes IN)
            {
                DNVaryings OUT = (DNVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float leafMask = max(LeafMask(IN.color.b), _ForceLeaf);
                float3 wsp = ApplyWind(TransformObjectToWorld(IN.positionOS.xyz), leafMask);
                OUT.positionWS = wsp;
                OUT.positionHCS = TransformWorldToHClip(wsp);
                #if defined(FOLIAGE_MOTION_PASS)
                    float3 previousWS = TransformObjectToWorld(IN.positionOS.xyz);
                    #if !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                        previousWS = mul(UNITY_PREV_MATRIX_M, IN.positionOS).xyz;
                    #endif
                    previousWS = ApplyWind(previousWS, leafMask, true);
                    OUT.currentCS = mul(_NonJitteredViewProjMatrix, float4(wsp, 1));
                    OUT.previousCS = mul(_PrevViewProjMatrix, float4(previousWS, 1));
                #endif
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.leafMask = leafMask;
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                OUT.appear = ScatterAppear();
                return OUT;
            }

            half4 dnFrag(DNVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                DistanceDither(IN.screenPos, IN.appear);
                half4 leaf = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float lm = IN.leafMask;
                clip(lerp(1.0, leaf.a, lm) - LeafCutoff(IN.uv, lm));
                #if defined(FOLIAGE_MOTION_PASS)
                    return float4(CalcNdcMotionVectorFromCsPositions(IN.currentCS, IN.previousCS), 0, 0);
                #else
                    return half4(normalize(IN.normalWS), 0.0);
                #endif
            }
