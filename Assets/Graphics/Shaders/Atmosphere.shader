Shader "Planet/Atmosphere"
{
    Properties
    {
        _AtmosphereColor ("Atmosphere Color", Color) = (0.3, 0.5, 1.0, 1.0)
        _SunsetColor ("Sunset Color", Color) = (1.0, 0.4, 0.1, 1.0)
        _FresnelPower ("Fresnel Power", Range(1, 8)) = 3.0
        _Intensity ("Intensity", Range(0, 3)) = 1.2
        _SunInfluence ("Sun Influence", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "Atmosphere"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _AtmosphereColor;
                float4 _SunsetColor;
                float _FresnelPower;
                float _Intensity;
                float _SunInfluence;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 viewDir = normalize(input.viewDirWS);

                // Fresnel: bright at edges, transparent at center
                // Cull Front means normals point inward, so flip
                float fresnel = pow(saturate(dot(normal, viewDir)), _FresnelPower);

                // Sun lighting
                Light mainLight = GetMainLight();
                float3 sunDir = normalize(mainLight.direction);
                float sunDot = dot(-normal, sunDir);

                // Day side brightness
                float dayAmount = saturate(sunDot);

                // Sunset zone: where sunDot is near 0 (terminator)
                float sunsetAmount = 1.0 - saturate(abs(sunDot) * 4.0);

                // Blend atmosphere and sunset colors
                float3 color = lerp(_AtmosphereColor.rgb, _SunsetColor.rgb, sunsetAmount * 0.5);

                // Final opacity: fresnel * sun influence
                float sunFade = lerp(1.0, saturate(dayAmount + 0.3), _SunInfluence);
                float alpha = fresnel * _Intensity * sunFade;

                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
