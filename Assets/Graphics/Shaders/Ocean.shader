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
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/CloudShadows.hlsl"

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
                float3 planetNormalWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float3 waterData : TEXCOORD4;
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
            float _PlanetRadius;
            float _AtmosphereRadius;
            float3 _SunParams;
            float _NightAmbientIntensity;
            int _OceanDebugMode;
            float _WaterVolumeEnabled;
            int _WaterWakeCount;
            float4 _WaterWakePositions[8];
            float4 _WaterWakeDirections[8];
            float4 _WaterWakeParams[8];

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                float n000 = Hash13(i + float3(0, 0, 0));
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, u.x);
                float nx10 = lerp(n010, n110, u.x);
                float nx01 = lerp(n001, n101, u.x);
                float nx11 = lerp(n011, n111, u.x);
                return lerp(lerp(nx00, nx10, u.y), lerp(nx01, nx11, u.y), u.z);
            }

            float3 SafeNormalize(float3 value, float3 fallback)
            {
                float lenSq = dot(value, value);
                return lenSq > 0.000001 ? value * rsqrt(lenSq) : fallback;
            }

            float3 TriplanarWaveWeights(float3 normalWS)
            {
                float3 weights = pow(abs(normalWS), 6.0);
                return weights / max(dot(weights, float3(1.0, 1.0, 1.0)), 0.0001);
            }

            float EvaluatePlanarWave(
                float3 localPosition,
                float3 normalWS,
                float3 planeDirectionWS,
                float frequency,
                float phase,
                float amplitude,
                out float3 gradientWS)
            {
                float wavePhase = dot(localPosition, planeDirectionWS) * frequency + phase;
                float waveSin = sin(wavePhase);
                float waveCos = cos(wavePhase);

                float3 tangentWS = planeDirectionWS - normalWS * dot(planeDirectionWS, normalWS);
                gradientWS = tangentWS * (waveCos * amplitude * frequency);
                return waveSin * amplitude;
            }

            float DeepWaterFactor(float depth01)
            {
                return smoothstep(0.07, 0.45, depth01);
            }

            float CameraUnderwater01(float surfaceRadius)
            {
                float cameraRadius = length(_WorldSpaceCameraPos.xyz - _PlanetCenter);
                float cameraSeaOffset = cameraRadius - surfaceRadius;
                return 1.0 - smoothstep(-0.25, 1.50, cameraSeaOffset);
            }

            float ShoreFoamSubmergedVisibility(float3 positionWS)
            {
                float surfaceRadius = length(positionWS - _PlanetCenter);
                float underwater = CameraUnderwater01(surfaceRadius);
                return lerp(1.0, 0.06, underwater);
            }

            float ShoreFoamFineVisibility(float3 positionWS)
            {
                float cameraDistance = distance(_WorldSpaceCameraPos.xyz, positionWS);
                float fadeStart = max(_ShoreFoamSoftness * 4.0, 420.0);
                float fadeEnd = max(_ShoreFoamSoftness * 13.0, 1450.0);
                float distanceFade = 1.0 - smoothstep(fadeStart, fadeEnd, cameraDistance);
                return saturate(distanceFade * ShoreFoamSubmergedVisibility(positionWS));
            }

            float UnderwaterShoreEdgeVisibility(float3 positionWS, float shore01)
            {
                float surfaceRadius = length(positionWS - _PlanetCenter);
                float underwater = CameraUnderwater01(surfaceRadius);
                float shoreEdge = 1.0 - smoothstep(0.10, 0.42, shore01);
                return lerp(1.0, 0.08, underwater * shoreEdge);
            }

            float WaterViewPath01(float3 positionWS, float3 normalWS, float3 viewDir)
            {
                float viewCos = saturate(abs(dot(normalWS, viewDir)));
                float grazing = smoothstep(0.14, 0.82, 1.0 - viewCos);
                float cameraDistance = distance(_WorldSpaceCameraPos.xyz, positionWS);
                float planetScale = max(_AtmosphereRadius, 1000.0);
                float normalizedDistance = cameraDistance / max(planetScale * 0.085, 420.0);
                return grazing * smoothstep(0.08, 1.05, normalizedDistance);
            }

            float SceneDepthValid(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return step(0.0001, rawDepth);
                #else
                    return 1.0 - step(0.9999, rawDepth);
                #endif
            }

            float WaterSceneGapMeters(float4 positionCS, float3 positionWS, out float validDepth)
            {
                float2 screenUv = GetNormalizedScreenSpaceUV(positionCS);
                float rawDepth = SampleSceneDepth(screenUv);
                validDepth = SceneDepthValid(rawDepth);
                float3 scenePositionWS = ComputeWorldSpacePosition(screenUv, rawDepth, UNITY_MATRIX_I_VP);

                float surfaceDistance = distance(_WorldSpaceCameraPos.xyz, positionWS);
                float sceneDistance = distance(_WorldSpaceCameraPos.xyz, scenePositionWS);
                return max(sceneDistance - surfaceDistance, 0.0) * validDepth;
            }

            float WaterSceneContactClearance01(float sceneGapMeters)
            {
                float contactStart = max(_ShallowDepth * 0.035, 1.25);
                float contactEnd = max(_ShallowDepth * 0.42, 14.0);
                return smoothstep(contactStart, contactEnd, sceneGapMeters);
            }

            float ShoreContactVisibility(float terrainClearance01, float sceneValid, float shore01)
            {
                float shoreContact = (1.0 - smoothstep(0.10, 0.52, shore01)) * sceneValid;
                return lerp(1.0, terrainClearance01, shoreContact);
            }

            float WaterScenePathFromGap01(float waterPath, float validDepth, float depth01, float shore01, float oceanFactor)
            {
                float pathMeters01 = 1.0 - exp2(-waterPath / max(_DeepDepth * 0.30, 32.0));

                float depthGate = smoothstep(0.008, 0.15, depth01);
                float shoreGate = smoothstep(0.030, 0.28, shore01);
                float bodyGate = lerp(0.38, 1.0, oceanFactor);
                return saturate(pathMeters01 * depthGate * shoreGate * bodyGate * validDepth);
            }

            float WaterScenePath01(float4 positionCS, float3 positionWS, float depth01, float shore01, float oceanFactor)
            {
                float validDepth;
                float waterPath = WaterSceneGapMeters(positionCS, positionWS, validDepth);
                return WaterScenePathFromGap01(waterPath, validDepth, depth01, shore01, oceanFactor);
            }

            float WaterCameraMedium01(float3 positionWS, float3 normalWS, float3 viewDir, float depth01, float shore01, float oceanFactor)
            {
                float cameraRadius = length(_WorldSpaceCameraPos.xyz - _PlanetCenter);
                float surfaceRadius = length(positionWS - _PlanetCenter);
                float cameraAboveWater = cameraRadius - surfaceRadius;
                float cameraNearSurface = 1.0 - smoothstep(22.0, max(_DeepDepth * 0.95, 260.0), max(cameraAboveWater, 0.0));
                float cameraUnderWater = CameraUnderwater01(surfaceRadius);
                float viewCos = saturate(abs(dot(normalWS, viewDir)));
                float grazingPath = smoothstep(0.10, 0.78, 1.0 - viewCos);
                float depthGate = smoothstep(0.014, 0.17, depth01);
                float shoreGate = smoothstep(0.040, 0.26, shore01);
                float bodyGate = lerp(0.42, 1.0, oceanFactor);
                return saturate(max(cameraUnderWater, cameraNearSurface * grazingPath) * depthGate * shoreGate * bodyGate);
            }

            float WaterAbsorption01(float depth01, float shore01, float oceanFactor, float3 positionWS, float3 normalWS, float3 viewDir, float scenePath01)
            {
                float viewCos = saturate(abs(dot(normalWS, viewDir)));
                float pathDepth = depth01 / max(viewCos, 0.075);
                float depthGate = smoothstep(0.012, 0.14, depth01);
                float exponentialAbsorption = 1.0 - exp2(-pathDepth * 6.4);
                float pathRamp = smoothstep(0.16, 0.72, pathDepth);
                float verticalAbsorption = max(exponentialAbsorption, pathRamp) * depthGate;

                float viewPath = WaterViewPath01(positionWS, normalWS, viewDir);
                float viewDepthGate = smoothstep(0.025, 0.14, depth01);
                float viewShoreGate = smoothstep(0.080, 0.34, shore01);
                float viewBodyGate = lerp(0.28, 1.0, oceanFactor);
                float horizontalAbsorption = viewPath * viewDepthGate * viewShoreGate * viewBodyGate;
                float openWaterAbsorption = smoothstep(0.18, 0.62, depth01)
                    * smoothstep(0.18, 0.54, shore01)
                    * oceanFactor
                    * lerp(0.38, 1.0, viewPath);
                float cameraMediumAbsorption = WaterCameraMedium01(positionWS, normalWS, viewDir, depth01, shore01, oceanFactor);

                return saturate(max(max(verticalAbsorption, horizontalAbsorption), max(max(openWaterAbsorption, scenePath01), cameraMediumAbsorption)));
            }

            float WaterOpticalAlpha(float depth01, float shore01, float oceanFactor, float3 positionWS, float3 normalWS, float3 viewDir, float scenePath01)
            {
                float absorption = WaterAbsorption01(depth01, shore01, oceanFactor, positionWS, normalWS, viewDir, scenePath01);
                float denseCoverage = smoothstep(0.06, 0.82, absorption);
                return saturate(lerp(_ShallowColor.a, max(_DeepColor.a, 0.995), denseCoverage));
            }

            float WaterFinalAlpha(float opticalAlpha, float absorption)
            {
                float densePath = smoothstep(0.30, 0.84, absorption);
                float legacyAlpha = saturate(lerp(opticalAlpha * _Alpha, max(opticalAlpha, 0.985), densePath));
                float volumeSurfaceAlpha = saturate(0.010 + opticalAlpha * 0.014 + absorption * 0.012);
                return lerp(legacyAlpha, volumeSurfaceAlpha, saturate(_WaterVolumeEnabled));
            }

            float WaterPathDarkening(float absorption)
            {
                return lerp(1.0, 0.34, smoothstep(0.14, 0.86, absorption));
            }

            float WindStrength01()
            {
                return saturate(_WindSpeed / 5.0);
            }

            float SampleOceanStorm(float3 normalWS, float depth01, float oceanFactor)
            {
                if (_CloudWeatherResolution <= 0)
                    return 0.0;

                float weatherStorm = saturate(SampleCloudShadowWeather(normalWS).g);
                return weatherStorm * DeepWaterFactor(depth01) * oceanFactor;
            }

            float OceanSeaState(float wind01)
            {
                // Sea state is driven primarily by wind so waves remain continuous
                // across the ocean. Local storm data is used for whitecaps, not
                // normal/height amplification, to avoid weather-map cyclone stamps.
                return saturate(0.18 + wind01 * 0.82);
            }

            float OceanFoamState(float wind01, float storm)
            {
                return saturate(0.12 + wind01 * 0.58 + storm * 0.72);
            }

            float EvaluateWaveLayer(
                float3 positionWS,
                float3 normalWS,
                float3 phaseSeedWS,
                float frequency,
                float phase,
                float amplitude,
                out float3 gradientWS)
            {
                float3 seedWS = SafeNormalize(phaseSeedWS, float3(1.0, 0.0, 0.0));
                float3 localPosition = positionWS - _PlanetCenter;
                float3 weights = TriplanarWaveWeights(normalWS);
                float3 planeDirX = SafeNormalize(float3(0.0, seedWS.y, seedWS.z), float3(0.0, 1.0, 0.0));
                float3 planeDirY = SafeNormalize(float3(seedWS.x, 0.0, seedWS.z), float3(1.0, 0.0, 0.0));
                float3 planeDirZ = SafeNormalize(float3(seedWS.x, seedWS.y, 0.0), float3(1.0, 0.0, 0.0));

                float3 gradientX;
                float3 gradientY;
                float3 gradientZ;
                float heightX = EvaluatePlanarWave(localPosition, normalWS, planeDirX, frequency, phase, amplitude, gradientX);
                float heightY = EvaluatePlanarWave(localPosition, normalWS, planeDirY, frequency, phase, amplitude, gradientY);
                float heightZ = EvaluatePlanarWave(localPosition, normalWS, planeDirZ, frequency, phase, amplitude, gradientZ);

                gradientWS = gradientX * weights.x + gradientY * weights.y + gradientZ * weights.z;
                return heightX * weights.x + heightY * weights.y + heightZ * weights.z;
            }

            float EvaluateOceanWaves(float3 positionWS, float3 normalWS, float depth01, float oceanFactor, out float3 gradientWS, out float crest)
            {
                float scale = max(_WaveScale, 1.0);
                float frequency = 6.28318530718 / scale;
                float time = _Time.y * _WaveSpeed;
                float wind01 = WindStrength01();
                float seaState = OceanSeaState(wind01);
                float deepWater = DeepWaterFactor(depth01);
                float amplitude = _WaveAmplitude
                    * lerp(0.32, 1.0, oceanFactor)
                    * lerp(0.18, 1.48, deepWater)
                    * lerp(0.82, 1.82, seaState);

                float3 base0 = normalize(float3(0.74, 0.18, 0.63));
                float3 base1 = normalize(float3(-0.32, 0.48, 0.82));
                float3 windSeed = dot(_WindDirection, _WindDirection) > 0.0001
                    ? normalize(_WindDirection)
                    : float3(1.0, 0.0, 0.0);
                float3 windCross = cross(windSeed, abs(windSeed.y) > 0.82 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0));
                windCross = SafeNormalize(windCross, float3(0.0, 0.0, 1.0));
                float3 wind1 = normalize(windSeed * 0.76 + windCross * 0.28);

                gradientWS = float3(0.0, 0.0, 0.0);
                float3 gradient;
                float height = 0.0;

                height += EvaluateWaveLayer(positionWS, normalWS, base0, frequency * 0.64, time * 0.44, amplitude * 0.42, gradient);
                gradientWS += gradient;
                height += EvaluateWaveLayer(positionWS, normalWS, base1, frequency * 0.88, -time * 0.62, amplitude * 0.22, gradient);
                gradientWS += gradient;

                height += EvaluateWaveLayer(positionWS, normalWS, windSeed, frequency * lerp(0.72, 1.04, seaState), time * lerp(0.78, 1.65, seaState), amplitude * lerp(0.16, 0.50, seaState), gradient);
                gradientWS += gradient;
                height += EvaluateWaveLayer(positionWS, normalWS, wind1, frequency * lerp(1.06, 1.58, seaState), time * lerp(1.05, 2.20, seaState), amplitude * lerp(0.06, 0.22, seaState), gradient);
                gradientWS += gradient;

                height += EvaluateWaveLayer(positionWS, normalWS, normalize(windSeed * 0.92 - windCross * 0.18), frequency * 0.46, time * lerp(0.34, 0.82, seaState), amplitude * deepWater * lerp(0.05, 0.18, seaState), gradient);
                gradientWS += gradient;

                float normalizedHeight = saturate(height / max(amplitude, 0.001) * 0.5 + 0.5);
                crest = smoothstep(0.68, 0.92, normalizedHeight) * oceanFactor;
                return height;
            }

            float3 EvaluateRippleGradient(float3 positionWS, float3 normalWS, float depth01, float oceanFactor)
            {
                float wind01 = WindStrength01();
                float seaState = OceanSeaState(wind01);
                float deepWater = DeepWaterFactor(depth01);
                float rippleScale = max(_WaveScale * lerp(0.12, 0.075, seaState), 34.0);
                float frequency = 6.28318530718 / rippleScale;
                float time = _Time.y * max(_WaveSpeed * lerp(2.0, 4.4, seaState), 0.55);

                float3 base0 = normalize(float3(0.18, 0.74, 0.63));
                float3 base1 = normalize(float3(-0.58, 0.24, 0.76));
                float3 windSeed = dot(_WindDirection, _WindDirection) > 0.0001
                    ? normalize(_WindDirection)
                    : float3(1.0, 0.0, 0.0);
                float3 windCross = cross(windSeed, abs(windSeed.y) > 0.82 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0));
                windCross = SafeNormalize(windCross, float3(0.0, 0.0, 1.0));
                float3 wind1 = normalize(windSeed * 0.62 + windCross * 0.42);

                float3 gradient;
                float3 rippleGradient = float3(0.0, 0.0, 0.0);

                // Normal-only surface ripples. These provide visible wave texture; they
                // are not used for vertex displacement because the global water mesh is
                // too coarse for short wavelengths.
                float rippleStrength = lerp(0.76, 1.82, seaState) * lerp(0.36, 1.22, deepWater);
                EvaluateWaveLayer(positionWS, normalWS, windSeed, frequency * 0.62, time * lerp(0.9, 1.9, seaState), 0.46 * rippleStrength, gradient);
                rippleGradient += gradient;
                EvaluateWaveLayer(positionWS, normalWS, wind1, frequency * 0.96, time * lerp(1.2, 2.4, seaState), 0.26 * rippleStrength, gradient);
                rippleGradient += gradient;
                EvaluateWaveLayer(positionWS, normalWS, base0, frequency * 1.55, -time * 1.7, 0.16 * lerp(0.88, 1.34, seaState), gradient);
                rippleGradient += gradient;
                EvaluateWaveLayer(positionWS, normalWS, base1, frequency * 2.45, time * 2.4, 0.08 * lerp(0.8, 1.55, seaState), gradient);
                rippleGradient += gradient;
                EvaluateWaveLayer(positionWS, normalWS, normalize(windSeed * 0.84 - windCross * 0.34), frequency * 3.30, time * 3.2, 0.04 * lerp(0.35, 1.0, seaState), gradient);
                rippleGradient += gradient;

                return rippleGradient * lerp(0.32, 1.0, oceanFactor) * lerp(0.28, 1.12, deepWater);
            }

            float MovingWaveRows(float3 positionWS, float3 normalWS, float depth01, float oceanFactor)
            {
                float wind01 = WindStrength01();
                float seaState = OceanSeaState(wind01);
                float deepWater = DeepWaterFactor(depth01);
                float3 windSeed = dot(_WindDirection, _WindDirection) > 0.0001
                    ? normalize(_WindDirection)
                    : float3(1.0, 0.0, 0.0);
                float3 windCross = cross(windSeed, abs(windSeed.y) > 0.82 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0));
                windCross = SafeNormalize(windCross, float3(0.0, 0.0, 1.0));
                float3 wind1 = normalize(windSeed * 0.58 + windCross * 0.48);
                float3 base0 = normalize(float3(-0.46, 0.41, 0.79));

                float time = _Time.y * max(_WaveSpeed * lerp(1.65, 3.1, seaState), 0.48);
                float scaleA = max(_WaveScale * lerp(0.34, 0.21, seaState), 84.0);
                float scaleB = max(_WaveScale * lerp(0.18, 0.12, seaState), 48.0);
                float scaleC = max(_WaveScale * lerp(0.11, 0.075, seaState), 34.0);
                float3 unusedGradient;
                float waveA = EvaluateWaveLayer(positionWS, normalWS, windSeed, 6.28318530718 / scaleA, time * 1.08, 1.0, unusedGradient);
                float waveB = EvaluateWaveLayer(positionWS, normalWS, wind1, 6.28318530718 / scaleB, -time * 1.46, 1.0, unusedGradient);
                float waveC = EvaluateWaveLayer(positionWS, normalWS, base0, 6.28318530718 / scaleC, time * 1.92, 1.0, unusedGradient);
                float breakup = lerp(0.72, 1.12, ValueNoise3((positionWS - _PlanetCenter) * 0.018 + float3(time * 0.025, 0.0, -time * 0.018)));
                return (waveA * 0.52 + waveB * 0.31 + waveC * 0.17)
                    * breakup
                    * oceanFactor
                    * lerp(0.22, 1.18, deepWater)
                    * lerp(0.82, 1.38, seaState);
            }

            float ComputeSurfaceGlint(
                float3 positionWS,
                float3 normalWS,
                float3 sunDir,
                float3 viewDir,
                float depth01,
                float shore01,
                float oceanFactor,
                float daylight,
                float surfaceEdgeVisibility,
                float wakeMask,
                out float glintCore,
                out float sparkle,
                out float glintEnvelope)
            {
                glintCore = 0.0;
                sparkle = 0.0;
                glintEnvelope = 0.0;

                if (_SunGlitterIntensity <= 0.0)
                    return 0.0;

                float compactPower = max(_SunGlitterPower, 64.0);
                float3 reflectedSun = reflect(-sunDir, normalWS);
                float align = saturate(dot(reflectedSun, viewDir));
                glintCore = pow(align, compactPower);
                glintEnvelope = pow(align, max(compactPower * 0.15, 80.0));
                sparkle = pow(align, max(compactPower * lerp(0.40, 0.68, saturate(wakeMask)), 96.0)) * glintEnvelope;

                float3 local = positionWS - _PlanetCenter;
                float sparkleNoise = lerp(0.42, 1.45, ValueNoise3(local * 0.058 + float3(_Time.y * 0.08, 0.0, -_Time.y * 0.045)));
                float microBands = smoothstep(0.36, 0.96, sin(dot(local, normalize(float3(0.61, 0.22, 0.76))) * 0.052 + _Time.y * 1.15) * 0.5 + 0.5);
                float waterMask = oceanFactor
                    * smoothstep(0.08, 0.32, depth01)
                    * smoothstep(0.08, 0.26, shore01)
                    * surfaceEdgeVisibility;

                return (glintCore * 1.24 + sparkle * sparkleNoise * lerp(0.10, 0.22, saturate(wakeMask)) + microBands * sparkle * 0.045)
                    * _SunGlitterIntensity
                    * daylight
                    * waterMask;
            }

            void SampleWakeField(
                float3 positionWS,
                float3 normalWS,
                out float wakeMask,
                out float wakeFoam,
                out float3 wakeGradientWS)
            {
                wakeMask = 0.0;
                wakeFoam = 0.0;
                wakeGradientWS = float3(0.0, 0.0, 0.0);

                int wakeCount = min(_WaterWakeCount, 8);
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    if (i >= wakeCount)
                        break;

                    float4 positionRadius = _WaterWakePositions[i];
                    float4 directionSpeed = _WaterWakeDirections[i];
                    float4 wakeParams = _WaterWakeParams[i];
                    float radius = max(positionRadius.w, 0.25);
                    float lengthMeters = max(wakeParams.z, radius * 2.0);
                    float strength = saturate(wakeParams.x) * saturate(directionSpeed.w);
                    float foamStrength = saturate(wakeParams.y);

                    if (strength <= 0.0)
                        continue;

                    float3 originNormal = SafeNormalize(positionRadius.xyz - _PlanetCenter, normalWS);
                    float3 fallbackForward = SafeNormalize(cross(originNormal, abs(originNormal.y) > 0.82 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0)), float3(1.0, 0.0, 0.0));
                    float3 forward = SafeNormalize(directionSpeed.xyz - originNormal * dot(directionSpeed.xyz, originNormal), fallbackForward);
                    float3 delta = positionWS - positionRadius.xyz;
                    float3 tangentDelta = delta - originNormal * dot(delta, originNormal);
                    float along = dot(tangentDelta, forward);
                    float3 sideVector = tangentDelta - forward * along;
                    float side = length(sideVector);
                    float3 sideDir = SafeNormalize(sideVector, fallbackForward);

                    float trailDistance = max(-along, 0.0);
                    float withinTrail = step(along, radius * 0.80) * (1.0 - smoothstep(lengthMeters * 0.86, lengthMeters, trailDistance));
                    float trail01 = saturate(trailDistance / lengthMeters);
                    float centerWidth = lerp(radius * 0.22, radius * 0.62, trail01);
                    float centerTrail = withinTrail
                        * (1.0 - smoothstep(centerWidth, centerWidth * 2.35, side))
                        * smoothstep(0.0, radius * 0.85, trailDistance + radius * 0.28);

                    float armCenter = trailDistance * 0.34;
                    float armWidth = radius * lerp(0.24, 0.72, trail01);
                    float kelvinArm = withinTrail
                        * (1.0 - smoothstep(armWidth, armWidth * 2.05, abs(side - armCenter)))
                        * smoothstep(radius * 0.35, radius * 1.75, trailDistance)
                        * (1.0 - smoothstep(0.62, 1.0, trail01));

                    float band = sin(trailDistance * 0.105 - side * 0.045 - _Time.y * 3.2) * 0.5 + 0.5;
                    band = smoothstep(0.34, 0.88, band);
                    float breakup = lerp(0.55, 1.18, ValueNoise3(positionWS * 0.035 + float3(_Time.y * 0.05, 0.0, -_Time.y * 0.04)));
                    float sourceMask = saturate((centerTrail * 0.88 + kelvinArm * (0.48 + band * 0.52)) * breakup * strength);
                    float sourceFoam = saturate((centerTrail * 0.46 + kelvinArm * band * 0.82) * foamStrength * strength);

                    wakeMask = saturate(wakeMask + sourceMask);
                    wakeFoam = saturate(wakeFoam + sourceFoam);
                    wakeGradientWS += (sideDir * kelvinArm + forward * centerTrail * 0.42)
                        * ((band * 2.0 - 1.0) * strength / max(radius * 0.16, 1.0));
                }
            }

            float FoamLine(float value, float width)
            {
                float centered = abs(frac(value) - 0.5);
                return 1.0 - smoothstep(width, width + 0.10, centered);
            }

            float FocusMotionMask(float depth01, float shore01, float oceanFactor)
            {
                float depthRelease = smoothstep(0.02, 0.18, depth01);
                float shoreRelease = smoothstep(0.02, 0.18, shore01) * 0.58;
                return oceanFactor
                    * saturate(max(depthRelease, shoreRelease));
            }

            float FocusNormalMask(float depth01, float shore01, float oceanFactor)
            {
                float depthRelease = smoothstep(0.012, 0.10, depth01);
                float shoreRelease = smoothstep(0.012, 0.10, shore01) * 0.72;
                return oceanFactor
                    * saturate(max(depthRelease, shoreRelease));
            }

            float ComputeShoreFoam(float3 positionWS, float shore01)
            {
                float shoreWidth01 = saturate(_ShoreFoamDepth / max(_ShoreFoamSoftness, 0.001));
                float normalizedShore = shore01 / max(shoreWidth01, 0.001);
                float edgeClear = smoothstep(0.055, 0.20, normalizedShore);
                float shorelineBand = smoothstep(0.10, 0.34, normalizedShore)
                    * (1.0 - smoothstep(0.72, 1.28, normalizedShore));
                shorelineBand = pow(saturate(shorelineBand), 0.82);
                float runupBand = smoothstep(0.08, 0.32, normalizedShore)
                    * (1.0 - smoothstep(0.92, 1.95, normalizedShore));

                float3 planetLocal = positionWS - _PlanetCenter;
                float slowTime = _Time.y * 0.055;
                float foamNoiseA = ValueNoise3(planetLocal * 0.038 + float3(slowTime, 0.0, slowTime * 0.61));
                float foamNoiseB = ValueNoise3(planetLocal * 0.091 + float3(0.0, -slowTime * 0.74, slowTime * 0.43));
                float foamBreakup = smoothstep(0.34, 0.82, foamNoiseA * 0.66 + foamNoiseB * 0.34 + shorelineBand * 0.34);

                float travellingLine = FoamLine(normalizedShore * 1.55 + foamNoiseB * 0.18 - _Time.y * 0.10, 0.09);
                float edgeLip = 1.0 - smoothstep(0.0, 0.24, normalizedShore);
                float submergedVisibility = ShoreFoamSubmergedVisibility(positionWS);
                float fineVisibility = ShoreFoamFineVisibility(positionWS);
                float lipBreakup = smoothstep(0.42, 0.78, foamNoiseA * 0.62 + foamNoiseB * 0.38);

                float staticFoam = shorelineBand * foamBreakup * 0.62 * submergedVisibility * lerp(0.56, 1.0, fineVisibility);
                float runupFoam = runupBand * travellingLine * foamBreakup * 0.18 * submergedVisibility * lerp(0.40, 1.0, fineVisibility);
                float lipFoam = edgeLip * edgeClear * lipBreakup * lerp(0.025, 0.10, foamNoiseA) * fineVisibility;
                return saturate(staticFoam + runupFoam + lipFoam);
            }

            float SunsetShimmer(
                float3 positionWS,
                float4 positionCS,
                float3 planetNormalWS,
                float3 waveNormalWS,
                float3 sunDir,
                float3 viewDir,
                float depth01,
                float shore01,
                float oceanFactor)
            {
                if (_SunGlitterIntensity <= 0.0)
                    return 0.0;

                float3 cameraNormalWS = SafeNormalize(_WorldSpaceCameraPos.xyz - _PlanetCenter, planetNormalWS);
                float cameraSun = dot(cameraNormalWS, sunDir);
                float lowSunMask = smoothstep(-0.075, 0.07, cameraSun) * (1.0 - smoothstep(0.26, 0.62, cameraSun));

                float4 sunClip = TransformWorldToHClip(_WorldSpaceCameraPos.xyz + sunDir * max(_AtmosphereRadius, 1000.0));
                float sunFacing = sunClip.w > 0.0 ? 1.0 : 0.0;
                float2 sunNdc = sunClip.xy / max(abs(sunClip.w), 0.0001);
                float2 waterNdc = positionCS.xy / max(abs(positionCS.w), 0.0001);
                float screenFade = 1.0 - smoothstep(1.02, 1.58, max(abs(sunNdc.x), abs(sunNdc.y)));

                float2 screenDelta = waterNdc - sunNdc;
                float belowSun = smoothstep(0.0, 0.07, -screenDelta.y);
                float pathDistance = saturate(-screenDelta.y * 0.62);
                float pathWidth = lerp(0.012, 0.18, pathDistance);
                float centerLine = 1.0 - smoothstep(pathWidth, pathWidth * 2.65, abs(screenDelta.x));
                float lengthFade = smoothstep(0.015, 0.11, pathDistance) * (1.0 - smoothstep(0.92, 1.35, pathDistance));

                float3 reflectedSun = reflect(-sunDir, waveNormalWS);
                float specAlign = saturate(dot(reflectedSun, viewDir));
                float broadSpec = pow(specAlign, 46.0);
                float sharpSpec = pow(specAlign, 420.0);

                float rowPhase = waterNdc.y * 130.0 + ValueNoise3(positionWS * 0.035) * 6.0 + _Time.y * 0.9;
                float rowBands = smoothstep(0.42, 0.98, sin(rowPhase) * 0.5 + 0.5);
                float breakup = lerp(0.34, 1.12, ValueNoise3(positionWS * 0.078 + float3(_Time.y * 0.04, 0.0, -_Time.y * 0.05)));
                float streaks = saturate(rowBands * breakup);

                float waterMask = oceanFactor
                    * smoothstep(0.06, 0.24, depth01)
                    * smoothstep(0.08, 0.26, shore01);
                float screenPath = centerLine * lengthFade * belowSun * streaks;
                float specularPath = (broadSpec * 0.45 + sharpSpec * 3.4) * streaks;

                return saturate(screenPath * 0.82 + specularPath)
                    * _SunGlitterIntensity
                    * lowSunMask
                    * sunFacing
                    * screenFade
                    * waterMask;
            }

            half4 RenderWaterFocus(float3 positionWS, float4 positionCS, float3 planetNormalWS, float depth01, float shore01, float oceanFactor)
            {
                float shoreFoam = ComputeShoreFoam(positionWS, shore01) * _ShoreFoamIntensity;

                float waterDepth = depth01 * max(_DeepDepth, 0.001);
                float metricDepthBlend = smoothstep(_ShallowDepth * 0.35, max(_DeepDepth * 0.38, _ShallowDepth + 1.0), waterDepth);
                float encodedDepthBlend = smoothstep(0.012, 0.32, depth01);
                float depthBlend = pow(saturate(max(metricDepthBlend, encodedDepthBlend)), 0.62);

                float shoreWidth01 = saturate(_ShoreFoamDepth / max(_ShoreFoamSoftness, 0.001));
                float shoreBand = 1.0 - smoothstep(shoreWidth01 * 0.35, shoreWidth01, shore01);
                float runupBand = 1.0 - smoothstep(shoreWidth01, shoreWidth01 * 4.2, shore01);
                float shallowShelf = 1.0 - smoothstep(shoreWidth01 * 0.35, shoreWidth01 * 2.4, shore01);
                float3 shallowColor = lerp(_ShallowColor.rgb, float3(0.36, 0.86, 0.82), shallowShelf * 0.34);
                float3 baseColor = lerp(shallowColor, _DeepColor.rgb, depthBlend);

                float oceanStorm = SampleOceanStorm(planetNormalWS, depth01, oceanFactor);
                float wind01 = WindStrength01();
                float seaState = OceanSeaState(wind01);
                float foamState = OceanFoamState(wind01, oceanStorm);

                float3 waveGradient;
                float waveCrest;
                float waveHeight = EvaluateOceanWaves(positionWS, planetNormalWS, depth01, oceanFactor, waveGradient, waveCrest);
                float3 rippleGradient = EvaluateRippleGradient(positionWS, planetNormalWS, depth01, oceanFactor);
                float wakeMask;
                float wakeFoam;
                float3 wakeGradient;
                SampleWakeField(positionWS, planetNormalWS, wakeMask, wakeFoam, wakeGradient);
                waveGradient += rippleGradient;
                waveGradient += wakeGradient * _WakeNormalStrength;

                float normalDetailMask = FocusNormalMask(depth01, shore01, oceanFactor);
                float wakeNormalBoost = lerp(1.0, 1.42, saturate(wakeMask));
                float3 waveNormalWS = normalize(planetNormalWS - waveGradient * (_WaveNormalStrength * 0.26 * lerp(0.86, 1.55, seaState) * normalDetailMask * wakeNormalBoost));
                float3 viewDir = GetWorldSpaceNormalizeViewDir(positionWS);
                float sceneDepthValid;
                float sceneGapMeters = WaterSceneGapMeters(positionCS, positionWS, sceneDepthValid);
                float scenePath = WaterScenePathFromGap01(sceneGapMeters, sceneDepthValid, depth01, shore01, oceanFactor);
                float shoreTerrainClearance = WaterSceneContactClearance01(sceneGapMeters);
                float absorption = WaterAbsorption01(depth01, shore01, oceanFactor, positionWS, planetNormalWS, viewDir, scenePath);
                float opticalDepthBlend = saturate(max(depthBlend, absorption * 1.05));
                float opticalAlpha = WaterOpticalAlpha(depth01, shore01, oceanFactor, positionWS, planetNormalWS, viewDir, scenePath);
                baseColor = lerp(shallowColor, _DeepColor.rgb, opticalDepthBlend);

                float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : float3(0.0, 1.0, 0.0);
                float localSun = dot(planetNormalWS, sunDir);
                float waveSun = saturate(dot(waveNormalWS, sunDir));
                float daylight = smoothstep(-0.04, 0.18, localSun);
                float nightLight = saturate(_NightAmbientIntensity * 0.08 + 0.006);
                float dayLight = lerp(0.36, 1.0, lerp(saturate(localSun), waveSun, normalDetailMask));
                float waterLight = lerp(nightLight, dayLight, daylight);
                float foamLight = lerp(nightLight * 0.35, dayLight, daylight);
                float normalizedWaveHeight = waveHeight / max(_WaveAmplitude, 0.001);
                float crestLift = saturate(normalizedWaveHeight * 1.15);
                float troughShade = saturate(-normalizedWaveHeight * 1.10);
                float rippleDetail = saturate(length(rippleGradient + wakeGradient * _WakeNormalStrength) * _WaveNormalStrength * 1.45 + wakeMask * 0.28);
                float directionalWaveLight = waveSun - saturate(localSun);
                float movingRows = MovingWaveRows(positionWS, planetNormalWS, depth01, oceanFactor);
                float movingCrests = smoothstep(0.25, 0.82, movingRows);
                float movingTroughs = smoothstep(0.18, 0.72, -movingRows);
                float baseView = saturate(dot(planetNormalWS, viewDir));
                float waveView = saturate(dot(waveNormalWS, viewDir));
                float viewRipple = (waveView - baseView) * 0.34;
                float deepWater = DeepWaterFactor(depth01);
                float openWaveMask = deepWater
                    * smoothstep(0.18, 0.58, shore01)
                    * oceanFactor
                    * normalDetailMask;
                float whitecapNoise = ValueNoise3((positionWS - _PlanetCenter) * 0.028 + float3(_Time.y * 0.045, 0.0, -_Time.y * 0.038));
                float whitecapSeed = waveCrest * 0.58 + movingCrests * 0.26 + rippleDetail * 0.12 + whitecapNoise * 0.16;
                float whitecaps = smoothstep(0.64, 0.95, whitecapSeed)
                    * openWaveMask
                    * smoothstep(0.14, 0.74, foamState)
                    * _WhitecapIntensity;
                float3 halfVector = SafeNormalize(sunDir + viewDir, waveNormalWS);
                float waveSpec = pow(saturate(dot(waveNormalWS, halfVector)), lerp(120.0, 42.0, seaState))
                    * openWaveMask
                    * daylight
                    * lerp(0.18, 0.72, seaState);
                float waveShade = (
                    crestLift * 0.14
                    - troughShade * 0.16
                    + directionalWaveLight * 0.34
                    + (rippleDetail - 0.22) * 0.18
                    + movingCrests * lerp(0.070, 0.145, seaState)
                    - movingTroughs * lerp(0.058, 0.126, seaState)
                    + viewRipple
                    + waveCrest * lerp(0.08, 0.18, seaState)) * daylight * normalDetailMask;

                float shoreFoamBlend = smoothstep(0.05, 0.72, shoreFoam) * _FoamColor.a * lerp(0.18, 1.08, daylight);
                float openFoamBlend = whitecaps * _FoamColor.a * lerp(0.24, 0.90, daylight);
                float wakeFoamBlend = wakeFoam * _WakeFoamIntensity * _FoamColor.a * lerp(0.32, 1.02, daylight);
                float shoreContactVisibility = ShoreContactVisibility(shoreTerrainClearance, sceneDepthValid, shore01);
                shoreFoamBlend *= UnderwaterShoreEdgeVisibility(positionWS, shore01) * shoreContactVisibility;
                float surfaceEdgeVisibility = UnderwaterShoreEdgeVisibility(positionWS, shore01) * shoreContactVisibility;
                wakeFoamBlend *= surfaceEdgeVisibility;
                float foamBlend = saturate(shoreFoamBlend + openFoamBlend + wakeFoamBlend);
                float3 litWater = baseColor * saturate(waterLight + waveShade);
                litWater -= float3(0.038, 0.052, 0.060) * troughShade * daylight * normalDetailMask;
                litWater += float3(0.050, 0.078, 0.076) * crestLift * daylight * normalDetailMask;
                litWater += float3(0.026, 0.045, 0.048) * rippleDetail * daylight * normalDetailMask;
                litWater += float3(0.052, 0.086, 0.088) * movingCrests * daylight * normalDetailMask * lerp(0.85, 1.55, seaState);
                litWater -= float3(0.038, 0.052, 0.058) * movingTroughs * daylight * normalDetailMask * lerp(0.85, 1.65, seaState);
                litWater *= WaterPathDarkening(absorption);
                litWater = lerp(litWater, litWater * float3(0.92, 0.96, 0.98), oceanStorm * 0.04 * daylight);
                litWater += waveSpec * float3(0.72, 0.92, 1.0);
                litWater += whitecaps * daylight * float3(0.10, 0.16, 0.17);
                float sunsetShimmer = SunsetShimmer(positionWS, positionCS, planetNormalWS, waveNormalWS, sunDir, viewDir, depth01, shore01, oceanFactor);
                float glintCore;
                float sparkle;
                float glintEnvelope;
                float surfaceGlint = ComputeSurfaceGlint(positionWS, waveNormalWS, sunDir, viewDir, depth01, shore01, oceanFactor, daylight, surfaceEdgeVisibility, wakeMask, glintCore, sparkle, glintEnvelope);
                litWater += surfaceGlint * float3(1.0, 0.86, 0.54);
                litWater += sunsetShimmer * float3(1.0, 0.48, 0.16);
                float3 litFoam = _FoamColor.rgb * foamLight;
                float3 color = lerp(litWater, litFoam, foamBlend);
                color += litFoam * foamBlend * (0.10 * daylight);
                float surfaceBaseAlpha = WaterFinalAlpha(opticalAlpha, absorption) * surfaceEdgeVisibility;
                float rawWaveDetailAlpha = saturate(
                    (abs(movingRows) * 0.030 + rippleDetail * 0.026 + max(crestLift, troughShade) * 0.018)
                    * daylight
                    * normalDetailMask
                    * openWaveMask
                    * surfaceEdgeVisibility);
                // Water volume owns bulk opacity, but surface effects need their own layer
                // or transparent blending erases every visible wave/glint/foam contribution.
                float surfaceDetailSignal = saturate(
                    movingCrests * 0.44
                    + movingTroughs * 0.30
                    + rippleDetail * 0.62
                    + max(crestLift, troughShade) * 0.36);
                float volumeSurfaceFeatureCoverage = oceanFactor
                    * normalDetailMask
                    * surfaceEdgeVisibility;
                float volumeSurfaceFeatureAlpha = saturate(_WaterVolumeEnabled)
                    * saturate(0.10 + surfaceDetailSignal * 0.14)
                    * daylight
                    * volumeSurfaceFeatureCoverage;
                float waveDetailAlpha = max(rawWaveDetailAlpha, volumeSurfaceFeatureAlpha);
                float foamAlpha = foamBlend * lerp(0.42, 0.72, daylight);
                float glintAlpha = saturate((sunsetShimmer * 0.22 + surfaceGlint * 0.38) * surfaceEdgeVisibility);
                float alphaPreview = saturate(surfaceBaseAlpha + waveDetailAlpha + foamAlpha + glintAlpha);

                if (_OceanDebugMode == 1)
                    return half4(lerp(float3(0.55, 1.0, 0.92), float3(0.0, 0.025, 0.16), depthBlend), 1.0);
                if (_OceanDebugMode == 2)
                    return half4(lerp(float3(0.02, 0.02, 0.025), float3(1.0, 0.92, 0.16), saturate(shoreBand + runupBand * 0.35)), 1.0);
                if (_OceanDebugMode == 3)
                    return half4(lerp(float3(0.86, 0.22, 0.70), float3(0.05, 0.85, 1.0), oceanFactor), 1.0);
                if (_OceanDebugMode == 4)
                    return half4(float3(daylight, waveSun, waterLight), 1.0);
                if (_OceanDebugMode == 5)
                    return half4(float3(saturate(glintCore * 80.0), saturate(sparkle * 12.0 + surfaceGlint * 0.16), saturate(glintEnvelope)), 1.0);
                if (_OceanDebugMode == 6)
                    return half4(waveNormalWS * 0.5 + 0.5, 1.0);
                if (_OceanDebugMode == 7)
                    return half4(lerp(float3(0.02, 0.035, 0.05), _FoamColor.rgb, saturate(shoreFoam + whitecaps + wakeFoam)), 1.0);
                if (_OceanDebugMode == 8)
                {
                    float focusMotionMask = FocusMotionMask(depth01, shore01, oceanFactor);
                    return half4(lerp(float3(0.04, 0.02, 0.10), float3(1.0, 0.34, 0.05), focusMotionMask), 1.0);
                }
                if (_OceanDebugMode == 9)
                {
                    float signedWave = clamp(waveHeight / max(_WaveAmplitude * 0.55, 0.001), -1.0, 1.0);
                    float3 troughColor = float3(0.02, 0.06, 0.30);
                    float3 neutralColor = float3(0.45, 0.56, 0.62);
                    float3 crestColor = float3(1.0, 0.96, 0.74);
                    float3 debugColor = signedWave < 0.0
                        ? lerp(neutralColor, troughColor, -signedWave)
                        : lerp(neutralColor, crestColor, signedWave);
                    return half4(debugColor, 1.0);
                }
                if (_OceanDebugMode == 10)
                {
                    float slope = saturate(length(waveGradient) * _WaveNormalStrength * 3.0);
                    return half4(lerp(float3(0.02, 0.04, 0.06), float3(0.1, 1.0, 0.45), slope), 1.0);
                }
                if (_OceanDebugMode == 11)
                    return half4(depth01, shore01, oceanFactor, 1.0);
                if (_OceanDebugMode == 12)
                    return half4(lerp(float3(0.03, 0.05, 0.07), float3(0.02, 0.32, 1.0), absorption), 1.0);
                if (_OceanDebugMode == 18)
                    return half4(shoreFoam, whitecaps, wakeFoam, 1.0);
                if (_OceanDebugMode == 19)
                    return half4(alphaPreview, opticalAlpha, scenePath, 1.0);
                if (_OceanDebugMode == 22)
                {
                    float shoreContact = (1.0 - smoothstep(0.10, 0.52, shore01)) * sceneDepthValid;
                    float gapDebug = saturate(sceneGapMeters / max(_ShallowDepth * 0.75, 16.0));
                    return half4(shoreContact, shoreTerrainClearance, gapDebug, 1.0);
                }
                if (_OceanDebugMode == 23)
                {
                    float polishAlpha = saturate(alphaPreview - surfaceBaseAlpha);
                    return half4(alphaPreview, surfaceBaseAlpha, saturate(polishAlpha * 8.0), 1.0);
                }
                if (_OceanDebugMode == 32)
                    return half4(1.0, 0.0, 1.0, saturate((shoreFoam + whitecaps + wakeFoam) * _FoamColor.a));
                if (_OceanDebugMode == 51)
                    return half4(wakeMask, wakeFoam, saturate(length(wakeGradient) * _WakeNormalStrength * 22.0), 1.0);
                if (_OceanDebugMode == 52)
                    return half4(saturate(surfaceGlint * 0.22 + sunsetShimmer * 0.20), foamBlend, saturate(length(waveGradient) * _WaveNormalStrength * 2.4), 1.0);
                if (_OceanDebugMode == 53)
                    return half4(color, 1.0);
                if (_OceanDebugMode == 54)
                    return half4(foamBlend, saturate((surfaceGlint + sunsetShimmer) * 0.25), saturate(waveDetailAlpha * 8.0), 1.0);
                if (_OceanDebugMode == 55)
                    return half4(surfaceBaseAlpha, foamAlpha, saturate(glintAlpha + waveDetailAlpha), 1.0);

                return half4(color, alphaPreview);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 objectNormalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 waterData = saturate(input.color.rgb);
                float3 fromCenter = positionWS - _PlanetCenter;
                float3 planetNormalWS = dot(fromCenter, fromCenter) > 0.0001
                    ? normalize(fromCenter)
                    : normalize(objectNormalWS);

                float3 waveGradient;
                float crest;
                float waveHeight = EvaluateOceanWaves(positionWS, planetNormalWS, waterData.r, waterData.b, waveGradient, crest);
                float displacementMask = waterData.b
                    * smoothstep(0.32, 0.78, waterData.g)
                    * smoothstep(0.22, 0.58, waterData.r);
                if (_OceanFocusMode < 0.5)
                {
                    positionWS += planetNormalWS * (waveHeight * displacementMask * 0.18);
                }
                else
                {
                    float focusMotionMask = FocusMotionMask(waterData.r, waterData.g, waterData.b);
                    positionWS += planetNormalWS * (waveHeight * focusMotionMask * _WaterMotionStrength);
                }

                output.positionWS = positionWS;
                output.planetNormalWS = planetNormalWS;
                output.normalWS = planetNormalWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.waterData = waterData;
                return output;
            }

            half4 frag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float depth01 = saturate(input.waterData.r);
                float shore01 = saturate(input.waterData.g);
                float oceanFactor = saturate(input.waterData.b);
                float waterDepth = depth01 * max(_DeepDepth, 0.001);
                float frontFace01 = IS_FRONT_VFACE(frontFace, 1.0, 0.0);
                float backFace01 = 1.0 - frontFace01;
                float cameraRadius = length(_WorldSpaceCameraPos.xyz - _PlanetCenter);
                float underwaterCamera = step(cameraRadius, _PlanetRadius + 0.25);

                if (_OceanDebugMode == 49)
                {
                    clip(min(backFace01, underwaterCamera) - 0.5);
                    return half4(1.0, 0.0, 1.0, 0.86);
                }

                if (backFace01 > 0.5 && underwaterCamera <= 0.0)
                    clip(-1.0);

                if (_OceanDebugMode == 24
                    || _OceanDebugMode == 26
                    || _OceanDebugMode == 27
                    || _OceanDebugMode == 28
                    || _OceanDebugMode == 29
                    || _OceanDebugMode == 30
                    || _OceanDebugMode == 31
                    || _OceanDebugMode == 33
                    || _OceanDebugMode == 34
                    || _OceanDebugMode == 35
                    || _OceanDebugMode == 36
                    || _OceanDebugMode == 37
                    || _OceanDebugMode == 38
                    || _OceanDebugMode == 39
                    || _OceanDebugMode == 41)
                    return half4(0.0, 0.0, 0.0, 0.0);

                bool focusSurfaceDebug = (_OceanDebugMode >= 1 && _OceanDebugMode <= 12)
                    || _OceanDebugMode == 18
                    || _OceanDebugMode == 19
                    || _OceanDebugMode == 22
                    || _OceanDebugMode == 23
                    || _OceanDebugMode == 40
                    || _OceanDebugMode == 32
                    || _OceanDebugMode == 51
                    || _OceanDebugMode == 52
                    || _OceanDebugMode == 53
                    || _OceanDebugMode == 54
                    || _OceanDebugMode == 55
                    || _OceanDebugMode == 56;
                if ((_OceanDebugMode == 0 || _OceanDebugMode == 25 || focusSurfaceDebug) && _OceanFocusMode >= 0.5)
                    return RenderWaterFocus(input.positionWS, input.positionCS, input.planetNormalWS, depth01, shore01, oceanFactor);

                float3 waveGradient;
                float waveCrest;
                float oceanStorm = SampleOceanStorm(input.planetNormalWS, depth01, oceanFactor);
                float wind01 = WindStrength01();
                float seaState = OceanSeaState(wind01);
                float foamState = OceanFoamState(wind01, oceanStorm);
                float waveHeight = EvaluateOceanWaves(input.positionWS, input.planetNormalWS, depth01, oceanFactor, waveGradient, waveCrest);

                float depthForWaves = smoothstep(0.03, 0.22, depth01);
                float shoreWaveMask = smoothstep(0.04, 0.20, shore01);
                float3 rippleGradient = EvaluateRippleGradient(input.positionWS, input.planetNormalWS, depth01, oceanFactor);
                waveGradient += rippleGradient * lerp(0.45, 1.35, depthForWaves);

                float waveInfluence = lerp(0.46, 1.18, oceanFactor) * lerp(0.38, 1.0, depthForWaves) * lerp(0.88, 1.58, seaState);
                float3 normalWS = normalize(input.planetNormalWS - waveGradient * (_WaveNormalStrength * waveInfluence));
                normalWS = normalize(lerp(input.planetNormalWS, normalWS, saturate(0.34 + shoreWaveMask * 0.66)));

                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : float3(0, 1, 0);
                float localSun = dot(input.planetNormalWS, sunDir);
                float daylight = smoothstep(-0.08, 0.18, localSun);
                float ndotl = saturate(dot(normalWS, sunDir));
                float shadow = CloudShadowFactor(input.positionWS, sunDir, localSun);
                float litDay = daylight * lerp(1.0, shadow, daylight);

                float metricDepthBlend = smoothstep(_ShallowDepth, max(_DeepDepth, _ShallowDepth + 1.0), waterDepth);
                float encodedDepthBlend = smoothstep(0.035, 0.46, depth01);
                float depthBlend = pow(saturate(max(metricDepthBlend, encodedDepthBlend)), 0.72);
                float sceneDepthValid;
                float sceneGapMeters = WaterSceneGapMeters(input.positionCS, input.positionWS, sceneDepthValid);
                float scenePath = WaterScenePathFromGap01(sceneGapMeters, sceneDepthValid, depth01, shore01, oceanFactor);
                float shoreTerrainClearance = WaterSceneContactClearance01(sceneGapMeters);
                float absorption = WaterAbsorption01(depth01, shore01, oceanFactor, input.positionWS, input.planetNormalWS, viewDir, scenePath);
                float opticalDepthBlend = saturate(max(depthBlend, absorption * 1.05));
                float opticalAlpha = WaterOpticalAlpha(depth01, shore01, oceanFactor, input.positionWS, input.planetNormalWS, viewDir, scenePath);

                float3 shallowColor = _ShallowColor.rgb;
                float3 deepColor = _DeepColor.rgb;
                float3 waterColor = lerp(shallowColor, deepColor, opticalDepthBlend);
                waterColor = lerp(waterColor, lerp(shallowColor, deepColor, opticalDepthBlend * 0.55), 1.0 - oceanFactor);

                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), 3.2);
                float volumeSurfaceMode = saturate(_WaterVolumeEnabled);
                float surfaceReflectionFresnel = fresnel * lerp(1.0, 0.24, volumeSurfaceMode);
                float3 skyReflect = lerp(float3(0.012, 0.017, 0.024), float3(0.46, 0.64, 0.82), daylight);
                waterColor = lerp(waterColor, skyReflect, surfaceReflectionFresnel * lerp(0.08, 0.42, daylight));

                float nightLight = saturate(_NightAmbientIntensity * 0.12 + 0.006);
                float dayLight = 0.26 + ndotl * 0.74;
                float lightAmount = lerp(nightLight, dayLight, daylight);
                float3 litColor = waterColor * lightAmount;
                litColor *= lerp(1.0, shadow, daylight);

                float waveShade = (waveCrest - 0.35) * 0.18 * daylight * oceanFactor;
                litColor += waveShade.xxx;
                litColor *= WaterPathDarkening(absorption);

                float shoreWidth01 = saturate(_ShoreFoamDepth / max(_ShoreFoamSoftness, 0.001));
                float shoreBand = 1.0 - smoothstep(shoreWidth01 * 0.35, shoreWidth01, shore01);
                float runupBand = 1.0 - smoothstep(shoreWidth01, shoreWidth01 * 4.2, shore01);
                float shoreFoam = ComputeShoreFoam(input.positionWS, shore01) * _ShoreFoamIntensity;
                float runupFoam = runupBand * shoreFoam * 0.28;
                float movingRows = MovingWaveRows(input.positionWS, input.planetNormalWS, depth01, oceanFactor);
                float movingCrests = smoothstep(0.25, 0.82, movingRows);
                float openWaveMask = DeepWaterFactor(depth01) * smoothstep(0.18, 0.58, shore01) * oceanFactor;
                float whitecapNoise = ValueNoise3((input.positionWS - _PlanetCenter) * 0.028 + float3(_Time.y * 0.045, 0.0, -_Time.y * 0.038));
                float crestFoam = smoothstep(0.64, 0.95, waveCrest * 0.58 + movingCrests * 0.26 + whitecapNoise * 0.16)
                    * openWaveMask
                    * smoothstep(0.14, 0.74, foamState)
                    * 0.58
                    * _WhitecapIntensity;
                float foam = saturate(shoreFoam + runupFoam + crestFoam);
                if (_OceanDebugMode == 32)
                    return half4(1.0, 0.0, 1.0, saturate(foam * _FoamColor.a));

                float foamLight = litDay * (0.58 + ndotl * 0.42);
                float shoreContactVisibility = ShoreContactVisibility(shoreTerrainClearance, sceneDepthValid, shore01);
                float surfaceEdgeVisibility = UnderwaterShoreEdgeVisibility(input.positionWS, shore01) * shoreContactVisibility;
                float foamBlend = foam * _FoamColor.a * foamLight * surfaceEdgeVisibility;
                litColor = lerp(litColor, _FoamColor.rgb * lerp(0.62, 1.0, daylight), foamBlend);

                float3 reflectedSun = reflect(-sunDir, input.planetNormalWS);
                float macroAlign = saturate(dot(reflectedSun, viewDir));
                float compactPower = max(_SunGlitterPower, 64.0);
                float glintCore = pow(macroAlign, compactPower);
                float glintEnvelope = pow(macroAlign, max(compactPower * 0.18, 96.0));

                float3 reflectedRippleSun = reflect(-sunDir, normalWS);
                float rippleAlign = saturate(dot(reflectedRippleSun, viewDir));
                float sparkle = pow(rippleAlign, max(compactPower * 0.62, 96.0)) * glintEnvelope;
                float sparkleNoise = lerp(0.52, 1.35, ValueNoise3(input.positionWS * 0.058 + _Time.y * 0.08));
                float glint = (glintCore * 1.4 + sparkle * 0.11 * sparkleNoise)
                    * _SunGlitterIntensity
                    * litDay
                    * oceanFactor
                    * smoothstep(0.10, 0.36, depth01);
                litColor += glint * float3(1.0, 0.88, 0.55);

                if (_OceanDebugMode == 1)
                    return half4(lerp(float3(0.55, 1.0, 0.92), float3(0.0, 0.025, 0.16), depthBlend), 1.0);
                if (_OceanDebugMode == 2)
                    return half4(lerp(float3(0.02, 0.02, 0.025), float3(1.0, 0.92, 0.16), saturate(shoreBand + runupBand * 0.35)), 1.0);
                if (_OceanDebugMode == 3)
                    return half4(lerp(float3(0.86, 0.22, 0.70), float3(0.05, 0.85, 1.0), oceanFactor), 1.0);
                if (_OceanDebugMode == 4)
                    return half4(float3(daylight, shadow, lightAmount), 1.0);
                if (_OceanDebugMode == 5)
                    return half4(float3(saturate(glintCore * 80.0), saturate(sparkle * 12.0), saturate(glintEnvelope)), 1.0);
                if (_OceanDebugMode == 6)
                    return half4(normalWS * 0.5 + 0.5, 1.0);
                if (_OceanDebugMode == 7)
                    return half4(lerp(float3(0.02, 0.035, 0.05), _FoamColor.rgb, foam), 1.0);
                if (_OceanDebugMode == 18)
                    return half4(shoreFoam, runupFoam, crestFoam, 1.0);
                if (_OceanDebugMode == 8)
                {
                    float focusMotionMask = FocusMotionMask(depth01, shore01, oceanFactor);
                    return half4(lerp(float3(0.04, 0.02, 0.10), float3(1.0, 0.34, 0.05), focusMotionMask), 1.0);
                }
                if (_OceanDebugMode == 9)
                {
                    float signedWave = clamp(waveHeight / max(_WaveAmplitude * 0.55, 0.001), -1.0, 1.0);
                    float3 troughColor = float3(0.02, 0.06, 0.30);
                    float3 neutralColor = float3(0.45, 0.56, 0.62);
                    float3 crestColor = float3(1.0, 0.96, 0.74);
                    float3 debugColor = signedWave < 0.0
                        ? lerp(neutralColor, troughColor, -signedWave)
                        : lerp(neutralColor, crestColor, signedWave);
                    return half4(debugColor, 1.0);
                }
                if (_OceanDebugMode == 10)
                {
                    float slope = saturate(length(waveGradient) * _WaveNormalStrength * 3.0);
                    return half4(lerp(float3(0.02, 0.04, 0.06), float3(0.1, 1.0, 0.45), slope), 1.0);
                }
                if (_OceanDebugMode == 11)
                    return half4(depth01, shore01, oceanFactor, 1.0);
                if (_OceanDebugMode == 12)
                    return half4(lerp(float3(0.03, 0.05, 0.07), float3(0.02, 0.32, 1.0), absorption), 1.0);

                float grazingSilhouette = smoothstep(0.42, 0.88, fresnel);
                float volumeGrazingFade = lerp(1.0, lerp(1.0, 0.24, grazingSilhouette), volumeSurfaceMode);
                float baseSurfaceAlpha = WaterFinalAlpha(opticalAlpha, absorption) * surfaceEdgeVisibility * volumeGrazingFade;
                float fresnelAlpha = fresnel * lerp(0.10, 0.006, volumeSurfaceMode) * daylight * surfaceEdgeVisibility;
                float alpha = saturate(baseSurfaceAlpha + fresnelAlpha + foamBlend * 0.32);
                if (_OceanDebugMode == 19)
                    return half4(alpha, opticalAlpha, scenePath, 1.0);
                if (_OceanDebugMode == 22)
                {
                    float shoreContact = (1.0 - smoothstep(0.10, 0.52, shore01)) * sceneDepthValid;
                    float gapDebug = saturate(sceneGapMeters / max(_ShallowDepth * 0.75, 16.0));
                    return half4(shoreContact, shoreTerrainClearance, gapDebug, 1.0);
                }
                if (_OceanDebugMode == 23)
                    return half4(alpha, baseSurfaceAlpha, saturate(fresnelAlpha * 8.0), 1.0);

                litColor = MixFog(litColor, input.fogFactor);
                return half4(litColor, alpha);
            }
            ENDHLSL
        }
    }
}
