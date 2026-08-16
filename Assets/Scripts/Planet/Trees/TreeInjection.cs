using System;
using System.Collections.Generic;
using UnityEngine;

// Replaces the scatter library's TREE prototypes (Interaction == Chop) with generated trees (plan 006). The
// species is chosen from the prototype's Biome (TreeDefLibrary.HasTree), so each biome grows the right tree.
// Applied at boot (Planet.RegisterWorldSettings) so play shows generated trees with no console step; `tree.inject
// off` reverts to Synty at runtime. Heavily guarded — any failure keeps the original Synty prototype, and a
// top-level catch keeps scatter working.
//
// Per-instance variety: instanced draw is one mesh per batch, so varied geometry comes from expanding each
// source tree into Variants generated prototypes (own seed + age stage, own scatter slot, spacing widened by
// sqrt(K) to hold total density). Different slots hash to different placements, so the variants interleave and
// a stand mixes ages and shapes. Variants are APPENDED after the originals so prototype indices stay stable for
// index-paired consumers (TreeShowcaseSpawner), and variant 0 keeps the source SlotId so saved chops still bind.
// ponytail: boot generates ~18*K trees on the main thread here; fine as a one-shot, move to a background job
// (T7) if boot hitches.
public static class TreeInjection
{
    public static bool Enabled = true;

    // Generated variants per source tree prototype (1 = the old one-mesh-per-prototype behaviour).
    public static int Variants = 3;

    static readonly Dictionary<TreeDefLibrary.TreeSpecies, (Material bark, Material foliage)> _mats = new();
    static readonly Dictionary<TreeDefLibrary.TreeSpecies, Material> _cleanFallback = new();
    static readonly Dictionary<TreeDefLibrary.TreeSpecies, Material> _coniferMats = new();
    static Material _cleanBase; // a clean leaf-image FoliageLit material (not a Synty palette atlas)
    static Texture2D _birchBark;

    public static ScatterLibraryDto Apply(ScatterLibraryDto lib)
    {
        if (!Enabled || lib?.Prototypes == null) return lib;
        try
        {
            _cleanBase = FindCleanLeaf(lib);
            int k = Mathf.Clamp(Variants, 1, 8);
            float spacingScale = Mathf.Sqrt(k); // K interleaved variants at sqrt(K) spacing ~ the original density
            int nextSlot = MaxSlot(lib) + 1;

            var protos = new List<ScatterPrototypeDto>(lib.Prototypes.Length * k);
            var extra = new List<ScatterPrototypeDto>();
            // Which tree prototype this is WITHIN its biome, in library order — picks the species from the
            // biome's set so a biome with several tree prototypes grows several species instead of one repeated.
            var biomeOrdinal = new Dictionary<BiomeType, int>();
            int replaced = 0, outOfSlots = 0;
            foreach (ScatterPrototypeDto p in lib.Prototypes)
            {
                int ordinal = 0;
                if (IsTree(p))
                {
                    biomeOrdinal.TryGetValue(p.Biome, out ordinal);
                    biomeOrdinal[p.Biome] = ordinal + 1;
                }
                ScatterPrototypeDto gen = IsTree(p) ? TryReplace(p, 0, k, p.SlotId, spacingScale, ordinal) : null;
                protos.Add(gen ?? p);
                if (gen == null) continue;
                replaced++;
                for (int v = 1; v < k; v++)
                {
                    if (nextSlot > ScatterId.MaxSlot) { outOfSlots++; continue; }
                    ScatterPrototypeDto variant = TryReplace(p, v, k, nextSlot, spacingScale, ordinal);
                    if (variant == null) continue;
                    extra.Add(variant);
                    nextSlot++;
                }
            }
            protos.AddRange(extra);

            LoggerProvider.Log(LogLevel.Info, "TreeInject",
                $"Replaced {replaced} tree prototype(s) with generated trees + {extra.Count} variant(s) " +
                $"(x{k}, slots through {nextSlot - 1}/{ScatterId.MaxSlot}).");
            if (outOfSlots > 0)
                LoggerProvider.Log(LogLevel.Warning, "TreeInject",
                    $"Out of scatter slots: {outOfSlots} tree variant(s) dropped. Lower tree.variants or free slots.");
            return new ScatterLibraryDto(protos.ToArray());
        }
        catch (Exception e)
        {
            LoggerProvider.LogException("TreeInject", e);
            return lib; // never break scatter
        }
    }

