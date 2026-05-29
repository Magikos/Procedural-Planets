Shader "Hidden/LoadingOverlay"
{
    // Fullscreen overlay for the loading screen.
    // Rendered via RenderPipelineManager.endCameraRendering — no UIDocument required.
    // _Alpha:    [0,1] opacity of the black backdrop
    // _Progress: [0,1] fill fraction of the progress bar
    //
    // All UV/position values use normalised screen space: (0,0) = bottom-left, (1,1) = top-right.
    // The fullscreen triangle vertices are in clip space and already produce uv.y=0 at the
    // visual bottom on all platforms — no UNITY_UV_STARTS_AT_TOP flip is needed here.

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZTest Always
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "LoadingOverlay"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Alpha;
            float _Progress;

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

                // Progress bar — centred on screen, normalised screen coords (y=0=bottom).
                const float barXMin  = 0.150;
                const float barXMax  = 0.850;
                const float barYMin  = 0.340;
                const float barYMax  = 0.360;
                const float border   = 0.003;   // border width in normalised units (~3 px at 1080p)

                float fillXMax = barXMin + saturate(_Progress) * (barXMax - barXMin);

                // Border rect (slightly larger than the inner bar).
                bool inBorderRect = uv.x >= barXMin - border && uv.x <= barXMax + border
                                 && uv.y >= barYMin - border && uv.y <= barYMax + border;

                // Inner bar rect.
                bool inInner = uv.x >= barXMin && uv.x <= barXMax
                            && uv.y >= barYMin && uv.y <= barYMax;

                bool inFill  = inInner && uv.x <= fillXMax;

                if (inBorderRect && !inInner)
                    return half4(1.0, 1.0, 1.0, _Alpha);           // border: white
                if (inFill)
                    return half4(1.0, 1.0, 1.0, _Alpha);           // filled: white
                if (inInner)
                    return half4(0.15, 0.15, 0.15, _Alpha);        // empty: dark grey

                return half4(0, 0, 0, _Alpha);                     // background: black
            }
            ENDHLSL
        }
    }
}
