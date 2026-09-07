using UnityEngine;

// Per-planet Texture2DArrays bound globally for the terrain shader, indexed by
// GetDefinitionByIndex slot id. Normal and ARM contain one slice per biome. Albedo contains
// primary slices followed by a second bank of optional broad-scale material variants.
//
// Format/dimensions are taken from the first non-null SurfaceAlbedo found in the registry.
// All biome textures in the registry must share dimensions + format; mismatched biomes get
// a neutral placeholder slice + a console warning. Biomes with no surface textures get
// placeholder slices so the array is always BiomeCount slices long with no holes.
//
// Owned by ColorGenerator. Rebuilt every Configure(); previous arrays Destroyed on rebuild.
public sealed class BiomeSurfaceTextureArrays : System.IDisposable
{
    // Global shader IDs — terrain shader (and Phase B+ debug shaders) bind by these names.
    static readonly int AlbedoArrayId = Shader.PropertyToID(ShaderGlobalIds.BiomeAlbedoArray);
    static readonly int NormalArrayId = Shader.PropertyToID(ShaderGlobalIds.BiomeNormalArray);
    static readonly int ArmArrayId = Shader.PropertyToID(ShaderGlobalIds.BiomeArmArray);
    static readonly int BiomeCountId = Shader.PropertyToID(ShaderGlobalIds.BiomeCount);
    static readonly int GrassParamsId = Shader.PropertyToID(ShaderGlobalIds.BiomeGrassParams);
    static readonly int GrassParamCountId = Shader.PropertyToID(ShaderGlobalIds.BiomeGrassParamCount);
    // Step 5: 1×N flat color LUT. Sampled by primary biome slice id; gives crisp per-pixel
    // biome boundaries before step 6 wires up the texture-array triplanar sampler.
    static readonly int FlatColorsId = Shader.PropertyToID(ShaderGlobalIds.BiomeFlatColors);

    // Fallback slice size when no biome supplies any surface texture. Tiny so we don't
    // burn memory on the magenta path; the shader still gets a bindable array.
    const int FallbackSliceSize = 4;
    const int GrassParamsStride = sizeof(float) * 20;

    Texture2DArray _albedoArray;
    Texture2DArray _normalArray;
    Texture2DArray _armArray;
    Texture2D _flatColorLut;
    ComputeBuffer _grassParamsBuffer;

    public Texture2DArray AlbedoArray => _albedoArray;
    public Texture2DArray NormalArray => _normalArray;
    public Texture2DArray ArmArray => _armArray;
    public Texture2D FlatColorLut => _flatColorLut;
    public ComputeBuffer GrassParamsBuffer => _grassParamsBuffer;
    public int SliceCount { get; private set; }

    public void Build(BiomeRegistryDto registry)
    {
        Dispose();
        if (registry == null) return;

        SliceCount = Mathf.Max(registry.BiomeCount, 1);

        // Determine array config from the first biome that provides surface textures.
        Texture2D sampleAlbedo = FindReferenceTexture(registry, t => t.SurfaceAlbedo);
        Texture2D sampleNormal = FindReferenceTexture(registry, t => t.SurfaceNormal);
        Texture2D sampleArm = FindReferenceTexture(registry, t => t.SurfaceARM);

        _albedoArray = BuildArray(registry, sampleAlbedo, def => def?.SurfaceAlbedo,
            placeholderColor: new Color32(255, 0, 255, 255), // magenta — author missing
            defaultFormat: TextureFormat.RGBA32, isLinear: false, "BiomeAlbedoArray",
            secondarySelector: def => def?.SurfaceSecondaryAlbedo);

        _normalArray = BuildArray(registry, sampleNormal, def => def?.SurfaceNormal,
            // Flat tangent-space normal (0,0,1) packed: (128,128,255,255)
            placeholderColor: new Color32(128, 128, 255, 255),
            defaultFormat: TextureFormat.RGBA32, isLinear: true, "BiomeNormalArray");

        _armArray = BuildArray(registry, sampleArm, def => def?.SurfaceARM,
            // AO=1, Roughness=0.5, Metallic=0
            placeholderColor: new Color32(255, 128, 0, 255),
            defaultFormat: TextureFormat.RGBA32, isLinear: true, "BiomeArmArray");

        _flatColorLut = BuildFlatColorLut(registry);
        _grassParamsBuffer = BuildGrassParamsBuffer(registry);

        Shader.SetGlobalTexture(AlbedoArrayId, _albedoArray);
        Shader.SetGlobalTexture(NormalArrayId, _normalArray);
        Shader.SetGlobalTexture(ArmArrayId, _armArray);
        Shader.SetGlobalTexture(FlatColorsId, _flatColorLut);
        Shader.SetGlobalBuffer(GrassParamsId, _grassParamsBuffer);
        Shader.SetGlobalInt(BiomeCountId, SliceCount);
        Shader.SetGlobalInt(GrassParamCountId, SliceCount);
        BiomeAlbedoTintRuntime.Publish(registry, SliceCount);
        BiomeBlendRuntime.PublishDefaults();
    }

