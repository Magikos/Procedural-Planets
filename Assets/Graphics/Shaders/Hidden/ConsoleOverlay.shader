Shader "Hidden/ConsoleOverlay"
{
    // Tinted-glass backdrop for the debug console.
    // Rendered via RenderPipelineManager.endCameraRendering — fullscreen triangle, masked by _BoundsRect.
    //
    // _BoundsRect:       (xMin, yMin, xMax, yMax) in normalised screen space (y=0 at bottom)
    // _BackdropColor:    inside-rect tint
    // _BorderColor:      edge tint (within _BorderThickness of the rect edge)
    // _BorderThickness:  normalised-screen units (e.g. 0.0025 ≈ 2.7 px at 1080p)
    // _Alpha:            [0,1] global opacity multiplier (animates 0→1 on open)
    // _ScanlineStrength: [0,1] retro scanline intensity (0 = clean)

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZTest Always
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ConsoleOverlay"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _BoundsRect;
            float4 _BackdropColor;
            float4 _BorderColor;
            float  _BorderThickness;
            float  _Alpha;
            float  _ScanlineStrength;

            struct Attributes { uint id : SV_VertexID; };
            struct Varyings   { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.uv  = float2((i.id << 1) & 2, i.id & 2);
                o.pos = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;

                // Outside the bounds rect: transparent (game shows through).
                bool inside = uv.x >= _BoundsRect.x && uv.x <= _BoundsRect.z
                           && uv.y >= _BoundsRect.y && uv.y <= _BoundsRect.w;
                if (!inside)
                    return half4(0, 0, 0, 0);

                // Border band: within _BorderThickness of any edge.
                float distLeft   = uv.x - _BoundsRect.x;
                float distRight  = _BoundsRect.z - uv.x;
                float distBottom = uv.y - _BoundsRect.y;
                float distTop    = _BoundsRect.w - uv.y;
                float minEdge = min(min(distLeft, distRight), min(distBottom, distTop));

                half4 color = (minEdge <= _BorderThickness) ? _BorderColor : _BackdropColor;

                // Optional scanlines — darken every other row.
                if (_ScanlineStrength > 0.0001)
                {
                    float row = floor(uv.y * 540.0);   // ~half of 1080
                    float scan = (frac(row * 0.5) < 0.25) ? (1.0 - _ScanlineStrength * 0.15) : 1.0;
                    color.rgb *= scan;
                }

                color.a *= _Alpha;
                return color;
            }
            ENDHLSL
        }
    }
}
