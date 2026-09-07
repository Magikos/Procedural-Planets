Shader "Planet/Ocean"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.28, 0.78, 0.82, 0.12)
        _DeepColor ("Deep Color", Color) = (0.0, 0.035, 0.095, 0.98)
        _FoamColor ("Foam Color", Color) = (0.92, 0.98, 1.0, 0.95)
        _ShallowDepth ("Shallow Depth", Range(1, 500)) = 28
        _DeepDepth ("Deep Depth", Range(20, 3000)) = 360
        _ShoreFoamDepth ("Shore Foam Depth (m of water)", Range(0.2, 40)) = 0.65
        _ShoreFoamSoftness ("Shore Range", Range(1, 300)) = 125
        _WaveNormalStrength ("Wave Normal Strength", Range(0, 16)) = 4.5
        _WaterMotionStrength ("Water Motion Strength", Range(0, 1)) = 0.24
        _SunGlitterIntensity ("Sun Glitter Intensity", Range(0, 4)) = 0.75
        _SunGlitterPower ("Sun Glitter Power", Range(64, 4096)) = 1400
        _ShoreFoamIntensity ("Shore Foam Intensity", Range(0, 3)) = 1
        _WhitecapIntensity ("Whitecap Intensity", Range(0, 3)) = 1
        _OceanFocusMode ("Ocean Focus Mode", Range(0, 1)) = 1
        _Alpha ("Alpha", Range(0, 1)) = 0.9
        _IceTint ("Ice Tint", Color) = (0.62, 0.82, 0.88, 1)
        _IceOpacity ("Ice Opacity", Range(0, 1)) = 0.88
        _IceRoughness ("Ice Roughness", Range(0, 1)) = 0.72
        _IceNormalStrength ("Ice Normal Strength", Range(0, 2)) = 0.35
        _IceBreakupScale ("Ice Breakup Scale", Range(1, 1000)) = 95
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }

        // Layer 2: first visible surface pass.
        // WaterVolume owns underwater fog/refraction/caustics. This pass adds
        // only the top sheet color so the layer can be validated by itself.
        Pass
        {
            Name "OceanForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Includes/DebugModes.hlsl"
            #include "Includes/CloudShadows.hlsl"
            #include "Includes/WaterLevelField.hlsl"
            #include "Includes/WaterVolumeData.hlsl"
            // Global; never inside UnityPerMaterial, or a same-named material property shadows it.
            float _SeaLevelRadius;
            #include "Includes/WaterDisplacement.hlsl"
            #include "Includes/WaterDepth.hlsl"

            #define FORCE_WATER_LAYER_PROOF 0
            #define SHOW_SURFACE_IN_OFF 1

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 waterData : TEXCOORD2;
                float swellHeight : TEXCOORD3; // vertex-evaluated swell, surfaced for WaveSwell debug
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float _ShallowDepth;
                float _DeepDepth;
                float _ShoreFoamDepth;
                float _ShoreFoamSoftness;
                float _WaveNormalStrength;
                float _WaterMotionStrength;
                float _SunGlitterIntensity;
                float _SunGlitterPower;
                float _ShoreFoamIntensity;
                float _WhitecapIntensity;
                float _OceanFocusMode;
                float _Alpha;
                float4 _IceTint;
                float _IceOpacity;
                float _IceRoughness;
                float _IceNormalStrength;
                float _IceBreakupScale;
            CBUFFER_END


            float3 _SunParams;
            float _NightAmbientIntensity;
            int _OceanDebugMode;

            struct SurfaceLayer
            {
                float3 color;
                float alpha;
                float depthBlend;
                float shoreVisibility;
                float fresnel;
                float viewPath;
                float daylight;
                float shadow;
                float3 normalWS;
                float waveHeight;
                float waveSlope;
                float rippleSignal;
                float waveProof;
                float surfaceDetail;
                float glint;
                float waveEnergy;
                float storm;
                float dataEdge;
                float foam;
                float shoreFoam;
                float crestFoam;
                float3 nearColor;
                float3 farColor;
                float pathBlend;
                float waterTemperature;
                float freezeFactor;
                float iceContribution;
            };



            float Luminance3(float3 color)
            {
                return dot(color, float3(0.2126, 0.7152, 0.0722));
            }

            // False-color brightness ramp for the LumaHeat debug view:
            // black -> blue -> cyan -> green -> yellow -> red -> white as input rises.
            // Lets the user describe surface brightness with a consistent vocabulary
            // ("the dark-side ocean is green here") instead of guessing RGB values.
            float3 LumaHeatRamp(float value)
            {
                float t = saturate(value) * 6.0;
                float3 color = float3(0.0, 0.0, 0.0);
                color = lerp(color, float3(0.0, 0.0, 1.0), saturate(t - 0.0));
                color = lerp(color, float3(0.0, 1.0, 1.0), saturate(t - 1.0));
                color = lerp(color, float3(0.0, 1.0, 0.0), saturate(t - 2.0));
                color = lerp(color, float3(1.0, 1.0, 0.0), saturate(t - 3.0));
                color = lerp(color, float3(1.0, 0.0, 0.0), saturate(t - 4.0));
                color = lerp(color, float3(1.0, 1.0, 1.0), saturate(t - 5.0));
                return color;
            }

            void BuildSurfaceBasis(float3 normalWS, out float3 tangentA, out float3 tangentB)
            {
                float3 referenceAxis = abs(normalWS.y) < 0.92 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                tangentA = SafeNormalize(cross(referenceAxis, normalWS), float3(1.0, 0.0, 0.0));
                tangentB = SafeNormalize(cross(normalWS, tangentA), float3(0.0, 0.0, 1.0));
            }



            float SampleOceanStorm(float3 normalWS, float3 tangentA, float3 tangentB)
            {
                if (_CloudWeatherResolution <= 0)
                    return 0.0;

                float weatherTexel = 1.0 / max((float)_CloudWeatherResolution, 1.0);
                float offset = max(weatherTexel * 2.75, 0.010);
                float center = SampleCloudShadowWeather(normalWS).g;
                float north = SampleCloudShadowWeather(SafeNormalize(normalWS + tangentA * offset, normalWS)).g;
                float south = SampleCloudShadowWeather(SafeNormalize(normalWS - tangentA * offset, normalWS)).g;
                float east = SampleCloudShadowWeather(SafeNormalize(normalWS + tangentB * offset, normalWS)).g;
                float west = SampleCloudShadowWeather(SafeNormalize(normalWS - tangentB * offset, normalWS)).g;
                float diagonalA = SampleCloudShadowWeather(SafeNormalize(normalWS + (tangentA + tangentB) * (offset * 0.72), normalWS)).g;
                float diagonalB = SampleCloudShadowWeather(SafeNormalize(normalWS - (tangentA + tangentB) * (offset * 0.72), normalWS)).g;
                float storm = center * 0.34 + (north + south + east + west) * 0.12 + (diagonalA + diagonalB) * 0.09;
                return smoothstep(0.08, 0.92, saturate(storm));
            }


            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 345.45));
                value += dot(value, value + 34.345);
                return frac(value.x * value.y);
            }

            float2 Hash22(float2 value)
            {
                return frac(sin(float2(
                    dot(value, float2(127.1, 311.7)),
                    dot(value, float2(269.5, 183.3)))) * 43758.5453);
            }

            float ValueNoise(float2 value)
            {
                float2 cell = floor(value);
                float2 local = frac(value);
                float2 blend = local * local * (3.0 - 2.0 * local);

                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, blend.x), lerp(c, d, blend.x), blend.y);
            }

            float FoamThresholdMask(float2 value)
            {
                float coarse = ValueNoise(value * 0.19);
                float fine = ValueNoise(value * 0.73 + coarse * 3.1);
                return saturate(lerp(fine, coarse, 0.42));
            }


            float IceNoise(float3 localPosition, float3 normalWS, float scale)
            {
                float3 uv = localPosition / scale;
                float3 weights = normalWS * normalWS;
                return dot(weights, float3(ValueNoise(uv.yz), ValueNoise(uv.zx), ValueNoise(uv.xy)));
            }

            float EvaluateIceContribution(float3 positionWS, float3 normalWS, float freezeFactor)
            {
                float3 localPosition = positionWS - _PlanetCenter;
                float scale = max(_IceBreakupScale, 1.0);
                float breakup = IceNoise(localPosition, normalWS, scale) * 0.68
                    + IceNoise(localPosition * 2.37 + 9.13 * scale, normalWS, scale) * 0.32;
                float transitionWeight = 1.0 - abs(freezeFactor * 2.0 - 1.0);
                float irregularFreeze = saturate(freezeFactor + (breakup - 0.5) * 0.30 * transitionWeight);
                return smoothstep(0.06, 0.94, irregularFreeze);
            }

            float3 ComputeIceNormal(float3 positionWS, float3 normalWS)
            {
                float3 tangentA;
                float3 tangentB;
                BuildSurfaceBasis(normalWS, tangentA, tangentB);
                float3 localPosition = positionWS - _PlanetCenter;
                float scale = max(_IceBreakupScale * 0.32, 1.0);
                float center = IceNoise(localPosition, normalWS, scale);
                float gradientX = IceNoise(localPosition + tangentA * (0.08 * scale), normalWS, scale) - center;
                float gradientY = IceNoise(localPosition + tangentB * (0.08 * scale), normalWS, scale) - center;
                float normalStrength = _IceNormalStrength * 2.4;
                return SafeNormalize(
                    normalWS - tangentA * gradientX * normalStrength - tangentB * gradientY * normalStrength,
                    normalWS);
            }

            float SurfaceVoronoi(float2 uv, float time)
            {
                float2 cell = floor(uv);
                float2 local = frac(uv);
                float nearest = 8.0;
                float secondNearest = 8.0;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 offset = float2(x, y);
                        float2 hash = Hash22(cell + offset);
                        float phase = dot(cell + offset, float2(0.37, 0.51));
                        float2 animated = 0.5 + 0.38 * sin(hash * 6.28318530718 + time * float2(0.73, 1.09) + phase);
                        float2 delta = offset + animated - local;
                        float distSq = dot(delta, delta);

                        if (distSq < nearest)
                        {
                            secondNearest = nearest;
                            nearest = distSq;
                        }
                        else if (distSq < secondNearest)
                        {
                            secondNearest = distSq;
                        }
                    }
                }

                float edgeDistance = sqrt(secondNearest) - sqrt(nearest);
                float core = 1.0 - smoothstep(0.030, 0.115, edgeDistance);
                float halo = 1.0 - smoothstep(0.09, 0.30, edgeDistance);
                return saturate(core * 0.40 + halo * 0.20);
            }

            float SurfaceCellPatternUv(float2 uv, float time, float chaos01)
            {
                float warpStrength = lerp(0.10, 0.24, chaos01);
                float2 waveWarp = float2(
                    sin(dot(uv, float2(0.73, 1.27)) + time * 0.38),
                    sin(dot(uv, float2(-1.11, 0.91)) - time * 0.34)) * warpStrength;
                float2 warpedUv = uv + waveWarp;
                float baseLayer = SurfaceVoronoi(warpedUv + float2(time * 0.12, -time * 0.07), time);
                float detailLayer = SurfaceVoronoi(warpedUv * 1.58 + float2(-time * 0.09, time * 0.13), time * 1.23);
                float pattern = saturate(baseLayer * 0.84 + detailLayer * 0.34 + baseLayer * detailLayer * 0.16);
                return smoothstep(0.10, 0.54, pattern);
            }

            float DetailResolve(float2 samplePos, float shortestWavelength)
            {
                // Procedural waves have no mip chain. Suppress frequencies above the pixel's sampling limit.
                float footprint = max(max(length(ddx(samplePos)), length(ddy(samplePos))), 1e-5);
                return smoothstep(2.0, 4.0, shortestWavelength / footprint);
            }

            float SurfaceCellPattern(float3 positionWS, float3 normalWS, float scale, float time, float chaos01)
            {
                float cellScale = max(scale * lerp(0.18, 0.11, chaos01), 1.0);
                float3 local = (positionWS - _PlanetCenter) / cellScale;
                float3 weights = pow(abs(normalWS), 4.0);
                weights /= max(weights.x + weights.y + weights.z, 0.0001);

                float x = SurfaceCellPatternUv(local.yz + float2(11.37, -4.91), time, chaos01);
                float y = SurfaceCellPatternUv(local.zx + float2(-6.53, 8.24), time * 1.03, chaos01);
                float z = SurfaceCellPatternUv(local.xy + float2(3.19, 13.72), time * 0.97, chaos01);
                return saturate(x * weights.x + y * weights.y + z * weights.z);
            }

            void ComputeSurfaceWaves(
                float3 positionWS,
                float3 normalWS,
                float depth01,
                float shore01,
                float shoreGradient,
                float body01,
                float swellHeight,
                out float3 rippleNormalWS,
                out float signedWaveHeight,
                out float waveSlope,
                out float rippleSignal,
                out float waveProof,
                out float waveEnergy,
                out float storm01,
                out float foamAmount,
                out float shoreFoam,
                out float crestFoam)
            {
                float3 tangentA;
                float3 tangentB;
                BuildSurfaceBasis(normalWS, tangentA, tangentB);

                float3 localPosition = positionWS - _PlanetCenter;
                float3 waveAxisA;
                float3 waveAxisB;
                BuildPlanetWaveAxes(waveAxisA, waveAxisB);

                float2 positionTS = float2(dot(localPosition, waveAxisA), dot(localPosition, waveAxisB));
                tangentA = SafeNormalize(waveAxisA - normalWS * dot(waveAxisA, normalWS), tangentA);
                tangentB = SafeNormalize(waveAxisB - normalWS * dot(waveAxisB, normalWS), tangentB);
                float2 windTS = float2(1.0, 0.0);
                float2 crossTS = float2(0.0, 1.0);
                float wind01 = WaterWeatherEnergy(normalWS);
                storm01 = SampleOceanStorm(normalWS, tangentA, tangentB);
                WaterRippleParams waveParams = EvaluateRippleParameters(depth01, body01, normalWS);
                float openWater01 = waveParams.openWater01;
                float deepWater01 = waveParams.deepWater01;
                float weatherEnergy = waveParams.weatherEnergy;
                waveEnergy = waveParams.waveEnergy;
                float chaos01 = waveParams.chaos01;
                float scale = waveParams.scale;
                float amplitude = waveParams.amplitude;
                float timeScale = waveParams.timeScale;

                // The wave field itself lives in WaterDisplacement.hlsl, beside the vertex swell, so that
                // anything which has to agree with this surface evaluates the same waves rather than a copy.
                WaterRippleField ripple = ComputeWaterRipple(positionTS, windTS, crossTS,
                    scale, amplitude, timeScale, waveEnergy, weatherEnergy, chaos01);

                float waveTime = ripple.waveTime;
                float2 wavePos = ripple.wavePos;
                float2 detailPos = ripple.detailPos;
                float2 detailPosCross = ripple.detailPosCross;
                float detailScale = ripple.detailScale;
                float height = ripple.height;
                float detailHeight = ripple.detailHeight;
                float2 gradientTS = ripple.gradientTS;
                float2 detailGradientTS = ripple.detailGradientTS;

                float detailResolve = DetailResolve(detailPos, detailScale * 0.42);
                float2 surfaceGradientTS = gradientTS * 0.18 + detailGradientTS * (1.35 * detailResolve);
                height += detailHeight * 0.42;
                float breakupNoise = ValueNoise(detailPos / max(detailScale * 5.6, 1.0) + float2(waveTime * 0.071, -waveTime * 0.043));
                float breakupNoiseB = ValueNoise((detailPos + crossTS * scale * 0.37) / max(detailScale * 3.4, 1.0) + float2(-waveTime * 0.052, waveTime * 0.064));
                float2 breakupVector = float2(breakupNoise - 0.5, breakupNoiseB - 0.5);
                float breakupStrength = lerp(0.045, 0.18, saturate(chaos01 + openWater01 * 0.35));
                surfaceGradientTS = surfaceGradientTS * lerp(0.72, 1.30, breakupNoise) + breakupVector * breakupStrength;
                float cellPattern = SurfaceCellPattern(positionWS, normalWS, scale, waveTime, chaos01);
                // Keep the centre-to-edge ratio near 1; a wider spread stamps the voronoi grid into the
                // wave normals as a visible honeycomb.
                surfaceGradientTS *= lerp(0.92, 1.08, cellPattern);

                // Fragment detail must reach as far inshore as the vertex swell, or crest foam stops at
                // the shallows while the geometry underneath is still moving. Reuse the swell gating
                // rather than restating its thresholds. (openWater01 above is a separate, deliberately
                // narrower gate for the detail layer, so it is not replaced here.)
                float swellOpen01, depthMask, shoreMask;
                EvaluateSwellGating(depth01, shore01, body01, swellOpen01, depthMask, shoreMask);
                float surfaceMask = depthMask * shoreMask * waveEnergy;
                float normalStrength = _WaveNormalStrength * 0.78 * lerp(0.42, 1.28, weatherEnergy) * surfaceMask;
                float2 normalGradient = surfaceGradientTS * normalStrength;

                rippleNormalWS = SafeNormalize(normalWS
                    - WaterGradientWS(normalGradient, waveAxisA, waveAxisB, normalWS), normalWS);
                signedWaveHeight = clamp(height / max(amplitude * 0.62, 0.001), -1.0, 1.0) * surfaceMask;
                waveSlope = saturate(length(normalGradient) + cellPattern * surfaceMask * 0.015);
                rippleSignal = lerp(0.5, saturate(max(0.5 + detailHeight / max(amplitude * 0.12, 0.001), cellPattern)), surfaceMask);

                float2 proofDirectionA = SafeNormalize2(windTS * 0.64 + crossTS * 0.76, windTS);
                float2 proofDirectionB = SafeNormalize2(windTS * -0.36 + crossTS * 0.93, crossTS);
                float proofSpacing = clamp(scale * lerp(0.24, 0.18, chaos01), 62.0, 180.0);
                float proofWarpA = (ValueNoise(detailPosCross / max(proofSpacing * 0.92, 1.0) + float2(waveTime * 0.19, 2.7)) - 0.5) * proofSpacing * lerp(0.28, 0.92, chaos01);
                float proofWarpB = (ValueNoise(detailPos / max(proofSpacing * 0.74, 1.0) + float2(3.1, -waveTime * 0.17)) - 0.5) * proofSpacing * lerp(0.22, 0.76, chaos01);
                float proofCoord = dot(wavePos + crossTS * proofWarpA, proofDirectionA) / max(proofSpacing, 1.0) + waveTime * 0.12;
                float proofCell = frac(proofCoord);
                float proofLineDistance = min(proofCell, 1.0 - proofCell);
                float proofA = 1.0 - smoothstep(0.028, 0.110, proofLineDistance);
                float proofCoordB = dot(detailPosCross + windTS * proofWarpB, proofDirectionB) / max(proofSpacing * 0.57, 1.0) - waveTime * 0.20;
                float proofCellB = frac(proofCoordB);
                float proofLineDistanceB = min(proofCellB, 1.0 - proofCellB);
                float proofB = 1.0 - smoothstep(0.024, 0.085, proofLineDistanceB);
                float proofBreakupA = smoothstep(0.16, 0.74, ValueNoise(wavePos / max(proofSpacing * 1.65, 1.0) + float2(waveTime * 0.11, 0.0)));
                float proofBreakupB = smoothstep(0.20, 0.82, ValueNoise(detailPos / max(proofSpacing * 1.05, 1.0) + float2(0.0, -waveTime * 0.13)));
                float proofCrossNoise = ValueNoise((wavePos + detailPos * 0.31) / max(proofSpacing * 2.35, 1.0) + waveTime * 0.041);
                proofA *= lerp(0.26, 1.0, proofBreakupA);
                proofB *= lerp(0.18, 1.0, proofBreakupB);
                float crossedProof = max(proofA, proofB * lerp(0.35, 0.72, chaos01));
                crossedProof += proofA * proofB * lerp(0.18, 0.42, proofCrossNoise);
                waveProof = saturate(max(cellPattern, crossedProof * 0.45) * lerp(0.42, 1.0, waveEnergy));

                // The shore-normal band moves independently of the breakup threshold.
                shoreFoam = shoreGradient * _ShoreFoamIntensity * lerp(0.72, 1.06, wind01);

                // Short ripples tilt reflections but do not break. Whitecaps follow the displaced swell crests.
                float crestHeight = swellHeight / max(_SwellAmplitude * lerp(0.10, 1.0, swellOpen01), 0.001);
                float crestWeather = smoothstep(0.25, 0.85, wind01);
                crestFoam = smoothstep(0.35, 0.70, crestHeight) * crestWeather
                    * _WhitecapIntensity * lerp(0.15, 1.0, openWater01);

                float foamSaturation = saturate(shoreFoam + crestFoam);
                // World-space breakup stays small enough to resolve around character-sized contacts.
                float2 foamUv = positionTS * 4.0 + float2(waveTime * 0.17, -waveTime * 0.11);
                float foamThreshold = FoamThresholdMask(foamUv);
                float foamAa = max(fwidth(foamThreshold), 0.035);
                foamAmount = smoothstep(foamThreshold - foamAa, foamThreshold + foamAa, foamSaturation)
                    * smoothstep(0.0, 0.10, foamSaturation);
            }

            float SurfaceDepthBlend(float depth01)
            {
                float waterDepthMeters = depth01 * max(_DeepDepth, 0.001);
                float metricDepth = smoothstep(_ShallowDepth * 0.25, max(_DeepDepth * 0.85, _ShallowDepth + 1.0), waterDepthMeters);
                float encodedDepth = smoothstep(0.035, 0.42, depth01);
                return pow(saturate(max(metricDepth, encodedDepth)), 0.72);
            }

            bool IsVolumeOwnedMode()
            {
                return _OceanDebugMode == DEBUG_VOLUME_ONLY
                    || _OceanDebugMode == DEBUG_WATER_OFF
                    || (_OceanDebugMode >= DEBUG_VOLUME_DATA && _OceanDebugMode <= DEBUG_VOLUME_REFRACTION)
                    || (_OceanDebugMode >= DEBUG_VOLUME_BOUNDARY && _OceanDebugMode <= DEBUG_VOLUME_OPTICAL)
                    || (_OceanDebugMode >= DEBUG_VOLUME_CONTACT && _OceanDebugMode <= DEBUG_VOLUME_OCCLUSION)
                    || (_OceanDebugMode >= DEBUG_VOLUME_SPHERE && _OceanDebugMode <= DEBUG_SEA_SOURCE_MATTE)
                    || _OceanDebugMode == DEBUG_VOLUME_AFTER_ATMOSPHERE
                    || (_OceanDebugMode >= DEBUG_VOLUME_CONTRIBUTION && _OceanDebugMode <= DEBUG_PRECIPITATION_CONTRIBUTION)
                    || (_OceanDebugMode >= DEBUG_CAUSTICS_ONLY && _OceanDebugMode <= DEBUG_CAUSTICS_PRISM)
                    || (_OceanDebugMode >= DEBUG_TERRAIN_COAST_MASK && _OceanDebugMode <= DEBUG_TERRAIN_OVERRIDE_COMPOSITE);
            }

            // Sky colour along the reflected ray. The previous constant could never match the real
            // sky, so grazing water read as flat paint against a warm horizon. Collapses to the
            // night constant at daylight 0 - the distant-water night floor below depends on that.
            float3 EvaluateSkyReflection(float3 reflectDir, float3 upWS, float3 sunDir, float daylight)
            {
                float upness = saturate(dot(reflectDir, upWS));
                float sunAlign = saturate(dot(reflectDir, sunDir));
                // Palette tracks the sky this atmosphere actually renders: teal above, warm gold near
                // the horizon. A neutral-grey horizon reflects as wet sand, not water.
                float3 zenithColor = float3(0.26, 0.44, 0.60);
                float3 horizonColor = float3(0.62, 0.60, 0.46);
                float3 dayColor = lerp(horizonColor, zenithColor, pow(upness, 0.55));
                dayColor += float3(0.40, 0.26, 0.09) * pow(sunAlign, 8.0) * 0.60;
                float4 weather = SampleWeather(upWS);
                float gloom = WeatherCloudGloom(upWS, weather.g);
                float cloudCover = saturate(max(weather.r, gloom));
                float cloudLuma = dot(dayColor, float3(0.2126, 0.7152, 0.0722));
                dayColor = lerp(dayColor, cloudLuma.xxx * (1.0 - gloom * 0.5), cloudCover);
                return lerp(float3(0.010, 0.018, 0.030), dayColor, daylight);
            }

            float ShoreGradient(float3 positionWS, float column, float sceneValid)
            {
                float3 axisA, axisB;
                BuildPlanetWaveAxes(axisA, axisB);
                float3 local = positionWS - _PlanetCenter;
                float2 uv = float2(dot(local, axisA), dot(local, axisB));
                float weather = WaterWeatherEnergy(normalize(local));
                float phase = _GameTime * max(_WaveSpeed, 0.001) * lerp(1.5, 2.8, weather)
                    + ValueNoise(uv * 0.35) * 2.0;
                float front = (0.5 + 0.5 * sin(phase)) * _ShoreFoamDepth;
                float bandWidth = max(_ShoreFoamDepth * 0.16, 0.025);
                float movingBand = 1.0 - smoothstep(bandWidth * 0.3, bandWidth,
                    abs(column - front));
                float contactRing = 1.0 - smoothstep(0.025, 0.12, column);
                float wet = smoothstep(0.0, 0.025, column);
                float breakup = lerp(0.25, 0.85, ValueNoise(uv * 2.0 + float2(phase * 0.3, -phase * 0.2)));
                return max(contactRing * 0.8, movingBand * breakup) * wet * sceneValid;
            }

            float ShorelineTrim(float waterPath, float column, float sceneValid)
            {
                // A short submerged ray fades contact without exposing a wide bed strip at grazing angles.
                return lerp(1.0, smoothstep(0.0, 0.35, waterPath) * step(0.0, column), sceneValid);
            }

            SurfaceLayer ComputeSurfaceLayer(
                float3 positionWS,
                float3 swellNormalWS,
                float depth01,
                float shore01,
                float body01,
                float waterTemperature01,
                float swellHeight,
                float2 screenUV)
            {
                SurfaceLayer layer;
                // Weather and planet coordinates use radial up. Only lighting follows the moving swell normal.
                float3 normalWS = SafeNormalize(positionWS - _PlanetCenter, swellNormalWS);

                float3 waterData = float3(depth01, shore01, body01);
                // fwidth is a SCREEN-space derivative used to detect a WORLD-space data problem, so it
                // saturates wherever a triangle compresses to few pixels - i.e. everywhere at grazing
                // angles - and the old 0.22 floor then cut glint by 78% along those bands. That read as
                // hard-edged blotches on the surface, worse than the vertex-colour seams it guards
                // against. Fires only on strong discontinuities now, and suppresses gently.
                // Root cause remains the vertex-colour packing in WaterMeshBuilder.
                float dataEdge = saturate(length(fwidth(waterData)) * 16.0);
                float dataContinuity = lerp(1.0, 0.78, smoothstep(0.35, 1.0, dataEdge));
                float depthBlend = SurfaceDepthBlend(depth01);
                float shoreVisibility = smoothstep(0.018, 0.18, shore01);
                float3 rippleNormalWS;
                float signedWaveHeight;
                float waveSlope;
                float rippleSignal;
                float waveProof;
                float waveEnergy;
                float storm01;
                float foamAmount;
                float shoreFoam;
                float crestFoam;
                float sceneValid;
                float waterPathMeters;
                float waterColumn = MeasuredWaterColumn(positionWS, screenUV, _SeaLevelRadius, sceneValid, waterPathMeters);
                float shoreGradient = ShoreGradient(positionWS, waterColumn, sceneValid);
                ComputeSurfaceWaves(positionWS, normalWS, depth01, shore01, shoreGradient, body01, swellHeight, rippleNormalWS, signedWaveHeight, waveSlope, rippleSignal, waveProof, waveEnergy, storm01, foamAmount, shoreFoam, crestFoam);
                rippleNormalWS = SafeNormalize(rippleNormalWS + swellNormalWS - normalWS, normalWS);
                float freezeFactor = EvaluateFreezeFactor(waterTemperature01, body01);
                float iceContribution = EvaluateIceContribution(positionWS, normalWS, freezeFactor);
                float liquidContribution = 1.0 - iceContribution;
                float3 iceNormalWS = ComputeIceNormal(positionWS, normalWS);
                rippleNormalWS = SafeNormalize(lerp(iceNormalWS, rippleNormalWS, liquidContribution), normalWS);
                signedWaveHeight *= liquidContribution;
                waveSlope *= liquidContribution;
                rippleSignal = lerp(0.5, rippleSignal, liquidContribution);
                waveProof *= liquidContribution;
                waveEnergy *= liquidContribution;
                storm01 *= liquidContribution;
                foamAmount *= liquidContribution;
                shoreFoam *= liquidContribution;
                crestFoam *= liquidContribution;
                float3 viewDir = SafeNormalize(_WorldSpaceCameraPos.xyz - positionWS, normalWS);
                float signedViewFacing = dot(viewDir, normalWS);
                float viewFacing = saturate(abs(dot(viewDir, rippleNormalWS)));
                float fresnel = pow(1.0 - viewFacing, 3.2);
                float viewPath = saturate(1.0 - exp(-waterPathMeters / WATER_ABSORPTION_UNIT_METRES));
                float foamCameraFacing = smoothstep(-0.025, 0.16, signedViewFacing);
                float foamDistanceVisibility = 1.0 - smoothstep(0.82, 0.98, viewPath);
                float foamVisibility = foamCameraFacing * lerp(1.0, foamDistanceVisibility, 0.35);
                foamAmount *= foamVisibility;
                shoreFoam *= foamVisibility;
                crestFoam *= foamVisibility;

                float3 sunDir = SafeNormalize(_SunParams, normalWS);
                float localSun = dot(normalWS, sunDir);
                float rippleSun = saturate(dot(rippleNormalWS, sunDir));
                float daylight = smoothstep(-0.08, 0.18, localSun);
                float shadow = CloudShadowFactor(positionWS, sunDir, localSun);
                shadow *= MainLightShadow(TransformWorldToShadowCoord(positionWS), positionWS,
                    half4(1, 1, 1, 1), half4(0, 0, 0, 0));

                float3 oceanShallow = lerp(_ShallowColor.rgb, float3(0.14, 0.58, 0.72), 0.30);
                float3 oceanDeep = max(_DeepColor.rgb, float3(0.0, 0.055, 0.18));
                float3 lakeShallow = lerp(_ShallowColor.rgb, float3(0.08, 0.32, 0.28), 0.25);
                float3 lakeDeep = max(_DeepColor.rgb, float3(0.005, 0.045, 0.060));
                float3 shallowColor = lerp(lakeShallow, oceanShallow, body01);
                float3 deepColor = lerp(lakeDeep, oceanDeep, body01);
                float3 waterColor = lerp(shallowColor, deepColor, depthBlend);

                float nightLight = saturate(_NightAmbientIntensity * 0.10 + 0.015);
                float dayLight = 0.46 + rippleSun * 0.54;
                float lightAmount = lerp(nightLight, dayLight, daylight);
                lightAmount *= lerp(1.0, shadow, daylight * 0.45);

                float3 reflectDir = reflect(-viewDir, rippleNormalWS);
                float3 skyReflection = EvaluateSkyReflection(reflectDir, normalWS, sunDir, daylight);

                float3 litColor = waterColor * lightAmount;
                // Reflection needs true Schlick (F0 0.02, ^5). The shared `fresnel` above uses ^3.2 for
                // the alpha/glint paths, which is far broader - reusing it here reflects mid-angles as
                // hard as grazing ones and washes the body colour out entirely.
                float reflectFresnel = 0.02 + 0.98 * pow(1.0 - viewFacing, 5.0);
                float reflectionBlend = reflectFresnel * lerp(0.08, 0.72, daylight) * lerp(0.45, 1.0, body01);
                float rippleContrast = (rippleSignal - 0.5) * 2.0;
                float waveShade = clamp(signedWaveHeight * 0.055 + rippleContrast * 0.072 + (rippleSun - saturate(localSun)) * 0.15, -0.12, 0.16);
                litColor *= 1.0 + waveShade * daylight * lerp(0.50, 1.0, body01);
                float3 baseSurfaceColor = lerp(litColor, skyReflection, reflectionBlend);
                float3 farWaterColor = max(deepColor, lerp(float3(0.02, 0.11, 0.07), float3(0.0, 0.060, 0.20), body01));
                // Distant-water term must collapse to the same night floor as the near path.
                // Previously it kept a fixed 0.48-0.90 of farWaterColor regardless of sun, so the
                // open ocean (where surfacePathBlend ~ 1) stayed ~half-lit at night = the dark-side glow.
                float farLight = lerp(nightLight, 0.76, daylight);
                float3 farBase = farWaterColor * farLight;
                // Lerp rather than sum, so raising the sky share cannot blow out the body colour and
                // daylight 0 still collapses farGraze to the same night floor as farBase.
                float3 farBody = farWaterColor * lerp(nightLight, 0.90, daylight);
                float3 farGraze = lerp(farBody, skyReflection, lerp(0.35, 0.72, body01) * daylight);
                float3 farSurfaceColor = lerp(farBase, farGraze, saturate(reflectFresnel * lerp(0.55, 0.85, body01)));
                float surfacePathBlend = smoothstep(0.10, 0.76, viewPath) * lerp(0.62, 0.98, body01);
                layer.color = lerp(baseSurfaceColor, farSurfaceColor, surfacePathBlend);
                layer.nearColor = baseSurfaceColor;
                layer.farColor = farSurfaceColor;
                layer.pathBlend = surfacePathBlend;
                float retainedSurfaceDetail = waveShade * daylight * shadow * lerp(0.60, 1.0, body01) * lerp(1.0, 0.42, surfacePathBlend) * liquidContribution;
                layer.color *= 1.0 + retainedSurfaceDetail;

                float3 halfDir = SafeNormalize(sunDir + viewDir, sunDir);
                float nDotH = saturate(dot(rippleNormalWS, halfDir));
                float glintPower = clamp(_SunGlitterPower * 0.22, 72.0, 640.0);
                float broadGlint = pow(nDotH, 72.0) * 0.16;
                float sharpGlint = pow(nDotH, glintPower);
                float glintSunMask = daylight * shadow * smoothstep(0.02, 0.24, localSun);
                float glintViewMask = lerp(0.28, 1.0, saturate(fresnel * 1.4 + viewPath * 0.35));
                // A flat, calm surface still reflects the sun. The half-vector selects the reflecting facets.
                float glint = (broadGlint + sharpGlint) * glintSunMask * glintViewMask * _SunGlitterIntensity * dataContinuity * liquidContribution;
                layer.color = saturate(layer.color + glint * float3(1.0, 0.92, 0.72));
                float3 foamLitColor = _FoamColor.rgb * lerp(0.05, 1.0, daylight) * lerp(0.70, 1.0, shadow);

                float iceSun = saturate(dot(iceNormalWS, sunDir));
                float iceLight = lerp(nightLight, 0.34 + iceSun * 0.66, daylight);
                iceLight *= lerp(1.0, shadow, daylight * 0.78);
                float3 localIcePosition = positionWS - _PlanetCenter;
                float iceNoise = ValueNoise(
                    float2(dot(localIcePosition, iceNormalWS.yzx),
                           dot(localIcePosition, iceNormalWS.zxy))
                    / max(_IceBreakupScale * 0.48, 1.0));
                float3 iceColor = _IceTint.rgb * iceLight * lerp(0.82, 1.12, iceNoise);
                float iceSpecPower = lerp(180.0, 18.0, _IceRoughness);
                float iceSpecular = pow(saturate(dot(iceNormalWS, halfDir)), iceSpecPower)
                    * daylight * shadow * lerp(0.34, 0.08, _IceRoughness);
                iceColor += iceSpecular * float3(0.86, 0.94, 1.0);
                layer.color = lerp(layer.color, saturate(iceColor), iceContribution);

                // Air before the surface does not absorb light. Extinction follows only the submerged
                // path, and Fresnel suppresses transmission at grazing angles. No bed means no sky leak.
                float3 transmittedLight = exp(-min(waterPathMeters, 100000.0) * WATER_ABSORPTION
                    / WATER_ABSORPTION_UNIT_METRES);
                float transmission = dot(transmittedLight, float3(0.2126, 0.7152, 0.0722));
                // Stylized clarity is reserved for downward views. Radial facing prevents ripple facets
                // from opening clear holes in an otherwise opaque grazing surface.
                transmission *= smoothstep(0.20, 0.70, abs(signedViewFacing));
                float surfaceAlpha = 1.0 - (1.0 - reflectFresnel) * transmission;
                float waterAlpha = saturate(lerp(surfaceAlpha, _IceOpacity, iceContribution))
                    * ShorelineTrim(waterPathMeters, waterColumn, sceneValid);
                // Foam is a separate scattering layer. Its narrow edge cannot inherit the water body's fade.
                float foamAlpha = saturate(foamAmount * _FoamColor.a)
                    * lerp(1.0, smoothstep(0.0, 0.025, waterColumn), sceneValid);
                layer.alpha = foamAlpha + waterAlpha * (1.0 - foamAlpha);
                layer.color = (foamLitColor * foamAlpha + layer.color * waterAlpha * (1.0 - foamAlpha))
                    / max(layer.alpha, 0.0001);
                // Atmosphere owns the underwater refraction window and total internal reflection.
                // Keep this opaque top sheet from painting over that view from below.
                if (signedViewFacing < 0.0)
                    layer.alpha *= 1.0 - CameraSubmerged01(_WorldSpaceCameraPos.xyz, _PlanetCenter,
                        _SeaLevelRadius, _SwellAmplitude);

                ClipWaterBackface(positionWS, _SeaLevelRadius);
                layer.depthBlend = depthBlend;
                layer.shoreVisibility = shoreVisibility;
                layer.fresnel = fresnel;
                layer.viewPath = viewPath;
                layer.daylight = daylight;
                layer.shadow = shadow;
                layer.normalWS = rippleNormalWS;
                layer.waveHeight = signedWaveHeight;
                layer.waveSlope = waveSlope;
                layer.rippleSignal = rippleSignal;
                layer.waveProof = waveProof;
                layer.surfaceDetail = retainedSurfaceDetail;
                layer.glint = glint;
                layer.waveEnergy = waveEnergy;
                layer.storm = storm01;
                layer.dataEdge = dataEdge;
                layer.foam = foamAmount;
                layer.shoreFoam = shoreFoam;
                layer.crestFoam = crestFoam;
                layer.waterTemperature = waterTemperature01;
                layer.freezeFactor = freezeFactor;
                layer.iceContribution = iceContribution;
                return layer;
            }

            // Large-swell vertex displacement lives in Includes/WaterDisplacement.hlsl so the volume
            // prepass rasterises the identical surface. This is the geometric layer of the GPU Gems
            // two-layer model; the fragment stage adds detail normals on top of it.
            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 objectNormalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 planetNormalWS = SafeNormalize(positionWS - _PlanetCenter, objectNormalWS);

                float4 waterData = saturate(input.color);
                float swellHeight;
                float3 swellNormal;
                positionWS = ComputeWaterVertexDisplacement(positionWS, planetNormalWS, waterData, swellNormal, swellHeight);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = swellNormal; // swell-following normal; fragment perturbs it with fine detail
                output.waterData = waterData;
                output.swellHeight = swellHeight;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (IsVolumeOwnedMode())
                    return half4(0.0, 0.0, 0.0, 0.0);

                #if FORCE_WATER_LAYER_PROOF
                    return half4(0.0, 1.0, 0.0, 1.0);
                #endif

                if (_OceanDebugMode == DEBUG_OFF && SHOW_SURFACE_IN_OFF == 0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float depth01 = saturate(input.waterData.r);
                float shore01 = saturate(input.waterData.g);
                float body01 = saturate(input.waterData.b);
                float waterTemperature01 = saturate(input.waterData.a);
                SurfaceLayer layer = ComputeSurfaceLayer(
                    input.positionWS,
                    input.normalWS,
                    depth01,
                    shore01,
                    body01,
                    waterTemperature01,
                    input.swellHeight,
                    input.positionCS.xy / max(_ScaledScreenParams.xy, float2(1.0, 1.0)));

                if (_OceanDebugMode == DEBUG_WATER_DEPTH)
                    return half4(lerp(float3(0.55, 1.0, 0.92), float3(0.0, 0.025, 0.16), layer.depthBlend), 1.0);
                if (_OceanDebugMode == DEBUG_WATER_SHORE)
                    return half4(lerp(float3(0.02, 0.02, 0.025), float3(1.0, 0.92, 0.16), shore01), 1.0);
                if (_OceanDebugMode == DEBUG_WATER_BODY)
                    return half4(lerp(float3(0.86, 0.22, 0.70), float3(0.05, 0.85, 1.0), body01), 1.0);
                if (_OceanDebugMode == DEBUG_WATER_TEMPERATURE)
                    return half4(lerp(float3(0.05, 0.25, 1.0), float3(1.0, 0.12, 0.02), layer.waterTemperature), 1.0);
                if (_OceanDebugMode == DEBUG_WATER_FREEZE)
                    return half4(lerp(float3(0.02, 0.02, 0.03), float3(0.45, 0.92, 1.0), layer.freezeFactor), 1.0);
                if (_OceanDebugMode == DEBUG_WATER_ICE_CONTRIBUTION)
                    return half4(lerp(float3(0.02, 0.02, 0.03), float3(1.0, 0.0, 1.0), layer.iceContribution), 1.0);
                if (_OceanDebugMode == DEBUG_WATER_LIGHTING)
                    return half4(layer.daylight, layer.shadow, layer.fresnel, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_GLINT)
                    return half4(saturate(layer.glint * 4.0).xxx, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_NORMALS)
                    return half4(layer.normalWS * 0.5 + 0.5, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_FOAM)
                    return half4(layer.foam.xxx, 1.0);
                if (_OceanDebugMode == DEBUG_FOAM_PARTS)
                    return half4(layer.shoreFoam, layer.crestFoam, layer.foam, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_MOTION_MASK)
                    return half4(layer.waveEnergy, layer.storm, _WindStrength01, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_WAVE_HEIGHT)
                {
                    float3 troughColor = float3(0.02, 0.06, 0.30);
                    float3 neutralColor = float3(0.45, 0.56, 0.62);
                    float3 crestColor = float3(1.0, 0.94, 0.62);
                    float3 debugColor = layer.waveHeight < 0.0
                        ? lerp(neutralColor, troughColor, -layer.waveHeight)
                        : lerp(neutralColor, crestColor, layer.waveHeight);
                    return half4(debugColor, 1.0);
                }
                if (_OceanDebugMode == DEBUG_WATER_WAVE_SLOPE)
                    return half4(lerp(float3(0.02, 0.04, 0.06), float3(0.1, 1.0, 0.45), saturate(layer.waveSlope * 2.4)), 1.0);
                // Two-tone: BRIGHT RED below the split, blue above. Point one of these at a shape you want
                // identified - if the shape turns solid red, that channel is what defines it. Split at 0.5
                // because the vertex channels are packed 0..1 and the interesting boundaries sit mid-range.
                // Screen-space derivative of the packed vertex data, hugely amplified. Any place adjacent
                // pixels disagree lights up, whatever the values are - which a fixed threshold split cannot
                // find. Red = the data is discontinuous here.
                if (_OceanDebugMode == DEBUG_SHAPE_IS_DATA_EDGE)
                {
                    float3 d = abs(ddx(float3(depth01, shore01, body01))) + abs(ddy(float3(depth01, shore01, body01)));
                    return half4(saturate(max(max(d.x, d.y), d.z) * 400.0), 0.0, 0.0, 1.0);
                }
                // DIRECT RAMP. These channels are 0..1, so the value itself is the right instrument.
                // Two earlier versions were worse: a 0.5 threshold split, which is blind whenever both
                // sides of a boundary sit above or below it, and frac(x * 32), which on a channel sitting
                // near 1.0 aliases into speckle. Both reported "uniform" for a channel that jumps by half
                // its range across eight pixels.
                if (_OceanDebugMode == DEBUG_SHAPE_IS_DEPTH)
                    return half4(depth01, 0.0, 0.0, 1.0);
                if (_OceanDebugMode == DEBUG_SHAPE_IS_SHORE)
                    return half4(shore01, 0.0, 0.0, 1.0);
                if (_OceanDebugMode == DEBUG_SHAPE_IS_BODY)
                    return half4(body01, 0.0, 0.0, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_DATA)
                    return half4(depth01, shore01, body01, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_ABSORPTION)
                    return half4(lerp(float3(0.03, 0.05, 0.07), float3(0.02, 0.32, 1.0), layer.depthBlend), 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_ALPHA)
                    return half4(layer.alpha, lerp(0.16, 0.54, layer.depthBlend), layer.fresnel, 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_CONTACT)
                    return half4(shore01, layer.shoreVisibility, layer.alpha, 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_BLEND)
                    return half4(layer.alpha, layer.depthBlend, layer.shoreVisibility, 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_RAW_OPAQUE)
                    return half4(layer.color, 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_FX_CONTRIB)
                    return half4(saturate(layer.waveSlope * 2.2), layer.dataEdge, saturate(layer.glint * 4.0), 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_FX_PROOF)
                {
                    float3 waveProofColor = lerp(float3(0.035, 0.0, 0.035), float3(1.0, 0.0, 1.0), layer.waveProof);
                    return half4(waveProofColor, 1.0);
                }
                if (_OceanDebugMode == DEBUG_SURFACE_ALPHA_PARTS)
                    return half4(layer.alpha, layer.viewPath, layer.fresnel, 1.0);
                if (_OceanDebugMode == DEBUG_FOAM_PINK)
                    return half4(1.0, 0.0, 1.0, 0.0);
                if (_OceanDebugMode == DEBUG_WATER_WAVE_SWELL)
                {
                    // Vertex-displaced swell height (the geometry that makes 3D waves).
                    // Map +/- (_SwellAmplitude * 0.5) to color: blue trough -> black calm -> yellow crest.
                    // Half-amplitude divisor so realistic peaks (~0.7-1.4 m) hit full saturation.
                    float swellRange = max(_SwellAmplitude * 0.5, 0.001);
                    float h = clamp(input.swellHeight / swellRange, -1.0, 1.0);
                    float3 troughColor = float3(0.05, 0.35, 1.0);
                    float3 calmColor = float3(0.02, 0.02, 0.04);
                    float3 crestColor = float3(1.0, 0.85, 0.18);
                    float3 swellColor = h < 0.0
                        ? lerp(calmColor, troughColor, -h)
                        : lerp(calmColor, crestColor, h);
                    return half4(swellColor, 1.0);
                }
                if (_OceanDebugMode == DEBUG_WATER_WAVE_ENERGY)
                {
                    // R = openWater01 (large body -> deep ocean), G = deepWater01 (away from terrain),
                    // B = shoreFade (1 in open water, 0 at coastline). Source of truth = EvaluateSwellGating,
                    // so the view always matches what the swell actually uses.
                    float openWater01, deepWater01, shoreFade;
                    EvaluateSwellGating(depth01, shore01, body01, openWater01, deepWater01, shoreFade);
                    return half4(openWater01, deepWater01, shoreFade, 1.0);
                }
                if (_OceanDebugMode == DEBUG_WATER_WAVE_PHASE)
                {
                    // World-lock test. Bright magenta DOTS at fixed world positions on a regular grid,
                    // on a deep-purple background. TIME-FROZEN. Pan the camera: if the dots stay glued
                    // to the ocean = world-locked (correct). If they slide with the camera = bug.
                    // Sharp dots (vs. sin-sum) so the landmarks are unmistakable in motion.
                    float3 axisA, axisB;
                    BuildPlanetWaveAxes(axisA, axisB);
                    float3 localPos = input.positionWS - _PlanetCenter;
                    float2 posTS = float2(dot(localPos, axisA), dot(localPos, axisB));
                    float spacing = max(_SwellWavelength, 24.0);
                    float2 cellPos = float2(frac(posTS.x / spacing), frac(posTS.y / spacing)) - 0.5;
                    float dist = length(cellPos);
                    float dotWidth = max(fwidth(posTS.x / spacing), fwidth(posTS.y / spacing)) * 2.0;
                    float dotMask = 1.0 - smoothstep(0.10, 0.10 + dotWidth, dist); // 10% of cell radius
                    float3 bgColor = float3(0.25, 0.0, 0.50);
                    float3 dotColor = float3(1.0, 0.20, 0.95);
                    return half4(lerp(bgColor, dotColor, dotMask), 1.0);
                }
                if (_OceanDebugMode == DEBUG_WATER_GLINT_LOCATOR)
                {
                    // UNMISTAKABLE glint-presence test. Renders the normal water composite, then
                    // PAINTS BRIGHT PINK wherever glint contributes to the final pixel.
                    // The key diagnostic question: does pink appear as a CONSTELLATION OF SMALL
                    // SPARKLES (correct - bumpy waves giving many facets at the right angle) or
                    // as ONE LARGE DISC (broken - a flat surface with a broad specular lobe)?
                    float glintFlag = smoothstep(0.001, 0.05, layer.glint);
                    float3 pink = float3(1.0, 0.0, 1.0);
                    return half4(lerp(layer.color, pink, glintFlag), 1.0);
                }
                if (_OceanDebugMode == DEBUG_WATER_FOAM_LOCATOR)
                {
                    // UNMISTAKABLE foam-presence test. Renders the normal water composite, then
                    // PAINTS BRIGHT PINK at full opacity anywhere foam is computed (foamAmount > tiny).
                    // Stop guessing whether foam is invisible because it's missing vs because it's
                    // composited too weakly: any visible pink = foam IS there in the final image;
                    // no pink = foam is genuinely not being produced where you expect.
                    float foamFlag = smoothstep(0.001, 0.05, layer.foam);
                    float3 pink = float3(1.0, 0.0, 1.0);
                    return half4(lerp(layer.color, pink, foamFlag), 1.0);
                }
                if (_OceanDebugMode == DEBUG_WATER_FOAM_ON_SWELL)
                {
                    // Foam validation: pure white foam pixels painted on top of WaveSwell color
                    // (blue trough / black calm / yellow crest). Answers "are whitecaps on the crests?"
                    // at a glance — yellow + white = correct; blue + white = foam in a trough (bug);
                    // black + white near shoreline = shore foam (correct).
                    float swellRange = max(_SwellAmplitude * 0.5, 0.001);
                    float h = clamp(input.swellHeight / swellRange, -1.0, 1.0);
                    float3 calmColor = float3(0.02, 0.02, 0.04);
                    float3 troughColor = float3(0.05, 0.35, 1.0);
                    float3 crestColor = float3(1.0, 0.85, 0.18);
                    float3 swellBg = h < 0.0
                        ? lerp(calmColor, troughColor, -h)
                        : lerp(calmColor, crestColor, h);
                    float foamMask = saturate(layer.foam * _FoamColor.a);
                    return half4(lerp(swellBg, float3(1.0, 1.0, 1.0), foamMask), 1.0);
                }
                if (_OceanDebugMode == DEBUG_WATER_WAVE_GRID)
                {
                    // Wireframe of the displaced surface. A 10 m world-space grid is drawn along the
                    // wave-tangent axes; because the vertices are radially displaced before the
                    // fragment runs, the grid lines visibly undulate with the swell — you see the
                    // actual wave geometry. Lines also color by swellHeight (yellow at crest, blue
                    // at trough), so wave amplitude is readable at a glance.
                    float3 axisA, axisB;
                    BuildPlanetWaveAxes(axisA, axisB);
                    float3 localPos = input.positionWS - _PlanetCenter;
                    float u = dot(localPos, axisA);
                    float v = dot(localPos, axisB);
                    float gridSpacing = 10.0;
                    float fu = frac(u / gridSpacing);
                    float fv = frac(v / gridSpacing);
                    float distU = min(fu, 1.0 - fu);
                    float distV = min(fv, 1.0 - fv);
                    float widthU = fwidth(u / gridSpacing) * 1.5;
                    float widthV = fwidth(v / gridSpacing) * 1.5;
                    float lineU = 1.0 - smoothstep(0.0, max(widthU, 0.005), distU);
                    float lineV = 1.0 - smoothstep(0.0, max(widthV, 0.005), distV);
                    float wire = saturate(lineU + lineV);

                    float swellRange = max(_SwellAmplitude * 0.5, 0.001);
                    float h = clamp(input.swellHeight / swellRange, -1.0, 1.0);
                    float3 calmColor = float3(0.55, 0.75, 0.95);
                    float3 troughColor = float3(0.05, 0.30, 1.0);
                    float3 crestColor = float3(1.0, 0.90, 0.20);
                    float3 lineColor = h < 0.0
                        ? lerp(calmColor, troughColor, -h)
                        : lerp(calmColor, crestColor, h);

                    float3 bgColor = float3(0.02, 0.05, 0.12);
                    return half4(lerp(bgColor, lineColor, wire), 1.0);
                }
                if (_OceanDebugMode == DEBUG_SURFACE_NIGHT_TERMS)
                {
                    // R = near-water lit term, G = distant-water lit term, B = how far-weighted this pixel is.
                    // On the dark side: a bright GREEN ocean means the far term is the one staying lit.
                    // (x4 gain so the small night values are visible.)
                    return half4(saturate(Luminance3(layer.nearColor) * 4.0),
                                 saturate(Luminance3(layer.farColor) * 4.0),
                                 layer.pathBlend, 1.0);
                }
                if (_OceanDebugMode == DEBUG_SURFACE_LUMA_HEAT)
                    return half4(LumaHeatRamp(Luminance3(layer.color) * 4.0), 1.0); // x4 gain: night brightness lands mid-ramp

                return half4(layer.color, layer.alpha);
            }
            ENDHLSL
        }
    }
}
