using System.Collections.Generic;
using UnityEngine;

// Dev preview for the tree generator (plan 006). `tree.gen` grows one tree in front of the camera; `tree.age`
// / `tree.species` pick the stage + species. `tree.gallery` grids every species x age near the player so the
// per-biome look can be tuned in one glance (and prints the biome->species map). Console-only, registered by
// Planet. Uses the same Scatter/VertexColorLit + per-species _BaseColor the injected planet trees use, so the
// gallery reads like the world. All meshes orient to the local surface up.
[CommandPrefix("tree")]
public sealed class TreePreview : System.IDisposable
{
    readonly Transform _planetTransform;
    float _age = 1f;
    TreeDefLibrary.TreeSpecies _species = TreeDefLibrary.TreeSpecies.Broadleaf;

    GameObject _last;
    Material _barkMat;
    Material _foliageMat;
    Material _coniferMat;

    GameObject _gallery;
    readonly List<Material> _galleryMats = new();

    // Real biome leaf materials from the scatter library, per species (asset refs — never destroy).
    readonly Dictionary<TreeDefLibrary.TreeSpecies, Material> _speciesFoliage = new();
    ScatterLibrary _lib;
    bool _libTried;
    Material _cleanLeaf;
    bool _cleanLeafTried;

    public TreePreview(Transform planetTransform)
    {
        _planetTransform = planetTransform;
        ConsoleRegistry.RegisterInstance(this);
    }

