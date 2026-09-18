Shader "Planet/WaterSplash"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float3 _PlanetCenter, _SunParams;
            struct Attributes { float4 position : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(input.position.xyz);
                o.position = TransformWorldToHClip(world);
                float daylight = smoothstep(-.08, .18, dot(normalize(world - _PlanetCenter), normalize(_SunParams)));
                o.color = float4(input.color.rgb * lerp(.035, 1.0, daylight), input.color.a);
                o.uv = input.uv;
                return o;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float radius = length(input.uv * 2.0 - 1.0);
                return half4(input.color.rgb, input.color.a * (1.0 - smoothstep(.2, 1.0, radius)));
            }
            ENDHLSL
        }
    }
}
