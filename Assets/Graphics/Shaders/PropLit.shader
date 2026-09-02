Shader "Planet/PropLit"
{
    // Planet-aware lit shader for surface gameplay objects (the character capsule now; dropped items, NPCs,
    // debris later). It shades from the shared ANALYTIC planet sun (Includes/PlanetSunLighting.hlsl) exactly
    // like the terrain and scatter props — so an object wearing this material darkens on the planet's NIGHT
    // side (the planet body occludes the sun). A plain URP Lit material can't: a directional light has no
    // positional occluder, so it "bleeds" light onto the far/night hemisphere. The _SunParams / _PlanetCenter
    // / _NightAmbientIntensity globals are published scene-wide every frame, so ANY object wearing this
    // material is planet-lit with zero per-object C# wiring. Drop it on a surface object and day/night works.
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.8,0.8,0.82,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Smoothness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/PlanetSunLighting.hlsl"
            #include "Includes/CloudShadows.hlsl"

            // Lit from the planet _SunParams sun (like terrain/foliage), not the URP main light (which has no
            // positional occluder and would bleed onto the night side). Published scene-wide every frame.
            float3 _SunParams;
            float3 _PlanetCenter;
            float _NightAmbientIntensity;

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;
                half3 albedo = _BaseColor.rgb * tex;

                // Planet sun lighting: day/night GATE from the planet normal (position vs sun) — the term that
                // is 0 on the far/night side; FORM shading from the surface normal facing the sun.
                float3 nrmWS = normalize(IN.normalWS);
                float3 planetNormal = normalize(IN.positionWS - _PlanetCenter);
                float3 sunDir = PlanetSunDirection(_SunParams, planetNormal);
                float localSun = dot(planetNormal, sunDir);
                float daylight = PlanetDaylightFromLocalSun(localSun);
                float ndl = saturate(dot(nrmWS, sunDir));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                half shadowAtten = MainLightRealtimeShadow(shadowCoord);
                float cloudShadow = CloudShadowFactor(IN.positionWS, sunDir, localSun);
                float shade = lerp(0.5, 1.0, shadowAtten * cloudShadow);

                half3 dayColor = albedo * lerp(0.72, 1.15, ndl * shade);
                dayColor *= PlanetCastShadow(shadowAtten, daylight, 0.25);
                half3 nightColor = albedo * PlanetNightAmbient(_NightAmbientIntensity) * 0.6;
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

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct SAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct SVaryings { float4 positionHCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            SVaryings shadowVert(SAttributes IN)
            {
                SVaryings OUT = (SVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 hcs = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    hcs.z = min(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    hcs.z = max(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionHCS = hcs;
                return OUT;
            }

            half4 shadowFrag(SVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex dnVert
            #pragma fragment dnFrag
            #pragma multi_compile_instancing
            #pragma target 4.5

            struct DNAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DNVaryings { float4 positionHCS : SV_POSITION; float3 normalWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            DNVaryings dnVert(DNAttributes IN)
            {
                DNVaryings OUT = (DNVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = pos.positionCS;
                OUT.normalWS = nrm.normalWS;
                return OUT;
            }

            half4 dnFrag(DNVaryings IN) : SV_Target
            {
                return half4(normalize(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
