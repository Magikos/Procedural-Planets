Shader "Planet/Grass"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+10"
        }

        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "GrassForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex GrassVertex
            #pragma fragment GrassFragment
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Includes/PlanetSunLighting.hlsl"
            #include "Includes/GrassDither.hlsl"
            #include "Includes/GrassInteractors.hlsl"

            struct BladeInstance
            {
                float4 RootHeight; // xyz = root WS, w = blade height
                float4 UpWidth;    // xyz = up WS, w = root half-width
                float4 Color;
            };

            StructuredBuffer<BladeInstance> _GrassBladeInstances;

            // Slice 5a: wind. Global uniforms come from WeatherManager (also consumed by
            // clouds, ocean, precipitation). _WindDirection is a normalized world vector;
            // _WindSpeed is a scalar (units roughly = wave cycles per second).
            float3 _WindDirection;
            float _WindSpeed;
            float3 _SunParams;
            float _NightAmbientIntensity;
            float3 _PlanetCenter;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                float3 rootUpWS : TEXCOORD3;
                float4 color : COLOR0;
            };

            #define TUFT_BLADE_VERTEX_COUNT 18u
            #define TUFT_BLADE_COUNT 3u
            #define TUFT_BLADE_SEGMENTS 3.0

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

            float3 AnyTangent(float3 normalWS)
            {
                float3 axis = abs(normalWS.y) < 0.92 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                return normalize(cross(axis, normalWS));
            }

            uint BladeSeed(uint instanceID, uint tuftIndex, float3 rootWS)
            {
                uint seed = instanceID * 747796405u;
                seed ^= asuint(rootWS.x);
                seed ^= asuint(rootWS.y) * 2891336453u;
                seed ^= asuint(rootWS.z) * 198491317u;
                seed ^= (tuftIndex + 1u) * 0x9e3779b9u;
                return HashUint(seed);
            }

            float SmoothPatchNoise(float3 relRoot, float scale, float phase)
            {
                float3 p = relRoot / max(scale, 0.001);
                float n = sin(dot(p, float3(1.13, 1.71, 0.83)) + phase);
                n += sin(dot(p, float3(-0.73, 0.91, 1.47)) + phase * 1.37) * 0.5;
                return saturate(n * 0.333 + 0.5);
            }

            float3 SafeNormalize(float3 value, float3 fallback)
            {
                float lenSq = dot(value, value);
                return lenSq > 1e-8 ? value * rsqrt(lenSq) : fallback;
            }

            // Slice 5a wind: tip-only bend in the wind direction projected onto the local
            // tangent plane. Uses three phase sources for richness:
            //   1. clump phase (SmoothPatchNoise) so whole patches sway together — large gust waves
            //   2. world-position phase along wind direction so the gust visibly travels
            //   3. per-blade hash for tiny individual variation so blades aren't lockstep
            // Magnitude scales with _WindSpeed and blade height; t*t scaling means roots stay put.
            float3 ComputeWindOffset(float3 rootWs, float3 upWs, float height, float t, uint seed)
            {
                float3 windDir = SafeNormalize(_WindDirection, float3(1.0, 0.0, 0.0));
                // Project wind onto tangent plane so blades sway parallel to the surface.
                float3 windTangent = windDir - upWs * dot(windDir, upWs);
                float tangentLenSq = dot(windTangent, windTangent);
                if (tangentLenSq < 1e-6)
                    return float3(0.0, 0.0, 0.0); // wind perpendicular to surface; no bend
                windTangent *= rsqrt(tangentLenSq);

                float3 relRoot = rootWs - _PlanetCenter;
                float waveFreq = max(_WindSpeed, 0.05) * 1.4; // baseline cadence even with low wind

                // Directional traveling wave is the primary signal — sin phases along the
                // wind direction so you see gust fronts visibly travel across the field.
                // Wave velocity ≈ waveFreq / 0.18 m/s along windTangent.
                float travelWave = sin(_Time.y * waveFreq + dot(rootWs, windTangent) * 0.18);
                // Patch-level gust envelope: amplitude modulation only, not phase. Some
                // patches catch a strong gust while neighbors stay calm, but the wave
                // direction remains coherent across patches.
                float gustEnvelope = lerp(0.6, 1.0, SmoothPatchNoise(relRoot, 8.0, 0.0));
                // Per-blade jitter at higher frequency adds fine-grained life without
                // breaking the traveling wave (small amplitude, additive, not phase mix).
                float bladeJitter = sin(_Time.y * waveFreq * 2.5 + Hash01(seed ^ 0x44444444u) * 6.2831853) * 0.12;
                float wave = travelWave * gustEnvelope + bladeJitter;

                // Tip displacement in meters, scales with speed and blade height. Cap at 35% of
                // height so even violent wind doesn't fold the blade past horizontal.
                float displacement = wave * max(_WindSpeed, 0.0) * 0.4 * height;
                displacement = clamp(displacement, -height * 0.35, height * 0.35);

                return windTangent * displacement * (t * t);
            }

            Varyings GrassVertex(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                BladeInstance blade = _GrassBladeInstances[instanceID];
                float3 rootWS = blade.RootHeight.xyz;
                float height = blade.RootHeight.w;
                float3 upWS = normalize(blade.UpWidth.xyz);
                float width = blade.UpWidth.w;

                uint tuftIndex = min(vertexID / TUFT_BLADE_VERTEX_COUNT, TUFT_BLADE_COUNT - 1u);
                uint bladeVertex = vertexID - tuftIndex * TUFT_BLADE_VERTEX_COUNT;
                uint segment = bladeVertex / 6u;
                uint localVertex = bladeVertex - segment * 6u;
                float t0 = (float)segment / TUFT_BLADE_SEGMENTS;
                float t1 = (float)(segment + 1u) / TUFT_BLADE_SEGMENTS;
                float t = (localVertex == 0u || localVertex == 1u || localVertex == 4u) ? t0 : t1;
                float side = (localVertex == 0u || localVertex == 2u || localVertex == 3u) ? -1.0 : 1.0;

                uint seed = BladeSeed(instanceID, tuftIndex, rootWS);
                float3 relRoot = rootWS - _PlanetCenter;
                float patchHeightNoise = SmoothPatchNoise(relRoot, 14.0, 1.37);
                float patchWidthNoise = SmoothPatchNoise(relRoot, 18.0, 4.11);
                float patchTintNoise = SmoothPatchNoise(relRoot, 24.0, 7.53);
                float3 tangentWS = AnyTangent(upWS);
                float3 bitangentWS = normalize(cross(upWS, tangentWS));

                float yaw = Hash01(seed ^ 0x6a09e667u) * 6.2831853 + (float)tuftIndex * 2.0943951;
                float yawSin = sin(yaw);
                float yawCos = cos(yaw);
                float3 sideWS = normalize(tangentWS * yawCos + bitangentWS * yawSin);
                float3 leanWS = normalize(cross(sideWS, upWS));

                float spread = max(width * 1.65, height * 0.025);
                float2 rootJitter = float2(
                    Hash01(seed ^ 0xbb67ae85u) - 0.5,
                    Hash01(seed ^ 0x3c6ef372u) - 0.5) * spread;
                float3 tuftRootWS = rootWS + tangentWS * rootJitter.x + bitangentWS * rootJitter.y;

                float patchHeight = lerp(0.82, 1.18, patchHeightNoise);
                float patchWidth = lerp(0.92, 1.10, patchWidthNoise);
                height *= patchHeight * lerp(0.48, 1.55, Hash01(seed ^ 0xa54ff53au));
                width *= patchWidth * lerp(1.05, 1.55, Hash01(seed ^ 0x510e527fu));

                float bend = t * t * height * lerp(0.16, 0.34, Hash01(seed ^ 0x9b05688cu));
                float lateralCurl = (Hash01(seed ^ 0x1f83d9abu) - 0.5) * width * t * (1.0 - t) * 0.8;
                float widthAtT = width * pow(saturate(1.0 - t), 1.15);
                // Slice 4a hook: forward-compat for slice 6 (character / entity grass bend).
                // SampleGrassInteractorBend returns 0 today; when slice 6 ships, it returns a
                // world-space bend vector that we scale by t*t so only the tip bends.
                float3 interactorBend = SampleGrassInteractorBend(tuftRootWS) * (t * t);
                // Slice 5a: tip-only wind bend in the tangent plane. Clump-based phase so
                // patches sway together; gust travels along _WindDirection.
                float3 windOffset = ComputeWindOffset(tuftRootWS, upWS, height, t, seed);
                float3 spineWS = tuftRootWS + upWS * (height * t) + leanWS * bend + sideWS * lateralCurl + interactorBend + windOffset;
                float3 positionWS = spineWS + sideWS * (side * widthAtT);

                float brightness = lerp(0.72, 1.16, Hash01(seed ^ 0x5be0cd19u));
                float tintHash = saturate(lerp(Hash01(seed ^ 0xc2b2ae35u), patchTintNoise, 0.18));
                float patchTint = lerp(0.94, 1.04, patchTintNoise);
                float colorJitter = Hash01(seed ^ 0x27d4eb2fu);
                float3 baseTint = lerp(float3(0.78, 0.96, 0.72), float3(1.04, 1.03, 0.86), tintHash);
                float3 bladeTintJitter = lerp(float3(0.92, 1.03, 0.94), float3(1.06, 0.99, 0.90), colorJitter);
                float3 tint = baseTint * bladeTintJitter * patchTint;
                float heightShade = lerp(0.48, 1.06, smoothstep(0.0, 1.0, t));

                // blade.Color.a carries the per-root distance fade in [0,1] from the
                // near-field compute kernel (1.0 = fully opaque, lower = closer to fade
                // cutoff). The chunk-path compute writes 1.0 so it's unaffected.
                float fadeAlpha = blade.Color.a;

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalize(leanWS * 0.72 + upWS * 0.24 + sideWS * side * 0.18);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.rootUpWS = upWS;
                output.color = float4(saturate(blade.Color.rgb * tint * brightness) * heightShade, fadeAlpha);
                return output;
            }

            half4 GrassFragment(Varyings input) : SV_Target
            {
                // Per-fragment dithered clip on the per-root fade alpha. fadeAlpha == 1 (chunk
                // path or near-field's full-density disc) always passes since dither in [0,1)
                // means 1 - dither > 0. fadeAlpha == 0 always clips. Intermediate values
                // produce a stippled fade that reads cleaner than a hard edge. Mid-field
                // (slice 4c) calls SampleGrassDither so its fade band stipple matches.
                clip(input.color.a - SampleGrassDither(input.positionCS.xy));

                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 normalWS = SafeNormalize(input.normalWS, float3(0.0, 1.0, 0.0));
                normalWS = dot(normalWS, viewDir) < 0.0 ? -normalWS : normalWS;

                float3 albedo = saturate(input.color.rgb);
                float3 planetNormal = PlanetSafeNormalize(input.positionWS - _PlanetCenter, normalWS);
                float3 rootUpWS = PlanetSafeNormalize(input.rootUpWS, planetNormal);
                float3 sunDir = PlanetSunDirection(_SunParams, planetNormal);
                float localSun = dot(planetNormal, sunDir);
                float daylight = PlanetDaylightFromLocalSun(localSun);
                float surfaceDirect = PlanetSurfaceDirect(rootUpWS, sunDir);

                float bladeDiffuse = saturate(dot(normalWS, sunDir));
                float wrapDiffuse = saturate(bladeDiffuse * 0.72 + 0.28);
                float3 dayColor = albedo * (0.16 + wrapDiffuse * surfaceDirect * 0.96);

                float horizonFactor = saturate(1.0 - abs(localSun) * 3.0);
                float backlit = pow(saturate(dot(viewDir, -sunDir)), 3.0) * daylight * horizonFactor * surfaceDirect;
                dayColor += albedo * backlit * 0.32;

                float3 nightAlbedo = lerp(albedo, float3(0.10, 0.14, 0.20), 0.68);
                float nightAmbient = PlanetNightAmbient(_NightAmbientIntensity);
                float3 nightColor = nightAlbedo * nightAmbient * 0.65;

                float3 color = lerp(nightColor, dayColor, daylight);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
