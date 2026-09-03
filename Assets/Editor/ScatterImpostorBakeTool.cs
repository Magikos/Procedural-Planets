using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Editor tool: pre-bake every scatter prototype's far-field impostor atlas to a texture asset and assign it
// to the prototype (ScatterPrototype.BakedImpostorAtlas). At runtime ScatterImpostorFactory uses the stored
// atlas and skips the on-load bake, so startup does not render hundreds of angles and the far billboards look
// identical every session. Re-runnable at any time (after adding/changing assets); prototypes without a
// stored atlas still bake live, which is the fallback for runtime-placed / custom-saved structures.
//
// Must run in PLAY mode: the bake renders foliage through FoliageLit's planet-sun lighting, which is only set
// up while playing (an edit-mode bake renders black cards). The tool freezes local noon first so every atlas
// bakes under the same overhead light.
public static class ScatterImpostorBakeTool
{
    const string AtlasFolder = "Assets/Resources/Settings/Scatter/ImpostorAtlases";
    // Every folder an impostor atlas can land in. GeneratedImpostors is written by GeneratedImpostorBakeTool.
    internal static readonly string[] AtlasFolders =
    {
        AtlasFolder,
        "Assets/Resources/Settings/Scatter/GeneratedImpostors",
    };
    const int OctGridN = 8;                 // matches ScatterImpostorFactory

