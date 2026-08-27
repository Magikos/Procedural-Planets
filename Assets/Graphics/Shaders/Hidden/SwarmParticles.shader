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
                float2 q = (i.uv - 0.5) * 2.0;      // -1..1 across the quad

                // Round, soft-edged, brightest at the middle: a firefly is mostly its own halo.
                float dot2 = saturate(length(q));

                // Two overlapping lobes, which at this size is all it takes to stop reading as a bead. The
                // particle's own rotation then flashes it edge-on and back, and that is the wingbeat.
                float2 wing = float2(0.42, 0.0);
                float2 radii = float2(0.62, 0.85);
                float wings = min(length((q - wing) / radii), length((q + wing) / radii));

                float d = lerp(dot2, saturate(wings), step(0.5, _Wings));
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
