using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a Unity Mesh whose quads render a text string using the Hidden/SDFText shader.
///
/// Coordinate space
/// ----------------
/// All vertex positions are in **normalised screen space**:
///   (0, 0) = bottom-left of screen
///   (1, 1) = top-right of screen
///
/// The SDFText vertex shader converts these directly to clip space, so the mesh
/// can be drawn with Matrix4x4.identity and no special camera/projection setup.
///
/// For world-surface text, use the world-space overload (originPos / right / up).
/// </summary>
public static class SDFTextMeshBuilder
{
    // -------------------------------------------------------------------------
    // Screen-space build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a screen-space text mesh.
    /// </summary>
    /// <param name="text">String to render. '\n' is supported.</param>
    /// <param name="font">Font asset providing the MSDF atlas and glyph metrics.</param>
    /// <param name="originX">Baseline start X in normalised screen space [0..1].</param>
    /// <param name="originY">Baseline Y in normalised screen space [0..1].</param>
    /// <param name="emSize">
    ///   Height of 1 em in normalised screen units.
    ///   Example: 0.04 = 4 % of the screen height.
    /// </param>
    /// <param name="color">Per-vertex colour (alpha is usually 1; use the material for fading).</param>
    /// <param name="alignment">Horizontal alignment of each line.</param>
    /// <returns>
    ///   A <see cref="Mesh"/> with <see cref="HideFlags.HideAndDontSave"/>, or
    ///   <c>null</c> if there is nothing renderable.
    ///   The caller is responsible for destroying it when done.
    /// </returns>
    public static Mesh BuildScreen(
        string text,
        SDFFontAsset font,
        float originX,
        float originY,
        float emSize,
        Color color,
        TextAnchor alignment = TextAnchor.UpperLeft)
    {
        if (string.IsNullOrEmpty(text) || font?.Glyphs == null) return null;

        // Apply aspect-ratio correction so glyphs appear square.
        // emSize is defined as a fraction of screen HEIGHT; the x axis needs a
        // different scale because one x-unit spans the full screen WIDTH.
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1f;
        float emSizeX = emSize / aspect;   // em in normalised-x units
        float emSizeY = emSize;            // em in normalised-y units

        // Split into lines so we can measure and align each one.
        var lines = text.Split('\n');

        var verts = new List<Vector3>(text.Length * 4);
        var uvs = new List<Vector2>(text.Length * 4);
        var cols = new List<Color>(text.Length * 4);
        var tris = new List<int>(text.Length * 6);

        float cy = originY;

        for (int l = 0; l < lines.Length; l++)
        {
            string line = lines[l];

            // Compute line width so we can apply horizontal alignment.
            float lineWidth = MeasureWidth(line, font) * emSizeX;

            float cx = alignment switch
            {
                TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter
                    => originX - lineWidth * 0.5f,
                TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight
                    => originX - lineWidth,
                _ => originX,
            };

            AppendLine(line, font, cx, cy, emSizeX, emSizeY, color, verts, uvs, cols, tris);

            cy -= font.LineHeight * emSizeY;
        }

        if (verts.Count == 0) return null;

        var mesh = new Mesh { name = "SDFText", hideFlags = HideFlags.HideAndDontSave };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // -------------------------------------------------------------------------
    // Width measurement
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the total advance of <paramref name="text"/> in em units
    /// (multiply by emSize to get screen units).
    /// </summary>
    public static float MeasureWidth(string text, SDFFontAsset font)
    {
        if (string.IsNullOrEmpty(text) || font == null) return 0f;

        float width = 0f;
        foreach (char c in text)
        {
            if (c == '\n') break;
            width += font.TryGetGlyph(c, out SDFGlyph g) ? g.Advance : 0.5f;
        }
        return width;
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private static void AppendLine(
        string line,
        SDFFontAsset font,
        float cx,
        float cy,
        float emSizeX,
        float emSizeY,
        Color color,
        List<Vector3> verts,
        List<Vector2> uvs,
        List<Color> cols,
        List<int> tris)
    {
        foreach (char c in line)
        {
            if (!font.TryGetGlyph(c, out SDFGlyph g))
            {
                cx += 0.5f * emSizeX;   // unknown glyph: skip with default advance
                continue;
            }

            // Only emit geometry if this glyph has a visible quad.
            // Space, thin non-printing chars, etc. have PlaneW/H ≈ 0.
            if (g.PlaneW > 0.0001f && g.PlaneH > 0.0001f)
            {
                float x0 = cx + g.PlaneX * emSizeX;
                float y0 = cy + g.PlaneY * emSizeY;
                float x1 = cx + (g.PlaneX + g.PlaneW) * emSizeX;
                float y1 = cy + (g.PlaneY + g.PlaneH) * emSizeY;

                float u0 = g.AtlasX;
                float v0 = g.AtlasY;
                float u1 = g.AtlasX + g.AtlasW;
                float v1 = g.AtlasY + g.AtlasH;

                int b = verts.Count;

                verts.Add(new Vector3(x0, y0, 0)); uvs.Add(new Vector2(u0, v0)); cols.Add(color);  // BL
                verts.Add(new Vector3(x1, y0, 0)); uvs.Add(new Vector2(u1, v0)); cols.Add(color);  // BR
                verts.Add(new Vector3(x1, y1, 0)); uvs.Add(new Vector2(u1, v1)); cols.Add(color);  // TR
                verts.Add(new Vector3(x0, y1, 0)); uvs.Add(new Vector2(u0, v1)); cols.Add(color);  // TL

                // Two CCW triangles: BL→TR→BR and BL→TL→TR
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 0); tris.Add(b + 3); tris.Add(b + 2);
            }

            cx += g.Advance * emSizeX;
        }
    }
}