    // Rebuild the scatter DTO from the source library at the current Enabled state, for a runtime toggle.
    public static ScatterLibraryDto Rebuild()
    {
        var so = Resources.Load<ScatterLibrary>("Settings/ScatterLibrary");
        return so != null ? Apply(ScatterLibraryDto.From(so)) : null;
    }

    static bool IsTree(ScatterPrototypeDto p) =>
        p != null && p.Interaction == ScatterInteraction.Chop && p.Parts != null && p.Parts.Length > 0;

    // FNV-1a, NOT string.GetHashCode/HashCode.Combine: .NET randomises string hashing per PROCESS, so those
    // gave every session a different tree for the same world seed. Trees are world content — they have to be
    // reproducible across runs, or a saved world regrows differently and impostor atlases can never be cached.
    static uint StableHash(string s, int salt)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in s) { h ^= c; h *= 16777619u; }
            h ^= (uint)salt; h *= 16777619u;
            return h;
        }
    }

    static int MaxSlot(ScatterLibraryDto lib)
    {
        int max = -1;
        foreach (ScatterPrototypeDto p in lib.Prototypes)
            if (p != null && p.SlotId > max) max = p.SlotId;
        return max;
    }

    static ScatterPrototypeDto TryReplace(ScatterPrototypeDto p, int variant, int variantCount, int slot, float spacingScale, int ordinalInBiome)
    {
        try
        {
            // Biome picks the species, except where a prototype names one the biome map can't express: the birch
            // prototypes live in Forest, which maps to Broadleaf, so a birch would never actually grow.
            TreeDefLibrary.TreeSpecies species;
            if ((p.DisplayName ?? "").IndexOf("birch", StringComparison.OrdinalIgnoreCase) >= 0)
                species = TreeDefLibrary.TreeSpecies.Birch;
            else
                species = TreeDefLibrary.SpeciesForPrototype(p.Biome, ordinalInBiome);

            int seed = (int)(StableHash(p.DisplayName ?? "tree", variant) % 900000) + 1;
            // One variant -> the old per-TYPE age. Several -> ladder them sapling..old so a stand of one
            // species mixes real age shapes (the generator scales height/girth/branch tiers/leaf size by age),
            // not just seeds.
            float age = variantCount <= 1
                ? 0.3f + (seed % 100) / 100f * 0.7f
                : Mathf.Lerp(0.25f, 1f, variant / (float)(variantCount - 1));
            // The library's "* Dead Tree" prototypes are bare standing snags; generating a leafy tree for them
            // loses that biome's dead look entirely.
            bool dead = (p.DisplayName ?? "").IndexOf("dead", StringComparison.OrdinalIgnoreCase) >= 0;
            TreeDef def = dead ? TreeDefLibrary.DeadSpecies(species, age) : TreeDefLibrary.Species(species, age);
            GeneratedTree t = TreeGenerator.Generate(def, seed);
            if (t.Bark == null || t.Bark.vertexCount == 0) return null; // keep Synty

            (Material bark, Material foliageFallback) = MatsFor(species, def);
            // Conifer cone is solid dark-green geometry (VertexColorLit); other species reuse the prototype's own
            // Synty leaf-patch material so each biome gets its correct leaf look (our cards carry the vtx.B leaf
            // mask it expects). Palette-atlas materials (pine's Generic_*) get a clean tinted leaf substitute.
            Material foliage = def.NeedleFoliage
                ? ConiferMat(species, def)
                : SyntyFoliage(p, species, def) ?? foliageFallback;

            float cull = p.Parts[0].MaxCullDistance;
            if (cull < 50f) cull = 300f;
            float[] dist = { cull * 0.6f, cull }; // hold full LOD0 canopy farther before the impostor takes over

            var barkPart = new ScatterPartDto(bark, t.BarkLods, Trim(dist, t.BarkLods.Length), true, true);
            // A dead tree has no foliage mesh at all; emitting an empty part would cost a draw band that renders
            // nothing and would confuse the impostor bake's bounds.
            bool hasFoliage = t.FoliageLods != null && t.FoliageLods.Length > 0
                              && t.FoliageLods[0] != null && t.FoliageLods[0].vertexCount > 0;
            var foliagePart = hasFoliage
                ? new ScatterPartDto(foliage, t.FoliageLods, Trim(dist, t.FoliageLods.Length), true, false)
                : null;

            // Keep the prototype's biome + placement rules; swap identity (slot/name), parts + stump, and clear
            // the Synty impostor atlas so the renderer bakes from the generated LOD0 — once per species, since
            // all variants declare the same ImpostorShareKey. Age now carries most of the size variance, so the
            // per-instance ScaleRange only jitters around it instead of doubling the tree.
            return p with
            {
                DisplayName = variant == 0 ? p.DisplayName : $"{p.DisplayName} v{variant}",
                SlotId = slot,
                SpacingMeters = p.SpacingMeters * spacingScale,
                Parts = foliagePart != null ? new[] { barkPart, foliagePart } : new[] { barkPart },
                ScaleRange = variantCount > 1 ? new Vector2(0.85f, 1.2f) : new Vector2(0.6f, 1.45f),
                StumpMesh = t.Stump,
                StumpMaterial = bark,
                BakedImpostorAtlas = null,
                BakedImpostorNormal = null,
                ImpostorShareKey = p.DisplayName,
            };
        }
        catch (Exception e)
        {
            LoggerProvider.LogException("TreeInject", e);
            return null; // keep Synty
        }
    }

    static Material SyntyFoliage(ScatterPrototypeDto p, TreeDefLibrary.TreeSpecies s, TreeDef def)
    {
        Material picked = PickFoliageMaterial(MatsOf(p));
        if (picked != null && !IsPaletteAtlas(picked)) return picked; // clean leaf-image material — use as-is
        return CleanFoliage(s, def);                                  // palette atlas / none -> tinted clean leaf
    }

    static Material[] MatsOf(ScatterPrototypeDto p)
    {
        if (p?.Parts == null) return System.Array.Empty<Material>();
        var mats = new Material[p.Parts.Length];
        for (int i = 0; i < mats.Length; i++) mats[i] = p.Parts[i]?.Material;
        return mats;
    }

    // Textures our whole-card [0,1] UVs can't use: palette/gradient atlases (Synty "Generic_*"), biome palette
    // sheets ("..._Texture_NN", e.g. FoliageDead), and multi-leaf atlases (pohutukawa). Only the clean single-patch
    // leaf textures (leafPatch_*) and the palm frond atlas (handled per-cell) survive; substitute for the rest.
    public static bool IsPaletteAtlas(Material m)
    {
        if (m == null) return true;
        Texture t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
        if (t == null) return true;
        string n = t.name != null ? t.name.ToLowerInvariant() : "";
        return n.Contains("generic") || n.Contains("_texture") || n.Contains("palette") || n.Contains("pohutukawa");
    }

    static Material FindCleanLeaf(ScatterLibraryDto lib)
    {
        foreach (ScatterPrototypeDto pr in lib.Prototypes)
        {
            if (!IsTree(pr)) continue;
            Material m = PickFoliageMaterial(MatsOf(pr));
            if (m != null && !IsPaletteAtlas(m)) return m;
        }
        return null;
    }

    // A tinted copy of a known clean leaf material, for species whose Synty material is a palette atlas (conifer).
    static Material CleanFoliage(TreeDefLibrary.TreeSpecies s, TreeDef def)
    {
        if (_cleanBase == null) return null;
        if (!_cleanFallback.TryGetValue(s, out Material m))
        {
            m = new Material(_cleanBase) { name = $"Gen {def.Name} leaf" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", def.LeafColor * 2f); // texture already dark; lift tint
            _cleanFallback[s] = m;
        }
        return m;
    }

    // The leaf material among a tree prototype's parts. Both trunk and foliage can use Scatter/FoliageLit, so pick
    // by name: prefer a "...Canopy" material, else the first non-"beard" FoliageLit part, else any FoliageLit part.
    // Handles combined 1-part trees (pine/palm/dead), trunk+foliage, and canopy+branches ordering. Public so the
    // preview can match the world per species.
    public static Material PickFoliageMaterial(IReadOnlyList<Material> mats)
    {
        if (mats == null) return null;
        Material nonBeard = null, any = null;
        foreach (Material m in mats)
        {
            if (m == null || m.shader == null || m.shader.name != "Scatter/FoliageLit") continue;
            any ??= m;
            string n = m.name != null ? m.name.ToLowerInvariant() : "";
            if (n.Contains("canopy")) return m;
            if (nonBeard == null && !n.Contains("beard")) nonBeard = m;
        }
        return nonBeard ?? any;
    }

    // Conifer needles have no texture in this pack, so the cone is solid geometry rendered with FoliageLit set to
    // _ForceLeaf (leaf mask on everywhere) so it gets the leaf lighting + reads the baked AO in vtx.G — shaded like
    // foliage, not a flat block. _BaseColor carries the dark-green tint.
    static Material ConiferMat(TreeDefLibrary.TreeSpecies s, TreeDef def)
    {
        if (!_coniferMats.TryGetValue(s, out Material m))
        {
            Shader sh = Shader.Find("Scatter/FoliageLit") ?? Shader.Find("Scatter/VertexColorLit");
            m = new Material(sh) { name = $"Gen {def.Name} needles" };
            // FoliageLit colors from _BaseMap (no _BaseColor), so paint a solid-green base; vtx.G still shades it.
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", SolidTex(def.LeafColor));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", def.LeafColor);
            if (m.HasProperty("_ForceLeaf")) m.SetFloat("_ForceLeaf", 1f);
            // FoliageLit's wind is per-material sway metres and defaults to 0, so a material built in code is
            // RIGID unless this is set — the leaf-mask side alone (_ForceLeaf) buys nothing. Needles flex less
            // than broadleaf cards; matches the authored FoliagePine.mat.
            if (m.HasProperty("_WindStrength")) m.SetFloat("_WindStrength", 0.1f);
            m.enableInstancing = true;
            _coniferMats[s] = m;
        }
        return m;
    }

    static Texture2D SolidTex(Color c)
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Gen needle tint" };
        var px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static (Material, Material) MatsFor(TreeDefLibrary.TreeSpecies s, TreeDef def)
    {
        if (!_mats.TryGetValue(s, out (Material bark, Material foliage) pair))
        {
            Material bark = Mat($"Gen {def.Name} bark", def.BarkColor);
            // Birch is defined by its markings, not just a pale trunk, and the trunk UVs tile per metre, so a
            // small wrapped texture reads at any age. Every other species stays flat-coloured.
            if (s == TreeDefLibrary.TreeSpecies.Birch && bark.HasProperty("_BaseMap"))
                bark.SetTexture("_BaseMap", BirchBark());
            pair = (bark, Mat($"Gen {def.Name} foliage", def.LeafColor));
            _mats[s] = pair;
        }
        return pair;
    }

    // Birch bark: near-white ground with dark lenticel dashes and a few grey streaks. Generated rather than
    // authored so it needs no art asset and stays deterministic. 1 texture height = 1 metre of trunk (the mesher
    // writes V in metres), so the dashes are life-sized on a sapling and on a 22 m adult alike.
    static Texture2D BirchBark()
    {
        if (_birchBark != null) return _birchBark;
        const int w = 64, h = 128;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Gen birch bark", wrapMode = TextureWrapMode.Repeat };
        var px = new Color[w * h];
        uint rng = 0x9E3779B9;
        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / (float)0xFFFFFF; }

        for (int i = 0; i < px.Length; i++)
        {
            float grain = 0.94f + 0.06f * Mathf.PerlinNoise((i % w) * 0.35f, (i / w) * 0.12f);
            px[i] = new Color(grain, grain * 0.99f, grain * 0.95f, 1f);
        }
        // Horizontal dashes: short, dark, scattered — a few per band up the trunk.
        for (int band = 0; band < 26; band++)
        {
            int y = (int)(Rand() * h);
            int x = (int)(Rand() * w);
            int len = 3 + (int)(Rand() * 11);
            int thick = 1 + (int)(Rand() * 3);
            float dark = 0.10f + Rand() * 0.22f;
            for (int dy = 0; dy < thick; dy++)
            for (int dx = 0; dx < len; dx++)
            {
                int xx = (x + dx) % w, yy = (y + dy) % h;
                float taper = 1f - Mathf.Abs(dx / (float)len - 0.5f) * 0.7f; // fade the dash ends
                px[yy * w + xx] = Color.Lerp(px[yy * w + xx], new Color(dark, dark * 0.95f, dark * 0.9f, 1f), taper);
            }
        }
        t.SetPixels(px);
        t.Apply();
        _birchBark = t;
        return t;
    }

    static float[] Trim(float[] dist, int n)
    {
        var r = new float[Mathf.Max(1, n)];
        for (int i = 0; i < r.Length; i++) r[i] = dist[Mathf.Min(i, dist.Length - 1)];
        return r;
    }

    static Material Mat(string name, Color c)
    {
        Shader sh = Shader.Find("Scatter/VertexColorLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.enableInstancing = true;
        return m;
    }
}
