using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Dev tool: bakes every ScatterLibrary prototype's far-field impostor card into one contact sheet and
// flags any that bake empty (mesh-only fallback). Run after importing/adding props to eyeball that each
// impostor silhouette is clean (no atlas leaks, right aspect) before wiring it onto the planet.
public static class ScatterImpostorContactSheetTool
{
    const int Cols = 7;
    const int Cell = 160;

    [MenuItem("Planet/Scatter/Bake Impostor Contact Sheet")]
    public static void BakeContactSheet()
    {
        ScatterLibrary lib = FindLibrary();
        if (lib == null) { Debug.LogWarning("[Impostor] No ScatterLibrary asset found."); return; }

        ScatterLibraryDto dto = ScatterLibraryDto.From(lib);
        int n = dto.Prototypes.Length;
        if (n == 0) { Debug.LogWarning("[Impostor] ScatterLibrary has no prototypes."); return; }

        int rows = (n + Cols - 1) / Cols;
        var sheet = new Texture2D(Cols * Cell, rows * Cell, TextureFormat.RGB24, false);
        Color bg = new Color(0.25f, 0.27f, 0.30f);
        FillSolid(sheet, bg);

        var meshOnly = new List<string>();
        try
        {
            for (int i = 0; i < n; i++)
            {
                ScatterPrototypeDto p = dto.Prototypes[i];
                var meshes = new List<Mesh>();
                var materials = new List<Material>();
                foreach (ScatterPartDto part in p.Parts)
                {
                    if (part.LodMeshes.Length == 0 || part.LodMeshes[0] == null) continue;
                    meshes.Add(part.LodMeshes[0]);
                    materials.Add(part.Material);
                }
                if (meshes.Count == 0) { meshOnly.Add($"{i} {p.DisplayName} (no mesh)"); continue; }

                ScatterImpostorBaker.Card card = ScatterImpostorBaker.Bake(meshes, materials);
                if (!card.Valid) meshOnly.Add($"{i} {p.DisplayName} (empty bake)");
                Composite(sheet, card.Texture, i);
                Object.DestroyImmediate(card.Texture);
            }
            sheet.Apply();

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ScatterImpostorContactSheet.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Debug.Log($"[Impostor] Contact sheet ({n} prototypes) written to {path}");
            if (meshOnly.Count > 0)
                Debug.LogWarning("[Impostor] Mesh-only (no impostor tier): " + string.Join(", ", meshOnly));
            EditorUtility.RevealInFinder(path);
        }
        finally { Object.DestroyImmediate(sheet); }
    }

    static ScatterLibrary FindLibrary()
    {
        string[] guids = AssetDatabase.FindAssets("t:ScatterLibrary");
        return guids.Length == 0
            ? null
            : AssetDatabase.LoadAssetAtPath<ScatterLibrary>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    static void FillSolid(Texture2D t, Color c)
    {
        var px = new Color[t.width * t.height];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px);
    }

    static void Composite(Texture2D sheet, Texture2D card, int idx)
    {
        int cw = card.width, ch = card.height;
        Color[] cp = card.GetPixels();
        Color bg = new Color(0.25f, 0.27f, 0.30f);
        float f = Mathf.Min((float)(Cell - 10) / cw, (float)(Cell - 10) / ch);
        int dw = Mathf.RoundToInt(cw * f), dh = Mathf.RoundToInt(ch * f);
        int rows = sheet.height / Cell;
        int col = idx % Cols, row = idx / Cols;
        int ox = col * Cell + (Cell - dw) / 2;
        int oy = (rows - 1 - row) * Cell + (Cell - dh) / 2; // row 0 at top
        for (int py = 0; py < dh; py++)
            for (int px = 0; px < dw; px++)
            {
                int sx = Mathf.Clamp(Mathf.RoundToInt(px / f), 0, cw - 1);
                int sy = Mathf.Clamp(Mathf.RoundToInt(py / f), 0, ch - 1);
                Color c = cp[sy * cw + sx];
                sheet.SetPixel(ox + px, oy + py, Color.Lerp(bg, new Color(c.r, c.g, c.b), c.a));
            }
    }
}
