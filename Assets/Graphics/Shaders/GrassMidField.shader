Shader "Planet/GrassMidField"
{
    // Deprecated A/B experiment. Production uses near grass, chunk grass, and
    // the far terrain blanket. Keep only until the mid-field diagnostics are
    // no longer needed.
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest+8"
        }

        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "GrassMidFieldForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex MidFieldVertex
            #pragma fragment MidFieldFragment
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Includes/PlanetSunLighting.hlsl"
            #include "Includes/GrassDither.hlsl"
            #include "Includes/GrassInteractors.hlsl"

            struct GrassCardInstance
            {
                float4 RootHeight;
                float4 UpWidth;
                float4 Color;
            };

            StructuredBuffer<GrassCardInstance> _MidFieldGrassInstances;

            float3 _WindDirection;
            float _WindSpeed;
            float3 _SunParams;
            float _NightAmbientIntensity;
            float3 _PlanetCenter;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 upWS : TEXCOORD1;
                float3 facingWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                float4 color : COLOR0;
            };

            uint HashUint(uint x)
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }

            float Hash01(uint seed)
            {
                return (HashUint(seed) & 0x00ffffffu) / 16777216.0;
            }

            float3 CardWindOffset(float3 rootWS, float3 upWS, float height, float top, uint seed)
            {
                float3 wind = PlanetSafeNormalize(_WindDirection, float3(1.0, 0.0, 0.0));
                wind -= upWS * dot(wind, upWS);
                wind = PlanetSafeNormalize(wind, float3(0.0, 0.0, 0.0));
                float phase = _Time.y * max(_WindSpeed, 0.05) * 1.15
                    - dot(rootWS, wind) * 0.12
                    + Hash01(seed) * 1.7;
                float displacement = sin(phase) * min(height * 0.16, max(_WindSpeed, 0.0) * height * 0.22);
                return wind * displacement * top
                    + SampleGrassInteractorBend(rootWS, upWS, height * 0.70) * top;
            }

            Varyings MidFieldVertex(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                GrassCardInstance card = _MidFieldGrassInstances[instanceID];
                float3 rootWS = card.RootHeight.xyz;
                float height = card.RootHeight.w;
                float3 upWS = PlanetSafeNormalize(card.UpWidth.xyz, normalize(rootWS - _PlanetCenter));
                float width = card.UpWidth.w;

                float2 uv;
                if (vertexID == 0u) uv = float2(0.0, 0.0);
                else if (vertexID == 1u) uv = float2(0.0, 1.0);
                else if (vertexID == 2u) uv = float2(1.0, 1.0);
                else if (vertexID == 3u) uv = float2(0.0, 0.0);
                else if (vertexID == 4u) uv = float2(1.0, 1.0);
                else uv = float2(1.0, 0.0);

                float3 towardCamera = _WorldSpaceCameraPos.xyz - rootWS;
                float3 facingWS = towardCamera - upWS * dot(towardCamera, upWS);
                float3 fallbackFacing = PlanetSafeNormalize(towardCamera, float3(0.0, 0.0, 1.0));
                facingWS = PlanetSafeNormalize(facingWS, fallbackFacing);
                float3 rightWS = PlanetSafeNormalize(cross(upWS, facingWS), float3(1.0, 0.0, 0.0));

                uint seed = HashUint(instanceID ^ asuint(rootWS.x) ^ asuint(rootWS.y) ^ asuint(rootWS.z));
                float top = uv.y;
                float3 positionWS = rootWS
                    + rightWS * ((uv.x - 0.5) * width)
                    + upWS * (top * height)
                    + CardWindOffset(rootWS, upWS, height, top * top, seed);

                float patch = lerp(0.88, 1.12, Hash01(seed ^ 0x6a09e667u));
                float warmCool = Hash01(seed ^ 0xbb67ae85u);
                float3 tintVariation = lerp(float3(0.90, 1.04, 0.91), float3(1.06, 0.99, 0.86), warmCool);

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.upWS = upWS;
                output.facingWS = facingWS;
                output.uv = uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.color = float4(saturate(card.Color.rgb * tintVariation * patch), card.Color.a);
                return output;
            }

            half4 MidFieldFragment(Varyings input) : SV_Target
            {
                float y = saturate(input.uv.y);
                float bladeCount = 7.0;
                float curve = sin(y * 5.1 + floor(input.uv.x * bladeCount) * 1.37) * (0.08 + y * 0.07);
                float strand = abs(frac(input.uv.x * bladeCount + curve) - 0.5) * 2.0;
                float strandWidth = lerp(0.82, 0.08, pow(y, 1.25));
                float antialias = max(fwidth(strand) * 1.25, 0.02);
                float shapeAlpha = 1.0 - smoothstep(strandWidth, strandWidth + antialias, strand);
                float edgeTaper = smoothstep(0.0, 0.035, input.uv.x)
                    * smoothstep(0.0, 0.035, 1.0 - input.uv.x);
                shapeAlpha *= edgeTaper;
                clip(shapeAlpha - 0.32);
                clip(input.color.a - SampleGrassDither(input.positionCS.xy));

                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 upWS = PlanetSafeNormalize(input.upWS, normalize(input.positionWS - _PlanetCenter));
                float3 normalWS = PlanetSafeNormalize(input.facingWS * 0.72 + upWS * 0.28, upWS);
                normalWS = dot(normalWS, viewDir) < 0.0 ? -normalWS : normalWS;

                float3 albedo = saturate(input.color.rgb * lerp(0.66, 1.04, y));
                float3 planetNormal = PlanetSafeNormalize(input.positionWS - _PlanetCenter, upWS);
                float3 sunDir = PlanetSunDirection(_SunParams, planetNormal);
                float localSun = dot(planetNormal, sunDir);
                float daylight = PlanetDaylightFromLocalSun(localSun);
                float surfaceDirect = PlanetSurfaceDirect(upWS, sunDir);
                float wrapDiffuse = saturate(dot(normalWS, sunDir) * 0.62 + 0.38);
                float3 dayColor = albedo * (0.15 + wrapDiffuse * surfaceDirect * 0.92);

                float horizonFactor = saturate(1.0 - abs(localSun) * 3.0);
                float backlit = pow(saturate(dot(viewDir, -sunDir)), 3.0)
                    * daylight * horizonFactor * surfaceDirect;
                dayColor += albedo * backlit * 0.18;

                float3 nightAlbedo = lerp(albedo, float3(0.10, 0.14, 0.20), 0.68);
                float3 nightColor = nightAlbedo * PlanetNightAmbient(_NightAmbientIntensity) * 0.65;
                float3 color = MixFog(lerp(nightColor, dayColor, daylight), input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
