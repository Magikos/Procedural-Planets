using System.Linq;
using UnityEngine;

public static class ConsoleBuiltins
{
    [ConsoleCommand("echo", "Print text back to the console.")]
    public static string Echo(
        [ParamDescription("text to print")] string text = "")
    {
        return text;
    }

    [ConsoleCommand("clear", "Clear console scrollback.")]
    public static void Clear()
    {
        if (ServiceLocator.TryGet<IConsoleService>(out var console))
            console.Clear();
    }

    [ConsoleCommand("console.abandon", "Stop tracking the pending async command. Background work continues silently.")]
    public static void Abandon()
    {
        if (ServiceLocator.TryGet<IConsoleService>(out var console))
            console.AbandonPending();
    }

    [ConsoleCommand("console.cancel", "Cancel the pending async command (with Y/N confirmation). Requires a CancellationToken-aware command.")]
    public static void Cancel()
    {
        if (ServiceLocator.TryGet<IConsoleService>(out var console))
            console.RequestCancelPending();
    }

    [ConsoleCommand("quit", "Quit the application (with Y/N confirmation).")]
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

    [ConsoleCommand("console.anchor", "Get or set the console anchor (Top/Bottom/Left/Right).")]
    public static string Anchor(ConsoleAnchor? anchor = null)
    {
        if (!ServiceLocator.TryGet<IConsoleService>(out var console)) return "";
        if (anchor == null) return $"current anchor: {console.Anchor}";
        console.Anchor = anchor.Value;
        return $"anchor set to {anchor.Value}";
    }

    [ConsoleCommand("console.scrollback-size", "Get or set scrollback capacity (lines).")]
    public static string ScrollbackSize(int? size = null)
    {
        if (!ServiceLocator.TryGet<IConsoleService>(out var console)) return "";
        if (size == null) return $"current scrollback capacity: {console.ScrollbackCapacity} lines";
        if (size.Value < 1) return "scrollback capacity must be ≥ 1";
        console.ScrollbackCapacity = size.Value;
        return $"scrollback capacity set to {size.Value} lines";
    }

    [ConsoleCommand("console.dump", "Write the console scrollback to local-only/console-dumps/<name>.txt (default: timestamped).")]
    public static string Dump(
        [ParamDescription("optional file name (without path); reused/overwritten if it exists")] string name = "")
    {
        if (!ServiceLocator.TryGet<IConsoleService>(out var console)) return "";

        string root = System.IO.Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(root)) return "console.dump: could not resolve project root";
        string dir = System.IO.Path.Combine(root, "local-only", "console-dumps");
        System.IO.Directory.CreateDirectory(dir);

        string file = string.IsNullOrWhiteSpace(name)
            ? $"console-{System.DateTime.Now:yyyyMMdd-HHmmss}.txt"
            : (name.EndsWith(".txt") ? name : name + ".txt");
        string path = System.IO.Path.Combine(dir, file);

        var sb = new System.Text.StringBuilder();
        foreach (var line in console.ScrollbackLines)
            sb.AppendLine(line.Text);
        System.IO.File.WriteAllText(path, sb.ToString());
        return $"console dumped ({console.ScrollbackLines.Count} lines) to {path}";
    }

    [ConsoleCommand("help", "List all commands, or describe one by name.")]
    public static void Help(
        [ParamDescription("optional command name to describe")]
        [CompletionSource(typeof(CommandNamesProvider))] string name = "")
    {
        if (!ServiceLocator.TryGet<IConsoleService>(out var console))
            return;

        if (!string.IsNullOrEmpty(name))
        {
            if (ConsoleRegistry.TryGet(name, out CommandData cmd))
            {
                console.PrintLine(ParameterData.FormatCommandSignature(cmd, includeDefaults: true));
                if (!string.IsNullOrEmpty(cmd.Description))
                    console.PrintLine($"  {cmd.Description}");
                foreach (var p in cmd.Parameters)
                {
                    if (!string.IsNullOrEmpty(p.Description))
                        console.PrintLine($"  {p.Name}: {p.Description}");
                }
                return;
            }

            // No exact match — try prefix-match (e.g. "help test" lists all test.*).
            string prefix = name.EndsWith(".") ? name : name + ".";
            var prefixed = ConsoleRegistry.Commands.Keys
                .Where(a => a.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a)
                .ToArray();

            if (prefixed.Length == 0)
            {
                console.PrintError($"unknown command: '{name}'");
                return;
            }

            console.PrintLine($"{prefixed.Length} commands matching '{prefix}':");
            foreach (string alias in prefixed)
            {
                ConsoleRegistry.TryGet(alias, out CommandData c);
                string desc = string.IsNullOrEmpty(c.Description) ? "" : $" — {c.Description}";
                console.PrintLine($"  {alias}{desc}");
            }
            return;
        }

        var aliases = ConsoleRegistry.Commands.Keys.OrderBy(a => a).ToArray();
        console.PrintLine($"{aliases.Length} commands registered:");
        foreach (string alias in aliases)
        {
            ConsoleRegistry.TryGet(alias, out CommandData cmd);
            string desc = string.IsNullOrEmpty(cmd.Description) ? "" : $" — {cmd.Description}";
            console.PrintLine($"  {alias}{desc}");
        }
    }

}
