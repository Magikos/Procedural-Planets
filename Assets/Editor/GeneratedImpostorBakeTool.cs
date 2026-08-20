using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Bakes the far-field impostor atlases for every GENERATED prop — trees, plants and rocks — to disk, so the
// runtime stops re-baking them on every load. MEASURED: 418 ms and 26.8 MB per share key, which was the whole
// of the 7.6 s scatter-renderer phase.
//
// This is the bake to run. Its sibling, "Bake Impostors (Source Library)", bakes the untouched Synty meshes
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
    const int OctGridN = 8; // must match ScatterImpostorFactory.OctGridN or the runtime samples the wrong cells

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

        // One atlas per share key: the age/seed variants of a species differ by a few metres of silhouette,
        // which is under a pixel at impostor range. A prototype that already carries a card needs nothing from
        // us — that is the untouched Synty library, which ships its own atlases.
        var byKey = new Dictionary<string, ScatterPrototypeDto>();
        foreach (ScatterPrototypeDto p in lib.Prototypes)
        {
            if (p == null || string.IsNullOrEmpty(p.ImpostorShareKey) || !p.HasImpostor) continue;
            if (p.BakedImpostorAtlas != null) continue;
            if (!byKey.ContainsKey(p.ImpostorShareKey)) byKey[p.ImpostorShareKey] = p;
        }
        if (byKey.Count == 0)
        {
            Debug.LogWarning("[GeneratedImpostorBake] No generated prototypes declare an impostor share key.");
            return;
        }

        var entries = new List<GeneratedImpostorManifest.Entry>();
        int baked = 0, skipped = 0;
        int i = 0;
        try
        {
            foreach (KeyValuePair<string, ScatterPrototypeDto> kv in byKey)
            {
                string key = kv.Key;
                ScatterPrototypeDto proto = kv.Value;
                EditorUtility.DisplayProgressBar("Baking generated impostors", key, i++ / (float)byKey.Count);

                var meshes = new List<Mesh>();
                var materials = new List<Material>();
                foreach (ScatterPartDto part in proto.Parts)
                {
                    if (!part.CanRender || part.LodMeshes.Length == 0 || part.LodMeshes[0] == null) continue;
                    meshes.Add(part.LodMeshes[0]);
                    materials.Add(part.Material);
                }
                if (meshes.Count == 0) { skipped++; continue; }

                // Per-KIND probe hash, not a hash of this prototype's meshes: every variant of the key has
                // different geometry and they all share this one card, so the runtime must be able to accept it
                // for all of them. Each injector owns its own probe, so route by the key's prefix — the same
                // function has to run here and at load or the hash never matches.
                string hash = IsRockKey(key) ? RockInjection.ImpostorProbeHash(key)
                    : IsPlantKey(key) ? PlantInjection.ImpostorProbeHash(key)
                    : TreeInjection.ImpostorProbeHash(key);

                // An empty hash can never match at load, so the entry would be dead weight AND the key would
                // silently live-bake forever while the manifest claimed to cover it. Refuse rather than write it.
                if (string.IsNullOrEmpty(hash))
                {
                    Debug.LogWarning($"[GeneratedImpostorBake] '{key}' produced no probe hash; skipping. " +
                                     "The atlas would never be matched at load.");
                    skipped++;
                    continue;
                }

                ScatterImpostorBaker.AtlasCard card = ScatterImpostorBaker.BakeAtlas(meshes, materials, OctGridN);
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
                if (hasNormal)
                {
                    AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceUpdate);
                    ScatterImpostorBakeTool.ConfigureAtlasImport(normalPath, isNormal: true);
                }

                entries.Add(new GeneratedImpostorManifest.Entry
                {
                    Key = key,
                    ShapeHash = hash,
                    Atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath),
                    Normal = hasNormal ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) : null,
                });
                baked++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        WriteManifest(entries);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        GeneratedImpostorManifest.ForgetCache();

        Debug.Log($"[GeneratedImpostorBake] Baked {baked} atlas(es), skipped {skipped}. " +
                  $"Saves roughly {baked * 418 / 1000f:0.0} s and {baked * 26.8f:0} MB per load. " +
                  $"Manifest: {ManifestPath}");
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

    static bool IsRockKey(string key) => key.StartsWith("rock-", System.StringComparison.Ordinal);

    // Ask PlantInjection rather than keeping a prefix list here. The list version rotted silently when six
    // kinds were added: their keys fell through to the TREE probe, which returned an empty hash, and those
    // props live-baked on every load while the manifest looked complete.
    static bool IsPlantKey(string key) => PlantInjection.OwnsKey(key);

    static void DestroyCard(ScatterImpostorBaker.AtlasCard card)
    {
        if (card.Texture != null) Object.DestroyImmediate(card.Texture);
        if (card.NormalTexture != null) Object.DestroyImmediate(card.NormalTexture);
    }

    // Share keys are species names today, but a key reaches a FILE PATH, so anything that is not clearly safe
    // is replaced rather than trusted.
    static string Sanitize(string key)
    {
        var sb = new System.Text.StringBuilder(key.Length);
        foreach (char c in key)
            sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
        return sb.ToString();
    }
}
