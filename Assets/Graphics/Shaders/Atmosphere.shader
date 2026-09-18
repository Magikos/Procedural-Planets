Shader "Hidden/Atmosphere"
{
HLSLINCLUDE

#include "Includes/Common.hlsl"
#include "Includes/Math.hlsl"
#include "Includes/DebugModes.hlsl"
#include "Includes/WaterVolumeData.hlsl"
#include "Includes/WaterLevelField.hlsl"
#include "Includes/WaterDisplacement.hlsl"

TEXTURE2D(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);
TEXTURE2D(_WaterInterfaceTexture);
SAMPLER(sampler_WaterInterfaceTexture);

float _SceneDepthDebug;       // DEBUG: 1 = show _CameraDepthTexture (red=sky/no-geometry, gray=geometry)
float _SceneDepthDebugRange;  // DEBUG: metres mapped to white in the grayscale depth view

float4 _LightShaftParams;
float4 _LightShaftParams2;
float4 _SunAureoleParams; // x=strength, y=power(radius)
float2 _TerrainAerialPerspectiveDistances;
int _LightShaftSamples;
int _PrecipitationDebugMode;
int _OceanDebugMode;
float _WaterVolumeEnabled;
// Published by CelestialManager; the shared night floor every lit surface uses. Tunable with `light.*`.
// The moon pair comes from the same publisher, on the scale WaterVolume lights its caustics with.
float _NightAmbientIntensity;
float3 _MoonParams;
float _MoonIntensity;
float _UnderwaterShaftIntensity;
float _UnderwaterShaftWidth;
float _UnderwaterSurfaceDetail;

float LightShaftNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}


float4 SampleWaterInterface(float2 uv, out float waterCoverage)
{
    // The prepass and surface cover the same displaced triangles. Expanding coverage into sky
    // suppresses atmospheric light outside the mesh and draws a dark line along the horizon.
    float4 waterData = SAMPLE_TEXTURE2D(_WaterInterfaceTexture, sampler_WaterInterfaceTexture, uv);
    waterCoverage = WaterVolumeCoverage(waterData);
    return waterData;
}
float WaterInterfaceFrontMask(float2 uv)
{
    if (_WaterVolumeEnabled <= 0.5 || _OceanDebugMode == DEBUG_ATMOSPHERE_BYPASS || _OceanDebugMode == DEBUG_VOLUME_AFTER_ATMOSPHERE)
        return 0.0;

    float waterCoverage;
    float4 waterData = SampleWaterInterface(uv, waterCoverage);
    float waterForwardDepth = waterData.r;

    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
    float sceneForwardDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    float waterValid = step(0.0001, waterForwardDepth) * step(waterForwardDepth, sceneForwardDepth + 0.01);
    // The interface ends the air path even when shallow water transmits the bed.
    // Optical coverage belongs to the volume composite, not atmospheric distance.
    return waterValid;
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
        || (_OceanDebugMode >= DEBUG_WATER_TEMPERATURE && _OceanDebugMode <= DEBUG_WATER_ICE_CONTRIBUTION)
        || (_OceanDebugMode >= DEBUG_TERRAIN_COAST_MASK && _OceanDebugMode <= DEBUG_TERRAIN_OVERRIDE_COMPOSITE)
        // The water-data shape views must show the raw channel, not the channel with aerial perspective
        // composited over it - that washes the contour bands out and was making a real difference look flat.
        // ShapeIsWaterMask and ShapeIsCompositeDepth are deliberately NOT listed: they live IN this shader,
        // and bypassing would return before they ever ran.
        || _OceanDebugMode == DEBUG_SHAPE_IS_DEPTH
        || _OceanDebugMode == DEBUG_SHAPE_IS_SHORE
        || _OceanDebugMode == DEBUG_SHAPE_IS_BODY
        || _OceanDebugMode == DEBUG_SHAPE_IS_DATA_EDGE;
}

