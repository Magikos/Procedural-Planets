using System.Linq;
using System.Text;

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

    [ConsoleCommand("help", "List all commands, or describe one by name.")]
    public static void Help(
        [ParamDescription("optional command name to describe")]
        [CompletionSource(typeof(CommandNamesProvider))] string name = "")
    {
        if (!ServiceLocator.TryGet<IConsoleService>(out var console))
            return;

        if (!string.IsNullOrEmpty(name))
        {
            if (!ConsoleRegistry.TryGet(name, out CommandData cmd))
            {
                console.PrintError($"unknown command: '{name}'");
                return;
            }

            console.PrintLine(FormatSignature(cmd));
            if (!string.IsNullOrEmpty(cmd.Description))
                console.PrintLine($"  {cmd.Description}");
            foreach (var p in cmd.Parameters)
            {
                if (!string.IsNullOrEmpty(p.Description))
                    console.PrintLine($"  {p.Name}: {p.Description}");
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

    static string FormatSignature(CommandData cmd)
    {
        if (cmd.Parameters.Length == 0) return cmd.Alias;

        var sb = new StringBuilder(cmd.Alias);
        foreach (var p in cmd.Parameters)
        {
            sb.Append(p.HasDefault
                ? $" [{p.Name}:{p.Type.Name}={p.DefaultValue}]"
                : $" <{p.Name}:{p.Type.Name}>");
        }
        return sb.ToString();
    }
}
