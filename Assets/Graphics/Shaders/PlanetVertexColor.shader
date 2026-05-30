Shader "Planet/VertexColor"
{
    Properties
    {
        _Smoothness ("Smoothness", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/CloudShadows.hlsl"
            #include "Includes/DebugModes.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                // UV2: per-vertex biome diagnostics baked by ColorGenerator + TerrainFace.
                // x = temperature (0..1), y = moisture (0..1),
                // z = primaryBiomeId normalized to biome count, w = |latitude| (0=equator, 1=pole).
                float4 biomeData : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 color : COLOR;
                float fogFactor : TEXCOORD2;
                float4 biomeData : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Smoothness;
            CBUFFER_END

            float _NightAmbientIntensity;
            float3 _SunParams;
            float3 _PlanetCenter;
            float _SeaLevelRadius;
            int _OceanDebugMode;

            float3 CubeFaceDebugColor(float3 directionWS)
            {
                float3 direction = normalize(directionWS);
                float3 axis = abs(direction);

                if (axis.x >= axis.y && axis.x >= axis.z)
                    return direction.x >= 0.0 ? float3(1.0, 0.15, 0.08) : float3(0.52, 0.10, 1.0);

                if (axis.y >= axis.z)
                    return direction.y >= 0.0 ? float3(0.10, 0.95, 0.18) : float3(1.0, 0.82, 0.08);

                return direction.z >= 0.0 ? float3(0.08, 0.45, 1.0) : float3(1.0, 0.18, 0.78);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                output.biomeData = input.biomeData;
                return output;
            }

            // Maps a normalized [0..1] biome id to a distinct hue. HSV->RGB so adjacent
            // ids land far apart in colour space and each biome reads as its own flat patch.
            float3 BiomeIdColor(float idNorm)
            {
                float h = frac(idNorm * 7.0 + 0.13) * 6.0;
                float3 rgb = saturate(float3(
                    abs(h - 3.0) - 1.0,
                    2.0 - abs(h - 2.0),
                    2.0 - abs(h - 4.0)));
                return rgb;
            }

            float3 HeatmapBlueRed(float t)
            {
                t = saturate(t);
                return float3(t, 1.0 - abs(t * 2.0 - 1.0), 1.0 - t);
            }

            float3 HeatmapYellowBlue(float t)
            {
                t = saturate(t);
                return lerp(float3(0.95, 0.78, 0.10), float3(0.10, 0.32, 0.95), t);
            }

            float3 ElevationBandColor(float elevationOverSea, float planetRadius)
            {
                // 7 bands keyed to fractions of planet radius — quick read of altitude profile.
                float rel = elevationOverSea / max(planetRadius, 1.0);
                if (rel < -0.020) return float3(0.04, 0.12, 0.40); // deep ocean
                if (rel < -0.005) return float3(0.18, 0.45, 0.75); // shallow ocean
                if (rel <  0.001) return float3(0.92, 0.84, 0.55); // beach / sea level
                if (rel <  0.010) return float3(0.45, 0.70, 0.32); // lowland
                if (rel <  0.030) return float3(0.30, 0.55, 0.22); // upland
                if (rel <  0.060) return float3(0.55, 0.48, 0.32); // hill
                if (rel <  0.100) return float3(0.70, 0.65, 0.55); // mountain
                return float3(1.00, 1.00, 1.00);                   // peak
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_OceanDebugMode == DEBUG_TERRAIN_SOURCE_PINK)
                    return half4(1.0, 0.0, 1.0, 1.0);

                if (_OceanDebugMode == DEBUG_TERRAIN_FACE_ID)
                    return half4(CubeFaceDebugColor(input.positionWS - _PlanetCenter), 1.0);

                if (_OceanDebugMode == DEBUG_BIOME_PRIMARY_ID)
                    return half4(BiomeIdColor(input.biomeData.z), 1.0);

                if (_OceanDebugMode == DEBUG_BIOME_TEMPERATURE)
                    return half4(HeatmapBlueRed(input.biomeData.x), 1.0);

                if (_OceanDebugMode == DEBUG_BIOME_MOISTURE)
                    return half4(HeatmapYellowBlue(input.biomeData.y), 1.0);

                if (_OceanDebugMode == DEBUG_BIOME_LATITUDE)
                {
                    float lat = saturate(input.biomeData.w);
                    return half4(lat, 1.0 - lat, lat * 0.5, 1.0);
                }

                if (_OceanDebugMode == DEBUG_BIOME_ELEVATION_BAND)
                {
                    float elevationOverSea = length(input.positionWS - _PlanetCenter) - _SeaLevelRadius;
                    return half4(ElevationBandColor(elevationOverSea, _SeaLevelRadius), 1.0);
                }

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.fogCoord = input.fogFactor;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = input.color.rgb;
                surfaceData.alpha = 1;
                surfaceData.smoothness = _Smoothness;
                surfaceData.metallic = 0;
                surfaceData.occlusion = 1;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                float3 planetNormal = normalize(input.positionWS - _PlanetCenter);
                float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : float3(0.0, 1.0, 0.0);
                float localSun = dot(planetNormal, sunDir);
                float daylight = smoothstep(-0.08, 0.18, localSun);
                float nightSide = 1.0 - daylight;

                color.rgb *= lerp(0.34, 1.0, daylight);

                float3 coolNightAlbedo = lerp(input.color.rgb, float3(0.12, 0.16, 0.22), 0.65);
                float nightAmbient = max(_NightAmbientIntensity, 0.035);
                color.rgb += coolNightAlbedo * nightAmbient * 0.65 * nightSide;

                color.rgb *= CloudShadowFactor(input.positionWS, sunDir, localSun);

                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
}
