Shader "Planet/Clouds"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Front

        Pass
        {
            Name "CloudPass"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/Math.hlsl"

            // Cloud uniforms
            float3 _CloudPlanetCenter;
            float _CloudInnerRadius;
            float _CloudOuterRadius;
            float _CloudNoiseScale;
            float _CloudDetailNoiseScale;
            float _CloudDetailWeight;
            float _CloudDensityMultiplier;
            float _CloudDensityOffset;
            float _CloudLightAbsorption;
            float _CloudDarknessThreshold;
            float4 _CloudPhaseParams; // x=forward, y=back, z=baseBrightness
            float _CloudAnimSpeed;
            int _CloudViewSteps;
            int _CloudLightSteps;

            // Weather globals (set by WeatherManager)
            float3 _WindDirection;
            float _WindSpeed;
            float _CloudCoverage;

            // Sun (set by AtmosphereController)
            float3 _SunParams;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            // --- Noise functions ---

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

                float a = Hash(i);
                float b = Hash(i + float3(1, 0, 0));
                float c = Hash(i + float3(0, 1, 0));
                float d = Hash(i + float3(1, 1, 0));
                float e = Hash(i + float3(0, 0, 1));
                float g = Hash(i + float3(1, 0, 1));
                float h = Hash(i + float3(0, 1, 1));
                float k = Hash(i + float3(1, 1, 1));

                return lerp(
                    lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y),
                    lerp(lerp(e, g, f.x), lerp(h, k, f.x), f.y),
                    f.z);
            }

            float FBM(float3 p, int octaves)
            {
                float value = 0;
                float amplitude = 0.5;
                float frequency = 1;
                for (int i = 0; i < octaves; i++)
                {
                    value += ValueNoise(p * frequency) * amplitude;
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }
                return value;
            }

            // --- Phase function (Henyey-Greenstein) ---

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

            // --- Cloud density at a point ---

            float SampleDensity(float3 worldPos)
            {
                float3 relPos = worldPos - _CloudPlanetCenter;
                float dist = length(relPos);
                float3 normal = relPos / dist;

                // Height within cloud shell [0,1]
                float thickness = _CloudOuterRadius - _CloudInnerRadius;
                float height01 = saturate((dist - _CloudInnerRadius) / thickness);

                // Height gradient: clouds thickest in the middle, thin at edges
                float heightGradient = saturate(height01 * 2.0) * saturate((1.0 - height01) * 2.0);

                // Noise sampling position: use the direction on the sphere + wind offset
                float time = _Time.y * _CloudAnimSpeed;
                float3 windOffset = _WindDirection * _WindSpeed * time;
                float3 noisePos = normal * _CloudNoiseScale + windOffset * 0.01;

                // Base shape noise
                float baseNoise = FBM(noisePos, 4);

                // Detail noise (erodes edges)
                float3 detailPos = normal * _CloudDetailNoiseScale + windOffset * 0.02;
                float detailNoise = FBM(detailPos + 7.7, 3);

                // Combine
                float density = baseNoise * heightGradient;
                density -= (1.0 - detailNoise) * _CloudDetailWeight * (1.0 - density);
                density += _CloudDensityOffset;

                // Apply coverage from weather system
                density = saturate(density - (1.0 - _CloudCoverage));

                return max(0, density * _CloudDensityMultiplier);
            }

            // --- Fake light march (estimate light reaching this point from sun) ---

            float LightMarch(float3 pos)
            {
                float3 dirToSun = _SunParams.xyz;
                float stepSize = (_CloudOuterRadius - _CloudInnerRadius) / (float)_CloudLightSteps;
                float totalDensity = 0;

                for (int i = 0; i < _CloudLightSteps; i++)
                {
                    pos += dirToSun * stepSize;
                    totalDensity += max(0, SampleDensity(pos)) * stepSize;
                }

                float transmittance = exp(-totalDensity * _CloudLightAbsorption);
                return _CloudDarknessThreshold + transmittance * (1.0 - _CloudDarknessThreshold);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 rayOrigin = _WorldSpaceCameraPos.xyz;
                float3 rayDir = normalize(input.positionWS - rayOrigin);
                float3 planetRelative = rayOrigin - _CloudPlanetCenter;

                // Ray-sphere intersections for cloud shell
                float2 hitInner = RaySphere(_CloudPlanetCenter, _CloudInnerRadius, rayOrigin, rayDir);
                float2 hitOuter = RaySphere(_CloudPlanetCenter, _CloudOuterRadius, rayOrigin, rayDir);

                float dstToCloud = hitInner.x;
                float dstThroughCloud = hitOuter.y;

                if (dstThroughCloud <= 0) return 0;

                float camDist = length(planetRelative);

                // If camera is inside the cloud shell, start from camera
                if (camDist > _CloudInnerRadius && camDist < _CloudOuterRadius)
                    dstToCloud = 0;

                float stepSize = dstThroughCloud / (float)_CloudViewSteps;
                float3 samplePos = rayOrigin + rayDir * (dstToCloud + stepSize * 0.5);

                // Phase function
                float cosAngle = dot(rayDir, _SunParams.xyz);
                float phase = CloudPhase(cosAngle);

                // March through cloud shell
                float transmittance = 1.0;
                float3 lightEnergy = 0;

                for (int i = 0; i < _CloudViewSteps; i++)
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

                // Get sun color
                Light mainLight = GetMainLight();
                float3 cloudColor = lightEnergy * mainLight.color.rgb;

                float alpha = 1.0 - transmittance;
                return half4(cloudColor, alpha);
            }
            ENDHLSL
        }
    }
}
