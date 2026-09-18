Shader "Planet/Moon"
{
    Properties
    {
        _BaseMap ("Surface color", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Crater normals", 2D) = "bump" {}
        _BumpScale ("Crater strength", Range(0,2)) = 1
        _Brightness ("Brightness", Range(0,4)) = 1
        _Earthshine ("Dark side", Range(0,0.1)) = 0.01
        [HideInInspector] _MoonSunDirection ("Sun direction", Vector) = (0,1,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float4 _MoonSunDirection;
            float _BumpScale;
            float _Brightness;
            float _Earthshine;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "Moon"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 tangent = normalize(input.tangentWS.xyz);
                float3 bitangent = cross(normal, tangent) * input.tangentWS.w;
                float3 detail = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                normal = normalize(tangent * detail.x + bitangent * detail.y + normal * detail.z);
                float3 lightDirection = normalize(_MoonSunDirection.xyz);
                float lit = saturate(dot(normal, lightDirection));
                lit *= step(0.0, dot(input.normalWS, lightDirection));
                float facing = saturate(dot(normal, GetWorldSpaceNormalizeViewDir(input.positionWS)));
                // A rough lunar surface retains detail across the full disc instead of a glossy sphere highlight.
                float diffuse = saturate(2.0 * lit / max(lit + facing, 0.001));
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                return half4(albedo * (_Brightness * diffuse + _Earthshine), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
