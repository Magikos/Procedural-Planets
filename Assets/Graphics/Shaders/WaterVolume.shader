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
    }

    HLSLINCLUDE
    #include "Includes/Common.hlsl"

    TEXTURE2D(_CameraDepthTexture);
    SAMPLER(sampler_CameraDepthTexture);
    TEXTURE2D(_Source);
    SAMPLER(sampler_Source);
    TEXTURE2D(_WaterVolumeData);
    SAMPLER(sampler_WaterVolumeData);

    float4 _ShallowColor;
    float4 _DeepColor;
    float4 _Time;
    float _ShallowDepth;
    float _DeepDepth;
    float _ShoreFoamSoftness;
    float _Alpha;
    float _VolumeDensity;
    float _RefractionStrength;

    float3 _PlanetCenter;
    float _PlanetRadius;
    float3 _SunParams;
    float _NightAmbientIntensity;
    int _OceanDebugMode;

    struct Attributes
    {
        uint vertexID : SV_VertexID;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 viewVector : TEXCOORD1;
    };

    float SceneDepthValid(float rawDepth)
    {
        #if UNITY_REVERSED_Z
            return step(0.0001, rawDepth);
        #else
            return 1.0 - step(0.9999, rawDepth);
        #endif
    }

    float Hash12(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float3 ContributionHeat(float3 delta, float scale)
    {
        float intensity = saturate(dot(abs(delta), float3(0.2126, 0.7152, 0.0722)) * scale);
        float lowToMid = smoothstep(0.02, 0.35, intensity);
        float midToHigh = smoothstep(0.35, 0.90, intensity);
        float3 color = lerp(float3(0.0, 0.0, 0.0), float3(0.0, 0.72, 1.0), lowToMid);
        color = lerp(color, float3(1.0, 0.34, 0.0), midToHigh);
        color += smoothstep(0.82, 1.0, intensity) * 0.25;
        return saturate(color);
    }

    float ValueNoise2(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        float2 u = f * f * (3.0 - 2.0 * f);
        float a = Hash12(i);
        float b = Hash12(i + float2(1.0, 0.0));
        float c = Hash12(i + float2(0.0, 1.0));
        float d = Hash12(i + float2(1.0, 1.0));
        return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
    }

    float3 WaterNormalizeSafe(float3 value, float3 fallback)
    {
        float lengthSq = dot(value, value);
        if (lengthSq <= 0.00000001)
            return fallback;

        return value * rsqrt(lengthSq);
    }

    float2 RefractionOffset(float2 uv, float strengthMask, float viewPath01)
    {
        float2 pixel = uv * _ScreenParams.xy;
        float t = _Time.y;

        float wave0 = sin(dot(pixel, float2(0.042, 0.016)) + t * 0.82);
        float wave1 = sin(dot(pixel, float2(-0.020, 0.052)) - t * 1.17);
        float wave2 = sin(dot(pixel, float2(0.076, -0.031)) + t * 0.54);
        float2 normalish = float2(wave0 + wave2 * 0.45, wave1 - wave2 * 0.35) * 0.5;

        float strength = _RefractionStrength * 0.006 * smoothstep(0.035, 0.55, viewPath01) * strengthMask;
        return normalish * strength;
    }

    float WaterCoverageFromData(float4 waterData)
    {
        return smoothstep(0.0005, 0.018, max(saturate(waterData.g), saturate(waterData.b)));
    }

    float WaterCoverageAt(float2 uv)
    {
        return WaterCoverageFromData(SAMPLE_TEXTURE2D(_WaterVolumeData, sampler_WaterVolumeData, uv));
    }

    void AccumulateBestWaterData(float2 uv, inout float4 bestData, inout float bestCoverage)
    {
        float4 candidate = SAMPLE_TEXTURE2D(_WaterVolumeData, sampler_WaterVolumeData, uv);
        float candidateCoverage = WaterCoverageFromData(candidate);
        if (candidateCoverage > bestCoverage)
        {
            bestData = candidate;
            bestCoverage = candidateCoverage;
        }
    }

    float4 WaterExpandedData(float2 uv, out float centerCoverage, out float expandedCoverage)
    {
        float4 centerData = SAMPLE_TEXTURE2D(_WaterVolumeData, sampler_WaterVolumeData, uv);
        centerCoverage = WaterCoverageFromData(centerData);

        float4 bestData = centerData;
        float bestCoverage = centerCoverage;
        float2 texel = 1.0 / max(_ScreenParams.xy, float2(1.0, 1.0));

        AccumulateBestWaterData(uv + float2(texel.x, 0.0), bestData, bestCoverage);
        AccumulateBestWaterData(uv - float2(texel.x, 0.0), bestData, bestCoverage);
        AccumulateBestWaterData(uv + float2(0.0, texel.y), bestData, bestCoverage);
        AccumulateBestWaterData(uv - float2(0.0, texel.y), bestData, bestCoverage);
        AccumulateBestWaterData(uv + texel, bestData, bestCoverage);
        AccumulateBestWaterData(uv - texel, bestData, bestCoverage);
        AccumulateBestWaterData(uv + float2(texel.x, -texel.y), bestData, bestCoverage);
        AccumulateBestWaterData(uv + float2(-texel.x, texel.y), bestData, bestCoverage);

        expandedCoverage = bestCoverage;
        return bestData;
    }

    float WaterScreenEdgeFade(float2 uv, float centerCoverage)
    {
        float2 texel = 1.0 / max(_ScreenParams.xy, float2(1.0, 1.0));
        float nearCoverage = centerCoverage;
        nearCoverage = min(nearCoverage, WaterCoverageAt(uv + float2(texel.x, 0.0)));
        nearCoverage = min(nearCoverage, WaterCoverageAt(uv - float2(texel.x, 0.0)));
        nearCoverage = min(nearCoverage, WaterCoverageAt(uv + float2(0.0, texel.y)));
        nearCoverage = min(nearCoverage, WaterCoverageAt(uv - float2(0.0, texel.y)));

        float2 wideTexel = texel * 2.0;
        float wideCoverage = centerCoverage;
        wideCoverage = min(wideCoverage, WaterCoverageAt(uv + float2(wideTexel.x, 0.0)));
        wideCoverage = min(wideCoverage, WaterCoverageAt(uv - float2(wideTexel.x, 0.0)));
        wideCoverage = min(wideCoverage, WaterCoverageAt(uv + float2(0.0, wideTexel.y)));
        wideCoverage = min(wideCoverage, WaterCoverageAt(uv - float2(0.0, wideTexel.y)));

        float nearFade = smoothstep(0.10, 0.95, nearCoverage);
        float wideFade = lerp(0.58, 1.0, smoothstep(0.10, 0.95, wideCoverage));
        return saturate(nearFade * wideFade);
    }

    float SeaSphereIntersections(float3 originWS, float3 viewDir, out float entryDistance, out float exitDistance)
    {
        entryDistance = 0.0;
        exitDistance = 0.0;

        float3 origin = originWS - _PlanetCenter;
        float b = dot(origin, viewDir);
        float c = dot(origin, origin) - _PlanetRadius * _PlanetRadius;
        float h = b * b - c;
        if (h <= 0.0)
            return 0.0;

        float root = sqrt(h);
        float nearHit = -b - root;
        float farHit = -b + root;
        if (farHit <= 0.0)
            return 0.0;

        entryDistance = max(nearHit, 0.0);
        exitDistance = max(farHit, 0.0);
        return 1.0;
    }

    float SeaSphereExitDistance(float3 originWS, float3 viewDir)
    {
        float entryDistance;
        float exitDistance;
        float hit = SeaSphereIntersections(originWS, viewDir, entryDistance, exitDistance);
        return hit > 0.0 ? exitDistance : 0.0;
    }

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);
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

    float4 Frag(Varyings input) : SV_Target
    {
        if ((_OceanDebugMode > 0 && _OceanDebugMode <= 11)
            || _OceanDebugMode == 18
            || _OceanDebugMode == 19
            || _OceanDebugMode == 22
            || _OceanDebugMode == 23
            || _OceanDebugMode == 25
            || _OceanDebugMode == 26
            || _OceanDebugMode == 32
            || _OceanDebugMode == 34)
            return SAMPLE_TEXTURE2D(_Source, sampler_Source, input.uv);

        float centerWaterCoverage;
        float expandedWaterCoverage;
        float4 waterData = WaterExpandedData(input.uv, centerWaterCoverage, expandedWaterCoverage);
        float rawSceneDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, input.uv).r;
        float sceneValid = SceneDepthValid(rawSceneDepth);
        float sceneForwardDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
        float viewLength = max(length(input.viewVector), 0.0001);
        float sceneRayDistance = sceneForwardDepth * viewLength;
        float3 viewDir = WaterNormalizeSafe(input.viewVector, float3(0.0, 0.0, 1.0));
        float cameraRadius = length(_WorldSpaceCameraPos.xyz - _PlanetCenter);
        float3 cameraUp = WaterNormalizeSafe(_WorldSpaceCameraPos.xyz - _PlanetCenter, float3(0.0, 1.0, 0.0));
        float cameraSeaOffset = cameraRadius - _PlanetRadius;
        float underwater = 1.0 - smoothstep(-0.25, 1.50, cameraSeaOffset);
        float nearSurfaceBand = max(_PlanetRadius * 0.022, 120.0);
        float orbitFadeBand = max(_PlanetRadius * 0.24, 1200.0);
        float surfaceProximity01 = 1.0 - smoothstep(nearSurfaceBand, orbitFadeBand, abs(cameraSeaOffset));
        float seaEntryDistance;
        float seaExitDistance;
        float seaIntersects = SeaSphereIntersections(_WorldSpaceCameraPos.xyz, viewDir, seaEntryDistance, seaExitDistance);
        float seaEntryForwardDepth = seaEntryDistance / viewLength;
        float sceneBehindSea = seaIntersects * sceneValid * step(seaEntryForwardDepth + 0.01, sceneForwardDepth);
        float seaEndDistance = lerp(seaExitDistance, min(sceneRayDistance, seaExitDistance), sceneValid);
        float seaPathMeters = max(seaEndDistance - seaEntryDistance, 0.0);
        float seaPath01 = saturate(1.0 - exp2(-seaPathMeters / max(_DeepDepth * 0.70, 120.0)));
        float3 seaEntryWS = _WorldSpaceCameraPos.xyz + viewDir * max(seaEntryDistance, 0.0);
        float3 seaNormalWS = WaterNormalizeSafe(seaEntryWS - _PlanetCenter, cameraUp);
        float seaGrazing01 = saturate(1.0 - abs(dot(viewDir, seaNormalWS)));

        float depth01Raw = saturate(waterData.g);
        float shore01Raw = saturate(waterData.b);
        float body01Raw = saturate(waterData.a);
        float waterForwardDepth = waterData.r;
        float existingWaterCoverage = max(centerWaterCoverage, expandedWaterCoverage);
        float analyticSphereWater = saturate((1.0 - underwater)
            * surfaceProximity01
            * sceneBehindSea
            * smoothstep(0.28, 0.78, seaGrazing01)
            * smoothstep(0.05, 0.28, seaPath01)
            * (1.0 - smoothstep(0.16, 0.86, existingWaterCoverage)));
        waterForwardDepth = lerp(waterForwardDepth, seaEntryForwardDepth, analyticSphereWater);
        depth01Raw = max(depth01Raw, analyticSphereWater * lerp(0.16, 0.58, seaPath01));
        shore01Raw = max(shore01Raw, analyticSphereWater * 0.62);
        body01Raw = max(body01Raw, analyticSphereWater);
        float waterMaskBasis = max(depth01Raw, shore01Raw);
        float dilationMask = saturate(expandedWaterCoverage * (1.0 - centerWaterCoverage));
        float waterMask = max(max(centerWaterCoverage, dilationMask * 0.92), analyticSphereWater * 0.94);
        float rawScreenEdgeFade = WaterScreenEdgeFade(input.uv, centerWaterCoverage);
        float screenEdgeFade = max(max(centerWaterCoverage * lerp(0.72, 1.0, rawScreenEdgeFade), dilationMask * 0.82), analyticSphereWater * 0.86);
        float coverageBasis = max(max(centerWaterCoverage, expandedWaterCoverage), analyticSphereWater);
        float edgeBasis = max(waterMaskBasis, coverageBasis * 0.55);
        float volumeEdgeMask = smoothstep(0.004, 0.050, edgeBasis);
        float volumeBodyMask = lerp(0.65, 1.0, smoothstep(0.10, 0.45, body01Raw));
        float volumeWaterMask = waterMask * volumeEdgeMask * volumeBodyMask * screenEdgeFade;
        float openWaterRecovery = centerWaterCoverage
            * smoothstep(0.14, 0.52, depth01Raw)
            * smoothstep(0.18, 0.62, shore01Raw)
            * lerp(0.55, 1.0, body01Raw);
        volumeWaterMask = max(volumeWaterMask, openWaterRecovery * 0.84);

        float3 waterPositionWS = _WorldSpaceCameraPos.xyz + viewDir * max(waterForwardDepth * viewLength, 0.0);
        float3 waterNormalWS = WaterNormalizeSafe(lerp(cameraUp, waterPositionWS - _PlanetCenter, volumeWaterMask), cameraUp);
        float grazing01 = saturate(1.0 - abs(dot(viewDir, waterNormalWS)));

        float waterVisibleRaw = volumeWaterMask * lerp(1.0, step(waterForwardDepth + 0.01, sceneForwardDepth), sceneValid);
        float skyFallbackPath = max(_DeepDepth * lerp(1.05, 2.10, grazing01), 420.0);
        float waterDepthFallback = lerp(max(_ShallowDepth * 0.65, 14.0), max(_DeepDepth * 0.92, 80.0), saturate(max(depth01Raw, body01Raw * depth01Raw)));
        float fallbackPath = waterDepthFallback;
        float aboveScenePath = max(sceneForwardDepth - waterForwardDepth, 0.0) * viewLength;
        float shoreContact = (1.0 - underwater)
            * waterVisibleRaw
            * sceneValid
            * (1.0 - smoothstep(0.10, 0.52, shore01Raw));
        float grazingSceneContact = (1.0 - underwater)
            * waterVisibleRaw
            * sceneValid
            * surfaceProximity01
            * smoothstep(0.36, 0.82, grazing01)
            * (1.0 - smoothstep(max(_ShallowDepth * 0.24, 8.0), max(_DeepDepth * 0.52, 120.0), aboveScenePath));
        float contactRisk = saturate(max(shoreContact, grazingSceneContact));
        float terrainClearance = smoothstep(max(_ShallowDepth * 0.12, 4.0), max(_ShallowDepth * 1.45, 46.0), aboveScenePath);
        float contactVisibilityFloor = lerp(0.62, 1.0, terrainClearance);
        float waterVisible = waterVisibleRaw * lerp(1.0, contactVisibilityFloor, contactRisk);
        float edgeDilation = dilationMask
            * surfaceProximity01
            * smoothstep(0.24, 0.76, grazing01);
        waterVisible = max(waterVisible, edgeDilation * 0.70);
        float openWater01 = saturate(max(depth01Raw, shore01Raw) * lerp(0.55, 1.0, body01Raw));
        float aboveWaterOpenMask = (1.0 - underwater)
            * waterVisible
            * body01Raw
            * smoothstep(0.10, 0.36, depth01Raw)
            * smoothstep(0.26, 0.66, shore01Raw);
        float lowAnglePath = skyFallbackPath
            * smoothstep(0.42, 0.90, grazing01)
            * smoothstep(0.22, 0.58, openWater01)
            * surfaceProximity01
            * aboveWaterOpenMask;

        float curvedSeaRay = saturate((1.0 - underwater)
            * surfaceProximity01
            * sceneBehindSea
            * smoothstep(0.30, 0.82, seaGrazing01)
            * smoothstep(0.035, 0.24, seaPath01)
            * smoothstep(0.08, 0.34, openWater01));
        float shoreSeaPathCoverage = saturate((1.0 - underwater)
            * surfaceProximity01
            * sceneBehindSea
            * volumeWaterMask
            * smoothstep(max(_ShallowDepth * 0.30, 8.0), 0.0, aboveScenePath));
        float curvedSeaOcclusion = saturate((1.0 - underwater)
            * surfaceProximity01
            * sceneBehindSea
            * smoothstep(0.18, 0.68, seaGrazing01)
            * smoothstep(0.025, 0.16, seaPath01));
        float curvedSeaCoverage = max(
            max(curvedSeaRay * max(volumeWaterMask, existingWaterCoverage * 0.72), shoreSeaPathCoverage),
            curvedSeaOcclusion * 0.92);
        float curvedSeaPath = seaPathMeters * curvedSeaCoverage;
        waterVisible = max(waterVisible, curvedSeaCoverage * 0.86);

        float abovePath = max(
            waterVisible * max(lerp(fallbackPath, aboveScenePath, sceneValid), lowAnglePath),
            curvedSeaPath);

        float insideSurfaceExit = SeaSphereExitDistance(_WorldSpaceCameraPos.xyz, viewDir);
        float insideScenePath = lerp(max(_DeepDepth * 1.55, 620.0), sceneRayDistance, sceneValid);
        float insidePath = underwater * min(insideScenePath, max(insideSurfaceExit, _ShallowDepth * 0.25));

        float pathMeters = max(abovePath, insidePath);
        float hasWater = saturate(waterVisible + underwater);

        if (_OceanDebugMode == 35)
            return float4(sceneBehindSea, seaPath01, seaGrazing01, 1.0);

        if (_OceanDebugMode == 36)
            return float4(volumeWaterMask, curvedSeaRay, curvedSeaCoverage, 1.0);

        if (_OceanDebugMode == 37)
        {
            float pathDebugScale = max(_DeepDepth * 2.2, 720.0);
            return float4(
                saturate(aboveScenePath / pathDebugScale),
                saturate(curvedSeaPath / pathDebugScale),
                saturate(pathMeters / pathDebugScale),
                1.0);
        }

        if (hasWater <= 0.0)
        {
            if ((_OceanDebugMode >= 13 && _OceanDebugMode <= 17)
                || _OceanDebugMode == 20
                || _OceanDebugMode == 21
                || _OceanDebugMode == 27
                || _OceanDebugMode == 28
                || _OceanDebugMode == 30
                || _OceanDebugMode == 33
                || _OceanDebugMode == 39
                || _OceanDebugMode == 43)
                return float4(0.0, 0.0, 0.0, 1.0);

            return SAMPLE_TEXTURE2D(_Source, sampler_Source, input.uv);
        }

        if (_OceanDebugMode == 13)
            return float4(depth01Raw, shore01Raw, body01Raw, 1.0);

        if (_OceanDebugMode == 14)
            return float4(waterMask, volumeWaterMask, screenEdgeFade, 1.0);

        if (_OceanDebugMode == 15)
        {
            float pathDebugScale = max(_DeepDepth * 2.2, 720.0);
            return float4(
                saturate(abovePath / pathDebugScale),
                saturate(insidePath / pathDebugScale),
                saturate(lowAnglePath / max(skyFallbackPath, 1.0)),
                1.0);
        }

        if (_OceanDebugMode == 20)
        {
            float sceneBehindWater = saturate((sceneForwardDepth - waterForwardDepth) / max(_DeepDepth, 1.0));
            return float4(waterVisible, sceneValid, sceneBehindWater * volumeWaterMask, 1.0);
        }

        if (_OceanDebugMode == 27)
            return float4(contactRisk, terrainClearance, waterVisible, 1.0);

        if (_OceanDebugMode == 28)
            return float4(centerWaterCoverage, expandedWaterCoverage, dilationMask, 1.0);

        if (_OceanDebugMode == 33)
            return float4(analyticSphereWater, sceneBehindSea, seaPath01, 1.0);

        float depth01 = lerp(0.60, depth01Raw, volumeWaterMask);
        float shore01 = lerp(0.78, shore01Raw, volumeWaterMask);
        float body01 = lerp(0.72, body01Raw, volumeWaterMask);

        float viewPath01 = saturate(1.0 - exp2(-pathMeters / max(_DeepDepth * 0.26, 34.0)));
        float depthGate = smoothstep(0.006, 0.14, depth01);
        float shoreGate = smoothstep(0.018, 0.30, shore01);
        float longViewGate = smoothstep(0.18, 0.72, viewPath01) * smoothstep(0.015, 0.20, shore01);
        float curvedSeaGate = curvedSeaCoverage * smoothstep(0.10, 0.40, viewPath01);
        float opticalGate = max(max(depthGate, longViewGate), curvedSeaGate);
        float oceanGate = lerp(0.58, 1.08, body01);
        float underwaterPath = underwater * smoothstep(0.10, 0.70, viewPath01);
        float longSurfacePath = waterVisible * smoothstep(0.30, 0.88, viewPath01) * smoothstep(0.18, 0.68, openWater01);
        float sourceWaterPath01 = saturate(1.0 - exp2(-max(aboveScenePath, curvedSeaPath) / max(_DeepDepth * 0.34, 46.0)));
        float sourcePathOcclusion = saturate((1.0 - underwater)
            * sceneValid
            * surfaceProximity01
            * max(max(waterVisible, analyticSphereWater * 0.92), curvedSeaCoverage)
            * smoothstep(0.30, 0.82, grazing01)
            * smoothstep(0.04, 0.30, viewPath01)
            * smoothstep(0.04, 0.36, sourceWaterPath01));
        float horizonOcclusion = saturate((1.0 - underwater)
            * surfaceProximity01
            * waterVisible
            * smoothstep(0.46, 0.88, grazing01)
            * smoothstep(0.18, 0.66, openWater01)
            * smoothstep(0.08, 0.46, viewPath01)
            + edgeDilation * 0.70
            + curvedSeaCoverage * 0.95);
        float sourceOcclusion = saturate((1.0 - underwater)
            * sceneValid
            * surfaceProximity01
            * max(max(waterVisible, waterVisibleRaw * 0.55), curvedSeaCoverage)
            * smoothstep(0.24, 0.82, grazing01)
            * saturate(max(max(contactRisk, horizonOcclusion * 0.88), sourcePathOcclusion * 0.92) + edgeDilation * 0.55 + curvedSeaCoverage * 0.78));
        sourceOcclusion = max(max(sourceOcclusion, sourcePathOcclusion * 0.88), curvedSeaCoverage * 0.82);
        float grazingBoost = lerp(1.0, 1.78, saturate(grazing01 * max(waterVisible, underwater * 0.82)));
        float densityScale = lerp(0.80, 2.15, saturate(max(depth01, viewPath01))) * lerp(0.72, 1.0, shoreGate) * oceanGate * grazingBoost;
        densityScale *= lerp(1.0, 2.25, saturate(underwaterPath + longSurfacePath * 0.55 + horizonOcclusion * 0.82 + sourceOcclusion + curvedSeaCoverage * 0.55));
        float optical = saturate((1.0 - exp2(-pathMeters / max(_DeepDepth * 0.15, 28.0))) * densityScale * opticalGate * _VolumeDensity);

        float contactRefractionFade = lerp(1.0, 0.10, saturate(contactRisk + horizonOcclusion * 0.85 + edgeDilation * 0.70));
        float debugRefractionEnabled = _OceanDebugMode == 29 ? 0.0 : 1.0;
        float refractionMask = saturate(waterVisible * surfaceProximity01 + underwater * 0.85)
            * smoothstep(0.035, 0.50, viewPath01)
            * contactRefractionFade
            * debugRefractionEnabled;
        float underwaterShoreRefractionFade = lerp(1.0,
            smoothstep(0.22, 0.78, depth01Raw)
            * smoothstep(0.30, 0.92, shore01Raw)
            * (1.0 - saturate(contactRisk * 0.85 + edgeDilation * 0.70)),
            underwater);
        refractionMask *= underwaterShoreRefractionFade;
        float2 refractionDelta = RefractionOffset(input.uv, refractionMask, viewPath01);
        float2 refractedUv = saturate(input.uv + refractionDelta);
        float2 sourceUv = lerp(input.uv, refractedUv, saturate(refractionMask));
        float4 original = SAMPLE_TEXTURE2D(_Source, sampler_Source, sourceUv);
        float sourceLuma = dot(original.rgb, float3(0.2126, 0.7152, 0.0722));
        float brightSourceBleed = saturate(sourceOcclusion * smoothstep(0.52, 0.86, sourceLuma));
        float horizonSilhouetteMatte = saturate((1.0 - underwater)
            * surfaceProximity01
            * sceneBehindSea
            * smoothstep(0.36, 0.86, seaGrazing01)
            * smoothstep(0.06, 0.32, seaPath01)
            * smoothstep(0.12, 0.66, viewPath01)
            * smoothstep(0.14, 0.56, openWater01));
        float longSeaSourceMatte = saturate((1.0 - underwater)
            * surfaceProximity01
            * sceneBehindSea
            * smoothstep(0.18, 0.58, seaGrazing01)
            * smoothstep(0.12, 0.54, seaPath01)
            * smoothstep(0.045, 0.24, max(curvedSeaCoverage, sourcePathOcclusion)));
        float contactEdgeSignal = saturate(max(max(contactRisk, edgeDilation), dilationMask));
        float horizonContactMatte = saturate((1.0 - underwater)
            * surfaceProximity01
            * sceneValid
            * seaIntersects
            * smoothstep(0.16, 0.52, seaGrazing01)
            * smoothstep(0.055, 0.34, seaPath01)
            * smoothstep(0.025, 0.18, contactEdgeSignal)
            * smoothstep(0.36, 0.78, sourceLuma));
        float seaSourceMatte = max(longSeaSourceMatte, horizonContactMatte * 0.88);

        float deepBlend = saturate(max(max(smoothstep(0.08, 0.52, depth01), smoothstep(0.12, 0.58, openWater01) * 0.74), optical * 0.88));
        float3 shallowColor = lerp(max(_ShallowColor.rgb, float3(0.10, 0.48, 0.50)), float3(0.04, 0.23, 0.28), smoothstep(0.45, 1.0, viewPath01) * 0.35);
        float3 deepColor = min(_DeepColor.rgb, float3(0.012, 0.095, 0.18));
        float3 scatterColor = lerp(shallowColor, deepColor, saturate(max(deepBlend, viewPath01 * 0.72)));
        float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : float3(0.0, 1.0, 0.0);
        float localSun = dot(waterNormalWS, sunDir);
        float viewSun = saturate(dot(viewDir, sunDir));
        float rawDaylight = smoothstep(-0.08, 0.18, localSun);
        float rawTwilight = smoothstep(-0.20, 0.08, localSun);

        // Automatically suppress day-driven volume lift when looking toward a low sun over the water horizon.
        // This keeps the underwater and horizon views deterministic without a runtime debug toggle.
        float sunsetBand = smoothstep(0.02, 0.40, rawDaylight) * (1.0 - smoothstep(0.54, 0.92, rawDaylight));
        float forwardSun = smoothstep(0.18, 0.78, viewSun);
        float brightBackground = smoothstep(0.28, 0.76, sourceLuma);
        float horizonView = smoothstep(0.12, 0.56, max(seaGrazing01, grazing01));
        float horizonRisk = saturate(max(horizonSilhouetteMatte, seaSourceMatte));
        float autoDayFlatten = saturate((1.0 - underwater)
            * surfaceProximity01
            * sunsetBand
            * max(horizonView * forwardSun * brightBackground, horizonRisk * 0.95));
        float sunsetHorizonFloor = saturate((1.0 - underwater)
            * surfaceProximity01
            * smoothstep(-0.26, 0.16, localSun)
            * horizonView
            * max(forwardSun * 0.90, horizonRisk));
        autoDayFlatten = max(autoDayFlatten, sunsetHorizonFloor);

        // Non-sunset safety net: if horizon/source matte risk is high, flatten anyway.
        float preSourceRisk = max(max(horizonRisk, sourceOcclusion), horizonSilhouetteMatte);
        float nonSunsetRiskFloor = saturate((1.0 - underwater)
            * surfaceProximity01
            * smoothstep(0.20, 0.72, preSourceRisk)
            * smoothstep(0.24, 0.72, max(horizonView, brightBackground)));
        float underwaterRiskFloor = saturate(underwater
            * smoothstep(0.06, 0.42, preSourceRisk)
            * smoothstep(0.10, 0.54, viewPath01));
        autoDayFlatten = max(autoDayFlatten, max(nonSunsetRiskFloor, underwaterRiskFloor));

        // Push risky sunset/horizon cases closer to full flatten.
        autoDayFlatten = max(autoDayFlatten, smoothstep(0.24, 0.62, autoDayFlatten));
        float flattenRisk = max(max(sunsetHorizonFloor, nonSunsetRiskFloor), underwaterRiskFloor);
        float flattenFloor = 0.92 * smoothstep(0.18, 0.52, flattenRisk);
        float dayFlatten = max(autoDayFlatten, flattenFloor);

        float daylight = rawDaylight;
        float twilight = rawTwilight;
        daylight = lerp(daylight, 0.0, dayFlatten);
        twilight = lerp(twilight, 0.0, dayFlatten);
        float surfaceScatterLight = max(daylight, twilight * 0.18) + _NightAmbientIntensity * 0.035;
        float submergedScatterLight = daylight * 0.34 + twilight * 0.10 + _NightAmbientIntensity * 0.018;
        float scatterLight = saturate(lerp(surfaceScatterLight, submergedScatterLight, underwater));
        scatterLight *= lerp(1.0, 0.38, saturate(underwaterPath + longSurfacePath * 0.62 + horizonOcclusion));

        // Surface reflection is handled in Ocean.shader; keep volume from lifting the background at sunset.
        float forwardSunHorizon = dayFlatten;
        float volumeBackgroundSuppress = lerp(1.0, 0.20, forwardSunHorizon);
        scatterLight *= volumeBackgroundSuppress;

        float extinctionBoost = lerp(1.0, 1.46, saturate(underwaterPath + longSurfacePath + horizonOcclusion * 0.85));
        float3 absorptionMeters = float3(max(_DeepDepth * 0.42, 92.0), max(_DeepDepth * 0.26, 58.0), max(_DeepDepth * 0.16, 34.0)) / extinctionBoost;
        float3 transmittance = exp2(-(pathMeters * densityScale * _VolumeDensity) / absorptionMeters);
        float sourceMatte = saturate(max(max(max(sourceOcclusion, brightSourceBleed), seaSourceMatte * 0.92), horizonSilhouetteMatte * 0.86));
        float underwaterShoreBand = 1.0 - smoothstep(0.08, 0.42, shore01Raw);
        float underwaterShallowBand = 1.0 - smoothstep(0.10, 0.46, depth01Raw);
        float underwaterContactEdge = smoothstep(0.010, 0.16, contactEdgeSignal);
        float sourceLumaEdge = smoothstep(0.018, 0.085, fwidth(sourceLuma));
        float underwaterSourceEdgeMatte = saturate(underwater
            * surfaceProximity01
            * smoothstep(0.08, 0.46, viewPath01)
            * smoothstep(0.18, 0.72, grazing01)
            * smoothstep(0.34, 0.76, sourceLuma)
            * sourceLumaEdge
            * max(0.35, 1.0 - smoothstep(0.52, 0.90, openWater01)));
        float underwaterShoreMatte = max(underwaterSourceEdgeMatte, saturate(underwater
            * smoothstep(0.02, 0.20, contactEdgeSignal)
            * max(underwaterShoreBand, underwaterShallowBand * underwaterContactEdge)
            * smoothstep(0.34, 0.82, sourceLuma)));
        sourceMatte = max(sourceMatte, underwaterShoreMatte * 0.95);

        if (_OceanDebugMode == 38)
        {
            float seaMatte = smoothstep(0.025, 0.20, max(max(curvedSeaCoverage, sourcePathOcclusion), sourceMatte));
            float3 diagnosticWater = min(deepColor, float3(0.006, 0.060, 0.110));
            return float4(lerp(original.rgb, diagnosticWater, seaMatte), original.a);
        }

        if (_OceanDebugMode == 39)
            return float4(longSeaSourceMatte, max(horizonContactMatte, underwaterSourceEdgeMatte), sourceMatte, original.a);

        float seaMatteCombined = saturate(max(seaSourceMatte, horizonSilhouetteMatte));
        float3 sourceTransmittanceFloor = lerp(float3(0.055, 0.095, 0.135), float3(0.012, 0.036, 0.064), seaMatteCombined);
        transmittance = lerp(transmittance, min(transmittance, sourceTransmittanceFloor), sourceMatte);
        transmittance *= lerp(1.0, 0.32, underwaterShoreMatte);
        float horizonGlowBleed = saturate(horizonSilhouetteMatte * smoothstep(0.56, 0.88, sourceLuma));
        horizonGlowBleed *= 1.0 - dayFlatten;
        float horizonSourceSuppression = lerp(1.0, 0.10, max(horizonSilhouetteMatte, horizonGlowBleed * 0.9));
        transmittance *= horizonSourceSuppression;
        float scatterStrength = lerp(0.38, 0.62, deepBlend) * lerp(1.0, 0.24, saturate(underwaterPath + longSurfacePath * 0.45 + horizonOcclusion * 0.85 + sourceMatte));
        scatterStrength *= lerp(1.0, 0.58, forwardSunHorizon);
        float3 absorbed = original.rgb * transmittance + scatterColor * scatterLight * (1.0 - transmittance) * scatterStrength;
        float volumeBlend = saturate(max(max(max(max(optical * 0.90, viewPath01 * opticalGate * 0.66), lowAnglePath / max(skyFallbackPath, 1.0) * 0.38), horizonOcclusion * 0.72), sourceMatte) * hasWater);
        float3 color = lerp(original.rgb, absorbed, volumeBlend);
        float underwaterTint = underwater * lerp(0.16, 1.0, smoothstep(0.015, 0.22, viewPath01));
        float3 underwaterBlue = lerp(float3(0.05, 0.30, 0.40), float3(0.015, 0.10, 0.19), smoothstep(0.45, 1.0, viewPath01));
        color = lerp(color, underwaterBlue, underwaterTint * lerp(0.34, 0.18, saturate(viewPath01)));

        float deepExtinction = saturate(max(max(max(underwaterPath, longSurfacePath) * max(optical, viewPath01) * 0.86, max(horizonOcclusion, curvedSeaCoverage) * 0.58), max(sourceMatte * 0.86, seaSourceMatte * 0.94)));
        deepExtinction = saturate(max(deepExtinction, max(horizonSilhouetteMatte * 0.82, horizonGlowBleed * 0.88)));
        deepExtinction = saturate(max(deepExtinction, underwaterShoreMatte * 0.82));
        deepExtinction *= lerp(1.0, 0.42, forwardSunHorizon);
        color = lerp(color, deepColor * lerp(0.18, 0.42, scatterLight), deepExtinction);
        color = lerp(color, deepColor * lerp(0.20, 0.44, scatterLight), underwaterShoreMatte * 0.48);
        color = lerp(color, deepColor * lerp(0.24, 0.48, scatterLight), seaSourceMatte * 0.78);
        color = lerp(color, deepColor * lerp(0.28, 0.52, scatterLight), horizonContactMatte * 0.42);
        color = lerp(color, deepColor * lerp(0.30, 0.54, scatterLight), horizonSilhouetteMatte * 0.52);
        color = lerp(color, deepColor * lerp(0.34, 0.58, scatterLight), horizonGlowBleed * 0.58);
        color = lerp(color, deepColor * lerp(0.28, 0.48, scatterLight), brightSourceBleed * 0.42);

        if (_OceanDebugMode == 43)
            return float4(ContributionHeat(color - original.rgb, 9.0), 1.0);

        if (_OceanDebugMode == 30)
            return float4(max(sourceMatte, underwaterShoreMatte), volumeBlend, saturate(1.0 - dot(transmittance, float3(0.3333, 0.3333, 0.3333))), original.a);

        if (_OceanDebugMode == 12)
            return float4(waterMask, waterVisible, saturate(optical + underwater * 0.35), original.a);

        if (_OceanDebugMode == 16)
            return float4(scatterLight, saturate(extinctionBoost - 1.0), volumeBlend, original.a);

        if (_OceanDebugMode == 17)
        {
            float2 appliedDelta = (sourceUv - input.uv) * _ScreenParams.xy;
            return float4(
                saturate(abs(appliedDelta.x) / 12.0),
                saturate(abs(appliedDelta.y) / 12.0),
                saturate(length(appliedDelta) / 12.0),
                original.a);
        }

        if (_OceanDebugMode == 21)
            return float4(optical, volumeBlend, deepExtinction, original.a);

        return float4(color, original.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

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
