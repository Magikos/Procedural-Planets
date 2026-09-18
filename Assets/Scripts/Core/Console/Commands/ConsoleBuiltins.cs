using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

[CommandPrefix("", Group = "Console and scripts", ReleasePolicy = ConsoleReleasePolicy.DevelopmentOnly)]
public static class ConsoleBuiltins
{
    [ConsoleCommand("console.echo", "Print text back to the console.", ReleasePolicy = ConsoleReleasePolicy.Player, Aliases = new[] { "echo" })]
    public static string Echo(
        [ParamDescription("text to print")] string text = "")
    {
        return text;
    }

    [ConsoleCommand("console.clear", "Clear console scrollback.", ReleasePolicy = ConsoleReleasePolicy.Player, Aliases = new[] { "clear" })]
    public static void Clear()
    {
        if (ServiceLocator.TryGet<IConsoleService>(out var console))
            console.Clear();
    }

    [ConsoleCommand("console.abandon", "Stop tracking the pending async command. Background work continues silently.", ReleasePolicy = ConsoleReleasePolicy.Player)]
    public static void Abandon()
    {
        if (ServiceLocator.TryGet<IConsoleService>(out var console))
            console.AbandonPending();
    }

    [ConsoleCommand("console.cancel", "Cancel the pending async command (with Y/N confirmation). Requires a CancellationToken-aware command.", ReleasePolicy = ConsoleReleasePolicy.Player)]
    public static void Cancel()
    {
        if (ServiceLocator.TryGet<IConsoleService>(out var console))
            console.RequestCancelPending();
    }

    [ConsoleCommand("console.quit", "Quit the application (with Y/N confirmation).", ReleasePolicy = ConsoleReleasePolicy.Player, Aliases = new[] { "quit" })]
    public static void Quit()
    {
        if (!ServiceLocator.TryGet<IConsoleService>(out var console)) return;
        console.Confirm("Quit the application?", () =>
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });
    }

    [ConsoleCommand("console.anchor", "Get or set the console anchor (Top/Bottom/Left/Right).", ReleasePolicy = ConsoleReleasePolicy.Player)]
    public static string Anchor(ConsoleAnchor? anchor = null)
    {
        if (!ServiceLocator.TryGet<IConsoleService>(out var console)) return "";
        if (anchor == null) return $"current anchor: {console.Anchor}";
        console.Anchor = anchor.Value;
        return $"anchor set to {anchor.Value}";
    }

    [ConsoleCommand("console.scrollback-size", "Get or set scrollback capacity (lines).", ReleasePolicy = ConsoleReleasePolicy.Player)]
    public static string ScrollbackSize(int? size = null)
    {
        if (!ServiceLocator.TryGet<IConsoleService>(out var console)) return "";
        if (size == null) return $"current scrollback capacity: {console.ScrollbackCapacity} lines";
        if (size.Value < 1) throw new ArgumentOutOfRangeException(nameof(size), "scrollback capacity must be ≥ 1");
        console.ScrollbackCapacity = size.Value;
        return $"scrollback capacity set to {size.Value} lines";
    }

    [ConsoleCommand("console.dump", "Write the console scrollback to local-only/console-dumps/<name>.txt (default: timestamped).")]
    public static string Dump(
        [ParamDescription("optional file name (without path); reused/overwritten if it exists")] string name = "")
    {
        ValidateDumpName(name);
        if (!ServiceLocator.TryGet<IConsoleService>(out var console)) return "";

        string root = System.IO.Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("console.dump: could not resolve project root");
        string dir = System.IO.Path.Combine(root, "local-only", "console-dumps");

        string file = string.IsNullOrWhiteSpace(name)
            ? $"console-{System.DateTime.Now:yyyyMMdd-HHmmss}.txt"
            : (name.EndsWith(".txt") ? name : name + ".txt");
        string path = Path.GetFullPath(Path.Combine(dir, file));
        if (!path.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Dump destination must stay inside console-dumps.", nameof(name));
        Directory.CreateDirectory(dir);

        var sb = new System.Text.StringBuilder();
        foreach (var line in console.ScrollbackLines)
            sb.AppendLine(line.Text);
        System.IO.File.WriteAllText(path, sb.ToString());
        return $"console dumped ({console.ScrollbackLines.Count} lines) to {path}";
    }

    public static void ValidateDumpName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Path.IsPathRooted(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\') || name == "." || name == "..")
            throw new ArgumentException("Use a filename without a path.", nameof(name));
    }

    [ConsoleCommand("console.help", "Browse command families. Use help all, help <family>, help <command>, or help <release policy>.",
        ReleasePolicy = ConsoleReleasePolicy.Player, Aliases = new[] { "help" }, Example = "help camera")]
    public static void Help(
        [ParamDescription("family, command, release policy, all, or search text")]
        [CompletionSource(typeof(CommandNamesProvider))] string name = "")
    {
        if (!ServiceLocator.TryGet<IConsoleService>(out var console)) return;
        if (ConsoleRegistry.TryGet(name, out var exact) && ConsoleCommandPolicy.CanExecute(exact))
        {
            console.PrintLine(ParameterData.FormatCommandSignature(exact, includeDefaults: true));
            console.PrintLine($"  [{exact.ReleasePolicy}] {exact.Description}");
            if (exact.Aliases.Length > 0) console.PrintLine("  aliases: " + string.Join(", ", exact.Aliases));
            if (!string.IsNullOrEmpty(exact.Example)) console.PrintLine("  example: " + exact.Example);
            foreach (var parameter in exact.Parameters)
                if (!string.IsNullOrEmpty(parameter.Description)) console.PrintLine($"  {parameter.Name}: {parameter.Description}");
            return;
        }
        var commands = CommandCatalog.Visible.ToArray();
        if (string.IsNullOrWhiteSpace(name))
        {
            console.PrintLine($"{commands.Length} commands. help <family> | help all | help Player | help <search text>");
            foreach (var group in commands.GroupBy(command => command.Group).OrderBy(group => group.Key))
                console.PrintLine($"  {group.Key}: {string.Join(", ", group.GroupBy(c => c.Family).Select(family => $"{family.Key} ({family.Count()})"))}");
            console.PrintLine("Release: Player = permitted locally; DevelopmentOnly/Unreviewed/Administrator = denied in release.");
            return;
        }
        var matches = CommandCatalog.Search(name).ToArray();
        console.PrintLine($"{matches.Length} matching commands:");
        foreach (var command in matches)
            console.PrintLine($"  {command.Alias} [{command.ReleasePolicy}] — {command.Description}");
    }

    [ConsoleCommand("console.catalog", "List command release policies, aliases, and owners. Optional save writes a TSV catalog.",
        ReleasePolicy = ConsoleReleasePolicy.DevelopmentOnly, Example = "console.catalog true")]
    public static string Catalog(bool save = false)
    {
        var text = new StringBuilder("Command\tGroup\tFamily\tReleasePolicy\tAliases\tOwner\tDescription\n");
        foreach (var command in CommandCatalog.All)
            text.AppendLine(string.Join("\t", new[] { command.Alias, command.Group, command.Family, command.ReleasePolicy.ToString(),
                string.Join(", ", command.Aliases), command.DeclaringType.FullName,
                command.Description.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ') }));
        if (!save) return text.ToString();
        string root = Directory.GetParent(Application.dataPath)?.FullName ?? throw new InvalidOperationException("Project root unavailable.");
        string folder = Path.Combine(root, "local-only", "console-dumps");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "command-catalog.tsv");
        File.WriteAllText(path, text.ToString());
        return $"Command catalog saved to {path}";
    }
}
