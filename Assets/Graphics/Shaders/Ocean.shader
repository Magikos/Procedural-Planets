Shader "Planet/Ocean"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.28, 0.78, 0.82, 0.12)
        _DeepColor ("Deep Color", Color) = (0.0, 0.035, 0.095, 0.98)
        _FoamColor ("Foam Color", Color) = (0.92, 0.98, 1.0, 0.95)
        _ShallowDepth ("Shallow Depth", Range(1, 500)) = 28
        _DeepDepth ("Deep Depth", Range(20, 3000)) = 360
        _ShoreFoamDepth ("Shore Foam Width", Range(1, 200)) = 24
        _ShoreFoamSoftness ("Shore Range", Range(1, 300)) = 125
        _WaveAmplitude ("Wave Amplitude", Range(0, 12)) = 3.4
        _WaveScale ("Wave Scale", Range(50, 2000)) = 480
        _WaveSpeed ("Wave Speed", Range(0, 4)) = 0.58
        _WaveNormalStrength ("Wave Normal Strength", Range(0, 16)) = 4.5
        _WaterMotionStrength ("Water Motion Strength", Range(0, 1)) = 0.24
        _SunGlitterIntensity ("Sun Glitter Intensity", Range(0, 4)) = 0.75
        _SunGlitterPower ("Sun Glitter Power", Range(64, 4096)) = 1400
        _ShoreFoamIntensity ("Shore Foam Intensity", Range(0, 3)) = 1
        _WhitecapIntensity ("Whitecap Intensity", Range(0, 3)) = 1
        _WakeFoamIntensity ("Wake Foam Intensity", Range(0, 4)) = 1
        _WakeNormalStrength ("Wake Normal Strength", Range(0, 4)) = 1
        _OceanFocusMode ("Ocean Focus Mode", Range(0, 1)) = 1
        _Alpha ("Alpha", Range(0, 1)) = 0.9
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Includes/DebugModes.hlsl"
            #include "Includes/CloudShadows.hlsl"

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
                float3 waterData : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float _ShallowDepth;
                float _DeepDepth;
                float _ShoreFoamDepth;
                float _ShoreFoamSoftness;
                float _WaveAmplitude;
                float _WaveScale;
                float _WaveSpeed;
                float _WaveNormalStrength;
                float _WaterMotionStrength;
                float _SunGlitterIntensity;
                float _SunGlitterPower;
                float _ShoreFoamIntensity;
                float _WhitecapIntensity;
                float _WakeFoamIntensity;
                float _WakeNormalStrength;
                float _OceanFocusMode;
                float _Alpha;
            CBUFFER_END

            float3 _PlanetCenter;
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
            };

            float3 SafeNormalize(float3 value, float3 fallback)
            {
                float lenSq = dot(value, value);
                return lenSq > 0.000001 ? value * rsqrt(lenSq) : fallback;
            }

            float2 SafeNormalize2(float2 value, float2 fallback)
            {
                float lenSq = dot(value, value);
                return lenSq > 0.000001 ? value * rsqrt(lenSq) : fallback;
            }

            void BuildSurfaceBasis(float3 normalWS, out float3 tangentA, out float3 tangentB)
            {
                float3 referenceAxis = abs(normalWS.y) < 0.92 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                tangentA = SafeNormalize(cross(referenceAxis, normalWS), float3(1.0, 0.0, 0.0));
                tangentB = SafeNormalize(cross(normalWS, tangentA), float3(0.0, 0.0, 1.0));
            }

            void BuildPlanetWaveAxes(out float3 axisA, out float3 axisB)
            {
                float3 windWS = SafeNormalize(_WindDirection, float3(1.0, 0.0, 0.0));
                float3 referenceAxis = abs(dot(windWS, float3(0.0, 1.0, 0.0))) < 0.92
                    ? float3(0.0, 1.0, 0.0)
                    : float3(0.0, 0.0, 1.0);

                axisA = windWS;
                axisB = SafeNormalize(cross(referenceAxis, axisA), float3(0.0, 0.0, 1.0));
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

            float EvaluateSurfaceWave(float2 positionTS, float2 directionTS, float wavelength, float speed, float amplitude, float phase, out float2 gradientTS)
            {
                float k = 6.28318530718 / max(wavelength, 0.001);
                float theta = dot(positionTS, directionTS) * k + _GameTime * speed + phase;
                float waveSin = sin(theta);
                float waveCos = cos(theta);
                gradientTS = directionTS * (waveCos * amplitude * k);
                return waveSin * amplitude;
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
                float body01,
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
                float wind01 = saturate(_WindSpeed / 5.0);
                storm01 = SampleOceanStorm(normalWS, tangentA, tangentB);
                float openWater01 = smoothstep(0.42, 0.88, body01);
                float deepWater01 = smoothstep(0.035, 0.22, depth01);
                float weatherEnergy = saturate(wind01 * 0.92 + openWater01 * 0.08);
                float lakeWind01 = smoothstep(0.34, 0.88, wind01);
                float lakeEnergy = lerp(0.035, 0.50, lakeWind01) * lerp(0.18, 1.0, deepWater01);
                float oceanEnergy = lerp(0.48, 1.0, deepWater01) * lerp(0.80, 1.20, weatherEnergy);
                waveEnergy = saturate(lerp(lakeEnergy, oceanEnergy, openWater01));
                float chaos01 = saturate(wind01 * lerp(0.36, 0.86, openWater01) + openWater01 * 0.10);

                float scale = max(_WaveScale, 12.0);
                float amplitude = max(_WaveAmplitude, 0.0);
                float timeScale = max(_WaveSpeed, 0.001);
                float waveTime = _GameTime * timeScale;
                float2 warpDirA = SafeNormalize2(windTS * 0.21 + crossTS * 0.98, crossTS);
                float2 warpDirB = SafeNormalize2(windTS * -0.76 + crossTS * 0.65, crossTS);
                float2 domainWarp = float2(
                    sin(dot(positionTS, warpDirA) / max(scale * 1.70, 1.0) + waveTime * 0.34),
                    sin(dot(positionTS, warpDirB) / max(scale * 1.23, 1.0) - waveTime * 0.27));
                domainWarp *= scale * lerp(0.018, 0.095, chaos01);
                float2 wavePos = positionTS + domainWarp;
                float2 detailDrift = windTS * (waveTime * scale * lerp(0.004, 0.018, weatherEnergy))
                    + crossTS * (sin(waveTime * 0.31) * scale * lerp(0.004, 0.016, chaos01));
                float2 detailPos = positionTS + domainWarp * lerp(1.35, 2.85, chaos01) + detailDrift;
                float2 crossDrift = crossTS * (waveTime * scale * lerp(0.006, 0.028, weatherEnergy))
                    + windTS * (sin(waveTime * 0.37 + 1.7) * scale * lerp(0.005, 0.020, chaos01));
                float2 detailPosCross = positionTS - domainWarp * lerp(0.85, 2.25, chaos01) + crossDrift;

                float2 gradientTS = float2(0.0, 0.0);
                float2 detailGradientTS = float2(0.0, 0.0);
                float2 gradient;
                float height = 0.0;
                float detailHeight = 0.0;
                float swellStrength = amplitude * waveEnergy;
                float detailStrength = amplitude * waveEnergy * lerp(0.62, 1.48, weatherEnergy);

                height += EvaluateSurfaceWave(wavePos, windTS, scale * 1.18, timeScale * 0.18, swellStrength * 0.19, 0.00, gradient);
                gradientTS += gradient;
                height += EvaluateSurfaceWave(wavePos, SafeNormalize2(windTS * 0.70 + crossTS * (0.24 + chaos01 * 0.18), windTS), scale * 0.58, timeScale * -0.24, swellStrength * 0.085, 1.70, gradient);
                gradientTS += gradient;
                height += EvaluateSurfaceWave(wavePos, SafeNormalize2(windTS * 0.34 - crossTS * 0.68, windTS), scale * 0.25, timeScale * 0.36, swellStrength * 0.040, 3.10, gradient);
                gradientTS += gradient;
                height += EvaluateSurfaceWave(wavePos, SafeNormalize2(windTS * -0.22 + crossTS * 0.98, crossTS), scale * 0.13, timeScale * -0.48, swellStrength * 0.018, 5.40, gradient);
                gradientTS += gradient;

                float detailScale = clamp(scale * lerp(0.038, 0.026, chaos01), 7.5, 26.0);
                detailHeight += EvaluateSurfaceWave(detailPos, SafeNormalize2(windTS * 0.54 + crossTS * 0.84, windTS), detailScale * 0.88, timeScale * 0.82, detailStrength * 0.028, 0.80, gradient);
                detailGradientTS += gradient;
                detailHeight += EvaluateSurfaceWave(detailPosCross, SafeNormalize2(windTS * -0.28 + crossTS * 0.96, crossTS), detailScale * 0.61, timeScale * -1.10, detailStrength * 0.020, 2.40, gradient);
                detailGradientTS += gradient;
                detailHeight += EvaluateSurfaceWave(detailPosCross, SafeNormalize2(windTS * 0.91 - crossTS * 0.42, windTS), detailScale * 0.42, timeScale * 1.38, detailStrength * 0.014, 4.90, gradient);
                detailGradientTS += gradient;

                float2 surfaceGradientTS = gradientTS * 0.18 + detailGradientTS * 1.35;
                height += detailHeight * 0.42;
                float breakupNoise = ValueNoise(detailPos / max(detailScale * 5.6, 1.0) + float2(waveTime * 0.071, -waveTime * 0.043));
                float breakupNoiseB = ValueNoise((detailPos + crossTS * scale * 0.37) / max(detailScale * 3.4, 1.0) + float2(-waveTime * 0.052, waveTime * 0.064));
                float2 breakupVector = float2(breakupNoise - 0.5, breakupNoiseB - 0.5);
                float breakupStrength = lerp(0.045, 0.18, saturate(chaos01 + openWater01 * 0.35));
                surfaceGradientTS = surfaceGradientTS * lerp(0.72, 1.30, breakupNoise) + breakupVector * breakupStrength;
                float cellPattern = SurfaceCellPattern(positionWS, normalWS, scale, waveTime, chaos01);
                surfaceGradientTS *= lerp(0.70, 1.38, cellPattern);

                float depthMask = smoothstep(0.010, 0.095, depth01);
                float shoreMask = smoothstep(0.008, 0.070, shore01);
                float surfaceMask = depthMask * shoreMask * waveEnergy;
                float normalStrength = _WaveNormalStrength * 0.78 * lerp(0.42, 1.28, weatherEnergy) * surfaceMask;
                float2 normalGradient = surfaceGradientTS * normalStrength;

                rippleNormalWS = SafeNormalize(normalWS - tangentA * normalGradient.x - tangentB * normalGradient.y, normalWS);
                signedWaveHeight = clamp(height / max(amplitude * 0.62, 0.001), -1.0, 1.0) * surfaceMask;
                waveSlope = saturate(length(normalGradient) + cellPattern * surfaceMask * 0.045);
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

                float shoreBand = (1.0 - smoothstep(0.020, 0.19, shore01)) * smoothstep(0.004, 0.080, depth01);
                float shorePulse = 0.66 + 0.34 * sin(dot(detailPos, windTS) / max(scale * 0.072, 1.0) - waveTime * 0.95);
                shoreFoam = saturate(shoreBand * shorePulse * _ShoreFoamIntensity * lerp(0.72, 1.06, wind01));

                float crestDriver = max(signedWaveHeight, 0.0) * 0.58 + waveSlope * 0.84 + waveProof * 0.20;
                float crestEnergy = saturate(wind01 * 0.56 + openWater01 * 0.34);
                float crestWeather = smoothstep(0.10, 0.62, crestEnergy);
                crestFoam = smoothstep(0.36, 0.76, crestDriver) * crestWeather * _WhitecapIntensity * lerp(0.45, 1.0, openWater01);

                float foamSaturation = saturate(shoreFoam + crestFoam);
                float2 foamCoord = detailPos / max(scale * 0.050, 1.0) + windTS * (waveTime * 0.18);
                float foamThreshold = FoamThresholdMask(foamCoord);
                float foamPresence = smoothstep(foamThreshold - 0.22, foamThreshold + 0.10, foamSaturation);
                foamAmount = saturate(foamSaturation * lerp(0.50, 1.0, foamPresence));
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
                    || (_OceanDebugMode >= DEBUG_VOLUME_LIP_PINK && _OceanDebugMode <= DEBUG_VOLUME_LIP_DEPTH_GATE)
                    || _OceanDebugMode == DEBUG_VOLUME_LIP_SCENE_PINK
                    || (_OceanDebugMode >= DEBUG_CAUSTICS_ONLY && _OceanDebugMode <= DEBUG_CAUSTICS_PRISM);
            }

            SurfaceLayer ComputeSurfaceLayer(float3 positionWS, float3 normalWS, float depth01, float shore01, float body01)
            {
                SurfaceLayer layer;

                float3 waterData = float3(depth01, shore01, body01);
                float dataEdge = saturate(length(fwidth(waterData)) * 16.0);
                float dataContinuity = lerp(1.0, 0.22, smoothstep(0.16, 0.86, dataEdge));
                float depthBlend = SurfaceDepthBlend(depth01);
                float shoreVisibility = smoothstep(0.018, 0.18, shore01);
                float bodyVisibility = lerp(0.45, 1.0, body01);
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
                ComputeSurfaceWaves(positionWS, normalWS, depth01, shore01, body01, rippleNormalWS, signedWaveHeight, waveSlope, rippleSignal, waveProof, waveEnergy, storm01, foamAmount, shoreFoam, crestFoam);
                float3 viewDir = SafeNormalize(_WorldSpaceCameraPos.xyz - positionWS, normalWS);
                float signedViewFacing = dot(viewDir, normalWS);
                float viewFacing = saturate(abs(dot(viewDir, rippleNormalWS)));
                float fresnel = pow(1.0 - viewFacing, 3.2);
                float cameraDistance = distance(_WorldSpaceCameraPos.xyz, positionWS);
                float viewPathMeters = cameraDistance / max(viewFacing, 0.12);
                float viewPath = saturate(1.0 - exp(-viewPathMeters / max(_DeepDepth * 0.62, 1.0)));
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

                float3 shallowColor = lerp(_ShallowColor.rgb, float3(0.14, 0.58, 0.72), 0.30);
                float3 deepColor = max(_DeepColor.rgb, float3(0.0, 0.055, 0.18));
                float3 waterColor = lerp(shallowColor, deepColor, depthBlend);

                float nightLight = saturate(_NightAmbientIntensity * 0.10 + 0.015);
                float dayLight = 0.46 + rippleSun * 0.54;
                float lightAmount = lerp(nightLight, dayLight, daylight);
                lightAmount *= lerp(1.0, shadow, daylight * 0.45);

                float3 skyReflection = lerp(float3(0.010, 0.018, 0.030), float3(0.38, 0.58, 0.76), daylight);
                float3 litColor = waterColor * lightAmount;
                float reflectionBlend = fresnel * lerp(0.08, 0.38, daylight);
                float rippleContrast = (rippleSignal - 0.5) * 2.0;
                float waveShade = clamp(signedWaveHeight * 0.055 + rippleContrast * 0.072 + (rippleSun - saturate(localSun)) * 0.15, -0.12, 0.16);
                litColor *= 1.0 + waveShade * daylight * lerp(0.50, 1.0, body01);
                float3 baseSurfaceColor = lerp(litColor, skyReflection, reflectionBlend);
                float3 farWaterColor = max(deepColor, float3(0.0, 0.060, 0.20));
                float3 farSurfaceColor = lerp(farWaterColor * lerp(0.48, 0.76, daylight), skyReflection * 0.10 + farWaterColor * 0.90, fresnel * 0.55);
                float surfacePathBlend = smoothstep(0.10, 0.76, viewPath) * lerp(0.62, 0.98, body01);
                layer.color = lerp(baseSurfaceColor, farSurfaceColor, surfacePathBlend);
                float retainedSurfaceDetail = waveShade * daylight * shadow * lerp(0.60, 1.0, body01) * lerp(1.0, 0.42, surfacePathBlend);
                layer.color *= 1.0 + retainedSurfaceDetail;

                float3 halfDir = SafeNormalize(sunDir + viewDir, sunDir);
                float nDotH = saturate(dot(rippleNormalWS, halfDir));
                float glintPower = clamp(_SunGlitterPower * 0.22, 72.0, 640.0);
                float broadGlint = pow(nDotH, 72.0) * 0.16;
                float sharpGlint = pow(nDotH, glintPower);
                float glintSlopeMask = smoothstep(0.012, 0.11, waveSlope);
                float glintSunMask = daylight * shadow * smoothstep(0.02, 0.24, localSun);
                float glintViewMask = lerp(0.28, 1.0, saturate(fresnel * 1.4 + viewPath * 0.35));
                float glint = (broadGlint + sharpGlint) * glintSlopeMask * glintSunMask * glintViewMask * _SunGlitterIntensity * dataContinuity;
                layer.color = saturate(layer.color + glint * float3(1.0, 0.92, 0.72));
                float3 foamLitColor = _FoamColor.rgb * lerp(0.05, 1.0, daylight) * lerp(0.70, 1.0, shadow);
                layer.color = lerp(layer.color, foamLitColor, saturate(foamAmount * _FoamColor.a));

                float depthAlpha = lerp(0.09, 0.34, depthBlend);
                float shoreAlpha = lerp(0.42, 1.0, shoreVisibility);
                float nearAlpha = _Alpha * depthAlpha * shoreAlpha * bodyVisibility;
                float farOpacityCeiling = lerp(0.72, 0.98, saturate(max(depthBlend, fresnel)));
                float farAlpha = farOpacityCeiling * lerp(0.74, 1.0, body01);
                float pathAlpha = saturate(smoothstep(0.08, 0.68, viewPath) * lerp(0.82, 1.0, fresnel));
                layer.alpha = saturate(lerp(nearAlpha, farAlpha, pathAlpha));
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
                return layer;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 objectNormalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 planetNormalWS = SafeNormalize(positionWS - _PlanetCenter, objectNormalWS);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = planetNormalWS;
                output.waterData = saturate(input.color.rgb);
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
                SurfaceLayer layer = ComputeSurfaceLayer(input.positionWS, input.normalWS, depth01, shore01, body01);

                if (_OceanDebugMode == DEBUG_WATER_DEPTH)
                    return half4(lerp(float3(0.55, 1.0, 0.92), float3(0.0, 0.025, 0.16), layer.depthBlend), 1.0);
                if (_OceanDebugMode == DEBUG_WATER_SHORE)
                    return half4(lerp(float3(0.02, 0.02, 0.025), float3(1.0, 0.92, 0.16), shore01), 1.0);
                if (_OceanDebugMode == DEBUG_WATER_BODY)
                    return half4(lerp(float3(0.86, 0.22, 0.70), float3(0.05, 0.85, 1.0), body01), 1.0);
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
                    return half4(layer.waveEnergy, layer.storm, saturate(_WindSpeed / 5.0), 1.0);
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

                return half4(layer.color, layer.alpha);
            }
            ENDHLSL
        }
    }
}
