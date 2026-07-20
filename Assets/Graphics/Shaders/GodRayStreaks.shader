Shader "Hidden/GodRayStreaks"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Includes/DebugModes.hlsl"

            TEXTURE2D(_Source);
            SAMPLER(sampler_Source);
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            float4 _SunParams;
            // x=strength (human 0-2), y=sample count, z=per-sample decay, w=march length (screen-UV units)
            float4 _GodRayStreakParams;
            float _GodRayStreakRadialFalloff;
            float _GodRayStreakDawnBoost;
            // Scene luminance above which a sample becomes a ray source. Only genuinely bright
            // things (the sun disc, bright backlit cloud edges) smear into beams; everything
            // dimmer - including dark cloud bodies - contributes nothing and reads as the gaps.
            float _GodRayStreakBrightThreshold;
            float3 _CloudPlanetCenter;
            float _AtmosphereRadius;
            int _OceanDebugMode;

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

            // 1 where the pixel shows sky (no terrain drawn), 0 where terrain occludes.
            float SkyDepthMask(float2 uv)
            {
                float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
                #if UNITY_REVERSED_Z
                    return 1.0 - step(0.0001, rawDepth);
                #else
                    return step(0.9999, rawDepth);
                #endif
            }

            float StreakNoise(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            float Luminance709(float3 c)
            {
                return dot(c, float3(0.2126, 0.7152, 0.0722));
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 src = SAMPLE_TEXTURE2D(_Source, sampler_Source, i.uv);
                float3 baseColor = src.rgb;

                float strength = _GodRayStreakParams.x;
                int sampleCount = (int)_GodRayStreakParams.y;
                float decay = _GodRayStreakParams.z;
                float marchLength = _GodRayStreakParams.w;

                bool isolate = _OceanDebugMode == DEBUG_GOD_RAY_STREAKS;
                if ((strength <= 0.0 || sampleCount <= 0) && !isolate)
                    return float4(baseColor, 1.0);

                float3 sunDir = dot(_SunParams.xyz, _SunParams.xyz) > 0.0001
                    ? normalize(_SunParams.xyz) : float3(0.0, 1.0, 0.0);
                float4 sunClip = TransformWorldToHClip(_WorldSpaceCameraPos.xyz + sunDir * max(_AtmosphereRadius, 1000.0));
                if (sunClip.w <= 0.0)
                    return isolate ? float4(0, 0, 0, 1) : float4(baseColor, 1.0); // sun behind camera

                float2 sunNdc = sunClip.xy / max(abs(sunClip.w), 0.0001);
                float2 sunUv = float2(sunNdc.x, sunNdc.y * _ProjectionParams.x) * 0.5 + 0.5;

                float screenDistance = max(abs(sunNdc.x), abs(sunNdc.y));
                // Wide margin so the glow eases out well before the sun reaches the frame edge,
                // instead of staying full-strength then popping off abruptly.
                float screenFade = 1.0 - smoothstep(0.55, 1.05, screenDistance);

                float3 camFromCenter = _WorldSpaceCameraPos.xyz - _CloudPlanetCenter;
                float camRadius = length(camFromCenter);
                float3 camNormal = camRadius > 0.0001 ? camFromCenter / camRadius : float3(0.0, 1.0, 0.0);
                // Wider than the original atmosphere shaft's band: crepuscular rays are strongest
                // at low sun angles (dawn/dusk), so ramp in earlier and stay present longer
                // through that low-angle window instead of only right at the horizon crossing.
                float sunElevationDot = dot(camNormal, sunDir);
                float localSunVisibility = smoothstep(-0.12, 0.08, sunElevationDot);

                // Dawn/dusk boost: strongest right at the horizon, fading out by ~20 degrees of
                // elevation either side, independent of the on/off gate above. Real crepuscular
                // rays are most dramatic at low sun angles, so this exaggerates strength there
                // rather than relying on the base scattering model's natural (much dimmer) falloff.
                float dawnWindow = (1.0 - smoothstep(0.0, 0.35, abs(sunElevationDot))) * localSunVisibility;
                strength *= lerp(1.0, _GodRayStreakDawnBoost, dawnWindow);

                // Soft radial fade with angular (screen-UV) distance from the sun. Without this,
                // "path is clear of cloud" alone reads as fully bright everywhere clouds aren't -
                // clear sky on the far side of the screen would look just as lit as sky right
                // next to the sun. This localizes the glow while still fanning out believably.
                // Aspect-corrected: raw UV distance treats a 2.77:1-wide capture as a square, so
                // an equal-UV-distance "circle" renders as a wide oval. Scale the horizontal
                // delta by the aspect ratio so the falloff is round in screen pixels.
                float screenAspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 sunUvDelta = i.uv - sunUv;
                sunUvDelta.x *= screenAspect;
                float sunUvDistance = length(sunUvDelta);
                float radialFalloff = exp(-sunUvDistance * _GodRayStreakRadialFalloff);

                // This is a 2D screen-space effect with no real depth awareness along the streak
                // direction, so it must not paint onto terrain: a distant cloud and a near hill
                // that happen to line up on screen are at completely different world depths, and
                // without this gate the streak reads as a "shadow" cast by the distant cloud onto
                // the near terrain, which has no 3D basis. Restrict the whole effect to sky pixels
                // only, matching CalculateLightShafts's own SkyDepthMask(uv) gate in Atmosphere.shader.
                float gate = screenFade * localSunVisibility * radialFalloff * SkyDepthMask(i.uv);
                if (gate <= 0.0)
                    return isolate ? float4(0, 0, 0, 1) : float4(baseColor, 1.0);

                // Radial blur (Mitchell / classic crepuscular-ray method): march from this pixel
                // toward the sun and smear the ACTUAL scene color of the bright things along that
                // line outward, decaying with distance. Because this pass runs AFTER clouds, the
                // scene already holds the sun disc, bright backlit cloud rims, and dark cloud
                // bodies - so bright sources become colored beams (warm at dawn, from the real
                // light), and dark clouds contribute nothing and read as the gaps the beams fan
                // through. This replaces the earlier "abstract occlusion value + fixed tint",
                // which read flat and grey; smearing real light is what gives the reference look.
                float2 delta = (i.uv - sunUv) * (marchLength / max(sampleCount, 1));
                float jitter = StreakNoise(i.uv * _ScreenParams.xy) - 0.5;
                float2 sampleUv = i.uv - delta * jitter;
                float illuminationDecay = 1.0;
                float3 rayAccum = 0.0;

                [loop]
                for (int s = 0; s < 64; s++)
                {
                    if (s >= sampleCount)
                        break;
                    sampleUv -= delta;
                    float inBounds = step(0.0, sampleUv.x) * step(sampleUv.x, 1.0)
                        * step(0.0, sampleUv.y) * step(sampleUv.y, 1.0);
                    float3 sampleColor = SAMPLE_TEXTURE2D(_Source, sampler_Source, sampleUv).rgb;
                    float bright = smoothstep(_GodRayStreakBrightThreshold,
                        _GodRayStreakBrightThreshold + 0.25, Luminance709(sampleColor));
                    rayAccum += sampleColor * bright * SkyDepthMask(sampleUv) * inBounds * illuminationDecay;
                    illuminationDecay *= decay;
                }
                rayAccum /= max(sampleCount, 1);

                if (isolate)
                    return float4(rayAccum * gate * 4.0, 1.0);

                return float4(baseColor + rayAccum * gate * strength, 1.0);
            }
            ENDHLSL
        }
    }
}
