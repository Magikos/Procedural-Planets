using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Bakes the far-field impostor atlases for every GENERATED prop — trees, plants and rocks — to disk, so the
// runtime stops re-baking them on every load. The runtime factory defines the view grid and cell size;
// the manifest records the grid because texture dimensions alone no longer identify the layout.
//
// This is the bake to run. Its sibling, "Bake Impostors (Source Library)", bakes the untouched source meshes
// instead; since injection now replaces every scatter prototype, those atlases only render with injection
// switched off, so that one is a fallback rather than part of the normal loop.
//
// Only the ATLASES are baked. The meshes stay runtime-generated because generating them is measured at 45 ms
// for every tree variant and 150 ms for every rock — caching those would save nothing and would put the
// edit-a-TreeDef-and-hit-play loop behind a bake step.
public static class GeneratedImpostorBakeTool
{
    const string OutputDir = "Assets/Resources/Settings/Scatter/GeneratedImpostors";
    const string ManifestPath = "Assets/Resources/Settings/GeneratedImpostors.asset";
    const int OctGridN = ScatterImpostorFactory.OctGridN;

    [MenuItem("Tools/ProceduralPlanets/Impostors/Bake Impostors (Generated Props)", false, 10)]
    public static void Bake()
    {
        // MUST run in play mode. Outside it the FoliageLit canopy renders black, the coverage key reads that as
        // background, and every tree bakes as a bare trunk — a plausible-looking atlas that is silently wrong,
        // and only visible as stick-trees on the horizon. Refuse rather than write that.
        if (!EditorApplication.isPlaying)
        {
            Debug.LogError("[GeneratedImpostorBake] Enter play mode first. Baking from edit mode produces " +
                           "trunk-only atlases, because foliage materials do not render outside play.");
            return;
        }

        // Rebuild through the normal injection path so what we bake is exactly what the runtime will draw,
        // but WITHOUT last run's cards, so a rebake sees the same "no atlas yet" library the first bake saw.
        bool restoreTrees = TreeInjection.UseBakedImpostors;
        bool restorePlants = PlantInjection.UseBakedImpostors;
        bool restoreRocks = RockInjection.UseBakedImpostors;
        TreeInjection.UseBakedImpostors = false;
        PlantInjection.UseBakedImpostors = false;
        RockInjection.UseBakedImpostors = false;
        // This rebuild intentionally has no cards, so the coverage check would warn about every key mid-bake.
        ScatterValidation.Suppressed = true;
        ScatterLibraryDto lib;
        try { lib = TreeInjection.Rebuild(); }
        finally
        {
            TreeInjection.UseBakedImpostors = restoreTrees;
            PlantInjection.UseBakedImpostors = restorePlants;
            RockInjection.UseBakedImpostors = restoreRocks;
            ScatterValidation.Suppressed = false;
        }
        if (lib?.Prototypes == null)
        {
            Debug.LogError("[GeneratedImpostorBake] No scatter library — expected Resources/Settings/ScatterLibrary.");
            return;
        }

        Directory.CreateDirectory(OutputDir);

        // ONE ATLAS PER PROTOTYPE. Sharing a card across a species was wrong twice over: the age variants of a
        // Broadleaf run 4.15 m and 21 leaf clumps at age 0.25 against 22.22 m and 314 at age 1.0, and every
        // biome's three generated rocks are three different stones. Measured before the split: 135 of 176
        // prototypes drew a card baked from a different mesh, and 002_Forest-Rock — a wide flat boulder —
        // billboarded as a tall pointed wedge. 16 angles instead of 64 pays for the extra atlases: a 512 px
        // atlas is a quarter the bytes of the 1024 px one it replaces.
        // A prototype that already carries a card needs nothing from us — that is the untouched source
        // library, which ships its own atlases.
        var targets = new List<ScatterPrototypeDto>();
        foreach (ScatterPrototypeDto p in lib.Prototypes)
        {
            if (p == null || string.IsNullOrEmpty(p.SpeciesKey) || !p.HasImpostor) continue;
            if (p.BakedImpostorAtlas != null) continue;
            targets.Add(p);
        }
        if (targets.Count == 0)
        {
            Debug.LogWarning("[GeneratedImpostorBake] No generated prototypes want an impostor card.");
            return;
        }

        var entries = new List<GeneratedImpostorManifest.Entry>();
        var written = new HashSet<string>();
        int baked = 0, skipped = 0;
        bool complete = false;
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                ScatterPrototypeDto proto = targets[i];
                string key = proto.ImpostorKey;
                EditorUtility.DisplayProgressBar("Baking generated impostors", key, i / (float)targets.Count);

                var meshes = new List<Mesh>();
                var materials = new List<Material>();
                foreach (ScatterPartDto part in proto.Parts)
                {
                    if (!part.CanRender || part.LodMeshes.Length == 0 || part.LodMeshes[0] == null) continue;
                    meshes.Add(part.LodMeshes[0]);
                    materials.Add(part.Material);
                }
                if (meshes.Count == 0) { skipped++; continue; }

                // The prototype's OWN appearance, so bake time and load time compute the same value from the
                // same object. The old per-species probe regenerated a canonical variant to hash instead, and
                // every kind that probe could not parse returned an empty hash and live-baked forever.
                string hash = proto.ImpostorSourceHash;
                if (string.IsNullOrEmpty(hash))
                {
                    Debug.LogWarning($"[GeneratedImpostorBake] '{key}' produced no appearance hash; skipping. " +
                                     "The atlas would never be matched at load.");
                    skipped++;
                    continue;
                }

                ScatterImpostorBaker.AtlasCard card = ScatterImpostorBaker.BakeAtlas(meshes, materials, OctGridN, ScatterImpostorFactory.AtlasCellPixels);
                if (!card.Valid)
                {
                    // Same case the empty-bake guard covers at runtime: too little silhouette to be worth a card.
                    Debug.LogWarning($"[GeneratedImpostorBake] '{key}' keyed too little silhouette; left unbaked.");
                    DestroyCard(card);
                    skipped++;
                    continue;
                }

                string safe = Sanitize(key);
                string atlasPath = $"{OutputDir}/{safe}.png";
                string normalPath = $"{OutputDir}/{safe}_n.png";
                File.WriteAllBytes(atlasPath, ImageConversion.EncodeToPNG(card.Texture));
                bool hasNormal = card.NormalTexture != null;
                if (hasNormal) File.WriteAllBytes(normalPath, ImageConversion.EncodeToPNG(card.NormalTexture));
                DestroyCard(card);

                AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
                ScatterImpostorBakeTool.ConfigureAtlasImport(atlasPath);
                written.Add(atlasPath);
                if (hasNormal)
                {
                    AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceUpdate);
                    ScatterImpostorBakeTool.ConfigureAtlasImport(normalPath, card.HasSurfaceData);
                    written.Add(normalPath);
                }

