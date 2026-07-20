Shader "Hidden/Clouds"
{
HLSLINCLUDE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Includes/Math.hlsl"
#include "Includes/DebugModes.hlsl"
#include "Includes/WeatherSampling.hlsl"
#include "Includes/CloudDensity.hlsl"
#include "Includes/ClimateSampling.hlsl"

TEXTURE2D(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);
TEXTURE2D(_Source);
SAMPLER(sampler_Source);

TEXTURE3D(_CloudShapeNoise);
SAMPLER(sampler_CloudShapeNoise);
TEXTURE3D(_CloudDetailNoise);
SAMPLER(sampler_CloudDetailNoise);
TEXTURE2D(_CloudBlueNoise);
SAMPLER(sampler_CloudBlueNoise);
float4 _CloudBlueNoise_TexelSize;

float3 _CloudPlanetCenter;
float _SeaLevelRadius;
float _CloudInnerRadius;
float _CloudOuterRadius;
int _CloudWeatherResolution;

// Shape
float _CloudNoiseScale;
float _CloudDetailNoiseScale;
float _CloudDetailWeight;
float4 _CloudShapeWeights;
float _CloudDensityMultiplier;
float _CloudDensityThreshold;
float _CloudShapeSharpness;
float _CloudBottomFeather;
float _CloudTopFeather;
float _CloudTopDensityBias;

// Lighting
float _CloudLightAbsorption;
float _CloudDarknessThreshold;
float4 _CloudPhaseParams;
float4 _CloudColor;
float4 _CloudStormColor;
float _CloudAmbientStrength;
float _CloudStormDarkening;
float _CloudPowderStrength;
float4 _CloudMultiScatterParams;
float4 _CloudAmbientSky;
float4 _CloudAmbientGround;
float _CloudAerialDensity;
float4 _CloudBacklitParams;
float4 _CloudSilverLiningParams;
float4 _CloudRainShaftParams; // x=strength (0=off), y=length metres below cloud base
float4 _WeatherLightningColor;

// Animation
float _CloudWindAngle;

// Ray march
int _CloudViewSteps;
int _CloudLightSteps;
float _CloudRayOffsetStrength;
int _CloudDebugMode;
float4 _CloudDebugParams;

// Platform quality - CLOUD_QUALITY_LOW keyword set by Shader.EnableKeyword on lower-tier hardware.
#ifdef CLOUD_QUALITY_LOW
    #define CLOUD_MAX_STEPS 8
    #define CLOUD_LIGHT_STEPS_MAX 3
#else
    #define CLOUD_MAX_STEPS 96
    #define CLOUD_LIGHT_STEPS_MAX 16
#endif

// Weather
float3 _WindDirection;
float _WindSpeedMps;

// Sun
float3 _SunParams;

// Night ambient
float _NightAmbientIntensity;

struct CloudSample
{
    float density;
    float condensation;
    float storm;
    float moistureSource;
    float condensationDelta;
    float height01;
};

// --- Phase function ---

float HG(float cosAngle, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * MATH_PI * pow(abs(1.0 + g2 - 2.0 * g * cosAngle), 1.5));
}

float CloudPhaseOctave(float cosAngle, float phaseScale)
{
    float forward = HG(cosAngle, _CloudPhaseParams.x * phaseScale);
    float back = HG(cosAngle, -_CloudPhaseParams.y * phaseScale);
    return _CloudPhaseParams.z + lerp(back, forward, 0.5) * _CloudPhaseParams.w;
}

float CloudBeerPowder(float lightDensity, float cosAngle)
{
    float beer = exp(-lightDensity * _CloudLightAbsorption);
    float powder = 1.0 - exp(-lightDensity * 2.0);
    float powderMix = saturate(_CloudPowderStrength * saturate(cosAngle));
    return _CloudDarknessThreshold + beer * lerp(1.0, powder, powderMix) * (1.0 - _CloudDarknessThreshold);
}

