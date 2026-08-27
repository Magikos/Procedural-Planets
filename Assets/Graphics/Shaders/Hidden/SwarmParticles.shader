Shader "Hidden/SwarmParticles"
{
    // Butterflies, fireflies and flies. One unlit billboard shader for all three: the dot is generated from
    // the quad's own UV rather than sampled, so there is no texture to author, import or keep in memory.
    //
    // Unlit ON PURPOSE. Every world shader here takes its light from the analytic planet sun
    // (Includes/PlanetSunLighting.hlsl) and none of them read Unity's additional lights, so a firefly cannot
    // light the ground it flies over whatever it does. It can only BE bright — which at night is the whole of
    // the effect, and is what a glowing sprite already does.
    //
    // Blend is a material property rather than two shaders: fireflies want additive glow, butterflies and
    // flies want ordinary alpha, and that is the only difference between them.

    Properties
    {
        _SrcBlend("Src Blend", Float) = 5      // SrcAlpha
        _DstBlend("Dst Blend", Float) = 10     // OneMinusSrcAlpha
        _Softness("Edge Softness", Range(0.01, 1)) = 0.35
        _Intensity("Intensity", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Name "Swarm"

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Softness;
            float _Intensity;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.pos = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // Round, soft-edged, and brightest at the middle. A firefly is mostly its own halo.
                float d = saturate(length(i.uv - 0.5) * 2.0);
                float alpha = 1.0 - smoothstep(1.0 - _Softness, 1.0, d);
                if (alpha <= 0.001) discard;

                half4 c = i.color;
                c.rgb *= _Intensity;
                c.a *= alpha;
                return c;
            }
            ENDHLSL
        }
    }
}
