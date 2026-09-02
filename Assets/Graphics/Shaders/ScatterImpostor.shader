Shader "Scatter/Impostor"
{
    // Far-field scatter impostor, OCTAHEDRAL: the prototype is baked from a GridN×GridN grid of hemisphere
    // camera angles (hemi-octahedral layout) into one atlas (RGB = UNLIT albedo, A = silhouette). The quad
    // is camera-facing and kept upright; the fragment picks the atlas cell matching the view direction in
    // the tree's own frame, so the card reads as the tree from ANY angle — including straight down, where a
    // single front-view billboard would foreshorten to a slab. Lighting is applied here at runtime from the
    // planet _SunParams sun + ambient the mesh uses (bright by day, dim at night), a synthesized canopy
    // normal shading the card like a soft blob so the sun-facing side lights regardless of view.
    Properties
    {
        _BaseMap ("Octahedral Atlas (RGB albedo, A silhouette)", 2D) = "white" {}
        _NormalMap ("Octahedral Normal Atlas (view-space, encoded)", 2D) = "bump" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _NormalBulge ("Canopy Normal Bulge", Range(0.2,2)) = 1.3
        _GridN ("Octahedral grid frames per axis", Float) = 8
        _CenterOffset ("Billboard centre height above pivot (m)", Float) = 1
        _WorldSize ("Billboard square side (m)", Float) = 2
        _LeafCard ("Whole-card leaf translucency (0/1)", Float) = 0
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
        void OctBillboardToward(float4x4 m, float3 viewDirW, float2 posXY, float gridN, float centerOffset,
            float worldSize, out float3 world, out float3 outRight, out float3 outUp, out float3 outView,
            out float2 gridCoord)
        {
            float3 origin = float3(m._m03, m._m13, m._m23);
            float3 up = normalize(float3(m._m01, m._m11, m._m21));       // surface normal (tree up)
            float3 objX = float3(m._m00, m._m10, m._m20);
            float scale = length(objX);
            objX = scale > 1e-6 ? objX / scale : float3(1, 0, 0);

            float3 center = origin + up * (centerOffset * scale);
            float3 viewW = normalize(viewDirW);
            float3 right = cross(up, viewW);
            float rl = length(right);
            right = rl > 1e-3 ? right / rl : objX;                        // pole: tree's own x axis
            float3 qUp = normalize(cross(viewW, right));                  // surface-up projected -> stays upright

            float2 q = posXY * (worldSize * scale);
            world = center + right * q.x + qUp * q.y;
            outRight = right;
            outUp = up;                                                  // surface normal (for lighting)
            outView = viewW;

            float3 viewObj = normalize(mul((float3x3)GetWorldToObjectMatrix(), viewW));
            gridCoord = HemiOctEncode(viewObj) * gridN;
        }

        // Camera-facing convenience wrapper: view direction = tree centre -> camera.
        void OctBillboard(float4x4 m, float2 posXY, float gridN, float centerOffset, float worldSize,
            out float3 world, out float3 outRight, out float3 outUp, out float3 outView, out float2 gridCoord)
        {
            float3 origin = float3(m._m03, m._m13, m._m23);
            float3 up = normalize(float3(m._m01, m._m11, m._m21));
            float scale = length(float3(m._m00, m._m10, m._m20));
            float3 center = origin + up * (centerOffset * scale);
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

        // Bilinear blend of the 4 neighbour octahedral frames (weights from the fractional grid coord), so
        // the card cross-fades between baked angles as the camera orbits instead of snapping cell-to-cell.
        // Cell centres sit at integer+0.5 (the bake framed cell i at (i+0.5)/N), so shift by 0.5 first.
        //
        // Unweighted on purpose. A texel solid in one cell is often empty in its neighbour, and an empty
        // texel is the bake background, so weighting colour by each cell's own alpha looks like the obvious
        // correction. Measured across all 61 cards it moves the card/mesh luminance ratio by at most 0.003:
        // the baker already dilates colour into the transparent texels, and neighbouring view angles agree
        // almost everywhere they overlap. Not the reason a card reads dimmer than its mesh.
        half4 SampleOctBlended(TEXTURE2D_PARAM(atlas, atlasSampler), float2 gridCoord, float2 quadUv, float gridN)
        {
            float2 g = gridCoord - 0.5;
            float2 baseC = floor(g);
            float2 f = saturate(g - baseC);
            half4 c = 0;
            [unroll] for (int cj = 0; cj < 2; cj++)
            [unroll] for (int ci = 0; ci < 2; ci++)
            {
                float2 cell = clamp(baseC + float2(ci, cj), 0.0, gridN - 1.0);
                float2 uv = (cell + quadUv) / gridN;
                float w = (ci == 0 ? 1.0 - f.x : f.x) * (cj == 0 ? 1.0 - f.y : f.y);
                c += SAMPLE_TEXTURE2D_BIAS(atlas, atlasSampler, uv, CARD_MIP_BIAS) * w;
            }
            return c;
        }

        // Level centres, not level floors — see the note in Scatter.shader. Here the compare runs the other
        // way (clip(coverage - threshold)), so a 0.0 cell instead KEEPS one pixel in 16 of a card that has
        // faded to nothing.
        static const float _Bayer4x4[16] = {
            0.5/16, 8.5/16, 2.5/16, 10.5/16,
            12.5/16, 4.5/16, 14.5/16, 6.5/16,
            3.5/16, 11.5/16, 1.5/16, 9.5/16,
            15.5/16, 7.5/16, 13.5/16, 5.5/16
        };

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/PlanetSunLighting.hlsl"
            #include "Includes/CloudShadows.hlsl"

            float3 _SunParams;
            float _NightAmbientIntensity;

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff;
                float _NormalBulge;
                float _GridN;
                float _CenterOffset;
                float _WorldSize;
                float _LeafCard;
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
                float3 billRight : TEXCOORD4;
                float3 billUp : TEXCOORD5;     // surface normal
                float3 billFwd : TEXCOORD6;    // view direction
                float3 positionWS : TEXCOORD7;
                float fogFactor : TEXCOORD8;
                float appear : TEXCOORD9;   // arrival ramp; unity_InstanceID is vertex-stage only
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 world, right, up, view;
                float2 gridCoord;
                OctBillboard(GetObjectToWorldMatrix(), IN.positionOS.xy, _GridN, _CenterOffset, _WorldSize,
                    world, right, up, view, gridCoord);

                OUT.positionHCS = TransformWorldToHClip(world);
                OUT.positionWS = world;
                OUT.uv = IN.uv;
                OUT.gridCoord = gridCoord;
                OUT.dist = distance(_WorldSpaceCameraPos, world);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                OUT.billRight = right;
                OUT.billUp = up;
                OUT.billFwd = view;
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                OUT.appear = ScatterAppear();
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 card = SampleOctBlended(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), IN.gridCoord, IN.uv, _GridN);
                clip(card.a - _Cutoff);

                float fadeIn = saturate((IN.dist - _FadeInStart) / max(1e-3, _FadeInEnd - _FadeInStart));
                float fadeOut = 1.0 - saturate((IN.dist - _FadeOutStart) / max(1e-3, _FadeOutEnd - _FadeOutStart));
                float coverage = fadeIn * fadeOut * IN.appear;

                float2 sp = (IN.screenPos.xy / max(IN.screenPos.w, 1e-4)) * _ScreenParams.xy;
                int2 pix = int2(fmod(sp, 4.0));
                clip(coverage - _Bayer4x4[pix.y * 4 + pix.x]);

                // Real surface normal from the baked view-space normal atlas, reconstructed to world space via
                // the billboard basis (right/up/view) so the card shades with the tree's/rock's actual facets
                // instead of a synthesized hemisphere. Unbaked materials default the atlas to flat (viewer-facing).
                float3 nEnc = SampleOctBlended(TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), IN.gridCoord, IN.uv, _GridN).rgb;
                float3 nv = nEnc * 2.0 - 1.0;
                // Reconstruct through the SAME frame the bake captured in: view-space x=billRight,
                // y=the camera up (cross(view,right), not the surface up), z=view. Using the surface up would
                // mis-map the normal for non-horizontal (top-down) view cells.
                float3 camUp = normalize(cross(IN.billFwd, IN.billRight));
                float3 N = normalize(IN.billRight * nv.x + camUp * nv.y + IN.billFwd * nv.z);

                float3 planetNormal = normalize(IN.billUp);
                float3 sunDir = PlanetSunDirection(_SunParams, planetNormal);
                float localSun = dot(planetNormal, sunDir);
                float daylight = PlanetDaylightFromLocalSun(localSun);
                half ndl = saturate(dot(N, sunDir));
                half shadowAtten = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));
                float cloudShadow = CloudShadowFactor(IN.positionWS, sunDir, localSun);
                // Shadow floor matches the mesh tier (Scatter.shader and FoliageLit both use 0.35). At 0.5 a
                // shadowed prop LIGHTENED as it crossed into impostor range, which is a pop in its own right.
                half shade = lerp(0.35, 1.0, shadowAtten * cloudShadow);
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
                dayColor += card.rgb * transmitTint * (back * wrap * _FoliageBacklight * _LeafCard)
                          * daylight * cloudShadow;
                // Cast shadow on the WHOLE card, matching FoliageLit: the form shading above has a 0.6
                // floor, so without this a shaded prop reads brighter as a card than as the mesh it
                // replaced and lightens at the handover.
                dayColor *= PlanetCastShadow(shadowAtten, daylight, 0.25);
                half3 nightColor = card.rgb * PlanetNightAmbient(_NightAmbientIntensity) * 0.6;
                // Linear, matching the mesh tier, the terrain and FoliageLit. See Scatter.shader for why the
                // old sqrt easing left props lit under a sun that had already set.
                half3 col = lerp(nightColor, dayColor, saturate(daylight));
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff;
                float _NormalBulge;
                float _GridN;
                float _CenterOffset;
                float _WorldSize;
                float _LeafCard;
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);

                // Face the light so the shadowmap sees the card; sample the cell for the light->tree view.
                float3 world, right, up, view;
                float2 gridCoord;
                OctBillboardToward(GetObjectToWorldMatrix(), -_LightDirection, IN.positionOS.xy, _GridN,
                    _CenterOffset, _WorldSize, world, right, up, view, gridCoord);

                OUT.uv = IN.uv;
                OUT.gridCoord = gridCoord;
                float4 hcs = TransformWorldToHClip(ApplyShadowBias(world, -_LightDirection, _LightDirection));
                #if UNITY_REVERSED_Z
                    hcs.z = min(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    hcs.z = max(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionHCS = hcs;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 card = SampleOctBlended(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), IN.gridCoord, IN.uv, _GridN);
                clip(card.a - _Cutoff);
                return 0;
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

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff;
                float _NormalBulge;
                float _GridN;
                float _CenterOffset;
                float _WorldSize;
                float _LeafCard;
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
                float dist : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float3 billRight : TEXCOORD4;
                float3 billUp : TEXCOORD5;
                float3 billFwd : TEXCOORD6;
                float appear : TEXCOORD8;
                float3 positionWS : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 world, right, up, view;
                float2 gridCoord;
                OctBillboard(GetObjectToWorldMatrix(), IN.positionOS.xy, _GridN, _CenterOffset, _WorldSize,
                    world, right, up, view, gridCoord);

                OUT.positionHCS = TransformWorldToHClip(world);
                OUT.positionWS = world;
                OUT.uv = IN.uv;
                OUT.gridCoord = gridCoord;
                OUT.dist = distance(_WorldSpaceCameraPos, world);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                OUT.billRight = right;
                OUT.billUp = up;
                OUT.billFwd = view;
                OUT.appear = ScatterAppear();
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 card = SampleOctBlended(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), IN.gridCoord, IN.uv, _GridN);
                clip(card.a - _Cutoff);

                float fadeIn = saturate((IN.dist - _FadeInStart) / max(1e-3, _FadeInEnd - _FadeInStart));
                float fadeOut = 1.0 - saturate((IN.dist - _FadeOutStart) / max(1e-3, _FadeOutEnd - _FadeOutStart));
                float coverage = fadeIn * fadeOut * IN.appear;

                float2 sp = (IN.screenPos.xy / max(IN.screenPos.w, 1e-4)) * _ScreenParams.xy;
                int2 pix = int2(fmod(sp, 4.0));
                clip(coverage - _Bayer4x4[pix.y * 4 + pix.x]);

                float cx = IN.uv.x * 2.0 - 1.0;
                float cy = IN.uv.y * 2.0 - 1.0;
                float nz = sqrt(saturate(_NormalBulge - cx * cx - cy * cy));
                float3 N = normalize(IN.billRight * cx + IN.billUp * cy + IN.billFwd * nz);
                return half4(N, 0);
            }
            ENDHLSL
        }
    }
}
