using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Bakes a scatter prototype's near mesh into one front-view billboard card (RGB = UNLIT albedo,
// A = silhouette) for the far-field impostor tier. Lighting is applied at runtime in the impostor
// shader from the same main light + ambient the mesh uses, so impostors track the day/night sun instead
// of freezing a bake-time light. The card is baked flat (white ambient, no directional) against a pure
// black background, so the silhouette is keyed off luminance. That black background depends on the bake
// camera being a Preview camera (below): the sky/atmosphere/cloud render features all skip Preview, so
// none of them paint sky into the background. Runtime-capable: the LOD strip bakes on load; the planet
// bakes the same way at build.
public static class ScatterImpostorBaker
{
    public struct Card
    {
        public Texture2D Texture;
        public float Width;  // world metres — the billboard quad width
        public float Height; // world metres
        public bool Valid;   // false when the bake produced almost no silhouette (see MinSilhouetteAlpha)
    }

    // Octahedral impostor atlas: the prototype baked from a GridN×GridN grid of hemisphere camera angles
    // (hemi-octahedral layout) into one square texture. At runtime the camera-facing quad picks the cell
    // matching the view direction, so the card reads as the tree from ANY angle — including straight down —
    // instead of a single front-view billboard that foreshortens to a slab from above.
    public struct AtlasCard
    {
        public Texture2D Texture;       // (GridN*AtlasCellPx) square hemi-octahedral atlas, cell (i,j) at (i,j)*cellPx
        public Texture2D NormalTexture; // packed view-space normal RG, surface depth B, leaf mask A when HasSurfaceData
        public float WorldSize;         // square billboard side in world metres (max of footprint width / height)
        public Vector3 CenterOffset;    // bake centre in object space, including lateral offsets of leaning props
        public int GridN;               // frames per axis
        public bool HasSurfaceData;
        public bool Valid;              // false when the bake keyed almost no silhouette (see MinSilhouetteAlpha)
    }

    const int CardHeightPx = 256;
    const int AtlasCellPx = 128;  // per-angle cell resolution in the octahedral atlas
    const int BakeLayer = 31; // isolate the bake rig from the rest of the scene

    // A bake that keys almost no coverage (thin _ForceLeaf blades like reeds, or a prototype whose front
    // view is nearly empty) yields an invisible card. Callers should skip the impostor tier for these and
    // let the prototype hard-cull at its mesh range instead of drawing nothing.
    const float MinSilhouetteAlpha = 0.2f;

    // Four texels of skirt at 128 px per cell: enough to survive mips 1-3, which is the range a card at the
    // tree line actually samples. Below that a whole cell averages anyway.
    const int ColorBleedPasses = 4;

    // The atlas camera frames the prop slightly wider than its bounds so a diagonal view cannot clip the
    // cell edge. The card quad has to span that FRAMED extent, not the mesh extent, or the card draws the
    // margin smaller than the mesh it replaces. That was a uniform 0.898 area ratio at the swap across the
    // whole library - a solid rock measured it as exactly as a tree, which is what gave it away as framing
    // and not an alpha-cutoff problem. Both AtlasCard paths must use the same number as the camera.
    const float FrameMargin = 1.04f;

    // A mesh self-shadows per fragment - a canopy darkens its own trunk and lower leaves - and a single
    // billboard quad cannot, so an uncorrected card reads BRIGHTER than the mesh it replaces and the swap
    // pops. The shader's cardSelfOcclusion takes the library-wide part of that off; what is left is
    // per-species. Measured over 185 prototypes at the 36 px handover, reeds and boulders land within 3% of
    // their mesh while conifers run 1.24-1.31x, so no single number can serve both.
    //
    // These fit lum = A + B*ln(size) + C*verticalLayers over the 122 prototypes whose card still tracks its
    // mesh silhouette (iou >= 0.6 - below that the brightness gap is a broken silhouette, a different defect,
    // and those rows only add noise). Residual SD 0.054. To re-fit, run ScatterLodSweep and regress its
    // lumRatioVsLod0 column on the sizeM and vertLayers columns.
    const float LumBase = 0.9640f;
    const float LumPerLogSize = 0.0386f;   // a taller prop hands over from a deeper, already-thinned mesh LOD
    const float LumPerVerticalLayer = 0.0198f;

