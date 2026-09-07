Shader "Hidden/CloudBlur"
{
    // Depth/opacity-aware softening of the volumetric cloud layer. The cloud raymarch stores its
    // opacity in alpha and, with a low step budget and no spatial filter, resolves as grain and
    // step banding. This pass blurs only where cloud is present (opacity-weighted), so terrain and
    // clear sky (alpha ~0) stay pixel-exact while cloud bodies and their noisy edges soften.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Off

        Pass
        {
            Name "CloudBlur"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Source);
            SAMPLER(sampler_Source);

            // x = strength (0..1 blend toward blurred), y = radius in texels, z/w unused.
            float4 _CloudBlurParams;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(uint vertexID : SV_VertexID)
            {
                v2f o;
                o.pos = GetFullScreenTriangleVertexPosition(vertexID);
                o.uv = GetFullScreenTriangleTexCoord(vertexID);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D(_Source, sampler_Source, i.uv);
                if (c.a <= 0.02) return c;

                float strength = _CloudBlurParams.x > 0.0 ? saturate(_CloudBlurParams.x) : 0.9;
                float radius = _CloudBlurParams.y > 0.0 ? _CloudBlurParams.y : 2.5;
                const float sigma = 1.6;

                float2 texel = (1.0 / _ScreenParams.xy) * radius;

                float3 rgbSum = 0.0;
                float wSum = 0.0;   // opacity-weighted (for colour, keeps terrain/sky out)
                float aSum = 0.0;   // gaussian-weighted (for the softened opacity)
                float gSum = 0.0;

                [unroll]
                for (int y = -2; y <= 2; y++)
                {
                    [unroll]
                    for (int x = -2; x <= 2; x++)
                    {
                        float2 off = float2(x, y);
                        float g = exp(-dot(off, off) / (2.0 * sigma * sigma));
                        float4 n = SAMPLE_TEXTURE2D(_Source, sampler_Source, i.uv + off * texel);
                        float wc = n.a * g;
                        rgbSum += n.rgb * wc;
                        wSum += wc;
                        aSum += n.a * g;
                        gSum += g;
                    }
                }

                float3 blurRGB = wSum > 1e-4 ? rgbSum / wSum : c.rgb;
                float blurA = gSum > 1e-4 ? aSum / gSum : c.a;

                // Gate on THIS pixel's cloud opacity so terrain and empty sky never blur (even
                // right next to a cloud) — no halos. Cloud interiors soften fully, edges partially.
                float presence = smoothstep(0.02, 0.25, c.a);
                float k = strength * presence;

                return float4(lerp(c.rgb, blurRGB, k), lerp(c.a, blurA, k));
            }
            ENDHLSL
        }
    }
}
