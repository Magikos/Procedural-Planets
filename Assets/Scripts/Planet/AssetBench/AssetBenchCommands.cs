using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Completion provider for <c>bench.load</c>: the manifest assets under Resources/Settings/AssetBench.</summary>
public sealed class BenchManifestNamesProvider : IConsoleCompletionProvider
{
    public IEnumerable<string> GetCompletions(string partialValue)
    {
        AssetBenchManifest[] all = AssetBenchCommands.AllManifests();
        var names = new List<string>(all.Length);
        foreach (AssetBenchManifest m in all) names.Add(m.name);
        return CompletionRanker.Rank(names, partialValue);
    }
}

/// <summary>
/// Console entry points for the asset bench. <see cref="MonoTargetType.Static"/> for the same reason as
/// <see cref="CharacterCommands"/>: <c>bench.load</c> must work before any host instance exists, so it
/// find-or-creates the single <see cref="AssetBenchHost"/>.
/// </summary>
[CommandPrefix("bench")]
public static class AssetBenchCommands
{
    const string ManifestFolder = "Settings/AssetBench";

    static AssetBenchHost _host;

    static AssetBenchHost Host()
    {
        if (_host != null) // Unity's null override makes a destroyed host compare == null, so re-find it.
            return _host;

        _host = UnityEngine.Object.FindAnyObjectByType<AssetBenchHost>();
        if (_host == null)
        {
            var go = new GameObject("Asset Bench Host");
            _host = go.AddComponent<AssetBenchHost>();
        }
        return _host;
    }

    internal static AssetBenchManifest[] AllManifests()
    {
        AssetBenchManifest[] all = Resources.LoadAll<AssetBenchManifest>(ManifestFolder);
        Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
        return all;
    }

    static string ReportDir()
    {
        string repoRoot = Directory.GetParent(Application.dataPath)?.FullName;
        return string.IsNullOrEmpty(repoRoot) ? null : Path.Combine(repoRoot, "docs", "research");
    }

    /// A batch counts as judged once a report exists — bench.report is the only thing that writes one.
    static bool HasReport(string batchId)
    {
        string dir = ReportDir();
        return dir != null && Directory.Exists(dir) && Directory.GetFiles(dir, $"bench-{batchId}-*.md").Length > 0;
    }

    [ConsoleCommand("list", "List the bench manifests and whether each has been judged already.", MonoTargetType.Static)]
    public static string ListCmd()
    {
        AssetBenchManifest[] all = AllManifests();
        if (all.Length == 0)
            return $"bench: no manifests under Resources/{ManifestFolder}";

        var sb = new StringBuilder($"bench: {all.Length} manifest(s)");
        foreach (AssetBenchManifest m in all)
        {
            int pairs = m.Entries != null ? m.Entries.Length : 0;
            sb.Append($"\n  {m.name} — {pairs} pair(s), {(HasReport(m.BatchId) ? "judged" : "pending")}");
        }
        return sb.ToString();
    }

    [ConsoleCommand("load", "Load a bench manifest by name from Resources/Settings/AssetBench and spawn its pairs.", MonoTargetType.Static)]
    public static string LoadCmd(
        [CompletionSource(typeof(BenchManifestNamesProvider))] string manifestName = "")
    {
        if (string.IsNullOrWhiteSpace(manifestName))
            return $"usage: bench.load <manifestName>  (or bench.load-next)\n{ListCmd()}";

        var manifest = Resources.Load<AssetBenchManifest>($"{ManifestFolder}/{manifestName}");
        if (manifest == null)
            return $"bench: no manifest '{manifestName}' under Resources/{ManifestFolder}\n{ListCmd()}";

        return Host().Service.Load(manifest, out _);
    }

    [ConsoleCommand("load-next", "Load the next manifest that has no report yet.", MonoTargetType.Static)]
    public static string LoadNextCmd()
    {
        foreach (AssetBenchManifest m in AllManifests())
            if (!HasReport(m.BatchId))
                return Host().Service.Load(m, out _);

        return "bench: nothing pending — every manifest has a report. bench.list to review them.";
    }

    [ConsoleCommand("next", "Focus the next pair.", MonoTargetType.Static)]
    public static string NextCmd()
    {
        AssetBenchService s = Host().Service;
        if (!s.IsLoaded) return "bench: no batch loaded";
        s.FocusNext();
        return s.StatusLine;
    }

    [ConsoleCommand("prev", "Focus the previous pair.", MonoTargetType.Static)]
    public static string PrevCmd()
    {
        AssetBenchService s = Host().Service;
        if (!s.IsLoaded) return "bench: no batch loaded";
        s.FocusPrevious();
        return s.StatusLine;
    }

