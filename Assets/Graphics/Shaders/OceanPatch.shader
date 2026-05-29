Shader "Planet/OceanPatch"
{
    // Near-camera water patch with REAL Gerstner vertex displacement.
    // Geometry: a flat grid (object space) that the OceanPatchController places under the camera.
    // The vertex shader projects each grid point onto the sea-level sphere, then displaces it via
    // the shared OceanWaves module (mirrored on CPU later for boat buoyancy).
    // Basin clipping is free: rendered at sea level with ZTest LEqual, dry land occludes it.
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.16, 0.52, 0.66, 1.0)
        _DeepColor ("Deep Color", Color) = (0.0, 0.10, 0.22, 1.0)
        _FoamColor ("Foam Color", Color) = (0.92, 0.98, 1.0, 1.0)
        _WaveAmplitude ("Wave Amplitude (m)", Range(0, 30)) = 6.0
        _WaveLength ("Wave Length (m)", Range(2, 600)) = 220
        _WaveSteepness ("Wave Steepness", Range(0, 1)) = 0.8
        _BodyScale ("Body Scale (0=pond,1=ocean)", Range(0, 1)) = 1.0
        _GlintPower ("Glint Power", Range(16, 4096)) = 600
        _GlintIntensity ("Glint Intensity", Range(0, 4)) = 0.4
        _Alpha ("Base Alpha", Range(0, 1)) = 0.85
    }

    SubShader
    {
        // Transparent+100 so the near patch draws ON TOP of the far ocean sheet (same sea-level depth),
        // otherwise the flat far ocean blends over the patch and washes the waves out.
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "OceanPatchForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Includes/OceanWaves.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float _WaveAmplitude;
                float _WaveLength;
                float _WaveSteepness;
                float _BodyScale;
                float _GlintPower;
                float _GlintIntensity;
                float _Alpha;
            CBUFFER_END

            // Set by OceanPatchController (decoupled from the atmosphere controller).
            float3 _OceanPatchCenter;
            float _OceanPatchSeaRadius;

            // Global (set by weather/atmosphere/celestial; safe if absent → fallbacks in the wave module).
            float3 _SunParams;
            float3 _WindDirection;
            float _WindSpeed;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float crest : TEXCOORD2;
                float edgeFade : TEXCOORD3; // radial fade → soft disc instead of a hard square
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                // Flat grid → world → projected onto the sea-level sphere (the undisplaced base point).
                float3 rawWorld = TransformObjectToWorld(input.positionOS.xyz);
                float3 vertexUp = normalize(rawWorld - _OceanPatchCenter);
                float3 baseWorld = _OceanPatchCenter + vertexUp * _OceanPatchSeaRadius;

                // ONE shared tangent frame for the whole patch (from the camera direction). Each vertex's
                // position projects onto it to get a horizontal coordinate that varies across the surface.
                float3 patchUp = normalize(_WorldSpaceCameraPos.xyz - _OceanPatchCenter);
                float3 frameA, frameB;
                BuildOceanFrame(patchUp, _WindDirection, frameA, frameB);

                float wind01 = saturate(_WindSpeed / 5.0);
                OceanSurfaceSample wave = EvaluateOceanSurface(
                    baseWorld, _OceanPatchCenter, patchUp, frameA, frameB, _GameTime, wind01,
                    _WaveAmplitude, _WaveLength, _BodyScale, _WaveSteepness);

                output.positionWS = wave.positionWS;
                output.normalWS = wave.normalWS;
                output.crest = wave.crest;
                // Grid is [-0.5,0.5] in object XZ; fade out before the edge so the square boundary is invisible.
                output.edgeFade = 1.0 - smoothstep(0.30, 0.48, length(input.positionOS.xz));
                output.positionCS = TransformWorldToHClip(wave.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 planetUp = normalize(input.positionWS - _OceanPatchCenter);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float3 sunDir = dot(_SunParams, _SunParams) > 0.0001 ? normalize(_SunParams) : planetUp;

                float daylight = smoothstep(-0.08, 0.18, dot(planetUp, sunDir));
                float facing = saturate(dot(viewDir, normalWS));
                float fresnel = pow(1.0 - facing, 5.0);

                // Base water color: deepen with viewing angle; lit by daylight (near-black at night).
                float3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, facing);
                waterColor *= lerp(0.06, 1.0, daylight);

                // Sky/horizon reflection at grazing angles.
                float3 skyColor = lerp(float3(0.01, 0.015, 0.025), float3(0.42, 0.60, 0.78), daylight);
                float3 color = lerp(waterColor, skyColor, fresnel * lerp(0.18, 0.55, daylight));

                // Tight sun glint off wave normals. No broad lobe — that read as one big reflective
                // circle darting across the (previously flat) surface. Tight + low intensity sparkles.
                float3 halfDir = normalize(sunDir + viewDir);
                float nDotH = saturate(dot(normalWS, halfDir));
                float glint = pow(nDotH, max(_GlintPower, 16.0)) * _GlintIntensity * daylight;
                color += glint * float3(1.0, 0.95, 0.82);

                // Whitecap foam on sharp crests — lit by sun, dark at night (no glow on the dark side).
                float foam = smoothstep(0.45, 0.85, input.crest);
                color = lerp(color, _FoamColor.rgb * lerp(0.04, 1.0, daylight), foam);

                float alpha = saturate((_Alpha + fresnel * 0.30 + foam * 0.40) * input.edgeFade);
                return half4(saturate(color), alpha);
            }
            ENDHLSL
        }
    }
}
