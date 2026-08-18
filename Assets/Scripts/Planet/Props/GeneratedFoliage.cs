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
        Color tinted = tint * Mathf.Max(0.1f, lift);
        if (m.HasProperty("_SeasonColor")) m.SetColor("_SeasonColor", tinted);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tinted);
        // FoliageLit's wind is per-material sway in metres and defaults to 0, so a material built in code is
        // RIGID unless this is set — copying the base material does not carry an authored value across.
        if (windStrength >= 0f && m.HasProperty("_WindStrength")) m.SetFloat("_WindStrength", windStrength);
        m.enableInstancing = true;
        _leaves[key] = m;
        return m;
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
