Shader "Hidden/Clouds"
{
HLSLINCLUDE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Includes/Math.hlsl"

TEXTURE2D(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);
TEXTURE2D(_Source);
SAMPLER(sampler_Source);

TEXTURE2D_ARRAY(_CloudWeatherMap);
SAMPLER(sampler_CloudWeatherMap);
TEXTURE3D(_CloudShapeNoise);
SAMPLER(sampler_CloudShapeNoise);
TEXTURE3D(_CloudDetailNoise);
SAMPLER(sampler_CloudDetailNoise);

float3 _CloudPlanetCenter;
float _PlanetRadius;
float _CloudInnerRadius;
float _CloudOuterRadius;
int _CloudWeatherResolution;
float4x4 _CloudWeatherRotation;

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
float4 _CloudSilverLiningParams;
float4 _WeatherLightningParams;
float4 _WeatherLightningColor;
float4 _WeatherLightningCell0;
float4 _WeatherLightningCell1;
float4 _WeatherLightningCell2;
float4 _WeatherLightningCell3;

// Animation
float _CloudAnimSpeed;

// Ray march
int _CloudViewSteps;
int _CloudLightSteps;
float _CloudRayOffsetStrength;
int _CloudDebugMode;
float4 _CloudDebugParams;

// Weather
float3 _WindDirection;
float _WindSpeed;

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

float CloudPhase(float cosAngle)
{
    float forward = HG(cosAngle, _CloudPhaseParams.x);
    float back = HG(cosAngle, -_CloudPhaseParams.y);
    return _CloudPhaseParams.z + lerp(back, forward, 0.5) * _CloudPhaseParams.w;
}

float WeatherLightningCell(float4 cell, float3 normal, float storm)
{
    if (cell.w <= 0.0)
        return 0.0;

    float3 direction = dot(cell.xyz, cell.xyz) > 0.0001
        ? normalize(cell.xyz)
        : float3(0.0, 1.0, 0.0);
    float stormMask = smoothstep(_WeatherLightningParams.z, 1.0, storm);
    float locationMask = smoothstep(_WeatherLightningParams.y, _WeatherLightningParams.x, dot(normal, direction));
    return cell.w * stormMask * pow(saturate(locationMask), 1.35);
}

float WeatherLightning(float3 normal, float storm)
{
    float lightning = WeatherLightningCell(_WeatherLightningCell0, normal, storm);
    lightning = max(lightning, WeatherLightningCell(_WeatherLightningCell1, normal, storm));
    lightning = max(lightning, WeatherLightningCell(_WeatherLightningCell2, normal, storm));
    lightning = max(lightning, WeatherLightningCell(_WeatherLightningCell3, normal, storm));
    return lightning;
}

float InterleavedGradientNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

float Hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

void CubeFaceUv(float3 direction, out int face, out float2 uv)
{
    float3 absDirection = abs(direction);
    float u;
    float v;

    if (absDirection.y >= absDirection.x && absDirection.y >= absDirection.z)
    {
        face = direction.y > 0 ? 0 : 1;
        float faceSign = direction.y > 0 ? 1.0 : -1.0;
        u = direction.x / max(absDirection.y, 0.00001);
        v = direction.z / max(absDirection.y, 0.00001) * faceSign;
    }
    else if (absDirection.x >= absDirection.y && absDirection.x >= absDirection.z)
    {
        face = direction.x > 0 ? 3 : 2;
        float faceSign = direction.x > 0 ? 1.0 : -1.0;
        u = direction.z / max(absDirection.x, 0.00001) * -faceSign;
        v = direction.y / max(absDirection.x, 0.00001);
    }
    else
    {
        face = direction.z > 0 ? 4 : 5;
        float faceSign = direction.z > 0 ? 1.0 : -1.0;
        u = direction.x / max(absDirection.z, 0.00001) * faceSign;
        v = direction.y / max(absDirection.z, 0.00001);
    }

    uv = saturate(float2(u, v) * 0.5 + 0.5);
}

float4 SampleWeather(float3 direction)
{
    float3 weatherDirection = mul((float3x3)_CloudWeatherRotation, direction);
    direction = dot(weatherDirection, weatherDirection) > 0.0001 ? normalize(weatherDirection) : direction;

    int face;
    float2 uv;
    CubeFaceUv(direction, face, uv);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_CloudWeatherMap, sampler_CloudWeatherMap, uv, face, 0);
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
    float4 weather = SampleWeather(direction);
    float condensation = weather.r;
    float storm = weather.g;
    sampleData.condensation = condensation;
    sampleData.storm = storm;
    sampleData.moistureSource = weather.b;
    sampleData.condensationDelta = (weather.a - 0.5) / max(_CloudDebugParams.z, 1.0);

    if (condensation <= 0.001)
        return sampleData;

    float3 windDir = dot(_WindDirection, _WindDirection) > 0.0001 ? normalize(_WindDirection) : float3(1.0, 0.0, 0.0);
    float3 windOffset = windDir * (_WindSpeed * _CloudAnimSpeed * _Time.y);
    float3 shapePos = worldPos * _CloudNoiseScale + windOffset * 0.003;
    float3 detailPos = worldPos * _CloudDetailNoiseScale + windOffset * 0.008;

    float shapeFBM = WeightedNoise(SAMPLE_TEXTURE3D_LOD(_CloudShapeNoise, sampler_CloudShapeNoise, shapePos, 0), _CloudShapeWeights);
    float detailFBM = dot(SAMPLE_TEXTURE3D_LOD(_CloudDetailNoise, sampler_CloudDetailNoise, detailPos, 0).rgb,
        float3(0.5, 0.35, 0.15));

    float bottomFade = smoothstep(0.0, max(_CloudBottomFeather, 0.0001), height01);
    float topFade = 1.0 - smoothstep(1.0 - saturate(_CloudTopFeather), 1.0, height01);
    float verticalShape = bottomFade * topFade;
    float topBias = lerp(1.0, pow(saturate(height01), max(_CloudTopDensityBias, 0.001)), 0.35);

    float frontShape = condensation * verticalShape;
    float cloudShape = shapeFBM * frontShape * topBias;
    float density = saturate((cloudShape - _CloudDensityThreshold) * _CloudShapeSharpness);

    float edgeErosion = (1.0 - detailFBM) * (1.0 - density) * _CloudDetailWeight;
    density = saturate(density - edgeErosion) * _CloudDensityMultiplier;

    sampleData.density = density;
    sampleData.height01 = height01;
    return sampleData;
}

