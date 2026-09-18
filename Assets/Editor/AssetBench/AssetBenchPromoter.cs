using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Promotes assets that passed the bench out of the gitignored staging folder and into the project.
///
/// Promotion moves an asset's whole dependency closure, not just the file named in the report. A mesh's
/// materials, textures and shaders are usually staged alongside it, and moving the mesh alone leaves them
/// behind in an ignored folder — which resolves fine locally, then arrives as missing materials on every
/// other checkout.
///
/// This is a separate post-play step because <see cref="AssetDatabase.MoveAsset"/> is not safe during play
/// mode. Moving through the AssetDatabase (rather than the filesystem) carries the .meta and preserves the
/// GUID, so anything already referencing a promoted asset survives the move.
/// </summary>
public sealed class AssetBenchPromoter : EditorWindow
{
    const string StagingRoot = "Assets/_Bench/";
    const string DefaultDestinationRoot = "Assets/Art/";

    sealed class Move
    {
        public string Source;
        public string Destination;
        public string Problem;
    }

    sealed class Candidate
    {
        public string Label;
        public string SourcePath;
        public bool Selected = true;
        public bool Expanded;
        public readonly List<Move> Moves = new();

        public int BlockedCount()
        {
            int n = 0;
            foreach (Move m in Moves) if (m.Problem != null) n++;
            return n;
        }
    }

    string _reportPath = "";
    string _destinationRoot = DefaultDestinationRoot;
    readonly List<Candidate> _candidates = new();
    Vector2 _scroll;
    string _summary = "";

