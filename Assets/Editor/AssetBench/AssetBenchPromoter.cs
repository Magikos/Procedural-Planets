using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Promotes assets that passed the bench out of the gitignored staging folder and into the project.
///
/// This is a separate post-play step because <see cref="AssetDatabase.MoveAsset"/> is not safe during play
/// mode. Moving through the AssetDatabase (rather than the filesystem) carries the .meta and preserves the
/// GUID, so anything already referencing a promoted asset survives the move.
/// </summary>
public sealed class AssetBenchPromoter : EditorWindow
{
    const string StagingRoot = "Assets/_Bench/";
    const string DefaultDestinationRoot = "Assets/AssetPacks/";

    sealed class Candidate
    {
        public string Label;
        public string SourcePath;
        public string DestinationPath;
        public bool Selected = true;
        public string Problem;
    }

    string _reportPath = "";
    readonly List<Candidate> _candidates = new();
    Vector2 _scroll;
    string _summary = "";

    [MenuItem("Tools/Asset Bench/Promote From Report...")]
    static void Open() => GetWindow<AssetBenchPromoter>(true, "Asset Bench — Promote", true).minSize = new Vector2(760f, 420f);

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Reads a bench report, then moves every 'Keep' row out of Assets/_Bench/ into the project.\n" +
            "Moves go through AssetDatabase so .meta files and GUIDs are preserved. Existing destinations are never overwritten.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Report", _reportPath, EditorStyles.textField);
            if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
                BrowseForReport();
        }

        using (new EditorGUI.DisabledScope(_candidates.Count == 0))
        {
            if (GUILayout.Button($"Promote {SelectedCount()} selected", GUILayout.Height(28f)))
                Promote();
        }

        if (_candidates.Count == 0)
        {
            EditorGUILayout.LabelField("No 'Keep' rows loaded.");
            if (!string.IsNullOrEmpty(_summary)) EditorGUILayout.HelpBox(_summary, MessageType.None);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (Candidate c in _candidates)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    c.Selected = EditorGUILayout.Toggle(c.Selected, GUILayout.Width(18f));
                    EditorGUILayout.LabelField(c.Label, EditorStyles.boldLabel);
                }

                EditorGUILayout.LabelField("from", c.SourcePath);
                c.DestinationPath = EditorGUILayout.TextField("to", c.DestinationPath);

                if (!string.IsNullOrEmpty(c.Problem))
                    EditorGUILayout.HelpBox(c.Problem, MessageType.Warning);
            }
        }
        EditorGUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(_summary))
            EditorGUILayout.HelpBox(_summary, MessageType.None);
    }

    int SelectedCount()
    {
        int n = 0;
        foreach (Candidate c in _candidates) if (c.Selected) n++;
        return n;
    }

    void BrowseForReport()
    {
        string repoRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string start = Path.Combine(repoRoot, "docs", "research");
        if (!Directory.Exists(start)) start = repoRoot;

        string chosen = EditorUtility.OpenFilePanel("Select a bench report", start, "md");
        if (string.IsNullOrEmpty(chosen)) return;

        _reportPath = chosen;
        LoadReport(chosen);
    }

    void LoadReport(string path)
    {
        _candidates.Clear();
        _summary = "";

        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception ex) { _summary = $"Could not read report: {ex.Message}"; return; }

        int keepRows = 0;
        foreach (string line in lines)
        {
            if (!line.StartsWith("|")) continue;

            string[] cells = line.Split('|');
            // Layout: "", #, Label, Verdict, NeedsRework, Biome, Note, Question, Path, ""
            if (cells.Length < 10) continue;

            string verdict = cells[3].Trim();
            if (!verdict.Equals("Keep", StringComparison.OrdinalIgnoreCase)) continue;

            string label = cells[2].Trim();
            string source = cells[9].Trim().Trim('`').Trim();
            if (string.IsNullOrEmpty(source)) continue;

            keepRows++;
            _candidates.Add(new Candidate
            {
                Label = label,
                SourcePath = source,
                DestinationPath = ProposeDestination(source),
                Problem = Validate(source, ProposeDestination(source))
            });
        }

        _summary = keepRows == 0
            ? "No 'Keep' rows found in that report."
            : $"Loaded {keepRows} 'Keep' row(s).";
    }

    static string ProposeDestination(string sourcePath)
    {
        if (!sourcePath.StartsWith(StagingRoot, StringComparison.OrdinalIgnoreCase))
            return DefaultDestinationRoot + Path.GetFileName(sourcePath);

        string relative = sourcePath.Substring(StagingRoot.Length);
        return DefaultDestinationRoot + relative;
    }

    static string Validate(string source, string destination)
    {
        if (!File.Exists(ToAbsolute(source)))
            return "Source asset no longer exists.";
        if (File.Exists(ToAbsolute(destination)))
            return "Destination already exists — this row will be skipped rather than overwritten.";
        return null;
    }

    static string ToAbsolute(string assetPath)
    {
        string repoRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
        return Path.Combine(repoRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    void Promote()
    {
        int moved = 0, skipped = 0, failed = 0;
        var log = new StringBuilder();

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (Candidate c in _candidates)
            {
                if (!c.Selected) { skipped++; continue; }

                string problem = Validate(c.SourcePath, c.DestinationPath);
                if (problem != null)
                {
                    c.Problem = problem;
                    skipped++;
                    log.AppendLine($"skip  {c.Label}: {problem}");
                    continue;
                }

                string destDir = Path.GetDirectoryName(c.DestinationPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(destDir) && !AssetDatabase.IsValidFolder(destDir))
                    CreateFolderRecursive(destDir);

                string error = AssetDatabase.MoveAsset(c.SourcePath, c.DestinationPath);
                if (string.IsNullOrEmpty(error))
                {
                    moved++;
                    c.Problem = null;
                    log.AppendLine($"moved {c.Label} → {c.DestinationPath}");
                }
                else
                {
                    failed++;
                    c.Problem = error;
                    log.AppendLine($"FAIL  {c.Label}: {error}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        _summary = $"Moved {moved} · skipped {skipped} · failed {failed}\n\n{log}";
    }

    static void CreateFolderRecursive(string folder)
    {
        string[] parts = folder.Split('/');
        string running = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = running + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(running, parts[i]);
            running = next;
        }
    }
}
