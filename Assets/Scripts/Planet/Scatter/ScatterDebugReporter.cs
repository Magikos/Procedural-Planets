using System.Collections.Generic;
using System.Text;
using UnityEngine;

// What scatter writes into the F10 capture sidecar.
//
// Reads only the library DTO, so it costs nothing and cannot fail: no gather, no instance walk, no GPU query.
// The band table plus the distance visible in the shot is enough to place any prop in its LOD, which is the
// question a "this looks odd" screenshot always raises and previously could not answer.
// Registered through Planet.RegisterWorldServices, NOT from its own constructor: the scatter owners are
// created in Awake, before any world context exists, so registering here would throw during boot.
public sealed class ScatterDebugReporter : IScatterDebugReport
{

    public void Append(StringBuilder sb)
    {
        // A capture must never fail because of a debug reporter. SettingsProvider THROWS rather than returning
        // false when there is no active world context, so this is a real path, not defensive noise.
        try { AppendUnsafe(sb); }
        catch (System.Exception e) { sb.AppendLine($"Report unavailable: {e.GetType().Name}: {e.Message}"); }
    }

    void AppendUnsafe(StringBuilder sb)
    {
        ScatterLibraryDto lib = SettingsProvider.GetSettings<ScatterLibraryDto>();
        // Injection state matters first: a prop that looks wrong may simply be the untouched source asset
        // because its injector is switched off, which is invisible in a screenshot.
        sb.AppendLine($"Injection: trees={OnOff(TreeInjection.Enabled)}(x{TreeInjection.Variants} variants) " +
                      $"plants={OnOff(PlantInjection.Enabled)}(per-kind variants) " +
                      $"rocks={OnOff(RockInjection.Enabled)}(x{RockInjection.Variants} variants)");

        int impostors = 0, cached = 0, meshOnly = 0;
        var liveKeys = new SortedSet<string>();
        foreach (ScatterPrototypeDto p in lib.Prototypes)
        {
            if (p == null || !p.CanRender) continue;
            if (!p.HasImpostor) { meshOnly++; continue; }
            impostors++;
            if (p.BakedImpostorAtlas != null) cached++;
            else liveKeys.Add(p.DisplayName ?? "?");
        }
        sb.AppendLine($"Library: {lib.Prototypes.Length} prototypes, {impostors} with an impostor tier, " +
                      $"{meshOnly} mesh-only (those HARD CULL at mesh range instead of billboarding)");
        sb.AppendLine($"Impostor cards: {cached} from disk, {liveKeys.Count} baked at load" +
                      (liveKeys.Count > 0 ? $" [{string.Join(", ", liveKeys)}]" : ""));

        AppendLodBands(lib, sb);
    }

    // At what distance each prototype swaps mesh LOD, and where the impostor takes over. To diagnose "odd LOD
    // at roughly N metres", find the row whose band contains N. Grouped by identical layout so the table stays
    // a dozen lines instead of one per prototype.
    static void AppendLodBands(ScatterLibraryDto lib, StringBuilder sb)
    {
        var groups = new SortedDictionary<string, List<string>>();
        foreach (ScatterPrototypeDto p in lib.Prototypes)
        {
            if (p == null || !p.CanRender) continue;

            var bands = new List<string>();
            foreach (ScatterPartDto part in p.Parts)
            {
                if (part == null || !part.CanRender || part.LodEndDistances == null) continue;
                for (int i = 0; i < part.LodEndDistances.Length; i++)
                    bands.Add($"lod{i}<{part.LodEndDistances[i]:0}m");
                break; // parts share one distance chain; the first renderable is representative
            }
            string impostor = p.HasImpostor
                ? $"impostor {p.ImpostorStartDistance:0}..{p.ImpostorEndDistance:0}m"
                : "NO IMPOSTOR (hard cull)";

            string key = string.Join(" ", bands) + "  " + impostor;
            if (!groups.TryGetValue(key, out List<string> names)) groups[key] = names = new List<string>();
            names.Add(p.DisplayName ?? "?");
        }

        sb.AppendLine($"LOD bands ({groups.Count} distinct layouts):");
        foreach (KeyValuePair<string, List<string>> g in groups)
        {
            List<string> n = g.Value;
            string sample = string.Join(", ", n.GetRange(0, Mathf.Min(n.Count, 4)));
            if (n.Count > 4) sample += $" (+{n.Count - 4} more)";
            sb.AppendLine($"  {g.Key}");
            sb.AppendLine($"      {n.Count}x: {sample}");
        }
    }

    static string OnOff(bool b) => b ? "ON" : "off";
}
