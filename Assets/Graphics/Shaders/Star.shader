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
float3 _PlanetCenter;
float _SeaLevelRadius;
float _AtmosphereRadius;

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

float PlanetSkyVisibility(float3 dir)
{
    if (_SeaLevelRadius <= 0.0)
        return 1.0;

    float3 offset = _WorldSpaceCameraPos.xyz - _PlanetCenter;
    float cameraRadius = length(offset);
    float3 rayDir = normalize(dir);

    if (cameraRadius <= _SeaLevelRadius * 1.002)
    {
        float3 localNormal = cameraRadius > 0.0001 ? offset / cameraRadius : float3(0.0, 1.0, 0.0);
        float horizonDot = dot(localNormal, rayDir);
        return smoothstep(-0.018, 0.032, horizonDot);
    }

    float2 planetHit = RaySphere(_PlanetCenter, _SeaLevelRadius, _WorldSpaceCameraPos.xyz, rayDir);
    float rayHitsPlanet = step(0.0001, planetHit.y) * step(0.0, planetHit.x);
    float rayForward = dot(offset, rayDir);
    float closestSq = max(dot(offset, offset) - rayForward * rayForward, 0.0);
    float horizonClearance = sqrt(closestSq) - _SeaLevelRadius;
    float horizonSoftness = max(_SeaLevelRadius * 0.00035, 0.35);
    float horizonVisibility = smoothstep(-horizonSoftness, horizonSoftness, horizonClearance);
    return lerp(1.0, horizonVisibility, rayHitsPlanet);
}

float StarVisibility(float3 dir)
{
    float3 sunDir = dot(_SunParams.xyz, _SunParams.xyz) > 0.0001 ? normalize(_SunParams.xyz) : float3(0.0, 1.0, 0.0);
    float3 fromCenter = _WorldSpaceCameraPos.xyz - _PlanetCenter;
    float cameraRadius = length(fromCenter);

    if (_AtmosphereRadius <= _SeaLevelRadius || cameraRadius <= 0.0001)
        return 1.0;

    float insideAtmosphere = 1.0 - smoothstep(_AtmosphereRadius * 0.92, _AtmosphereRadius * 1.08, cameraRadius);
    float localDay = smoothstep(-0.06, 0.18, dot(fromCenter / cameraRadius, sunDir));
    float sunFacing = smoothstep(-0.15, 0.55, dot(dir, sunDir));
    float daylightFade = saturate(localDay * lerp(0.82, 1.0, sunFacing) * insideAtmosphere);

    return 1.0 - daylightFade;
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
                float skyVisibility = PlanetSkyVisibility(dir);
                float3 background = (ProceduralStars(dir) * StarVisibility(dir) + SunDisc(dir)) * skyVisibility;
                return float4(background, 1);
            }
            ENDHLSL
        }
    }
}
