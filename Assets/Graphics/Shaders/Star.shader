Shader "Hidden/Stars"
{
HLSLINCLUDE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Includes/Math.hlsl"

float _StarSeed;
float _StarDensity;
float _StarBrightness;
float3 _SunParams;
float _SunIntensity;
float _SunDiscSize;
float _SunDiscBlend;

float Hash31(float3 p)
{
    p = frac(p * float3(443.897, 441.423, 437.195));
    p += dot(p, p.yzx + 19.19);
    return frac((p.x + p.y) * p.z);
}

float2 Hash32(float3 p)
{
    p = frac(p * float3(443.897, 441.423, 437.195));
    p += dot(p, p.yzx + 19.19);
    return frac(float2((p.x + p.y) * p.z, (p.z + p.x) * p.y));
}

float3 ProceduralStars(float3 dir)
{
    float theta = atan2(dir.z, dir.x);
    float phi = asin(dir.y);

    float gridScale = _StarDensity;
    float2 gridUV = float2(theta * (gridScale / MATH_PI), phi * (gridScale / (MATH_PI * 0.5)));
    float2 cellID = floor(gridUV);
    float2 cellUV = frac(gridUV) - 0.5;

    float3 starColor = 0;

    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            float2 neighbor = float2(x, y);
            float2 id = cellID + neighbor;

            float3 hashInput = float3(id.x, id.y, _StarSeed);
            float starPresence = Hash31(hashInput);
            if (starPresence > 0.3) continue;

            float2 starOffset = Hash32(hashInput + 7.0) - 0.5;
            float2 delta = neighbor + starOffset - cellUV;
            float dist = length(delta);

            float sizeFactor = Hash31(hashInput + 13.0);
            float starRadius = lerp(0.01, 0.04, sizeFactor * sizeFactor);

            float glow = saturate(1.0 - dist / starRadius);
            glow = glow * glow * glow;

            float brightness = lerp(0.2, 1.0, sizeFactor * sizeFactor) * _StarBrightness;

            float colorHash = Hash31(hashInput + 23.0);
            float3 tint = colorHash > 0.85 ? float3(0.85, 0.9, 1.0)
                         : colorHash < 0.1  ? float3(1.0, 0.92, 0.8)
                         : float3(1, 1, 1);

            starColor += glow * brightness * tint;
        }
    }

    return starColor;
}

float3 SunDisc(float3 dir)
{
    float discSize = _SunDiscSize > 0.0 ? _SunDiscSize : 0.9995;
    float discBlend = _SunDiscBlend > 0.0 ? _SunDiscBlend : 0.002;
    float3 sunDir = dot(_SunParams.xyz, _SunParams.xyz) > 0.0001 ? normalize(_SunParams.xyz) : float3(0.0, 1.0, 0.0);
    float sunDot = dot(dir, sunDir);
    float sunDisc = smoothstep(discSize - discBlend, discSize, sunDot);
    float3 sunColor = sunDisc * float3(1.2, 1.1, 0.9) * _SunIntensity;
    return sunColor / (1.0 + sunColor);
}

ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "RenderStars"

            HLSLPROGRAM
            #pragma vertex StarVertex
            #pragma fragment StarFragment
            #pragma target 4.0

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 viewVector : TEXCOORD0;
            };

            v2f StarVertex(uint vertexID : SV_VertexID)
            {
                v2f output;
                output.pos = GetFullScreenTriangleVertexPosition(vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(vertexID);
                #if UNITY_UV_STARTS_AT_TOP
                    float2 ndc = float2(uv.x * 2.0 - 1.0, uv.y * 2.0 - 1.0);
                #else
                    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
                #endif
                float3 viewVector = mul(unity_CameraInvProjection, float4(ndc, 0, -1)).xyz;
                output.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0)).xyz;
                return output;
            }

            float4 StarFragment(v2f i) : SV_Target
            {
                float3 dir = normalize(i.viewVector);
                float3 background = ProceduralStars(dir) + SunDisc(dir);
                return float4(background, 1);
            }
            ENDHLSL
        }
    }
}
