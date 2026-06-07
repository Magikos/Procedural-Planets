Shader "Hidden/Atmosphere"
{
HLSLINCLUDE

#include "Includes/Common.hlsl"
#include "Includes/Math.hlsl"
#include "Includes/DebugModes.hlsl"

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
    if (_WaterVolumeEnabled <= 0.5 || _OceanDebugMode == DEBUG_ATMOSPHERE_BYPASS || _OceanDebugMode == DEBUG_VOLUME_AFTER_ATMOSPHERE)
        return 0.0;

    float waterCoverage;
    float4 waterData = SampleWaterInterfaceDilated(uv, waterCoverage);
    float waterForwardDepth = waterData.r;

    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
    float sceneForwardDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    float waterValid = step(0.0001, waterForwardDepth) * step(waterForwardDepth, sceneForwardDepth + 0.01);
    return saturate(waterCoverage * waterValid);
}


bool ShouldBypassAtmosphereForWaterDebug()
{
    return _OceanDebugMode == DEBUG_ATMOSPHERE_BYPASS
        || (_OceanDebugMode >= DEBUG_WATER_DEPTH && _OceanDebugMode <= DEBUG_WATER_ABSORPTION)  // surface isolations 1-12
        || _OceanDebugMode == DEBUG_FOAM_PARTS
        || _OceanDebugMode == DEBUG_SURFACE_ALPHA
        || _OceanDebugMode == DEBUG_SURFACE_CONTACT
        || _OceanDebugMode == DEBUG_SURFACE_BLEND
        || _OceanDebugMode == DEBUG_SURFACE_ONLY
        || _OceanDebugMode == DEBUG_WATER_OFF
        || _OceanDebugMode == DEBUG_FOAM_PINK
        || _OceanDebugMode == DEBUG_SURFACE_BACKFACE_PINK
        || (_OceanDebugMode >= DEBUG_WAKE_MASK && _OceanDebugMode <= DEBUG_SURFACE_FX_PROOF)  // surface isolation 51-57
        || (_OceanDebugMode >= DEBUG_CAUSTICS_ONLY && _OceanDebugMode <= DEBUG_CAUSTICS_PRISM)
        || (_OceanDebugMode >= DEBUG_SURFACE_NIGHT_TERMS && _OceanDebugMode <= DEBUG_WATER_GLINT_LOCATOR) // night + wave + foam + glint discovery 64-72
        || (_OceanDebugMode >= DEBUG_BIOME_PRIMARY_ID && _OceanDebugMode <= DEBUG_BIOME_ALTITUDE_COOLING)
        || (_OceanDebugMode >= DEBUG_WATER_TEMPERATURE && _OceanDebugMode <= DEBUG_WATER_ICE_CONTRIBUTION);
}

