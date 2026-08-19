using System.Collections.Generic;
using UnityEngine;

// Shared material factory for generated props. TreeInjection and PlantInjection both need "a solid tinted
// surface" (bark, stems) and "a tinted copy of a clean leaf texture" (canopies, blades), and two copies of
// that logic would drift — the leaf substitution in particular encodes which Synty textures our whole-card
// UVs can actually wear.
//
// Materials are cached by key and live for the domain: they are shared by every instance of a species, and
// re-creating one per prototype would multiply draw-call batches for no visual difference.
public static class GeneratedFoliage
{
    static Material _cleanLeafBase;
    static readonly Dictionary<string, Material> _leaves = new();
    static readonly Dictionary<string, Material> _solids = new();

    public static bool HasLeafBase => _cleanLeafBase != null;

    // The library's best clean single-leaf material, found once per rebuild by TreeInjection (which is the only
    // place that knows how to recognise one). Everything tinted downstream is a copy of this.
    public static void Prime(Material cleanLeafBase) => _cleanLeafBase = cleanLeafBase;

    // A tinted copy of the clean leaf texture. Returns null when the library had no usable leaf material, which
    // callers treat as "keep the Synty prop" rather than shipping an untextured blob.
    public static Material Leaf(string key, Color tint, float windStrength = -1f, float lift = 2f)
    {
        if (_cleanLeafBase == null) return null;
        if (_leaves.TryGetValue(key, out Material hit) && hit != null) return hit;

        var m = new Material(_cleanLeafBase) { name = $"Gen {key} leaf" };
        // Scatter/FoliageLit has NO _BaseColor — its leaf tint knob is _SeasonColor, which multiplies the leaf
        // albedo. Setting _BaseColor on it is a silent no-op behind a HasProperty guard, which is exactly how
        // three separate passes at the plant palette changed nothing on screen. Set whichever the material
        // actually exposes.
        Color tinted = Normalise(tint * Mathf.Max(0.1f, lift));
        if (m.HasProperty("_SeasonColor")) m.SetColor("_SeasonColor", tinted);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tinted);
        // FoliageLit's wind is per-material sway in metres and defaults to 0, so a material built in code is
        // RIGID unless this is set — copying the base material does not carry an authored value across.
        if (windStrength >= 0f && m.HasProperty("_WindStrength")) m.SetFloat("_WindStrength", windStrength);
        m.enableInstancing = true;
        _leaves[key] = m;
        return m;
    }

    // Cap an albedo multiplier at 1 while keeping its hue.
    //
    // _SeasonColor MULTIPLIES the leaf albedo, so a value above 1 is brighter than white and behaves like a
    // weak emissive: fine in daylight, but at night it is the only lit thing in frame and the props read as
    // glowing. The lift that stops dark textures reading black pushed bright tints to nearly 2x white — the
    // flowers were the worst at 1.98 — which is what lit up a dark forest floor.
    public static Color Normalise(Color c)
    {
        float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return peak > 1f ? new Color(c.r / peak, c.g / peak, c.b / peak, c.a) : c;
    }

    // Flat vertex-lit surface for stems and bark.
    public static Material Solid(string key, Color tint)
    {
        if (_solids.TryGetValue(key, out Material hit) && hit != null) return hit;

        Shader sh = Shader.Find("Scatter/VertexColorLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh) { name = $"Gen {key}" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
        m.enableInstancing = true;
        _solids[key] = m;
        return m;
    }
}
