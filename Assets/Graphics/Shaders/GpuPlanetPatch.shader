Shader "Planet/GpuChunkPatch"
{
    Properties
    {
        _Smoothness ("Smoothness", Range(0, 1)) = 0.0
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
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float elevation : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Smoothness;
                float _PlanetRadius;
                float _OceanLevel;
            CBUFFER_END

            StructuredBuffer<float4> _GpuTerrainVertices;
            StructuredBuffer<float4> _GpuTerrainNormals;

            float3 ElevationColor(float elevation)
            {
                float sea = _OceanLevel;
                float aboveSea = elevation - sea;

                if (aboveSea < -0.025) return float3(0.035, 0.11, 0.28);
                if (aboveSea < -0.004) return float3(0.12, 0.32, 0.44);
                if (aboveSea < 0.002) return float3(0.72, 0.66, 0.42);
                if (aboveSea < 0.018) return float3(0.28, 0.52, 0.25);
                if (aboveSea < 0.050) return float3(0.36, 0.42, 0.25);
                if (aboveSea < 0.090) return float3(0.50, 0.45, 0.36);
                return float3(0.86, 0.86, 0.82);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionOS = _GpuTerrainVertices[input.vertexID].xyz;
                float4 normalAndElevation = _GpuTerrainNormals[input.vertexID];
                float3 normalOS = normalize(normalAndElevation.xyz);

                VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.elevation = normalAndElevation.w;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.fogCoord = input.fogFactor;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = ElevationColor(input.elevation);
                surfaceData.alpha = 1.0;
                surfaceData.smoothness = _Smoothness;
                surfaceData.metallic = 0.0;
                surfaceData.occlusion = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }
}