    public void Dispose()
    {
        ReleaseBuffer(ref _grassParamsBuffer);
        DestroyArray(ref _albedoArray);
        DestroyArray(ref _normalArray);
        DestroyArray(ref _armArray);
        DestroyTex(ref _flatColorLut);
        Shader.SetGlobalTexture(AlbedoArrayId, (Texture)null);
        Shader.SetGlobalTexture(NormalArrayId, (Texture)null);
        Shader.SetGlobalTexture(ArmArrayId, (Texture)null);
        Shader.SetGlobalTexture(FlatColorsId, (Texture)null);
        Shader.SetGlobalBuffer(GrassParamsId, (ComputeBuffer)null);
        Shader.SetGlobalInt(BiomeCountId, 0);
        Shader.SetGlobalInt(GrassParamCountId, 0);
        BiomeAlbedoTintRuntime.Clear();
        SliceCount = 0;
    }

    // Step 5 flat-color LUT: 1×N RGBA, one texel per biome slice id. Pixel value is the
    // BiomeDefinitionDto.TintColor blended with the gradient mid-sample, matching what
    // ColorGenerator's per-vertex path uses today. Sampled in the shader as the placeholder
    // for the texture-array path before Phase B step 6 (and as a fallback for biomes that
    // don't author surface textures even after step 6).
    Texture2D BuildFlatColorLut(BiomeRegistryDto registry)
    {
        var tex = new Texture2D(SliceCount, 1, TextureFormat.RGBA32, mipChain: false, linear: false)
        {
            name = "BiomeFlatColorLut",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        var pixels = new Color32[SliceCount];
        var sb = new System.Text.StringBuilder($"FlatColorLut ({SliceCount} slots): ");
        for (int slot = 0; slot < SliceCount; slot++)
        {
            BiomeDefinitionDto def = registry.GetDefinitionByIndex(slot);
            Color c = ResolveBiomeColor(def);
            pixels[slot] = c;
            string biomeName = def != null ? def.Type.ToString() : "?";
            sb.Append($"[{slot}={biomeName} #{ColorUtility.ToHtmlStringRGB(c)}] ");
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        LoggerProvider.Get().Log(LogLevel.Info, "BiomeSurfaceTextureArrays", sb.ToString());
        return tex;
    }

    static Color ResolveBiomeColor(BiomeDefinitionDto def)
    {
        if (def == null) return new Color(1f, 0f, 1f, 1f); // magenta — registry hole
        Color gradient = def.ColorGradient != null ? def.ColorGradient.Evaluate(0.5f) : Color.white;
        return gradient * (1f - def.TintPercent) + def.TintColor * def.TintPercent;
    }

    ComputeBuffer BuildGrassParamsBuffer(BiomeRegistryDto registry)
    {
        var data = new BiomeGrassParamsGpu[SliceCount];
        for (int slot = 0; slot < SliceCount; slot++)
        {
            var def = registry.GetDefinitionByIndex(slot);
            if (def == null) { data[slot] = default; continue; }
            var placement = GrassBiomePlacementConfig.From(def);
            var tint = GrassBiomeTintConfig.From(def);
            data[slot] = PackGrassParams(placement, tint);
        }

        var buffer = new ComputeBuffer(SliceCount, GrassParamsStride, ComputeBufferType.Structured);
        buffer.SetData(data);
        return buffer;
    }

    static BiomeGrassParamsGpu PackGrassParams(in GrassBiomePlacementConfig placement, in GrassBiomeTintConfig tint)
    {
        Color t = tint.TintBase;
        Color d = tint.TintDryShift;
        Color l = tint.TintLushShift;
        return new BiomeGrassParamsGpu
        {
            Shape = new Vector4(placement.Density, placement.Height, placement.Width, placement.ClumpStrength),
            Placement = new Vector4(placement.MaxSlopeDegrees, placement.SlopeFadeDegrees, placement.MinWaterClearance, placement.BiomeBlendPower),
            Tint = new Vector4(t.r, t.g, t.b, t.a),
            TintDry = new Vector4(d.r, d.g, d.b, 1f),
            TintLush = new Vector4(l.r, l.g, l.b, 1f),
        };
    }

    static void DestroyTex(ref Texture2D tex)
    {
        if (tex == null) return;
        if (Application.isPlaying) Object.Destroy(tex);
        else Object.DestroyImmediate(tex);
        tex = null;
    }

    static void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null) return;
        buffer.Release();
        buffer = null;
    }

