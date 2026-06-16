using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A ScriptableObject that holds an MSDF atlas texture and the per-glyph metrics
/// needed to render text with the Hidden/SDFText shader.
///
/// How to create one
/// -----------------
/// 1. In the Project window: right-click → Create → ProceduralPlanets → SDF Font Asset.
/// 2. Select the new asset.
/// 3. In the Inspector click "Import msdf-atlas-gen JSON..." and point it at the
///    .json file output by msdf-atlas-gen.
/// 4. Assign the matching PNG to the Atlas field.
///    (Set the texture's Format to RGBA 32-bit, sRGB = OFF, no compression.)
/// 5. Place the asset in Assets/Resources/ so LoadingManager can find it at runtime
///    via Resources.Load&lt;SDFFontAsset&gt;("DefaultFont").
///
/// Generating the atlas (msdf-atlas-gen must be on PATH):
///   msdf-atlas-gen -font MyFont.ttf -chars "[0x20, 0x7E]" -size 48 -pxrange 4
///                  -format png -imageout Atlas.png -json Atlas.json
///
/// The -pxrange value must match the PixelRange field below.
/// A size of 48 and pxrange of 4 work well for most UI text sizes.
/// </summary>
[CreateAssetMenu(fileName = "NewSDFFontAsset", menuName = "ProceduralPlanets/SDF Font Asset")]
public class SDFFontAsset : ScriptableObject
{
    [Tooltip("MSDF atlas texture. Texture Type = Default, sRGB = off, no compression.")]
    public Texture2D Atlas;

    [Tooltip("Font size in pixels used when generating the atlas (-size argument).")]
    public float FontSize = 48f;

    [Tooltip("SDF pixel range used when generating the atlas (-pxrange argument, typically 4).")]
    public float PixelRange = 4f;

    [Tooltip("Line height as a multiple of em size (from the metrics section of the JSON).")]
    public float LineHeight = 1.2f;

    [Tooltip("Ascender height in em units (from the JSON metrics).")]
    public float Ascender = 0.75f;

    [Tooltip("Descender in em units, negative (from the JSON metrics).")]
    public float Descender = -0.25f;

    [Tooltip("All glyphs in this font. Populated automatically by the JSON importer.")]
    public SDFGlyph[] Glyphs;

    // -------------------------------------------------------------------------
    // Runtime lookup
    // -------------------------------------------------------------------------

    private Dictionary<int, int> _glyphIndex;

    /// <summary>Looks up the glyph for character <paramref name="c"/>.</summary>
    public bool TryGetGlyph(char c, out SDFGlyph glyph)
    {
        if (_glyphIndex == null) BuildIndex();

        if (_glyphIndex.TryGetValue((int)c, out int idx))
        {
            glyph = Glyphs[idx];
            return true;
        }

        glyph = default;
        return false;
    }

    private void BuildIndex()
    {
        _glyphIndex = new Dictionary<int, int>();
        if (Glyphs == null) return;
        for (int i = 0; i < Glyphs.Length; i++)
        {
            int unicode = Glyphs[i].Unicode;
            if (_glyphIndex.ContainsKey(unicode))
                LoggerProvider.Get().Log(LogLevel.Warning, "SDFFontAsset", $"Duplicate unicode U+{unicode:X4} in '{name}' at index {i} — previous entry overwritten.");
            _glyphIndex[unicode] = i;
        }
    }

    private void OnEnable() => BuildIndex();
    private void OnValidate() => BuildIndex();
}
