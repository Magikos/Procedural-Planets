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
    static readonly int AlbedoArrayId = Shader.PropertyToID("_BiomeAlbedoArray");
    static readonly int NormalArrayId = Shader.PropertyToID("_BiomeNormalArray");
    static readonly int ArmArrayId = Shader.PropertyToID("_BiomeArmArray");
    static readonly int BiomeCountId = Shader.PropertyToID("_BiomeCount");
    static readonly int GrassParamsId = Shader.PropertyToID("_BiomeGrassParams");
    static readonly int GrassParamCountId = Shader.PropertyToID("_BiomeGrassParamCount");
    // Step 5: 1×N flat color LUT. Sampled by primary biome slice id; gives crisp per-pixel
    // biome boundaries before step 6 wires up the texture-array triplanar sampler.
    static readonly int FlatColorsId = Shader.PropertyToID("_BiomeFlatColors");

    // Fallback slice size when no biome supplies any surface texture. Tiny so we don't
    // burn memory on the magenta path; the shader still gets a bindable array.
    const int FallbackSliceSize = 4;
    const int GrassParamsStride = sizeof(float) * 12;

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

    public void Build(BiomeRegistry registry)
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
        SliceCount = 0;
    }

    // Step 5 flat-color LUT: 1×N RGBA, one texel per biome slice id. Pixel value is the
    // BiomeDefinition.TintColor blended with the gradient mid-sample, matching what
    // ColorGenerator's per-vertex path uses today. Sampled in the shader as the placeholder
    // for the texture-array path before Phase B step 6 (and as a fallback for biomes that
    // don't author surface textures even after step 6).
    Texture2D BuildFlatColorLut(BiomeRegistry registry)
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
            BiomeDefinition def = registry.GetDefinitionByIndex(slot);
            Color c = ResolveBiomeColor(def);
            pixels[slot] = c;
            string biomeName = def != null ? def.Type.ToString() : "?";
            sb.Append($"[{slot}={biomeName} #{ColorUtility.ToHtmlStringRGB(c)}] ");
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        Debug.Log($"[PhaseB] {sb}");
        return tex;
    }

    static Color ResolveBiomeColor(BiomeDefinition def)
    {
        if (def == null) return new Color(1f, 0f, 1f, 1f); // magenta — registry hole
        Color gradient = def.ColorGradient != null ? def.ColorGradient.Evaluate(0.5f) : Color.white;
        return gradient * (1f - def.TintPercent) + def.TintColor * def.TintPercent;
    }

    ComputeBuffer BuildGrassParamsBuffer(BiomeRegistry registry)
    {
        var data = new BiomeGrassParamsGpu[SliceCount];
        for (int slot = 0; slot < SliceCount; slot++)
        {
            data[slot] = ResolveGrassParams(registry.GetDefinitionByIndex(slot));
        }

        var buffer = new ComputeBuffer(SliceCount, GrassParamsStride, ComputeBufferType.Structured);
        buffer.SetData(data);
        return buffer;
    }

    static BiomeGrassParamsGpu ResolveGrassParams(BiomeDefinition def)
    {
        if (def == null) return default;

        Color tint = def.GrassTintBase;
        return new BiomeGrassParamsGpu
        {
            Shape = new Vector4(
                Mathf.Clamp01(def.GrassDensity),
                Mathf.Max(0f, def.GrassHeight),
                Mathf.Max(0f, def.GrassWidth),
                Mathf.Clamp01(def.GrassClumpStrength)),
            Placement = new Vector4(
                Mathf.Clamp(def.GrassMaxSlopeDegrees, 0f, 90f),
                Mathf.Max(0f, def.GrassSlopeFadeDegrees),
                Mathf.Max(0f, def.GrassMinWaterClearance),
                Mathf.Max(0.001f, def.GrassBiomeBlendPower)),
            Tint = new Vector4(tint.r, tint.g, tint.b, tint.a),
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

    Texture2DArray BuildArray(BiomeRegistry registry, Texture2D sample,
        System.Func<BiomeDefinition, Texture2D> selector,
        Color32 placeholderColor, TextureFormat defaultFormat, bool isLinear, string name,
        System.Func<BiomeDefinition, Texture2D> secondarySelector = null)
    {
        int width = sample != null ? sample.width : FallbackSliceSize;
        int height = sample != null ? sample.height : FallbackSliceSize;
        TextureFormat format = sample != null ? sample.format : defaultFormat;
        int mipCount = sample != null ? sample.mipmapCount : 1;
        int bankCount = secondarySelector != null ? 2 : 1;
        int arrayDepth = SliceCount * bankCount;

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
            Debug.LogWarning($"[BiomeSurfaceTextureArrays] {name} format {format} unsupported as array ({ex.Message}); falling back to RGBA32.");
            format = TextureFormat.RGBA32;
            mipCount = 1;
            array = new Texture2DArray(width, height, arrayDepth, format, false, isLinear)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
            };
        }

        Texture2D placeholder = BuildPlaceholder(width, height, format, isLinear, placeholderColor);
        int matched = 0;
        int placeholderUsed = 0;
        try
        {
            for (int bank = 0; bank < bankCount; bank++)
            {
                for (int slot = 0; slot < SliceCount; slot++)
                {
                    BiomeDefinition def = registry.GetDefinitionByIndex(slot);
                    Texture2D primary = selector(def);
                    Texture2D src = bank == 0 ? primary : secondarySelector(def) ?? primary;
                    int arraySlice = slot + bank * SliceCount;

                    if (src != null && src.width == width && src.height == height && src.format == format)
                    {
                        for (int mip = 0; mip < mipCount; mip++)
                            Graphics.CopyTexture(src, 0, mip, array, arraySlice, mip);
                        matched++;
                    }
                    else
                    {
                        if (src != null)
                        {
                            Debug.LogWarning($"[BiomeSurfaceTextureArrays] {name} bank {bank} slot {slot} ({def?.Type}) texture '{src.name}' is {src.width}x{src.height} {src.format}, expected {width}x{height} {format}. Using placeholder.");
                        }
                        Graphics.CopyTexture(placeholder, 0, 0, array, arraySlice, 0);
                        placeholderUsed++;
                    }
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(placeholder);
        }

        Debug.Log($"[BiomeSurfaceTextureArrays] {name}: {width}x{height} {format}, {matched}/{arrayDepth} slices from source, {placeholderUsed} placeholder, {bankCount} bank(s). Reference texture: {(sample != null ? sample.name : "<none>")}.");

        array.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return array;
    }

    static Texture2D BuildPlaceholder(int width, int height, TextureFormat format, bool isLinear, Color32 color)
    {
        var tex = new Texture2D(width, height, format, mipChain: false, linear: isLinear)
        {
            name = "BiomeSurfacePlaceholder",
            wrapMode = TextureWrapMode.Repeat,
        };
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return tex;
    }

    static Texture2D FindReferenceTexture(BiomeRegistry registry, System.Func<BiomeDefinition, Texture2D> selector)
    {
        int count = registry.BiomeCount;
        for (int i = 0; i < count; i++)
        {
            var def = registry.GetDefinitionByIndex(i);
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
    }
}