    [ConsoleCommand("gen", "Generate a preview tree of the current species/age in front of the camera. Optional seed.", MonoTargetType.Registry)]
    string GenCmd(int? seed = null)
    {
        var cam = Camera.main;
        if (cam == null) return "tree: no main camera";

        TreeDef def = TreeDefLibrary.Species(_species, _age);
        int s = seed ?? Random.Range(1, 999999);
        GeneratedTree tree = TreeGenerator.Generate(def, s);

        if (_last != null) Object.Destroy(_last);
        Vector3 pos = cam.transform.position + cam.transform.forward * 8f;

        _last = new GameObject($"PreviewTree({def.Name} {s})");
        _last.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, SurfaceUp(pos)));
        Material genFoliage = def.FoliageStyle == FoliageStyle.ConiferCone
            ? ConiferPreviewMat(def.LeafColor)
            : FoliageMatForSpecies(_species) ?? FoliageMat(def.LeafColor);
        AddMesh(_last.transform, "bark", tree.Bark, BarkMat(def.BarkColor));
        AddMesh(_last.transform, "foliage", tree.Foliage, genFoliage);

        return $"tree: '{def.Name}' age {_age:F2} seed {s} — H {tree.Height:F1}m, HP {tree.ChopHp}, wood {tree.WoodYield}; " +
               $"bark {(tree.Bark ? tree.Bark.vertexCount : 0)}v, foliage {(tree.Foliage ? tree.Foliage.vertexCount : 0)}v, " +
               $"stump {(tree.Stump ? tree.Stump.vertexCount : 0)}v, log {(tree.Log ? tree.Log.vertexCount : 0)}v";
    }

    [ConsoleCommand("age", "Set preview tree age 0..1 (sapling..old), then re-run tree.gen.", MonoTargetType.Registry)]
    string AgeCmd(float age)
    {
        _age = Mathf.Clamp01(age);
        return $"tree age = {_age:F2} (0=sapling, 1=old). Run tree.gen to see it.";
    }

    [ConsoleCommand("species", "Set preview species (broadleaf/conifer/birch/palm/acacia/shrub), then re-run tree.gen.", MonoTargetType.Registry)]
    string SpeciesCmd(string name)
    {
        if (!TreeDefLibrary.TryParseSpecies(name, out _species))
            return $"tree: unknown species '{name}'. Options: broadleaf, conifer, birch, palm, acacia, shrub.";
        return $"tree species = {_species}. Run tree.gen to see it.";
    }

    [ConsoleCommand("gallery", "Grid every generated species x age (sapling..old) near the player + print the biome map. Optional seed.", MonoTargetType.Registry)]
    string GalleryCmd(int? seed = null)
    {
        var cam = Camera.main;
        if (cam == null) return "tree: no main camera";
        int baseSeed = seed ?? 12345;

        ClearGallery();
        _gallery = new GameObject("TreeGallery");

        Vector3 center = cam.transform.position + cam.transform.forward * 16f;
        Vector3 up = SurfaceUp(center);
        Vector3 right = Vector3.Cross(cam.transform.forward, up);
        right = right.sqrMagnitude > 1e-5f ? right.normalized : Vector3.Cross(Vector3.forward, up).normalized;
        Vector3 fwd = Vector3.Cross(up, right).normalized;

        float[] ages = { 0.15f, 0.4f, 0.7f, 1f };
        string[] ageNames = { "sapling", "young", "adult", "old" };
        var species = TreeDefLibrary.AllSpecies;
        const float colSpacing = 7f, rowSpacing = 9f;
        float colOffset = (ages.Length - 1) * 0.5f;

        int trees = 0;
        for (int r = 0; r < species.Length; r++)
        {
            Material bark = null, foliage = null;
            for (int c = 0; c < ages.Length; c++)
            {
                TreeDef def = TreeDefLibrary.Species(species[r], ages[c]);
                if (bark == null)
                {
                    bark = MakeMat($"{def.Name} bark", def.BarkColor);
                    _galleryMats.Add(bark);
                    foliage = def.FoliageStyle == FoliageStyle.ConiferCone ? ConiferPreviewMat(def.LeafColor) : FoliageMatForSpecies(species[r]);
                    if (foliage == null) { foliage = MakeMat($"{def.Name} foliage", def.LeafColor); _galleryMats.Add(foliage); }
                }

                GeneratedTree tree = TreeGenerator.Generate(def, baseSeed + r * 31 + c);
                Vector3 pos = center + right * ((c - colOffset) * colSpacing) + fwd * (-r * rowSpacing);
                var cell = new GameObject($"{def.Name} ({ageNames[c]})");
                cell.transform.SetParent(_gallery.transform, false);
                cell.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, SurfaceUp(pos)));
                AddMesh(cell.transform, "bark", tree.Bark, bark);
                AddMesh(cell.transform, "foliage", tree.Foliage, foliage);
                AddLabel(cell.transform, $"{def.Name}\n{ageNames[c]}");
                trees++;
            }
        }

        return $"tree.gallery: {trees} trees ({species.Length} species x {ages.Length} ages) at {center}. Biome map:\n{TreeDefLibrary.BiomeMapSummary()}";
    }

    [ConsoleCommand("inject", "Replace scatter trees with generated trees per biome (on/off), then run `generate` to apply.", MonoTargetType.Registry)]
    string InjectCmd(string state = "on")
    {
        TreeInjection.Enabled = state == "on" || state == "true" || state == "1";
        string status = $"generated-tree injection {(TreeInjection.Enabled ? "ON" : "OFF")}";

        // The DTO is snapshotted at boot; re-register it so a runtime toggle actually takes effect on the next
        // generate (Configure re-fetches ScatterLibraryDto). Off-play or pre-registration, boot-time Apply covers it.
        if (SettingsProvider.IsRegistered<ScatterLibraryDto>())
        {
            ScatterLibraryDto dto = TreeInjection.Rebuild();
            if (dto != null)
            {
                SettingsProvider.Update(dto);
                return $"{status} — scatter library updated. Run `generate` to rebuild the world.";
            }
        }
        return $"{status} — run `generate` to apply.";
    }

    Vector3 SurfaceUp(Vector3 worldPos)
    {
        if (_planetTransform == null) return Vector3.up;
        Vector3 up = worldPos - _planetTransform.position;
        return up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;
    }

    void AddMesh(Transform parent, string name, Mesh mesh, Material mat)
    {
        if (mesh == null) return;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static void AddLabel(Transform parent, string text)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, -0.4f, 0f);
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.characterSize = 0.14f;
        tm.fontSize = 64;
        tm.anchor = TextAnchor.UpperCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.black;
    }

    Material BarkMat(Color color)
    {
        if (_barkMat == null) _barkMat = MakeMat("Tree bark", color);
        else if (_barkMat.HasProperty("_BaseColor")) _barkMat.SetColor("_BaseColor", color);
        return _barkMat;
    }

    Material FoliageMat(Color color)
    {
        if (_foliageMat == null) _foliageMat = MakeMat("Tree foliage", color);
        else if (_foliageMat.HasProperty("_BaseColor")) _foliageMat.SetColor("_BaseColor", color);
        return _foliageMat;
    }

    // The real biome leaf material (FoliageLit + leaf-patch texture) for a species: the foliage material of a
    // scatter tree prototype whose biome maps to that species. Asset ref — cache it, never destroy it.
    Material FoliageMatForSpecies(TreeDefLibrary.TreeSpecies s)
    {
        if (_speciesFoliage.TryGetValue(s, out Material cached)) return cached;
        if (!_libTried) { _libTried = true; _lib = Resources.Load<ScatterLibrary>("Settings/ScatterLibrary"); }

        Material found = null;
        if (_lib?.Prototypes != null)
            foreach (ScatterPrototype proto in _lib.Prototypes)
            {
                if (proto == null || proto.Interaction != ScatterInteraction.Chop || proto.Parts == null) continue;
                if (!TreeDefLibrary.HasTree(proto.Biome, out TreeDefLibrary.TreeSpecies ps) || ps != s) continue;
                Material m = TreeInjection.PickFoliageMaterial(MaterialsOf(proto));
                if (m != null && !TreeInjection.IsPaletteAtlas(m)) { found = m; break; } // skip palette atlases (pine)
            }
        found ??= CleanLeaf(); // palette-atlas species (conifer) -> a clean leaf material
        _speciesFoliage[s] = found;
        return found;
    }

    // First clean (non-palette) leaf material in the library, as a fallback for species whose Synty material is a
    // palette atlas. Asset ref — never destroy.
    Material CleanLeaf()
    {
        if (_cleanLeafTried) return _cleanLeaf;
        _cleanLeafTried = true;
        if (_lib?.Prototypes != null)
            foreach (ScatterPrototype proto in _lib.Prototypes)
            {
                if (proto == null || proto.Interaction != ScatterInteraction.Chop || proto.Parts == null) continue;
                Material m = TreeInjection.PickFoliageMaterial(MaterialsOf(proto));
                if (m != null && !TreeInjection.IsPaletteAtlas(m)) { _cleanLeaf = m; break; }
            }
        return _cleanLeaf;
    }

    static Material[] MaterialsOf(ScatterPrototype proto)
    {
        var mats = new Material[proto.Parts.Length];
        for (int i = 0; i < mats.Length; i++) mats[i] = proto.Parts[i]?.Material;
        return mats;
    }

    // Conifer needles: solid geometry on FoliageLit with _ForceLeaf so it gets foliage lighting + reads the cone's
    // baked AO (vtx.G), matching the injected world.
    Material ConiferPreviewMat(Color color)
    {
        if (_coniferMat == null)
        {
            Shader sh = Shader.Find("Scatter/FoliageLit") ?? Shader.Find("Scatter/VertexColorLit");
            _coniferMat = new Material(sh) { name = "Tree needles", hideFlags = HideFlags.HideAndDontSave };
            if (_coniferMat.HasProperty("_ForceLeaf")) _coniferMat.SetFloat("_ForceLeaf", 1f);
            _coniferMat.enableInstancing = true;
            // FoliageLit colors from _BaseMap (no _BaseColor), so paint a solid-green base; vtx.G AO shades it.
            if (_coniferMat.HasProperty("_BaseMap"))
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "needle tint", hideFlags = HideFlags.HideAndDontSave };
                var px = new Color[16];
                for (int i = 0; i < px.Length; i++) px[i] = color;
                t.SetPixels(px); t.Apply();
                _coniferMat.SetTexture("_BaseMap", t);
            }
        }
        if (_coniferMat.HasProperty("_BaseColor")) _coniferMat.SetColor("_BaseColor", color);
        return _coniferMat;
    }

    // Same shader the injected planet trees use: Scatter/VertexColorLit tints by _BaseColor and is planet-lit.
    static Material MakeMat(string name, Color color)
    {
        Shader sh = Shader.Find("Scatter/VertexColorLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh) { name = name, hideFlags = HideFlags.HideAndDontSave };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        m.enableInstancing = true;
        return m;
    }

    void ClearGallery()
    {
        if (_gallery != null) Object.Destroy(_gallery);
        _gallery = null;
        foreach (Material m in _galleryMats) if (m != null) Object.Destroy(m);
        _galleryMats.Clear();
    }

    public void Dispose()
    {
        ConsoleRegistry.UnregisterInstance(typeof(TreePreview));
        if (_last != null) Object.Destroy(_last);
        if (_barkMat != null) Object.Destroy(_barkMat);
        if (_foliageMat != null) Object.Destroy(_foliageMat);
        if (_coniferMat != null) Object.Destroy(_coniferMat);
        ClearGallery();
    }
}
