using System.Collections.Generic;
using UnityEngine;

// Editor-only sanity pass over the FINAL scatter library, run once per injection so problems announce
// themselves instead of waiting to be spotted in a screenshot.
//
// Every check here exists because the bug it catches actually shipped and was found by eye:
//  - an impostor key with no baked card silently live-bakes, costing seconds of load and saying nothing;
//  - a leaf tint above 1 is an albedo brighter than white, invisible by day and glowing at night;
//  - a prototype that renders nothing is invisible by definition.
// None of these throw, all of them look plausible in the editor, and that is exactly why they need a
// deliberate check rather than a review pass.
public static class ScatterValidation
{
    // Above this an albedo multiplier is brighter than white and reads as self-lit once the sun is down.
    // Slightly over 1 is tolerated because authored Synty canopies sit a little hot on purpose.
    const float AlbedoWarnAbove = 1.05f;

    // Set by the impostor bake tool while it rebuilds. The tool deliberately clears every baked card so it can
    // bake from a clean library, which would otherwise make this warn about all 52 keys during the very action
    // that fixes them. A check that cries wolf while you are fixing it is a check you learn to ignore.
    public static bool Suppressed;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Run(ScatterLibraryDto lib)
    {
        if (Suppressed || lib?.Prototypes == null) return;
        ReportImpostorCoverage(lib);
        ReportOverbrightMaterials(lib);
        ReportUndrawable(lib);
        ReportLodDiscontinuity(lib);
    }

    // A far LOD that is a different SIZE from the near one pops as you cross the band. Found this way: the
    // generated rocks rebuilt their coarse shell from the same displacement field at fewer vertices, missed the
    // outward bumps, and came out 17% smaller — a boulder that shrank when you walked away from it.
    static void ReportLodDiscontinuity(ScatterLibraryDto lib)
    {
        // 18%, not 12%: the foliage mesher DELIBERATELY builds far-LOD leaves 15% larger to hold canopy
        // coverage as detail drops, which shows up as a 1.15 bounds ratio on species whose leaves are the
        // silhouette (palms). Flagging that every load would be noise, and a check that cries wolf gets
        // ignored — this still catches the rock case, which was 0.83.
        const float Tolerance = 0.18f;
        var bad = new List<string>();
        foreach (ScatterPrototypeDto p in lib.Prototypes)
        {
            if (p?.Parts == null) continue;
            foreach (ScatterPartDto part in p.Parts)
            {
                if (part?.LodMeshes == null || part.LodMeshes.Length < 2) continue;
                for (int i = 1; i < part.LodMeshes.Length; i++)
                {
                    Mesh near = part.LodMeshes[i - 1], far = part.LodMeshes[i];
                    if (near == null || far == null) continue;
                    float a = near.bounds.extents.magnitude, b = far.bounds.extents.magnitude;
                    if (a < 1e-4f) continue;
                    float ratio = b / a;
                    if (Mathf.Abs(ratio - 1f) > Tolerance)
                    { bad.Add($"{p.DisplayName} lod{i} x{ratio:0.00}"); break; }
                    // A far LOD heavier than the near one is the LOD chain built backwards.
                    if (far.vertexCount > near.vertexCount)
                    { bad.Add($"{p.DisplayName} lod{i} is HEAVIER than lod{i - 1}"); break; }
                }
            }
        }
        if (bad.Count == 0) return;
        LoggerProvider.Log(LogLevel.Warning, "ScatterCheck",
            $"{bad.Count} prototype(s) change size or cost across an LOD band, which pops as you cross it: " +
            string.Join(", ", bad.GetRange(0, Mathf.Min(bad.Count, 12))) +
            (bad.Count > 12 ? $" (+{bad.Count - 12} more)" : ""));
    }

    // Keys that will bake at load. Correct but slow, and utterly silent without this.
    static void ReportImpostorCoverage(ScatterLibraryDto lib)
    {
        var missed = new SortedSet<string>();
        int cached = 0;
        foreach (ScatterPrototypeDto p in lib.Prototypes)
        {
            if (p == null || !p.HasImpostor || string.IsNullOrEmpty(p.ImpostorShareKey)) continue;
            if (p.BakedImpostorAtlas != null) cached++;
            else missed.Add(p.ImpostorShareKey);
        }
        if (missed.Count == 0)
        {
            LoggerProvider.Log(LogLevel.Debug, "ScatterCheck", $"All {cached} impostor prototype(s) read a baked card.");
            return;
        }
        // MEASURED 418 ms per key, so the cost estimate is honest rather than a vague "slower".
        LoggerProvider.Log(LogLevel.Warning, "ScatterCheck",
            $"{missed.Count} impostor key(s) have no baked card and will bake at load " +
            $"(about {missed.Count * 0.42f:0.0} s): {string.Join(", ", missed)}. " +
            "Fix with Tools > ProceduralPlanets > Impostors > Bake Impostors (Generated Props), from play mode.");
    }

    // Albedo above white. Twice now this shipped as a "glowing props" report from a night screenshot.
    static void ReportOverbrightMaterials(ScatterLibraryDto lib)
    {
        var hot = new SortedDictionary<string, float>();
        var seen = new HashSet<Material>();
        foreach (ScatterPrototypeDto p in lib.Prototypes)
        {
            if (p?.Parts == null) continue;
            foreach (ScatterPartDto part in p.Parts)
            {
                Material m = part?.Material;
                if (m == null || !seen.Add(m)) continue;
                float peak = Mathf.Max(Peak(m, "_SeasonColor"), Peak(m, "_BaseColor"));
                if (peak > AlbedoWarnAbove) hot[m.name] = peak;
            }
        }
        if (hot.Count == 0) return;

        var parts = new List<string>();
        foreach (KeyValuePair<string, float> kv in hot) parts.Add($"{kv.Key} ({kv.Value:0.00})");
        LoggerProvider.Log(LogLevel.Warning, "ScatterCheck",
            $"{hot.Count} scatter material(s) have an albedo tint above {AlbedoWarnAbove:0.00}, which is brighter " +
            $"than white and reads as self-lit at night: {string.Join(", ", parts)}.");
    }

    static float Peak(Material m, string prop)
    {
        if (!m.HasProperty(prop)) return 0f;
        Color c = m.GetColor(prop);
        return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
    }

    // A prototype that draws nothing is invisible by definition, so nobody reports it as broken - they just
    // never see the prop and assume that biome has none.
    static void ReportUndrawable(ScatterLibraryDto lib)
    {
        var broken = new List<string>();
        foreach (ScatterPrototypeDto p in lib.Prototypes)
        {
            if (p == null) continue;
            bool anyDrawable = false;
            if (p.Parts != null)
                foreach (ScatterPartDto part in p.Parts)
                    if (part != null && part.CanRender) { anyDrawable = true; break; }
            if (!anyDrawable) broken.Add(p.DisplayName ?? "(unnamed)");
        }
        if (broken.Count == 0) return;
        LoggerProvider.Log(LogLevel.Warning, "ScatterCheck",
            $"{broken.Count} prototype(s) have no drawable part and will render nothing: {string.Join(", ", broken)}.");
    }
}
