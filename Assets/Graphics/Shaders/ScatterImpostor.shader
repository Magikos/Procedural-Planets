Shader "Scatter/Impostor"
{
    // Far-field scatter impostor: one camera-facing quad per instance, textured with a baked front-view
    // card (RGB = UNLIT albedo, A = silhouette). Billboards cylindrically around the instance's surface-up
    // axis (trees stay upright on the sphere), alpha-cuts the card, and dither cross-fades in over the
    // mesh-LOD cull band and out at its own far cull. Lighting is applied here at runtime from the same
    // URP main light + ambient SH the foliage mesh uses, so impostors track the day/night sun (bright by
    // day, dark at night) instead of freezing a bake-time light. A synthesized spherical normal shades the
    // card like a soft canopy blob so the sun-facing side lights regardless of view angle.
    Properties
    {
        _BaseMap ("Card Albedo (RGB) Silhouette (A)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _NormalBulge ("Canopy Normal Bulge", Range(0.2,2)) = 1.3
        _FadeInStart ("Fade-in start (m)", Float) = 340
        _FadeInEnd ("Fade-in end (m)", Float) = 400
        _FadeOutStart ("Fade-out start (m)", Float) = 1100
        _FadeOutEnd ("Fade-out end (m)", Float) = 1200
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/PlanetSunLighting.hlsl"
            #include "Includes/CloudShadows.hlsl"

            // Light from the planet _SunParams sun (like FoliageLit + terrain), not the URP main light,
            // so impostors match the near mesh trees and track day/night.
            float3 _SunParams;
            float _NightAmbientIntensity;

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff;
                float _NormalBulge;
                float _FadeInStart;
                float _FadeInEnd;
                float _FadeOutStart;
                float _FadeOutEnd;
            CBUFFER_END

            // GPU-driven indirect draw: setup() feeds unity_ObjectToWorld (which GetObjectToWorldMatrix reads)
            // from the buffer under procedural instancing; the RenderMeshInstanced path is untouched.
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                StructuredBuffer<float4x4> _ScatterMatrices;
                StructuredBuffer<float4x4> _ScatterMatricesInv;
                StructuredBuffer<uint> _ScatterVisible; // this band's indices into the master matrix buffers
            #endif
            void setup()
            {
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                uint idx = _ScatterVisible[unity_InstanceID];
                unity_ObjectToWorld = _ScatterMatrices[idx];
                unity_WorldToObject = _ScatterMatricesInv[idx];
            #endif
            }

            static const float _Bayer4x4[16] = {
                0.0/16, 8.0/16, 2.0/16, 10.0/16,
                12.0/16, 4.0/16, 14.0/16, 6.0/16,
                3.0/16, 11.0/16, 1.0/16, 9.0/16,
                15.0/16, 7.0/16, 13.0/16, 5.0/16
            };

            struct Attributes
            {
                float4 positionOS : POSITION; // quad: x in [-w/2,w/2], y in [0,h], z=0
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float dist : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float3 billRight : TEXCOORD3; // billboard basis, world space
                float3 billUp : TEXCOORD4;
                float3 billFwd : TEXCOORD5;
                float3 positionWS : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);

                // Per-instance world basis from the instance-aware object->world matrix (GetObjectToWorld
                // resolves the per-instance matrix under GPU instancing; raw unity_ObjectToWorld field
                // access does not). Origin at the pivot (tree base), up = the y-column (surface normal on
                // the sphere), uniform scale from the x-column.
                float4x4 m = GetObjectToWorldMatrix();
                float3 origin = float3(m._m03, m._m13, m._m23);
                float3 up = normalize(float3(m._m01, m._m11, m._m21));
                float scale = length(float3(m._m00, m._m10, m._m20));

                float3 toCam = _WorldSpaceCameraPos - origin;
                float3 fwd = toCam - up * dot(toCam, up); // cylindrical: face the camera around the up axis
                fwd = normalize(fwd);
                float3 right = normalize(cross(up, fwd));

                float3 local = IN.positionOS.xyz * scale;
                float3 world = origin + right * local.x + up * local.y;

                OUT.positionHCS = TransformWorldToHClip(world);
                OUT.positionWS = world;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.dist = distance(_WorldSpaceCameraPos, origin);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                OUT.billRight = right;
                OUT.billUp = up;
                OUT.billFwd = fwd;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 card = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(card.a - _Cutoff);

                float fadeIn = saturate((IN.dist - _FadeInStart) / max(1e-3, _FadeInEnd - _FadeInStart));
                float fadeOut = 1.0 - saturate((IN.dist - _FadeOutStart) / max(1e-3, _FadeOutEnd - _FadeOutStart));
                float coverage = fadeIn * fadeOut;

                float2 sp = (IN.screenPos.xy / max(IN.screenPos.w, 1e-4)) * _ScreenParams.xy;
                int2 pix = int2(fmod(sp, 4.0));
                clip(coverage - _Bayer4x4[pix.y * 4 + pix.x]);

                // Synthesized canopy normal: treat the card as a hemisphere bulging toward the viewer so
                // the sun-facing side lights and the far side falls into shade, regardless of camera angle.
                float cx = IN.uv.x * 2.0 - 1.0;
                float cy = IN.uv.y * 2.0 - 1.0;
                float nz = sqrt(saturate(_NormalBulge - cx * cx - cy * cy));
                float3 N = normalize(IN.billRight * cx + IN.billUp * cy + IN.billFwd * nz);
                // Tilt the canopy normal toward the surface-up (billUp), the way FoliageLit tilts leaf
                // normals up: a camera-facing normal misses a near-overhead (noon) sun, so the whole far
                // tree line and the rock/bush billboards collapsed to the shade floor and read grey/black.
                N = normalize(lerp(N, IN.billUp, 0.6));

                // Planet sun (matches FoliageLit): billUp is the surface normal at the instance.
                float3 planetNormal = normalize(IN.billUp);
                float3 sunDir = PlanetSunDirection(_SunParams, planetNormal);
                float localSun = dot(planetNormal, sunDir);
                float daylight = PlanetDaylightFromLocalSun(localSun);
                half ndl = saturate(dot(N, sunDir));
                // Receive the same cast + cloud shadow the mesh foliage does, so a distant billboard darkens
                // under a hillside or a drifting cloud instead of glowing while the world around it shades.
                // Floored like the leaf shade so the far tree line dims but never collapses to black.
                half shadowAtten = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));
                float cloudShadow = CloudShadowFactor(IN.positionWS, sunDir, localSun);
                half shade = lerp(0.5, 1.0, shadowAtten * cloudShadow);
                // Match the near-tree canopy floor so the far tree line reads at the same brightness as the
                // mesh trees it fades into (both are lush, not a dark band).
                half3 dayColor = card.rgb * lerp(0.6, 1.12, ndl * shade);
                half3 nightColor = card.rgb * PlanetNightAmbient(_NightAmbientIntensity) * 0.6;
                return half4(lerp(nightColor, dayColor, daylight), 1);
            }
            ENDHLSL
        }

        // Writes the impostor into the depth-normals prepass so it lands in _CameraDepthTexture /
        // _CameraNormalsTexture (clouds/atmosphere/SSAO read those). Mirrors the ForwardLit clip + dither
        // coverage so the depth footprint matches the visible card exactly.
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff;
                float _NormalBulge;
                float _FadeInStart;
                float _FadeInEnd;
                float _FadeOutStart;
                float _FadeOutEnd;
            CBUFFER_END

            // GPU-driven indirect draw: setup() feeds unity_ObjectToWorld (which GetObjectToWorldMatrix reads)
            // from the buffer under procedural instancing; the RenderMeshInstanced path is untouched.
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                StructuredBuffer<float4x4> _ScatterMatrices;
                StructuredBuffer<float4x4> _ScatterMatricesInv;
                StructuredBuffer<uint> _ScatterVisible; // this band's indices into the master matrix buffers
            #endif
            void setup()
            {
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                uint idx = _ScatterVisible[unity_InstanceID];
                unity_ObjectToWorld = _ScatterMatrices[idx];
                unity_WorldToObject = _ScatterMatricesInv[idx];
            #endif
            }

            static const float _Bayer4x4[16] = {
                0.0/16, 8.0/16, 2.0/16, 10.0/16,
                12.0/16, 4.0/16, 14.0/16, 6.0/16,
                3.0/16, 11.0/16, 1.0/16, 9.0/16,
                15.0/16, 7.0/16, 13.0/16, 5.0/16
            };

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
                float dist : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float3 billRight : TEXCOORD3;
                float3 billUp : TEXCOORD4;
                float3 billFwd : TEXCOORD5;
                float3 positionWS : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);

                float4x4 m = GetObjectToWorldMatrix();
                float3 origin = float3(m._m03, m._m13, m._m23);
                float3 up = normalize(float3(m._m01, m._m11, m._m21));
                float scale = length(float3(m._m00, m._m10, m._m20));

                float3 toCam = _WorldSpaceCameraPos - origin;
                float3 fwd = toCam - up * dot(toCam, up);
                fwd = normalize(fwd);
                float3 right = normalize(cross(up, fwd));

                float3 local = IN.positionOS.xyz * scale;
                float3 world = origin + right * local.x + up * local.y;

                OUT.positionHCS = TransformWorldToHClip(world);
                OUT.positionWS = world;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.dist = distance(_WorldSpaceCameraPos, origin);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                OUT.billRight = right;
                OUT.billUp = up;
                OUT.billFwd = fwd;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 card = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(card.a - _Cutoff);

                float fadeIn = saturate((IN.dist - _FadeInStart) / max(1e-3, _FadeInEnd - _FadeInStart));
                float fadeOut = 1.0 - saturate((IN.dist - _FadeOutStart) / max(1e-3, _FadeOutEnd - _FadeOutStart));
                float coverage = fadeIn * fadeOut;

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
