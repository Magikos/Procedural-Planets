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
            #include "Includes/ClimateSampling.hlsl"
            #include "Includes/WaterCamera.hlsl"

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
            float  _RainNearRadius;
            float _RainActiveCount;
            float _RainAltitudeFade;
            float4 _WeatherParticleCounts;
            int _WeatherParticleProof;
            float3 _PlanetCenter;
            float  _SeaRadius;
            float4 _WeatherLightningColor;
            float4 _WeatherParticleSnowColor;
            float4 _WeatherParticleSnowParams;
            float3 _SunParams;
            float _NightAmbientIntensity;

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
                float  lightning : TEXCOORD4;
                nointerpolation float state : TEXCOORD5;
                float illumination : TEXCOORD6;
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
                float3 sideDir = cross(viewDir, motionDir);
                if (dot(sideDir, sideDir) < 0.000001)
                {
                    float3 reference = abs(motionDir.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
                    sideDir = cross(reference, motionDir);
                }
                sideDir = normalize(sideDir);

                float side;
                float along;
                QuadCorner(vertexID, side, along);

                // Streak's tail follows the velocity backward from the particle's
                // current head position. Drop's HEAD is where the particle is "now."
                float3 head = r.Position;
                float3 tail = head - motionDir * _RainStreakLength;
                float3 center = lerp(tail, head, along);
                float3 worldPos = center + sideDir * (side * _RainStreakWidth * 0.5);

                bool impact = r.Life01 < 0;
                bool snow = r.Pad == 1 || r.Pad == 4;
                o.state = impact ? (r.Pad == 2 ? 2 : 3) : snow ? 1 : 0;
                if (snow && !impact)
                {
                    float3 up = normalize(cross(viewDir, sideDir));
                    float size = max(_WeatherParticleSnowParams.z, 0.01);
                    worldPos = head + sideDir * side * size + up * (along * 2 - 1) * size;
                }
                if (impact)
                {
                    float3 normal = normalize(r.Velocity);
                    float3 reference = abs(normal.y) < 0.9 ? float3(0,1,0) : float3(1,0,0);
                    float3 tangent = normalize(cross(normal, reference));
                    float age = saturate(1 + r.Life01 / 0.3);
                    float size = lerp(0.025, r.Pad == 2 ? 0.3 : 0.09, age);
                    worldPos = head + tangent * side * size + cross(normal, tangent) * (along * 2 - 1) * size;
                }
                o.positionCS = TransformWorldToHClip(worldPos);
                o.screenPosition = ComputeScreenPos(o.positionCS);
                o.viewDepth = -TransformWorldToView(worldPos).z;
                o.stripUv = float2(side * 0.5 + 0.5, along);

                // Drops only render where raw rain overlaps a storm cell.
                float3 normal = normalize(r.Position - _PlanetCenter);
                float storm = saturate(SampleWeather(normal).g);
                float temperature = ClimateTemperatureCelsius(SampleClimate01(normal).x);
                float rainSignal = WeatherPrecipitationSignal(normal, storm);
                o.lightning = WeatherLightning(normal, storm) * _WeatherLightningColor.a;
                // Use the same daylight and storm attenuation as the distant curtain.
                float daylight = saturate((dot(normal, _SunParams) + 0.1) * 3.0);
                o.illumination = saturate(_NightAmbientIntensity * 0.45
                    + daylight * 0.85 * lerp(1.0, 0.28, storm) + 0.12);

                // Per-particle DENSITY gate. Each particle has a stable per-id
                // "rank" in [0,1]. A drop is visible when its rank is below the
                // local rain intensity (scaled). Sprinkle (low signal) only
                // lights up the lowest-ranked drops → few visible drops.
                // Downpour (high signal) lights up most drops → dense rain.
                // Without this, every spawned drop renders the same in light
                // vs heavy rain — only WHERE rain falls, not HOW MUCH.
                float dropRank = Hash01(instanceID * 0x9e3779b9u + 0x7777u);
                if (_WeatherParticleProof > 0) rainSignal = _WeatherParticleProof == 4
                    || (_WeatherParticleProof == 3 && snow) || (_WeatherParticleProof == 2 && !snow) ? 1 : 0;
                float densityFactor = saturate(rainSignal * _RainDensityScale);
                if (snow) densityFactor *= saturate(_WeatherParticleCounts.z / max(_RainActiveCount,1));

                // Soft below-threshold cutoff so very light noise around the
                // threshold doesn't pop drops in/out at the boundary.
                float thresholdFade = smoothstep(
                    _RainVisibilityThreshold * 0.5,
                    _RainVisibilityThreshold,
                    rainSignal);
                float visible = smoothstep(dropRank, min(dropRank + 0.2, 1.0), densityFactor);

                float distanceFade = 1.0 - smoothstep(_RainNearRadius * 0.4, _RainNearRadius,
                    distance(r.Position, _WorldSpaceCameraPos.xyz));
                o.alpha = visible * thresholdFade * (snow ? _WeatherParticleSnowParams.y : _RainColor.a) * distanceFade
                    * (snow ? 1.0 : 0.85)
                    * _RainAltitudeFade
                    * (impact ? saturate(-r.Life01 / 0.3) * 0.2 : saturate(r.Life01))
                    * smoothstep(0.25, 1.0, distance(r.Position, _WorldSpaceCameraPos.xyz))
                    * (1.0 - WaterCameraImmersion(_WorldSpaceCameraPos.xyz));
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
                float softIntersection = i.state >= 2 ? 1.0 : saturate((sceneDepth - i.viewDepth) / 0.5);

                // Taper the entire streak so its silhouette blends into the rain veil.
                float edgeFade = 1.0 - smoothstep(0.0, 1.0, abs(i.stripUv.x * 2.0 - 1.0));
                float endFade = smoothstep(0.0, 0.3, i.stripUv.y)
                              * (1.0 - smoothstep(0.7, 1.0, i.stripUv.y));

                float shape = edgeFade * endFade;
                float radial = length(i.stripUv * 2 - 1);
                if (i.state == 1) shape = 1 - smoothstep(0.25, 1, radial);
                else if (i.state == 2) shape = smoothstep(0.65, 0.8, radial) * (1 - smoothstep(0.8, 1, radial));
                else if (i.state == 3) shape = 1 - smoothstep(0.1, 1, radial);
                float alpha = i.alpha * shape * softIntersection;
                if (alpha <= 0.001)
                    discard;

                // Lightning flash: drops brighten and pick up the strike color while a
                // nearby lightning cell is active (same signal as clouds and curtains).
                float3 color = (i.state == 1 ? _WeatherParticleSnowColor.rgb : _RainColor.rgb) * (i.illumination + i.lightning * 0.55)
                    + _WeatherLightningColor.rgb * i.lightning * 0.25;
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
