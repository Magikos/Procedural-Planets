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

    // The impostor bake tool clears this while it rebuilds, so it bakes from a library whose generated trees
    // carry NO card. Without it the tool would see the atlases it baked last time, mistake them for prebaked
    // Synty cards, and skip every tree — i.e. it could bake once and never again.
    public static bool UseBakedImpostors = true;

    static readonly Dictionary<TreeDefLibrary.TreeSpecies, (Material bark, Material foliage)> _mats = new();
    static readonly Dictionary<TreeDefLibrary.TreeSpecies, Material> _cleanFallback = new();
    static readonly Dictionary<TreeDefLibrary.TreeSpecies, Material> _coniferMats = new();
    static Material _cleanBase; // a clean leaf-image FoliageLit material (not a Synty palette atlas)

    public static ScatterLibraryDto Apply(ScatterLibraryDto lib)
    {
        if (!Enabled || lib?.Prototypes == null) return lib;
        try
        {
            _cleanBase = FindCleanLeaf(lib);
            // Recognising a usable leaf material needs the tree library, so this is the only place that can find
            // it; hand it to the shared factory so the non-tree plants can tint copies of the same texture.
            GeneratedFoliage.Prime(_cleanBase);
            int k = Mathf.Clamp(Variants, 1, 8);
            float spacingScale = Mathf.Sqrt(k); // K interleaved variants at sqrt(K) spacing ~ the original density
            int nextSlot = MaxSlot(lib) + 1;

            var protos = new List<ScatterPrototypeDto>(lib.Prototypes.Length * k);
            var extra = new List<ScatterPrototypeDto>();
            // Which tree prototype this is WITHIN its biome, in library order — picks the species from the
            // biome's set so a biome with several tree prototypes grows several species instead of one repeated.
            var biomeOrdinal = new Dictionary<BiomeType, int>();
            int replaced = 0, outOfSlots = 0, ferns = 0;
            foreach (ScatterPrototypeDto p in lib.Prototypes)
            {
                int ordinal = 0;
                if (IsTree(p))
                {
                    biomeOrdinal.TryGetValue(p.Biome, out ordinal);
                    biomeOrdinal[p.Biome] = ordinal + 1;
                }
                if (IsFern(p))
                {
                    ScatterPrototypeDto fern = TryReplaceFern(p);
                    protos.Add(fern ?? p);
                    if (fern != null) ferns++;
                    continue;
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
                $"(x{k}, slots through {nextSlot - 1}/{ScatterId.MaxSlot}); {ferns} fern prototype(s).");
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
    // Rocks are applied here too because this is the ONE place a runtime toggle rebuilds the library — rebuilding
    // trees alone would silently drop the generated rocks every time `tree.inject` was touched.
    // THE one place the injection chain is composed. Boot and the runtime toggle both call this: they used to
    // compose it separately, they drifted, and a booted world silently had no plants while a runtime toggle
    // produced them — which reads as a broken injector rather than an unwired one.
    //
    // Trees, then plants, then rocks. Each allocates its variant slots above the highest one it can see, so the
    // order fixes which range each family occupies; changing it renumbers saved variant slots.
    public static ScatterLibraryDto ApplyAll(ScatterLibraryDto source)
    {
        ScatterLibraryDto lib = RockInjection.Apply(PlantInjection.Apply(Apply(source)));
        var prototypes = new ScatterPrototypeDto[lib.Prototypes.Length];
        for (int i = 0; i < prototypes.Length; i++)
            prototypes[i] = lib.Prototypes[i].ApplyMeshOnlyPolicy();
        lib = new ScatterLibraryDto(prototypes);
        ScatterValidation.Run(lib);
        return lib;
    }

    public static ScatterLibraryDto Rebuild()
    {
        var so = Resources.Load<ScatterLibrary>("Settings/ScatterLibrary");
        return so != null ? ApplyAll(ScatterLibraryDto.From(so)) : null;
    }

    static bool IsTree(ScatterPrototypeDto p) =>
        p != null && p.Interaction == ScatterInteraction.Chop && p.Parts != null && p.Parts.Length > 0;

    // Ferns are Collect, not Chop, so the tree path never saw them. They are the one non-tree plant the
    // generator already produces, and matching them by name keeps the rest of the Collect library (flowers,
    // mushrooms, reeds) on its Synty meshes.
    static bool IsFern(ScatterPrototypeDto p) =>
        p != null && p.Interaction != ScatterInteraction.Chop && p.Parts != null && p.Parts.Length > 0
        && (p.DisplayName ?? "").IndexOf("fern", StringComparison.OrdinalIgnoreCase) >= 0;

    // A generated fern replacing a Collect prototype: one bark part for the stem, one foliage part for the
    // fronds, no cut-set (nothing chops a fern) and no extra variants (they are small enough that seed variety
    // is not worth spending scatter slots on).
    static ScatterPrototypeDto TryReplaceFern(ScatterPrototypeDto p)
    {
        try
        {
            int seed = (int)(StableHash(p.DisplayName ?? "fern", 0) % 900000) + 1;
            TreeDef def = TreeDefLibrary.Species(TreeDefLibrary.TreeSpecies.Fern, 1f);
            GeneratedTree t = TreeGenerator.Generate(def, seed);
            if (t.Bark == null || t.Bark.vertexCount == 0) return null;
            if (t.Foliage == null || t.Foliage.vertexCount == 0) return null;

            // NOT the prototype's own material: the Synty ferns wear Leaf_Palm_01, a 3-cell frond atlas, and the
            // blade primitive maps UV 0..1 across the WHOLE texture — so every frond would show all three cells
            // squashed together. A tinted single-leaf texture is the only thing whole-card UVs can wear.
            Material foliage = CleanFoliage(TreeDefLibrary.TreeSpecies.Fern, def);
            if (foliage == null) return null; // no clean leaf material to tint — keep the Synty fern
            (Material stem, Material _) = MatsFor(TreeDefLibrary.TreeSpecies.Fern, def);

            float cull = p.Parts[0].MaxCullDistance;
            if (cull < 20f) cull = 90f;
            float[] dist = { cull };
            var gen = p with
            {
                Parts = new[]
                {
                    new ScatterPartDto(stem, new[] { t.Bark }, dist, false, true),
                    new ScatterPartDto(foliage, new[] { t.Foliage }, dist, false, true),
                },
                BakedImpostorAtlas = null,
                BakedImpostorNormal = null,
                SpeciesKey = "Fern",
            };
            return UseBakedImpostors ? GeneratedImpostorManifest.WithCachedAtlas(gen) : gen;
        }
        catch (Exception e)
        {
            LoggerProvider.LogException("TreeInject", e);
            return null;
        }
    }

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
                // ordinal + variant: variant 0 keeps the biome's primary species, and the extra variants walk
                // the rest of the set. That is what finally places Cypress and Cedar — their biomes have only
                // ONE tree prototype each, so ordinal alone never advanced past the set's first entry. It also
                // means a single stand mixes species, not just ages.
                species = TreeDefLibrary.SpeciesForPrototype(p.Biome, ordinalInBiome + variant);

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
            // The billboard handles the far tier; keep the authored cull for its reach.
            float[] dist = { cull };

            var barkPart = new ScatterPartDto(bark, t.BarkLods, dist, true, true);
            // A dead tree has no foliage mesh at all; emitting an empty part would cost a draw band that renders
            // nothing and would confuse the impostor bake's bounds.
            bool hasFoliage = t.FoliageLods != null && t.FoliageLods.Length > 0
                              && t.FoliageLods[0] != null && t.FoliageLods[0].vertexCount > 0;
            var foliagePart = hasFoliage
                ? new ScatterPartDto(foliage, t.FoliageLods, dist, true, false)
                : null;

            // Keep the prototype's biome + placement rules; swap identity (slot/name), parts and stump. The
            // Synty atlas cannot be kept - it is the wrong silhouette - so the card comes from the generated
            // LOD0, per prototype, and WithCachedAtlas attaches the disk-baked one when it still matches.
            var gen = p with
            {
                DisplayName = variant == 0 ? p.DisplayName : $"{p.DisplayName} v{variant}",
                SlotId = slot,
                SpacingMeters = p.SpacingMeters * spacingScale,
                Parts = foliagePart != null ? new[] { barkPart, foliagePart } : new[] { barkPart },
                // Age now carries most of the size variance, so the per-instance jitter only nudges around it
                // instead of doubling the tree.
                ScaleRange = variantCount > 1 ? new Vector2(0.85f, 1.2f) : new Vector2(0.6f, 1.45f),
                StumpMesh = t.Stump,
                StumpMaterial = bark,
                BakedImpostorAtlas = null,
                BakedImpostorNormal = null,
                // Grove grouping only. Every variant of a species draws from one clumping field, so a grove of
                // variant 0 does not land in a clearing of variant 1 and average back out to uniform.
                SpeciesKey = species + (dead ? "-dead" : ""),
            };
            return UseBakedImpostors ? GeneratedImpostorManifest.WithCachedAtlas(gen) : gen;
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
        if (picked != null && !IsPaletteAtlas(picked) && !IsWrongCellAtlas(picked, s))
            return picked;             // clean leaf-image material — use as-is
        return CleanFoliage(s, def);   // palette / wrong-atlas / none -> tinted clean leaf
    }

    // A multi-cell atlas is only wearable by the primitive that maps to ONE cell. Leaf_Palm_01 packs three
    // fronds side by side and only AddFrond (LeafGroup 1, the Palm species) indexes a single one; a crossed leaf
    // card maps UV 0..1 across the WHOLE texture, so a broadleaf wearing it renders all three fronds squashed
    // into every leaf — big serrated green-and-dead-orange blades instead of foliage. This became reachable when
    // biomes gained species SETS: Tropical now grows Broadleaf beside Palm, off the palm prototype's material.
    static bool IsWrongCellAtlas(Material m, TreeDefLibrary.TreeSpecies s)
    {
        if (s == TreeDefLibrary.TreeSpecies.Palm) return false; // the one species that indexes a single cell
        Texture t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
        string n = t != null && t.name != null ? t.name.ToLowerInvariant() : "";
        return n.Contains("palm");
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
            // FoliageLit exposes _SeasonColor, not _BaseColor, so the old _BaseColor line was a silent no-op and
            // every species that fell back to this material wore the base texture's colour instead of its own.
            // Lifted because the source texture is dark, then capped at 1: an albedo multiplier above 1 is
            // brighter than white and reads as self-lit at night.
            Color leafTint = GeneratedFoliage.Normalise(def.LeafColor * 2.2f);
            if (m.HasProperty("_SeasonColor")) m.SetColor("_SeasonColor", leafTint);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", leafTint);
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

    // Bark character per species. The succulents get RIBBED, which is the whole silhouette read of a saguaro
    // up close and the thing that stopped the cactus looking like a smooth green post.
    static GeneratedSurfaceTexture.BarkStyle BarkStyleFor(TreeDefLibrary.TreeSpecies s) => s switch
    {
        TreeDefLibrary.TreeSpecies.Birch => GeneratedSurfaceTexture.BarkStyle.Birch,
        TreeDefLibrary.TreeSpecies.Conifer or TreeDefLibrary.TreeSpecies.Pine
            or TreeDefLibrary.TreeSpecies.Cedar or TreeDefLibrary.TreeSpecies.Cypress
            => GeneratedSurfaceTexture.BarkStyle.Plated,
        TreeDefLibrary.TreeSpecies.Palm => GeneratedSurfaceTexture.BarkStyle.Fibrous,
        TreeDefLibrary.TreeSpecies.Cactus or TreeDefLibrary.TreeSpecies.JoshuaTree
            => GeneratedSurfaceTexture.BarkStyle.Ribbed,
        // A baobab's bark is famously smooth and taut over the swollen trunk; furrowing it fights the shape.
        TreeDefLibrary.TreeSpecies.Baobab => GeneratedSurfaceTexture.BarkStyle.Smooth,
        TreeDefLibrary.TreeSpecies.Shrub or TreeDefLibrary.TreeSpecies.Fern
            => GeneratedSurfaceTexture.BarkStyle.Smooth,
        _ => GeneratedSurfaceTexture.BarkStyle.Furrowed,
    };

    static (Material, Material) MatsFor(TreeDefLibrary.TreeSpecies s, TreeDef def)
    {
        if (!_mats.TryGetValue(s, out (Material bark, Material foliage) pair))
        {
            Material bark = Mat($"Gen {def.Name} bark", def.BarkColor);
            // EVERY species gets a bark texture, not just birch. Trunk UVs tile per metre, so a small wrapped
            // luminance texture reads at any age, and the flat-tinted trunks every other species used to wear
            // were the single biggest reason they looked like plastic next to the birch.
            if (bark.HasProperty("_BaseMap"))
                bark.SetTexture("_BaseMap", GeneratedSurfaceTexture.Bark(BarkStyleFor(s)));
            pair = (bark, Mat($"Gen {def.Name} foliage", def.LeafColor));
            _mats[s] = pair;
        }
        return pair;
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
