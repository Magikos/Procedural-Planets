Shader "Scatter/Impostor"
{
    // Far-field scatter impostor, OCTAHEDRAL: the prototype is baked from a GridN×GridN grid of hemisphere
    // camera angles (hemi-octahedral layout) into one atlas (RGB = UNLIT albedo, A = silhouette). The quad
    // is camera-facing and kept upright; the fragment picks the atlas cell matching the view direction in
    // the tree's own frame, so the card reads as the tree from ANY angle — including straight down, where a
    // single front-view billboard would foreshorten to a slab. Lighting is applied here at runtime from the
    // planet sun and ambient used by the mesh. Packed surface data supplies normals, depth and leaf mask
    // for view reprojection, lighting and shadow reconstruction.
    Properties
    {
        [HideInInspector] [NonModifiableTextureData] _ScatterDitherNoise ("LOD Blue Noise", 2D) = "gray" {}
        _BaseMap ("Octahedral Atlas (RGB albedo, A silhouette)", 2D) = "white" {}
        _NormalMap ("Octahedral Normal Atlas (view-space, encoded)", 2D) = "bump" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _HasSurfaceData ("Atlas contains packed normals, depth and leaf mask", Float) = 0
        _GridN ("Octahedral grid frames per axis", Float) = 8
        _CenterOffset ("Bake centre in object space (m)", Vector) = (0,1,0,0)
        _WorldSize ("Billboard square side (m)", Float) = 2
        _LeafCard ("Whole-card leaf translucency (0/1)", Float) = 0
        _CardBrightness ("Card brightness trim vs its mesh", Range(0.5,1.5)) = 1.0
        _FadeInStart ("Fade-in start (m)", Float) = 340
        _FadeInEnd ("Fade-in end (m)", Float) = 400
        _FadeOutStart ("Fade-out start (m)", Float) = 1100
        _FadeOutEnd ("Fade-out end (m)", Float) = 1200
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }

        // Shared octahedral + billboard helpers (kept in the include so both passes stay identical).
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Hemi-octahedral encode: unit direction (upper hemisphere, y = up) -> square uv in [0,1]^2. Inverse
        // of the C# bake's HemiOctDecode: dir straight up -> uv centre (top-down cell); the four horizon
        // cardinals -> the four corners.
        float2 HemiOctEncode(float3 n)
        {
            n = normalize(n);
            n.y = max(n.y, 0.0);
            n /= (abs(n.x) + abs(n.y) + abs(n.z));
            return float2(n.x + n.z, n.x - n.z) * 0.5 + 0.5;
        }

        // Upright billboard facing an arbitrary world direction (toward the camera for the lit pass, toward
        // the light for the shadow pass). Returns the quad world position for a unit centred quad vertex
        // (posOS.xy in [-0.5,0.5]) plus the basis and the continuous octahedral grid coordinate (frame = the
        // view direction resolved into the tree's object frame — matches the C# bake).
        void OctBillboardToward(float4x4 m, float3 viewDirW, float2 posXY, float gridN, float3 centerOffset,
            float worldSize, out float3 world, out float3 outRight, out float3 outUp, out float3 outView,
            out float2 gridCoord)
        {
            float3 up = normalize(float3(m._m01, m._m11, m._m21));       // surface normal (tree up)
            float3 objX = float3(m._m00, m._m10, m._m20);
            float scale = length(objX);
            objX = scale > 1e-6 ? objX / scale : float3(1, 0, 0);

            float3 center = mul(m, float4(centerOffset, 1.0)).xyz;
            float3 viewW = normalize(viewDirW);
            float3 right = cross(viewW, up);
            float rl = length(right);
            right = rl > 1e-3 ? right / rl : objX;                        // pole: tree's own x axis
            float3 qUp = normalize(cross(right, viewW));                  // bake camera up

            float2 q = posXY * (worldSize * scale);
            world = center + right * q.x + qUp * q.y;
            outRight = right;
            outUp = up;                                                  // surface normal (for lighting)
            outView = viewW;

            float3 viewObj = normalize(mul((float3x3)GetWorldToObjectMatrix(), viewW));
            gridCoord = HemiOctEncode(viewObj) * gridN;
        }

        // Camera-facing convenience wrapper: view direction = tree centre -> camera.
        void OctBillboard(float4x4 m, float2 posXY, float gridN, float3 centerOffset, float worldSize,
            out float3 world, out float3 outRight, out float3 outUp, out float3 outView, out float2 gridCoord)
        {
            float3 center = mul(m, float4(centerOffset, 1.0)).xyz;
            OctBillboardToward(m, _WorldSpaceCameraPos - center, posXY, gridN, centerOffset, worldSize,
                world, outRight, outUp, outView, gridCoord);
        }

        // A cell is 128 px and the card draws ~24 px tall the frame it takes over, so the hardware picks
        // mip ~2.4 - a 16-32 px image magnified back up. That second round of filtering is why an unbiased
        // card reads as a smooth blob beside a mesh whose canopy is still ragged at the same size.
        // Half a level back is as far as this can go. Measured over all 61 cards at 24 px, -0.5 lifts every
        // prop: edge energy against the last mesh LOD rises (worst case 0.57 -> 0.62) AND clipped area rises
        // with it (conifers 0.844 -> 0.894, wildflowers 0.695 -> 0.713). Push to -1 and area inverts - the
        // conifers fall to 0.794 and a distant pine trunk breaks into dashes, because the trunk is one texel
        // of near-cutoff alpha that only survives by mipMapsPreserveCoverage rescaling it in the deeper mips.
        // Sharpen past those mips and the rescale is gone. Fixing that means a thicker baked trunk, not a
        // bigger bias.
        #define CARD_MIP_BIAS (-0.5)

        // Reproject each baked view onto the current viewing ray. Sampling every view at the same UV
        // moves thin trunks and branches during a turn, even when the atlas contains the correct mesh.
        float2 SurfaceUv(TEXTURE2D_PARAM(surfaceAtlas, surfaceSampler), float2 gridCoord,
            float2 cell, float2 quadUv, float gridN, float surfaceData)
        {
            if (surfaceData < 0.5) return quadUv;
            float2 encoded = gridCoord / gridN * 2.0 - 1.0;
            float2 p = float2(encoded.x + encoded.y, encoded.x - encoded.y) * 0.5;
            float3 view = normalize(float3(p.x, 1.0 - abs(p.x) - abs(p.y), p.y));
            float3 right = cross(view, float3(0, 1, 0));
            right = dot(right, right) > 1e-6 ? normalize(right) : float3(1, 0, 0);
            float3 up = normalize(cross(right, view));
            float3 origin = right * (quadUv.x - 0.5) + up * (quadUv.y - 0.5);

            encoded = (cell + 0.5) / gridN * 2.0 - 1.0;
            p = float2(encoded.x + encoded.y, encoded.x - encoded.y) * 0.5;
            float3 bakeView = normalize(float3(p.x, 1.0 - abs(p.x) - abs(p.y), p.y));
            float3 bakeRight = cross(bakeView, float3(0, 1, 0));
            if (dot(bakeRight, bakeRight) < 1e-6) bakeRight = cross(bakeView, float3(0, 0, 1));
            bakeRight = normalize(bakeRight);
            float3 bakeUp = normalize(cross(bakeRight, bakeView));
            float2 uv = float2(dot(origin, bakeRight), dot(origin, bakeUp)) + 0.5;
            float depth = SAMPLE_TEXTURE2D_BIAS(surfaceAtlas, surfaceSampler,
                (cell + saturate(uv)) / gridN, CARD_MIP_BIAS).b - 0.5;
            // Background depth is 1; it cannot define a ray/surface intersection.
            if (depth >= 0.499) return saturate(uv);
            float rayDistance = (depth - dot(origin, bakeView)) / max(dot(view, bakeView), 0.1);
            float3 surface = origin + view * rayDistance;
            return saturate(float2(dot(surface, bakeRight), dot(surface, bakeUp)) + 0.5);
        }

        struct ImpostorSurface
        {
            half4 colour;
            float3 normalOS;
            float3 surfaceOS;
            float leafMask;
        };

        // All passes share the same reprojected samples. Read each normal/depth texel once after
        // reprojection and reuse it for lighting and shadow placement.
        ImpostorSurface SampleOctSurface(TEXTURE2D_PARAM(atlas, atlasSampler),
            TEXTURE2D_PARAM(surfaceAtlas, surfaceSampler), float2 gridCoord,
            float2 quadUv, float gridN, float surfaceData)
        {
            float2 g = gridCoord - 0.5;
            float2 baseC = floor(g);
            float2 f = saturate(g - baseC);
            ImpostorSurface result = (ImpostorSurface)0;
            [unroll] for (int cj = 0; cj < 2; cj++)
            [unroll] for (int ci = 0; ci < 2; ci++)
            {
                float2 cell = clamp(baseC + float2(ci, cj), 0.0, gridN - 1.0);
                float2 uv = SurfaceUv(TEXTURE2D_ARGS(surfaceAtlas, surfaceSampler),
                    gridCoord, cell, quadUv, gridN, surfaceData);
                float2 atlasUv = (cell + uv) / gridN;
                float w = (ci == 0 ? 1.0 - f.x : f.x) * (cj == 0 ? 1.0 - f.y : f.y);
                half4 colour = SAMPLE_TEXTURE2D_BIAS(atlas, atlasSampler, atlasUv, CARD_MIP_BIAS);
                result.colour += colour * w;
                float surfaceWeight = w * colour.a;
                float4 data = SAMPLE_TEXTURE2D_BIAS(surfaceAtlas, surfaceSampler, atlasUv, CARD_MIP_BIAS);
                float3 n = surfaceData > 0.5 ? UnpackNormalOctQuadEncode(data.rg * 2.0 - 1.0)
                    : data.rgb * 2.0 - 1.0;

                float2 encoded = (cell + 0.5) / gridN * 2.0 - 1.0;
                float2 p = float2(encoded.x + encoded.y, encoded.x - encoded.y) * 0.5;
                float3 view = normalize(float3(p.x, 1.0 - abs(p.x) - abs(p.y), p.y));
                float3 right = cross(float3(0, 1, 0), view);
                if (dot(right, right) < 1e-6) right = cross(float3(0, 0, 1), view);
                right = normalize(right);
                float3 up = normalize(cross(view, right));
                result.normalOS += (-right * n.x + up * n.y + view * n.z) * surfaceWeight;
                result.surfaceOS += (-right * (uv.x - 0.5) + up * (uv.y - 0.5)
                    + view * (data.b - 0.5)) * surfaceWeight;
                result.leafMask += data.a * surfaceWeight;
            }
            result.surfaceOS /= max(result.colour.a, 1e-6);
            result.leafMask /= max(result.colour.a, 1e-6);
            result.normalOS = dot(result.normalOS, result.normalOS) > 1e-6
                ? normalize(result.normalOS) : float3(0, 1, 0);
            return result;
        }

        #include "Includes/ScatterDither.hlsl"

        // Arrival ramp, shared with the mesh tiers. Folded into `coverage` rather than a DistanceDither,
        // because the impostor already dithers on its own fade-in/fade-out coverage. The buffers it reads are
        // declared per-pass in this shader, so SCATTER_APPEAR is defined after them, not here.
        float _ScatterFadeInSeconds;
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #pragma instancing_options procedural:setup
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/PlanetSunLighting.hlsl"
            #include "Includes/CloudShadows.hlsl"

            float3 _SunParams;
            float3 _PlanetCenter;
            float _NightAmbientIntensity;

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff;
                float _HasSurfaceData;
                float _GridN;
                float3 _CenterOffset;
                float _WorldSize;
                float _LeafCard;
                float _CardBrightness;
                float _FadeInStart;
                float _FadeInEnd;
                float _FadeOutStart;
                float _FadeOutEnd;
                // Must stay in every pass's UnityPerMaterial, in the same order: the SRP batcher requires an
                // identical layout across a shader's passes.
                float4 _LodDebugTint;
            CBUFFER_END

            // Look knob, global rather than per-material so it can be dialled live alongside the mesh tier
            // (see ShaderGlobalIds.Scatter). ScatterRenderer publishes it; unset reads 0 and the card simply
            // loses its backlight.
            float _FoliageBacklight;

            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                StructuredBuffer<float4x4> _ScatterMatrices;
                StructuredBuffer<float4x4> _ScatterMatricesInv;
                StructuredBuffer<uint> _ScatterVisible;
                StructuredBuffer<float> _ScatterBorn;
            #endif
            float ScatterAppear()
            {
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                if (_ScatterFadeInSeconds <= 0.0) return 1.0;
                return saturate((_Time.y - _ScatterBorn[_ScatterVisible[unity_InstanceID]]) / _ScatterFadeInSeconds);
            #else
                return 1.0;
            #endif
            }
            void setup()
            {
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                uint idx = _ScatterVisible[unity_InstanceID];
                unity_ObjectToWorld = _ScatterMatrices[idx];
                unity_WorldToObject = _ScatterMatricesInv[idx];
            #endif
            }

            struct Attributes
            {
                float4 positionOS : POSITION; // unit centred quad: xy in [-0.5,0.5], z=0
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 gridCoord : TEXCOORD1; // continuous octahedral frame coordinate
                float dist : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float3 billUp : TEXCOORD5;     // surface normal
                float3 billFwd : TEXCOORD6;    // view direction
                float3 positionWS : TEXCOORD7;
                float fogFactor : TEXCOORD8;
                float appear : TEXCOORD9;   // arrival ramp; unity_InstanceID is vertex-stage only
                float3 shadowSampleWS : TEXCOORD10;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 world, right, up, view;
                float2 gridCoord;
                OctBillboard(GetObjectToWorldMatrix(), IN.positionOS.xy, _GridN, _CenterOffset, _WorldSize,
                    world, right, up, view, gridCoord);

                // Legacy atlas fallback: one shadow sample off the quad and toward the sun. The ShadowCaster pass
                // draws a LIGHT-facing quad through the same centre, so the camera-facing quad this pass
                // shades cuts through its own caster: everything on the far side of that intersection reads
                // as shadowed, which paints a hard straight line across the card and drops a squat prop
                // (rock, bush) about 30% darker than the mesh it just replaced. Measured: rock cards
                // lum 0.718 of mesh with shadows on, 1.031 with them off. 112 of 155 cards start inside the
                // 250 m shadow distance, so this lands in the handover band, not past it.
                float4x4 objToWorld = GetObjectToWorldMatrix();
                float3 cardUp = normalize(float3(objToWorld._m01, objToWorld._m11, objToWorld._m21));
                float cardScale = length(float3(objToWorld._m00, objToWorld._m10, objToWorld._m20));
                float3 cardCenter = mul(objToWorld, float4(_CenterOffset, 1.0)).xyz;
                float3 towardSun = PlanetSunDirection(_SunParams, cardUp);
                OUT.shadowSampleWS = cardCenter + towardSun * (_WorldSize * cardScale * 0.6);

                OUT.positionHCS = TransformWorldToHClip(world);
                OUT.positionWS = world;
                OUT.uv = IN.uv;
                OUT.gridCoord = gridCoord;
                OUT.dist = distance(_WorldSpaceCameraPos, GetObjectToWorldMatrix()._m03_m13_m23);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                OUT.billUp = up;
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                OUT.appear = ScatterAppear();
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                ImpostorSurface sample = SampleOctSurface(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap),
                    TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), IN.gridCoord, IN.uv, _GridN, _HasSurfaceData);
                half4 card = sample.colour;
                clip(card.a - _Cutoff);

                float coverage = ScatterCardCoverage(IN.dist, float2(_FadeInStart, _FadeInEnd),
                    float2(_FadeOutStart, _FadeOutEnd), IN.appear);

                clip(coverage - ScatterDitherThreshold(IN.screenPos));

                // Real surface normal from the baked view-space normal atlas, reconstructed to world space via
                // the billboard basis (right/up/view) so the card shades with the tree's/rock's actual facets
                // instead of a synthesized hemisphere. Unbaked materials default the atlas to flat (viewer-facing).
                float3 surfaceOS = sample.surfaceOS;
                float leafMask = sample.leafMask;
                float3 normalOS = sample.normalOS;
                float3 N = TransformObjectToWorldNormal(normalOS);

                float3 planetNormal = normalize(IN.positionWS - _PlanetCenter);
                float3 sunDir = PlanetSunDirection(_SunParams, planetNormal);
                float localSun = dot(planetNormal, sunDir);
                float daylight = PlanetDaylightFromLocalSun(localSun);
                half ndl = saturate(dot(N, sunDir));
                float scale = length(GetObjectToWorldMatrix()._m00_m10_m20);
                // Depth reconstructs the receiver instead of sampling an artificially sunlit point.
                float3 surfaceWS = TransformObjectToWorld(_CenterOffset + surfaceOS * _WorldSize);
                // The receiver and caster reconstruct different sampled views. Allow one atlas texel
                // of depth error so quantization does not turn a lit facet into its own shadow.
                uint atlasWidth, atlasHeight;
                _NormalMap.GetDimensions(atlasWidth, atlasHeight);
                float depthTolerance = _WorldSize * scale * _GridN / max((float)atlasWidth, 1.0);
                float3 shadowPosition = _HasSurfaceData > 0.5
                    ? surfaceWS + sunDir * depthTolerance : IN.shadowSampleWS;
                half shadowAtten = MainLightShadow(TransformWorldToShadowCoord(shadowPosition), shadowPosition,
                    half4(1, 1, 1, 1), half4(0, 0, 0, 0));
                float cloudShadow = CloudShadowFactor(IN.positionWS, sunDir, localSun);
                // Shadow floor matches the mesh tier (Scatter.shader and FoliageLit both use 0.35). At 0.5 a
                // shadowed prop LIGHTENED as it crossed into impostor range, which is a pop in its own right.
                // The mesh tier self-shadows: a canopy darkens the trunk and the lower leaves, a boulder
                // darkens its own underside. A single quad cannot, so an uncorrected card reads brighter
                // than the mesh it replaced. This takes the library-wide part of that back off the
                // directional term, so a prop's shaded side stays where it is and only its lit side comes
                // down. What is left over is per-species; _CardBrightness below carries that.
                half cardSelfOcclusion = _HasSurfaceData > 0.5 ? 1.0 : 0.70;
                half shade = lerp(0.35, 1.0, shadowAtten * cloudShadow) * cardSelfOcclusion;
                // Shaded floor matches Scatter.shader's mesh tier (0.6) so a prop's dark side does not step
                // brighter as it crosses the mesh -> impostor handoff. These were 0.85 vs 0.6, which read as
                // a prop changing shade as you walked toward it.
                half3 dayColor = card.rgb * lerp(0.6, 1.28, ndl * shade);

                // Leaf translucency, matching the mesh tier (FoliageLit). Without it a tree lost its canopy
                // glow the frame it crossed into card range, which is a lighting pop in its own right - the
                // card range is exactly where a backlit tree line is most of what you see. Whole-card and
                // gated by _LeafCard because the atlas carries no leaf mask, so a trunk transmits too; at
                // card range a trunk is a few pixels wide and it does not read.
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float back = pow(saturate(dot(viewDir, -sunDir)), 1.8);
                float wrap = saturate(dot(N, -sunDir)) * 0.6 + 0.4;
                half3 transmitTint = half3(1.08, 1.04, 0.86);
                dayColor += card.rgb * transmitTint * (back * wrap * _FoliageBacklight * (_HasSurfaceData > 0.5 ? leafMask : _LeafCard))
                          * daylight * cloudShadow;
                // Per-species trim onto the mesh the card replaces. What cardSelfOcclusion leaves behind is
                // not a constant: over 185 props at the handover size, reeds and boulders sit within 3% of
                // their mesh while conifers run 24-31% bright. The directional term cannot carry that - the
                // 0.6 floor below caps how far it can pull a card down, and forcing it to 0.2 moved the
                // library mean only 1.062 -> 0.950 - so this scales the whole lit colour instead. The value
                // comes from the prop's own geometry: ScatterImpostorBaker.CardBrightnessOf.
                dayColor *= _HasSurfaceData > 0.5 ? 1.0 : _CardBrightness;
                // Cast shadow on the WHOLE card, matching FoliageLit: the form shading above has a 0.6
                // floor, so without this a shaded prop reads brighter as a card than as the mesh it
                // replaced and lightens at the handover.
                dayColor *= PlanetCastShadow(shadowAtten, daylight, 0.25);
                half3 nightColor = card.rgb * PlanetNightAmbient(_NightAmbientIntensity) * 0.6;
                // Linear, matching the mesh tier, the terrain and FoliageLit. See Scatter.shader for why the
                // old sqrt easing left props lit under a sun that had already set.
                half3 col = lerp(nightColor, dayColor, saturate(daylight));
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 aoUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-4);
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(aoUV);
                    col *= lerp(1.0, ao.indirectAmbientOcclusion, lerp(0.25, 0.5, _LeafCard));
                #endif
                col = lerp(col, _LodDebugTint.rgb, _LodDebugTint.a); // scatter.lodview: LOD-band colour
                // Fog LAST, like the mesh tier. Without it a prop shed its aerial perspective the instant it
                // crossed into impostor range — the tier that draws from 340 m to 2250 m, where fog matters most.
                col = MixFog(col, IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // Casts a ground shadow for the far tree so a distant impostor is grounded instead of floating
        // shadowless while the near mesh trees cast. The quad faces the LIGHT (not the camera) and samples
        // the octahedral cell for the light->tree direction, so the shadow is the tree's real silhouette
        // from the sun's angle — camera-independent and correct as the sun moves.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #pragma instancing_options procedural:setup
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff;
                float _HasSurfaceData;
                float _GridN;
                float3 _CenterOffset;
                float _WorldSize;
                float _LeafCard;
                float _CardBrightness;
                float _FadeInStart;
                float _FadeInEnd;
                float _FadeOutStart;
                float _FadeOutEnd;
                // Must stay in every pass's UnityPerMaterial, in the same order: the SRP batcher requires an
                // identical layout across a shader's passes.
                float4 _LodDebugTint;
            CBUFFER_END

            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                StructuredBuffer<float4x4> _ScatterMatrices;
                StructuredBuffer<float4x4> _ScatterMatricesInv;
                StructuredBuffer<uint> _ScatterVisible;
                StructuredBuffer<float> _ScatterBorn;
            #endif
            float ScatterAppear()
            {
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                if (_ScatterFadeInSeconds <= 0.0) return 1.0;
                return saturate((_Time.y - _ScatterBorn[_ScatterVisible[unity_InstanceID]]) / _ScatterFadeInSeconds);
            #else
                return 1.0;
            #endif
            }
            void setup()
            {
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                uint idx = _ScatterVisible[unity_InstanceID];
                unity_ObjectToWorld = _ScatterMatrices[idx];
                unity_WorldToObject = _ScatterMatricesInv[idx];
            #endif
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 gridCoord : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float dist : TEXCOORD3;
                float appear : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                // Face the light so the shadowmap sees the card; sample the cell for the light->tree view.
                float3 world, right, up, view;
                float2 gridCoord;
                OctBillboardToward(GetObjectToWorldMatrix(), _LightDirection, IN.positionOS.xy, _GridN,
                    _CenterOffset, _WorldSize, world, right, up, view, gridCoord);

                OUT.uv = IN.uv;
                OUT.gridCoord = gridCoord;
                float4 hcs = TransformWorldToHClip(ApplyShadowBias(world, _LightDirection, _LightDirection));
                #if UNITY_REVERSED_Z
                    hcs.z = min(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    hcs.z = max(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionHCS = hcs;
                OUT.screenPos = ComputeScreenPos(hcs);
                OUT.dist = distance(_WorldSpaceCameraPos, GetObjectToWorldMatrix()._m03_m13_m23);
                OUT.appear = ScatterAppear();
                return OUT;
            }

            float frag(Varyings IN) : SV_Depth
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                ImpostorSurface sample = SampleOctSurface(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap),
                    TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), IN.gridCoord, IN.uv, _GridN, _HasSurfaceData);
                half4 card = sample.colour;
                clip(card.a - _Cutoff);
                float coverage = ScatterCardCoverage(IN.dist, float2(_FadeInStart, _FadeInEnd),
                    float2(_FadeOutStart, _FadeOutEnd), IN.appear);
                clip(coverage - ScatterDitherThreshold(IN.screenPos));
                if (_HasSurfaceData < 0.5) return IN.positionHCS.z;
                float3 surfaceOS = sample.surfaceOS;
                float leafMask = sample.leafMask;
                float3 normalOS = sample.normalOS;
                float3 surface = TransformObjectToWorld(_CenterOffset + surfaceOS * _WorldSize);
                float4 hcs = TransformWorldToHClip(ApplyShadowBias(surface,
                    TransformObjectToWorldNormal(normalOS), _LightDirection));
                #if UNITY_REVERSED_Z
                    return min(hcs.z / hcs.w, UNITY_NEAR_CLIP_VALUE);
                #else
                    return max(hcs.z / hcs.w, UNITY_NEAR_CLIP_VALUE);
                #endif
            }
            ENDHLSL
        }

        // Writes the impostor into the depth-normals prepass so it lands in _CameraDepthTexture /
        // _CameraNormalsTexture (clouds/atmosphere/SSAO read those). Mirrors the ForwardLit billboard +
        // atlas-cell clip so the depth footprint matches the visible card exactly.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #pragma instancing_options procedural:setup

            #include "Includes/ImpostorDepthMotion.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            Cull Off
            ZWrite On
            ColorMask RG
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #define IMPOSTOR_MOTION_PASS
            #include "Includes/ImpostorDepthMotion.hlsl"
            ENDHLSL
        }
    }
}
