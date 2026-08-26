Shader "Hidden/PredatorVision"
{
    // Debug view for finding wildlife. The world is multiplied down to a dull blue and every creature is
    // marked in a hot colour that ignores depth, so an animal is findable through a hill and a 45 cm rabbit
    // is still a visible dot at two hundred metres.
    //
    // Drawn from RenderPipelineManager.endCameraRendering, the same way the console and loading overlays are,
    // so it needs no renderer feature and no change to the URP renderer asset.
    //
    // Pass 0  fullscreen tint   _TintColor       multiplied over the scene
    // Pass 1  creature marker   _MarkerColor     constant screen size, _MarkerPixels across

    // Declared, not merely used in HLSL. An undeclared uniform still binds, but it reports HasProperty=false,
    // and this project has already lost time to a SetColor that silently did nothing against a shader whose
    // Properties block lacked the name.
    Properties
    {
        _TintColor("World Tint (multiplied)", Color) = (0.10, 0.17, 0.38, 1)
        _MarkerColor("Marker Color", Color) = (1, 0.16, 0.10, 1)
        _MarkerPixels("Marker Size (pixels)", Float) = 26
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PredatorTint"

            ZTest Always
            ZWrite Off
            Cull Off
            // Multiply rather than alpha-blend: it crushes the red and green channels instead of washing a
            // flat colour over everything, so the scene stays readable as shape while going cold and dim.
            Blend DstColor Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _TintColor;

            struct Attributes { uint id : SV_VertexID; };
            struct Varyings   { float4 pos : SV_POSITION; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                float2 uv = float2((i.id << 1) & 2, i.id & 2);
                o.pos = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return _TintColor; }
            ENDHLSL
        }

        Pass
        {
            Name "PredatorMarker"

            // Always on top: the whole point is seeing an animal that is behind terrain.
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _MarkerColor;
            float  _MarkerPixels;

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings   { float4 pos : SV_POSITION; float2 local : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;

                // The quad's own vertices are only used as corner offsets; the marker is pinned to the
                // object's ORIGIN and then expanded in screen space. That is what keeps it the same size
                // whether the animal is five metres away or five hundred.
                float3 centerWS = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float4 clip = mul(UNITY_MATRIX_VP, float4(centerWS, 1.0));

                float2 corner = v.positionOS.xy * 2.0;          // quad is +-0.5 in object space
                o.local = corner;

                // clip.w keeps the offset constant in pixels after the perspective divide.
                float2 pixelToClip = _MarkerPixels / _ScreenParams.xy * clip.w;
                clip.xy += corner * pixelToClip;

                o.pos = clip;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // A ring rather than a blob, so the animal underneath stays visible inside its own marker.
                float r = length(i.local);
                if (r > 1.0) discard;

                half edge = smoothstep(0.45, 0.62, r) * (1.0 - smoothstep(0.88, 1.0, r));
                half core = 1.0 - smoothstep(0.0, 0.30, r);

                half4 c = _MarkerColor;
                c.a *= saturate(edge + core);
                return c;
            }
            ENDHLSL
        }
    }
}