float CompositeDepthScaled(float2 uv, float viewLength)
{
    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
    float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;
    if (_WaterVolumeEnabled <= 0.5 || _OceanDebugMode == DEBUG_ATMOSPHERE_BYPASS || _OceanDebugMode == DEBUG_VOLUME_AFTER_ATMOSPHERE)
        return sceneDepth;

    float waterCoverage;
    float4 waterData = SampleWaterInterface(uv, waterCoverage);
    float waterForwardDepth = waterData.r;
    float waterDepth = waterForwardDepth * viewLength;
    float waterValid = step(0.0001, waterForwardDepth) * step(waterDepth, sceneDepth + 0.01);
    float interfaceMask = waterValid;
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
                    // Aspect-corrected UV distance: raw length(sampleUv - sunUv) treats a
                    // non-square screen as a square, so equal-UV-distance renders as an oval
                    // (wide on this project's wide capture aspect). Scaling the horizontal
                    // delta by the screen aspect makes the glow round in screen pixels.
                    float2 sunDelta = sampleUv - sunUv;
                    sunDelta.x *= _ScreenParams.x / max(_ScreenParams.y, 1.0);
                    float sunDistance = length(sunDelta);
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
                return CameraSubmerged01(_WorldSpaceCameraPos.xyz, _PlanetCenter, _SeaLevelRadius, _SwellAmplitude);
            }

            // How far along viewDir a submerged camera's ray travels before it leaves the water.
            //
            // Measured against the water LEVEL FIELD rather than the sea sphere, so a lake perched above sea
            // level is measured to its own surface. A ray that is not headed up never leaves the water at
            // all; the large stand-in makes transmit zero downstream, so the composite collapses to the
            // column with no special case.
            float UnderwaterPathToSurface(float3 viewDir, out float cameraRadius, out float depthBelowSurface)
            {
                float3 fromCentre = _WorldSpaceCameraPos.xyz - _PlanetCenter;
                cameraRadius = length(fromCentre);
                float3 camUp = fromCentre / max(cameraRadius, 0.0001);
                depthBelowSurface = max(WaterSurfaceRadiusAt(camUp, _SeaLevelRadius) - cameraRadius, 0.0);
                float cosViewUp = dot(viewDir, camUp);
                return cosViewUp > 0.02 ? depthBelowSurface / cosViewUp : 4000.0;
            }

            // Does this ray reach open air before it reaches anything solid?
            //
            // Sky always does. So does above-water GEOMETRY - a far shore, the trees standing on it, a cloud
            // behind them - and that is the case this used to miss. The interface branch gated on
            // SkyDepthMask, so any pixel with depth skipped Snell's window, Fresnel and total internal
            // reflection entirely, and the atmosphere then handed back the raw scene colour. Above-water
            // geometry therefore reached a submerged eye carrying its full above-water lighting, unabsorbed:
            // a sunlit tree on the shore was the brightest thing in an underwater frame, and it stayed
            // sharp at angles far outside the 48.75 degree cone where it should have been replaced by the
            // mirror. Anything nearer than the surface - the seabed, the column itself - still answers
            // false and keeps the volume pass's own attenuation.
            bool ViewLeavesWater(float2 uv, float3 viewDir, float viewLength,
                out float waterPath, out float cameraRadius, out float depthBelowSurface)
            {
                waterPath = UnderwaterPathToSurface(viewDir, cameraRadius, depthBelowSurface);

                if (SkyDepthMask(uv) > 0.5)
                    return true;

                float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
                float geometryDistance = LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;

                // Whether the RECEIVER stands in air, not whether it is further away than an estimated exit
                // point. Comparing distances fails at grazing angles: waterPath is a flat-plane estimate that
                // runs away to its stand-in near the horizon, so a shore that is genuinely dry tested as
                // nearer than the exit and fell through to its raw colour - which left the waterline band
                // lit while the clouds far behind it were correctly replaced. This is the same question, and
                // the same authority, WaterVolume asks of its own receivers.
                float3 receiverWS = _WorldSpaceCameraPos.xyz + viewDir * geometryDistance;
                float3 receiverFromCentre = receiverWS - _PlanetCenter;
                float receiverRadius = length(receiverFromCentre);
                bool receiverInAir = receiverRadius > WaterSurfaceRadiusAt(
                    receiverFromCentre / max(receiverRadius, 0.0001), _SeaLevelRadius);

                // Never absorb over more water than lies between the eye and the thing being looked at. A
                // grazing ray to a shore a kilometre off still crosses a kilometre of water, so it arrives
                // as column colour - which is correct, and is why the far waterline goes flat rather than
                // staying sharp.
                waterPath = min(waterPath, geometryDistance);
                return receiverInAir;
            }

            // The sun's direction seen from UNDER the surface. Refraction bends it toward vertical, so from
            // below the sun sits higher than it does from the beach, and at grazing incidence it is pulled
            // inside the 48.75 degree cone rather than staying near the horizon. Both the column's forward
            // scattering and the shafts have to use this; the direction in the sky is wrong for both.
            float3 RefractedSunDirection(float3 cameraUp, float3 sunDir, out float cosSunWater)
            {
                float cosSunAir = dot(cameraUp, sunDir);
                float sinSunWater = sqrt(saturate(1.0 - cosSunAir * cosSunAir)) / WATER_IOR;
                cosSunWater = sqrt(saturate(1.0 - sinSunWater * sinSunWater));
                float3 sunTangent = sunDir - cameraUp * cosSunAir;
                return SafeNormalize(
                    SafeNormalize(sunTangent, cameraUp) * sinSunWater + cameraUp * cosSunWater, cameraUp);
            }

            float3 UnderwaterSkyColor(float3 viewDir)
            {
                float3 cameraUp = normalize(_WorldSpaceCameraPos.xyz - _PlanetCenter);
                float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : cameraUp;
                float3 moonDir = dot(_MoonParams, _MoonParams) > 0.0001 ? normalize(_MoonParams) : cameraUp;
                float cameraDepth = max(-CameraSeaOffset(_WorldSpaceCameraPos.xyz, _PlanetCenter, _SeaLevelRadius), 0.0);
                return UnderwaterAmbientColor(viewDir, cameraUp, cameraDepth,
                    sunDir, _SunIntensity, moonDir, _MoonIntensity, _NightAmbientIntensity);
            }

            // Project broad surface illumination into the column along the refracted sun direction.
            // World-space entry points keep beams stationary when the camera moves.
            float3 UnderwaterSunShafts(float3 viewDir, float rayLength, float surfaceRadius,
                float depth01, float body01, float freeze01, float dither)
            {
                float3 cameraUp = normalize(_WorldSpaceCameraPos.xyz - _PlanetCenter);
                float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : cameraUp;
                float daylight = smoothstep(-0.02, 0.22, dot(cameraUp, sunDir))
                    * saturate(_SunIntensity / 17.0) * (1.0 - saturate(freeze01));
                if (daylight <= 0.001 || _UnderwaterShaftIntensity <= 0.0 || rayLength <= 0.0)
                    return float3(0.0, 0.0, 0.0);

                float cosSunWater;
                float3 sunUnderwater = RefractedSunDirection(cameraUp, sunDir, cosSunWater);
                const float3 SHAFT_SCATTER = float3(0.0055, 0.0105, 0.0112);
                int SHAFT_STEPS = _WaterQuality.y > 0.0 ? (int)_WaterQuality.y : 24;
                float marchLength = min(rayLength, 90.0);
                float stepLength = marchLength / SHAFT_STEPS;
                float3 extinction = UnderwaterExtinction();
                float3 stepIntegral = (1.0 - exp(-extinction * stepLength)) / extinction;

                float3 waveAxisA, waveAxisB;
                BuildPlanetWaveAxes(waveAxisA, waveAxisB);
                WaterRippleParams shaftParams = EvaluateRippleParameters(depth01, body01, cameraUp);
                float shaftWidth = max(_UnderwaterShaftWidth, 1.0);
                float3 drift = waveAxisA * (_GameTime * shaftParams.timeScale * 0.20);
                float3 accumulated = float3(0.0, 0.0, 0.0);
                [loop]
                for (int step = 0; step < SHAFT_STEPS; step++)
                {
                    float travelled = (step + dither) * stepLength;
                    float3 samplePos = _WorldSpaceCameraPos.xyz + viewDir * travelled;
                    float sampleDepth = surfaceRadius - length(samplePos - _PlanetCenter);
                    if (sampleDepth <= 0.0)
                        continue;

                    float sunPath = sampleDepth / max(cosSunWater, 0.15);
                    float3 entryLocal = samplePos + sunUnderwater * sunPath - _PlanetCenter;
                    // Unresolved wave focusing uses a smooth envelope instead of metre-scale ripple slopes.
                    // Broad bands remain visible after integration and still vary under an overhead sun.
                    float pattern = ValueNoise3D((entryLocal + drift) / shaftWidth);
                    float beam = lerp(0.18, 2.4, smoothstep(0.28, 0.74, pattern));
                    beam = lerp(1.0, beam, shaftParams.waveEnergy);
                    float3 transmit = UnderwaterTransmittance(sunPath + step * stepLength);
                    accumulated += transmit * beam * stepIntegral;
                }

                float forward = saturate(dot(viewDir, sunUnderwater));
                float phase = 0.25 + pow(forward, 3.0) * 1.75;
                return accumulated * phase * daylight * SHAFT_SCATTER * _UnderwaterShaftIntensity;
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
                if (_OceanDebugMode == 0 && IsWaterPresentationCamera(_WorldSpaceCameraPos.xyz) && _WaterCameraSurface.z > 0.001)
                {
                    float2 wobble = float2(sin(i.uv.y * 48.0 + _GameTime * 7.0), sin(i.uv.x * 37.0 - _GameTime * 5.0));
                    i.uv = clamp(i.uv + wobble * _WaterCameraSurface.z * 0.002, 0.001, 0.999);
                    #if UNITY_UV_STARTS_AT_TOP
                        float2 ndc = i.uv * 2.0 - 1.0;
                    #else
                        float2 ndc = float2(i.uv.x * 2.0 - 1.0, 1.0 - i.uv.y * 2.0);
                    #endif
                    i.viewVector = mul(unity_CameraToWorld, float4(mul(unity_CameraInvProjection, float4(ndc, 0, -1)).xyz, 0)).xyz;
                }
                float4 originalCol = SAMPLE_TEXTURE2D(_Source, sampler_Source, i.uv);

                // DEBUG (global _SceneDepthDebug): visualize _CameraDepthTexture — the exact depth the
                // atmosphere + clouds read. RED = classified as sky (no geometry written here); GRAY =
                // geometry by distance. If tree canopies show RED, the scatter is missing from this depth
                // and the atmosphere/clouds composite over it. Off by default.
                if (_SceneDepthDebug > 0.5)
                {
                    float rd = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, i.uv).r;
                    #if UNITY_REVERSED_Z
                        float isSky = 1.0 - step(0.0001, rd);
                    #else
                        float isSky = step(0.9999, rd);
                    #endif
                    float g = saturate(LinearEyeDepth(rd, _ZBufferParams) / max(_SceneDepthDebugRange, 1.0));
                    return isSky > 0.5 ? float4(1, 0, 0, 1) : float4(g, g, g, 1);
                }

                if (ShouldBypassAtmosphereForWaterDebug())
                    return originalCol;

                // Two-tone view of the mask the atmosphere uses to decide a pixel is water. It gates
                // CompositeDepthScaled, which swaps the depth aerial perspective is computed from between
                // the scene and the WATER surface - so a hard boundary here becomes a hard-edged region of
                // different haze sitting on the sea.
                if (_OceanDebugMode == DEBUG_SHAPE_IS_WATER_MASK)
                {
                    // BANDED, not thresholded. The first version of this split at 0.5 and reported the mask
                    // "clean" - but a mask running 0.6 to 0.95 is uniformly above that split while still
                    // moving the lerp in CompositeDepthScaled a long way. Same mistake the channel views
                    // made. Bands show any variation at all.
                    float m = WaterInterfaceFrontMask(i.uv);
                    return float4(frac(m * 16.0), 0.0, 0.0, 1.0);
                }

                float viewLength = length(i.viewVector);
                float3 viewDir = i.viewVector / max(viewLength, 0.0001);

                // The distance aerial perspective is actually computed from, as a red ramp. A hard edge
                // here is a hard edge in the haze, which is what a stepped region on the sea looks like.
                if (_OceanDebugMode == DEBUG_SHAPE_IS_COMPOSITE_DEPTH)
                    return float4(saturate(CompositeDepthScaled(i.uv, viewLength) / 3000.0), 0.0, 0.0, 1.0);

                // The water forward depth the volume prepass wrote, banded so any discontinuity shows. This
                // is the value CompositeDepthScaled substitutes for scene depth, and it is NOT the surface's
                // vertex channels - those measured smooth. If the shape is anywhere, it should be here.
                if (_OceanDebugMode == DEBUG_SHAPE_IS_PREPASS_DEPTH)
                {
                    float coverage;
                    float4 waterData = SampleWaterInterface(i.uv, coverage);
                    // Same scale as ShapeIsCompositeDepth so the two are directly comparable. The first
                    // version used frac(d/40) on a distance of order a kilometre, which cycles about
                    // twenty-five times and aliases into noise at grazing - it hid the very structure it
                    // was meant to find.
                    return float4(saturate(waterData.r * viewLength / 3000.0), 0.0, 0.0, 1.0);
                }

                // Which branch of min(sceneDepth, waterDepth) actually wins, since the mask is 1 over the
                // water and CompositeDepthScaled reduces to that min. Both inputs measured smooth on their
                // own, so if the shape lives anywhere it is in WHERE the winner changes. Red = the water
                // surface is nearer, blue = the seabed is.
                if (_OceanDebugMode == DEBUG_SHAPE_IS_DEPTH_SOURCE)
                {
                    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, i.uv).r;
                    float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;
                    float coverage;
                    float4 waterData = SampleWaterInterface(i.uv, coverage);
                    float waterDepth = waterData.r * viewLength;
                    bool waterWins = waterData.r > 0.0001 && waterDepth < sceneDepth;
                    return float4(waterWins ? float3(1.0, 0.0, 0.0) : float3(0.0, 0.15, 0.9), 1.0);
                }

                // Only once the camera is MORE IN than out. This used to fire at 0.01, which with a 5 m
                // swell meant a camera a metre below the mean surface - visibly in open air between waves -
                // took the full underwater treatment and had Snell's window painted across its sky as a
                // hard disc. The branch returns early, so a partial weight cannot soften it; the gate itself
                // has to be the decision.
                float uwPathToSurface = 0.0;
                float uwCameraRadius = 0.0;
                float uwDepthBelowSurface = 0.0;
                bool uwLeavesWater = false;
                if (_WaterVolumeEnabled > 0.5 && CameraUnderwater01() > 0.5)
                {
                    uwLeavesWater = ViewLeavesWater(i.uv, viewDir, viewLength,
                        uwPathToSurface, uwCameraRadius, uwDepthBelowSurface);
                }

                if (uwLeavesWater)
                {
                    // ONE composite of the three things that physically reach the eye, not a stack of
                    // overrides. Every earlier version of this block computed a term and then let the next
                    // term paint over it, so each fix here broke the one before it.
                    //
                    //   through the surface   Snell's window - the sky, refracted
                    //   off the surface       total internal reflection outside that window
                    //   the water column      in-scattered light between the eye and the surface
                    //
                    // Fresnel splits the first two and the column's own opacity weighs them against the
                    // third, so nothing needs masking out of anything else.
                    float3 camUp = normalize(_WorldSpaceCameraPos.xyz - _PlanetCenter);
                    float cosViewUp = dot(viewDir, camUp);

                    // The column looking out along the ray. The mirror term is further down - it reflects
                    // about the wave-tilted surface, which is not known yet.
                    float3 columnColor = UnderwaterSkyColor(viewDir);

                    // Distance to the surface along THIS ray, against the water LEVEL FIELD rather than the
                    // sea sphere, so a lake perched above sea level is measured to its own surface.
                    //
                    // NOT from the prepass depth channel. Underwater that pass deliberately clips nothing,
                    // draws Cull Off and writes no depth, so in patches it records the distance to the ocean
                    // PAST THE HORIZON instead of the surface overhead - the same defect 1fbcedf fixed for
                    // the view from above. Reading it here split the upward view into two flat colours along
                    // a triangle edge.
                    //
                    // A ray that is not headed up never leaves the water, and the large stand-in makes
                    // transmit zero, so the composite collapses to the column with no special case.
                    float cameraRadius = uwCameraRadius;
                    float depthBelowSurface = uwDepthBelowSurface;
                    float pathToSurface = uwPathToSurface;

                    // THE SURFACE'S OWN NORMAL where this ray leaves the water, not the planet normal.
                    //
                    // Everything below refracts and reflects about this, so the window's rim heaves with the
                    // swell instead of sitting as a fixed circle about straight up. That heave is most of
                    // what reads as "being underwater"; without it the disc is geometrically right and dead.
                    //
                    // The same include the surface mesh and the volume prepass displace with, so the rim
                    // tracks the waves the player can see rather than a second wave field.
                    //
                    // Gating comes from the prepass channels. They are the far ocean's in the same patches
                    // the depth channel is wrong in, but openWater/deepWater/shoreFade are near-identical
                    // between near and far open water, so the error does not show the way a metric distance
                    // does. It would show inside a small lake seen across a shoreline; nothing does that yet.
                    float exitCoverage;
                    float4 exitData = SampleWaterInterface(i.uv, exitCoverage);
                    float exitShore01;
                    uint exitKind;
                    DecodeWaterShoreKind(exitData.b, exitShore01, exitKind);
                    float exitBody01 = WaterKindIsOcean(exitKind) ? 1.0 : 0.0;

                    float3 exitPointWS = _WorldSpaceCameraPos.xyz + viewDir * min(pathToSurface, 200.0);
                    float3 exitPlanetNormal = SafeNormalize(exitPointWS - _PlanetCenter, camUp);
                    float swellHeight;
                    float3 swellNormal;
                    ComputeOceanSwell(exitPointWS, exitPlanetNormal, exitData.g, exitShore01, exitBody01,
                        swellHeight, swellNormal);

                    // Fine chop on top of the swell. The swell alone gives a rim that heaves but is
                    // otherwise smooth; the short waves are what make a real Snell's window's edge crawl.
                    //
                    // Geometric slope only. Ocean.shader multiplies this gradient by _WaveNormalStrength to
                    // exaggerate its shading, and carrying that here would refract light through a surface
                    // steeper than the one the mesh and the depth buffer actually agree on.
                    float3 waveAxisA, waveAxisB;
                    BuildPlanetWaveAxes(waveAxisA, waveAxisB);
                    float3 exitLocal = exitPointWS - _PlanetCenter;
                    float2 exitTS = float2(dot(exitLocal, waveAxisA), dot(exitLocal, waveAxisB));
                    WaterRippleParams exitParams = EvaluateRippleParameters(exitData.g, exitBody01, exitPlanetNormal);
                    WaterRippleField exitRipple = ComputeWaterRipple(exitTS, float2(1.0, 0.0), float2(0.0, 1.0),
                        exitParams.scale, exitParams.amplitude, exitParams.timeScale,
                        exitParams.waveEnergy, exitParams.weatherEnergy, exitParams.chaos01);

                    float2 chopGradientTS = exitRipple.gradientTS * 0.18 + exitRipple.detailGradientTS * 1.35;
                    float3 microSlope = WaterMicroSlope(exitLocal, exitPlanetNormal, exitRipple.detailScale,
                        exitParams.timeScale, pathToSurface);
                    swellNormal = SafeNormalize(swellNormal + microSlope * saturate(_UnderwaterSurfaceDetail), swellNormal);
                    float3 choppyNormal = SafeNormalize(
                        swellNormal - WaterGradientWS(chopGradientTS, waveAxisA, waveAxisB, exitPlanetNormal),
                        swellNormal);

                    // Ice locks the surface flat, exactly as ComputeWaterVertexDisplacement does for the mesh.
                    float3 surfaceNormal = SafeNormalize(
                        lerp(exitPlanetNormal, choppyNormal, 1.0 - saturate(exitData.a)), exitPlanetNormal);

                    float cosWater = dot(viewDir, surfaceNormal);

                    // What total internal reflection shows: the water below, mirrored in the wave-tilted
                    // underside. Reflecting about the planet normal instead made every direction outside the
                    // window map to the same near-horizontal ray and come back one flat colour.
                    //
                    // ponytail: the column, not the seabed. A real underside also mirrors the bottom where
                    // it is close enough to see, which needs the underwater scene sampled about the
                    // reflected ray - and that ray points down and behind, so it is mostly off screen and
                    // screen-space reflection will not supply it.
                    float3 mirrorColor = UnderwaterSkyColor(reflect(viewDir, surfaceNormal));

                    // Unpolarised Fresnel for water -> air. Past the critical angle sinAir exceeds 1, there
                    // is no transmitted ray at all, and this stays exactly 1 - total internal reflection.
                    // The window's rim and its outer edge come out of that instead of a placed smoothstep,
                    // which is why the window used to read as a soft glow rather than a defined disc.
                    float sinWater = sqrt(saturate(1.0 - cosWater * cosWater));
                    float sinAir = WATER_IOR * sinWater;
                    float fresnel = 1.0;
                    float3 refracted = surfaceNormal;
                    if (sinAir < 1.0 && cosWater > 0.0)
                    {
                        float cosAir = sqrt(saturate(1.0 - sinAir * sinAir));
                        float rs = (WATER_IOR * cosWater - cosAir) / (WATER_IOR * cosWater + cosAir);
                        float rp = (WATER_IOR * cosAir - cosWater) / (WATER_IOR * cosAir + cosWater);
                        fresnel = saturate(0.5 * (rs * rs + rp * rp));
                        float3 tangent = viewDir - surfaceNormal * cosWater;
                        float tangentLength = length(tangent);
                        refracted = tangentLength > 1e-5
                            ? normalize(tangent / tangentLength * sinAir + surfaceNormal * cosAir)
                            : surfaceNormal;
                    }

                    // Between waves the camera genuinely is part in and part out, so the window eases in
                    // with submersion as well as with angle. The gate above is binary and cannot do this.
                    float window = (1.0 - fresnel) * saturate((CameraUnderwater01() - 0.5) / 0.35);

                    float3 skyColor = float3(0.0, 0.0, 0.0);
                    if (window > 0.001 && SkyDepthMask(i.uv) > 0.5)
                    {
                        // Scatter from where the ray LEAVES the water, not from the camera: the atmosphere
                        // integrates outward from the planet surface, so a start point below sea level has
                        // no atmosphere in front of it to integrate.
                        //
                        // Lifted clear of _SeaLevelRadius first. CalculateScattering treats that radius as
                        // the planet and hands back the background for any ray starting inside it, and the
                        // exit point sits ON the water - exactly that radius for the ocean, below it for a
                        // lake in a basin. Left as it was, the whole window returned black except near the
                        // frame edges, where the longer rays happened to clear the sphere.
                        //
                        // Black background: beyond the atmosphere there is space, and passing the water
                        // colour in tinted the window with the very wash this branch exists to remove.
                        float exitRadius = length(exitPointWS - _PlanetCenter);
                        float3 scatterStart = _PlanetCenter + exitPlanetNormal
                                            * max(exitRadius, _SeaLevelRadius + _SwellAmplitude + 1.0);
                        skyColor = CalculateScattering(scatterStart, refracted,
                            _AtmosphereRadius * 4.0, float3(0.0, 0.0, 0.0));
                    }
                    else if (window > 0.001)
                    {
                        // Above-water geometry inside the window - the far shore, the trees on it, a cloud
                        // behind them. It is already lit, so what reaches the eye is that colour, weighed by
                        // the same Fresnel and absorbed over the same path as the sky beside it.
                        //
                        // ponytail: sampled straight down the UNREFRACTED ray. A true Snell's window bends
                        // the sight line at the surface, and for geometry that means resampling the scene
                        // along the refracted direction - which a post pass cannot do, because the camera
                        // never rasterised the scene there. Compression inside the disc is the part that is
                        // missing, so the shore sits slightly wrong within the window rather than being the
                        // wrong shore; outside the cone Fresnel reaches 1 and none of it survives anyway.
                        // Upgrade path is to refract the screen-space sample position toward the window
                        // centre and accept the edge stretch, the usual way this is faked.
                        skyColor = originalCol.xyz;
                    }

                    float3 interfaceColor = lerp(mirrorColor, skyColor, window);

                    // Beer-Lambert over the water actually between the eye and the surface, on the same
                    // coefficients the volume uses so the window agrees with the rest of the frame about
                    // how water absorbs. Not saturated: the old cap floored blue transmission at 0.56 no
                    // matter how deep the camera was, which is a large part of why the surface read as
                    // flooded from any depth.
                    float3 transmit = UnderwaterTransmittance(pathToSurface);
                    float3 result = interfaceColor * transmit + columnColor * (1.0 - transmit);

                    // Shafts live in the column between the eye and the surface, so they add on top of a
                    // composite that already accounts for that column's own glow.
                    float surfaceRadius = cameraRadius + depthBelowSurface;
                    float3 shafts = UnderwaterSunShafts(viewDir, pathToSurface, surfaceRadius,
                        exitData.g, exitBody01, exitData.a, LightShaftNoise(i.uv * _ScreenParams.xy));
                    result += shafts;

                    return float4(result, originalCol.w);
                }

                if (_OceanDebugMode == DEBUG_ATMOSPHERE_WATER_CUT && _WaterVolumeEnabled > 0.5)
                {
                    if (WaterInterfaceFrontMask(i.uv) > 0.01)
                        return originalCol;
                }

                float sceneDepth = CompositeDepthScaled(i.uv, viewLength);
                float3 color = CalculateScattering(_WorldSpaceCameraPos.xyz, viewDir,
                    sceneDepth, originalCol.xyz);
                float3 shaftColor = CalculateLightShafts(i.uv);
                color += shaftColor;

                // Under water the sight path is water, not air, so none of the above applies. The volume
                // pass runs first and has already attenuated these pixels by how much water is in the way;
                // laying air scattering over the top of that re-lit every one of them with the SKY's colour.
                // That is why the far shore and everything standing on it read bleached cream by day and
                // flat black at night, instead of fading into the water.
                //
                // Air light shafts go with it - they are shafts through atmosphere. The underwater shafts
                // that replace them are built against the water column and applied after air-detail preservation.
                float underwater01 = _WaterVolumeEnabled > 0.5 ? CameraUnderwater01() : 0.0;

                // Raw shaft signal only, amplified so a faint contribution is still visible.
                // Isolates whether CalculateLightShafts is producing anything at all, independent
                // of how it blends into the sky.
                if (_OceanDebugMode == DEBUG_ATMOSPHERE_LIGHT_SHAFTS)
                    return float4(shaftColor * 4.0, 1.0);

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

                // Fade the preserve out with distance, on the same curve the terrain already uses.
                //
                // Preserving a fixed slice of the water's own colour is right in front of the camera, where
                // the surface detail is the point. It is wrong at the limb: water twelve kilometres away was
                // still keeping forty per cent of itself while the sky beside it went to full scattering, so
                // the two never met in tone and the join stayed visible. The mesh silhouette there is one
                // pixel wide and unantialiased, so any difference across it reads as a hard jagged line -
                // the sharp edge at the horizon.
                //
                // Far water now converges to the same atmosphere as the sky it meets, which removes the
                // tonal step the aliasing was riding on. Near water is untouched.
                float waterClarityEnd = max(_TerrainAerialPerspectiveDistances.x, 0.0);
                float waterAtmosphereStart = max(_TerrainAerialPerspectiveDistances.y, waterClarityEnd + 1.0);
                float waterClarity = 1.0 - smoothstep(waterClarityEnd, waterAtmosphereStart, sceneDepth);

                float detailPreserve = waterSurfaceMask * detailPreserveFraction * waterClarity;
                float highlightPreserve = waterSurfaceMask * smoothstep(0.32, 0.82, sourceLuma) * 0.24 * waterClarity;
                color = lerp(color, originalCol.rgb, detailPreserve);
                color = lerp(color, max(color, originalCol.rgb), highlightPreserve);

                // Sun aureole: soft radial bloom around the sun that fills in the "fireball" halo
                // at low sun, where single-scatter Mie is killed by sun extinction and leaves a bare
                // disc. Extinction-independent and weighted by lowSun01 so it fades out by midday.
                float sunViewCos = dot(viewDir, sunDirNorm);
                // Confine the bloom to the true sunrise/sunset band (gone by ~6 deg) so mid-morning
                // and late-dusk don't inherit a sky-filling blob - steeper than the shared lowSun01.
                float aureoleElev = 1.0 - smoothstep(-0.05, 0.10, sunElevation);
                // SkyDepthMask misclassifies the planet as sky from orbit (its reversed-Z depth is
                // below the mask's threshold), so the aureole bled through the planet body. Gate it
                // with an explicit planet-sphere hit test: no bloom where the view ray hits the planet.
                float2 aurPlanetHit = RaySphere(_PlanetCenter, _SeaLevelRadius, _WorldSpaceCameraPos.xyz, viewDir);
                float aurSeesSky = 1.0 - step(0.0001, aurPlanetHit.y) * step(0.0, aurPlanetHit.x);
                float aureole = pow(saturate(sunViewCos), max(_SunAureoleParams.y, 1.0))
                    * _SunAureoleParams.x * aureoleElev * SkyDepthMask(i.uv) * aurSeesSky;
                color += float3(1.0, 0.55, 0.28) * aureole;

                float sceneMask = 1.0 - SkyDepthMask(i.uv);
                float nonWaterSceneMask = sceneMask * (1.0 - waterSurfaceMask);
                float terrainClarityEnd = max(_TerrainAerialPerspectiveDistances.x, 0.0);
                float terrainAtmosphereStart = max(
                    _TerrainAerialPerspectiveDistances.y, terrainClarityEnd + 1.0);

                // Preserve local terrain exactly, then return smoothly to the existing
                // scattering before the horizon. The scene masks keep this branch off sky
                // pixels and water, so their established atmosphere remains unchanged.
                float terrainClarity = 1.0 - smoothstep(
                    terrainClarityEnd, terrainAtmosphereStart, sceneDepth);
                color = lerp(color, originalCol.rgb, nonWaterSceneMask * terrainClarity);

                // Underwater scattering must survive the air pass's local-detail preservation.
                color = lerp(color, originalCol.xyz, underwater01);
                if (underwater01 > 0.001)
                {
                    float3 submergedFromCentre = _WorldSpaceCameraPos.xyz - _PlanetCenter;
                    float submergedRadius = length(submergedFromCentre);
                    float submergedSurfaceRadius = WaterSurfaceRadiusAt(
                        submergedFromCentre / max(submergedRadius, 0.0001), _SeaLevelRadius);

                    // The GEOMETRY's distance, not CompositeDepthScaled - that substitutes the water
                    // surface's distance where the surface covers a pixel, which underwater is the thing
                    // behind the camera's own head rather than the seabed the shaft ends on.
                    float rawSceneDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, i.uv).r;
                    float geometryDistance = LinearEyeDepth(rawSceneDepth, _ZBufferParams) * viewLength;

                    float shaftCoverage;
                    float4 shaftData = SampleWaterInterface(i.uv, shaftCoverage);
                    float shaftShore01;
                    uint shaftKind;
                    DecodeWaterShoreKind(shaftData.b, shaftShore01, shaftKind);

                    color += UnderwaterSunShafts(viewDir, geometryDistance, submergedSurfaceRadius,
                        shaftData.g, WaterKindIsOcean(shaftKind) ? 1.0 : 0.0, shaftData.a,
                        LightShaftNoise(i.uv * _ScreenParams.xy)) * underwater01;
                }
                if (_OceanDebugMode == DEBUG_ATMOSPHERE_CONTRIBUTION)
                    return float4(ContributionHeat(color - originalCol.xyz, 8.0, float3(0.18, 0.48, 1.0), float3(1.0, 0.95, 0.22)), 1.0);

                return float4(color, originalCol.w);
            }

            ENDHLSL
        }
    }
}
