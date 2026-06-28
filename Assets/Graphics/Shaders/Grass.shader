Shader "Planet/Grass"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            // WaterVolume composites before transparents. Draw grass immediately after
            // that composite, write depth, then let the ocean surface depth-test against it.
            "Queue" = "Transparent-10"
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
            #include "Includes/CloudShadows.hlsl"
            #include "Includes/GrassColor.hlsl"
            #include "Includes/GrassInteractors.hlsl"
            #include "Includes/GrassDither.hlsl"

            struct BladeInstance
            {
                float4 RootHeight; // xyz = root WS, w = blade height
                float4 UpWidth;    // xyz = up WS, w = root half-width
                float4 Color;
            };

            StructuredBuffer<BladeInstance> _GrassBladeInstances;

            // Wind globals are declared by CloudShadows.hlsl and populated by WeatherManager.
            float3 _SunParams;
            float _NightAmbientIntensity;
            float3 _PlanetCenter;
            int _GrassGeometryMode;
            float _GrassClusterStartDistance;
            float _GrassClusterEndDistance;
            float _GrassVisualNearFadeStart;
            float _GrassVisualNearFadeEnd;
            float _GrassVisualFadeStart;
            float _GrassVisualFadeEnd;
            float _GrassChunkFade;
            float4 _GrassDebugBladeTint;
            float _GrassDebugLayerColors;
            float4 _GrassLayerDebugTint;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                float3 rootUpWS : TEXCOORD3;
                float2 clusterUv : TEXCOORD4;
                nointerpolation float clusterSeed : TEXCOORD5;
                nointerpolation float clusterMode : TEXCOORD6;
                nointerpolation float fadeAlpha : TEXCOORD7;
                float4 color : COLOR0;
            };

            #define TUFT_BLADE_VERTEX_COUNT 18u
            #define TUFT_BLADE_COUNT 3u
            #define TUFT_BLADE_SEGMENTS 3.0
            #define CLUSTER_BLADE_COLUMNS 5.0

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

            float HashFloat(float seed)
            {
                return frac(sin(seed * 12.9898 + 78.233) * 43758.5453);
            }

            float3 AnyTangent(float3 normalWS)
            {
                float3 axis = abs(normalWS.y) < 0.92 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                return normalize(cross(axis, normalWS));
            }

            uint BladeSeed(uint instanceID, uint tuftIndex, float3 rootWS)
            {
                // Determinism MUST be position-based only. The near-field re-dispatches
                // the placement compute every page shift (~4m of camera movement), and
                // the same world cell ends up at a different instanceID each dispatch
                // because the compute fills the buffer in dispatch order. Mixing
                // instanceID into the seed therefore re-rolls every blade's yaw, height,
                // color, etc. as the camera moves — visible as the whole field
                // "redrawing and randomizing" between page shifts. Hash purely on
                // rootWS + tuftIndex so the same world position always produces the
                // same blade regardless of which dispatch placed it.
                uint seed = asuint(rootWS.x);
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

            // Wind speed at which the blade bends halfway. Higher = stiffer grass that
            // resists wind; the steady lean is windForce / (windForce + stiffness), so wind
            // far above this pins the blade bent over while wind far below barely moves it.
            #define GRASS_WIND_STIFFNESS 6.0

            // Tip bend driven as wind force vs blade stiffness. The wind leaves a steady lean
            // in the wind direction (force overcoming springiness) with a wiggle layered on
            // top: lively at moderate wind, just a flutter once the blade is pinned over.
            // t*t scaling keeps roots planted and only the tip moving.
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
                float windForce = max(_WindSpeedMps, 0.0);
                float bend01 = windForce / (windForce + GRASS_WIND_STIFFNESS); // 0 calm .. ->1 gale

                // Steady lean held in the wind direction while the gust persists.
                float steadyBend = bend01 * 0.7;

                // Wiggle around the leaned pose. Travelling gust front + patch envelope + a
                // per-blade jitter, all phased on planet-relative position so it can't lose
                // precision at large world coordinates. sin(wt - kx) propagates along +wind.
                float waveFreq = 0.5 + windForce * 0.12;
                float travelWave = sin(_GameTime * waveFreq - dot(relRoot, windTangent) * 0.18);
                float gustEnvelope = lerp(0.7, 1.0, SmoothPatchNoise(relRoot, 8.0, 0.0));
                float bladeJitter = sin(_GameTime * waveFreq * 2.5 + Hash01(seed ^ 0x44444444u) * 6.2831853) * 0.12;
                // Flutter peaks at mid wind and tapers as the blade pins over, so a gale is a
                // held bend with a small shiver rather than a wide sway.
                float wiggleEnvelope = bend01 * (1.0 - 0.55 * bend01);
                float wiggle = (travelWave * gustEnvelope + bladeJitter) * wiggleEnvelope * 0.28;

                float displacement = (steadyBend + wiggle) * height;
                displacement = clamp(displacement, -0.12 * height, 0.72 * height);

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
                float viewDistance = length(_WorldSpaceCameraPos - rootWS);
                float visualEdgeFade = 1.0;
                if (_GrassVisualNearFadeEnd > _GrassVisualNearFadeStart + 0.01)
                    visualEdgeFade *= smoothstep(_GrassVisualNearFadeStart, _GrassVisualNearFadeEnd, viewDistance);
                if (_GrassVisualFadeEnd > _GrassVisualFadeStart + 0.01)
                    visualEdgeFade *= 1.0 - smoothstep(_GrassVisualFadeStart, _GrassVisualFadeEnd, viewDistance);
                visualEdgeFade *= saturate(_GrassChunkFade);

                float3 toCameraTangent = _WorldSpaceCameraPos - rootWS;
                toCameraTangent -= upWS * dot(toCameraTangent, upWS);
                float tangentViewLengthSq = dot(toCameraTangent, toCameraTangent);
                if (tangentViewLengthSq > 1e-6)
                {
                    float3 billboardSideWS = normalize(cross(upWS, toCameraTangent));
                    if (dot(billboardSideWS, sideWS) < 0.0)
                        billboardSideWS = -billboardSideWS;
                    float billboardWeight = smoothstep(150.0, 420.0, viewDistance) * 0.78;
                    sideWS = normalize(lerp(sideWS, billboardSideWS, billboardWeight));
                }
                float3 leanWS = normalize(cross(sideWS, upWS));

                float spread = max(width * 1.65, height * 0.025);
                float2 rootJitter = float2(
                    Hash01(seed ^ 0xbb67ae85u) - 0.5,
                    Hash01(seed ^ 0x3c6ef372u) - 0.5) * spread;
                float3 tuftRootWS = rootWS + tangentWS * rootJitter.x + bitangentWS * rootJitter.y;

                float patchHeight = lerp(0.76, 1.24, patchHeightNoise);
                float patchWidth = lerp(0.88, 1.16, patchWidthNoise);
                height *= patchHeight * lerp(0.42, 1.68, Hash01(seed ^ 0xa54ff53au));
                width *= patchWidth * lerp(0.96, 1.68, Hash01(seed ^ 0x510e527fu));
                // Preserve projected coverage as physical density thins. This is cheaper
                // than adding more roots and works with the billboard turn above.
                width *= lerp(1.0, 1.42, smoothstep(160.0, 500.0, viewDistance));
                float geometryFade = smoothstep(0.0, 1.0, saturate(visualEdgeFade));
                height *= geometryFade;
                width *= geometryFade;

                float bend = t * t * height * lerp(0.16, 0.34, Hash01(seed ^ 0x9b05688cu));
                float lateralCurl = (Hash01(seed ^ 0x1f83d9abu) - 0.5) * width * t * (1.0 - t) * 0.8;

                // Close grass must remain physical geometry because the camera can enter it.
                // Cluster cards are only a distant representation. Stagger the handoff per
                // root so changing representation cannot form a camera-centered ring.
                float clusterStart = max(_GrassClusterStartDistance, 2.0);
                float clusterEnd = max(_GrassClusterEndDistance, clusterStart + 0.01);
                float clusterThreshold = lerp(
                    clusterStart,
                    clusterEnd,
                    Hash01(seed ^ 0x94d049bbu));
                float clusterMode = _GrassGeometryMode <= 0
                    ? 0.0
                    : (_GrassGeometryMode >= 2 ? 1.0 : step(clusterThreshold, viewDistance));

                float ribbonHalfWidth = width * pow(saturate(1.0 - t), 1.15);
                float clusterHalfWidth = width * 3.4;
                float clusterWidthAtT = clusterHalfWidth * lerp(1.0, 0.88, t);
                float widthAtT = lerp(ribbonHalfWidth, clusterWidthAtT, clusterMode);
                // Tangent-plane bend from dynamic interactors (debug sphere today;
                // player character / projectiles / animals / magic AOEs later).
                // SampleGrassInteractorBend returns 0 when no interactors are active.
                // Scaled by t*t so blade roots stay put and only tips bend.
                float3 interactorBend = SampleGrassInteractorBend(tuftRootWS, upWS, height * 0.85) * (t * t);
                // Slice 5a: tip-only wind bend in the tangent plane. Clump-based phase so
                // patches sway together; gust travels along _WindDirection.
                float3 windOffset = ComputeWindOffset(tuftRootWS, upWS, height, t, seed);
                float3 spineWS = tuftRootWS + upWS * (height * t) + leanWS * bend + sideWS * lateralCurl + interactorBend + windOffset;
                float3 positionWS = spineWS + sideWS * (side * widthAtT);

                float brightness = lerp(0.56, 1.04, Hash01(seed ^ 0x5be0cd19u));
                float tintHash = saturate(lerp(Hash01(seed ^ 0xc2b2ae35u), patchTintNoise, 0.18));
                float patchTint = lerp(0.86, 1.06, patchTintNoise);
                float colorJitter = Hash01(seed ^ 0x27d4eb2fu);
                float3 baseTint = lerp(float3(0.70, 0.88, 0.64), float3(1.04, 0.96, 0.72), tintHash);
                float3 bladeTintJitter = lerp(float3(0.82, 1.03, 0.86), float3(1.05, 0.91, 0.78), colorJitter);
                float3 tint = baseTint * bladeTintJitter * patchTint;
                float heightShade = lerp(0.42, 0.94, smoothstep(0.0, 1.0, t));
                float3 biomeTint = GradeGrassTint(blade.Color.rgb, 0.76, 0.88);
                float3 bladeAlbedo = saturate(biomeTint * tint * brightness) * heightShade;
                float3 canopyAlbedo = GradeGrassTint(blade.Color.rgb, 0.82, 0.98) * 0.76;
                float canopyHandoff = smoothstep(120.0, 240.0, viewDistance);

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalize(leanWS * 0.72 + upWS * 0.24 + sideWS * side * 0.18);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.rootUpWS = upWS;
                output.clusterUv = float2(side * 0.5 + 0.5, t);
                output.clusterSeed = Hash01(seed ^ 0xd2511f53u);
                output.clusterMode = clusterMode;
                output.fadeAlpha = saturate(visualEdgeFade);
                output.color = float4(lerp(bladeAlbedo, canopyAlbedo, canopyHandoff), 1.0);
                return output;
            }

            half4 GrassFragment(Varyings input) : SV_Target
            {
                clip(input.fadeAlpha - SampleGrassDither(input.positionCS.xy) - 0.001);

                if (input.clusterMode > 0.5)
                {
                    float bladeCoord = min(input.clusterUv.x, 0.99999) * CLUSTER_BLADE_COLUMNS;
                    float bladeIndex = floor(bladeCoord);
                    float bladeLocalX = frac(bladeCoord) - 0.5;
                    float bladeHash = HashFloat(bladeIndex + input.clusterSeed * 97.0);
                    float bladeHeight = lerp(0.68, 1.02, bladeHash);
                    clip(bladeHeight - input.clusterUv.y);

                    float bladeT = saturate(input.clusterUv.y / max(bladeHeight, 0.001));
                    float bladeLean = (HashFloat(bladeIndex + input.clusterSeed * 173.0) - 0.5)
                        * bladeT * (1.0 - bladeT) * 0.24;
                    float bladeHalfWidth = lerp(0.35, 0.018, pow(bladeT, 0.82));
                    clip(bladeHalfWidth - abs(bladeLocalX - bladeLean));
                }

                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 normalWS = SafeNormalize(input.normalWS, float3(0.0, 1.0, 0.0));

                float layerDebugStrength = saturate(_GrassDebugLayerColors) * saturate(_GrassLayerDebugTint.a);
                float3 debugTint = lerp(
                    saturate(_GrassDebugBladeTint.rgb),
                    saturate(_GrassLayerDebugTint.rgb),
                    layerDebugStrength);
                float edgeShade = lerp(0.55, 1.0, saturate(input.fadeAlpha));
                float3 sourceAlbedo = saturate(input.color.rgb)
                    * lerp(0.45, 1.0, saturate(_GrassChunkFade))
                    * edgeShade;
                float3 albedo = lerp(
                    sourceAlbedo,
                    debugTint,
                    max(saturate(_GrassDebugBladeTint.a), layerDebugStrength));
                float3 planetNormal = PlanetSafeNormalize(input.positionWS - _PlanetCenter, normalWS);
                float3 rootUpWS = PlanetSafeNormalize(input.rootUpWS, planetNormal);
                float3 sunDir = PlanetSunDirection(_SunParams, planetNormal);
                float localSun = dot(planetNormal, sunDir);
                float daylight = PlanetDaylightFromLocalSun(localSun);
                float surfaceDirect = PlanetSurfaceDirect(rootUpWS, sunDir);
                float cloudShadow = CloudShadowFactor(input.positionWS, sunDir, localSun);

                // Grass ribbons are intentionally double-sided. Using a camera-facing
                // normal makes their diffuse term flip when the camera crosses a ribbon
                // plane. Light both sides symmetrically so movement cannot change which
                // normal sign receives sunlight.
                float bladeDiffuse = saturate(abs(dot(normalWS, sunDir)));
                float wrapDiffuse = saturate(bladeDiffuse * 0.72 + 0.28);
                float3 dayColor = albedo * (0.12 + wrapDiffuse * surfaceDirect * 0.82);
                dayColor *= lerp(1.0, cloudShadow, daylight);

                float horizonFactor = saturate(1.0 - abs(localSun) * 3.0);
                float backlit = pow(saturate(dot(viewDir, -sunDir)), 3.0) * daylight * horizonFactor * surfaceDirect;
                dayColor += albedo * backlit * cloudShadow * 0.16;

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
