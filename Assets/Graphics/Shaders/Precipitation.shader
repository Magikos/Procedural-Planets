Shader "Hidden/Precipitation"
{
HLSLINCLUDE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Includes/Math.hlsl"
#include "Includes/DebugModes.hlsl"
#include "Includes/WeatherSampling.hlsl"

TEXTURE2D(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);
TEXTURE2D(_Source);
SAMPLER(sampler_Source);

int _CloudWeatherResolution;

int _PrecipitationEnabled;
float3 _PrecipitationPlanetCenter;
float4 _PrecipitationRadii;
float4 _PrecipitationParams;
float4 _PrecipitationFadeParams;
float4 _PrecipitationVisualParams;
float4 _PrecipitationColor;
float4 _PrecipitationStormColor;
int _PrecipitationViewSteps;
int _PrecipitationDebugMode;
int _OceanDebugMode;
float4 _PrecipitationDebugDotParams;

// Platform quality - CLOUD_QUALITY_LOW keyword set by Shader.EnableKeyword on lower-tier hardware.
#ifdef CLOUD_QUALITY_LOW
    #define PRECIPITATION_MAX_STEPS 8
#else
    #define PRECIPITATION_MAX_STEPS 48
#endif

float3 _WindDirection;
float _WindSpeedMps;
float3 _SunParams;
float _NightAmbientIntensity;
float4 _WeatherLightningColor;


float SceneDepthScaled(float2 uv, float viewLength)
{
    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
    return LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;
}

float SceneDepthLinear(float2 uv)
{
    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
    return LinearEyeDepth(rawDepth, _ZBufferParams);
}

float PrecipitationSeaRadius()
{
    return _PrecipitationRadii.w > 1.0
        ? _PrecipitationRadii.w
        : max(_PrecipitationRadii.x - 25.0, 1.0);
}

float PrecipitationCameraAboveSea()
{
    float seaRadius = PrecipitationSeaRadius();
    float cameraRadius = length(_WorldSpaceCameraPos.xyz - _PrecipitationPlanetCenter);
    // Keep precipitation completely off below the waterline, then fade it in above the surface.
    return smoothstep(0.0, 12.0, cameraRadius - seaRadius);
}

float4 NoPrecipitationContribution(float4 sceneColor)
{
    return _OceanDebugMode == DEBUG_PRECIPITATION_CONTRIBUTION ? float4(0.0, 0.0, 0.0, 1.0) : sceneColor;
}

float ValueNoise(float2 p)
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

float3 DebugDotColor(float value)
{
    float3 green = float3(0.1, 1.0, 0.12);
    float3 yellow = float3(1.0, 0.86, 0.08);
    float3 red = float3(1.0, 0.08, 0.04);
    return value < 0.5
        ? lerp(green, yellow, value * 2.0)
        : lerp(yellow, red, (value - 0.5) * 2.0);
}

float4 RenderCellDotDebug(float4 sceneColor, float3 normal, int mode)
{
    float3 weatherDirection = mul((float3x3)_CloudWeatherRotation, normal);
    weatherDirection = dot(weatherDirection, weatherDirection) > 0.0001 ? normalize(weatherDirection) : normal;

    int face;
    float2 uv;
    CubeFaceUv(weatherDirection, face, uv);

    float4 weather = SAMPLE_TEXTURE2D_ARRAY_LOD(_CloudWeatherMap, sampler_CloudWeatherMap, uv, face, 0);
    float4 dynamics = SAMPLE_TEXTURE2D_ARRAY_LOD(_WeatherDynamicsMap, sampler_WeatherDynamicsMap, uv, face, 0);
    float value = mode == 2 ? dynamics.b : weather.g;

    float gridResolution = max((float)_CloudWeatherResolution, 1.0);
    float2 cellUv = frac(uv * gridResolution);
    float centerDistance = length((cellUv - 0.5) * 2.0);
    float dotRadius = lerp(_PrecipitationDebugDotParams.x, _PrecipitationDebugDotParams.y, saturate(value));
    float dotMask = 1.0 - smoothstep(dotRadius, dotRadius + 0.055, centerDistance);
    float valueVisibility = lerp(0.34, 1.0, saturate(value));
    float alpha = dotMask * valueVisibility * _PrecipitationDebugDotParams.z;

    float3 debugColor = DebugDotColor(saturate(value));
    return float4(lerp(sceneColor.rgb * 0.42, debugColor, alpha), sceneColor.a);
}

float SamplePrecipitationSignal(float3 worldPos, out float storm, out float rainRate, out float height01)
{
    storm = 0.0;
    rainRate = 0.0;
    height01 = 0.0;

    float bottomRadius = _PrecipitationRadii.x;
    float topRadius = _PrecipitationRadii.y;
    float layerThickness = max(topRadius - bottomRadius, 1.0);

    float3 fromCenter = worldPos - _PrecipitationPlanetCenter;
    float radius = length(fromCenter);
    if (radius < bottomRadius || radius > topRadius)
        return 0.0;

    float3 normal = fromCenter / max(radius, 0.0001);
    height01 = saturate((radius - bottomRadius) / layerThickness);

    float4 weather = SampleWeather(normal);
    float4 dynamics = SampleDynamics(normal);
    rainRate = saturate(dynamics.b);
    storm = saturate(weather.g);
    float stormGate = smoothstep(_PrecipitationParams.y,
        min(1.0, _PrecipitationParams.y + _PrecipitationParams.z), storm);
    float cloudSupport = smoothstep(0.58, 0.9, weather.r);
    return rainRate * stormGate * cloudSupport * _PrecipitationParams.x;
}

float SamplePrecipitationDensity(float3 worldPos)
{
    float storm;
    float rainRate;
    float height01;
    float precipitation = SamplePrecipitationSignal(worldPos, storm, rainRate, height01);
    if (precipitation <= 0.0001)
        return 0.0;

    float bottomRadius = _PrecipitationRadii.x;
    float topRadius = _PrecipitationRadii.y;
    float layerThickness = max(topRadius - bottomRadius, 1.0);
    float3 fromCenter = worldPos - _PrecipitationPlanetCenter;
    float radius = length(fromCenter);
    float3 normal = fromCenter / max(radius, 0.0001);
    float bottomFade = smoothstep(0.0, max(_PrecipitationFadeParams.x, 0.0001), height01);
    float topFade = 1.0 - smoothstep(1.0 - saturate(_PrecipitationFadeParams.y), 1.0, height01);

    float3 reference = abs(normal.y) > 0.92 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
    float3 tangent = normalize(cross(reference, normal));
    float3 bitangent = normalize(cross(normal, tangent));
    float3 wind = dot(_WindDirection, _WindDirection) > 0.0001 ? normalize(_WindDirection) : float3(1.0, 0.0, 0.0);
    float2 windTangent = float2(dot(wind, tangent), dot(wind, bitangent));

    float3 weatherDirection = mul((float3x3)_CloudWeatherRotation, normal);
    weatherDirection = dot(weatherDirection, weatherDirection) > 0.0001 ? normalize(weatherDirection) : normal;
    int face;
    float2 faceUv;
    CubeFaceUv(weatherDirection, face, faceUv);

    float faceOffset = (float)face * 613.17;
    float2 local = (faceUv - 0.5) * bottomRadius * 2.0 + faceOffset;
    local += windTangent * ((1.0 - height01) * _PrecipitationVisualParams.z * layerThickness);
    // Noise coordinates move opposite feature motion, so subtract to advect toward +wind.
    local -= windTangent * (_GameTime * _WindSpeedMps);

    float curtainScale = max(_PrecipitationVisualParams.x, 1.0);
    float large = ValueNoise(local / curtainScale);
    large = lerp(large, ValueNoise(local / (curtainScale * 0.43) + 19.7), 0.35);
    float curtain = smoothstep(0.22, 0.88, large);
    float fineBreakup = ValueNoise(local / max(curtainScale * 0.18, 8.0) + float2(_GameTime * 0.21, -_GameTime * 0.13));
    float heightBreakup = ValueNoise(float2(height01 * 43.0 + faceOffset * 0.007, dot(local, float2(0.013, 0.019))));

    float stormWeight = lerp(0.6, 1.15, saturate(storm));
    return precipitation
        * bottomFade
        * topFade
        * lerp(0.36, 1.0, curtain)
        * lerp(0.82, 1.08, fineBreakup)
        * lerp(0.76, 1.06, heightBreakup)
        * stormWeight;
}

ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Off

        Pass
        {
            Name "RenderPrecipitation"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile _ CLOUD_QUALITY_LOW

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

                if (_PrecipitationEnabled == 0 || _CloudWeatherResolution <= 0)
                    return NoPrecipitationContribution(sceneColor);

                float cameraAboveSea = PrecipitationCameraAboveSea();
                if (cameraAboveSea <= 0.0001)
                    return NoPrecipitationContribution(sceneColor);

                float bottomRadius = _PrecipitationRadii.x;
                float topRadius = _PrecipitationRadii.y;
                if (topRadius <= bottomRadius)
                    return NoPrecipitationContribution(sceneColor);

                float viewLength = length(i.viewVector);
                float3 rayDir = i.viewVector / max(viewLength, 0.0001);
                float3 rayOrigin = _WorldSpaceCameraPos.xyz;

                float sceneDepth = SceneDepthScaled(i.uv, viewLength);
                sceneDepth = min(sceneDepth, _PrecipitationRadii.z);

                float2 outerHit = RaySphere(_PrecipitationPlanetCenter, topRadius, rayOrigin, rayDir);
                if (outerHit.y <= 0.0)
                    return NoPrecipitationContribution(sceneColor);

                float startDistance = outerHit.x;
                float endDistance = min(outerHit.x + outerHit.y, sceneDepth);

                // Clip precipitation to the sea-horizon geometry so distant rain cannot render
                // behind the curved water/planet silhouette when depth textures become ambiguous.
                float seaRadius = PrecipitationSeaRadius();
                float2 seaHit = RaySphere(_PrecipitationPlanetCenter, seaRadius, rayOrigin, rayDir);
                if (seaHit.y > 0.0 && seaHit.x > 0.0)
                    endDistance = min(endDistance, seaHit.x);

                if (endDistance <= startDistance)
                    return NoPrecipitationContribution(sceneColor);

                if (_PrecipitationDebugMode == 2 || _PrecipitationDebugMode == 3)
                {
                    float debugDistance = lerp(startDistance, endDistance, 0.35);
                    float3 debugPos = rayOrigin + rayDir * debugDistance;
                    float3 debugNormal = normalize(debugPos - _PrecipitationPlanetCenter);
                    return RenderCellDotDebug(sceneColor, debugNormal, _PrecipitationDebugMode);
                }

                int steps = min(max(_PrecipitationViewSteps, 4), PRECIPITATION_MAX_STEPS);
                float stepSize = (endDistance - startDistance) / steps;
                float2 pixel = floor(i.uv * _ScreenParams.xy);
                float pixelJitter = Hash12(pixel);
                float3 samplePos = rayOrigin + rayDir * (startDistance + stepSize * lerp(0.2, 0.8, pixelJitter));

                float alpha = 0.0;
                float weightedStorm = 0.0;
                float weightedLightning = 0.0;
                float debugMask = 0.0;

                UNITY_LOOP
                for (int s = 0; s < 48; s++)
                {
                    if (s >= steps)
                        break;

                    float stepJitter = (Hash12(pixel + float2(s * 19.19, s * 47.23)) - 0.5) * stepSize * 0.52;
                    float3 jitteredSamplePos = samplePos + rayDir * stepJitter;
                    float density = SamplePrecipitationDensity(jitteredSamplePos);
                    debugMask = max(debugMask, density);
                    if (density > 0.0001)
                    {
                        float3 normal = normalize(jitteredSamplePos - _PrecipitationPlanetCenter);
                        float storm = SampleWeather(normal).g;
                        float lightning = WeatherLightning(normal, storm);
                        float distanceFade = saturate(1.0 - (length(jitteredSamplePos - rayOrigin) / max(_PrecipitationRadii.z, 1.0)) * 0.35);
                        float sampleAlpha = saturate(density * stepSize * 0.0048 * distanceFade);
                        sampleAlpha *= 1.0 - alpha;
                        alpha += sampleAlpha;
                        weightedStorm += storm * sampleAlpha;
                        weightedLightning += lightning * sampleAlpha;

                        if (alpha > _PrecipitationParams.w)
                            break;
                    }

                    samplePos += rayDir * stepSize;
                }

                if (_PrecipitationDebugMode == 1)
                {
                    float3 debugColor = lerp(float3(0.02, 0.04, 0.06), float3(0.1, 0.75, 1.0), saturate(debugMask));
                    return float4(sceneColor.rgb * 0.35 + debugColor * saturate(debugMask), sceneColor.a);
                }

                alpha = min(alpha, _PrecipitationParams.w);
                alpha *= cameraAboveSea;
                if (alpha <= 0.0001)
                    return NoPrecipitationContribution(sceneColor);

                float averageStorm = weightedStorm / max(alpha, 0.0001);
                float averageLightning = weightedLightning / max(alpha, 0.0001);
                float rainLightning = averageLightning * _WeatherLightningColor.a;
                float3 toCamera = normalize(rayOrigin - _PrecipitationPlanetCenter);
                float localSun = saturate((dot(toCamera, _SunParams.xyz) + 0.1) * 3.0);
                float stormDim = lerp(1.0, 0.28, saturate(averageStorm));
                float light = _NightAmbientIntensity * 0.45 + localSun * 0.85 * stormDim + 0.12 + rainLightning * 0.65;
                float3 rainColor = lerp(_PrecipitationColor.rgb, _PrecipitationStormColor.rgb, saturate(averageStorm));
                float3 result = lerp(sceneColor.rgb, rainColor * light + _WeatherLightningColor.rgb * rainLightning * 0.28, alpha);

                if (_OceanDebugMode == DEBUG_PRECIPITATION_CONTRIBUTION)
                    return float4(ContributionHeat(result - sceneColor.rgb, 10.0, float3(0.55, 0.20, 1.0), float3(1.0, 0.30, 0.55)), 1.0);

                return float4(result, sceneColor.a);
            }
            ENDHLSL
        }

    }
}
