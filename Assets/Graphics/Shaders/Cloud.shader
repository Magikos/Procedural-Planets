Shader "Hidden/Clouds"
{
HLSLINCLUDE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Includes/Math.hlsl"

TEXTURE2D(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);
TEXTURE2D(_Source);
SAMPLER(sampler_Source);

TEXTURE3D(_CloudShapeNoise);
SAMPLER(sampler_CloudShapeNoise);
TEXTURE3D(_CloudDetailNoise);
SAMPLER(sampler_CloudDetailNoise);

// Cloud instances
struct CloudData
{
    float3 position;
    float horizontalRadius;
    float verticalThickness;
    float density;
    float pad1, pad2;
};

StructuredBuffer<CloudData> _CloudBuffer;
int _CloudCount;

float3 _CloudPlanetCenter;

// Shape
float _CloudNoiseScale;
float _CloudDetailNoiseScale;
float _CloudDetailWeight;
float4 _CloudShapeWeights;
float _CloudDensityMultiplier;

// Lighting
float _CloudLightAbsorption;
float _CloudDarknessThreshold;
float4 _CloudPhaseParams;

// Animation
float _CloudAnimSpeed;

// Ray march
int _CloudViewSteps;
int _CloudLightSteps;

// Weather
float3 _WindDirection;
float _WindSpeed;

// Sun
float3 _SunParams;

// Night ambient
float _NightAmbientIntensity;

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
    return _CloudPhaseParams.z + lerp(back, forward, 0.5) * 0.5;
}

// --- Density at a point (checks all cloud instances) ---

float SampleDensity(float3 worldPos)
{
    float totalDensity = 0;

    for (int c = 0; c < _CloudCount; c++)
    {
        CloudData cloud = _CloudBuffer[c];
        float3 delta = worldPos - cloud.position;

        // Decompose into horizontal and vertical components relative to planet surface
        float3 surfaceNormal = normalize(cloud.position - _CloudPlanetCenter);
        float verticalDist = dot(delta, surfaceNormal);
        float3 horizontalDelta = delta - surfaceNormal * verticalDist;
        float horizontalDist = length(horizontalDelta);

        // Check if inside the ellipsoid bounding volume
        float hNorm = horizontalDist / cloud.horizontalRadius;
        float vNorm = verticalDist / (cloud.verticalThickness * 0.5);
        if (hNorm > 1.0 || abs(vNorm) > 1.0) continue;

        // Edge fade: soft boundary at the edges of the volume
        float hFade = saturate((1.0 - hNorm) * 3.0);
        float vFade = saturate((1.0 - abs(vNorm)) * 3.0);
        float edgeFade = hFade * vFade;

        // Height gradient: flat bottom, puffy top
        float height01 = vNorm * 0.5 + 0.5; // 0=bottom, 1=top
        float heightGradient = saturate(height01 / 0.2) * saturate((1.0 - height01) / 0.4);

        // Noise in local cloud space — tiles across the volume
        float3 localPos = float3(
            horizontalDelta.x / cloud.horizontalRadius,
            verticalDist / cloud.verticalThickness,
            horizontalDelta.z / cloud.horizontalRadius
        );
        float3 noisePos = localPos * 3.0 + cloud.position * 0.001;
        float4 shapeNoise = SAMPLE_TEXTURE3D_LOD(_CloudShapeNoise, sampler_CloudShapeNoise, noisePos, 0);
        float4 normalizedWeights = _CloudShapeWeights / dot(_CloudShapeWeights, 1);
        float shapeFBM = dot(shapeNoise, normalizedWeights);

        // Only noise peaks form cloud — creates irregular puffs within the volume
        float cloudShape = shapeFBM * heightGradient * edgeFade;
        float baseDensity = cloudShape - 0.45;
        if (baseDensity <= 0) continue;

        // Detail noise erodes for wispy edges
        float3 detailPos = localPos * 8.0 + cloud.position * 0.002;
        float4 detailNoise = SAMPLE_TEXTURE3D_LOD(_CloudDetailNoise, sampler_CloudDetailNoise, detailPos, 0);
        float detailFBM = dot(detailNoise.rgb, float3(0.5, 0.35, 0.15));
        float oneMinusShape = 1.0 - cloudShape;
        baseDensity -= (1.0 - detailFBM) * oneMinusShape * _CloudDetailWeight;

        totalDensity += max(0, baseDensity * cloud.density * 0.1);
    }

    return totalDensity * _CloudDensityMultiplier;
}