float CompositeDepthScaled(float2 uv, float viewLength)
{
    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
    float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;
    if (_WaterVolumeEnabled <= 0.5 || _OceanDebugMode == DEBUG_ATMOSPHERE_BYPASS || _OceanDebugMode == DEBUG_VOLUME_AFTER_ATMOSPHERE)
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

                float atmosphereThickness = max(_AtmosphereRadius - _SeaLevelRadius, 1.0);
                float3 cameraFromCenter = _WorldSpaceCameraPos.xyz - _PlanetCenter;
                float cameraRadius = length(cameraFromCenter);
                float3 cameraNormal = cameraRadius > 0.0001 ? cameraFromCenter / cameraRadius : float3(0.0, 1.0, 0.0);
                float cameraHeight01 = (cameraRadius - _SeaLevelRadius) / atmosphereThickness;
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

            float CameraUnderwater01()
            {
                float seaOffset = length(_WorldSpaceCameraPos.xyz - _PlanetCenter) - _SeaLevelRadius;
                return 1.0 - smoothstep(-1.5, 2.0, seaOffset);
            }

            float3 UnderwaterSkyColor(float3 viewDir)
            {
                float3 cameraUp = normalize(_WorldSpaceCameraPos.xyz - _PlanetCenter);
                float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : cameraUp;
                float daylight = smoothstep(-0.08, 0.20, dot(cameraUp, sunDir));
                float viewUp = smoothstep(-0.35, 0.85, dot(viewDir, cameraUp));
                float3 deepWater = float3(0.0, 0.020, 0.070);
                float3 litWater = lerp(float3(0.012, 0.105, 0.165), float3(0.065, 0.300, 0.420), daylight);
                return lerp(deepWater, litWater, viewUp * 0.62 + daylight * 0.24);
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
                if (ShouldBypassAtmosphereForWaterDebug())
                    return originalCol;

                float viewLength = length(i.viewVector);
                float3 viewDir = i.viewVector / max(viewLength, 0.0001);

                if (_WaterVolumeEnabled > 0.5 && CameraUnderwater01() > 0.01 && SkyDepthMask(i.uv) > 0.5)
                    return float4(UnderwaterSkyColor(viewDir), originalCol.w);

                if (_OceanDebugMode == DEBUG_ATMOSPHERE_WATER_CUT && _WaterVolumeEnabled > 0.5)
                {
                    if (WaterInterfaceFrontMask(i.uv) > 0.01)
                        return originalCol;
                }

                float sceneDepth = CompositeDepthScaled(i.uv, viewLength);
                float3 color = CalculateScattering(_WorldSpaceCameraPos.xyz, viewDir,
                    sceneDepth, originalCol.xyz);
                color += CalculateLightShafts(i.uv);

                // Water and near terrain own high-frequency local detail. Atmosphere should
                // haze those pixels, but not replace all of their surface variation.
                float waterSurfaceMask = smoothstep(0.02, 0.30, WaterInterfaceFrontMask(i.uv));
                float sourceLuma = dot(originalCol.rgb, float3(0.2126, 0.7152, 0.0722));

                // Sun-elevation-dependent detail preserve. At sunset the scattered atmosphere
                // colour is intensely orange/red; previously 62% of that bled through on water,
                // creating the "water glows under the sun" effect at low sun angles. When the
                // sun is at/below the horizon, preserve more of the original (dark) water; at
                // higher sun angles use the original 0.38 preserve so daytime is unchanged.
                float3 fromCenter = _WorldSpaceCameraPos.xyz - _PlanetCenter;
                float fromCenterLenSq = dot(fromCenter, fromCenter);
                float3 planetUpCam = fromCenterLenSq > 0.0001 ? fromCenter * rsqrt(fromCenterLenSq) : float3(0.0, 1.0, 0.0);
                float3 sunDirNorm = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : float3(0.0, 1.0, 0.0);
                float sunElevation = dot(planetUpCam, sunDirNorm); // -1 below horizon, +1 zenith
                float lowSun01 = 1.0 - smoothstep(-0.05, 0.30, sunElevation); // 1 at horizon, 0 above ~17 deg
                // Was lerp(0.38, 0.75) which made sunset water too dark - lost the warm orange glow
                // user wanted to keep. Reduced sunset cap so ~45% of the warm atmosphere still
                // contributes (vs 25% with the harder fix, vs 62% with no fix at all).
                float detailPreserveFraction = lerp(0.38, 0.55, lowSun01);

                float detailPreserve = waterSurfaceMask * detailPreserveFraction;
                float highlightPreserve = waterSurfaceMask * smoothstep(0.32, 0.82, sourceLuma) * 0.24;
                color = lerp(color, originalCol.rgb, detailPreserve);
                color = lerp(color, max(color, originalCol.rgb), highlightPreserve);

                float sceneMask = 1.0 - SkyDepthMask(i.uv);
                float nonWaterSceneMask = sceneMask * (1.0 - waterSurfaceMask);
                float atmosphereThickness = max(_AtmosphereRadius - _SeaLevelRadius, 1.0);
                float terrainNearMask = 1.0 - smoothstep(atmosphereThickness * 1.5, atmosphereThickness * 7.0, sceneDepth);
                float terrainDetailPreserve = nonWaterSceneMask * terrainNearMask * lerp(0.24, 0.42, lowSun01);
                color = lerp(color, originalCol.rgb, terrainDetailPreserve);

                if (_OceanDebugMode == DEBUG_ATMOSPHERE_CONTRIBUTION)
                    return float4(ContributionHeat(color - originalCol.xyz, 8.0, float3(0.18, 0.48, 1.0), float3(1.0, 0.95, 0.22)), 1.0);

                return float4(color, originalCol.w);
            }

            ENDHLSL
        }
    }
}