    [MenuItem("Tools/Asset Bench/Promote From Report...")]
    static void Open() => GetWindow<AssetBenchPromoter>(true, "Asset Bench — Promote", true).minSize = new Vector2(820f, 480f);

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Reads a bench report and moves every 'Keep' row out of Assets/_Bench/ into the project, "
            + "together with the materials, textures and shaders it depends on.\n"
            + "Moves go through AssetDatabase so .meta files and GUIDs are preserved. "
            + "Existing destinations are never overwritten.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Report", _reportPath, EditorStyles.textField);
            if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
                BrowseForReport();
        }

        EditorGUI.BeginChangeCheck();
        _destinationRoot = EditorGUILayout.TextField("Destination root", _destinationRoot);
        if (EditorGUI.EndChangeCheck())
            RebuildDestinations();

        using (new EditorGUI.DisabledScope(_candidates.Count == 0))
        {
            if (GUILayout.Button($"Promote {SelectedCount()} selected ({SelectedFileCount()} files)", GUILayout.Height(28f)))
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
                    c.Expanded = EditorGUILayout.Foldout(c.Expanded, $"{c.Label}  —  {c.Moves.Count} file(s)", true);
                }

                EditorGUILayout.LabelField("asset", c.SourcePath);

                int blocked = c.BlockedCount();
                if (blocked > 0)
                    EditorGUILayout.HelpBox($"{blocked} of {c.Moves.Count} file(s) will be skipped — see the list.", MessageType.Warning);

                if (!c.Expanded) continue;

                EditorGUI.indentLevel++;
                foreach (Move m in c.Moves)
                {
                    EditorGUILayout.LabelField(Relative(m.Source), m.Problem == null ? "→ " + Relative(m.Destination) : "SKIP: " + m.Problem);
                }
                EditorGUI.indentLevel--;
            }
        }
        EditorGUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(_summary))
            EditorGUILayout.HelpBox(_summary, MessageType.None);
    }

    static string Relative(string assetPath) =>
        assetPath.StartsWith(StagingRoot, StringComparison.OrdinalIgnoreCase)
            ? assetPath.Substring(StagingRoot.Length)
            : assetPath;

    int SelectedCount()
    {
        int n = 0;
        foreach (Candidate c in _candidates) if (c.Selected) n++;
        return n;
    }

    int SelectedFileCount()
    {
        int n = 0;
        foreach (Candidate c in _candidates)
            if (c.Selected)
                foreach (Move m in c.Moves)
                    if (m.Problem == null) n++;
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

        foreach (string line in lines)
        {
            if (!line.StartsWith("|")) continue;

            string[] cells = line.Split('|');
            // Leading and trailing pipes produce empty first and last cells:
            // "", #, Label, Verdict, NeedsRework, Biome, Note, Question, Path, ""
            if (cells.Length < 10) continue;

            if (!cells[3].Trim().Equals("Keep", StringComparison.OrdinalIgnoreCase)) continue;

            // Found by its backticks rather than a fixed index, so adding a column cannot silently
            // turn every row into "no Keep rows found".
            string source = null;
            foreach (string cell in cells)
            {
                string trimmed = cell.Trim();
                if (trimmed.StartsWith("`") && trimmed.EndsWith("`") && trimmed.Length > 2)
                    source = trimmed.Trim('`').Trim();
            }
            if (string.IsNullOrEmpty(source)) continue;

            _candidates.Add(new Candidate { Label = cells[2].Trim(), SourcePath = source });
        }

        RebuildDestinations();

        _summary = _candidates.Count == 0
            ? "No 'Keep' rows found in that report."
            : $"Loaded {_candidates.Count} 'Keep' row(s), {SelectedFileCount()} file(s) to move.";
    }

    void RebuildDestinations()
    {
        foreach (Candidate c in _candidates)
        {
            c.Moves.Clear();
            foreach (string source in ClosureOf(c.SourcePath))
            {
                string destination = ProposeDestination(source);
                c.Moves.Add(new Move
                {
                    Source = source,
                    Destination = destination,
                    Problem = Validate(source, destination),
                });
            }
        }
    }

    /// The asset plus every dependency still sitting in staging. Dependencies already outside the staging
    /// folder are shared project assets and stay where they are.
    static List<string> ClosureOf(string assetPath)
    {
        var closure = new List<string> { assetPath };

        foreach (string dependency in AssetDatabase.GetDependencies(assetPath, true))
        {
            if (dependency == assetPath) continue;
            if (!dependency.StartsWith(StagingRoot, StringComparison.OrdinalIgnoreCase)) continue;
            if (!closure.Contains(dependency)) closure.Add(dependency);
        }

        return closure;
    }

    string ProposeDestination(string sourcePath)
    {
        string root = string.IsNullOrWhiteSpace(_destinationRoot) ? DefaultDestinationRoot : _destinationRoot;
        if (!root.EndsWith("/")) root += "/";
        return root + Relative(sourcePath);
    }

    static string Validate(string source, string destination)
    {
        if (!File.Exists(ToAbsolute(source)))
            return "source no longer exists";
        if (File.Exists(ToAbsolute(destination)))
            return "destination already exists";
        return null;
    }

    static string ToAbsolute(string assetPath)
    {
        string repoRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
        return Path.Combine(repoRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    void Promote()
    {
        RebuildDestinations();

        int moved = 0, skipped = 0, failed = 0;
        var log = new StringBuilder();

        // Folders first, outside the batch: CreateFolder does not register with the asset database until
        // StopAssetEditing, so creating one inside the batch makes every move into it fail with
        // "Parent directory is not in asset database".
        foreach (Candidate c in _candidates)
        {
            if (!c.Selected) continue;
            foreach (Move m in c.Moves)
            {
                string destDir = Path.GetDirectoryName(m.Destination)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(destDir) && !AssetDatabase.IsValidFolder(destDir))
                    CreateFolderRecursive(destDir);
            }
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (Candidate c in _candidates)
            {
                if (!c.Selected) { skipped += c.Moves.Count; continue; }

                foreach (Move m in c.Moves)
                {
                    string problem = Validate(m.Source, m.Destination);
                    if (problem != null)
                    {
                        m.Problem = problem;
                        skipped++;
                        log.AppendLine($"skip  {Relative(m.Source)}: {problem}");
                        continue;
                    }

                    string error = AssetDatabase.MoveAsset(m.Source, m.Destination);
                    if (string.IsNullOrEmpty(error))
                    {
                        moved++;
                        m.Problem = null;
                        log.AppendLine($"moved {Relative(m.Source)} → {m.Destination}");
                    }
                    else
                    {
                        failed++;
                        m.Problem = error;
                        log.AppendLine($"FAIL  {Relative(m.Source)}: {error}");
                    }
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

    /// Follows the path CreateFolder actually returns rather than the one requested. CreateFolder
    /// uniquifies — asked for "Corals" when it cannot see an existing one, it silently makes "Corals 1" —
    /// so assuming the requested name spawns a numbered duplicate for every level of every file.
    static void CreateFolderRecursive(string folder)
    {
        string[] parts = folder.Split('/');
        string running = parts[0]; // "Assets"

        for (int i = 1; i < parts.Length; i++)
        {
            string next = running + "/" + parts[i];
            if (AssetDatabase.IsValidFolder(next))
            {
                running = next;
                continue;
            }

            string guid = AssetDatabase.CreateFolder(running, parts[i]);
            string created = AssetDatabase.GUIDToAssetPath(guid);
            running = string.IsNullOrEmpty(created) ? next : created;
        }
    }
}