float CloudMultiScatter(float lightDensity, float cosAngle, float directLight, float sunVisibility)
{
    float scatter = directLight * CloudPhaseOctave(cosAngle, 1.0);
    float attenuation = _CloudMultiScatterParams.x;
    float contribution = _CloudMultiScatterParams.y;
    float phaseScale = _CloudMultiScatterParams.z;
    float strength = _CloudMultiScatterParams.w;

    [unroll]
    for (int octave = 1; octave < 3; octave++)
    {
        float octaveTransmittance = exp(-lightDensity * _CloudLightAbsorption * attenuation) * sunVisibility;
        scatter += strength * contribution * octaveTransmittance * CloudPhaseOctave(cosAngle, phaseScale);
        attenuation *= _CloudMultiScatterParams.x;
        contribution *= _CloudMultiScatterParams.y;
        phaseScale *= _CloudMultiScatterParams.z;
    }

    return scatter;
}

float WeightedNoise(float4 noise, float4 weights)
{
    return dot(noise, weights / max(dot(weights, float4(1.0, 1.0, 1.0, 1.0)), 0.0001));
}

CloudSample SampleCloud(float3 worldPos)
{
    CloudSample sampleData;
    sampleData.density = 0;
    sampleData.condensation = 0;
    sampleData.storm = 0;
    sampleData.moistureSource = 0;
    sampleData.condensationDelta = 0;
    sampleData.height01 = 0;

    float layerThickness = max(_CloudOuterRadius - _CloudInnerRadius, 0.0001);
    float3 fromCenter = worldPos - _CloudPlanetCenter;
    float radius = length(fromCenter);
    float height01 = saturate((radius - _CloudInnerRadius) / layerThickness);

    if (radius < _CloudInnerRadius || radius > _CloudOuterRadius)
        return sampleData;

    float3 direction = fromCenter / max(radius, 0.0001);
    sampleData.height01 = height01;
    float4 weather = SampleWeather(direction);
    float condensation = weather.r;
    float storm = weather.g;
    sampleData.condensation = condensation;
    sampleData.storm = storm;
    sampleData.moistureSource = weather.b;
    sampleData.condensationDelta = (weather.a - 0.5) / max(_CloudDebugParams.z, 1.0);

    if (condensation <= 0.001)
        return sampleData;

    // The procedural cloud shape rides with the wind: advect the noise along the local
    // windTangent (the same convention as grass and weather particles) by rotating the sample
    // position back about cross(direction, windDir) by the accumulated wind angle. Sampling the
    // origin makes the detail flow toward +windTangent instead of staying pinned to world space.
    float3 windAxis = cross(direction, _WindDirection);
    float windAxisLen = length(windAxis);
    float3 advectedPos = windAxisLen > 1e-5
        ? RotateAroundAxis(fromCenter, windAxis / windAxisLen, -_CloudWindAngle) + _CloudPlanetCenter
        : worldPos;
    float3 shapePos = advectedPos * _CloudNoiseScale;
    float shapeFBM = WeightedNoise(SAMPLE_TEXTURE3D_LOD(_CloudShapeNoise, sampler_CloudShapeNoise, shapePos, 0), _CloudShapeWeights);

    // Cloud type is chosen by climate temperature (independent of density): warm air builds
    // cumulus, cold air layers into stratus. Storm still drives cumulonimbus.
    float convectivity = smoothstep(0.2, 0.6, SampleClimate01(direction).x);
    float verticalProfile = CloudVerticalProfile(height01, convectivity, storm,
        _CloudBottomFeather, _CloudTopFeather, _CloudTopDensityBias);

    float cloudShape = shapeFBM * condensation * verticalProfile;
    float density = saturate((cloudShape - _CloudDensityThreshold) * _CloudShapeSharpness);
    if (density <= 0.0001)
        return sampleData;

#ifdef CLOUD_QUALITY_LOW
    float detailFBM = 0.5; // skip detail noise sample on low-quality path
#else
    float3 detailPos = advectedPos * _CloudDetailNoiseScale;
    float detailFBM = dot(SAMPLE_TEXTURE3D_LOD(_CloudDetailNoise, sampler_CloudDetailNoise, detailPos, 0).rgb,
        float3(0.5, 0.35, 0.15));
#endif

    float edgeErosion = (1.0 - detailFBM) * (1.0 - density) * _CloudDetailWeight;
    density = saturate(density - edgeErosion) * _CloudDensityMultiplier;

    sampleData.density = density;
    return sampleData;
}

struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 viewVector : TEXCOORD1;
};

v2f vert(uint vertexID : SV_VertexID)
{
    v2f output;
    output.pos = GetFullScreenTriangleVertexPosition(vertexID);
    float2 uv = GetFullScreenTriangleTexCoord(vertexID);
    output.uv = uv;
    #if UNITY_UV_STARTS_AT_TOP
        float2 ndc = float2(uv.x * 2.0 - 1.0, uv.y * 2.0 - 1.0);
    #else
        float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    #endif
    float3 viewVector = mul(unity_CameraInvProjection, float4(ndc, 0, -1)).xyz;
    output.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0)).xyz;
    return output;
}

float LightMarch(float3 pos, float lightStepSize, float2 pixel, int viewStep, float cosAngle, out float lightDensity)
{
    float3 surfaceNormal = normalize(pos - _CloudPlanetCenter);
    float sunDot = dot(surfaceNormal, _SunParams.xyz);
    float sunVisibility = smoothstep(-0.55, 0.35, sunDot);

    float totalDensity = 0;
    float jitterStrength = saturate(_CloudRayOffsetStrength);
    float viewStepF = (float)viewStep;
    float lightStartJitter = (Hash12(pixel + float2(viewStepF * 37.17, 13.73)) - 0.5)
        * lightStepSize
        * jitterStrength;
    float3 lightPos = pos + _SunParams.xyz * lightStartJitter;

    int lightSteps = min(_CloudLightSteps, CLOUD_LIGHT_STEPS_MAX);
    for (int i = 0; i < lightSteps; i++)
    {
        float lightStepF = (float)i;
        float perStepJitter = (Hash12(pixel + float2(viewStepF * 19.31 + lightStepF * 7.11, lightStepF * 43.17)) - 0.5)
            * lightStepSize
            * jitterStrength
            * 0.35;
        lightPos += _SunParams.xyz * lightStepSize;
        totalDensity += SampleCloud(lightPos + _SunParams.xyz * perStepJitter).density * lightStepSize;
    }

    lightDensity = totalDensity;
    return CloudBeerPowder(lightDensity, cosAngle) * sunVisibility;
}

