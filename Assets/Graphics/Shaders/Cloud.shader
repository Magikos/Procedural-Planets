Shader "Hidden/Clouds"
{
HLSLINCLUDE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Includes/Math.hlsl"

TEXTURE2D(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);
TEXTURE2D(_Source);
SAMPLER(sampler_Source);

// Cloud shell
float3 _CloudPlanetCenter;
float _CloudInnerRadius;
float _CloudOuterRadius;

// Shape
float _CloudNoiseScale;
float _CloudDetailNoiseScale;
float _CloudDetailWeight;
float _CloudDensityMultiplier;
float _CloudDensityOffset;

// Lighting
float _CloudLightAbsorption;
float _CloudDarknessThreshold;
float4 _CloudPhaseParams;

// Animation
float _CloudAnimSpeed;

// Ray march
int _CloudViewSteps;
int _CloudLightSteps;

// Weather (from WeatherManager)
float3 _WindDirection;
float _WindSpeed;
float _CloudCoverage;

// Sun (from AtmosphereController)
float3 _SunParams;

// --- Noise ---

float Hash(float3 p)
{
    p = frac(p * float3(443.897, 441.423, 437.195));
    p += dot(p, p.yzx + 19.19);
    return frac((p.x + p.y) * p.z);
}

float ValueNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    return lerp(
        lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x),
             lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
        lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
             lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y),
        f.z);
}

float FBM(float3 p, int octaves)
{
    float value = 0;
    float amp = 0.5;
    float freq = 1;
    for (int i = 0; i < octaves; i++)
    {
        value += ValueNoise(p * freq) * amp;
        amp *= 0.5;
        freq *= 2.0;
    }
    return value;
}

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

// --- Cloud density ---

float SampleDensity(float3 worldPos)
{
    float3 relPos = worldPos - _CloudPlanetCenter;
    float dist = length(relPos);

    float thickness = _CloudOuterRadius - _CloudInnerRadius;
    float height01 = saturate((dist - _CloudInnerRadius) / thickness);

    // Height gradient: thickest in middle, thin at top/bottom edges
    float heightGradient = saturate(height01 * 2.0) * saturate((1.0 - height01) * 2.0);

    // Sample noise in world space so clouds are anchored and have 3D depth
    float time = _Time.y * _CloudAnimSpeed;
    float3 windOffset = _WindDirection * _WindSpeed * time * 10.0;
    float3 noisePos = (worldPos + windOffset) * _CloudNoiseScale * 0.001;

    float baseNoise = FBM(noisePos, 4);

    float3 detailPos = (worldPos + windOffset * 2.0) * _CloudDetailNoiseScale * 0.001;
    float detailNoise = FBM(detailPos + 7.7, 3);

    float density = baseNoise * heightGradient;
    density -= (1.0 - detailNoise) * _CloudDetailWeight * (1.0 - density);
    density += _CloudDensityOffset;
    density = saturate(density - (1.0 - _CloudCoverage));

    return max(0, density * _CloudDensityMultiplier);
}

// --- Light march ---

float LightMarch(float3 pos)
{
    // Check if this point can see the sun at all (night side = no light)
    float3 surfaceNormal = normalize(pos - _CloudPlanetCenter);
    float sunDot = dot(surfaceNormal, _SunParams.xyz);
    // Smooth transition: fully lit when sun > 10° above horizon, dark when below -5°
    float sunVisibility = saturate((sunDot + 0.09) * 5.0);
    if (sunVisibility <= 0) return 0;

    float3 dirToSun = _SunParams.xyz;
    float stepSize = (_CloudOuterRadius - _CloudInnerRadius) / (float)_CloudLightSteps;
    float totalDensity = 0;

    for (int i = 0; i < _CloudLightSteps; i++)
    {
        pos += dirToSun * stepSize;
        totalDensity += max(0, SampleDensity(pos)) * stepSize;
    }

    float transmittance = exp(-totalDensity * _CloudLightAbsorption);
    return transmittance * sunVisibility;
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
            #pragma target 4.0

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
                float viewLength = length(i.viewVector);
                float3 rayDir = i.viewVector / viewLength;
                float3 rayOrigin = _WorldSpaceCameraPos.xyz;

                // Scene depth
                float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, i.uv).r;
                float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;

                // Ray-sphere intersections for cloud shell
                float2 hitInner = RaySphere(_CloudPlanetCenter, _CloudInnerRadius, rayOrigin, rayDir);
                float2 hitOuter = RaySphere(_CloudPlanetCenter, _CloudOuterRadius, rayOrigin, rayDir);

                // No cloud intersection
                if (hitOuter.y <= 0) return sceneColor;

                // Calculate entry and exit distances through the cloud shell
                float camDist = length(rayOrigin - _CloudPlanetCenter);
                float dstToCloud, dstThroughCloud;

                if (camDist < _CloudInnerRadius)
                {
                    // Camera below clouds: enter at inner sphere, exit at inner sphere (going up through shell)
                    dstToCloud = hitInner.x;
                    dstThroughCloud = hitOuter.x + hitOuter.y - dstToCloud;
                }
                else if (camDist > _CloudOuterRadius)
                {
                    // Camera above clouds (space): enter at outer sphere near, exit at outer sphere far
                    dstToCloud = hitOuter.x;
                    // If ray also hits inner sphere, cloud shell is the gap
                    if (hitInner.y > 0)
                        dstThroughCloud = hitInner.x - dstToCloud;
                    else
                        dstThroughCloud = hitOuter.y;
                }
                else
                {
                    // Camera inside cloud shell
                    dstToCloud = 0;
                    dstThroughCloud = hitOuter.y;
                    if (hitInner.y > 0 && hitInner.x > 0)
                        dstThroughCloud = min(dstThroughCloud, hitInner.x);
                }

                // Clamp to scene depth (terrain in front of clouds)
                dstThroughCloud = min(dstThroughCloud, sceneDepth - dstToCloud);
                if (dstThroughCloud <= 0) return sceneColor;

                float stepSize = dstThroughCloud / (float)_CloudViewSteps;
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
                        float lightTransmittance = LightMarch(samplePos);
                        lightEnergy += density * stepSize * transmittance * lightTransmittance * phase;
                        transmittance *= exp(-density * stepSize * _CloudLightAbsorption);

                        if (transmittance < 0.01) break;
                    }

                    samplePos += rayDir * stepSize;
                }

                float3 cloudColor = lightEnergy;
                float3 result = sceneColor.rgb * transmittance + cloudColor;

                return float4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