float LightMarch(float3 pos, float lightStepSize, float2 pixel, int viewStep)
{
    float3 surfaceNormal = normalize(pos - _CloudPlanetCenter);
    float sunDot = dot(surfaceNormal, _SunParams.xyz);
    float sunVisibility = saturate((sunDot + 0.08) * 6.0);

    float totalDensity = 0;
    float jitterStrength = saturate(_CloudRayOffsetStrength);
    float viewStepF = (float)viewStep;
    float lightStartJitter = (Hash12(pixel + float2(viewStepF * 37.17, 13.73)) - 0.5)
        * lightStepSize
        * jitterStrength;
    float3 lightPos = pos + _SunParams.xyz * lightStartJitter;

    for (int i = 0; i < _CloudLightSteps; i++)
    {
        float lightStepF = (float)i;
        float perStepJitter = (Hash12(pixel + float2(viewStepF * 19.31 + lightStepF * 7.11, lightStepF * 43.17)) - 0.5)
            * lightStepSize
            * jitterStrength
            * 0.35;
        lightPos += _SunParams.xyz * lightStepSize;
        totalDensity += SampleCloud(lightPos + _SunParams.xyz * perStepJitter).density * lightStepSize;
    }

    float transmittance = exp(-totalDensity * _CloudLightAbsorption);
    float lit = _CloudDarknessThreshold + transmittance * (1.0 - _CloudDarknessThreshold);
    return lit * sunVisibility;
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
                if (_PlanetRadius > 0.0)
                {
                    float2 oceanHit = RaySphere(_CloudPlanetCenter, _PlanetRadius, rayOrigin, rayDir);
                    if (oceanHit.y > 0.0)
                        sceneDepth = min(sceneDepth, oceanHit.x);
                }

                float2 outerHit = RaySphere(_CloudPlanetCenter, _CloudOuterRadius, rayOrigin, rayDir);
                if (outerHit.y <= 0)
                    return sceneColor;

                float startDistance = outerHit.x;
                float endDistance = min(outerHit.x + outerHit.y, sceneDepth);

                float cameraRadius = length(rayOrigin - _CloudPlanetCenter);
                if (cameraRadius < _CloudInnerRadius)
                {
                    float2 innerHit = RaySphere(_CloudPlanetCenter, _CloudInnerRadius, rayOrigin, rayDir);
                    startDistance = max(startDistance, innerHit.x + innerHit.y);
                }

                if (endDistance <= startDistance)
                    return sceneColor;

                int viewSteps = max(_CloudViewSteps, 1);
                float stepSize = (endDistance - startDistance) / viewSteps;
                float2 pixel = floor(i.uv * _ScreenParams.xy);
                float pixelJitter = lerp(
                    InterleavedGradientNoise(i.uv * _ScreenParams.xy),
                    Hash12(pixel),
                    0.7);
                float jitterStrength = saturate(_CloudRayOffsetStrength);

                float cosAngle = dot(rayDir, _SunParams.xyz);
                float phase = CloudPhase(cosAngle);
                float lightStepSize = max((_CloudOuterRadius - _CloudInnerRadius) / max(_CloudLightSteps, 1), 1.0);

                float transmittance = 1.0;
                float3 lightEnergy = 0;
                float debugWeather = 0.0;
                float debugStorm = 0.0;
                float debugDensity = 0.0;
                float debugSilverLining = 0.0;
                float debugMoistureSource = 0.0;
                float debugCondensationChange = 0.0;
                float debugCondensationSign = 0.0;

                UNITY_LOOP
                for (int s = 0; s < viewSteps; s++)
                {
                    float stepF = (float)s;
                    float stepNoise = Hash12(pixel + float2(stepF * 17.17 + pixelJitter, stepF * 61.31 + pixelJitter * 1.37));
                    float withinStep = saturate(lerp(0.5, stepNoise, jitterStrength));
                    float marchDistance = min(startDistance + stepSize * (stepF + withinStep), endDistance);
                    float3 jitteredSamplePos = rayOrigin + rayDir * marchDistance;
                    CloudSample cloud = SampleCloud(jitteredSamplePos);
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
                        float lightTransmittance = LightMarch(jitteredSamplePos, lightStepSize, pixel, s);
                        float3 surfaceNormal = normalize(jitteredSamplePos - _CloudPlanetCenter);
                        float localSun = saturate((dot(surfaceNormal, _SunParams.xyz) + 0.12) * 2.5);
                        float3 cloudAlbedo = lerp(_CloudColor.rgb, _CloudStormColor.rgb, cloud.storm);
                        float stormLight = lerp(1.0, 1.0 - _CloudStormDarkening, cloud.storm);
                        float ambientStrength = lerp(_CloudAmbientStrength * 0.12, _CloudAmbientStrength, localSun);
                        float ambient = (_NightAmbientIntensity * 0.25 + ambientStrength) * (0.35 + 0.65 * cloud.height01);
                        float3 lighting = cloudAlbedo * (lightTransmittance * phase * stormLight + ambient);

                        float density01 = saturate(cloud.density / max(_CloudDensityMultiplier, 0.0001));
                        float thinEdge = pow(saturate(1.0 - density01), max(_CloudSilverLiningParams.z, 0.001));
                        float forwardSun = pow(saturate(cosAngle), max(_CloudSilverLiningParams.y, 1.0));
                        float horizonSun = saturate((dot(surfaceNormal, _SunParams.xyz) + 0.35) * 1.6);
                        float stormSuppression = saturate(1.0 - cloud.storm * _CloudSilverLiningParams.w);
                        float silverLining = _CloudSilverLiningParams.x * forwardSun * thinEdge
                            * lightTransmittance * horizonSun * stormSuppression;
                        lighting += _CloudColor.rgb * silverLining;

                        float lightning = WeatherLightning(surfaceNormal, cloud.storm);
                        lighting += _WeatherLightningColor.rgb * lightning * lerp(0.75, 1.2, cloud.height01)
                            * lerp(0.7, 1.1, density01);

                        debugDensity = max(debugDensity, density01);
                        debugSilverLining = max(debugSilverLining, saturate(silverLining));

                        lightEnergy += cloud.density * stepSize * transmittance * lighting;
                        transmittance *= exp(-cloud.density * stepSize * _CloudLightAbsorption);

                        if (transmittance < 0.01)
                            break;
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
                    return float4(baseScene + debugColor * saturate(debugMask), sceneColor.a);
                }

                float3 result = sceneColor.rgb * transmittance + lightEnergy;
                return float4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
