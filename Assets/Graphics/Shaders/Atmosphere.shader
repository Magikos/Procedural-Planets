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
// Published by PlanetWaterSurface. See ShaderGlobalIds.WaterDeepColor for why it is not called _DeepColor.
float4 _WaterDeepColor;
// Published by CelestialManager; the shared night floor every lit surface uses. Tunable with `light.*`.
// The moon pair comes from the same publisher, on the scale WaterVolume lights its caustics with.
float _NightAmbientIntensity;
float3 _MoonParams;
float _MoonIntensity;

float LightShaftNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}


void AccumulateWaterInterface(float2 uv, float edgeWeight, inout float4 bestData, inout float bestCoverage)
{
    float4 candidate = SAMPLE_TEXTURE2D(_WaterInterfaceTexture, sampler_WaterInterfaceTexture, uv);
    float candidateCoverage = WaterVolumeCoverage(candidate) * edgeWeight;
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
    waterCoverage = WaterVolumeCoverage(waterData);
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
                float daylight = smoothstep(-0.08, 0.20, dot(cameraUp, sunDir));
                float viewUp = smoothstep(-0.35, 0.85, dot(viewDir, cameraUp));
                // The authored deep-water colour, the same one the volume settles to, rather than a second
                // copy of it. The copy that used to live here read (0.0, 0.020, 0.070) against an authored
                // (0.008, 0.058, 0.133) - about half the brightness - so a far shore faded to something
                // three to four times darker than the water around it instead of fading INTO it.
                float3 deepWater = _WaterDeepColor.rgb;
                float3 ambient = lerp(deepWater, float3(0.065, 0.300, 0.420), viewUp * 0.62 + daylight * 0.24);

                // Colour and LIGHT LEVEL, kept apart. The old form lerped toward a "lit" colour whose night
                // end was (0.012, 0.105, 0.165) - brighter than the authored deep colour in both green and
                // blue - so midnight underwater came out a mid-blue however dark the world above it was.
                // A night floor belongs in the light, not in the palette.
                //
                // Same floor Ocean.shader uses for its surface, so the water reads consistently from above
                // and below and `light.*` moves both at once, plus the moon on the same scale the volume
                // pass lights its caustics with. A moon below the horizon lights nothing.
                float3 moonDir = dot(_MoonParams, _MoonParams) > 0.0001 ? normalize(_MoonParams) : cameraUp;
                float moonlight = saturate(_MoonIntensity) * saturate(dot(cameraUp, moonDir));
                float nightLevel = saturate(_NightAmbientIntensity * 0.10 + 0.015 + moonlight);
                ambient *= lerp(nightLevel, 1.0, daylight);

                // Forward scattering toward the sun. Water scatters strongly forward, so looking toward the
                // sun underwater is markedly brighter than looking away from it at the same elevation.
                // Without this term the column is one colour per elevation in every direction, which is what
                // makes it read as a painted backdrop instead of a medium you are inside.
                //
                float cosSunWater;
                float3 sunUnderwater = RefractedSunDirection(cameraUp, sunDir, cosSunWater);

                // No sun term here. The directional part of the column - brighter toward the sun, dimmer
                // away from it, dimmer with depth - is the march in UnderwaterSunShafts, which integrates it
                // properly along the ray. A second hand-set copy of it here is the duplicated-override shape
                // this whole file was restructured to remove.
                return ambient;
            }

            // Sunlight in the column itself. Water scatters it sideways, so the beams between the surface
            // and the seabed are visible from outside them - the underwater half of the effect the seabed
            // caustics are the other half of.
            //
            // The bands come from the swell: where the surface tilts to face the sun, more light crosses it
            // and the beam under that patch is brighter, and where it tilts away the beam thins. That is the
            // same tilt the caustics focus with, so the two agree without sharing a pattern - which they
            // could not do anyway, since CausticPattern is 81 animated Voronoi cells per call and this would
            // need one per march step.
            //
            // Sampled where each step's SUN ray crosses the surface, not at the step itself. Anchoring the
            // bands to the step would slide them with the camera instead of leaving them standing in the
            // water under the waves that cast them.
            float3 UnderwaterSunShafts(float3 viewDir, float rayLength, float surfaceRadius,
                float depth01, float body01, float dither)
            {
                float3 cameraUp = normalize(_WorldSpaceCameraPos.xyz - _PlanetCenter);
                float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : cameraUp;
                float daylight = smoothstep(-0.02, 0.22, dot(cameraUp, sunDir));
                if (daylight <= 0.001)
                    return float3(0.0, 0.0, 0.0);

                float cosSunWater;
                float3 sunUnderwater = RefractedSunDirection(cameraUp, sunDir, cosSunWater);

                // How much of the sunlight crossing the column scatters back to the eye per metre. Set by
                // measurement, not by taste: the ambient term this replaced read (0.010, 0.038, 0.036) at
                // 8 m down looking 70 degrees off vertical toward the sun, and this reproduces it there
                // while now falling off with depth and path the way the ambient copy could not.
                // Bryan has not had his eye on the magnitude.
                const float3 SHAFT_SCATTER = float3(0.0055, 0.0105, 0.0112);

                // Beyond this the column has absorbed the shafts anyway, and marching further only spends
                // steps where nothing is left to see.
                const int SHAFT_STEPS = 10;
                float marchLength = min(rayLength, 90.0);
                float stepLength = marchLength / SHAFT_STEPS;

                // Constant over the march, so out of the loop.
                float3 waveAxisA, waveAxisB;
                BuildPlanetWaveAxes(waveAxisA, waveAxisB);
                WaterRippleParams shaftParams = EvaluateRippleParameters(depth01, body01);

                float3 accumulated = float3(0.0, 0.0, 0.0);
                for (int step = 0; step < SHAFT_STEPS; step++)
                {
                    float travelled = (step + dither) * stepLength;
                    float3 samplePos = _WorldSpaceCameraPos.xyz + viewDir * travelled;
                    float sampleDepth = surfaceRadius - length(samplePos - _PlanetCenter);
                    if (sampleDepth <= 0.0)
                        continue;

                    float sunPath = sampleDepth / max(cosSunWater, 0.15);
                    float3 entryPoint = samplePos + sunUnderwater * sunPath;
                    float3 entryFlat = SafeNormalize(entryPoint - _PlanetCenter, cameraUp);

                    // The SHORT waves, not the swell. Focusing goes as surface curvature and curvature as
                    // 1/wavelength squared, so the 90 m swell barely bands at all - measured mean |tiltGain|
                    // 0.03 - while the metre-scale ripples do. Same field Ocean.shader shades with, so a
                    // bright beam lands where the surface above it is actually tilted into the sun.
                    float3 entryLocal = entryPoint - _PlanetCenter;
                    float2 entryTS = float2(dot(entryLocal, waveAxisA), dot(entryLocal, waveAxisB));
                    WaterRippleField entryRipple = ComputeWaterRipple(entryTS, float2(1.0, 0.0), float2(0.0, 1.0),
                        shaftParams.scale, shaftParams.amplitude, shaftParams.timeScale,
                        shaftParams.waveEnergy, shaftParams.weatherEnergy, shaftParams.chaos01);
                    float2 entryGradient = entryRipple.gradientTS * 0.18 + entryRipple.detailGradientTS * 1.35;
                    float3 entryTangentA = SafeNormalize(waveAxisA - entryFlat * dot(waveAxisA, entryFlat), waveAxisA);
                    float3 entryTangentB = SafeNormalize(waveAxisB - entryFlat * dot(waveAxisB, entryFlat), waveAxisB);
                    float3 entryNormal = SafeNormalize(
                        entryFlat - entryTangentA * entryGradient.x - entryTangentB * entryGradient.y, entryFlat);

                    // How much more (or less) light crosses the surface here than across a flat one. First
                    // order in the tilt, and NOT clamped at 1 - the whole point is the bright half.
                    float tiltGain = dot(entryNormal, sunDir) - dot(entryFlat, sunDir);
                    float beam = max(1.0 + tiltGain * 6.0, 0.0);

                    float3 transmit = exp(-WATER_ABSORPTION
                        * ((sunPath + travelled) / WATER_ABSORPTION_UNIT_METRES));
                    accumulated += transmit * beam * stepLength;
                }

                // Forward-peaked, like the ambient term: a shaft is brightest seen nearly along itself and
                // all but invisible looking straight down one.
                float forward = saturate(dot(viewDir, sunUnderwater));
                float phase = 0.25 + pow(forward, 3.0) * 1.75;
                return accumulated * phase * daylight * SHAFT_SCATTER;
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
                    float4 waterData = SampleWaterInterfaceDilated(i.uv, coverage);
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
                    float4 waterData = SampleWaterInterfaceDilated(i.uv, coverage);
                    float waterDepth = waterData.r * viewLength;
                    bool waterWins = waterData.r > 0.0001 && waterDepth < sceneDepth;
                    return float4(waterWins ? float3(1.0, 0.0, 0.0) : float3(0.0, 0.15, 0.9), 1.0);
                }

                // Only once the camera is MORE IN than out. This used to fire at 0.01, which with a 5 m
                // swell meant a camera a metre below the mean surface - visibly in open air between waves -
                // took the full underwater treatment and had Snell's window painted across its sky as a
                // hard disc. The branch returns early, so a partial weight cannot soften it; the gate itself
                // has to be the decision.
                if (_WaterVolumeEnabled > 0.5 && CameraUnderwater01() > 0.5 && SkyDepthMask(i.uv) > 0.5)
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
                    float3 fromCentre = _WorldSpaceCameraPos.xyz - _PlanetCenter;
                    float cameraRadius = length(fromCentre);
                    float depthBelowSurface = max(
                        WaterSurfaceRadiusAt(fromCentre / max(cameraRadius, 0.0001), _SeaLevelRadius) - cameraRadius,
                        0.0);
                    float pathToSurface = cosViewUp > 0.02 ? depthBelowSurface / cosViewUp : 4000.0;

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
                    float4 exitData = SampleWaterInterfaceDilated(i.uv, exitCoverage);
                    float exitShore01;
                    uint exitKind;
                    DecodeWaterShoreKind(exitData.b, exitShore01, exitKind);
                    float exitBody01 = exitKind == WATER_KIND_OCEAN ? 1.0 : 0.0;

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
                    WaterRippleParams exitParams = EvaluateRippleParameters(exitData.g, exitBody01);
                    WaterRippleField exitRipple = ComputeWaterRipple(exitTS, float2(1.0, 0.0), float2(0.0, 1.0),
                        exitParams.scale, exitParams.amplitude, exitParams.timeScale,
                        exitParams.waveEnergy, exitParams.weatherEnergy, exitParams.chaos01);

                    float2 chopGradientTS = exitRipple.gradientTS * 0.18 + exitRipple.detailGradientTS * 1.35;
                    float3 chopTangentA = SafeNormalize(
                        waveAxisA - exitPlanetNormal * dot(waveAxisA, exitPlanetNormal), waveAxisA);
                    float3 chopTangentB = SafeNormalize(
                        waveAxisB - exitPlanetNormal * dot(waveAxisB, exitPlanetNormal), waveAxisB);
                    float3 choppyNormal = SafeNormalize(
                        swellNormal - chopTangentA * chopGradientTS.x - chopTangentB * chopGradientTS.y,
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
                    if (window > 0.001)
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

                    // ponytail: swell only - no fine ripple, no glint. The rim heaves with the waves but has
                    // no small-scale chop on it. The ripple normal lives in Ocean.shader's fragment stage and
                    // is not in the shared include; hoisting it there is the upgrade path.
                    float3 interfaceColor = lerp(mirrorColor, skyColor, window);

                    // Beer-Lambert over the water actually between the eye and the surface, on the same
                    // coefficients the volume uses so the window agrees with the rest of the frame about
                    // how water absorbs. Not saturated: the old cap floored blue transmission at 0.56 no
                    // matter how deep the camera was, which is a large part of why the surface read as
                    // flooded from any depth.
                    float3 transmit = exp(-WATER_ABSORPTION * (pathToSurface / WATER_ABSORPTION_UNIT_METRES));
                    float3 result = interfaceColor * transmit + columnColor * (1.0 - transmit);

                    // Shafts live in the column between the eye and the surface, so they add on top of a
                    // composite that already accounts for that column's own glow.
                    float surfaceRadius = cameraRadius + depthBelowSurface;
                    float3 shafts = UnderwaterSunShafts(viewDir, pathToSurface, surfaceRadius,
                        exitData.g, exitBody01, LightShaftNoise(i.uv * _ScreenParams.xy));
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
                // that replace them are built against the water column instead, and are added here rather
                // than blended, because they are light arriving on top of what the volume already drew.
                float underwater01 = _WaterVolumeEnabled > 0.5 ? CameraUnderwater01() : 0.0;
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
                    float4 shaftData = SampleWaterInterfaceDilated(i.uv, shaftCoverage);
                    float shaftShore01;
                    uint shaftKind;
                    DecodeWaterShoreKind(shaftData.b, shaftShore01, shaftKind);

                    color += UnderwaterSunShafts(viewDir, geometryDistance, submergedSurfaceRadius,
                        shaftData.g, shaftKind == WATER_KIND_OCEAN ? 1.0 : 0.0,
                        LightShaftNoise(i.uv * _ScreenParams.xy)) * underwater01;
                }

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

                if (_OceanDebugMode == DEBUG_ATMOSPHERE_CONTRIBUTION)
                    return float4(ContributionHeat(color - originalCol.xyz, 8.0, float3(0.18, 0.48, 1.0), float3(1.0, 0.95, 0.22)), 1.0);

                return float4(color, originalCol.w);
            }

            ENDHLSL
        }
    }
}
