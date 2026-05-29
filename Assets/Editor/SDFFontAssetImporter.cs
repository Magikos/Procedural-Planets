#if UNITY_EDITOR
// ============================================================================
//  SDFFontAssetImporter
//  Custom Inspector for SDFFontAsset that adds a one-click JSON import button.
//
//  How to use
//  ----------
//  1.  Run msdf-atlas-gen to produce a PNG atlas and a JSON metrics file:
//
//        msdf-atlas-gen -font MyFont.ttf -chars "[0x20, 0x7E]" \
//                       -size 48 -pxrange 4 \
//                       -format png -imageout Assets/Graphics/Fonts/MyFont_Atlas.png \
//                       -json Assets/Graphics/Fonts/MyFont_Atlas.json
//
//      Recommended settings:
//        -size 48        — atlas glyph cell size in pixels (larger = sharper at big sizes)
//        -pxrange 4      — SDF spread; must match SDFFontAsset.PixelRange
//        -chars "[0x20, 0x7E]"  — printable ASCII; extend as needed
//
//  2.  In Unity, right-click → Create → ProceduralPlanets → SDF Font Asset.
//
//  3.  Drag the generated PNG onto the Atlas field.
//      Import settings for the texture:
//        • Texture Type  = Default  (NOT Sprite)
//        • sRGB (Color Texture) = OFF  (the MSDF data is linear)
//        • Compression = None  (or at most BC7; DXT1/5 can corrupt SDF data)
//        • Generate Mip Maps = ON
//
//  4.  Select the SDFFontAsset and click "Import msdf-atlas-gen JSON...".
//      Browse to the .json file and confirm.  All glyph data is filled in.
//
//  5.  Place the asset in Assets/Resources/ and name it "DefaultFont" so that
//      LoadingManager can find it via Resources.Load<SDFFontAsset>("DefaultFont").
//
//  JSON schema supported (msdf-atlas-gen v1.x)
//  --------------------------------------------
//  {
//    "atlas":   { "type":"msdf", "distanceRange":4, "size":48,
//                 "width":512, "height":512, "yOrigin":"bottom" },
//    "metrics": { "emSize":1, "lineHeight":1.1, "ascender":0.75, "descender":-0.25 },
//    "glyphs":  [
//      { "unicode":65, "advance":0.722,
//        "planeBounds": {"left":0.013,"bottom":-0.013,"right":0.709,"top":0.685},
//        "atlasBounds": {"left":1.5,"bottom":1.5,"right":35.5,"top":34.5} }
//    ]
//  }
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(SDFFontAsset))]
public class SDFFontAssetImporter : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Import Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Import msdf-atlas-gen JSON…", GUILayout.Height(28)))
            ImportJson((SDFFontAsset)target);

        EditorGUILayout.HelpBox(
            "Generate the atlas with:\n" +
            "msdf-atlas-gen -font Font.ttf -chars \"[0x20, 0x7E]\" " +
            "-size 48 -pxrange 4 -format png -imageout Atlas.png -json Atlas.json",
            MessageType.Info);
    }

    // -------------------------------------------------------------------------
    // Import logic
    // -------------------------------------------------------------------------

    private static void ImportJson(SDFFontAsset asset)
    {
        string path = EditorUtility.OpenFilePanel(
            "Select msdf-atlas-gen JSON file", Application.dataPath, "json");

        if (string.IsNullOrEmpty(path)) return;

        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Read Error", ex.Message, "OK");
            return;
        }

        MsdfRoot data;
        try { data = JsonUtility.FromJson<MsdfRoot>(json); }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Parse Error", ex.Message, "OK");
            return;
        }

        if (data == null)
        {
            EditorUtility.DisplayDialog("Import Failed", "Could not parse JSON.", "OK");
            return;
        }

        Undo.RecordObject(asset, "Import MSDF JSON");

        // --- Atlas metadata ---
        int atlasW = 512;
        int atlasH = 512;
        bool yFromBottom = true;

        if (data.atlas != null)
        {
            asset.FontSize = data.atlas.size;
            asset.PixelRange = data.atlas.distanceRange;
            atlasW = Mathf.Max(1, data.atlas.width);
            atlasH = Mathf.Max(1, data.atlas.height);
            // msdf-atlas-gen default is "bottom"; old versions may use "top".
            yFromBottom = data.atlas.yOrigin != "top";
        }

        // --- Typographic metrics ---
        if (data.metrics != null)
        {
            asset.LineHeight = data.metrics.lineHeight;
            asset.Ascender = data.metrics.ascender;
            asset.Descender = data.metrics.descender;
        }

        // --- Per-glyph data ---
        if (data.glyphs == null || data.glyphs.Length == 0)
        {
            EditorUtility.DisplayDialog("Import Failed", "JSON contains no glyphs.", "OK");
            return;
        }

        var glyphs = new List<SDFGlyph>(data.glyphs.Length);
        foreach (var g in data.glyphs)
        {
            var glyph = new SDFGlyph
            {
                Unicode = g.unicode,
                Advance = g.advance,
            };

            if (g.planeBounds != null)
            {
                glyph.PlaneX = g.planeBounds.left;
                glyph.PlaneY = g.planeBounds.bottom;
                glyph.PlaneW = g.planeBounds.right - g.planeBounds.left;
                glyph.PlaneH = g.planeBounds.top - g.planeBounds.bottom;
            }

            if (g.atlasBounds != null)
            {
                // Convert atlas pixel coords to normalised UVs.
                // yFromBottom == true  → v=0 is image bottom (Unity/GL default) — no flip needed.
                // yFromBottom == false → v=0 is image top  — flip vertical.

                float px0 = g.atlasBounds.left;
                float px1 = g.atlasBounds.right;
                float py0, py1;

                if (yFromBottom)
                {
                    py0 = g.atlasBounds.bottom;
                    py1 = g.atlasBounds.top;
                }
                else
                {
                    // Flip: image-top pixel row → UV bottom
                    py0 = atlasH - g.atlasBounds.top;
                    py1 = atlasH - g.atlasBounds.bottom;
                }

                glyph.AtlasX = px0 / atlasW;
                glyph.AtlasY = py0 / atlasH;
                glyph.AtlasW = (px1 - px0) / atlasW;
                glyph.AtlasH = (py1 - py0) / atlasH;
            }

            glyphs.Add(glyph);
        }

        asset.Glyphs = glyphs.ToArray();

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog(
            "Import Complete",
            $"Imported {glyphs.Count} glyphs from '{Path.GetFileName(path)}'.\n\n" +
            "Remember to:\n" +
            "• Assign the atlas PNG to the Atlas field.\n" +
            "• Set the texture's sRGB to OFF and Compression to None.\n" +
            "• Place this asset in Assets/Resources/ named 'DefaultFont'.",
            "OK");
    }

    // -------------------------------------------------------------------------
    // JSON schema mirror classes (JsonUtility requires [Serializable] + public fields)
    // -------------------------------------------------------------------------

    [Serializable] private class MsdfRoot { public MsdfAtlas atlas; public MsdfMetrics metrics; public MsdfGlyph[] glyphs; }
    [Serializable] private class MsdfAtlas { public string type; public float distanceRange; public float size; public int width; public int height; public string yOrigin; }
    [Serializable] private class MsdfMetrics { public float emSize; public float lineHeight; public float ascender; public float descender; }
    [Serializable] private class MsdfGlyph { public int unicode; public float advance; public MsdfBounds planeBounds; public MsdfBounds atlasBounds; }
    [Serializable] private class MsdfBounds { public float left, bottom, right, top; }
}
#endif
