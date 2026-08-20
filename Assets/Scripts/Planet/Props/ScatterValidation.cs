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
