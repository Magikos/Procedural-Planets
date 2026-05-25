Shader "Planet/Ocean"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.28, 0.78, 0.82, 0.12)
        _DeepColor ("Deep Color", Color) = (0.0, 0.035, 0.095, 0.98)
        _FoamColor ("Foam Color", Color) = (0.92, 0.98, 1.0, 0.95)
        _ShallowDepth ("Shallow Depth", Range(1, 500)) = 28
        _DeepDepth ("Deep Depth", Range(20, 3000)) = 360
        _ShoreFoamDepth ("Shore Foam Width", Range(1, 200)) = 24
        _ShoreFoamSoftness ("Shore Range", Range(1, 300)) = 125
        _WaveAmplitude ("Wave Amplitude", Range(0, 12)) = 3.4
        _WaveScale ("Wave Scale", Range(50, 2000)) = 480
        _WaveSpeed ("Wave Speed", Range(0, 4)) = 0.58
        _WaveNormalStrength ("Wave Normal Strength", Range(0, 16)) = 4.5
        _WaterMotionStrength ("Water Motion Strength", Range(0, 1)) = 0.24
        _SunGlitterIntensity ("Sun Glitter Intensity", Range(0, 4)) = 0.75
        _SunGlitterPower ("Sun Glitter Power", Range(64, 4096)) = 1400
        _ShoreFoamIntensity ("Shore Foam Intensity", Range(0, 3)) = 1
        _WhitecapIntensity ("Whitecap Intensity", Range(0, 3)) = 1
        _WakeFoamIntensity ("Wake Foam Intensity", Range(0, 4)) = 1
        _WakeNormalStrength ("Wake Normal Strength", Range(0, 4)) = 1
        _OceanFocusMode ("Ocean Focus Mode", Range(0, 1)) = 1
        _Alpha ("Alpha", Range(0, 1)) = 0.9
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }

        // Layer 2: first visible surface pass.
        // WaterVolume owns underwater fog/refraction/caustics. This pass adds
        // only the top sheet color so the layer can be validated by itself.
        Pass
        {
            Name "OceanForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Includes/DebugModes.hlsl"
            #include "Includes/CloudShadows.hlsl"

            #define FORCE_WATER_LAYER_PROOF 0
            #define SHOW_SURFACE_IN_OFF 1

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 waterData : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float _ShallowDepth;
                float _DeepDepth;
                float _ShoreFoamDepth;
                float _ShoreFoamSoftness;
                float _WaveAmplitude;
                float _WaveScale;
                float _WaveSpeed;
                float _WaveNormalStrength;
                float _WaterMotionStrength;
                float _SunGlitterIntensity;
                float _SunGlitterPower;
                float _ShoreFoamIntensity;
                float _WhitecapIntensity;
                float _WakeFoamIntensity;
                float _WakeNormalStrength;
                float _OceanFocusMode;
                float _Alpha;
            CBUFFER_END

            float3 _PlanetCenter;
            float3 _SunParams;
            float _NightAmbientIntensity;
            int _OceanDebugMode;

            struct SurfaceLayer
            {
                float3 color;
                float alpha;
                float depthBlend;
                float shoreVisibility;
                float fresnel;
                float viewPath;
                float daylight;
                float shadow;
            };

            float3 SafeNormalize(float3 value, float3 fallback)
            {
                float lenSq = dot(value, value);
                return lenSq > 0.000001 ? value * rsqrt(lenSq) : fallback;
            }

            float SurfaceDepthBlend(float depth01)
            {
                float waterDepthMeters = depth01 * max(_DeepDepth, 0.001);
                float metricDepth = smoothstep(_ShallowDepth * 0.25, max(_DeepDepth * 0.85, _ShallowDepth + 1.0), waterDepthMeters);
                float encodedDepth = smoothstep(0.035, 0.42, depth01);
                return pow(saturate(max(metricDepth, encodedDepth)), 0.72);
            }

            bool IsVolumeOwnedMode()
            {
                return _OceanDebugMode == DEBUG_VOLUME_ONLY
                    || _OceanDebugMode == DEBUG_WATER_OFF
                    || (_OceanDebugMode >= DEBUG_VOLUME_DATA && _OceanDebugMode <= DEBUG_VOLUME_REFRACTION)
                    || (_OceanDebugMode >= DEBUG_VOLUME_BOUNDARY && _OceanDebugMode <= DEBUG_VOLUME_OPTICAL)
                    || (_OceanDebugMode >= DEBUG_VOLUME_CONTACT && _OceanDebugMode <= DEBUG_VOLUME_OCCLUSION)
                    || (_OceanDebugMode >= DEBUG_VOLUME_SPHERE && _OceanDebugMode <= DEBUG_SEA_SOURCE_MATTE)
                    || _OceanDebugMode == DEBUG_VOLUME_AFTER_ATMOSPHERE
                    || (_OceanDebugMode >= DEBUG_VOLUME_CONTRIBUTION && _OceanDebugMode <= DEBUG_PRECIPITATION_CONTRIBUTION)
                    || (_OceanDebugMode >= DEBUG_VOLUME_LIP_PINK && _OceanDebugMode <= DEBUG_VOLUME_LIP_DEPTH_GATE)
                    || _OceanDebugMode == DEBUG_VOLUME_LIP_SCENE_PINK
                    || (_OceanDebugMode >= DEBUG_CAUSTICS_ONLY && _OceanDebugMode <= DEBUG_CAUSTICS_PRISM);
            }

            SurfaceLayer ComputeSurfaceLayer(float3 positionWS, float3 normalWS, float depth01, float shore01, float body01)
            {
                SurfaceLayer layer;

                float depthBlend = SurfaceDepthBlend(depth01);
                float shoreVisibility = smoothstep(0.018, 0.18, shore01);
                float bodyVisibility = lerp(0.45, 1.0, body01);
                float3 viewDir = SafeNormalize(_WorldSpaceCameraPos.xyz - positionWS, normalWS);
                float viewFacing = saturate(abs(dot(viewDir, normalWS)));
                float fresnel = pow(1.0 - viewFacing, 3.2);
                float cameraDistance = distance(_WorldSpaceCameraPos.xyz, positionWS);
                float viewPathMeters = cameraDistance / max(viewFacing, 0.12);
                float viewPath = saturate(1.0 - exp(-viewPathMeters / max(_DeepDepth * 0.62, 1.0)));

                float3 sunDir = SafeNormalize(_SunParams, normalWS);
                float localSun = dot(normalWS, sunDir);
                float daylight = smoothstep(-0.08, 0.18, localSun);
                float shadow = CloudShadowFactor(positionWS, sunDir, localSun);

                float3 shallowColor = lerp(_ShallowColor.rgb, float3(0.14, 0.58, 0.72), 0.30);
                float3 deepColor = max(_DeepColor.rgb, float3(0.0, 0.055, 0.18));
                float3 waterColor = lerp(shallowColor, deepColor, depthBlend);

                float nightLight = saturate(_NightAmbientIntensity * 0.14 + 0.035);
                float dayLight = 0.46 + saturate(localSun) * 0.54;
                float lightAmount = lerp(nightLight, dayLight, daylight);
                lightAmount *= lerp(1.0, shadow, daylight * 0.45);

                float3 skyReflection = lerp(float3(0.010, 0.018, 0.030), float3(0.38, 0.58, 0.76), daylight);
                float3 litColor = waterColor * lightAmount;
                float reflectionBlend = fresnel * lerp(0.08, 0.38, daylight);
                float3 baseSurfaceColor = lerp(litColor, skyReflection, reflectionBlend);
                float3 farWaterColor = max(deepColor, float3(0.0, 0.060, 0.20));
                float3 farSurfaceColor = lerp(farWaterColor * lerp(0.48, 0.76, daylight), skyReflection * 0.10 + farWaterColor * 0.90, fresnel * 0.55);
                float surfacePathBlend = smoothstep(0.10, 0.76, viewPath) * lerp(0.62, 0.98, body01);
                layer.color = lerp(baseSurfaceColor, farSurfaceColor, surfacePathBlend);

                float depthAlpha = lerp(0.09, 0.34, depthBlend);
                float shoreAlpha = lerp(0.42, 1.0, shoreVisibility);
                float nearAlpha = _Alpha * depthAlpha * shoreAlpha * bodyVisibility;
                float farOpacityCeiling = lerp(0.72, 0.98, saturate(max(depthBlend, fresnel)));
                float farAlpha = farOpacityCeiling * lerp(0.74, 1.0, body01);
                float pathAlpha = saturate(smoothstep(0.08, 0.68, viewPath) * lerp(0.82, 1.0, fresnel));
                layer.alpha = saturate(lerp(nearAlpha, farAlpha, pathAlpha));
                layer.depthBlend = depthBlend;
                layer.shoreVisibility = shoreVisibility;
                layer.fresnel = fresnel;
                layer.viewPath = viewPath;
                layer.daylight = daylight;
                layer.shadow = shadow;
                return layer;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 objectNormalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 planetNormalWS = SafeNormalize(positionWS - _PlanetCenter, objectNormalWS);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = planetNormalWS;
                output.waterData = saturate(input.color.rgb);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (IsVolumeOwnedMode())
                    return half4(0.0, 0.0, 0.0, 0.0);

                #if FORCE_WATER_LAYER_PROOF
                    return half4(0.0, 1.0, 0.0, 1.0);
                #endif

                if (_OceanDebugMode == DEBUG_OFF && SHOW_SURFACE_IN_OFF == 0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float depth01 = saturate(input.waterData.r);
                float shore01 = saturate(input.waterData.g);
                float body01 = saturate(input.waterData.b);
                SurfaceLayer layer = ComputeSurfaceLayer(input.positionWS, input.normalWS, depth01, shore01, body01);

                if (_OceanDebugMode == DEBUG_WATER_DEPTH)
                    return half4(lerp(float3(0.55, 1.0, 0.92), float3(0.0, 0.025, 0.16), layer.depthBlend), 1.0);
                if (_OceanDebugMode == DEBUG_WATER_SHORE)
                    return half4(lerp(float3(0.02, 0.02, 0.025), float3(1.0, 0.92, 0.16), shore01), 1.0);
                if (_OceanDebugMode == DEBUG_WATER_BODY)
                    return half4(lerp(float3(0.86, 0.22, 0.70), float3(0.05, 0.85, 1.0), body01), 1.0);
                if (_OceanDebugMode == DEBUG_WATER_LIGHTING)
                    return half4(layer.daylight, layer.shadow, layer.fresnel, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_GLINT)
                    return half4(layer.fresnel.xxx, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_NORMALS)
                    return half4(input.normalWS * 0.5 + 0.5, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_FOAM || _OceanDebugMode == DEBUG_FOAM_PARTS)
                    return half4(0.0, 0.0, 0.0, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_MOTION_MASK)
                    return half4(0.0, 0.0, 0.0, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_WAVE_HEIGHT)
                    return half4(0.45, 0.56, 0.62, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_WAVE_SLOPE)
                    return half4(0.0, 0.0, 0.0, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_DATA)
                    return half4(depth01, shore01, body01, 1.0);
                if (_OceanDebugMode == DEBUG_WATER_ABSORPTION)
                    return half4(lerp(float3(0.03, 0.05, 0.07), float3(0.02, 0.32, 1.0), layer.depthBlend), 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_ALPHA)
                    return half4(layer.alpha, lerp(0.16, 0.54, layer.depthBlend), layer.fresnel, 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_CONTACT)
                    return half4(shore01, layer.shoreVisibility, layer.alpha, 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_BLEND)
                    return half4(layer.alpha, layer.depthBlend, layer.shoreVisibility, 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_RAW_OPAQUE)
                    return half4(layer.color, 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_FX_CONTRIB || _OceanDebugMode == DEBUG_SURFACE_FX_PROOF)
                    return half4(0.0, 0.0, 0.0, 1.0);
                if (_OceanDebugMode == DEBUG_SURFACE_ALPHA_PARTS)
                    return half4(layer.alpha, layer.viewPath, layer.fresnel, 1.0);
                if (_OceanDebugMode == DEBUG_FOAM_PINK)
                    return half4(1.0, 0.0, 1.0, 0.0);

                return half4(layer.color, layer.alpha);
            }
            ENDHLSL
        }
    }
}