    [ConsoleCommand("go", "Focus a pair by its 1-based number, or list the pairs when called with no number.", MonoTargetType.Static)]
    public static string GoCmd(int index = 0)
    {
        AssetBenchService s = Host().Service;
        return index <= 0 ? s.PairList() : s.FocusOneBased(index);
    }

    [ConsoleCommand("zoom", "Framing distance multiplier for the focused pair; below 1 moves in (also - / =).", MonoTargetType.Static)]
    public static string ZoomCmd(float? value = null)
    {
        AssetBenchService s = Host().Service;
        return value.HasValue ? s.SetZoom(value.Value) : $"bench: zoom = {s.Zoom:F2} (lower is closer)";
    }

    [ConsoleCommand("spacing", "Multiplier on how far apart the bench lays props out. Applies on the next bench.load.", MonoTargetType.Static)]
    public static string SpacingCmd(float? value = null)
    {
        AssetBenchService s = Host().Service;
        return value.HasValue ? s.SetSpacing(value.Value) : $"bench: spacing = {s.Spacing:F2}";
    }

    [ConsoleCommand("isolate", "Hide or show the reference prefabs so the candidate reads alone (also H).", MonoTargetType.Static)]
    public static string IsolateCmd() => Host().Service.ToggleIsolate();

    [ConsoleCommand("hud", "Show or hide the bench HUD (also F4).", MonoTargetType.Static)]
    public static string HudCmd(bool? on = null)
    {
        AssetBenchHost host = Host();
        host.HudVisible = on ?? !host.HudVisible;
        return $"bench: HUD {(host.HudVisible ? "shown" : "hidden")}";
    }

    [ConsoleCommand("keep", "Mark the focused pair as keep and advance.", MonoTargetType.Static)]
    public static string KeepCmd() => Host().Service.SetVerdict(BenchVerdict.Keep);

    [ConsoleCommand("cut", "Mark the focused pair as cut and advance.", MonoTargetType.Static)]
    public static string CutCmd() => Host().Service.SetVerdict(BenchVerdict.Cut);

    [ConsoleCommand("later", "Mark the focused pair as later (reconsider) and advance.", MonoTargetType.Static)]
    public static string LaterCmd() => Host().Service.SetVerdict(BenchVerdict.Later);

    [ConsoleCommand("note", "Attach a note to the focused pair.", MonoTargetType.Static)]
    public static string NoteCmd(string text) => Host().Service.SetNote(text);

    [ConsoleCommand("rework", "Flag the focused pair as needing rework before it is usable (true/false).", MonoTargetType.Static)]
    public static string ReworkCmd(bool value) => Host().Service.SetNeedsRework(value);

    [ConsoleCommand("biome", "Tag subsequent verdicts with a biome name, so the report records where you judged.", MonoTargetType.Static)]
    public static string BiomeCmd(string biome) => Host().Service.SetBiome(biome);

    [ConsoleCommand("status", "Print the current bench batch and focus.", MonoTargetType.Static)]
    public static string StatusCmd() => Host().Service.StatusLine;

    [ConsoleCommand("report", "Write the verdict report to docs/research and despawn the batch.", MonoTargetType.Static)]
    public static string ReportCmd()
    {
        AssetBenchService s = Host().Service;
        if (!s.IsLoaded) return "bench: no batch loaded";

        int unjudged = s.Count - s.JudgedCount;
        string stamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        string markdown = s.BuildReport(stamp);
        string batchId = s.BatchId;

        string dir = ReportDir();
        if (dir == null)
            return "bench: could not resolve the repo root";

        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"bench-{batchId}-{DateTime.UtcNow:yyyy-MM-dd}.md");

        try
        {
            File.WriteAllText(path, markdown);
        }
        catch (Exception ex)
        {
            return $"bench: failed to write report — {ex.Message}";
        }

        s.Unload();

        // The batch despawns here, so an unjudged row means a lost look — say so rather than bury it.
        string pending = unjudged > 0 ? $"  ⚠ {unjudged} pair(s) left unjudged." : "";
        return $"bench: report written to {path} (batch despawned).{pending}";
    }

    [ConsoleCommand("cancel", "Despawn the batch and discard verdicts without writing a report.", MonoTargetType.Static)]
    public static string CancelCmd()
    {
        AssetBenchService s = Host().Service;
        if (!s.IsLoaded) return "bench: no batch loaded";
        s.Unload();
        return "bench: batch cancelled, verdicts discarded";
    }
}
