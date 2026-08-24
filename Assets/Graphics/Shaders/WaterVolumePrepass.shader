Shader "Hidden/WaterVolumePrepass"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Includes/DebugModes.hlsl"
        #include "Includes/WaterDisplacement.hlsl"
        #include "Includes/WaterLevelField.hlsl"
        #include "Includes/WaterVolumeData.hlsl"

        TEXTURE2D(_CameraDepthTexture);
        SAMPLER(sampler_CameraDepthTexture);

        // Global, so deliberately NOT in a per-material buffer - a material property of the same name would
        // shadow it and the shader would silently read a stale planet radius.
        float _SeaLevelRadius;
        float _ShoreFoamSoftness;
        float _DeepDepth;
        int _OceanDebugMode;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float4 waterData : TEXCOORD0;
            float forwardDepth : TEXCOORD1;
            float3 positionWS : TEXCOORD2;
        };

        float SceneDepthValid(float rawDepth)
        {
            #if UNITY_REVERSED_Z
                return step(0.0001, rawDepth);
            #else
                return 1.0 - step(0.9999, rawDepth);
            #endif
        }

        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float4 waterData = saturate(input.color);

            // Displace exactly as Ocean.shader does. forwardDepth is the distance the volume composite
            // measures its water column from, so it has to describe the surface the player actually sees;
            // sampling the undisplaced shell puts every column read out by up to the swell amplitude.
            float3 planetNormalWS = SafeNormalize(positionWS - _PlanetCenter, float3(0.0, 1.0, 0.0));
            float3 swellNormalWS;
            float swellHeight;
            positionWS = ComputeWaterVertexDisplacement(positionWS, planetNormalWS, waterData, swellNormalWS, swellHeight);

            float3 positionVS = TransformWorldToView(positionWS);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.waterData = waterData;
            output.forwardDepth = max(-positionVS.z, 0.0);
            output.positionWS = positionWS;
            return output;
        }

        // How deep the water is AT THIS PIXEL, measured against the depth buffer rather than read from the
        // mesh's baked vertex depth.
        //
        // The water mesh cannot resolve a shoreline finer than its own triangles, so a vertex-baked depth
        // quantises the whole shoreline feather to the mesh edges - which is why the feather used to snap to
        // triangle contours no matter how it was tuned. The depth buffer knows exactly where the bed is under
        // every pixel, so the feather now follows the real waterline and is independent of mesh resolution.
        //
        // Depth is taken at the BED's own direction, not by projecting the gap between the water pixel and
        // the bed onto the water pixel's up.
        //
        // That projection is only right while the bed sits more or less directly under the water pixel. At a
        // grazing angle the depth-buffer hit is hundreds of metres further along the ray, so it subtracts the
        // radius of one place from the water height of another, and past a certain view angle the result
        // flips sign and the column collapses to zero. The angle at which it flips is the same for every
        // pixel across the frame, which draws it as a hard horizontal line partway up the sea - the sharp
        // edge at the horizon.
        //
        // Asking the level field where the water surface is above the BED point removes the angle from the
        // problem entirely. Same form the caustics already use in WaterVolume.shader.
        // sceneValid also carries HOW FAR the bed is, because the depth buffer is only trustworthy for this
        // question while the bed is close to the water pixel.
        //
        // The buffer answers "what is behind this pixel", not "what is under it". Looking down at a shore
        // those coincide. Looking ALONG the water they do not: the first opaque hit behind a water pixel is
        // land on the far side, hundreds of metres away and above the water, so the column reads zero and
        // the volume decides there is no water. That happens at the same view angle right across the frame,
        // which draws it as a hard horizontal line partway up the sea - the sharp edge at the horizon.
        //
        // So near the shore the measurement wins, giving the fine mesh-independent waterline, and across
        // open water it hands back to the mesh's own baked depth, which is coarse but is right out there.
        float MeasuredWaterColumn(Varyings input, out float sceneValid)
        {
            float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS);
            float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, screenUv).r;

            float3 sceneWS = ComputeWorldSpacePosition(screenUv, rawDepth, UNITY_MATRIX_I_VP);
            float bedOffset = length(sceneWS - input.positionWS);
            sceneValid = SceneDepthValid(rawDepth) * (1.0 - smoothstep(40.0, 160.0, bedOffset));

            float3 fromCenter = sceneWS - _PlanetCenter;
            float bedRadius = max(length(fromCenter), 0.0001);
            float surfaceRadius = WaterSurfaceRadiusAt(fromCenter / bedRadius, _SeaLevelRadius);
            return max(surfaceRadius - bedRadius, 0.0);
        }


        float4 EncodeWaterVolumeData(Varyings input)
        {
            float sceneValid;
            float measured01 = saturate(MeasuredWaterColumn(input, sceneValid) / max(_DeepDepth, 0.001));

            // Open sky behind the water - the horizon - has no scene to measure against, and the reconstructed
            // position there is the far plane, which would read as immeasurably deep OR as zero depending on
            // the platform's depth convention. Keep the mesh's own baked depth for those pixels; it is coarse
            // but it is never nonsense.
            float depth01 = lerp(input.waterData.r, measured01, sceneValid);
            float shore01 = input.waterData.g;
            float body01 = input.waterData.b;
            float freezeFactor = EvaluateFreezeFactor(input.waterData.a, body01);
            uint kind = body01 >= 0.5 ? WATER_KIND_OCEAN : WATER_KIND_LAKE;
            return float4(input.forwardDepth, depth01, EncodeWaterShoreKind(shore01, kind), freezeFactor);
        }

        float4 Frag(Varyings input) : SV_Target
        {
            // Drop water that is showing the camera its UNDERSIDE.
            //
            // This pass draws Cull Off - it has to, because from below the surface the underside is the whole
            // view - and ZWrite Off with the depth attachment bound read-only, so its own triangles never
            // depth-test against each other. Whichever one rasterises LAST wins the pixel, and that is index
            // order, not distance. At grazing the near ocean and the ocean past the horizon cover the same
            // pixels, so in patches the forward depth recorded here is the FAR surface's. The atmosphere
            // substitutes that distance for scene depth in CompositeDepthScaled and hazes those patches by
            // the wrong amount - a hard-edged region of different haze lying on the sea, its boundary
            // following triangle edges.
            //
            // On a convex sphere no back-facing water can legitimately be seen from above the surface, so
            // the facing sign settles it, exactly as it does for the visible surface in Ocean.shader.
            // Underwater nothing is discarded, which is what keeps Cull Off doing its real job.
            float3 planetNormalWS = SafeNormalize(input.positionWS - _PlanetCenter, float3(0.0, 1.0, 0.0));
            float3 toCameraWS = SafeNormalize(_WorldSpaceCameraPos.xyz - input.positionWS, planetNormalWS);
            float cameraSeaOffset = length(_WorldSpaceCameraPos.xyz - _PlanetCenter) - _SeaLevelRadius;
            float cameraAboveWater = saturate((cameraSeaOffset - 0.5) / 2.0);
            clip(lerp(1.0, dot(toCameraWS, planetNormalWS), cameraAboveWater));

            return EncodeWaterVolumeData(input);
        }

        ENDHLSL

        Pass
        {
            Name "WaterVolumePrepass"
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.0
            ENDHLSL
        }

    }
}
