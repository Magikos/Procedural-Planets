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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 color : COLOR;
                float fogFactor : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Smoothness;
            CBUFFER_END

            float _NightAmbientIntensity;
            float3 _SunParams;
            float3 _PlanetCenter;
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
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_OceanDebugMode == DEBUG_TERRAIN_SOURCE_PINK)
                    return half4(1.0, 0.0, 1.0, 1.0);

                if (_OceanDebugMode == DEBUG_TERRAIN_FACE_ID)
                    return half4(CubeFaceDebugColor(input.positionWS - _PlanetCenter), 1.0);

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