    [MenuItem("Tools/ProceduralPlanets/Impostors/Bake Impostors (Source Library)", false, 20)]
    public static void BakeAll()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Bake Impostor Atlases",
                "Enter Play mode first, then run this again.\n\n" +
                "The bake renders foliage through the planet-sun lighting, which is only set up during Play. " +
                "Baking in edit mode produces black cards.", "OK");
            return;
        }

        FreezeLocalNoon();

        string[] guids = AssetDatabase.FindAssets("t:ScatterPrototype");
        Directory.CreateDirectory(AtlasFolder);
        int baked = 0, skipped = 0;
        try
        {
            for (int gi = 0; gi < guids.Length; gi++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[gi]);
                var proto = AssetDatabase.LoadAssetAtPath<ScatterPrototype>(path);
                if (proto == null) continue;
                EditorUtility.DisplayProgressBar("Baking impostor atlases", proto.name, (float)gi / Mathf.Max(1, guids.Length));

                var meshes = new List<Mesh>();
                var mats = new List<Material>();
                CollectImpostorParts(proto, meshes, mats);
                // The runtime decides which prototypes draw a card; asking it here is what keeps the baked set
                // and the drawn set the same. A local copy of the rule is how ground clutter ended up with a
                // hard cull edge and no atlas to bake into.
                if (meshes.Count == 0 || !ScatterPrototypeDto.From(proto).HasImpostor) { skipped++; continue; }

                ScatterImpostorBaker.AtlasCard card = ScatterImpostorBaker.BakeAtlas(meshes, mats, OctGridN);
                if (!card.Valid)
                {
                    if (card.Texture != null) Object.DestroyImmediate(card.Texture);
                    Debug.LogWarning($"[Impostor bake] '{proto.name}' keyed too little silhouette; left to bake at runtime.");
                    skipped++;
                    continue;
                }

                string atlasPath = $"{AtlasFolder}/{proto.name}_impostor.png";
                File.WriteAllBytes(atlasPath, ImageConversion.EncodeToPNG(card.Texture));
                Object.DestroyImmediate(card.Texture);
                AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
                ConfigureAtlasImport(atlasPath);

                proto.BakedImpostorAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);

                if (card.NormalTexture != null)
                {
                    string normalPath = $"{AtlasFolder}/{proto.name}_impostor_n.png";
                    File.WriteAllBytes(normalPath, ImageConversion.EncodeToPNG(card.NormalTexture));
                    Object.DestroyImmediate(card.NormalTexture);
                    AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceUpdate);
                    ConfigureAtlasImport(normalPath, isNormal: true);
                    proto.BakedImpostorNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                }
                EditorUtility.SetDirty(proto);
                baked++;
            }
            AssetDatabase.SaveAssets();
        }
        finally { EditorUtility.ClearProgressBar(); }

        Debug.Log($"[Impostor bake] baked {baked}, skipped {skipped} (no impostor tier / empty bake). " +
                  $"Atlases in {AtlasFolder}. Stop and re-enter Play to see the stored atlases used (no on-load bake).");
    }

    // Changing ScatterImpostorFactory.CoveragePreserveReference invalidates every atlas's preserve-coverage
    // reference, which is a two-minute reimport rather than a fifteen-minute re-bake: the pixels are already
    // correct. The shader's CardAlphaCutoff is a runtime value and needs neither.
    [MenuItem("Tools/ProceduralPlanets/Impostors/Reimport Atlas Settings", false, 21)]
    public static void ReimportAtlasSettings()
    {
        int count = 0;
        try
        {
            foreach (string dir in AtlasFolders)
            {
                if (!Directory.Exists(dir)) continue;
                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { dir });
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    EditorUtility.DisplayProgressBar("Reimporting atlases", path, (float)i / guids.Length);
                    ConfigureAtlasImport(path, isNormal: path.EndsWith("_n.png"));
                    count++;
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        Debug.Log($"[Impostor bake] reimported {count} atlas texture(s) preserving coverage at {ScatterImpostorFactory.CoveragePreserveReference:0.00}.");
    }

    [MenuItem("Tools/ProceduralPlanets/Impostors/Clear Source Library Atlases", false, 22)]
    public static void ClearAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:ScatterPrototype");
        int cleared = 0;
        foreach (string g in guids)
        {
            var proto = AssetDatabase.LoadAssetAtPath<ScatterPrototype>(AssetDatabase.GUIDToAssetPath(g));
            if (proto != null && (proto.BakedImpostorAtlas != null || proto.BakedImpostorNormal != null))
            {
                proto.BakedImpostorAtlas = null;
                proto.BakedImpostorNormal = null;
                EditorUtility.SetDirty(proto);
                cleared++;
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Impostor bake] cleared {cleared} atlas references (prototypes now bake at runtime). " +
                  $"The .png files under {AtlasFolder} are left on disk.");
    }

    static void CollectImpostorParts(ScatterPrototype proto, List<Mesh> meshes, List<Material> mats)
    {
        if (proto.Parts != null && proto.Parts.Length > 0)
        {
            foreach (ScatterPart part in proto.Parts)
            {
                if (part == null || part.Material == null || part.LodMeshes == null
                    || part.LodMeshes.Length == 0 || part.LodMeshes[0] == null) continue;
                meshes.Add(part.LodMeshes[0]);
                mats.Add(part.Material);
            }
        }
        else if (proto.Material != null && proto.LodMeshes != null && proto.LodMeshes.Length > 0 && proto.LodMeshes[0] != null)
        {
            meshes.Add(proto.LodMeshes[0]);
            mats.Add(proto.Material);
        }
    }

    // internal: the generated-prop bake tool writes atlases too and must import them identically. Two copies
    // of these settings is exactly how the mip fix would get half-applied again.
    internal static void ConfigureAtlasImport(string atlasPath, bool isNormal = false)
    {
        if (AssetImporter.GetAtPath(atlasPath) is not TextureImporter imp) return;
        imp.textureType = TextureImporterType.Default;
        imp.alphaSource = TextureImporterAlphaSource.FromInput;
        imp.alphaIsTransparency = true;
        imp.sRGBTexture = !isNormal; // normals are linear data, not colour
        // Mips are REQUIRED here, not optional. A tree-line impostor covers a few screen pixels while its atlas
        // cell is 128px, so an unmipped card is minified ~16x and point-samples near-randomly; against the
        // shader's hard clip(card.a - _Cutoff) that noise becomes binary keep/discard, which is the speckled
        // horizon. The old setting disabled them to avoid cells bleeding into each other, but that only happens
        // in the deepest mips, by which point the whole card is a couple of pixels wide.
        imp.mipmapEnabled = true;
        // Averaging alpha down a mip chain thins a silhouette until it falls under the cutoff and dissolves.
        // Preserve-coverage rescales each mip's alpha to hold the same clipped area, at this shader's cutoff.
        imp.mipMapsPreserveCoverage = true;
        imp.alphaTestReferenceValue = ScatterImpostorFactory.CoveragePreserveReference;
        // Box averaging is what makes a card read as a smooth blob next to a mesh whose canopy is still
        // ragged at the same size. Kaiser keeps more of the high frequency, and measured across all 61 cards
        // at the 24 px size the card takes over at, it also holds MORE of the silhouette: the thinnest card
        // goes 0.71 -> 0.74 of its mesh area, the median 0.894 -> 0.898, and the reeds stop overshooting
        // (1.27 -> 1.24). Change this and every prebaked atlas needs a reimport, not just a rebake.
        imp.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Trilinear; // crossfade between mips instead of stepping between them
        imp.anisoLevel = 4;                    // cards are seen at a grazing angle across the tree line
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
    }

    // Best-effort: freeze the sun at local noon so every atlas bakes under the same overhead light. Reflected
    // so this editor-only tool needs no reference to the runtime console assembly.
    static void FreezeLocalNoon()
    {
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb == null || mb.GetType().Name != "ConsoleController") continue;
            var run = mb.GetType().GetMethod("RunCommand");
            if (run == null) return;
            run.Invoke(mb, new object[] { "time.set-local 0.5" });
            run.Invoke(mb, new object[] { "time.freeze true" });
            return;
        }
    }
}