    // Past these the trim stops matching the mesh and starts changing how the prop reads on its own.
    const float MinCardBrightness = 0.75f;
    const float MaxCardBrightness = 1.12f;

    static readonly List<Vector3> _areaVerts = new List<Vector3>();
    static readonly List<int> _areaTris = new List<int>();

    // How many layers of the prop's own geometry a downward ray crosses: total surface area projected onto
    // the ground plane, over the prop's footprint. A boulder shell scores about 1.5, a conifer 5.2, because
    // its canopy stacks over its own trunk. That is exactly the geometry the mesh shadows itself with.
    public static float VerticalLayersOf(IReadOnlyList<Mesh> meshes) => Measure(meshes).layers;

    // Scale on the card's lit colour that brings it back onto the mesh it replaces. 1 means no trim.
    public static float CardBrightnessOf(IReadOnlyList<Mesh> meshes)
    {
        (float layers, float sizeMeters) = Measure(meshes);
        if (sizeMeters <= 0f) return 1f;
        float lum = LumBase + LumPerLogSize * Mathf.Log(sizeMeters) + LumPerVerticalLayer * layers;
        return Mathf.Clamp(1f / Mathf.Max(lum, 0.1f), MinCardBrightness, MaxCardBrightness);
    }

    static (float layers, float sizeMeters) Measure(IReadOnlyList<Mesh> meshes)
    {
        if (meshes == null || meshes.Count == 0) return (0f, 0f);
        Bounds b = default;
        bool haveBounds = false;
        double down = 0.0;
        for (int m = 0; m < meshes.Count; m++)
        {
            Mesh mesh = meshes[m];
            if (mesh == null) continue;
            // An imported mesh without Read/Write enabled cannot be walked, and a partial sum would read as
            // a prop with no canopy and under-correct. Fall back to the uncorrected case for the whole prop.
            if (!mesh.isReadable) return (0f, 0f);
            if (!haveBounds) { b = mesh.bounds; haveBounds = true; }
            else b.Encapsulate(mesh.bounds);
            mesh.GetVertices(_areaVerts);
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                mesh.GetTriangles(_areaTris, s);
                for (int i = 0; i + 2 < _areaTris.Count; i += 3)
                {
                    Vector3 p0 = _areaVerts[_areaTris[i]];
                    Vector3 e1 = _areaVerts[_areaTris[i + 1]] - p0;
                    Vector3 e2 = _areaVerts[_areaTris[i + 2]] - p0;
                    down += Mathf.Abs(Vector3.Cross(e1, e2).y) * 0.5;
                }
            }
        }
        if (!haveBounds) return (0f, 0f);
        Vector3 e = b.size;
        double footprint = (double)Mathf.Max(e.x, 1e-3f) * Mathf.Max(e.z, 1e-3f);
        // Same measure as ScatterPrototypeDto.BoundsSizeMeters, over the same LOD0 meshes.
        return ((float)(down / footprint), Mathf.Max(e.y, Mathf.Max(e.x, e.z)));
    }

    public static Card Bake(IReadOnlyList<Mesh> meshes, IReadOnlyList<Material> materials)
    {
        using var state = new BakeState();
        Bounds b = meshes[0].bounds;
        for (int i = 1; i < meshes.Count; i++) b.Encapsulate(meshes[i].bounds);
        float w = Mathf.Max(b.size.x, b.size.z);
        float h = Mathf.Max(b.size.y, 1e-3f);
        Vector3 ctr = b.center;
        int px = Mathf.Max(8, Mathf.RoundToInt(CardHeightPx * w / h));

        var root = state.Own(new GameObject("ImpostorBakeRig") { hideFlags = HideFlags.HideAndDontSave });
        for (int i = 0; i < meshes.Count; i++)
        {
            var g = new GameObject("m") { layer = BakeLayer };
            g.transform.SetParent(root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = meshes[i];
            state.ConfigureRenderer(g.AddComponent<MeshRenderer>(), materials[i]);
        }
        var camGO = new GameObject("c");
        camGO.transform.SetParent(root.transform, false);
        Camera cam = camGO.AddComponent<Camera>();
        // Preview camera type: every sky/atmosphere/cloud/star/scatter render feature skips Preview and
        // Reflection cameras, so none of them paint the sky into the bake's background. Without this the
        // atmosphere feature fills the background with sky blue (opaque), the luminance key reads that as
        // geometry, and the whole card bakes as an opaque blue box instead of a clean tree silhouette.
        cam.cameraType = CameraType.Preview;
        cam.orthographic = true;
        cam.orthographicSize = h * 0.52f;
        cam.aspect = (float)px / CardHeightPx;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.cullingMask = 1 << BakeLayer;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = Mathf.Max(w, h) * 8f;
        camGO.transform.position = ctr + new Vector3(0f, 0f, -Mathf.Max(w, h) * 2f);
        camGO.transform.LookAt(ctr);

        // Flat white ambient + no directional light -> the mesh renders as ~unlit albedo (runtime
        // lights it). Against the pure-black background this keeps the luminance key clean.
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.white;
        int albedoBakeId = Shader.PropertyToID(ShaderGlobalIds.ImpostorAlbedoBake);
        Shader.SetGlobalFloat(albedoBakeId, 1f);

        var rt = state.Own(new RenderTexture(px, CardHeightPx, 16, RenderTextureFormat.ARGB32));
        Texture2D albedo = RenderTo(cam, rt, Color.black, state);

        Shader.SetGlobalFloat(albedoBakeId, 0f);

        var card = state.Own(new Texture2D(px, CardHeightPx, TextureFormat.ARGB32, false));
        Color[] ap = albedo.GetPixels();
        var outPx = new Color[ap.Length];
        float maxAlpha = 0f;
        for (int i = 0; i < ap.Length; i++)
        {
            float cover = Mathf.Max(ap[i].r, Mathf.Max(ap[i].g, ap[i].b));
            float t = Mathf.Clamp01((cover - 0.008f) / (0.03f - 0.008f)); // coverage, not luminance (see BakeAtlas)
            float a = t * t * (3f - 2f * t); // pure-black bg -> 0, geometry -> 1 (real smoothstep)
            if (a > maxAlpha) maxAlpha = a;
            outPx[i] = new Color(ap[i].r, ap[i].g, ap[i].b, a);
        }
        card.SetPixels(outPx);
        card.Apply();

        cam.targetTexture = null;

        return new Card { Texture = state.Keep(card), Width = w, Height = h, Valid = maxAlpha >= MinSilhouetteAlpha };
    }

    // Bakes the prototype from a GridN×GridN hemisphere of angles into one octahedral atlas. Same unlit-
    // albedo + luminance-key silhouette as Bake, but square cells (framing the tree's max extent so it fits
    // from any angle) and one render per cell. The billboard is centred on the tree centre (CenterOffset)
    // and made camera-facing at runtime, so the atlas cell for the view direction always shows a real angle.
    public static AtlasCard BakeAtlas(IReadOnlyList<Mesh> meshes, IReadOnlyList<Material> materials, int gridN, int cellPixels = AtlasCellPx)
        => BakeAtlasAsync(meshes, materials, gridN, cellPixels, default, false).GetAwaiter().GetResult();

    public static async Awaitable<AtlasCard> BakeAtlasAsync(IReadOnlyList<Mesh> meshes,
        IReadOnlyList<Material> materials, int gridN, int cellPixels,
        System.Threading.CancellationToken ct, bool yieldFrames = true)
    {
        using var state = new BakeState();
        if (gridN < 2) throw new System.ArgumentOutOfRangeException(nameof(gridN));
        if (cellPixels < 16) throw new System.ArgumentOutOfRangeException(nameof(cellPixels));
        Bounds b = meshes[0].bounds;
        for (int i = 1; i < meshes.Count; i++) b.Encapsulate(meshes[i].bounds);
        float w = Mathf.Max(b.size.x, b.size.z);
        float h = Mathf.Max(b.size.y, 1e-3f);
        float s = Mathf.Max(w, h);   // square framing fits the tree from top-down (w wide) and side (h tall)
        Vector3 ctr = b.center;

        var root = state.Own(new GameObject("ImpostorAtlasBakeRig") { hideFlags = HideFlags.HideAndDontSave });
        for (int i = 0; i < meshes.Count; i++)
        {
            var g = new GameObject("m") { layer = BakeLayer };
            g.transform.SetParent(root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = meshes[i];
            state.ConfigureRenderer(g.AddComponent<MeshRenderer>(), materials[i]);
        }
        var camGO = new GameObject("c");
        camGO.transform.SetParent(root.transform, false);
        Camera cam = camGO.AddComponent<Camera>();
        cam.enabled = false;
        cam.cameraType = CameraType.Preview;   // black background (see Bake) — the luminance key depends on it
        cam.orthographic = true;
        cam.orthographicSize = s * FrameMargin * 0.5f;
        cam.aspect = 1f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 1 << BakeLayer;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = s * 8f;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.white;
        int albedoBakeId = Shader.PropertyToID(ShaderGlobalIds.ImpostorAlbedoBake);
        int normalBakeId = Shader.PropertyToID(ShaderGlobalIds.ImpostorNormalBake);

        int atlasPx = gridN * cellPixels;
        // WITH mipmaps: an impostor card at the tree line covers a few screen pixels while a cell is 128 px, so
        // an unmipped atlas is minified ~16x and samples essentially at random. Against the shader's hard alpha
        // clip that aliasing becomes binary keep/discard, which is what made the distant tree line read as
        // speckled holes. Mips also cap how far cells can bleed into each other: they only merge below ~8 px per
        // cell, by which point the card is a couple of pixels on screen.
        var atlas = state.Own(new Texture2D(atlasPx, atlasPx, TextureFormat.ARGB32, true));
        var normalAtlas = state.Own(new Texture2D(atlasPx, atlasPx, TextureFormat.ARGB32, true));
        // Every cell renders into its own viewport rect of one atlas-sized target, so the bake pays two
        // GPU readbacks instead of two per cell. ReadPixels stalls the pipeline until the GPU drains, and
        // at 8x8 that was 128 stalls per prototype — the dominant cost of the whole bake.
        var albedoRt = state.Own(new RenderTexture(atlasPx, atlasPx, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
        var normalRt = state.Own(new RenderTexture(atlasPx, atlasPx, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
        var neutralNormalBg = new Color(0.5f, 0.5f, 1f, 1f); // encoded (0,0,1): faces the viewer
        float dist = s * 2f;
        Shader.SetGlobalFloat(ShaderGlobalIds.ImpostorBakeDistance, dist);
        Shader.SetGlobalFloat(ShaderGlobalIds.ImpostorBakeSize, s * FrameMargin);
        float cellFrac = 1f / gridN;
        float maxAlpha = 0f;
        for (int j = 0; j < gridN; j++)
        for (int i = 0; i < gridN; i++)
        {
            Vector3 dir = HemiOctDecode(new Vector2((i + 0.5f) / gridN, (j + 0.5f) / gridN)); // tree -> camera
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.forward, dir); // top-down pole
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right).normalized;
            camGO.transform.SetPositionAndRotation(ctr + dir * dist, Quaternion.LookRotation(-dir, up));

            cam.rect = new Rect(i * cellFrac, j * cellFrac, cellFrac, cellFrac);

            // Albedo pass: flat unlit albedo on a black background (coverage keys the silhouette).
            Shader.SetGlobalFloat(albedoBakeId, 1f);
            Shader.SetGlobalFloat(normalBakeId, 0f);
            cam.backgroundColor = Color.black;
            cam.targetTexture = albedoRt;
            cam.Render();

            // Normal pass: view-space surface normal on a neutral (viewer-facing) background.
            Shader.SetGlobalFloat(albedoBakeId, 0f);
            Shader.SetGlobalFloat(normalBakeId, 1f);
            cam.backgroundColor = neutralNormalBg;
            cam.targetTexture = normalRt;
            cam.Render();
            if (yieldFrames && i == gridN - 1)
            {
                root.SetActive(false);
                state.RestoreGlobals();
                await Awaitable.NextFrameAsync(ct);
                root.SetActive(true);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white;
                Shader.SetGlobalFloat(ShaderGlobalIds.ImpostorBakeDistance, dist);
                Shader.SetGlobalFloat(ShaderGlobalIds.ImpostorBakeSize, s * FrameMargin);
            }
        }
        Shader.SetGlobalFloat(albedoBakeId, 0f);
        Shader.SetGlobalFloat(normalBakeId, 0f);

        RenderTexture.active = albedoRt;
        atlas.ReadPixels(new Rect(0, 0, atlasPx, atlasPx), 0, 0);
        RenderTexture.active = normalRt;
        normalAtlas.ReadPixels(new Rect(0, 0, atlasPx, atlasPx), 0, 0);

        Color[] ap = atlas.GetPixels();
        Color[] np = normalAtlas.GetPixels();
        root.SetActive(false);
        state.RestoreGlobals();
        if (yieldFrames) await Awaitable.BackgroundThreadAsync();
        try
        {
            for (int k = 0; k < ap.Length; k++)
            {
                // Coverage from geometry presence, not brightness: the background is pure black, so any pixel
                // the tree rendered has some colour. Keying the silhouette off luminance dropped dark foliage
                // (shadowed / dark-green leaves) as holes ("shot with a shotgun"); key off the max channel
                // with a low floor so dark-but-present leaves stay a solid silhouette.
                float cover = Mathf.Max(ap[k].r, Mathf.Max(ap[k].g, ap[k].b));
                float t = Mathf.Clamp01((cover - 0.008f) / (0.03f - 0.008f));
                float a = t * t * (3f - 2f * t);
                if (a > maxAlpha) maxAlpha = a;
                ap[k] = new Color(ap[k].r, ap[k].g, ap[k].b, a);
                // Surface data keeps its depth and leaf mask; coverage comes from the colour atlas.
            }
            DilateColorIntoTransparent(ap, atlasPx, cellPixels, ColorBleedPasses);
            DilateColorIntoTransparent(np, atlasPx, cellPixels, ColorBleedPasses, ap);
        }
        finally
        {
            if (yieldFrames) await Awaitable.MainThreadAsync();
        }
        ct.ThrowIfCancellationRequested();
        atlas.SetPixels(ap);
        normalAtlas.SetPixels(np);
        // Apply(true) builds the mip chain; without it the texture keeps mip 0 only and the allocation above
        // buys nothing. Trilinear so the card crossfades between mips instead of stepping between them.
        atlas.Apply(true);
        normalAtlas.Apply(true);
        atlas.filterMode = FilterMode.Trilinear;
        normalAtlas.filterMode = FilterMode.Trilinear;

        cam.targetTexture = null;

        return new AtlasCard { Texture = state.Keep(atlas), NormalTexture = state.Keep(normalAtlas), WorldSize = s * FrameMargin, CenterOffset = ctr, GridN = gridN, HasSurfaceData = true, Valid = maxAlpha >= MinSilhouetteAlpha };
    }

    // Every transparent texel is pure black (the bake background), so the mip chain averages the card's
    // colour toward black exactly where its silhouette is thinnest — and a card at the tree line is always
    // minified, so that reads as a dark fringe and forces a high alpha cutoff to hide it. Bleed the keyed
    // colour outward into the transparent texels first: mip 0 is untouched because alpha is preserved, but
    // every lower mip then averages real foliage colour, which is what lets the shader clip at a low cutoff
    // and keep thin silhouettes. Bleeding stays inside each octahedral cell so one view angle never leaks
    // colour into its neighbour.
    static void DilateColorIntoTransparent(Color[] px, int size, int cellPx, int passes, Color[] coverage = null)
    {
        var filled = new bool[px.Length];
        for (int i = 0; i < px.Length; i++) filled[i] = (coverage ?? px)[i].a > 0.01f;
        var src = new Color[px.Length];
        var srcFilled = new bool[px.Length];
        for (int p = 0; p < passes; p++)
        {
            System.Array.Copy(px, src, px.Length);
            System.Array.Copy(filled, srcFilled, filled.Length);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int k = y * size + x;
                if (srcFilled[k]) continue;
                int cx0 = x / cellPx * cellPx, cy0 = y / cellPx * cellPx;
                float r = 0f, g = 0f, b = 0f, a = 0f;
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < cx0 || nx >= cx0 + cellPx || ny < cy0 || ny >= cy0 + cellPx) continue;
                    int j = ny * size + nx;
                    if (!srcFilled[j]) continue;
                    r += src[j].r; g += src[j].g; b += src[j].b; a += src[j].a; n++;
                }
                if (n == 0) continue;
                px[k] = new Color(r / n, g / n, b / n, coverage != null ? a / n : px[k].a);
                filled[k] = true;
            }
        }
    }

    // Wrap a PRE-BAKED atlas texture (from the editor bake tool) as an AtlasCard, recomputing the
    // framing metadata (WorldSize, CenterOffset, GridN) from the same mesh bounds + cell size the bake used,
    // so a stored atlas needs no metadata sidecar. Lets the runtime skip the on-load bake when an atlas is
    // present, while the live BakeAtlas path stays as the fallback for runtime-placed / custom structures.
    public static AtlasCard FromPrebaked(Texture2D atlas, Texture2D normalAtlas, IReadOnlyList<Mesh> meshes, int gridN = 0, bool hasSurfaceData = false)
    {
        if (atlas == null || meshes == null || meshes.Count == 0) return default;
        Bounds b = meshes[0].bounds;
        for (int i = 1; i < meshes.Count; i++) b.Encapsulate(meshes[i].bounds);
        float w = Mathf.Max(b.size.x, b.size.z);
        float h = Mathf.Max(b.size.y, 1e-3f);
        if (gridN <= 0) gridN = Mathf.Max(1, atlas.width / AtlasCellPx);
        return new AtlasCard { Texture = atlas, NormalTexture = normalAtlas, WorldSize = Mathf.Max(w, h) * FrameMargin, CenterOffset = b.center, GridN = gridN, HasSurfaceData = hasSurfaceData, Valid = true };
    }

    // Hemi-octahedral decode: square uv in [0,1]^2 -> unit direction on the upper hemisphere (y = up).
    // Cell centre uv -> the camera direction that cell was baked from; the runtime encode is its inverse.
    // uv=(0.5,0.5) -> straight up (top-down view); the four corners -> the horizon cardinal directions.
    static Vector3 HemiOctDecode(Vector2 f)
    {
        f = f * 2f - Vector2.one;
        Vector2 p = new Vector2(f.x + f.y, f.x - f.y) * 0.5f;
        float y = 1f - Mathf.Abs(p.x) - Mathf.Abs(p.y);
        return new Vector3(p.x, y, p.y).normalized;
    }

    sealed class BakeState : System.IDisposable
    {
        static readonly string[] Globals =
        {
            ShaderGlobalIds.ImpostorAlbedoBake, ShaderGlobalIds.ImpostorNormalBake,
            ShaderGlobalIds.ImpostorBakeDistance, ShaderGlobalIds.ImpostorBakeSize,
        };
        readonly float[] _savedGlobals = new float[Globals.Length];
        readonly AmbientMode _ambientMode = RenderSettings.ambientMode;
        readonly Color _ambientLight = RenderSettings.ambientLight;
        readonly RenderTexture _active = RenderTexture.active;
        readonly List<Object> _owned = new List<Object>();
        ComputeBuffer _emptyInteractors;
        MaterialPropertyBlock _bakeProperties;

        public BakeState()
        {
            for (int i = 0; i < Globals.Length; i++) _savedGlobals[i] = Shader.GetGlobalFloat(Globals[i]);
        }

        public T Own<T>(T resource) where T : Object
        {
            _owned.Add(resource);
            return resource;
        }

        public void ConfigureRenderer(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            if (_emptyInteractors == null)
            {
                // D3D12 requires declared buffers even when the bake bypasses wind.
                // Local bindings leave the live interaction registry untouched.
                _emptyInteractors = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<GrassInteractorGpu>());
                _emptyInteractors.SetData(new GrassInteractorGpu[1]);
                _bakeProperties = new MaterialPropertyBlock();
                _bakeProperties.SetBuffer(ShaderGlobalIds.GrassInteractors, _emptyInteractors);
                _bakeProperties.SetBuffer(ShaderGlobalIds.GrassInteractorsPrevious, _emptyInteractors);
                _bakeProperties.SetInt(ShaderGlobalIds.GrassInteractorCount, 0);
                _bakeProperties.SetInt(ShaderGlobalIds.GrassInteractorPreviousCount, 0);
            }
            renderer.SetPropertyBlock(_bakeProperties);
        }

        public T Keep<T>(T resource) where T : Object
        {
            _owned.Remove(resource);
            return resource;
        }

        public void RestoreGlobals()
        {
            for (int i = 0; i < Globals.Length; i++) Shader.SetGlobalFloat(Globals[i], _savedGlobals[i]);
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientLight = _ambientLight;
            RenderTexture.active = _active;
        }

        public void Dispose()
        {
            RestoreGlobals();
            for (int i = _owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(_owned[i]);
            _emptyInteractors?.Release();
        }
    }

    static Texture2D RenderTo(Camera cam, RenderTexture rt, Color bg, BakeState state)
    {
        cam.backgroundColor = bg;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var t = state.Own(new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false));
        t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        t.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        return t;
    }
}