float3 PrecipitationDebugColor(float value)
{
    float3 low = float3(0.05, 0.25, 1.0);
    float3 mid = float3(0.15, 1.0, 0.35);
    float3 high = float3(1.0, 0.12, 0.02);
    return value < 0.5
        ? lerp(low, mid, value * 2.0)
        : lerp(mid, high, (value - 0.5) * 2.0);
}

ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Off

        Pass
        {
            Name "RenderClouds"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile _ CLOUD_QUALITY_LOW

            float4 frag(v2f i) : SV_Target
            {
                float4 sceneColor = SAMPLE_TEXTURE2D(_Source, sampler_Source, i.uv);

                if (_CloudWeatherResolution <= 0 || _CloudOuterRadius <= _CloudInnerRadius)
                    return sceneColor;

                float viewLength = length(i.viewVector);
                float3 rayDir = i.viewVector / max(viewLength, 0.0001);
                float3 rayOrigin = _WorldSpaceCameraPos.xyz;

                float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, i.uv).r;
                float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;
                if (_SeaLevelRadius > 0.0)
                {
                    float2 oceanHit = RaySphere(_CloudPlanetCenter, _SeaLevelRadius, rayOrigin, rayDir);
                    if (oceanHit.y > 0.0)
                        sceneDepth = min(sceneDepth, oceanHit.x);
                }

                float2 outerHit = RaySphere(_CloudPlanetCenter, _CloudOuterRadius, rayOrigin, rayDir);
                if (outerHit.y <= 0)
                    return sceneColor;

                float startDistance = outerHit.x;
                float endDistance = min(outerHit.x + outerHit.y, sceneDepth);
                // Distance to the cloud layer itself, used for the aerial-perspective fade. Kept
                // separate from the march start so extending the march down into the sub-cloud air
                // for the rain shaft doesn't collapse the aerial fade to zero.
                float cloudStartDistance = startDistance;

                float cameraRadius = length(rayOrigin - _CloudPlanetCenter);
                if (cameraRadius < _CloudInnerRadius)
                {
                    float2 innerHit = RaySphere(_CloudPlanetCenter, _CloudInnerRadius, rayOrigin, rayDir);
                    cloudStartDistance = max(startDistance, innerHit.x + innerHit.y);
                    // Normally skip the empty sub-cloud air and start at the inner-shell exit. With
                    // the rain shaft on, keep that segment so virga renders when viewed from below
                    // (standing on the ground looking at a distant storm).
                    if (_CloudRainShaftParams.x > 0.0)
                        startDistance = max(startDistance, 0.0);
                    else
                        startDistance = cloudStartDistance;
                }

                if (endDistance <= startDistance)
                    return sceneColor;

                int viewSteps = min(max(_CloudViewSteps, 1), CLOUD_MAX_STEPS);
                float stepSize = (endDistance - startDistance) / viewSteps;
                float2 pixel = floor(i.uv * _ScreenParams.xy);
                float pixelJitter = SAMPLE_TEXTURE2D_LOD(_CloudBlueNoise, sampler_CloudBlueNoise,
                    (pixel + 0.5) * _CloudBlueNoise_TexelSize.xy, 0).r;
                float jitterStrength = saturate(_CloudRayOffsetStrength);

                float cosAngle = dot(rayDir, _SunParams.xyz);
                float lightStepSize = max((_CloudOuterRadius - _CloudInnerRadius) / max(min(_CloudLightSteps, CLOUD_LIGHT_STEPS_MAX), 1), 1.0);

                float transmittance = 1.0;
                float3 lightEnergy = 0;
                float debugWeather = 0.0;
                float debugStorm = 0.0;
                float debugDensity = 0.0;
                float debugSilverLining = 0.0;
                float debugMoistureSource = 0.0;
                float debugCondensationChange = 0.0;
                float debugCondensationSign = 0.0;
                float debugRainRate = 0.0;
                float debugWeatherRainRate = 0.0;

                UNITY_LOOP
                for (int s = 0; s < viewSteps; s++)
                {
                    float stepF = (float)s;
                    float stepNoise = Hash12(pixel + float2(stepF * 17.17 + pixelJitter, stepF * 61.31 + pixelJitter * 1.37));
                    float withinStep = saturate(lerp(0.5, stepNoise, jitterStrength));
                    float marchDistance = min(startDistance + stepSize * (stepF + withinStep), endDistance);
                    float3 jitteredSamplePos = rayOrigin + rayDir * marchDistance;
                    CloudSample cloud = SampleCloud(jitteredSamplePos);
                    float3 sampleNormal = normalize(jitteredSamplePos - _CloudPlanetCenter);
                    float weatherRainRate = 0.0;
                    if (_CloudDebugMode == 9)
                    {
                        weatherRainRate = WeatherPrecipitationSignal(sampleNormal, cloud.storm);
                        debugWeatherRainRate = max(debugWeatherRainRate, weatherRainRate);
                    }
                    debugWeather = max(debugWeather, cloud.condensation);
                    debugStorm = max(debugStorm, cloud.storm);
                    debugMoistureSource = max(debugMoistureSource, cloud.moistureSource);
                    float condensationChange = abs(cloud.condensationDelta);
                    if (condensationChange > debugCondensationChange)
                    {
                        debugCondensationChange = condensationChange;
                        debugCondensationSign = cloud.condensationDelta >= 0.0 ? 1.0 : -1.0;
                    }

                    if (cloud.density > 0.0001)
                    {
                        float lightDensity = 0.0;
                        float lightTransmittance = LightMarch(jitteredSamplePos, lightStepSize, pixel, s, cosAngle, lightDensity);
                        float3 surfaceNormal = sampleNormal;
                        float sunFacing = dot(surfaceNormal, _SunParams.xyz);
                        float localSun = smoothstep(-0.55, 0.35, sunFacing);

                        if (_CloudDebugMode != 9)
                            weatherRainRate = WeatherPrecipitationSignal(sampleNormal, cloud.storm);
                        float rainRate = weatherRainRate;
                        float gloom = WeatherCloudGloomFromRain(cloud.storm, rainRate);

                        float3 cloudAlbedo = lerp(_CloudColor.rgb, _CloudStormColor.rgb, gloom);
                        float stormLight = lerp(1.0, 1.0 - _CloudStormDarkening, gloom);
                        float ambientStrength = lerp(_CloudAmbientStrength * 0.22, _CloudAmbientStrength, localSun);
                        float3 ambient = lerp(_CloudAmbientGround.rgb, _CloudAmbientSky.rgb, cloud.height01)
                            * (_NightAmbientIntensity * 0.25 + ambientStrength);
                        float multiScatter = CloudMultiScatter(lightDensity, cosAngle, lightTransmittance, localSun);
                        float3 lighting = cloudAlbedo
                            * (multiScatter * stormLight + ambient);

                        float density01 = saturate(cloud.density / max(_CloudDensityMultiplier, 0.0001));
                        float thinEdge = pow(saturate(1.0 - density01), max(_CloudSilverLiningParams.z, 0.001));
                        float forwardSun = pow(saturate(cosAngle), max(_CloudSilverLiningParams.y, 1.0));
                        float horizonSun = saturate((dot(surfaceNormal, _SunParams.xyz) + 0.35) * 1.6);
                        // Dim the rim on gloomy clouds, never kill it: dark storm clouds keep a
                        // silver lining where they are optically thin.
                        float rimKeep = saturate(1.0 - gloom * _CloudSilverLiningParams.w);
                        // Backlit rim/glow visibility: light wrapping a cloud edge stays bright
                        // even when the cloud body blocks DIRECT transmission - which is exactly
                        // the sun-behind-cloud case. Gating the rim on raw lightTransmittance
                        // suppressed it in the very situation it should appear (flat grey cloud,
                        // no silver lining). Blend toward 1 so thin backlit edges glow; thick
                        // cloud still dims it somewhat.
                        float backlitVisibility = lerp(lightTransmittance, 1.0, 0.6);

                        float silverLining = _CloudSilverLiningParams.x * forwardSun * thinEdge
                            * backlitVisibility * horizonSun * rimKeep;
                        lighting += _CloudColor.rgb * silverLining;

                        // Backlit inner glow: forward-scattered sun bleeding through the cloud body
                        // when lit from behind (view looking toward the sun).
                        float backGlow = pow(saturate(cosAngle), max(_CloudBacklitParams.y, 1.0))
                            * backlitVisibility * _CloudBacklitParams.x * rimKeep;
                        lighting += _CloudColor.rgb * backGlow;

                        float lightning = WeatherLightning(surfaceNormal, cloud.storm);
                        lighting += _WeatherLightningColor.rgb * lightning * lerp(0.75, 1.2, cloud.height01)
                            * lerp(0.7, 1.1, density01);

                        debugDensity = max(debugDensity, density01);
                        debugSilverLining = max(debugSilverLining, saturate(silverLining));
                        debugRainRate = max(debugRainRate, rainRate);

                        lightEnergy += cloud.density * stepSize * transmittance * lighting;
                        transmittance *= exp(-cloud.density * stepSize * _CloudLightAbsorption);

                        if (transmittance < 0.01)
                            break;
                    }
                    else if (_CloudRainShaftParams.x > 0.0)
                    {
                        // Virga / rain shaft: below the cloud base SampleCloud is zero, so read the
                        // weather column directly and hang a faint grey veil under gloomy (raining)
                        // cells. Fades downward from the base like a curtain and hazes the background
                        // behind it. Reuses the cloud density scale so opacity tracks cloud opacity.
                        float sampleRadius = length(jitteredSamplePos - _CloudPlanetCenter);
                        float belowBase = _CloudInnerRadius - sampleRadius;
                        if (belowBase > 0.0)
                        {
                            float4 weatherColumn = SampleWeather(sampleNormal);
                            float rainSignal = WeatherPrecipitationSignal(sampleNormal, weatherColumn.g);
                            float rainAmount = WeatherCloudGloomFromRain(weatherColumn.g, rainSignal)
                                * smoothstep(0.02, 0.25, weatherColumn.r);
                            if (rainAmount > 0.001)
                            {
                                float vfade = saturate(1.0 - belowBase / max(_CloudRainShaftParams.y, 1.0));
                                float rainSigma = rainAmount * vfade * _CloudRainShaftParams.x * _CloudDensityMultiplier;
                                // Cap per-step optical depth so a long high-altitude step (edge-on
                                // through the whole veil) can't wall off to opaque white; keep it a
                                // translucent grey curtain.
                                float veilOD = min(rainSigma * stepSize, 0.25);
                                float rainLocalSun = smoothstep(-0.55, 0.35, dot(sampleNormal, _SunParams.xyz));
                                float3 rainColor = _CloudAmbientSky.rgb * 0.28 * lerp(0.5, 1.0, rainLocalSun);
                                lightEnergy += rainColor * veilOD * transmittance;
                                transmittance *= exp(-veilOD);
                                if (transmittance < 0.01)
                                    break;
                            }
                        }
                    }

                }

                if (_CloudDebugMode > 0)
                {
                    float opticalDepth = saturate(1.0 - transmittance);
                    float3 baseScene = sceneColor.rgb * 0.25;
                    float3 debugColor = 0;

                    if (_CloudDebugMode == 1)
                        debugColor = lerp(float3(0.02, 0.05, 0.08), float3(0.15, 0.75, 1.0), debugWeather);
                    if (_CloudDebugMode == 2)
                        debugColor = lerp(float3(0.02, 0.04, 0.12), float3(1.0, 0.18, 0.08), debugStorm);
                    if (_CloudDebugMode == 3)
                        debugColor = lerp(float3(0.02, 0.02, 0.02), float3(1.0, 1.0, 1.0), debugDensity);
                    if (_CloudDebugMode == 4)
                        debugColor = lerp(float3(0.02, 0.04, 0.1), float3(1.0, 0.8, 0.1), opticalDepth);
                    if (_CloudDebugMode == 5)
                        debugColor = lerp(float3(0.02, 0.02, 0.02), float3(1.0, 0.92, 0.55), debugSilverLining);
                    if (_CloudDebugMode == 6)
                        debugColor = lerp(float3(0.22, 0.12, 0.04), float3(0.15, 0.85, 1.0), debugMoistureSource);
                    if (_CloudDebugMode == 7)
                    {
                        float3 drying = float3(1.0, 0.18, 0.08);
                        float3 condensing = float3(0.1, 0.75, 1.0);
                        debugColor = lerp(drying, condensing, step(0.0, debugCondensationSign));
                    }
                    if (_CloudDebugMode == 8)
                    {
                        debugColor = PrecipitationDebugColor(debugRainRate);
                    }
                    if (_CloudDebugMode == 9)
                    {
                        debugColor = PrecipitationDebugColor(debugWeatherRainRate);
                    }

                    float debugMask = max(max(max(debugWeather, debugStorm), max(debugDensity, opticalDepth)),
                        debugSilverLining);
                    if (_CloudDebugMode == 6)
                        debugMask = debugMoistureSource;
                    if (_CloudDebugMode == 7)
                    {
                        float changeThreshold = max(_CloudDebugParams.x, 0.0);
                        float changeSaturation = max(_CloudDebugParams.y, changeThreshold + 0.00001);
                        debugMask = smoothstep(changeThreshold, changeSaturation, debugCondensationChange);
                    }
                    if (_CloudDebugMode == 8)
                        debugMask = debugDensity;
                    if (_CloudDebugMode == 9)
                        debugMask = debugWeather;
                    return float4(baseScene + debugColor * saturate(debugMask), sceneColor.a);
                }

                float3 result = sceneColor.rgb * transmittance + lightEnergy;

                // Aerial perspective: clouds render after the atmosphere, so sceneColor already
                // holds the atmosphere-lit sky behind this pixel. Fade the cloud toward it with
                // view distance so distant clouds haze into the sky instead of pasting on it.
                float aerial = 1.0 - exp(-cloudStartDistance * _CloudAerialDensity);
                result = lerp(result, sceneColor.rgb, aerial);

                // Screen-space cloud opacity for the god-ray streak pass to occlude on (distant
                // hazed clouds block less). The pass reads this back from alpha, then restores
                // alpha to 1 so downstream post-processing is unaffected.
                float cloudOpacity = (1.0 - transmittance) * (1.0 - aerial);
                return float4(result, cloudOpacity);
            }
            ENDHLSL
        }
    }
}
