using System;
using System.Reflection;
using UnityEngine;

public static class CommandExecutor
{
    /// <summary>
    /// Execute a raw command line. Routes output through <paramref name="console"/> for
    /// scrollback display (and errors as `<color=red>` for slice 3 color tags — currently raw text).
    /// </summary>
    public static void Execute(string commandLine, IConsoleService console)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return;

        var tokens = CommandParser.Tokenize(commandLine);
        if (tokens.Count == 0) return;

        string alias = tokens[0];
        if (!ConsoleRegistry.TryGet(alias, out CommandData cmd))
        {
            console.PrintError($"unknown command: '{alias}' — try 'help'");
            return;
        }

        var argTokens = tokens.GetRange(1, tokens.Count - 1);
        if (!CommandParser.TryBind(cmd, argTokens, out object[] args, out string bindError))
        {
            console.PrintError($"{alias}: {bindError}");
            console.PrintLine($"  usage: {FormatSignature(cmd)}");
            return;
        }

        object target;
        try
        {
            target = ResolveTarget(cmd);
        }
        catch (Exception ex)
        {
            console.PrintError($"{alias}: could not resolve target — {ex.Message}");
            return;
        }

        if (cmd.TargetType != MonoTargetType.Static && target == null)
        {
            console.PrintError($"{alias}: no live instance of {cmd.DeclaringType.Name} found");
            return;
        }

        object result;
        try
        {
            result = cmd.Method.Invoke(target, args);
        }
        catch (TargetInvocationException tex)
        {
            console.PrintError($"{alias}: {tex.InnerException?.Message ?? tex.Message}");
            return;
        }
        catch (Exception ex)
        {
            console.PrintError($"{alias}: {ex.Message}");
            return;
        }

        // Async commands ship in CONSOLE-4; for slice 2 we print a note that the awaitable was issued.
        if (cmd.IsAsync && result != null)
        {
            console.PrintLine($"{alias}: async command issued (await handling lands in CONSOLE-4)");
            return;
        }

        if (result is string s && !string.IsNullOrEmpty(s))
            console.PrintLine(s);
    }

    static object ResolveTarget(CommandData cmd)
    {
        switch (cmd.TargetType)
        {
            case MonoTargetType.Static:
                return null;

            case MonoTargetType.Single:
                if (typeof(UnityEngine.Object).IsAssignableFrom(cmd.DeclaringType))
                    return UnityEngine.Object.FindAnyObjectByType(cmd.DeclaringType, FindObjectsInactive.Exclude);
                throw new InvalidOperationException(
                    $"MonoTargetType.Single requires a UnityEngine.Object-derived type; {cmd.DeclaringType.Name} is not.");

            case MonoTargetType.Registry:
                return ConsoleRegistry.GetInstance(cmd.DeclaringType);

            default:
                throw new NotSupportedException($"unsupported target type: {cmd.TargetType}");
        }
    }

    static string FormatSignature(CommandData cmd)
    {
        if (cmd.Parameters.Length == 0) return cmd.Alias;

        var parts = new string[cmd.Parameters.Length];
        for (int i = 0; i < cmd.Parameters.Length; i++)
        {
            ParameterData p = cmd.Parameters[i];
            parts[i] = p.HasDefault
                ? $"[{p.Name}:{p.Type.Name}={p.DefaultValue}]"
                : $"<{p.Name}:{p.Type.Name}>";
        }
        return $"{cmd.Alias} {string.Join(" ", parts)}";
    }
}
