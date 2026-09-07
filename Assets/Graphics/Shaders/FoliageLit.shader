Shader "Scatter/FoliageLit"
{
    // Lit foliage shader for scatter trees/plants. One material renders both trunk and canopy: the
    // mesh's vertex-colour BLUE channel is Synty's flex mask (low on the trunk, high on the leaves),
    // so we blend _TrunkMap (opaque bark) -> _BaseMap (alpha-cut leaves) by it. This handles both
    // baked-trunk meshes (trunk + leaf cards in one mesh) and separate trunk/foliage meshes.
    //
    // Season/wind are per-material knobs a season system can drive later (via property block or by
    // promotion to globals): _SeasonColor tints leaves only, _LeafFall raises the leaf cutoff so
    // leaves thin then bare, _WindStrength sways leaves (not the trunk) by the same mask. A screen-
    // space dither fades instances near the cull distance, matching Scatter.shader.
    Properties
    {
        [HideInInspector] [NonModifiableTextureData] _ScatterDitherNoise ("LOD Blue Noise", 2D) = "gray" {}
        _BaseMap ("Leaf Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _TrunkMap ("Trunk / Bark Albedo", 2D) = "white" {}
        _TrunkTint ("Trunk / Core Tint", Color) = (1,1,1,1)
        _SeasonColor ("Season Leaf Tint", Color) = (1,1,1,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.08
        _Cutoff ("Leaf Alpha Cutoff", Range(0,1)) = 0.4
        _LeafFall ("Leaf Fall (0 full .. 1 bare)", Range(0,1)) = 0
        _CutoffFadeMip ("Sub-pixel Cutoff Relax Start (mip)", Float) = 4
        _CutoffFadeRange ("Sub-pixel Cutoff Relax Range (mips)", Float) = 3
        _LeafMaskLo ("Leaf Mask Low (vtx.B)", Range(0,1)) = 0.6
        _LeafMaskHi ("Leaf Mask High (vtx.B)", Range(0,1)) = 0.85
        _LeafNormalUp ("Leaf Normal Up-Blend (canopy softness)", Range(0,1)) = 0.6
        [Toggle] _ForceLeaf ("Force Leaf (moss / hanging beards)", Float) = 0
        _WindStrength ("Wind Strength (m)", Float) = 0
        _WindFreq ("Wind Frequency", Float) = 1.6
        [HideInInspector] _WindFadeEnd ("Neutral Wind Distance", Float) = 0
        _FadeStart ("Fade Start Distance", Float) = 120
        _FadeEnd ("Fade End Distance", Float) = 150

        [Header(Leaf Colour Noise (Synty style))]
        _ColorNoiseStrength ("Colour Noise Strength", Range(0,1)) = 0.35
        _ColorNoiseLargeFreq ("Colour Noise Large Freq", Float) = 0.59
        _ColorNoiseSmallFreq ("Colour Noise Small Freq", Float) = 4.44
        _ColorNoiseWarm ("Colour Noise Warm Tint", Color) = (1.18, 1.0, 0.62, 1)
        _ColorNoiseCool ("Colour Noise Cool Tint", Color) = (0.82, 1.0, 0.88, 1)

        [Header(Leaf AO (baked in vertex colour G))]
        _LeafAOIntensity ("Leaf AO Intensity", Range(0,1)) = 0.5

        [Header(Player interaction (small plants only))]
        _InteractiveBend ("Interactive Bend Height (m, 0 = rigid)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Includes/PlanetWind.hlsl"
        #include "Includes/VegetationMotion.hlsl"
        #include "Includes/GrassInteractors.hlsl" // same global _GrassInteractors buffer the grass uses
        float _ImpostorAlbedoBake;
        float _ImpostorNormalBake;
        float3 _PlanetCenter;

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseMap_TexelSize;
            float4 _TrunkTint;
            float4 _SeasonColor;
            float _Smoothness;
            float _Cutoff;
            float _LeafFall;
            float _CutoffFadeMip;
            float _CutoffFadeRange;
            float _LeafMaskLo;
            float _LeafMaskHi;
            float _LeafNormalUp;
            float _WindStrength;
            float _WindFreq;
            float _WindFadeEnd;
            float _ForceLeaf;
            float _FadeStart;
            float _FadeEnd;
            float _ColorNoiseStrength;
            float _ColorNoiseLargeFreq;
            float _ColorNoiseSmallFreq;
            float4 _ColorNoiseWarm;
            float4 _ColorNoiseCool;
            float _LeafAOIntensity;
            float _InteractiveBend;
            float4 _LodDebugTint;
        CBUFFER_END

        // Look knob, global rather than per-material so it can be dialled live (see ShaderGlobalIds.Scatter).
        // ScatterRenderer publishes it; unset would read 0 and silently disable translucency.
        float _FoliageBacklight;

        // GPU-driven indirect draw: setup() feeds the per-instance transform from these buffers when drawn
        // via Graphics.RenderMeshIndirect (procedural instancing), leaving the RenderMeshInstanced path
        // untouched. It runs before every vert (all passes), so wind + ApplyInteractorBend (which read
        // unity_ObjectToWorld) and the ShadowCaster/DepthNormals passes all see the buffer transform.
        #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            StructuredBuffer<float4x4> _ScatterMatrices;
            StructuredBuffer<float4x4> _ScatterMatricesInv;
            StructuredBuffer<uint> _ScatterVisible; // this band's indices into the master matrix buffers
            // Arrival time per instance, so a newly gathered prop ramps in. See Scatter.shader.
            StructuredBuffer<float> _ScatterBorn;
        #endif
        float _ScatterFadeInSeconds;

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

        #include "Includes/ScatterDither.hlsl"

        void DistanceDither(float4 screenPos, float appear)
        {
            float dist = distance(GetObjectToWorldMatrix()._m03_m13_m23, _WorldSpaceCameraPos);
            float fade = saturate((dist - _FadeStart) / max(1e-3, _FadeEnd - _FadeStart));
            fade = max(fade, 1.0 - appear);
            clip(ScatterDitherThreshold(screenPos) > fade ? 1.0 : -1.0);

        }

        // Blue vertex channel: ~0 on the trunk, ~1 on the leaves. Remap to a 0..1 leaf mask.
        float LeafMask(float vtxBlue)
        {
            return smoothstep(_LeafMaskLo, _LeafMaskHi, vtxBlue);
        }

        // Trunk stays opaque; leaves cut out, and leaf-fall raises their cutoff toward bare.
        //
        // Alpha-tested coverage does not accumulate across overlapping SUB-PIXEL primitives. While the
        // leaf cards are several pixels wide they overlap and fill each other's alpha holes, so a canopy
        // reads solid. Once each card is about a pixel, one card alone claims the pixel and the leaf
        // texture's alpha coverage alone decides leaf-or-sky - the canopy speckles away and the tree
        // renders as a bare trunk-and-branch skeleton long before its impostor takes over. So relax the
        // cutoff toward 0 as the sampled mip climbs. Mip level IS texels-per-pixel, which makes this
        // independent of FOV, prototype scale, and whether the leaves come from one card texture or from
        // a single cell of a 4k atlas.
        float LeafCutoff(float2 uv, float lm)
        {
            float2 duv = max(abs(ddx(uv)), abs(ddy(uv))) * _BaseMap_TexelSize.zw;
            float mip = 0.5 * log2(max(dot(duv, duv), 1e-12));
            float shrink = saturate((mip - _CutoffFadeMip) / max(1e-3, _CutoffFadeRange));
            return lerp(0.0, (_Cutoff + _LeafFall) * (1.0 - shrink), lm);
        }

        // Cheap 3D value noise (hash-based, smooth-interpolated) for per-leaf/per-region colour variation.
        float FoliageHash(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }
        float FoliageNoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = FoliageHash(i + float3(0,0,0)), n100 = FoliageHash(i + float3(1,0,0));
            float n010 = FoliageHash(i + float3(0,1,0)), n110 = FoliageHash(i + float3(1,1,0));
            float n001 = FoliageHash(i + float3(0,0,1)), n101 = FoliageHash(i + float3(1,0,1));
            float n011 = FoliageHash(i + float3(0,1,1)), n111 = FoliageHash(i + float3(1,1,1));
            float nx00 = lerp(n000,n100,f.x), nx10 = lerp(n010,n110,f.x);
            float nx01 = lerp(n001,n101,f.x), nx11 = lerp(n011,n111,f.x);
            return lerp(lerp(nx00,nx10,f.y), lerp(nx01,nx11,f.y), f.z);
        }

        // Synty-style leaf colour variation: a large-frequency noise pushes whole regions warm/cool, a
        // small-frequency noise varies per-leaf brightness. Applied to leaves only (via leafMask outside),
        // so a canopy reads as many subtly different leaves instead of one flat colour.
        half3 LeafColourVariation(half3 albedo, float3 positionOS)
        {
            if (_ColorNoiseStrength <= 0.001) return albedo;
            float nL = FoliageNoise(positionOS * _ColorNoiseLargeFreq);
            float nS = FoliageNoise(positionOS * _ColorNoiseSmallFreq);
            half3 tint = lerp(_ColorNoiseCool.rgb, _ColorNoiseWarm.rgb, nL); // warm/cool patches
            tint *= lerp(0.85, 1.15, nS);                                    // per-leaf brightness
            return lerp(albedo, albedo * tint, _ColorNoiseStrength);
        }

        // Player/creature/projectile push on SMALL plants only (flowers, ferns), reusing the exact
        // global interactor buffer the grass reads. _InteractiveBend is the plant's height in metres and
        // doubles as the on/off gate: 0 (trees, rocks) => rigid, no buffer read. Roots stay planted and
        // the bend grows toward the top (height fraction squared), so the plant folds instead of sliding.
        float3 ApplyInteractorBend(float3 positionWS, bool previous)
        {
            if (_InteractiveBend <= 1e-3) return positionWS;
            float4x4 objectMatrix = GetObjectToWorldMatrix();
            #if !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                if (previous) objectMatrix = UNITY_PREV_MATRIX_M;
            #endif
            float3 rootWS = objectMatrix._m03_m13_m23;
            float3 upWS = normalize(mul((float3x3)objectMatrix, float3(0, 1, 0)));
            float heightFrac = saturate(dot(positionWS - rootWS, upWS) / _InteractiveBend);
            float3 bend = SampleGrassInteractorBend(rootWS, upWS, _InteractiveBend * 0.6, previous);
            return positionWS + bend * (heightFrac * heightFrac);
        }

        // Wind comes entirely from the shared wind system (PlanetWind globals): calm => zero motion (no
        // animation without a wind provider), otherwise it follows the weather wind direction + strength.
        // _WindStrength is this plant's per-material flex (sway metres at full wind); the trunk (leafMask
        // ~0, or _WindStrength 0) stays rigid. Three Synty-style layers on top of a steady downwind lean:
        //   lean    - slow whole-canopy push downwind (net-positive, never blows back upwind),
        //   branch  - medium per-branch sway (low spatial freq),
        //   flutter - fast small per-leaf shimmer (high spatial freq), thrown cross-wind for liveliness.
        // Spatial phases decorrelate neighbours so nothing sways in unison.
        float3 ApplyWind(float3 positionWS, float leafMask, bool previous)
        {
            if (_ImpostorAlbedoBake > 0.5 || _ImpostorNormalBake > 0.5) return positionWS;
            previous = HasVegetationHistory(previous);
            positionWS = ApplyInteractorBend(positionWS, previous); // push before wind; both add on the tangent plane
            float flex = leafMask * _WindStrength;
            if (_WindFadeEnd > 0.0)
            {
                float3 cameraPosition = previous ? _VegetationPreviousCamera : _WorldSpaceCameraPos;
                float dist = distance(GetObjectToWorldMatrix()._m03_m13_m23, cameraPosition);
                flex *= 1.0 - smoothstep(_WindFadeEnd * 0.65, _WindFadeEnd, dist);
            }
            float strength = previous ? saturate(max(_VegetationPreviousTime.z, _VegetationPreviousWind.w * 0.06))
                : saturate(max(_WindStrength01, _WindSpeedMps * 0.06));
            if (strength <= 1e-4 || flex <= 1e-5) return positionWS;

            float3 radial = positionWS - _PlanetCenter;
            float3 up = dot(radial, radial) > 1e-6 ? normalize(radial) : float3(0, 1, 0);
            float3 wind = previous ? _VegetationPreviousWind.xyz : _WindDirection;
            float3 dir = wind - up * dot(wind, up);
            float tangentLength = length(dir);
            if (tangentLength <= 1e-5) return positionWS;
            dir /= tangentLength;
            float3 side = cross(dir, up);
            float t = (previous ? _VegetationPreviousTime.x : _Time.y) * _WindFreq;
            float ph  = dot(positionWS, float3(1.6, 0.4, 1.35)); // branch-scale spatial phase
            float phF = dot(positionWS, float3(9.3, 7.1, 11.7)); // leaf-scale spatial phase

            float lean    = 0.55 + 0.45 * sin(t * 0.4 + ph * 0.6); // 0.1..1.0: always downwind
            float branch  = sin(t * 1.1 + ph * 2.1) * 0.30;
            float flutter = sin(t * 2.9 + phF) * 0.16;

            float along = (lean + branch) * flex * strength;
            float crossAmt = flutter * flex * strength;
            return positionWS + dir * along + side * crossAmt;
        }
        float3 ApplyWind(float3 positionWS, float leafMask)
        {
            return ApplyWind(positionWS, leafMask, false);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off // foliage cards are single quads: draw both sides so they don't vanish edge-on / from behind

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #pragma instancing_options procedural:setup
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/PlanetSunLighting.hlsl"
            #include "Includes/CloudShadows.hlsl"

            // The planet is lit by the custom _SunParams sun (terrain + grass use it); the URP main
            // light does not drive it, so lighting foliage via GetMainLight left it ambient-only and
            // dark. Light foliage from _SunParams too so trees match the world and track day/night.
            float3 _SunParams;
            float _NightAmbientIntensity;
            float _ImpostorBakeDistance;
            float _ImpostorBakeSize;

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_TrunkMap); SAMPLER(sampler_TrunkMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float leafMask : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                float4 screenPos : TEXCOORD5;
                float leafAO : TEXCOORD6; // Synty baked leaf AO (vertex colour G)
                float appear : TEXCOORD7;   // arrival ramp; unity_InstanceID is vertex-stage only
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float leafMask = max(LeafMask(IN.color.b), _ForceLeaf);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                float3 wsp = ApplyWind(pos.positionWS, leafMask);
                OUT.positionWS = wsp;
                OUT.positionHCS = TransformWorldToHClip(wsp);
                OUT.normalWS = GetVertexNormalInputs(IN.normalOS).normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.leafMask = leafMask;
                OUT.leafAO = IN.color.g; // Synty bakes leaf AO into vertex colour G (0 occluded .. 1 exposed)
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                OUT.appear = ScatterAppear();
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                DistanceDither(IN.screenPos, IN.appear);

                half4 leaf = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 trunk = SAMPLE_TEXTURE2D(_TrunkMap, sampler_TrunkMap, IN.uv).rgb * _TrunkTint.rgb;
                float lm = IN.leafMask;

                float alpha = lerp(1.0, leaf.a, lm);
                clip(alpha - LeafCutoff(IN.uv, lm));

                half3 albedo = lerp(trunk, leaf.rgb * _SeasonColor.rgb, lm);
                // Per-leaf/region colour variation (Synty-style noise) on the leaves only, so the canopy
                // reads as many subtly different leaves instead of one flat green mass.
                albedo = lerp(albedo, LeafColourVariation(albedo, TransformWorldToObject(IN.positionWS)), lm);
                // Synty baked leaf AO (vertex colour G): darken occluded interior leaves toward the
                // bright exposed crown. Leaf-only (lm) so the trunk (G=0) is not blackened.
                float leafAO = lerp(1.0 - _LeafAOIntensity, 1.0, IN.leafAO);
                albedo *= lerp(1.0, leafAO, lm);

                // Impostor bake: return flat unlit albedo (leaf cutout + baked leaf-AO kept, no directional
                // sun) so the runtime impostor shader relights it. No half-lit / black-side impostor.
                if (_ImpostorAlbedoBake > 0.5) return half4(albedo, 1.0);

                // Double-sided: flip the normal on back faces so a leaf lit from either side reads correctly
                // instead of the back face going black (which made the canopy merge into dark clumps).
                float faceSign = IS_FRONT_VFACE(cullFace, 1.0, -1.0);
                float3 nrmWS = normalize(IN.normalWS) * faceSign;
                // Canopy softening: blend leaf normals toward world up so a dense canopy lights like a soft
                // volume (bright crown, gently lit sides/underside) instead of dark per-card faces. Trunk
                // (lm=0) keeps its true normal.
                nrmWS = normalize(lerp(nrmWS, normalize(TransformObjectToWorldDir(float3(0, 1, 0))), _LeafNormalUp * lm));
                // Impostor normal pass: output the view-space (canopy-softened) normal so the runtime relights
                // the card with real structure instead of a synthesized hemisphere.
                if (_ImpostorNormalBake > 0.5)
                {
                    float depth = (TransformWorldToView(IN.positionWS).z + _ImpostorBakeDistance)
                        / max(_ImpostorBakeSize, 0.001) + 0.5;
                    return half4(PackNormalOctQuadEncode(mul((float3x3)UNITY_MATRIX_V, nrmWS)) * 0.5 + 0.5, saturate(depth), lm);
                }

                // Planet sun lighting (matches the terrain/grass, which shade from _SunParams). Diffuse
                // only: albedo * a day level that ramps with the leaf normal facing the sun, blended to
                // a cool night ambient by the daylight factor at this point on the sphere.
                float3 planetNormal = normalize(IN.positionWS - _PlanetCenter);
                float3 sunDir = PlanetSunDirection(_SunParams, planetNormal);
                float localSun = dot(planetNormal, sunDir);
                float daylight = PlanetDaylightFromLocalSun(localSun);
                float ndl = saturate(dot(nrmWS, sunDir));

                // Receive the Sun's cast shadow (the URP main light tracks _SunParams): the shadow pulls
                // the lit term toward the ambient floor, so canopy/terrain shadows darken the trees rather
                // than only being cast off them. cloudShadow is the same shared factor terrain/grass/water
                // use, so drifting clouds darken the trees along with the rest of the world.
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                half shadowAtten = MainLightShadow(shadowCoord, IN.positionWS, half4(1, 1, 1, 1), half4(0, 0, 0, 0));
                float cloudShadow = CloudShadowFactor(IN.positionWS, sunDir, localSun);
                // Leaves take only a soft self-shadow (never below ~0.5) so a dense canopy stays lush at
                // eye level instead of collapsing to black where it shadows its own sides; the trunk keeps
                // the full cast shadow so it still grounds. Higher ambient floor keeps shaded foliage green.
                float leafShade = lerp(0.35, 1.0, shadowAtten * cloudShadow);
                float trunkShade = lerp(0.3, 1.0, shadowAtten * cloudShadow);
                float direct = ndl * lerp(trunkShade, leafShade, lm);
                half3 dayColor = albedo * lerp(0.6, 1.28, direct);
                // Leaf translucency: the canopy glows where the sun is behind the leaves (lm = leaf mask, so
                // the trunk is excluded). This is the single biggest thing separating a stylised forest from
                // a pile of opaque geometry, so the lobe is deliberately BROAD - a tight one (pow 3) only lit
                // the few leaves sitting exactly on the sun vector, which reads as a specular glint rather
                // than a lit canopy. `wrap` adds the light that bleeds THROUGH a leaf facing away from the
                // sun, and the tint warms it, because a real leaf transmits yellow-green while it reflects
                // green.
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float back = pow(saturate(dot(viewDir, -sunDir)), 1.8);
                float wrap = saturate(dot(nrmWS, -sunDir)) * 0.6 + 0.4;
                // Only a slight warm shift: pushing blue down hard turned whole canopies mustard-brown and
                // cost them their green, which reads as autumn rather than as sunlit.
                half3 transmitTint = half3(1.08, 1.04, 0.86);
                dayColor += albedo * transmitTint * (back * wrap * _FoliageBacklight) * daylight * cloudShadow * lm;
                // Cast shadow on the WHOLE plant so understory foliage under a tree visibly darkens — the
                // soft leafShade above only gives canopy interior depth. Sunlit crowns (shadowAtten≈1) are
                // untouched.
                dayColor *= PlanetCastShadow(shadowAtten, daylight, 0.25);
                float nightAmbient = PlanetNightAmbient(_NightAmbientIntensity);
                half3 nightColor = albedo * nightAmbient * 0.6;
                half3 col = lerp(nightColor, dayColor, daylight);

                // Screen-space AO — applied gently so it seats trees on the ground and shades interiors
                // without crushing the dense canopy to black.
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 aoUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-4);
                    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(aoUV);
                    col *= lerp(1.0, aoFactor.indirectAmbientOcclusion, 0.5);
                #endif

                col = lerp(col, _LodDebugTint.rgb, _LodDebugTint.a); // scatter.lodview: LOD-band colour
                col = MixFog(col, IN.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #pragma instancing_options procedural:setup

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float3 _LightDirection;

            struct SAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct SVaryings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float leafMask : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float appear : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            SVaryings shadowVert(SAttributes IN)
            {
                SVaryings OUT = (SVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float leafMask = max(LeafMask(IN.color.b), _ForceLeaf);
                float3 posWS = ApplyWind(TransformObjectToWorld(IN.positionOS.xyz), leafMask);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 hcs = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    hcs.z = min(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    hcs.z = max(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionHCS = hcs;
                OUT.screenPos = ComputeScreenPos(hcs);
                OUT.appear = ScatterAppear();
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.leafMask = leafMask;
                return OUT;
            }

            half4 shadowFrag(SVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                DistanceDither(IN.screenPos, IN.appear);
                float lm = IN.leafMask;
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a;
                clip(lerp(1.0, a, lm) - LeafCutoff(IN.uv, lm));
                return 0;
            }
            ENDHLSL
        }

        // Writes depth + world normal so the scatter lands in the URP depth-normals prepass (and thus
        // _CameraDepthTexture / _CameraNormalsTexture) — URP runs this pass automatically for the instanced
        // draw. Clip matches ForwardLit exactly (same leaf cutoff + DistanceDither + wind) so the depth
        // silhouette equals the drawn canopy — otherwise clouds/atmosphere/SSAO composite over the trees.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex dnVert
            #pragma fragment dnFrag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #pragma instancing_options procedural:setup

            #include "Includes/FoliageDepthMotion.hlsl"
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
            #pragma vertex dnVert
            #pragma fragment dnFrag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #define FOLIAGE_MOTION_PASS
            #include "Includes/FoliageDepthMotion.hlsl"
            ENDHLSL
        }
    }
}