    Texture2DArray BuildArray(BiomeRegistryDto registry, Texture2D sample,
        System.Func<BiomeDefinitionDto, Texture2D> selector,
        Color32 placeholderColor, TextureFormat defaultFormat, bool isLinear, string name,
        System.Func<BiomeDefinitionDto, Texture2D> secondarySelector = null)
    {
        int width = sample != null ? sample.width : FallbackSliceSize;
        int height = sample != null ? sample.height : FallbackSliceSize;
        TextureFormat format = sample != null ? sample.format : defaultFormat;
        int mipCount = sample != null && sample.mipmapCount > 1
            ? Mathf.FloorToInt(Mathf.Log(Mathf.Max(width, height), 2f)) + 1 : 1;
        int bankCount = secondarySelector != null ? 2 : 1;
        int arrayDepth = SliceCount * bankCount;

        // Compressed arrays cannot receive CPU-authored fallback colours. Expand only when needed.
        if (sample != null && UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(sample.graphicsFormat))
        {
            for (int bank = 0; bank < bankCount; bank++)
            for (int slot = 0; slot < SliceCount; slot++)
            {
                BiomeDefinitionDto def = registry.GetDefinitionByIndex(slot);
                Texture2D primary = selector(def);
                Texture2D src = bank == 0 ? primary : secondarySelector(def) ?? primary;
                if (src == null || src.width != width || src.height != height || src.format != sample.format || src.mipmapCount < mipCount)
                    format = TextureFormat.RGBA32;
            }
        }

        Texture2DArray array;
        try
        {
            array = new Texture2DArray(width, height, arrayDepth, format, mipCount > 1, isLinear)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
            };
        }
        catch (System.Exception ex)
        {
            // Some formats are unsupported as array members on certain platforms — fall
            // back to RGBA32 and warn.
            LoggerProvider.Get().Log(LogLevel.Warning, "BiomeSurfaceTextureArrays", $"{name} format {format} unsupported as array ({ex.Message}); falling back to RGBA32.");
            format = TextureFormat.RGBA32;
            mipCount = 1;
            array = new Texture2DArray(width, height, arrayDepth, format, false, isLinear)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
            };
        }

        // Release CPU storage before GPU copies; Apply afterwards can overwrite copied pixels.
        array.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        Texture2D placeholder = null;
        int matched = 0;
        int placeholderUsed = 0;
        try
        {
            for (int bank = 0; bank < bankCount; bank++)
            {
                for (int slot = 0; slot < SliceCount; slot++)
                {
                    BiomeDefinitionDto def = registry.GetDefinitionByIndex(slot);
                    Texture2D primary = selector(def);
                    Texture2D src = bank == 0 ? primary : secondarySelector(def) ?? primary;
                    int arraySlice = slot + bank * SliceCount;

                    if (src != null && src.width == width && src.height == height && src.mipmapCount >= mipCount
                        && (src.format == format || format == TextureFormat.RGBA32))
                    {
                        if (src.format == format)
                            for (int mip = 0; mip < mipCount; mip++)
                                Graphics.CopyTexture(src, 0, mip, array, arraySlice, mip);
                        else
                            CopyConvertedSlice(src, array, arraySlice);
                        matched++;
                    }
                    else
                    {
                        if (src != null)
                        {
                            LoggerProvider.Get().Log(LogLevel.Warning, "BiomeSurfaceTextureArrays", $"{name} bank {bank} slot {slot} ({def?.Type}) texture '{src.name}' is {src.width}x{src.height} {src.format} ({src.mipmapCount} mips), expected {width}x{height} {format} ({mipCount} mips). Using placeholder.");
                        }
                        if (placeholder == null)
                            placeholder = BuildPlaceholder(width, height, format, isLinear, placeholderColor, mipCount > 1);
                        for (int mip = 0; mip < mipCount; mip++)
                            Graphics.CopyTexture(placeholder, 0, mip, array, arraySlice, mip);
                        placeholderUsed++;
                    }
                }
            }
        }
        finally
        {
            DestroyTex(ref placeholder);
        }

        LoggerProvider.Get().Log(LogLevel.Info, "BiomeSurfaceTextureArrays", $"{name}: {width}x{height} {format}, {matched}/{arrayDepth} slices from source, {placeholderUsed} placeholder, {bankCount} bank(s). Reference texture: {(sample != null ? sample.name : "<none>")}.");

        return array;
    }

    static void CopyConvertedSlice(Texture2D source, Texture2DArray destination, int slice)
    {
        var descriptor = new RenderTextureDescriptor(destination.width, destination.height)
        {
            graphicsFormat = destination.graphicsFormat,
            depthBufferBits = 0,
            msaaSamples = 1,
            useMipMap = destination.mipmapCount > 1,
            autoGenerateMips = false,
        };
        RenderTexture temporary = RenderTexture.GetTemporary(descriptor);
        RenderTexture active = RenderTexture.active;
        bool srgbWrite = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = temporary.sRGB;
            Graphics.Blit(source, temporary);
            if (descriptor.useMipMap) temporary.GenerateMips();
            for (int mip = 0; mip < destination.mipmapCount; mip++)
                Graphics.CopyTexture(temporary, 0, mip, destination, slice, mip);
        }
        finally
        {
            GL.sRGBWrite = srgbWrite;
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    static Texture2D BuildPlaceholder(int width, int height, TextureFormat format, bool isLinear, Color32 color, bool mipChain)
    {
        var tex = new Texture2D(width, height, format, mipChain: mipChain, linear: isLinear)
        {
            name = "BiomeSurfacePlaceholder",
            wrapMode = TextureWrapMode.Repeat,
        };
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: mipChain, makeNoLongerReadable: false);
        return tex;
    }

    static Texture2D FindReferenceTexture(BiomeRegistryDto registry, System.Func<BiomeDefinitionDto, Texture2D> selector)
    {
        int count = registry.BiomeCount;
        for (int i = 0; i < count; i++)
        {
            var def = registry.GetDefinitionByIndex(i);
            if (def == null) continue;
            Texture2D t = selector(def);
            if (t != null) return t;
        }
        return null;
    }

    static void DestroyArray(ref Texture2DArray array)
    {
        if (array == null) return;
        if (Application.isPlaying) Object.Destroy(array);
        else Object.DestroyImmediate(array);
        array = null;
    }

    struct BiomeGrassParamsGpu
    {
        public Vector4 Shape;
        public Vector4 Placement;
        public Vector4 Tint;
        public Vector4 TintDry;
        public Vector4 TintLush;
    }
}
