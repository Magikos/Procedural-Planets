Shader "Planet/Grass"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+10"
        }

        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "GrassForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex GrassVertex
            #pragma fragment GrassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct BladeInstance
            {
                float4 RootHeight; // xyz = root WS, w = blade height
                float4 UpWidth;    // xyz = up WS, w = root half-width
                float4 Color;
            };

            StructuredBuffer<BladeInstance> _GrassBladeInstances;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 color : COLOR0;
            };

            float3 AnyTangent(float3 normalWS)
            {
                float3 axis = abs(normalWS.y) < 0.92 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                return normalize(cross(axis, normalWS));
            }

            Varyings GrassVertex(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                BladeInstance blade = _GrassBladeInstances[instanceID];
                float3 rootWS = blade.RootHeight.xyz;
                float height = blade.RootHeight.w;
                float3 upWS = normalize(blade.UpWidth.xyz);
                float width = blade.UpWidth.w;

                uint segment = vertexID / 6u;
                uint localVertex = vertexID - segment * 6u;
                float t0 = (float)segment / 3.0;
                float t1 = (float)(segment + 1u) / 3.0;
                float t = (localVertex == 0u || localVertex == 1u || localVertex == 4u) ? t0 : t1;
                float side = (localVertex == 0u || localVertex == 2u || localVertex == 3u) ? -1.0 : 1.0;

                float3 tangentWS = AnyTangent(upWS);
                float widthAtT = width * (1.0 - t);
                float3 positionWS = rootWS + upWS * (height * t) + tangentWS * (side * widthAtT);

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalize(upWS + tangentWS * side * 0.18);
                output.color = blade.Color * lerp(0.55, 1.0, t);
                return output;
            }

            half4 GrassFragment(Varyings input) : SV_Target
            {
                float3 lightDir = normalize(float3(0.35, 0.8, 0.28));
                float ndl = saturate(dot(normalize(input.normalWS), lightDir) * 0.5 + 0.5);
                float3 color = input.color.rgb * lerp(0.55, 1.12, ndl);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
