using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Deep check of the impostor pipeline, covering the things the runtime pass cannot see: texture import
// settings, manifest integrity, and whether a card actually bakes to a usable silhouette.
//
// The point is that none of these failures announce themselves. A wrong mip setting shows up as a speckled
// tree line, a stale manifest entry shows up as load time creeping back, an empty bake shows up as a prop
// that quietly is not there. All of them were found by staring at screenshots; all of them are mechanical
// to check. Run this instead of hoping to notice.
public static class ScatterImpostorValidator
{
    // Deliberately NOT the shader's _Cutoff. The card clips above what the mip chain preserves so bilinear
    // spread between rescaled texels cannot fatten the silhouette at range; see CardAlphaCutoff.
    const float PreserveReference = ScatterImpostorFactory.CoveragePreserveReference;
    const int ExpectedGridN = 8;       // ScatterImpostorFactory.OctGridN

    [MenuItem("Tools/ProceduralPlanets/Impostors/Validate", false, 30)]
    public static void Validate()
    {
        var problems = new List<string>();
        var notes = new List<string>();

        CheckShader(problems);
        CheckAtlasImportSettings(problems, notes);
        CheckManifest(problems, notes);
        CheckLibrary(problems, notes);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[ImpostorValidate] {problems.Count} problem(s), {notes.Count} note(s).");
        foreach (string n in notes) sb.AppendLine("  note: " + n);
        foreach (string p in problems) sb.AppendLine("  PROBLEM: " + p);

        if (problems.Count > 0) Debug.LogWarning(sb.ToString());
        else Debug.Log(sb.ToString());
    }

    static void CheckShader(List<string> problems)
    {
        // Resolved by name at runtime in ScatterImpostorFactory, so a rename breaks every impostor silently:
        // TryBuild returns default and the prop simply hard-culls at mesh range instead of billboarding.
        if (Shader.Find("Scatter/Impostor") == null)
            problems.Add("Shader 'Scatter/Impostor' not found. ScatterImpostorFactory resolves it by name, " +
                         "so every impostor would silently fall back to mesh-only.");
    }

    static void CheckAtlasImportSettings(List<string> problems, List<string> notes)
    {
        int checked_ = 0;
        foreach (string dir in ScatterImpostorBakeTool.AtlasFolders)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter imp) continue;
                checked_++;
                string file = Path.GetFileName(path);

                // Unmipped cards minify ~16x at the tree line and point-sample against a hard alpha clip,
                // which is the speckled horizon. Preserve-coverage stops the silhouette dissolving as the
                // mip chain averages its alpha down past the cutoff.
                if (!imp.mipmapEnabled) problems.Add($"{file}: mipmaps disabled (causes a speckled tree line).");
                else if (!imp.mipMapsPreserveCoverage) problems.Add($"{file}: mipMapsPreserveCoverage off (silhouette thins away in the distance).");
                else if (Mathf.Abs(imp.alphaTestReferenceValue - PreserveReference) > 0.01f)
                    problems.Add($"{file}: alphaTestReferenceValue {imp.alphaTestReferenceValue:0.00} does not match the preserve reference {PreserveReference:0.00}.");

                if (imp.filterMode != FilterMode.Trilinear)
                    notes.Add($"{file}: filterMode {imp.filterMode}, expected Trilinear (mip transitions will step).");

                if (imp.mipmapFilter != TextureImporterMipFilter.KaiserFilter)
                    notes.Add($"{file}: mipmapFilter {imp.mipmapFilter}, expected Kaiser (box averaging softens the card into a blob at the swap).");

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null && tex.width % ExpectedGridN != 0)
                    problems.Add($"{file}: {tex.width}px is not divisible by the {ExpectedGridN}x{ExpectedGridN} cell grid.");
            }
        }
        notes.Add($"{checked_} atlas texture(s) checked.");
    }

    static void CheckManifest(List<string> problems, List<string> notes)
    {
        var manifest = AssetDatabase.LoadAssetAtPath<GeneratedImpostorManifest>(
            "Assets/Resources/Settings/GeneratedImpostors.asset");
        if (manifest == null) { problems.Add("GeneratedImpostors manifest asset is missing; every generated key will bake at load."); return; }

        var keys = new HashSet<string>();
        foreach (GeneratedImpostorManifest.Entry e in manifest.Entries)
        {
            if (e == null) { problems.Add("Manifest contains a null entry."); continue; }
            // An empty hash can never match at load, so the entry looks like coverage but provides none.
            if (string.IsNullOrEmpty(e.ShapeHash)) problems.Add($"Manifest '{e.Key}' has an empty hash; it can never match and will bake at load.");
            if (e.Atlas == null) problems.Add($"Manifest '{e.Key}' has no atlas texture.");
            if (!keys.Add(e.Key)) problems.Add($"Manifest has a duplicate key '{e.Key}'; the first wins and the rest are dead.");
        }
        notes.Add($"{manifest.Entries.Length} manifest entry(ies) checked.");
    }

    static void CheckLibrary(List<string> problems, List<string> notes)
    {
        ScatterLibraryDto lib = TreeInjection.Rebuild();
        if (lib?.Prototypes == null) { problems.Add("Scatter library failed to build."); return; }

        int impostors = 0, cached = 0;
        var live = new SortedSet<string>();
        foreach (ScatterPrototypeDto p in lib.Prototypes)
        {
            if (p == null) continue;
            if (!p.HasImpostor) continue;
            impostors++;
            if (p.BakedImpostorAtlas != null) cached++;
            else live.Add(string.IsNullOrEmpty(p.ImpostorShareKey) ? (p.DisplayName ?? "(unnamed)") : p.ImpostorShareKey);

            // A card baked from a differently-sized mesh billboards at the wrong scale, which reads as a prop
            // that changes size as the impostor takes over.
            if (p.BakedImpostorAtlas != null && p.BakedImpostorNormal == null)
                notes.Add($"'{p.DisplayName}' has an albedo atlas but no normal atlas; the far card will shade flat.");
        }
        if (live.Count > 0)
            problems.Add($"{live.Count} impostor key(s) will bake at load (about {live.Count * 0.42f:0.0} s): {string.Join(", ", live)}.");
        notes.Add($"{cached}/{impostors} impostor prototype(s) read a baked card.");
    }
}