                entries.Add(new GeneratedImpostorManifest.Entry
                {
                    Key = key,
                    ShapeHash = hash,
                    GridN = card.GridN,
                    HasSurfaceData = card.HasSurfaceData,
                    Atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath),
                    Normal = hasNormal ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) : null,
                });
                baked++;
            }
            complete = true;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        WriteManifest(entries);
        // Only after a clean full pass. A run that threw part way holds a partial written set, so every atlas
        // it had not reached yet would look like an orphan.
        int deleted = complete ? DeleteOrphans(written) : 0;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        GeneratedImpostorManifest.ForgetCache();

        Debug.Log($"[GeneratedImpostorBake] Baked {baked} atlas(es), skipped {skipped}, deleted {deleted} orphan(s). " +
                  $"Manifest: {ManifestPath}");
    }

    // Atlas PNGs this run did not write. Renaming a prototype or dropping a variant leaves its old atlas on
    // disk referenced by nothing, and the folder had accumulated about thirty of them.
    static int DeleteOrphans(HashSet<string> written)
    {
        int deleted = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { OutputDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (written.Contains(path)) continue;
            if (AssetDatabase.DeleteAsset(path)) deleted++;
        }
        return deleted;
    }

    static void WriteManifest(List<GeneratedImpostorManifest.Entry> entries)
    {
        var manifest = AssetDatabase.LoadAssetAtPath<GeneratedImpostorManifest>(ManifestPath);
        if (manifest == null)
        {
            manifest = ScriptableObject.CreateInstance<GeneratedImpostorManifest>();
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath));
            AssetDatabase.CreateAsset(manifest, ManifestPath);
        }
        manifest.Entries = entries.ToArray();
        EditorUtility.SetDirty(manifest);
    }

    static void DestroyCard(ScatterImpostorBaker.AtlasCard card)
    {
        if (card.Texture != null) Object.DestroyImmediate(card.Texture);
        if (card.NormalTexture != null) Object.DestroyImmediate(card.NormalTexture);
    }

    // Keys are prototype display names, and a key reaches a FILE PATH, so anything that is not clearly safe
    // is replaced rather than trusted.
    static string Sanitize(string key)
    {
        var sb = new System.Text.StringBuilder(key.Length);
        foreach (char c in key)
            sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
        return sb.ToString();
    }
}
