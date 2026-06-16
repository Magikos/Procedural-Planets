using System;
using System.Reflection;
using UnityEngine;

public static class CommandExecutor
{
    /// <summary>
    /// Execute a raw command line. Routes output through <paramref name="console"/> for
    /// scrollback display. Async-returning commands are handed off to
    /// <see cref="IConsoleService.BeginAsync"/> for in-place spinner UX.
    /// </summary>
    public static void Execute(string commandLine, IConsoleService console, System.Threading.CancellationToken cancellation = default)
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
            object[] finalArgs = args;
            if (cmd.HasCancellationToken)
            {
                finalArgs = new object[args.Length + 1];
                System.Array.Copy(args, finalArgs, args.Length);
                finalArgs[args.Length] = cancellation;
            }
            result = cmd.Method.Invoke(target, finalArgs);
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

        if (cmd.IsAsync && result != null)
        {
            console.BeginAsync(alias, result, cmd.HasCancellationToken);
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
                {
                    if (cmd.CachedSingleTarget != null)
                        return cmd.CachedSingleTarget;
                    cmd.CachedSingleTarget = UnityEngine.Object.FindAnyObjectByType(cmd.DeclaringType, FindObjectsInactive.Exclude);
                    return cmd.CachedSingleTarget;
                }
                throw new InvalidOperationException(
                    $"MonoTargetType.Single requires a UnityEngine.Object-derived type; {cmd.DeclaringType.Name} is not.");

            case MonoTargetType.Registry:
                return ConsoleRegistry.GetInstance(cmd.DeclaringType);

            default:
                throw new NotSupportedException($"unsupported target type: {cmd.TargetType}");
        }
    }

    static string FormatSignature(CommandData cmd) => ParameterData.FormatCommandSignature(cmd, includeDefaults: true);
}
