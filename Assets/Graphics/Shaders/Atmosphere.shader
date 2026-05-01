Shader "Hidden/Atmosphere"
{
HLSLINCLUDE

#include "Includes/Common.hlsl"
#include "Includes/Math.hlsl"

TEXTURE2D(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);

float CompositeDepthScaled(float2 uv, float viewLength)
{
    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
    return LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;
}

#include "Includes/Atmosphere.hlsl"

ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Off

        Pass
        {
            Name "RenderAtmosphere"

            HLSLPROGRAM

            #pragma vertex AtmosphereVertex
            #pragma fragment AtmosphereFragment

            #pragma target 4.0
            #pragma multi_compile _ ATMOSPHERE_DEBUG_DEPTH ATMOSPHERE_DEBUG_SCATTER ATMOSPHERE_DEBUG_SURFACE

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 viewVector : TEXCOORD1;
            };

            TEXTURE2D(_Source);
            SAMPLER(sampler_Source);

            v2f AtmosphereVertex(Attributes v)
            {
                v2f output;
                output.pos = GetFullScreenTriangleVertexPosition(v.vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(v.vertexID);
                output.uv = uv;

                #if UNITY_UV_STARTS_AT_TOP
                    float2 ndcForView = float2(uv.x * 2.0 - 1.0, uv.y * 2.0 - 1.0);
                #else
                    float2 ndcForView = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
                #endif
                float3 viewVector = mul(unity_CameraInvProjection, float4(ndcForView, 0, -1)).xyz;
                output.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0)).xyz;
                return output;
            }

            float4 AtmosphereFragment(v2f i) : SV_Target
            {
                float4 originalCol = SAMPLE_TEXTURE2D(_Source, sampler_Source, i.uv);
                float viewLength = length(i.viewVector);
                float sceneDepth = CompositeDepthScaled(i.uv, viewLength);

                #if defined(ATMOSPHERE_DEBUG_DEPTH)
                    float depthVis = saturate(sceneDepth / (_PlanetRadius * 4));
                    return float4(depthVis, depthVis, depthVis, 1);
                #elif defined(ATMOSPHERE_DEBUG_SCATTER)
                    float3 dbgScatter = CalculateScattering(_WorldSpaceCameraPos.xyz, i.viewVector / viewLength, sceneDepth, float3(0,0,0), i.uv);
                    return float4(dbgScatter, 1);
                #elif defined(ATMOSPHERE_DEBUG_SURFACE)
                    float3 dbgFull = CalculateScattering(_WorldSpaceCameraPos.xyz, i.viewVector / viewLength, sceneDepth, originalCol.xyz, i.uv);
                    float3 dbgNoScene = CalculateScattering(_WorldSpaceCameraPos.xyz, i.viewVector / viewLength, sceneDepth, float3(0,0,0), i.uv);
                    return float4(dbgFull - dbgNoScene, 1);
                #endif

                float3 color = CalculateScattering(_WorldSpaceCameraPos.xyz, i.viewVector / viewLength, sceneDepth, originalCol.xyz, i.uv);
                return float4(color, originalCol.w);
            }

            ENDHLSL
        }
    }
}
