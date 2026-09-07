#if defined(IMPOSTOR_MOTION_PASS)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
#endif
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff;
                float _HasSurfaceData;
                float _GridN;
                float3 _CenterOffset;
                float _WorldSize;
                float _LeafCard;
                float _CardBrightness;
                float _FadeInStart;
                float _FadeInEnd;
                float _FadeOutStart;
                float _FadeOutEnd;
                // Must stay in every pass's UnityPerMaterial, in the same order: the SRP batcher requires an
                // identical layout across a shader's passes.
                float4 _LodDebugTint;
            CBUFFER_END

            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                StructuredBuffer<float4x4> _ScatterMatrices;
                StructuredBuffer<float4x4> _ScatterMatricesInv;
                StructuredBuffer<uint> _ScatterVisible;
                StructuredBuffer<float> _ScatterBorn;
            #endif
            float ScatterAppear()
            {
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                if (_ScatterFadeInSeconds <= 0.0) return 1.0;
                return saturate((_Time.y - _ScatterBorn[_ScatterVisible[unity_InstanceID]]) / _ScatterFadeInSeconds);
            #else
                return 1.0;
            #endif
            }
            void setup()
            {
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                uint idx = _ScatterVisible[unity_InstanceID];
                unity_ObjectToWorld = _ScatterMatrices[idx];
                unity_WorldToObject = _ScatterMatricesInv[idx];
            #endif
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 gridCoord : TEXCOORD1;
                float dist : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float3 billUp : TEXCOORD5;
                float appear : TEXCOORD8;
                float3 positionWS : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 world, right, up, view;
                float2 gridCoord;
                OctBillboard(GetObjectToWorldMatrix(), IN.positionOS.xy, _GridN, _CenterOffset, _WorldSize,
                    world, right, up, view, gridCoord);

                OUT.positionHCS = TransformWorldToHClip(world);
                OUT.positionWS = world;
                OUT.uv = IN.uv;
                OUT.gridCoord = gridCoord;
                OUT.dist = distance(_WorldSpaceCameraPos, GetObjectToWorldMatrix()._m03_m13_m23);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                OUT.billUp = up;
                OUT.appear = ScatterAppear();
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                ImpostorSurface sample = SampleOctSurface(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap),
                    TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), IN.gridCoord, IN.uv, _GridN, _HasSurfaceData);
                half4 card = sample.colour;
                clip(card.a - _Cutoff);

                float coverage = ScatterCardCoverage(IN.dist, float2(_FadeInStart, _FadeInEnd),
                    float2(_FadeOutStart, _FadeOutEnd), IN.appear);

                clip(coverage - ScatterDitherThreshold(IN.screenPos));

                float3 surfaceOS = sample.surfaceOS;
                float leafMask = sample.leafMask;
                float3 normalOS = sample.normalOS;
                float3 N = TransformObjectToWorldNormal(normalOS);
                #if defined(IMPOSTOR_MOTION_PASS)
                    float3 surfaceWS = _HasSurfaceData > 0.5
                        ? TransformObjectToWorld(_CenterOffset + sample.surfaceOS * _WorldSize) : IN.positionWS;
                    return float4(CalcNdcMotionVectorFromCsPositions(
                        mul(_NonJitteredViewProjMatrix, float4(surfaceWS, 1)),
                        mul(_PrevViewProjMatrix, float4(surfaceWS, 1))), 0, 0);
                #else
                    return half4(N, 0);
                #endif
            }