// --- Light march ---

float LightMarch(float3 pos, float stepSize)
{
    float3 surfaceNormal = normalize(pos - _CloudPlanetCenter);
    float sunDot = dot(surfaceNormal, _SunParams.xyz);
    float sunVisibility = saturate((sunDot + 0.09) * 5.0);

    float3 dirToSun = _SunParams.xyz;
    float totalDensity = 0;

    for (int i = 0; i < _CloudLightSteps; i++)
    {
        pos += dirToSun * stepSize;
        totalDensity += max(0, SampleDensity(pos)) * stepSize;
    }

    float transmittance = exp(-totalDensity * _CloudLightAbsorption);
    float lit = _CloudDarknessThreshold + transmittance * (1.0 - _CloudDarknessThreshold);
    return lit * sunVisibility;
}

// --- Find ray intersection with any cloud ---

// Returns (nearestEntry, totalTraversal) across all cloud spheres along the ray
float2 FindCloudIntersection(float3 rayOrigin, float3 rayDir, float maxDist)
{
    float nearestEntry = maxDist;
    float farthestExit = 0;
    bool anyHit = false;

    for (int c = 0; c < _CloudCount; c++)
    {
        CloudData cloud = _CloudBuffer[c];
        // Use horizontal radius as bounding sphere (conservative)
        float2 hit = RaySphere(cloud.position, cloud.horizontalRadius, rayOrigin, rayDir);

        if (hit.y > 0) // ray intersects this cloud
        {
            float entry = max(hit.x, 0);
            float exit = entry + hit.y;
            if (exit > 0 && entry < maxDist)
            {
                nearestEntry = min(nearestEntry, entry);
                farthestExit = max(farthestExit, min(exit, maxDist));
                anyHit = true;
            }
        }
    }

    if (!anyHit) return float2(0, 0);
    return float2(nearestEntry, farthestExit - nearestEntry);
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

                if (_CloudCount <= 0) return sceneColor;

                float viewLength = length(i.viewVector);
                float3 rayDir = i.viewVector / viewLength;
                float3 rayOrigin = _WorldSpaceCameraPos.xyz;

                float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, i.uv).r;
                float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;

                // Find if ray hits any cloud
                float2 cloudHit = FindCloudIntersection(rayOrigin, rayDir, sceneDepth);
                if (cloudHit.y <= 0) return sceneColor;

                float dstToCloud = cloudHit.x;
                float dstThroughClouds = cloudHit.y;

                float stepSize = dstThroughClouds / (float)_CloudViewSteps;
                float3 samplePos = rayOrigin + rayDir * (dstToCloud + stepSize * 0.5);

                float cosAngle = dot(rayDir, _SunParams.xyz);
                float phase = CloudPhase(cosAngle);

                float transmittance = 1.0;
                float3 lightEnergy = 0;

                for (int s = 0; s < _CloudViewSteps; s++)
                {
                    float density = SampleDensity(samplePos);

                    if (density > 0.001)
                    {
                        float lightTransmittance = LightMarch(samplePos, stepSize * 2.0);
                        lightEnergy += density * stepSize * transmittance * lightTransmittance * phase;
                        lightEnergy += density * stepSize * transmittance * _NightAmbientIntensity * 0.15;
                        transmittance *= exp(-density * stepSize * _CloudLightAbsorption);

                        if (transmittance < 0.01) break;
                    }

                    samplePos += rayDir * stepSize;
                }

                float3 cloudColor = saturate(lightEnergy);
                float3 result = sceneColor.rgb * transmittance + cloudColor;

                return float4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
