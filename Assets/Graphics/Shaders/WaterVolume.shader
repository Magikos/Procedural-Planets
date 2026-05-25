Shader "Hidden/WaterVolume"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.42, 0.82, 0.84, 0.22)
        _DeepColor ("Deep Color", Color) = (0.0, 0.02, 0.08, 1.0)
        _ShallowDepth ("Shallow Depth", Float) = 28
        _DeepDepth ("Deep Depth", Float) = 360
        _ShoreFoamSoftness ("Shore Range", Float) = 125
        _Alpha ("Alpha", Range(0, 1)) = 0.35
        _VolumeDensity ("Volume Density", Range(0.1, 4)) = 1.65
        _RefractionStrength ("Refraction Strength", Range(0, 1)) = 0.38
        _CausticIntensity ("Caustic Intensity", Range(0, 8)) = 0.42
        _CausticScale ("Caustic Scale", Float) = 0.052
        _CausticSpeed ("Caustic Speed", Range(0, 4)) = 0.75
        _CausticDepth ("Caustic Depth", Float) = 115
        _CausticContrast ("Caustic Contrast", Range(0.25, 8)) = 1.35
        _CausticPrismStrength ("Caustic Prism Strength", Range(0, 2)) = 0.46
        _CausticPrismEdgeBoost ("Caustic Prism Edge Boost", Float) = 18
    }

    HLSLINCLUDE
    #include "Includes/Common.hlsl"
    #include "Includes/Math.hlsl"
    #include "Includes/DebugModes.hlsl"
    #include "Includes/CloudShadows.hlsl"

    #define FORCE_WATER_LAYER_PROOF 0

    TEXTURE2D(_Source);
    SAMPLER(sampler_Source);
    TEXTURE2D(_WaterVolumeData);
    SAMPLER(sampler_WaterVolumeData);
    TEXTURE2D(_CameraDepthTexture);
    SAMPLER(sampler_CameraDepthTexture);

    float4 _ShallowColor;
    float4 _DeepColor;
    float _ShallowDepth;
    float _DeepDepth;
    float _Alpha;
    float _VolumeDensity;
    float _RefractionStrength;
    float _CausticIntensity;
    float _CausticScale;
    float _CausticSpeed;
    float _CausticDepth;
    float _CausticContrast;
    float _CausticPrismStrength;
    float _CausticPrismEdgeBoost;
    float3 _PlanetCenter;
    float _SeaLevelRadius;
    float3 _SunParams;
    float _SunIntensity;
    float3 _MoonParams;
    float _MoonIntensity;
    int _OceanDebugMode;

    struct Attributes
    {
        uint vertexID : SV_VertexID;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    struct CausticResult
    {
        float mask;
        float pattern;
        float depthFade;
        float pathFade;
        float sunLight;
        float moonLight;
        float light;
        float waterDepth;
        float waterPath;
        float3 contribution;
        float3 prismContribution;
    };

    struct BottomDistortionResult
    {
        float2 offsetUv;
        float mask;
        float valid;
        float strengthPixels;
        float3 color;
    };

    float WaterCoverageFromData(float4 waterData)
    {
        return smoothstep(0.0005, 0.018, max(saturate(waterData.g), saturate(waterData.b)));
    }

    float SceneDepthValid(float rawDepth)
    {
        #if UNITY_REVERSED_Z
            return step(0.0001, rawDepth);
        #else
            return 1.0 - step(0.9999, rawDepth);
        #endif
    }

    float3 ViewVectorFromUv(float2 uv)
    {
        float2 ndc = float2(uv.x * 2.0 - 1.0, uv.y * 2.0 - 1.0);
        float3 viewVector = mul(unity_CameraInvProjection, float4(ndc, 0.0, -1.0)).xyz;
        return mul(unity_CameraToWorld, float4(viewVector, 0.0)).xyz;
    }

    float3 SafeNormalize3(float3 value, float3 fallback)
    {
        float lenSq = dot(value, value);
        return lenSq > 0.0000001 ? value * rsqrt(lenSq) : fallback;
    }

    float CameraSeaOffset()
    {
        return length(_WorldSpaceCameraPos.xyz - _PlanetCenter) - _SeaLevelRadius;
    }

    float CameraUnderwater01()
    {
        return 1.0 - smoothstep(-1.5, 2.0, CameraSeaOffset());
    }

    float VolumeLayerVisibility()
    {
        float seaRadius = max(_SeaLevelRadius, 1.0);
        float cameraAltitude = max(CameraSeaOffset(), 0.0);
        float orbitalFade = 1.0 - smoothstep(seaRadius * 0.06, seaRadius * 0.42, cameraAltitude);
        return CameraSeaOffset() < 0.0 ? 1.0 : saturate(orbitalFade);
    }

    float3 UnderwaterNoDepthColor(float3 rayDir)
    {
        float3 cameraUp = SafeNormalize3(_WorldSpaceCameraPos.xyz - _PlanetCenter, float3(0.0, 1.0, 0.0));
        float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : cameraUp;
        float localSun = smoothstep(-0.08, 0.20, dot(cameraUp, sunDir));
        float viewUp = smoothstep(-0.35, 0.85, dot(rayDir, cameraUp));
        float light = saturate(localSun * saturate(_SunIntensity / 17.0));
        float3 deepWater = max(_DeepColor.rgb, float3(0.0, 0.024, 0.085));
        float3 litWater = lerp(float3(0.015, 0.115, 0.18), float3(0.08, 0.34, 0.46), light);
        return lerp(deepWater, litWater, viewUp * 0.65 + light * 0.25);
    }

    float3 ReceiverNormalFromDepth(float3 receiverWS, float3 rayDir, float3 fallback)
    {
        float3 dx = ddx(receiverWS);
        float3 dy = ddy(receiverWS);
        float3 normalWS = SafeNormalize3(cross(dy, dx), fallback);
        return dot(normalWS, -rayDir) < 0.0 ? -normalWS : normalWS;
    }

    float WaterPathToReceiver(float3 rayDir, float receiverDistance)
    {
        if (_SeaLevelRadius <= 0.0 || receiverDistance <= 0.0)
            return 0.0;

        float2 seaHit = RaySphere(_PlanetCenter, _SeaLevelRadius, _WorldSpaceCameraPos.xyz, rayDir);
        if (seaHit.y <= 0.0 || seaHit.x >= MAX_FLOAT * 0.5)
            return 0.0;

        float pathStart = max(seaHit.x, 0.0);
        float pathEnd = min(receiverDistance, seaHit.x + seaHit.y);
        return max(pathEnd - pathStart, 0.0);
    }

    float FarTerrainWaterlineMask(float3 receiverWS, float3 rayDir, float receiverDistance, float screenWaterCoverage, out float seaPath)
    {
        seaPath = 0.0;

        if (_SeaLevelRadius <= 0.0 || receiverDistance <= 0.0)
            return 0.0;

        float3 fromCenter = receiverWS - _PlanetCenter;
        float receiverRadius = length(fromCenter);
        if (receiverRadius <= 0.0001)
            return 0.0;

        float2 seaHit = RaySphere(_PlanetCenter, _SeaLevelRadius, _WorldSpaceCameraPos.xyz, rayDir);
        if (seaHit.y <= 0.0 || seaHit.x >= MAX_FLOAT * 0.5)
            return 0.0;

        float seaStart = max(seaHit.x, 0.0);
        float seaEnd = min(receiverDistance, seaHit.x + seaHit.y);
        seaPath = max(seaEnd - seaStart, 0.0);
        float seaBeforeTerrain = step(seaStart, receiverDistance - 0.25);
        float pathBeforeTerrain = smoothstep(max(_ShallowDepth * 0.08, 1.0), max(_ShallowDepth * 1.5, 18.0), seaPath);

        float3 planetUp = fromCenter / receiverRadius;
        float grazingView = 1.0 - smoothstep(0.08, 0.55, abs(dot(rayDir, planetUp)));
        float lineOfSightWater = max(saturate(screenWaterCoverage), pathBeforeTerrain);
        return saturate(seaBeforeTerrain * pathBeforeTerrain * lineOfSightWater * lerp(0.40, 1.0, grazingView));
    }

    float3 FarTerrainWaterlineColor(float3 sourceColor, float seaPath, float mask, float volumeLight)
    {
        float pathTint = saturate(1.0 - exp(-seaPath / max(_DeepDepth * 0.30, 1.0)));
        float extinction = saturate(1.0 - exp(-seaPath / max(_DeepDepth * 0.22, 1.0)));
        float3 shallowTint = lerp(_ShallowColor.rgb, float3(0.16, 0.58, 0.68), 0.35);
        float3 deepTint = max(_DeepColor.rgb, float3(0.0, 0.028, 0.105));
        float3 waterTint = lerp(shallowTint, deepTint, pathTint);
        float lightScale = lerp(0.34, 0.82, volumeLight);
        float3 attenuatedSource = sourceColor * exp(-float3(3.80, 1.75, 0.58) * extinction);
        float3 waterColor = lerp(attenuatedSource, waterTint * lightScale, saturate(extinction * 0.92));
        return lerp(sourceColor, waterColor, saturate(mask));
    }

    float2 Hash22(float2 p)
    {
        return frac(sin(float2(
            dot(p, float2(127.1, 311.7)),
            dot(p, float2(269.5, 183.3)))) * 43758.5453);
    }

    float CausticVoronoi(float2 uv, float time)
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
                float2 animated = 0.5 + 0.38 * sin(hash * MATH_TAU + time * float2(0.83, 1.17) + phase);
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
        float core = 1.0 - smoothstep(0.035, 0.13, edgeDistance);
        float halo = 1.0 - smoothstep(0.10, 0.34, edgeDistance);
        return saturate(core * 0.34 + halo * 0.22);
    }

    float CausticPatternUv(float2 uv, float time)
    {
        float2 waveWarp = float2(
            sin(dot(uv, float2(0.73, 1.27)) + time * 0.42),
            sin(dot(uv, float2(-1.11, 0.91)) - time * 0.37)) * 0.18;
        float2 warpedUv = uv + waveWarp;
        float baseLayer = CausticVoronoi(warpedUv + float2(time * 0.17, -time * 0.09), time);
        float detailLayer = CausticVoronoi(warpedUv * 1.63 + float2(-time * 0.11, time * 0.15), time * 1.27);
        float caustic = saturate(baseLayer * 0.86 + detailLayer * 0.38 + baseLayer * detailLayer * 0.18);
        caustic = smoothstep(0.10, 0.58, caustic);
        return pow(caustic, max(_CausticContrast, 0.05));
    }

    float CausticPattern(float3 worldPos, float3 planetUp)
    {
        float3 local = (worldPos - _PlanetCenter) * max(_CausticScale, 0.0001);
        float3 weights = pow(abs(planetUp), 4.0);
        weights /= max(weights.x + weights.y + weights.z, 0.0001);

        float time = _GameTime * _CausticSpeed;
        float causticX = CausticPatternUv(local.yz + float2(11.37, -4.91), time);
        float causticY = CausticPatternUv(local.zx + float2(-6.53, 8.24), time * 1.03);
        float causticZ = CausticPatternUv(local.xy + float2(3.19, 13.72), time * 0.97);
        return causticX * weights.x + causticY * weights.y + causticZ * weights.z;
    }

    float3 CausticPrismColor(float pattern)
    {
        if (_CausticPrismStrength <= 0.0)
            return 0.0;

        float2 gradient = float2(ddx(pattern), ddy(pattern));
        float gradientLength = length(gradient);
        float edge = saturate(gradientLength * max(_CausticPrismEdgeBoost, 0.0));
        float2 gradientDir = gradient * rsqrt(max(dot(gradient, gradient), 0.000001));
        float ridgeMask = smoothstep(0.03, 0.42, pattern);
        float side = dot(gradientDir, float2(0.7415, -0.6709));
        float redSide = smoothstep(-0.12, 0.92, side);
        float blueSide = smoothstep(-0.12, 0.92, -side);
        float greenCenter = saturate(1.0 - abs(side) * 1.18);

        float3 redFringe = float3(1.00, 0.22, 0.06) * redSide;
        float3 greenFringe = float3(0.08, 0.95, 0.20) * greenCenter * 0.70;
        float3 blueFringe = float3(0.16, 0.36, 1.00) * blueSide;
        return (redFringe + greenFringe + blueFringe) * edge * ridgeMask;
    }

    float2 BottomDistortionField(float3 worldPos)
    {
        float3 local = (worldPos - _PlanetCenter) * 0.0125;
        float time = _GameTime * 0.42;
        float wave0 = sin(dot(local.xy, float2(1.37, 0.61)) + time * 1.18);
        float wave1 = sin(dot(local.yz, float2(-0.74, 1.52)) - time * 0.91);
        float wave2 = sin(dot(local.zx, float2(1.91, -0.83)) + time * 0.67);
        float wave3 = sin(dot(local.xy + local.zz, float2(-1.12, 1.76)) - time * 1.31);
        return float2(wave0 + wave2 * 0.55, wave1 - wave3 * 0.45) * 0.46;
    }

    float VolumeOpticalDepth(CausticResult caustics)
    {
        float depthRange = max(_DeepDepth - _ShallowDepth, 1.0);
        float depth01 = saturate((caustics.waterDepth - _ShallowDepth * 0.35) / depthRange);
        float path01 = saturate(caustics.waterPath / max(_DeepDepth * 0.55, 1.0));
        float density = max(_VolumeDensity, 0.001);
        float shallowRelease = smoothstep(0.015, 0.12, caustics.waterPath);
        float opticalDepth = depth01 * 0.85 + path01 * 1.95 + depth01 * path01 * 1.40;
        return opticalDepth * density * shallowRelease;
    }

    float3 VolumeBodyTransmittance(CausticResult caustics)
    {
        float opticalDepth = VolumeOpticalDepth(caustics);
        float3 absorption = float3(4.85, 2.05, 0.72);
        return exp(-absorption * opticalDepth);
    }

    float3 VolumeBodyTint(CausticResult caustics)
    {
        float opticalDepth = VolumeOpticalDepth(caustics);
        float deepMix = saturate(1.0 - exp(-opticalDepth * 1.08));
        float3 shallowTint = lerp(_ShallowColor.rgb, float3(0.22, 0.76, 0.82), 0.35);
        float3 deepTint = max(_DeepColor.rgb, float3(0.0, 0.028, 0.105));
        return lerp(shallowTint, deepTint, deepMix);
    }

    float VolumeBodyOpacity(CausticResult caustics)
    {
        float opticalDepth = VolumeOpticalDepth(caustics);
        float opticalOpacity = saturate(1.0 - exp(-opticalDepth * 1.72));
        float pathGate = smoothstep(0.02, 0.70, caustics.waterPath);
        return saturate(opticalOpacity * max(_Alpha, 0.65) * caustics.mask * pathGate);
    }

    float VolumeDepthFog(CausticResult caustics)
    {
        float pathFog = 1.0 - exp(-caustics.waterPath / max(_DeepDepth * 0.26, 1.0));
        float depthFog = 1.0 - exp(-caustics.waterDepth / max(_DeepDepth * 0.44, 1.0));
        return saturate(max(pathFog * 0.78, depthFog * 0.48) * caustics.mask);
    }

    float VolumeBodyLight(CausticResult caustics)
    {
        // Absorption can happen in darkness, but blue volume scatter needs light.
        return saturate(caustics.light * 1.28 + 0.012);
    }

    BottomDistortionResult EmptyBottomDistortionResult(float3 sourceColor)
    {
        BottomDistortionResult result;
        result.offsetUv = 0.0;
        result.mask = 0.0;
        result.valid = 0.0;
        result.strengthPixels = 0.0;
        result.color = sourceColor;
        return result;
    }

    BottomDistortionResult ComputeBottomDistortion(float2 uv, float3 sourceColor, float3 receiverWS, CausticResult caustics, float debugScale)
    {
        BottomDistortionResult result = EmptyBottomDistortionResult(sourceColor);
        if (_RefractionStrength <= 0.0 || caustics.mask <= 0.0)
            return result;

        float pathRamp = smoothstep(0.08, 0.85, caustics.waterPath);
        float baseMask = caustics.mask * caustics.depthFade * pathRamp;
        if (baseMask <= 0.0)
            return result;

        float2 field = BottomDistortionField(receiverWS);
        float shallowStrength = lerp(1.0, 0.45, saturate(caustics.waterDepth / max(_CausticDepth, 1.0)));
        float pixelStrength = _RefractionStrength * lerp(2.0, 7.0, pathRamp) * shallowStrength * baseMask * debugScale;
        float2 offsetUv = field * pixelStrength / max(_ScreenParams.xy, float2(1.0, 1.0));
        float2 refractedUv = clamp(uv + offsetUv, float2(0.001, 0.001), float2(0.999, 0.999));

        float rawRefractedDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, refractedUv).r;
        float depthValid = SceneDepthValid(rawRefractedDepth);
        float3 refractedViewVector = ViewVectorFromUv(refractedUv);
        float refractedViewLength = max(length(refractedViewVector), 0.0001);
        float3 refractedRayDir = refractedViewVector / refractedViewLength;
        float refractedDistance = LinearEyeDepth(rawRefractedDepth, _ZBufferParams) * refractedViewLength;
        float3 refractedReceiverWS = _WorldSpaceCameraPos.xyz + refractedRayDir * refractedDistance;

        float refractedRadius = length(refractedReceiverWS - _PlanetCenter);
        float refractedWaterDepth = _SeaLevelRadius - refractedRadius;
        float refractedUnderwater = smoothstep(0.05, 1.25, refractedWaterDepth);
        float refractedWaterPath = WaterPathToReceiver(refractedRayDir, refractedDistance);
        float refractedPathMask = smoothstep(0.02, 0.75, refractedWaterPath);
        float sampleValid = depthValid * refractedUnderwater * refractedPathMask;

        result.offsetUv = offsetUv * sampleValid;
        result.mask = baseMask * sampleValid;
        result.valid = sampleValid;
        result.strengthPixels = length(result.offsetUv * _ScreenParams.xy);
        result.color = lerp(sourceColor, SAMPLE_TEXTURE2D(_Source, sampler_Source, refractedUv).rgb, sampleValid);
        return result;
    }

    CausticResult EmptyCausticResult()
    {
        CausticResult result;
        result.mask = 0.0;
        result.pattern = 0.0;
        result.depthFade = 0.0;
        result.pathFade = 0.0;
        result.sunLight = 0.0;
        result.moonLight = 0.0;
        result.light = 0.0;
        result.waterDepth = 0.0;
        result.waterPath = 0.0;
        result.contribution = 0.0;
        result.prismContribution = 0.0;
        return result;
    }

    CausticResult ComputeReceiverCaustics(float3 receiverWS, float3 rayDir, float receiverDistance, float screenWaterCoverage)
    {
        CausticResult result = EmptyCausticResult();
        if (_SeaLevelRadius <= 0.0)
            return result;

        float3 fromCenter = receiverWS - _PlanetCenter;
        float receiverRadius = length(fromCenter);
        if (receiverRadius <= 0.0001)
            return result;

        float waterDepth = _SeaLevelRadius - receiverRadius;
        float receiverUnderwater = smoothstep(0.05, 1.25, waterDepth);
        if (receiverUnderwater <= 0.0)
            return result;

        float waterPath = WaterPathToReceiver(rayDir, receiverDistance);
        float pathMask = smoothstep(0.02, 0.75, waterPath);
        float coverage = max(saturate(screenWaterCoverage), pathMask);
        float mask = receiverUnderwater * coverage;
        if (mask <= 0.0)
            return result;

        float3 planetUp = fromCenter / receiverRadius;
        float3 receiverNormal = ReceiverNormalFromDepth(receiverWS, rayDir, planetUp);
        float depthFade = exp(-waterDepth / max(_CausticDepth, 1.0));
        float pathFade = exp(-waterPath / max(_DeepDepth * 0.85, 1.0));

        float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : float3(0.0, 1.0, 0.0);
        float localSun = smoothstep(0.025, 0.22, dot(planetUp, sunDir));
        float sunFacing = smoothstep(0.04, 0.55, dot(receiverNormal, sunDir));
        float sunShadow = CloudShadowFactor(receiverWS, sunDir, localSun);
        float sunIntensity01 = saturate(_SunIntensity / 17.0);
        float sunLight = localSun * sunFacing * sunShadow * sunIntensity01;

        float3 moonDir = dot(_MoonParams, _MoonParams) > 0.0001 ? normalize(_MoonParams) : float3(0.0, -1.0, 0.0);
        float localMoon = smoothstep(0.08, 0.34, dot(planetUp, moonDir));
        float moonFacing = smoothstep(0.10, 0.62, dot(receiverNormal, moonDir));
        float moonShadow = CloudShadowFactor(receiverWS, moonDir, localMoon);
        float moonLight = localMoon * moonFacing * moonShadow * saturate(_MoonIntensity);

        float light = saturate(sunLight + moonLight);
        if (light <= 0.0001)
        {
            result.mask = mask;
            result.depthFade = depthFade;
            result.pathFade = pathFade;
            result.waterDepth = waterDepth;
            result.waterPath = waterPath;
            return result;
        }

        float pattern = CausticPattern(receiverWS, planetUp);
        float shallow01 = saturate(waterDepth / max(_CausticDepth, 1.0));
        float3 causticColor = lerp(float3(1.0, 0.93, 0.72), float3(0.42, 0.80, 0.95), shallow01);
        float prismMask = saturate(mask * depthFade * pathFade * light);
        float3 prismColor = CausticPrismColor(pattern);
        float prismPresence = saturate(max(prismColor.r, max(prismColor.g, prismColor.b)) * _CausticPrismStrength);
        float whitePattern = pattern * (1.0 - prismPresence * 0.42);
        float intensity = whitePattern * _CausticIntensity * mask * depthFade * pathFade * light;
        float3 prismContribution = prismColor * _CausticPrismStrength * prismMask * (0.55 + pattern * 0.75);

        result.mask = mask;
        result.pattern = pattern;
        result.depthFade = depthFade;
        result.pathFade = pathFade;
        result.sunLight = sunLight;
        result.moonLight = moonLight;
        result.light = light;
        result.waterDepth = waterDepth;
        result.waterPath = waterPath;
        result.contribution = causticColor * intensity;
        result.prismContribution = prismContribution;
        return result;
    }

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    float4 Frag(Varyings input) : SV_Target
    {
        float4 source = SAMPLE_TEXTURE2D(_Source, sampler_Source, input.uv);

        if (_OceanDebugMode == DEBUG_WATER_OFF || _OceanDebugMode == DEBUG_SURFACE_ONLY)
            return source;

        #if FORCE_WATER_LAYER_PROOF
        {
            float rawDepthProof = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, input.uv).r;
            float4 waterDataProof = SAMPLE_TEXTURE2D(_WaterVolumeData, sampler_WaterVolumeData, input.uv);
            float waterMaskProof = WaterCoverageFromData(waterDataProof);

            if (SceneDepthValid(rawDepthProof) > 0.0)
            {
                float3 viewVectorProof = ViewVectorFromUv(input.uv);
                float viewLengthProof = max(length(viewVectorProof), 0.0001);
                float3 rayDirProof = viewVectorProof / viewLengthProof;
                float receiverDistanceProof = LinearEyeDepth(rawDepthProof, _ZBufferParams) * viewLengthProof;
                float3 receiverWSProof = _WorldSpaceCameraPos.xyz + rayDirProof * receiverDistanceProof;
                CausticResult causticsProof = ComputeReceiverCaustics(receiverWSProof, rayDirProof, receiverDistanceProof, waterMaskProof);
                waterMaskProof = max(waterMaskProof, causticsProof.mask);
            }

            float waterMask = step(0.001, waterMaskProof);
            float3 outsideColor = _OceanDebugMode == DEBUG_OFF ? source.rgb : float3(0.0, 0.0, 0.0);
            return float4(lerp(outsideColor, float3(0.0, 0.0, 1.0), waterMask), 1.0);
        }
        #endif

        float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, input.uv).r;
        bool causticDebug = _OceanDebugMode == DEBUG_CAUSTICS_MASK
            || _OceanDebugMode == DEBUG_CAUSTICS_ONLY
            || _OceanDebugMode == DEBUG_CAUSTICS_LIGHT
            || _OceanDebugMode == DEBUG_CAUSTICS_PRISM;
        bool bottomDistortionDebug = _OceanDebugMode == DEBUG_BOTTOM_DISTORTION_ONLY
            || _OceanDebugMode == DEBUG_BOTTOM_DISTORTION_VECTOR
            || _OceanDebugMode == DEBUG_VOLUME_REFRACTION;
        bool volumeBodyDebug = _OceanDebugMode == DEBUG_VOLUME_ONLY
            || _OceanDebugMode == DEBUG_VOLUME_CONTRIBUTION;

        float3 viewVector = ViewVectorFromUv(input.uv);
        float viewLength = max(length(viewVector), 0.0001);
        float3 rayDir = viewVector / viewLength;

        if (SceneDepthValid(rawDepth) <= 0.0)
        {
            if (CameraUnderwater01() > 0.01 && (_OceanDebugMode == DEBUG_OFF || volumeBodyDebug))
                return float4(UnderwaterNoDepthColor(rayDir), 1.0);

            return (causticDebug || bottomDistortionDebug || volumeBodyDebug) ? float4(0.0, 0.0, 0.0, 1.0) : source;
        }

        float4 waterData = SAMPLE_TEXTURE2D(_WaterVolumeData, sampler_WaterVolumeData, input.uv);
        float screenWaterCoverage = WaterCoverageFromData(waterData);
        float receiverDistance = LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;
        float3 receiverWS = _WorldSpaceCameraPos.xyz + rayDir * receiverDistance;

        CausticResult caustics = ComputeReceiverCaustics(receiverWS, rayDir, receiverDistance, screenWaterCoverage);
        float layerVisibility = VolumeLayerVisibility();
        caustics.mask *= layerVisibility;
        caustics.contribution *= layerVisibility;
        caustics.prismContribution *= layerVisibility;
        float farTerrainWaterlinePath;
        float farTerrainWaterlineMask = FarTerrainWaterlineMask(receiverWS, rayDir, receiverDistance, screenWaterCoverage, farTerrainWaterlinePath) * layerVisibility;
        farTerrainWaterlinePath *= layerVisibility;
        float distortionDebugScale = bottomDistortionDebug ? 3.0 : 1.0;
        BottomDistortionResult bottomDistortion = ComputeBottomDistortion(
            input.uv,
            source.rgb,
            receiverWS,
            caustics,
            distortionDebugScale);

        if (_OceanDebugMode == DEBUG_VOLUME_MASK || _OceanDebugMode == DEBUG_VOLUME_OCCLUSION)
            return float4(caustics.mask, screenWaterCoverage, saturate(caustics.waterPath / max(_DeepDepth, 1.0)), 1.0);

        if (_OceanDebugMode == DEBUG_VOLUME_PATH)
            return float4(
                saturate(caustics.waterDepth / max(_CausticDepth, 1.0)),
                saturate(caustics.waterPath / max(_DeepDepth, 1.0)),
                caustics.mask,
                1.0);

        if (_OceanDebugMode == DEBUG_VOLUME_LIGHT)
            return float4(caustics.sunLight, caustics.moonLight, caustics.light, 1.0);

        if (_OceanDebugMode == DEBUG_BOTTOM_DISTORTION_VECTOR || _OceanDebugMode == DEBUG_VOLUME_REFRACTION)
        {
            float2 pixelOffset = bottomDistortion.offsetUv * _ScreenParams.xy;
            float3 vectorColor = float3(saturate(pixelOffset * 0.15 + 0.5), bottomDistortion.mask);
            return float4(lerp(float3(0.0, 0.0, 0.0), vectorColor, saturate(bottomDistortion.mask * 2.0)), 1.0);
        }

        if (_OceanDebugMode == DEBUG_BOTTOM_DISTORTION_ONLY)
        {
            float distortionHeat = saturate(bottomDistortion.strengthPixels / 6.0);
            float3 proofColor = lerp(bottomDistortion.color, float3(0.25, 0.72, 1.0), distortionHeat * 0.22);
            return float4(lerp(float3(0.0, 0.0, 0.0), proofColor, saturate(bottomDistortion.mask * 1.45)), 1.0);
        }

        if (_OceanDebugMode == DEBUG_CAUSTICS_MASK)
            return float4(caustics.mask, caustics.depthFade, caustics.pathFade, 1.0);

        if (_OceanDebugMode == DEBUG_CAUSTICS_LIGHT)
            return float4(caustics.sunLight, caustics.moonLight, caustics.light, 1.0);

        if (_OceanDebugMode == DEBUG_CAUSTICS_ONLY)
        {
            float proof = caustics.pattern * caustics.mask * max(caustics.light, 0.15);
            return float4(saturate(proof * float3(0.90, 1.00, 0.86) * 2.0), 1.0);
        }

        if (_OceanDebugMode == DEBUG_CAUSTICS_PRISM)
        {
            float3 prismProof = caustics.prismContribution * 8.0;
            return float4(saturate(prismProof), 1.0);
        }

        float causticMask = caustics.mask * caustics.depthFade * caustics.pathFade * caustics.light;
        float troughShadow = saturate((1.0 - caustics.pattern) * causticMask * 0.025);
        float bottomDistortionBlend = _OceanDebugMode == DEBUG_VOLUME_NO_REFRACTION
            ? 0.0
            : saturate(bottomDistortion.mask * 0.58);
        float3 baseColor = lerp(source.rgb, bottomDistortion.color, bottomDistortionBlend);
        float volumeOpacity = VolumeBodyOpacity(caustics);
        float3 volumeTint = VolumeBodyTint(caustics);
        float3 volumeTransmittance = VolumeBodyTransmittance(caustics);
        float volumeLight = VolumeBodyLight(caustics);

        if (_OceanDebugMode == DEBUG_VOLUME_ONLY || _OceanDebugMode == DEBUG_VOLUME_CONTRIBUTION)
        {
            float3 volumeOnlyColor = saturate(volumeTint * (0.18 + volumeOpacity * 1.55) * volumeLight * caustics.mask);
            float3 waterlineOnlyColor = FarTerrainWaterlineColor(source.rgb, farTerrainWaterlinePath, farTerrainWaterlineMask, volumeLight);
            volumeOnlyColor = lerp(volumeOnlyColor, waterlineOnlyColor, smoothstep(0.02, 0.28, farTerrainWaterlineMask));
            return float4(volumeOnlyColor, 1.0);
        }

        float3 transmittedScene = baseColor * lerp(float3(1.0, 1.0, 1.0), volumeTransmittance, caustics.mask);
        float lostLight = saturate(1.0 - dot(volumeTransmittance, float3(0.2126, 0.7152, 0.0722)));
        float scatterAmount = saturate(volumeOpacity * lostLight * 0.82 * volumeLight);
        float3 waterBody = transmittedScene + volumeTint * scatterAmount;
        float depthFog = VolumeDepthFog(caustics);
        float3 fogColor = volumeTint * lerp(0.14, 0.62, volumeLight);
        waterBody = lerp(waterBody, fogColor, depthFog * 0.58);
        float3 color = waterBody * (1.0 - troughShadow) + caustics.contribution * 0.48 + caustics.prismContribution * 1.28;
        color = FarTerrainWaterlineColor(color, farTerrainWaterlinePath, farTerrainWaterlineMask, volumeLight);
        return float4(saturate(color), source.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "WaterVolumeComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.0
            ENDHLSL
        }
    }
}
