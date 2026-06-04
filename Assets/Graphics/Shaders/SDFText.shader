// MSDF text rendering shader based on:
//   Valve / Chris Green — "Improved Alpha-Tested Magnification for Vector Textures and
//   Special Effects", SIGGRAPH 2007.  (single-channel SDF technique)
//
//   Viktor Chlumský / Sloup et al. — "Shape Decomposition for Multi-Channel Distance
//   Fields", EUROGRAPHICS 2018.  (MSDF — multi-channel, preserves sharp corners)
//
// Vertex positions must be supplied in normalised screen space:
//   (0,0) = bottom-left,  (1,1) = top-right.
// The vertex shader converts them to clip space.  This lets the mesh builder work
// entirely in screen-relative units without needing a special camera or matrix setup.
//
// For world-space text on a surface, use SDFTextWorld.shader (TODO).

Shader "Hidden/SDFText"
{
    Properties
    {
        _MainTex      ("Font Atlas (MSDF, Linear RGB)",   2D)           = "white" {}
        _FaceColor    ("Face Color",                      Color)         = (1, 1, 1, 1)
        _OutlineColor ("Outline Color",                   Color)         = (0, 0, 0, 0)
        // _OutlineWidth: 0 = no outline.  0.5 = outline fills all of the SDF spread.
        _OutlineWidth ("Outline Width [0..0.5]",          Range(0, 0.5)) = 0.0
        // Must match the -pxrange value used when generating the atlas.
        _PxRange      ("SDF Pixel Range",                 Float)         = 4.0
        // Clip rect in normalised screen space (xMin, yMin, xMax, yMax). Default = no clip.
        // Console sets this to the backdrop bounds so output text doesn't bleed past the panel.
        _ClipRect     ("Clip Rect (normalised screen)",   Vector)        = (-100, -100, 100, 100)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        ZTest     Always
        ZWrite    Off
        Cull      Off
        Blend     SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "SDFText"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ---------------------------------------------------------------
            // Uniforms
            // ---------------------------------------------------------------

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Unity populates _MainTex_TexelSize automatically:
            //   .x = 1/width,  .y = 1/height,  .z = width,  .w = height
            float4 _MainTex_TexelSize;

            float4 _FaceColor;
            float4 _OutlineColor;
            float  _OutlineWidth;
            float  _PxRange;
            float4 _ClipRect;

            // ---------------------------------------------------------------
            // Structs
            // ---------------------------------------------------------------

            struct Attributes
            {
                float3 positionOS : POSITION;   // normalised screen space [0..1]
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float2 screenPos  : TEXCOORD1;
            };

            // ---------------------------------------------------------------
            // Helpers
            // ---------------------------------------------------------------

            // MSDF median-of-three: returns the middle value of r, g, b.
            // This reconstructs the true signed distance from the three encoded
            // directional distances (see Chlumský 2018, Section 4).
            float Median(float r, float g, float b)
            {
                return max(min(r, g), min(max(r, g), b));
            }

            // ---------------------------------------------------------------
            // Vertex shader
            // ---------------------------------------------------------------

            Varyings Vert(Attributes i)
            {
                Varyings o;
                // Convert normalised screen space [0,1] → clip space [-1,1].
                // y=0 (bottom) maps to NDC −1; y=1 (top) maps to NDC +1.
                o.positionCS = float4(i.positionOS.x * 2.0 - 1.0,
                                      i.positionOS.y * 2.0 - 1.0,
                                      0.0, 1.0);
                o.uv        = i.uv;
                o.color     = i.color;
                o.screenPos = i.positionOS.xy;
                return o;
            }

            // ---------------------------------------------------------------
            // Fragment shader
            // ---------------------------------------------------------------

            half4 Frag(Varyings i) : SV_Target
            {
                // --- 0. Discard fragments outside the clip rect (per-instance bound). ---
                if (i.screenPos.x < _ClipRect.x || i.screenPos.x > _ClipRect.z ||
                    i.screenPos.y < _ClipRect.y || i.screenPos.y > _ClipRect.w)
                    discard;

                // --- 1. Sample the MSDF atlas and recover the distance value ---

                float3 msd  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                float  dist = Median(msd.r, msd.g, msd.b);

                // --- 2. Screen-space anti-aliasing (Chlumský reference impl.) ---
                //
                // unitRange:     the SDF spread expressed in UV units
                //                  = pxRange / atlasSize
                // screenTexSize: how many screen pixels one UV unit covers
                //                  = 1 / fwidth(uv)   (fwidth = abs(ddx) + abs(ddy))
                // screenPxRange: the SDF spread in screen pixels; clamped to ≥1
                //                so we always get at least 1-pixel anti-aliasing.

                float2 unitRange    = float2(_PxRange, _PxRange) * _MainTex_TexelSize.xy;
                float2 screenTxSize = 1.0 / abs(fwidth(i.uv));
                float  screenPxRange = max(0.5 * dot(unitRange, screenTxSize), 1.0);

                // --- 3. Compute opacities ---

                // Face (interior): smoothstep centred on the 0.5 boundary.
                float faceOpacity = saturate(screenPxRange * (dist - 0.5) + 0.5);

                // Outline: second boundary inset by _OutlineWidth from the face edge.
                //   _OutlineWidth = 0   → no outline
                //   _OutlineWidth = 0.5 → outline fills the full SDF spread
                float outlineOpacity = saturate(screenPxRange * (dist - (0.5 - _OutlineWidth)) + 0.5);

                // --- 4. Composite face over outline ---

                float4 faceColor    = i.color * _FaceColor;
                float4 outlineColor = _OutlineColor;

                float4 result;
                result.rgb = lerp(outlineColor.rgb, faceColor.rgb, faceOpacity);
                result.a   = lerp(outlineColor.a * outlineOpacity, faceColor.a, faceOpacity);

                return result;
            }
            ENDHLSL
        }
    }
}
