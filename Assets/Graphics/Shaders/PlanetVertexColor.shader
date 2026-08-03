Shader "Planet/VertexColor"
{
    Properties
    {
        _Smoothness ("Smoothness", Range(0,1)) = 0.0
        // Phase B step 6: world-space tiling factor for triplanar biome textures.
        // 0.055 = one tile per ~18 world units. Lower = larger tiles; higher = more
        // close-range detail but more visible repetition at altitude.
        _BiomeTriplanarTiling ("Biome Triplanar Tiling", Range(0.001, 1.0)) = 0.055
        _BiomeSecondaryTilingScale ("Biome Secondary Tiling Scale", Range(0.1, 4.0)) = 0.32
        _BiomeSecondaryBlend ("Biome Secondary Blend", Range(0.0, 0.5)) = 0.28
        _BiomeMacroVariationScale ("Biome Macro Variation Scale", Range(0.0001, 0.1)) = 0.012
        _BiomeMacroVariationStrength ("Biome Macro Variation Strength", Range(0.0, 0.6)) = 0.14
        // Phase B step 8: scales the tangent-space normal-map perturbation before swizzle.
        // 1.0 = unmodified ground PBR (subtle), 3-5 = punchy "obviously bumpy" look.
        _BiomeNormalStrength ("Biome Normal Strength", Range(0.0, 8.0)) = 2.0
        _BiomeNormalReliefShadow ("Biome Normal Relief Shadow", Range(0.0, 1.0)) = 0.55
        _TerrainOverrideEnabled ("Terrain Geography Overrides", Float) = 1.0
        [HideInInspector] _CoastSlice ("Coast Biome Slice", Float) = 1.0
        _CoastBelowSeaDepth ("Coast Below-Sea Depth", Float) = 8.0
        _CoastStartHeight ("Coast Start Height", Float) = 2.0
        _CoastEndHeight ("Coast End Height", Float) = 20.0
        _CoastTiling ("Coast Tiling", Range(0.001, 1.0)) = 0.08
        [HideInInspector] _SlopeSlice ("Slope Biome Slice", Float) = 0.0
        _SlopeStartDegrees ("Slope Start Degrees", Range(0.0, 90.0)) = 28.0
        _SlopeFullDegrees ("Slope Full Degrees", Range(0.0, 90.0)) = 48.0
        _SlopeTiling ("Slope Tiling", Range(0.001, 1.0)) = 0.075
        [HideInInspector] _SnowSlice ("Snow Biome Slice", Float) = 0.0
        _SnowFullTemperature ("Snow Full Temperature", Range(0.0, 1.0)) = 0.28
        _SnowFadeEndTemperature ("Snow Fade End Temperature", Range(0.0, 1.0)) = 0.42
        _SnowTiling ("Snow Tiling", Range(0.001, 1.0)) = 0.09
        _GrassFarOverlayStrength ("Grass Far Overlay Strength", Range(0.0, 1.0)) = 1.0
        _GrassFarOverlayStart ("Grass Far Overlay Start", Float) = 35.0
        _GrassFarOverlayEnd ("Grass Far Overlay End", Float) = 260.0
        _GrassFarOverlayNoiseScale ("Grass Far Overlay Noise Scale", Range(0.001, 0.5)) = 0.055
        _GrassFarOverlayOrbitStrength ("Grass Far Overlay Orbit Strength", Range(0.0, 1.0)) = 0.42
        _GrassFarOverlayAltitudeStart ("Grass Far Overlay Altitude Start", Float) = 750.0
        _GrassFarOverlayAltitudeEnd ("Grass Far Overlay Altitude End", Float) = 2600.0
        _GrassFarOverlayFiberStrength ("Grass Far Overlay Fiber Strength", Range(0.0, 1.0)) = 0.65
        _GrassSurfaceBrightness ("Grass Surface Brightness", Range(0.3, 1.5)) = 0.4
        [HideInInspector] _GrassWaterRadius ("Grass Water Radius", Float) = -1.0
        [HideInInspector] _SurfacePathDebug ("Surface Path Debug", Float) = 0.0
        [HideInInspector] _PathWearMask ("Path Wear Mask", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #pragma target 4.5
            // Phase B step 5: per-pixel biome sampling. Off = legacy vertex-color path
            // (the PerFaceSurfaceProvider stays on this so its meshes keep rendering).
            #pragma multi_compile _ _BIOME_COLOR_MODE_TEXTURE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/CloudShadows.hlsl"
            #include "Includes/DebugModes.hlsl"
            #include "Includes/GrassColor.hlsl"
            #include "Includes/PlanetSunLighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                // UV0: chunk-local UV in [0,1]² — Phase B step 5 biome map sample coord.
                // PerFace path leaves this unset (defaults to zero, which is fine since the
                // chunk biome map keyword is off for that path).
                float2 chunkUv : TEXCOORD0;
                // UV2: per-vertex biome diagnostics baked by ColorGenerator + TerrainFace.
                // x = temperature (0..1), y = moisture (0..1),
                // z = primaryBiomeId normalized to biome count,
                // w = normalized temperature removed by altitude lapse.
                float4 biomeData : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 color : COLOR;
                float fogFactor : TEXCOORD2;
                float4 biomeData : TEXCOORD3;
                float2 chunkUv : TEXCOORD4;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Smoothness;
                float _BiomeTriplanarTiling;
                float _BiomeSecondaryTilingScale;
                float _BiomeSecondaryBlend;
                float _BiomeMacroVariationScale;
                float _BiomeMacroVariationStrength;
                float _BiomeNormalStrength;
                float _BiomeNormalReliefShadow;
                float _TerrainOverrideEnabled;
                float _CoastSlice;
                float _CoastBelowSeaDepth;
                float _CoastStartHeight;
                float _CoastEndHeight;
                float _CoastTiling;
                float _SlopeSlice;
                float _SlopeStartDegrees;
                float _SlopeFullDegrees;
                float _SlopeTiling;
                float _SnowSlice;
                float _SnowFullTemperature;
                float _SnowFadeEndTemperature;
                float _SnowTiling;
                float _GrassFarOverlayStrength;
                float _GrassFarOverlayStart;
                float _GrassFarOverlayEnd;
                float _GrassFarOverlayNoiseScale;
                float _GrassFarOverlayOrbitStrength;
                float _GrassFarOverlayAltitudeStart;
                float _GrassFarOverlayAltitudeEnd;
                float _GrassFarOverlayFiberStrength;
                float _GrassSurfaceBrightness;
                float _GrassWaterRadius;
                float _SurfacePathDebug;
            CBUFFER_END

            float _NightAmbientIntensity;
            float3 _SunParams;
            float _GrassDebugLayerColors;
            float3 _PlanetCenter;
            float _SeaLevelRadius;
            int _OceanDebugMode;

            // Phase B step 5b: per-chunk top-K biome textures + global flat color LUT.
            //   _BiomeBlendedColor : pre-blended albedo per texel (bilinear) — cheap path.
            //   _BiomeIds          : top-4 biome slice ids per texel (point — categorical).
            //   _BiomeWeights      : top-4 weights per texel (bilinear, sums to 1.0).
            TEXTURE2D(_BiomeBlendedColor); SAMPLER(sampler_BiomeBlendedColor);
            TEXTURE2D(_BiomeIds);          SAMPLER(sampler_BiomeIds);
            TEXTURE2D(_BiomeWeights);      SAMPLER(sampler_BiomeWeights);
            float4 _BiomeMap_TexelSize;
            float4 _BiomeMapUvScale;
            TEXTURE2D(_BiomeFlatColors);   SAMPLER(sampler_BiomeFlatColors);
            int _BiomeCount;

            // Phase B step 6: per-planet Texture2DArrays (built by BiomeSurfaceTextureArrays
            // at planet init). One slice per biome slot id; the slot id is what _BiomeIds
            // texture's channels store. Bound globally via Shader.SetGlobalTexture.
            TEXTURE2D_ARRAY(_BiomeAlbedoArray); SAMPLER(sampler_BiomeAlbedoArray);
            TEXTURE2D_ARRAY(_BiomeNormalArray); SAMPLER(sampler_BiomeNormalArray);
            TEXTURE2D_ARRAY(_BiomeArmArray);    SAMPLER(sampler_BiomeArmArray);

            // Phase B step 9: per-chunk surface-state mask. Channels reserved for Phase E:
            //   R = paved alpha (concrete / brick / etc — disables grass + freezes other state)
            //   G = scorched alpha (burned / blackened)
            //   B = snow depth
            //   A = wetness
            // Bound but unsampled in Phase B. Phase E shader work will multiply / blend the
            // PBR signals (especially albedo + roughness) by these.
            TEXTURE2D(_SurfaceStateMask); SAMPLER(sampler_SurfaceStateMask);
            TEXTURE2D(_PathWearMask); SAMPLER(sampler_PathWearMask);

            struct BiomeGrassParams
            {
                float4 Shape;     // x density, y height, z width, w clump strength
                float4 Placement; // x maxSlopeDeg, y slopeFadeDeg, z waterClearance, w blendPower
                float4 Tint;
                float4 TintDry;
                float4 TintLush;
            };

            struct GrassOverlayParams
            {
                float density;
                float3 tint;
                float maxSlopeDeg;
                float slopeFadeDeg;
                float waterClearance;
            };

            struct GrassOverlayEval
            {
                float farWeight;
                float midWeight;
                float nearWeight;
                float envCoverage;
                float approachWeight;
                float density;
                float slopeKeep;
                float waterKeep;
                float3 tint;
                float3 relPos;
                float3 planetNormal;
            };

            StructuredBuffer<BiomeGrassParams> _BiomeGrassParams;
            int _BiomeGrassParamCount;

            // _BiomeTriplanarTiling lives in UnityPerMaterial above so SRP batcher works.

            float3 CubeFaceDebugColor(float3 directionWS)
            {
                float3 direction = normalize(directionWS);
                float3 axis = abs(direction);

                if (axis.x >= axis.y && axis.x >= axis.z)
                    return direction.x >= 0.0 ? float3(1.0, 0.15, 0.08) : float3(0.52, 0.10, 1.0);

                if (axis.y >= axis.z)
                    return direction.y >= 0.0 ? float3(0.10, 0.95, 0.18) : float3(1.0, 0.82, 0.08);

                return direction.z >= 0.0 ? float3(0.08, 0.45, 1.0) : float3(1.0, 0.18, 0.78);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                output.biomeData = input.biomeData;
                output.chunkUv = input.chunkUv;
                return output;
            }

            float3 SampleBiomeLutColor(float biomeId)
            {
                int count = max(_BiomeCount, 1);
                float safeId = clamp(round(biomeId), 0.0, (float)(count - 1));
                // Sample LUT at the center of the slot's texel. Point filtering on the LUT
                // gives the exact biome color with no neighbor bleeding.
                float lutU = (safeId + 0.5) / (float)count;
                return SAMPLE_TEXTURE2D(_BiomeFlatColors, sampler_BiomeFlatColors, float2(lutU, 0.5)).rgb;
            }

            // chunkUv → biome-map UV. Identity until step 6+ reintroduces face-atlas mapping.
            float2 BiomeMapUv(float2 chunkUv)
            {
                return saturate(chunkUv * _BiomeMapUvScale.xy + _BiomeMapUvScale.zw);
            }

            float4 SampleSurfaceState(float2 chunkUv)
            {
                return SAMPLE_TEXTURE2D(_SurfaceStateMask, sampler_SurfaceStateMask, saturate(chunkUv));
            }

            float SamplePathWear(float2 chunkUv)
            {
                return SAMPLE_TEXTURE2D(_PathWearMask, sampler_PathWearMask, saturate(chunkUv)).r;
            }

            // Phase B step 5b: cheap-path surface albedo. Bakes already weight-summed the
            // top-K biome colors per texel into _BiomeBlendedColor, so a single bilinear
            // sample gives a smooth multi-biome blend across boundaries — no stairsteps.
            float3 SampleBiomeFlatColor(float2 chunkUv)
            {
                return SAMPLE_TEXTURE2D(_BiomeBlendedColor, sampler_BiomeBlendedColor, BiomeMapUv(chunkUv)).rgb;
            }

            // Step 5b diagnostic: dominant biome id (channel R of the ids texture, point-filtered).
            float DominantBiomeId(float2 chunkUv)
            {
                float2 uv = BiomeMapUv(chunkUv);
                return round(SAMPLE_TEXTURE2D(_BiomeIds, sampler_BiomeIds, uv).r * 255.0);
            }

            // Phase B step 6+8: triplanar weights and per-axis UVs shared across albedo,
            // normal, and ARM samplers. Computed once per fragment in the helper that calls
            // these — passed in to avoid redundant pow/divide work.
            void ComputeTriplanarBlendWeights(float3 worldNormal, out float3 bw)
            {
                float3 absN = abs(worldNormal);
                bw = pow(absN, 4.0);
                bw /= max(bw.x + bw.y + bw.z, 1e-5);
            }

            // Small deterministic noise helpers used only for albedo variation.
            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash31(i + float3(0.0, 0.0, 0.0));
                float n100 = Hash31(i + float3(1.0, 0.0, 0.0));
                float n010 = Hash31(i + float3(0.0, 1.0, 0.0));
                float n110 = Hash31(i + float3(1.0, 1.0, 0.0));
                float n001 = Hash31(i + float3(0.0, 0.0, 1.0));
                float n101 = Hash31(i + float3(1.0, 0.0, 1.0));
                float n011 = Hash31(i + float3(0.0, 1.0, 1.0));
                float n111 = Hash31(i + float3(1.0, 1.0, 1.0));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);
                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            float3 BiomeSliceOffset(float slice)
            {
                return float3(
                    Hash11(slice + 11.0),
                    Hash11(slice + 37.0),
                    Hash11(slice + 73.0)) * 97.0;
            }

            float3 TriplanarSampleAlbedoAtTiling(float3 worldPos, float3 bw, float slice, float tiling)
            {
                float2 uvX = worldPos.yz * tiling;
                float2 uvY = worldPos.xz * tiling;
                float2 uvZ = worldPos.xy * tiling;

                float3 cX = SAMPLE_TEXTURE2D_ARRAY(_BiomeAlbedoArray, sampler_BiomeAlbedoArray, uvX, slice).rgb;
                float3 cY = SAMPLE_TEXTURE2D_ARRAY(_BiomeAlbedoArray, sampler_BiomeAlbedoArray, uvY, slice).rgb;
                float3 cZ = SAMPLE_TEXTURE2D_ARRAY(_BiomeAlbedoArray, sampler_BiomeAlbedoArray, uvZ, slice).rgb;

                return cX * bw.x + cY * bw.y + cZ * bw.z;
            }

            // Albedo bank zero stores the primary biome material and bank one stores an
            // optional authored secondary material. This replaces the old same-texture
            // resample without adding fragment texture taps. Normal and ARM stay
            // primary-only until profiling proves a second complete PBR sample is viable.
            float3 TriplanarSampleAlbedo(float3 worldPos, float3 bw, float slice)
            {
                float3 baseColor = TriplanarSampleAlbedoAtTiling(worldPos, bw, slice, _BiomeTriplanarTiling);
                if (_OceanDebugMode == DEBUG_TERRAIN_PRIMARY_ALBEDO)
                    return baseColor;

                float secondaryBlend = saturate(_BiomeSecondaryBlend);
                float variationStrength = saturate(_BiomeMacroVariationStrength);
                if (secondaryBlend <= 0.0001 && variationStrength <= 0.0001)
                    return baseColor;

                float3 offset = BiomeSliceOffset(slice);
                float macroScale = max(_BiomeMacroVariationScale, 0.00001);
                float macro = ValueNoise3D((worldPos + offset) * macroScale);
                float3 varied = baseColor;

                if (secondaryBlend > 0.0001)
                {
                    float secondaryTiling = _BiomeTriplanarTiling * max(_BiomeSecondaryTilingScale, 0.001);
                    float secondarySlice = slice + max((float)_BiomeCount, 1.0);
                    float3 secondary = TriplanarSampleAlbedoAtTiling(
                        worldPos + offset, bw, secondarySlice, secondaryTiling);
                    float secondaryMask = lerp(0.35, 1.0, smoothstep(0.12, 0.88, macro));
                    varied = lerp(varied, secondary, secondaryBlend * secondaryMask);
                }

                float brightness = lerp(1.0 - variationStrength, 1.0 + variationStrength, macro);
                return saturate(varied * brightness);
            }

            // Phase B step 8: triplanar tangent-space normal sample -> world-space normal.
            // RNM blending keeps a flat normal map identity-preserving: flat texture in
            // returns the geometric world normal, not a cube-axis blend.
            // Scale the tangent-space XY perturbation and re-normalize. The naive
            // "scale xy, reconstruct z = sqrt(1 - dot(xy,xy))" formula collapses z to 0
            // whenever the scaled XY magnitude reaches 1 — flipping the normal entirely
            // sideways and killing the diffuse dot product against the sun. Normalizing the
            // whole post-scale vector preserves a valid unit normal at any strength.
            float3 ScaleTangentNormal(float3 tn, float strength)
            {
                tn.xy *= strength;
                return normalize(tn);
            }

            float3 BlendRnm(float3 baseNormal, float3 detailNormal)
            {
                baseNormal.z += 1.0;
                detailNormal.xy = -detailNormal.xy;
                return baseNormal * dot(baseNormal, detailNormal) / max(baseNormal.z, 1e-5) - detailNormal;
            }

            float3 TriplanarSampleNormalAtTiling(
                float3 worldPos,
                float3 worldNormal,
                float3 bw,
                float slice,
                float tiling)
            {
                float2 uvX = worldPos.yz * tiling;
                float2 uvY = worldPos.xz * tiling;
                float2 uvZ = worldPos.xy * tiling;

                float3 tnX = ScaleTangentNormal(UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(_BiomeNormalArray, sampler_BiomeNormalArray, uvX, slice)), _BiomeNormalStrength);
                float3 tnY = ScaleTangentNormal(UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(_BiomeNormalArray, sampler_BiomeNormalArray, uvY, slice)), _BiomeNormalStrength);
                float3 tnZ = ScaleTangentNormal(UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(_BiomeNormalArray, sampler_BiomeNormalArray, uvZ, slice)), _BiomeNormalStrength);

                float3 absNormal = abs(worldNormal);
                float3 rnX = BlendRnm(float3(worldNormal.zy, absNormal.x), tnX);
                float3 rnY = BlendRnm(float3(worldNormal.xz, absNormal.y), tnY);
                float3 rnZ = BlendRnm(float3(worldNormal.xy, absNormal.z), tnZ);

                float3 axisSign = sign(worldNormal);
                rnX.z *= axisSign.x;
                rnY.z *= axisSign.y;
                rnZ.z *= axisSign.z;

                return normalize(rnX.zyx * bw.x + rnY.xzy * bw.y + rnZ.xyz * bw.z);
            }

            // Phase B step 8: triplanar ARM sample (AO/Roughness/Metallic). Linear blend across
            // axes — these are scalar material properties, not directional, so no swizzling.
            float3 TriplanarSampleNormal(float3 worldPos, float3 worldNormal, float3 bw, float slice)
            {
                return TriplanarSampleNormalAtTiling(
                    worldPos, worldNormal, bw, slice, _BiomeTriplanarTiling);
            }

            // ARM channels are scalar material properties, so linear triplanar blending is valid.
            float3 TriplanarSampleArmAtTiling(float3 worldPos, float3 bw, float slice, float tiling)
            {
                float2 uvX = worldPos.yz * tiling;
                float2 uvY = worldPos.xz * tiling;
                float2 uvZ = worldPos.xy * tiling;

                float3 cX = SAMPLE_TEXTURE2D_ARRAY(_BiomeArmArray, sampler_BiomeArmArray, uvX, slice).rgb;
                float3 cY = SAMPLE_TEXTURE2D_ARRAY(_BiomeArmArray, sampler_BiomeArmArray, uvY, slice).rgb;
                float3 cZ = SAMPLE_TEXTURE2D_ARRAY(_BiomeArmArray, sampler_BiomeArmArray, uvZ, slice).rgb;

                return cX * bw.x + cY * bw.y + cZ * bw.z;
            }

            float3 TriplanarSampleArm(float3 worldPos, float3 bw, float slice)
            {
                return TriplanarSampleArmAtTiling(worldPos, bw, slice, _BiomeTriplanarTiling);
            }

            // Phase B step 6: weighted top-K triplanar albedo with MANUAL 4-corner bilinear.
            //
            // Problem with naive sampling (point ids + bilinear weights at fragment uv): the
            // dominant biome's TEXTURE swaps abruptly when chunkUv crosses a texel boundary
            // because ids are point-sampled (categorical, no interpolation). That produces
            // visible stair-step edges at every texel boundary regardless of how smoothly the
            // weights themselves transition.
            //
            // Correct approach: point-sample (ids, weights) at all 4 corner texels around the
            // fragment, compute each corner's full top-K weighted triplanar sum, then combine
            // the 4 corner results with standard bilinear corner weights ((1-fx)(1-fy) etc.).
            // The texture sampling itself is interpolated → no stair-steps. Cost: up to 4
            // corners × 4 slots × 3 axes = 48 array taps worst-case; in interior single-biome
            // regions where only slot.x has non-trivial weight per corner, ~12 taps typical.
            // Phase B step 6+8: full PBR sample for one corner texel. Reads (ids, weights)
            // once and reuses the values to compute the weighted top-K triplanar contribution
            // for albedo, normal, and ARM. Three out-params avoid having to re-sample at the
            // 4-corner-blend level.
            void CornerTriplanarWeightedPbr(int2 texel, float3 worldPos, float3 worldNormal, float3 bw,
                out float3 albedo, out float3 normalWS, out float3 arm)
            {
                float2 uv = (texel + 0.5) * _BiomeMap_TexelSize.xy;
                float4 idsF = SAMPLE_TEXTURE2D(_BiomeIds, sampler_BiomeIds, uv) * 255.0;
                float4 w = SAMPLE_TEXTURE2D(_BiomeWeights, sampler_BiomeWeights, uv);

                albedo  = float3(0, 0, 0);
                normalWS = float3(0, 0, 0);
                arm     = float3(0, 0, 0);
                const float wEps = 0.004;

                if (w.x > wEps)
                {
                    float s = round(idsF.x);
                    albedo   += w.x * TriplanarSampleAlbedo(worldPos, bw, s);
                    normalWS += w.x * TriplanarSampleNormal(worldPos, worldNormal, bw, s);
                    arm      += w.x * TriplanarSampleArm(worldPos, bw, s);
                }
                if (w.y > wEps)
                {
                    float s = round(idsF.y);
                    albedo   += w.y * TriplanarSampleAlbedo(worldPos, bw, s);
                    normalWS += w.y * TriplanarSampleNormal(worldPos, worldNormal, bw, s);
                    arm      += w.y * TriplanarSampleArm(worldPos, bw, s);
                }
                if (w.z > wEps)
                {
                    float s = round(idsF.z);
                    albedo   += w.z * TriplanarSampleAlbedo(worldPos, bw, s);
                    normalWS += w.z * TriplanarSampleNormal(worldPos, worldNormal, bw, s);
                    arm      += w.z * TriplanarSampleArm(worldPos, bw, s);
                }
                if (w.w > wEps)
                {
                    float s = round(idsF.w);
                    albedo   += w.w * TriplanarSampleAlbedo(worldPos, bw, s);
                    normalWS += w.w * TriplanarSampleNormal(worldPos, worldNormal, bw, s);
                    arm      += w.w * TriplanarSampleArm(worldPos, bw, s);
                }
            }

            void SampleBiomeTriplanarPbr(float2 chunkUv, float3 worldPos, float3 worldNormal,
                out float3 albedo, out float3 normalWS, out float3 arm)
            {
                float2 uv = BiomeMapUv(chunkUv);
                float2 res = max(_BiomeMap_TexelSize.zw, float2(1.0, 1.0));
                float2 maxTexel = max(res - float2(1.0, 1.0), float2(0.0, 0.0));

                float2 texelCoord = uv * res - 0.5;
                int2 base_ = (int2)floor(texelCoord);
                float2 f = saturate(texelCoord - base_);

                int2 t00 = clamp(base_,               int2(0,0), int2(maxTexel));
                int2 t10 = clamp(base_ + int2(1, 0),  int2(0,0), int2(maxTexel));
                int2 t01 = clamp(base_ + int2(0, 1),  int2(0,0), int2(maxTexel));
                int2 t11 = clamp(base_ + int2(1, 1),  int2(0,0), int2(maxTexel));

                float3 bw;
                ComputeTriplanarBlendWeights(worldNormal, bw);

                float3 a00, a10, a01, a11;
                float3 n00, n10, n01, n11;
                float3 m00, m10, m01, m11;
                CornerTriplanarWeightedPbr(t00, worldPos, worldNormal, bw, a00, n00, m00);
                CornerTriplanarWeightedPbr(t10, worldPos, worldNormal, bw, a10, n10, m10);
                CornerTriplanarWeightedPbr(t01, worldPos, worldNormal, bw, a01, n01, m01);
                CornerTriplanarWeightedPbr(t11, worldPos, worldNormal, bw, a11, n11, m11);

                float3 ax0 = lerp(a00, a10, f.x);
                float3 ax1 = lerp(a01, a11, f.x);
                albedo = lerp(ax0, ax1, f.y);

                float3 nx0 = lerp(n00, n10, f.x);
                float3 nx1 = lerp(n01, n11, f.x);
                normalWS = lerp(nx0, nx1, f.y);

                float3 mx0 = lerp(m00, m10, f.x);
                float3 mx1 = lerp(m01, m11, f.x);
                arm = lerp(mx0, mx1, f.y);
            }

            // Albedo-only entry kept for the DEBUG_BIOME_MAP_FLAT_COLOR path and any callers
            // that only need albedo. Lighter than SampleBiomeTriplanarPbr.
            float3 SampleBiomeTriplanarAlbedo(float2 chunkUv, float3 worldPos, float3 worldNormal)
            {
                float3 a, n, m;
                SampleBiomeTriplanarPbr(chunkUv, worldPos, worldNormal, a, n, m);
                return a;
            }

            BiomeGrassParams GetGrassParams(uint id)
            {
                BiomeGrassParams p;
                p.Shape = 0.0;
                p.Placement = 0.0;
                p.Tint = 0.0;
                if (id < (uint)_BiomeGrassParamCount)
                    p = _BiomeGrassParams[id];
                return p;
            }

            GrassOverlayParams EmptyGrassOverlayParams()
            {
                GrassOverlayParams p;
                p.density = 0.0;
                p.tint = 0.0;
                p.maxSlopeDeg = 0.0;
                p.slopeFadeDeg = 0.0;
                p.waterClearance = 0.0;
                return p;
            }

            void AccumulateGrassOverlaySlot(float idPacked, float weight, inout GrassOverlayParams overlay, inout float totalParamWeight)
            {
                weight = saturate(weight);
                if (weight <= 0.0001)
                    return;

                BiomeGrassParams p = GetGrassParams((uint)round(idPacked * 255.0));
                float grassDensity = saturate(p.Shape.x);
                float blendPower = max(p.Placement.w, 0.001);
                overlay.density += grassDensity * pow(weight, blendPower);

                float paramWeight = weight * grassDensity;
                if (paramWeight <= 0.0001)
                    return;

                overlay.tint += p.Tint.rgb * paramWeight;
                overlay.maxSlopeDeg += p.Placement.x * paramWeight;
                overlay.slopeFadeDeg += p.Placement.y * paramWeight;
                overlay.waterClearance += p.Placement.z * paramWeight;
                totalParamWeight += paramWeight;
            }

            GrassOverlayParams NormalizeGrassOverlayParams(GrassOverlayParams overlay, float totalParamWeight)
            {
                overlay.density = saturate(overlay.density);
                if (totalParamWeight > 0.0001)
                {
                    float inv = rcp(totalParamWeight);
                    overlay.tint *= inv;
                    overlay.maxSlopeDeg *= inv;
                    overlay.slopeFadeDeg *= inv;
                    overlay.waterClearance *= inv;
                }
                return overlay;
            }

            GrassOverlayParams CornerGrassOverlayParams(int2 texel)
            {
                float4 ids = LOAD_TEXTURE2D(_BiomeIds, texel);
                float4 weights = LOAD_TEXTURE2D(_BiomeWeights, texel);

                GrassOverlayParams overlay = EmptyGrassOverlayParams();
                float totalParamWeight = 0.0;
                AccumulateGrassOverlaySlot(ids.x, weights.x, overlay, totalParamWeight);
                AccumulateGrassOverlaySlot(ids.y, weights.y, overlay, totalParamWeight);
                AccumulateGrassOverlaySlot(ids.z, weights.z, overlay, totalParamWeight);
                AccumulateGrassOverlaySlot(ids.w, weights.w, overlay, totalParamWeight);
                return NormalizeGrassOverlayParams(overlay, totalParamWeight);
            }

            GrassOverlayParams LerpGrassOverlayParams(GrassOverlayParams a, GrassOverlayParams b, float t)
            {
                GrassOverlayParams o;
                o.density = lerp(a.density, b.density, t);
                o.tint = lerp(a.tint, b.tint, t);
                o.maxSlopeDeg = lerp(a.maxSlopeDeg, b.maxSlopeDeg, t);
                o.slopeFadeDeg = lerp(a.slopeFadeDeg, b.slopeFadeDeg, t);
                o.waterClearance = lerp(a.waterClearance, b.waterClearance, t);
                return o;
            }

            GrassOverlayParams SampleGrassOverlayParams(float2 chunkUv)
            {
                float2 uv = BiomeMapUv(chunkUv);
                float2 res = max(_BiomeMap_TexelSize.zw, float2(1.0, 1.0));
                float2 maxTexel = max(res - float2(1.0, 1.0), float2(0.0, 0.0));

                float2 texelCoord = uv * res - 0.5;
                int2 base_ = (int2)floor(texelCoord);
                float2 f = saturate(texelCoord - base_);

                int2 t00 = clamp(base_,               int2(0,0), int2(maxTexel));
                int2 t10 = clamp(base_ + int2(1, 0),  int2(0,0), int2(maxTexel));
                int2 t01 = clamp(base_ + int2(0, 1),  int2(0,0), int2(maxTexel));
                int2 t11 = clamp(base_ + int2(1, 1),  int2(0,0), int2(maxTexel));

                GrassOverlayParams p00 = CornerGrassOverlayParams(t00);
                GrassOverlayParams p10 = CornerGrassOverlayParams(t10);
                GrassOverlayParams p01 = CornerGrassOverlayParams(t01);
                GrassOverlayParams p11 = CornerGrassOverlayParams(t11);

                GrassOverlayParams px0 = LerpGrassOverlayParams(p00, p10, f.x);
                GrassOverlayParams px1 = LerpGrassOverlayParams(p01, p11, f.x);
                return LerpGrassOverlayParams(px0, px1, f.y);
            }

            GrassOverlayEval EmptyGrassOverlayEval()
            {
                GrassOverlayEval e;
                e.farWeight = 0.0;
                e.midWeight = 0.0;
                e.nearWeight = 0.0;
                e.envCoverage = 0.0;
                e.approachWeight = 0.0;
                e.density = 0.0;
                e.slopeKeep = 0.0;
                e.waterKeep = 0.0;
                e.tint = 0.0;
                e.relPos = 0.0;
                e.planetNormal = 0.0;
                return e;
            }

            GrassOverlayEval EvaluateGrassOverlay(float2 chunkUv, float3 positionWS, float3 geometricNormalWS)
            {
                GrassOverlayEval eval = EmptyGrassOverlayEval();
                if (_GrassFarOverlayStrength <= 0.0001 || _BiomeGrassParamCount <= 0)
                    return eval;

                GrassOverlayParams grass = SampleGrassOverlayParams(chunkUv);
                eval.density = saturate(grass.density);
                if (grass.density <= 0.001)
                    return eval;

                float3 relPos = positionWS - _PlanetCenter;
                float3 planetNormal = PlanetSafeNormalize(relPos, normalize(geometricNormalWS));
                float3 slopeNormal = normalize(geometricNormalWS);
                if (dot(slopeNormal, planetNormal) < 0.0)
                    slopeNormal = -slopeNormal;
                float slopeDeg = acos(saturate(dot(slopeNormal, planetNormal))) * 57.2957795;
                float slopeFade = max(grass.slopeFadeDeg, 0.001);
                float slopeKeep = 1.0 - smoothstep(grass.maxSlopeDeg - slopeFade, grass.maxSlopeDeg + slopeFade, slopeDeg);
                eval.slopeKeep = saturate(slopeKeep);

                float waterKeep = 1.0;
                if (_GrassWaterRadius > 0.0)
                {
                    float altitude = length(relPos) - _GrassWaterRadius;
                    waterKeep = smoothstep(max(grass.waterClearance, 0.0), max(grass.waterClearance, 0.0) + 4.0, altitude);
                }
                eval.waterKeep = saturate(waterKeep);

                float viewDistance = length(positionWS - _WorldSpaceCameraPos);
                float farMask = smoothstep(_GrassFarOverlayStart, max(_GrassFarOverlayEnd, _GrassFarOverlayStart + 1.0), viewDistance);
                float cameraAltitude = max(0.0, length(_WorldSpaceCameraPos - _PlanetCenter) - _SeaLevelRadius);
                float nearSurface = 1.0 - smoothstep(_GrassFarOverlayAltitudeStart,
                    max(_GrassFarOverlayAltitudeEnd, _GrassFarOverlayAltitudeStart + 1.0), cameraAltitude);
                float approachWeight = lerp(saturate(_GrassFarOverlayOrbitStrength), 1.0, nearSurface);

                // Linear coverage with a toe cut, NOT a pow-lift: coverage must track biome grass
                // density directly. The old pow(.,0.62) is concave, so it lifts the low-density
                // fringe at biome borders faster than the biome colour blends - that mismatch is
                // the historical stripe. The toe drops the near-zero-density fringe entirely.
                float rawCoverage = saturate(grass.density * slopeKeep * waterKeep);
                float coverageToe = 0.12;
                float envCoverage = saturate((rawCoverage - coverageToe) / (1.0 - coverageToe));
                float nearWeight = 1.0 - smoothstep(144.0, 200.0, viewDistance);
                float midWeight = smoothstep(144.0, 200.0, viewDistance)
                    * (1.0 - smoothstep(200.0, 600.0, viewDistance));

                eval.farWeight = saturate(envCoverage * farMask * approachWeight * _GrassFarOverlayStrength);
                eval.midWeight = saturate(envCoverage * midWeight);
                eval.nearWeight = saturate(envCoverage * nearWeight);
                eval.envCoverage = envCoverage;
                eval.approachWeight = approachWeight;
                eval.tint = saturate(grass.tint);
                eval.relPos = relPos;
                eval.planetNormal = planetNormal;
                return eval;
            }

            float3 ApplyGrassSurfaceAlbedo(
                float2 chunkUv,
                float3 positionWS,
                float3 geometricNormalWS,
                float3 terrainAlbedo)
            {
                float4 surfaceState = SampleSurfaceState(chunkUv);
                float pathWear = SamplePathWear(chunkUv);
                if (_SurfacePathDebug > 1.5)
                    return pathWear.xxx;

                float paved = saturate(max(surfaceState.r, pathWear));
                float scorched = saturate(surfaceState.g);
                float scorchVisual = smoothstep(0.08, 0.72, scorched);
                float pathMask = saturate(max(paved, scorchVisual));
                if (_SurfacePathDebug > 0.5 && pathMask > 0.001)
                    return lerp(terrainAlbedo, float3(1.0, 0.0, 0.85), pathMask);
                terrainAlbedo = lerp(terrainAlbedo, float3(0.32, 0.26, 0.18), paved);
                float3 scorchColor = lerp(float3(0.035, 0.027, 0.020), float3(0.002, 0.002, 0.002), smoothstep(0.45, 1.0, scorched));
                terrainAlbedo = lerp(terrainAlbedo, scorchColor, scorchVisual);

                GrassOverlayEval eval = EvaluateGrassOverlay(chunkUv, positionWS, geometricNormalWS);
                // Keep close ground free to show through blade gaps, then hand off to a full
                // grass-surface material where physical blades thin out near their draw limit.
                // Wide, gentle coverage ramp: a narrow ramp reads as a bright ring at biome borders
                // (the soft biome blend maps a thin farWeight band to a visible spatial stripe).
                float grassCoverage = smoothstep(0.0, 0.9, eval.farWeight);
                grassCoverage *= 1.0 - pathMask;
                if (grassCoverage <= 0.001)
                    return terrainAlbedo;
                if (_GrassDebugLayerColors > 0.5)
                    return lerp(terrainAlbedo, float3(0.70, 0.0, 0.0), grassCoverage);

                float3 axis = abs(eval.planetNormal.y) < 0.92 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                float3 tangentA = normalize(cross(axis, eval.planetNormal));
                float3 tangentB = normalize(cross(eval.planetNormal, tangentA));
                float2 fiberUv = float2(dot(eval.relPos, tangentA), dot(eval.relPos, tangentB));
                float noiseScale = max(_GrassFarOverlayNoiseScale, 0.0001);
                float macro = ValueNoise3D(eval.relPos * noiseScale + eval.tint * 37.0);
                float detail = ValueNoise3D(eval.relPos * (noiseScale * 3.0) + 19.0);
                float fiber = ValueNoise3D(float3(
                    fiberUv.x * noiseScale * 0.42,
                    fiberUv.y * noiseScale * 2.35,
                    11.0 + eval.tint.g * 23.0));
                float breakup = lerp(macro * 0.65 + detail * 0.35, fiber, saturate(_GrassFarOverlayFiberStrength));
                float patch = ValueNoise3D(eval.relPos * (noiseScale * 0.22) + eval.tint * 71.0 + 5.0);
                float2 fleckUv = float2(fiberUv.x * 0.9, fiberUv.y * 3.0);
                float fleckFilter = saturate(1.0 - max(fwidth(fleckUv.x), fwidth(fleckUv.y)) * 1.5);
                float fleck = smoothstep(0.52, 0.88,
                    ValueNoise3D(float3(fleckUv.x, fleckUv.y, 23.0 + eval.tint.r * 19.0)));
                fleck = lerp(0.5, fleck, fleckFilter);
                grassCoverage = saturate(grassCoverage
                    * lerp(0.86, 1.08, patch)
                    * lerp(0.58, 1.22, fleck));

                // Match the authored blade color pipeline so geometry and surface LOD share
                // one material identity. Variation comes from grass fibers, never dirt albedo.
                // Saturation trimmed from 0.82 so the overlay reads as grass on every ground colour
                // (incl. tan savanna, which must not look barren at distance) without the vivid green
                // popping as a bright line where a grassy biome meets an arid one.
                float3 grassSurface = GradeGrassTint(eval.tint, 0.72, 0.98);
                float surfaceVariation = lerp(0.82, 1.04, breakup)
                    * lerp(0.98, 1.06, fiber)
                    * lerp(0.84, 1.16, patch)
                    * lerp(0.68, 1.30, fleck);
                grassSurface *= max(0.05, surfaceVariation);
                grassSurface *= _GrassSurfaceBrightness;

                return lerp(terrainAlbedo, saturate(grassSurface), grassCoverage);
            }

            struct TerrainOverrideMasks
            {
                float coast;
                float slope;
                float snow;
            };

            TerrainOverrideMasks EvaluateTerrainOverrideMasks(
                float3 positionWS,
                float3 geometricNormalWS,
                float temperature01)
            {
                TerrainOverrideMasks masks;
                float enabled = saturate(_TerrainOverrideEnabled);
                float3 relPos = positionWS - _PlanetCenter;
                float3 radialNormal = PlanetSafeNormalize(relPos, normalize(geometricNormalWS));
                float heightAboveSea = length(relPos) - _SeaLevelRadius;

                float belowSeaDepth = max(_CoastBelowSeaDepth, 0.01);
                float coastBelow = smoothstep(-belowSeaDepth, 0.0, heightAboveSea);
                float coastAbove = 1.0 - smoothstep(
                    max(_CoastStartHeight, 0.0),
                    max(_CoastEndHeight, _CoastStartHeight + 0.01),
                    heightAboveSea);
                masks.coast = saturate(coastBelow * coastAbove) * enabled;

                float slopeDegrees = acos(saturate(dot(normalize(geometricNormalWS), radialNormal)))
                    * 57.2957795;
                float slopeStart = clamp(_SlopeStartDegrees, 0.0, 89.99);
                float slopeEnd = clamp(
                    max(_SlopeFullDegrees, slopeStart + 0.01),
                    slopeStart + 0.01,
                    90.0);
                masks.slope = smoothstep(slopeStart, slopeEnd, slopeDegrees) * enabled;

                masks.snow = (1.0 - smoothstep(
                    saturate(_SnowFullTemperature),
                    max(saturate(_SnowFadeEndTemperature), saturate(_SnowFullTemperature) + 0.001),
                    saturate(temperature01))) * enabled;
                return masks;
            }

            void ApplyTerrainOverrideLayer(
                float mask,
                float slice,
                float tiling,
                float3 localPosition,
                float3 geometricNormalWS,
                float3 triplanarWeights,
                inout float3 albedo,
                inout float3 normalWS,
                inout float3 arm)
            {
                if (mask <= 0.001)
                    return;

                float safeSlice = clamp(round(slice), 0.0, max((float)_BiomeCount - 1.0, 0.0));
                float safeTiling = max(tiling, 0.001);
                float3 layerAlbedo = TriplanarSampleAlbedoAtTiling(
                    localPosition, triplanarWeights, safeSlice, safeTiling);
                float3 layerNormal = TriplanarSampleNormalAtTiling(
                    localPosition, geometricNormalWS, triplanarWeights, safeSlice, safeTiling);
                float3 layerArm = TriplanarSampleArmAtTiling(
                    localPosition, triplanarWeights, safeSlice, safeTiling);

                albedo = lerp(albedo, layerAlbedo, mask);
                normalWS = normalize(lerp(normalWS, layerNormal, mask));
                arm = lerp(arm, layerArm, mask);
            }

            void ApplyTerrainOverrides(
                TerrainOverrideMasks masks,
                float3 positionWS,
                float3 geometricNormalWS,
                inout float3 albedo,
                inout float3 normalWS,
                inout float3 arm)
            {
                if (_TerrainOverrideEnabled <= 0.001)
                    return;

                float3 localPosition = positionWS - _PlanetCenter;
                float3 triplanarWeights;
                ComputeTriplanarBlendWeights(geometricNormalWS, triplanarWeights);

                ApplyTerrainOverrideLayer(masks.coast, _CoastSlice, _CoastTiling,
                    localPosition, geometricNormalWS, triplanarWeights, albedo, normalWS, arm);
                ApplyTerrainOverrideLayer(masks.slope, _SlopeSlice, _SlopeTiling,
                    localPosition, geometricNormalWS, triplanarWeights, albedo, normalWS, arm);
                ApplyTerrainOverrideLayer(masks.snow, _SnowSlice, _SnowTiling,
                    localPosition, geometricNormalWS, triplanarWeights, albedo, normalWS, arm);
            }

            // Maps a normalized [0..1] biome id to a distinct hue. HSV->RGB so adjacent
            // ids land far apart in colour space and each biome reads as its own flat patch.
            float3 BiomeIdColor(float idNorm)
            {
                float h = frac(idNorm * 7.0 + 0.13) * 6.0;
                float3 rgb = saturate(float3(
                    abs(h - 3.0) - 1.0,
                    2.0 - abs(h - 2.0),
                    2.0 - abs(h - 4.0)));
                return rgb;
            }

            float3 HeatmapBlueRed(float t)
            {
                t = saturate(t);
                return float3(t, 1.0 - abs(t * 2.0 - 1.0), 1.0 - t);
            }

            float3 HeatmapYellowBlue(float t)
            {
                t = saturate(t);
                return lerp(float3(0.95, 0.78, 0.10), float3(0.10, 0.32, 0.95), t);
            }

            float3 ElevationBandColor(float elevationOverSea, float planetRadius)
            {
                // 7 bands keyed to fractions of planet radius — quick read of altitude profile.
                float rel = elevationOverSea / max(planetRadius, 1.0);
                if (rel < -0.020) return float3(0.04, 0.12, 0.40); // deep ocean
                if (rel < -0.005) return float3(0.18, 0.45, 0.75); // shallow ocean
                if (rel <  0.001) return float3(0.92, 0.84, 0.55); // beach / sea level
                if (rel <  0.010) return float3(0.45, 0.70, 0.32); // lowland
                if (rel <  0.030) return float3(0.30, 0.55, 0.22); // upland
                if (rel <  0.060) return float3(0.55, 0.48, 0.32); // hill
                if (rel <  0.100) return float3(0.70, 0.65, 0.55); // mountain
                return float3(1.00, 1.00, 1.00);                   // peak
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_OceanDebugMode == DEBUG_TERRAIN_SOURCE_PINK)
                    return half4(1.0, 0.0, 1.0, 1.0);

                if (_OceanDebugMode == DEBUG_TERRAIN_FACE_ID)
                    return half4(CubeFaceDebugColor(input.positionWS - _PlanetCenter), 1.0);

                if (_OceanDebugMode == DEBUG_BIOME_PRIMARY_ID)
                    return half4(BiomeIdColor(input.biomeData.z), 1.0);

                if (_OceanDebugMode == DEBUG_BIOME_TEMPERATURE)
                    return half4(HeatmapBlueRed(input.biomeData.x), 1.0);

                if (_OceanDebugMode == DEBUG_BIOME_MOISTURE)
                    return half4(HeatmapYellowBlue(input.biomeData.y), 1.0);

                if (_OceanDebugMode == DEBUG_BIOME_LATITUDE)
                {
                    float3 radial = normalize(input.positionWS - _PlanetCenter);
                    float lat = abs(asin(clamp(radial.y, -1.0, 1.0))) / 1.57079632679;
                    return half4(lat, 1.0 - lat, lat * 0.5, 1.0);
                }

                if (_OceanDebugMode == DEBUG_BIOME_ALTITUDE_COOLING)
                    return half4(HeatmapBlueRed(saturate(input.biomeData.w)), 1.0);

                if (_OceanDebugMode == DEBUG_BIOME_ELEVATION_BAND)
                {
                    float elevationOverSea = length(input.positionWS - _PlanetCenter) - _SeaLevelRadius;
                    return half4(ElevationBandColor(elevationOverSea, _SeaLevelRadius), 1.0);
                }

                if (_OceanDebugMode == DEBUG_BIOME_MAP_PRIMARY_ID)
                {
                    // Step 5b: dominant biome id from the top-K ids texture (R = highest-weight slot).
                    float idNorm = DominantBiomeId(input.chunkUv) / max((float)_BiomeCount, 1.0);
                    return half4(BiomeIdColor(idNorm), 1.0);
                }

                if (_OceanDebugMode == DEBUG_BIOME_MAP_BLEND)
                {
                    // Step 5b: visualize the dominant weight (channel R of _BiomeWeights). 1.0 = pure
                    // single-biome texel; lower values = multi-biome blends. Bilinear-sampled so the
                    // visualization itself is smooth across texels.
                    float w0 = SAMPLE_TEXTURE2D(_BiomeWeights, sampler_BiomeWeights, BiomeMapUv(input.chunkUv)).r;
                    return half4(w0, w0, w0, 1.0);
                }

                if (_OceanDebugMode == DEBUG_BIOME_MAP_FLAT_COLOR)
                    return half4(SampleBiomeFlatColor(input.chunkUv), 1.0);

                if (_OceanDebugMode == DEBUG_GRASS_LOD_COVERAGE)
                {
                    GrassOverlayEval grassEval = EvaluateGrassOverlay(input.chunkUv,
                        input.positionWS, normalize(input.normalWS));
                    // Diagnostic contribution weights follow the active handoff ranges.
                    return half4(grassEval.farWeight, grassEval.midWeight, grassEval.nearWeight, 1.0);
                }

                float3 geometricNormalWS = normalize(input.normalWS);
                TerrainOverrideMasks terrainOverrides = EvaluateTerrainOverrideMasks(
                    input.positionWS, geometricNormalWS, input.biomeData.x);

                if (_OceanDebugMode == DEBUG_TERRAIN_COAST_MASK)
                    return half4(float3(terrainOverrides.coast, terrainOverrides.coast, terrainOverrides.coast), 1.0);

                if (_OceanDebugMode == DEBUG_TERRAIN_SLOPE_MASK)
                    return half4(float3(terrainOverrides.slope, terrainOverrides.slope, terrainOverrides.slope), 1.0);

                if (_OceanDebugMode == DEBUG_TERRAIN_SNOW_MASK)
                    return half4(float3(terrainOverrides.snow, terrainOverrides.snow, terrainOverrides.snow), 1.0);

                if (_OceanDebugMode == DEBUG_TERRAIN_OVERRIDE_COMPOSITE)
                    return half4(
                        terrainOverrides.coast,
                        terrainOverrides.slope,
                        terrainOverrides.snow,
                        1.0);

                // Phase B step 6+8: keyword-on path samples per-biome triplanar PBR (albedo,
                // normal, ARM). Keyword-off keeps the legacy vertex-color path with neutral
                // surface props for back-compat with the PerFaceSurfaceProvider.
                float3 surfaceAlbedo;
                float3 surfaceNormalWS;
                float3 surfaceArm; // R=AO, G=Roughness, B=Metallic (B = 0 for all our biomes)
                #ifdef _BIOME_COLOR_MODE_TEXTURE
                    SampleBiomeTriplanarPbr(input.chunkUv,
                        input.positionWS - _PlanetCenter, geometricNormalWS,
                        surfaceAlbedo, surfaceNormalWS, surfaceArm);
                    // Triplanar normal is in world space, not yet normalized after the weighted
                    // blend. Normalize and gently mix with the geometric normal so detail bumps
                    // perturb lighting without ever flipping past the surface tangent plane.
                    surfaceNormalWS = normalize(surfaceNormalWS);
                #else
                    surfaceAlbedo = input.color.rgb;
                    surfaceNormalWS = geometricNormalWS;
                    surfaceArm = float3(1.0, 1.0, 0.0); // neutral: AO=1, roughness=1, metallic=0
                #endif

                if (_OceanDebugMode == DEBUG_TERRAIN_PRIMARY_ALBEDO
                    || _OceanDebugMode == DEBUG_TERRAIN_MIXED_ALBEDO)
                {
                    return half4(surfaceAlbedo, 1.0);
                }

                ApplyTerrainOverrides(terrainOverrides, input.positionWS, geometricNormalWS,
                    surfaceAlbedo, surfaceNormalWS, surfaceArm);
                // Grass geometry and the continuous grass surface share the same biome,
                // slope, and water gates. Apply the grass surface after geography overrides
                // so valid coverage remains grass at every geometry LOD.
                #ifdef _BIOME_COLOR_MODE_TEXTURE
                    surfaceAlbedo = ApplyGrassSurfaceAlbedo(input.chunkUv,
                        input.positionWS, geometricNormalWS, surfaceAlbedo);
                #endif

                if (_OceanDebugMode == DEBUG_TERRAIN_SELECTED_ALBEDO)
                    return half4(surfaceAlbedo, 1.0);

                if (_OceanDebugMode == DEBUG_TERRAIN_SUN_LIGHTING)
                {
                    // Uses the triplanar-perturbed normal so the visualization matches the
                    // real terrain shading path.
                    float3 planetNormal = PlanetSafeNormalize(input.positionWS - _PlanetCenter, normalize(input.normalWS));
                    float3 sunDir = PlanetSunDirection(_SunParams, planetNormal);
                    float localSun = dot(planetNormal, sunDir);
                    float daylight = PlanetDaylightFromLocalSun(localSun);
                    float cloudShadow = CloudShadowFactor(input.positionWS, sunDir, localSun);
                    float terrainDiffuse = saturate(dot(surfaceNormalWS, sunDir));
                    return half4(daylight, cloudShadow, terrainDiffuse, 1.0);
                }

                // Phase B step 8: visualize the triplanar PBR signals so it's obvious whether
                // normal/AO/roughness sampling is actually contributing.
                if (_OceanDebugMode == DEBUG_TERRAIN_SURFACE_NORMAL)
                {
                    // Show the perturbation of the textured normal AWAY from the geometric
                    // normal, amplified ×20. Pure geometric → mid-gray (0.5,0.5,0.5).
                    // Visible texture bumps → bright color speckle. Easier to read than
                    // displaying surfaceNormalWS directly, which is dominated by the planet's
                    // curvature and reads as nearly-uniform light green for any flat-ish area.
                    float3 geomN = normalize(input.normalWS);
                    float3 delta = (surfaceNormalWS - geomN) * 20.0;
                    return half4(delta * 0.5 + 0.5, 1.0);
                }
                if (_OceanDebugMode == DEBUG_TERRAIN_SURFACE_AO)
                {
                    // Triplanar AO (ARM.r). White = fully lit; dark = occluded crevices.
                    float ao = surfaceArm.r;
                    return half4(ao, ao, ao, 1.0);
                }
                if (_OceanDebugMode == DEBUG_TERRAIN_SURFACE_ROUGHNESS)
                {
                    // Triplanar roughness (ARM.g). White = matte; dark = glossy.
                    float r = surfaceArm.g;
                    return half4(r, r, r, 1.0);
                }

                float3 planetNormal = PlanetSafeNormalize(input.positionWS - _PlanetCenter, normalize(input.normalWS));
                float3 sunDir = PlanetSunDirection(_SunParams, planetNormal);
                float localSun = dot(planetNormal, sunDir);
                float daylight = PlanetDaylightFromLocalSun(localSun);
                float nightSide = 1.0 - daylight;

                // Phase B step 8: lighting uses the triplanar-perturbed surface normal so
                // detail bumps respond to the sun. AO from ARM.r darkens crevices in shadow;
                // Roughness from ARM.g drives a Blinn specular term (sharp on ice/rock,
                // smeared on sand/grass). Metallic from ARM.b stays at 0 for natural surfaces
                // — we route that through anyway for future metal biomes.
                // The custom analytic sun owns the shading (URP's PBR isn't stable at this
                // planet scale); we sample only the cascade shadowmap for cast shadows below.
                float geometricDiffuse = saturate(dot(normalize(input.normalWS), sunDir));
                float terrainDiffuse = saturate(dot(surfaceNormalWS, sunDir));
                float cloudShadow = CloudShadowFactor(input.positionWS, sunDir, localSun);
                // Receive the sun's realtime cast shadows (trees, foliage, terrain relief) so props are
                // rooted in the world instead of floating on the surface. The custom analytic sun still
                // owns the shading; this only gates the DIRECT term, so shadowed ground falls to the same
                // ambient floor grass/foliage use (never crushed to black), fading out at night.
                float mainShadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(input.positionWS));
                float sunShadow = lerp(1.0, mainShadow, daylight);
                float litDiffuse = terrainDiffuse * sunShadow;
                float ao = surfaceArm.r;
                float roughness = surfaceArm.g;
                float smoothness = 1.0 - roughness;
                float metallic = surfaceArm.b;

                float normalTurnsAwayFromSun = saturate((geometricDiffuse - terrainDiffuse) * 3.0);
                float reliefShadow = 1.0 - normalTurnsAwayFromSun * saturate(_BiomeNormalReliefShadow) * daylight;
                float dayLight = lerp(0.24, 1.12, litDiffuse) * lerp(0.36, 1.0, ao) * reliefShadow;
                dayLight *= lerp(0.45, 1.0, sunShadow);
                float3 dayColor = surfaceAlbedo * dayLight * lerp(1.0, cloudShadow, daylight);

                // Blinn specular: F0 = 0.04 dielectric / albedo metallic. Exponent scales with
                // smoothness². Strength fades with cloud shadow + daylight + smoothness so wet
                // / glossy surfaces shine and matte / cloudy areas don't.
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 halfDir = normalize(sunDir + viewDirWS);
                float NoH = saturate(dot(surfaceNormalWS, halfDir));
                float specExp = lerp(2.0, 256.0, smoothness * smoothness);
                float specMagnitude = pow(NoH, specExp) * smoothness;
                float3 specF0 = lerp(float3(0.04, 0.04, 0.04), surfaceAlbedo, metallic);
                float3 specular = specF0 * specMagnitude * daylight * cloudShadow * mainShadow;
                dayColor += specular;

                float3 coolNightAlbedo = lerp(surfaceAlbedo, float3(0.12, 0.16, 0.22), 0.65);
                float nightAmbient = PlanetNightAmbient(_NightAmbientIntensity);
                float3 nightColor = coolNightAlbedo * nightAmbient * 0.65 * lerp(0.55, 1.0, ao);

                half4 color = half4(lerp(dayColor, nightColor, nightSide), 1.0);
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
}
