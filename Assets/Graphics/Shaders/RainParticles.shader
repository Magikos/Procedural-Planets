Shader "Hidden/RainParticles"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Rain"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Includes/WeatherSampling.hlsl"

            // Must match the layout of `Raindrop` in RainParticleUpdate.compute.
            struct Raindrop
            {
                float3 Position;
                float3 Velocity;
                float  Life01;
                float  Pad;
            };

            StructuredBuffer<Raindrop> _RainParticles;

            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            float  _RainStreakWidth;
            float  _RainStreakLength;
            float4 _RainColor;
            float  _RainVisibilityThreshold;   // dynamics.b threshold below which drops are fully invisible.
            float  _RainDensityScale;          // multiplier on dynamics.b for the density gate. Higher = more drops visible at the same rain rate; saturates at 1.
            float3 _PlanetCenter;
            float  _SeaRadius;

            uint HashUint(uint x)
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }

            float Hash01(uint seed)
            {
                return (HashUint(seed) & 0x00ffffffu) / 16777216.0;
            }

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPosition : TEXCOORD0;
                float  viewDepth : TEXCOORD1;
                float  alpha : TEXCOORD2;
                float2 stripUv : TEXCOORD3;
            };

            // 6-vertex quad: two triangles forming a stretched rectangle in
            // (side, along) local coordinates.
            void QuadCorner(uint corner, out float side, out float along)
            {
                if (corner == 0u) { side = -1.0; along = 0.0; }
                else if (corner == 1u) { side =  1.0; along = 0.0; }
                else if (corner == 2u) { side = -1.0; along = 1.0; }
                else if (corner == 3u) { side =  1.0; along = 0.0; }
                else if (corner == 4u) { side =  1.0; along = 1.0; }
                else                    { side = -1.0; along = 1.0; }
            }

            float SceneDepthLinear(float2 uv)
            {
                float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
                return LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Varyings o = (Varyings)0;

                Raindrop r = _RainParticles[instanceID];

                // Build a stretched billboard along the velocity direction.
                // The streak's "along" axis follows velocity; "side" is camera-
                // facing perpendicular.
                float speed = max(length(r.Velocity), 0.001);
                float3 motionDir = r.Velocity / speed;
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - r.Position);
                float3 sideDir = normalize(cross(viewDir, motionDir));

                float side;
                float along;
                QuadCorner(vertexID, side, along);

                // Streak's tail follows the velocity backward from the particle's
                // current head position. Drop's HEAD is where the particle is "now."
                float3 head = r.Position;
                float3 tail = head - motionDir * _RainStreakLength;
                float3 center = lerp(tail, head, along);
                float3 worldPos = center + sideDir * (side * _RainStreakWidth * 0.5);

                o.positionCS = TransformWorldToHClip(worldPos);
                o.screenPosition = ComputeScreenPos(o.positionCS);
                o.viewDepth = -TransformWorldToView(worldPos).z;
                o.stripUv = float2(side * 0.5 + 0.5, along);

                // Visibility gate. Sample the weather dynamics map at the
                // particle's surface-normal direction. Drops only render where
                // there is actual rain — dynamics.b carries local rain rate.
                float3 normal = normalize(r.Position - _PlanetCenter);
                float4 dynamics = SampleDynamics(normal);
                float rainSignal = saturate(dynamics.b);

                // Per-particle DENSITY gate. Each particle has a stable per-id
                // "rank" in [0,1]. A drop is visible when its rank is below the
                // local rain intensity (scaled). Sprinkle (low signal) only
                // lights up the lowest-ranked drops → few visible drops.
                // Downpour (high signal) lights up most drops → dense rain.
                // Without this, every spawned drop renders the same in light
                // vs heavy rain — only WHERE rain falls, not HOW MUCH.
                float dropRank = Hash01(instanceID * 0x9e3779b9u + 0x7777u);
                float densityFactor = saturate(rainSignal * _RainDensityScale);

                // Soft below-threshold cutoff so very light noise around the
                // threshold doesn't pop drops in/out at the boundary.
                float thresholdFade = smoothstep(
                    _RainVisibilityThreshold * 0.5,
                    _RainVisibilityThreshold,
                    rainSignal);
                float visible = (dropRank < densityFactor) ? 1.0 : 0.0;

                o.alpha = visible * thresholdFade * _RainColor.a;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                if (i.alpha <= 0.001)
                    discard;

                float2 uv = i.screenPosition.xy / max(i.screenPosition.w, 0.0001);
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    discard;

                // Soft particle: fade against scene depth so drops don't pop
                // hard when intersecting terrain or water.
                float sceneDepth = SceneDepthLinear(uv);
                if (i.viewDepth > sceneDepth + 0.05)
                    discard;
                float softIntersection = saturate((sceneDepth - i.viewDepth) / 0.5);

                // Tight edge/end fades so the streak CENTER stays close to fully
                // opaque. Wide fades made atmospheric scattering bleed through and
                // tint drops orange near the sunset horizon — drops at low alpha
                // pick up whatever color the sky has at that screen pixel.
                float edgeFade = 1.0 - smoothstep(0.78, 1.0, abs(i.stripUv.x * 2.0 - 1.0));
                float endFade = smoothstep(0.0, 0.04, i.stripUv.y)
                              * (1.0 - smoothstep(0.96, 1.0, i.stripUv.y));

                float alpha = i.alpha * edgeFade * endFade * softIntersection;
                if (alpha <= 0.001)
                    discard;

                return float4(_RainColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
