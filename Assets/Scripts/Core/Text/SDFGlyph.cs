using System;

/// <summary>
/// Per-glyph metrics produced by msdf-atlas-gen and stored inside an SDFFontAsset.
///
/// Coordinate conventions
/// ----------------------
/// All plane (quad) values are in EM units where 1 em = the font size used during
/// atlas generation.  Origin is the glyph's drawing cursor on the baseline.
///
///   PlaneX / PlaneY  — left / bottom edge of the quad relative to the cursor.
///                      PlaneY is negative for descenders (e.g. 'g', 'p').
///   PlaneW / PlaneH  — width / height of the quad in em units.
///   Advance          — how far to move the cursor after drawing this glyph.
///
/// Atlas UV values are pre-normalised to [0..1] during import (see SDFFontAssetImporter).
/// v=0 is the bottom of the texture (OpenGL / Unity convention).
/// </summary>
[Serializable]
public struct SDFGlyph
{
    /// <summary>Unicode code point (e.g. 65 = 'A').</summary>
    public int Unicode;

    /// <summary>Horizontal advance in em units.</summary>
    public float Advance;

    /// <summary>Quad left edge offset from the cursor, in em units.</summary>
    public float PlaneX;

    /// <summary>Quad bottom edge offset from the baseline, in em units (negative for descenders).</summary>
    public float PlaneY;

    /// <summary>Quad width in em units.</summary>
    public float PlaneW;

    /// <summary>Quad height in em units.</summary>
    public float PlaneH;

    /// <summary>Atlas UV left [0..1].</summary>
    public float AtlasX;

    /// <summary>Atlas UV bottom [0..1].</summary>
    public float AtlasY;

    /// <summary>Atlas UV width [0..1].</summary>
    public float AtlasW;

    /// <summary>Atlas UV height [0..1].</summary>
    public float AtlasH;
}
