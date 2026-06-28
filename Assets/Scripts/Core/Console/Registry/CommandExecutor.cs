using System;
using System.Reflection;
using System.Threading;
using UnityEngine;

public static class CommandExecutor
{
    /// <summary>
    /// Execute a raw command line. Routes output through <paramref name="console"/> for
    /// scrollback display. Async-returning commands are handed off to
    /// <see cref="IConsoleService.BeginAsync"/> for in-place spinner UX.
    /// </summary>
    public static void Execute(string commandLine, IConsoleService console, CancellationToken cancellation = default)
    {
        ConsoleCommandResult result = Invoke(commandLine, cancellation);
        if (!result.Success)
        {
            PrintFailure(console, result);
            return;
        }

        if (result.IsAsync && result.AwaitableResult != null)
        {
            console?.BeginAsync(result.Alias, result.AwaitableResult, result.IsCancellable);
            return;
        }

        PrintOutput(console, result);
    }

    /// <summary>
    /// Execute a command and await async-returning command methods inline. Used by diagnostic
    /// command scripts where commands must run sequentially instead of through console UI state.
    /// </summary>
    public static async Awaitable<ConsoleCommandResult> ExecuteAsync(
        string commandLine,
        IConsoleService console,
        CancellationToken cancellation = default,
        bool printOutput = true)
    {
        ConsoleCommandResult result = Invoke(commandLine, cancellation);
        if (!result.Success)
        {
            PrintFailure(console, result);
            return result;
        }

        if (result.IsAsync && result.AwaitableResult != null)
        {
            try
            {
                object asyncResult = await ConsoleAwaitableUtility.AwaitResultAsync(
                    result.AwaitableResult,
                    cancellation);
                result = result.WithOutput(asyncResult?.ToString());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = result.WithError($"{result.Alias}: {ex.Message}");
                PrintFailure(console, result);
                return result;
            }
        }

        if (printOutput)
            PrintOutput(console, result);

        return result;
    }

    /// <summary>
    /// Execute only a synchronous command and return its output without printing to the console.
    /// This is intended for F10 sidecar metadata recorders.
    /// </summary>
    public static ConsoleCommandResult ExecuteImmediate(string commandLine)
    {
        ConsoleCommandResult result = Invoke(commandLine, CancellationToken.None);
        if (!result.Success)
            return result;
        return result.IsAsync
            ? result.WithError($"{result.Alias}: async commands are not valid in immediate execution")
            : result;
    }

    static ConsoleCommandResult Invoke(string commandLine, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return ConsoleCommandResult.Failed(commandLine, "", "empty command");

        var tokens = CommandParser.Tokenize(commandLine);
        if (tokens.Count == 0)
            return ConsoleCommandResult.Failed(commandLine, "", "empty command");

        string alias = tokens[0];
        if (!ConsoleRegistry.TryGet(alias, out CommandData cmd))
            return ConsoleCommandResult.Failed(commandLine, alias, $"unknown command: '{alias}' - try 'help'");

        var argTokens = tokens.GetRange(1, tokens.Count - 1);
        if (!CommandParser.TryBind(cmd, argTokens, out object[] args, out string bindError))
        {
            return ConsoleCommandResult.Failed(
                commandLine,
                alias,
                $"{alias}: {bindError}",
                FormatSignature(cmd));
        }

        object target;
        try
        {
            target = ResolveTarget(cmd);
        }
        catch (Exception ex)
        {
            return ConsoleCommandResult.Failed(
                commandLine,
                alias,
                $"{alias}: could not resolve target - {ex.Message}");
        }

        if (cmd.TargetType != MonoTargetType.Static && target == null)
        {
            return ConsoleCommandResult.Failed(
                commandLine,
                alias,
                $"{alias}: no live instance of {cmd.DeclaringType.Name} found");
        }

        object rawResult;
        try
        {
            object[] finalArgs = args;
            if (cmd.HasCancellationToken)
            {
                finalArgs = new object[args.Length + 1];
                Array.Copy(args, finalArgs, args.Length);
                finalArgs[args.Length] = cancellation;
            }
            rawResult = cmd.Method.Invoke(target, finalArgs);
        }
        catch (TargetInvocationException tex)
        {
            return ConsoleCommandResult.Failed(
                commandLine,
                alias,
                $"{alias}: {tex.InnerException?.Message ?? tex.Message}");
        }
        catch (Exception ex)
        {
            return ConsoleCommandResult.Failed(commandLine, alias, $"{alias}: {ex.Message}");
        }

        if (cmd.IsAsync)
        {
            return new ConsoleCommandResult(
                true,
                commandLine,
                alias,
                "",
                "",
                "",
                true,
                cmd.HasCancellationToken,
                rawResult);
        }

        return new ConsoleCommandResult(
            true,
            commandLine,
            alias,
            rawResult?.ToString(),
            "",
            "",
            false,
            cmd.HasCancellationToken,
            null);
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
                    cmd.CachedSingleTarget = UnityEngine.Object.FindAnyObjectByType(
                        cmd.DeclaringType,
                        FindObjectsInactive.Exclude);
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

    static void PrintFailure(IConsoleService console, ConsoleCommandResult result)
    {
        if (console == null)
            return;

        console.PrintError(result.Error);
        if (!string.IsNullOrEmpty(result.Usage))
            console.PrintLine($"  usage: {result.Usage}");
    }

    static void PrintOutput(IConsoleService console, ConsoleCommandResult result)
    {
        if (console == null || !result.HasOutput)
            return;
        console.PrintLine(result.Output);
    }

    static string FormatSignature(CommandData cmd)
    {
        return ParameterData.FormatCommandSignature(cmd, includeDefaults: true);
    }
}
