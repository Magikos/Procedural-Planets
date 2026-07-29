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
        _BaseMap ("Leaf Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _TrunkMap ("Trunk / Bark Albedo", 2D) = "white" {}
        _TrunkTint ("Trunk / Core Tint", Color) = (1,1,1,1)
        _SeasonColor ("Season Leaf Tint", Color) = (1,1,1,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.08
        _Cutoff ("Leaf Alpha Cutoff", Range(0,1)) = 0.4
        _LeafFall ("Leaf Fall (0 full .. 1 bare)", Range(0,1)) = 0
        _LeafMaskLo ("Leaf Mask Low (vtx.B)", Range(0,1)) = 0.6
        _LeafMaskHi ("Leaf Mask High (vtx.B)", Range(0,1)) = 0.85
        _LeafNormalUp ("Leaf Normal Up-Blend (canopy softness)", Range(0,1)) = 0.6
        [Toggle] _ForceLeaf ("Force Leaf (moss / hanging beards)", Float) = 0
        _WindStrength ("Wind Strength (m)", Float) = 0
        _WindFreq ("Wind Frequency", Float) = 1.6
        _FadeStart ("Fade Start Distance", Float) = 120
        _FadeEnd ("Fade End Distance", Float) = 150
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _TrunkTint;
            float4 _SeasonColor;
            float _Smoothness;
            float _Cutoff;
            float _LeafFall;
            float _LeafMaskLo;
            float _LeafMaskHi;
            float _LeafNormalUp;
            float _WindStrength;
            float _WindFreq;
            float _ForceLeaf;
            float _FadeStart;
            float _FadeEnd;
        CBUFFER_END

        static const float _Bayer4x4[16] = {
            0.0/16, 8.0/16, 2.0/16, 10.0/16,
            12.0/16, 4.0/16, 14.0/16, 6.0/16,
            3.0/16, 11.0/16, 1.0/16, 9.0/16,
            15.0/16, 7.0/16, 13.0/16, 5.0/16
        };

        void DistanceDither(float3 positionWS, float4 screenPos)
        {
            float dist = distance(positionWS, _WorldSpaceCameraPos);
            float fade = saturate((dist - _FadeStart) / max(1e-3, _FadeEnd - _FadeStart));
            float2 sp = (screenPos.xy / max(screenPos.w, 1e-4)) * _ScreenParams.xy;
            int2 pix = int2(fmod(sp, 4.0));
            clip(_Bayer4x4[pix.y * 4 + pix.x] - fade);
        }

        // Blue vertex channel: ~0 on the trunk, ~1 on the leaves. Remap to a 0..1 leaf mask.
        float LeafMask(float vtxBlue)
        {
            return smoothstep(_LeafMaskLo, _LeafMaskHi, vtxBlue);
        }

        // Leaves sway; the trunk stays put. Simple world-space breeze scaled by the leaf mask.
        float3 ApplyWind(float3 positionWS, float leafMask)
        {
            if (_WindStrength <= 0.0) return positionWS;
            float phase = _Time.y * _WindFreq + positionWS.x * 0.15 + positionWS.z * 0.13;
            float sway = sin(phase) * _WindStrength * leafMask;
            positionWS.x += sway;
            positionWS.z += sway * 0.6;
            return positionWS;
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/PlanetSunLighting.hlsl"

            // The planet is lit by the custom _SunParams sun (terrain + grass use it); the URP main
            // light does not drive it, so lighting foliage via GetMainLight left it ambient-only and
            // dark. Light foliage from _SunParams too so trees match the world and track day/night.
            float3 _SunParams;
            float3 _PlanetCenter;
            float _NightAmbientIntensity;

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
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                DistanceDither(IN.positionWS, IN.screenPos);

                half4 leaf = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 trunk = SAMPLE_TEXTURE2D(_TrunkMap, sampler_TrunkMap, IN.uv).rgb * _TrunkTint.rgb;
                float lm = IN.leafMask;

                // Trunk stays opaque; leaves cut out, and leaf-fall raises their cutoff toward bare.
                float cutoff = lerp(0.0, _Cutoff + _LeafFall, lm);
                float alpha = lerp(1.0, leaf.a, lm);
                clip(alpha - cutoff);

                half3 albedo = lerp(trunk, leaf.rgb * _SeasonColor.rgb, lm);

                // Double-sided: flip the normal on back faces so a leaf lit from either side reads correctly
                // instead of the back face going black (which made the canopy merge into dark clumps).
                float faceSign = IS_FRONT_VFACE(cullFace, 1.0, -1.0);
                float3 nrmWS = normalize(IN.normalWS) * faceSign;
                // Canopy softening: blend leaf normals toward world up so a dense canopy lights like a soft
                // volume (bright crown, gently lit sides/underside) instead of dark per-card faces. Trunk
                // (lm=0) keeps its true normal.
                nrmWS = normalize(lerp(nrmWS, float3(0.0, 1.0, 0.0), _LeafNormalUp * lm));

                // Planet sun lighting (matches the terrain/grass, which shade from _SunParams). Diffuse
                // only: albedo * a day level that ramps with the leaf normal facing the sun, blended to
                // a cool night ambient by the daylight factor at this point on the sphere.
                float3 planetNormal = normalize(IN.positionWS - _PlanetCenter);
                float3 sunDir = PlanetSunDirection(_SunParams, planetNormal);
                float daylight = PlanetDaylightFromLocalSun(dot(planetNormal, sunDir));
                float ndl = saturate(dot(nrmWS, sunDir));
                half3 dayColor = albedo * lerp(0.32, 1.0, ndl);
                float nightAmbient = PlanetNightAmbient(_NightAmbientIntensity);
                half3 nightColor = albedo * nightAmbient * 0.6;
                half3 col = lerp(nightColor, dayColor, daylight);
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            SVaryings shadowVert(SAttributes IN)
            {
                SVaryings OUT = (SVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
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
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.leafMask = leafMask;
                return OUT;
            }

            half4 shadowFrag(SVaryings IN) : SV_Target
            {
                float lm = IN.leafMask;
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a;
                float cutoff = lerp(0.0, _Cutoff + _LeafFall, lm);
                clip(lerp(1.0, a, lm) - cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
