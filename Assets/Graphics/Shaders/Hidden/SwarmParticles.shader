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
        [Toggle] _Wings("Two-lobed wing shape instead of a dot", Float) = 0
        _Tint("Tint", Color) = (1,1,1,1)
        _FlapAngle("Wing angle", Float) = 0
        _Bee("Bee markings", Float) = 0
        _ButterflyAtlas("Butterfly wing poses", 2D) = "white" {}
        _ButterflyFrame("Butterfly pose", Float) = 0
        _UseButterflyAtlas("Use butterfly atlas", Float) = 0
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
            float _Wings;
            float4 _Tint;
            float _FlapAngle;
            float _Bee;
            TEXTURE2D(_ButterflyAtlas);
            SAMPLER(sampler_ButterflyAtlas);
            float _ButterflyFrame;
            float _UseButterflyAtlas;

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
                v.positionOS.y += abs(v.positionOS.x) * sin(_FlapAngle);
                v.positionOS.x *= cos(_FlapAngle);
                o.pos = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = (_UseButterflyAtlas > 0.5 ? float4(1, 1, 1, 1) : v.color) * _Tint;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                if (_UseButterflyAtlas > 0.5)
                {
                    float2 uv = float2(i.uv.x, (i.uv.y + 7.0 - clamp(_ButterflyFrame, 0.0, 7.0)) / 8.0);
                    half4 art = SAMPLE_TEXTURE2D(_ButterflyAtlas, sampler_ButterflyAtlas, uv);
                    clip(art.a - 0.5);
                    return half4(art.rgb * i.color.rgb * _Intensity, art.a * i.color.a);
                }
                float2 q = (i.uv - 0.5) * 2.0;      // -1..1 across the quad

                // Round, soft-edged, brightest at the middle: a firefly is mostly its own halo.
                float dot2 = saturate(length(q));

                // Both wing halves share the hinge at local X=0.
                float2 wing = float2(0.42, 0.0);
                float2 radii = float2(0.62, 0.85);
                float wings = min(length((q - wing) / radii), length((q + wing) / radii));

                float d = lerp(dot2, saturate(wings), step(0.5, _Wings));
                float alpha = 1.0 - smoothstep(1.0 - _Softness, 1.0, d);
                if (alpha <= 0.001) discard;

                half4 c = i.color;
                float body = 1.0 - smoothstep(0.10, 0.20, abs(q.x));
                float stripe = step(0.0, sin(q.y * 18.0));
                c.rgb *= lerp(1.0, lerp(0.15, 1.0, stripe), _Bee * body);
                c.rgb *= _Intensity;
                c.a *= alpha;
                return c;
            }
            ENDHLSL
        }
    }
}
