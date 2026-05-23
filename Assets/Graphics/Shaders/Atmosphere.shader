Shader "Hidden/Atmosphere"
{
HLSLINCLUDE

#include "Includes/Common.hlsl"
#include "Includes/Math.hlsl"

TEXTURE2D(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);
TEXTURE2D(_WaterInterfaceTexture);
SAMPLER(sampler_WaterInterfaceTexture);

float4 _LightShaftParams;
float4 _LightShaftParams2;
int _LightShaftSamples;
int _PrecipitationDebugMode;
int _OceanDebugMode;
float _WaterVolumeEnabled;

float LightShaftNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

float WaterInterfaceCoverage(float4 waterData)
{
    return smoothstep(0.0005, 0.018, max(saturate(waterData.g), saturate(waterData.b)));
}

void AccumulateWaterInterface(float2 uv, float edgeWeight, inout float4 bestData, inout float bestCoverage)
{
    float4 candidate = SAMPLE_TEXTURE2D(_WaterInterfaceTexture, sampler_WaterInterfaceTexture, uv);
    float candidateCoverage = WaterInterfaceCoverage(candidate) * edgeWeight;
    if (candidateCoverage > bestCoverage)
    {
        bestData = candidate;
        bestCoverage = candidateCoverage;
    }
}

float4 SampleWaterInterfaceDilated(float2 uv, out float waterCoverage)
{
    float2 texel = 1.0 / max(_ScreenParams.xy, float2(1.0, 1.0));
    float4 waterData = SAMPLE_TEXTURE2D(_WaterInterfaceTexture, sampler_WaterInterfaceTexture, uv);
    waterCoverage = WaterInterfaceCoverage(waterData);
    AccumulateWaterInterface(uv + float2(texel.x, 0.0), 0.74, waterData, waterCoverage);
    AccumulateWaterInterface(uv - float2(texel.x, 0.0), 0.74, waterData, waterCoverage);
    AccumulateWaterInterface(uv + float2(0.0, texel.y), 0.74, waterData, waterCoverage);
    AccumulateWaterInterface(uv - float2(0.0, texel.y), 0.74, waterData, waterCoverage);
    return waterData;
}

float WaterInterfaceFrontMask(float2 uv)
{
    if (_WaterVolumeEnabled <= 0.5 || _OceanDebugMode == 40 || _OceanDebugMode == 41)
        return 0.0;

    float waterCoverage;
    float4 waterData = SampleWaterInterfaceDilated(uv, waterCoverage);
    float waterForwardDepth = waterData.r;

    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
    float sceneForwardDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    float waterValid = step(0.0001, waterForwardDepth) * step(waterForwardDepth, sceneForwardDepth + 0.01);
    return saturate(waterCoverage * waterValid);
}

float3 ContributionHeat(float3 delta, float scale)
{
    float intensity = saturate(dot(abs(delta), float3(0.2126, 0.7152, 0.0722)) * scale);
    float lowToMid = smoothstep(0.02, 0.30, intensity);
    float midToHigh = smoothstep(0.30, 0.88, intensity);
    float3 color = lerp(float3(0.0, 0.0, 0.0), float3(0.18, 0.48, 1.0), lowToMid);
    color = lerp(color, float3(1.0, 0.95, 0.22), midToHigh);
    color += smoothstep(0.82, 1.0, intensity) * 0.25;
    return saturate(color);
}

float CompositeDepthScaled(float2 uv, float viewLength)
{
    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
    float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;
    if (_WaterVolumeEnabled <= 0.5 || _OceanDebugMode == 40 || _OceanDebugMode == 41)
        return sceneDepth;

    float waterCoverage;
    float4 waterData = SampleWaterInterfaceDilated(uv, waterCoverage);
    float waterForwardDepth = waterData.r;
    float waterDepth = waterForwardDepth * viewLength;
    float waterValid = step(0.0001, waterForwardDepth) * step(waterDepth, sceneDepth + 0.01);
    float interfaceMask = saturate(waterCoverage * waterValid);
    return lerp(sceneDepth, min(sceneDepth, waterDepth), interfaceMask);
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
            #pragma multi_compile _ DIRECTIONAL_SUN

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

            float SkyDepthMask(float2 uv)
            {
                float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
                #if UNITY_REVERSED_Z
                    return 1.0 - step(0.0001, rawDepth);
                #else
                    return step(0.9999, rawDepth);
                #endif
            }

            float3 CalculateLightShafts(float2 uv)
            {
                int sampleCount = min(max(_LightShaftSamples, 0), 32);
                float strength = _LightShaftParams.x;
                if (strength <= 0.0 || sampleCount <= 0 || _PrecipitationDebugMode > 0)
                    return float3(0.0, 0.0, 0.0);

                float waterBlock = WaterInterfaceFrontMask(uv);
                float targetVisibility = 1.0 - smoothstep(0.01, 0.22, waterBlock);
                if (targetVisibility <= 0.0)
                    return float3(0.0, 0.0, 0.0);

                float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : float3(0.0, 1.0, 0.0);
                float4 sunClip = TransformWorldToHClip(_WorldSpaceCameraPos.xyz + sunDir * max(_AtmosphereRadius, 1000.0));
                float2 sunNdc = sunClip.xy / max(abs(sunClip.w), 0.0001);
                float2 sunUv = float2(sunNdc.x, sunNdc.y * _ProjectionParams.x) * 0.5 + 0.5;

                float sunFacing = sunClip.w > 0.0 ? 1.0 : 0.0;
                float screenDistance = max(abs(sunNdc.x), abs(sunNdc.y));
                float screenFade = 1.0 - smoothstep(0.95, max(_LightShaftParams2.w, 0.96), screenDistance);

                float atmosphereThickness = max(_AtmosphereRadius - _PlanetRadius, 1.0);
                float3 cameraFromCenter = _WorldSpaceCameraPos.xyz - _PlanetCenter;
                float cameraRadius = length(cameraFromCenter);
                float3 cameraNormal = cameraRadius > 0.0001 ? cameraFromCenter / cameraRadius : float3(0.0, 1.0, 0.0);
                float cameraHeight01 = (cameraRadius - _PlanetRadius) / atmosphereThickness;
                float altitudeFade = 1.0 - smoothstep(0.85, 1.25, cameraHeight01);
                float localSunVisibility = smoothstep(-0.025, 0.065, dot(cameraNormal, sunDir));
                float visibility = sunFacing * screenFade * altitudeFade * localSunVisibility;

                if (visibility <= 0.0)
                    return float3(0.0, 0.0, 0.0);

                float2 delta = (uv - sunUv) * (_LightShaftParams.y / sampleCount);
                float rayJitter = LightShaftNoise(uv * _ScreenParams.xy) - 0.5;
                float2 sampleUv = uv - delta * rayJitter;
                float illuminationDecay = 1.0;
                float3 light = 0.0;

                [loop]
                for (int s = 0; s < 32; s++)
                {
                    if (s >= sampleCount)
                        break;

                    sampleUv -= delta;
                    float inBounds = step(0.0, sampleUv.x) * step(sampleUv.x, 1.0)
                        * step(0.0, sampleUv.y) * step(sampleUv.y, 1.0);
                    float sampleWaterBlock = WaterInterfaceFrontMask(sampleUv);
                    float skyMask = SkyDepthMask(sampleUv) * inBounds * (1.0 - smoothstep(0.01, 0.22, sampleWaterBlock));
                    float3 sampleColor = SAMPLE_TEXTURE2D(_Source, sampler_Source, sampleUv).rgb;
                    float luminance = dot(sampleColor, float3(0.2126, 0.7152, 0.0722));
                    float brightMask = smoothstep(_LightShaftParams2.y, _LightShaftParams2.y + _LightShaftParams2.z, luminance);
                    float sunDistance = length(sampleUv - sunUv);
                    float sunProximity = 1.0 - smoothstep(0.04, 0.72, sunDistance);
                    float directSunMask = 1.0 - smoothstep(0.0, 0.22, sunDistance);
                    float shaftMask = saturate(brightMask * sunProximity + directSunMask * 0.45);

                    float3 shaftColor = lerp(float3(0.78, 0.88, 1.0), float3(1.0, 0.88, 0.58), saturate(1.0 - sunDir.y));
                    light += shaftColor * shaftMask * skyMask * illuminationDecay * _LightShaftParams.w;
                    illuminationDecay *= _LightShaftParams.z;
                }

                return light * _LightShaftParams2.x * strength * visibility * SkyDepthMask(uv) * targetVisibility;
            }

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
                if (_OceanDebugMode == 40)
                    return originalCol;

                float viewLength = length(i.viewVector);

                if (_OceanDebugMode == 42 && _WaterVolumeEnabled > 0.5)
                {
                    if (WaterInterfaceFrontMask(i.uv) > 0.01)
                        return originalCol;
                }

                float sceneDepth = CompositeDepthScaled(i.uv, viewLength);
                float3 color = CalculateScattering(_WorldSpaceCameraPos.xyz, i.viewVector / viewLength,
                    sceneDepth, originalCol.xyz);
                color += CalculateLightShafts(i.uv);

                if (_OceanDebugMode == 44)
                    return float4(ContributionHeat(color - originalCol.xyz, 8.0), 1.0);

                return float4(color, originalCol.w);
            }

            ENDHLSL
        }
    }
}
