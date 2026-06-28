using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityEngine;

public sealed class ConsoleScriptDefinition
{
    public ConsoleScriptDefinition(string name, string resourceName, string text)
    {
        Name = name;
        ResourceName = resourceName;
        Text = text ?? "";
    }

    public string Name { get; }
    public string ResourceName { get; }
    public string Text { get; }
}

public static class ConsoleScriptCatalog
{
    const string ResourcePath = "ConsoleScripts";

    public static IReadOnlyList<ConsoleScriptDefinition> LoadScripts()
    {
        TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourcePath);
        var scripts = new List<ConsoleScriptDefinition>(assets.Length);
        for (int i = 0; i < assets.Length; i++)
        {
            TextAsset asset = assets[i];
            if (asset == null)
                continue;

            string name = ResolveScriptName(asset.name, asset.text);
            scripts.Add(new ConsoleScriptDefinition(name, asset.name, asset.text));
        }
        scripts.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return scripts;
    }

    public static bool TryFind(string name, out ConsoleScriptDefinition script)
    {
        IReadOnlyList<ConsoleScriptDefinition> scripts = LoadScripts();
        for (int i = 0; i < scripts.Count; i++)
        {
            if (string.Equals(scripts[i].Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(scripts[i].ResourceName, name, StringComparison.OrdinalIgnoreCase))
            {
                script = scripts[i];
                return true;
            }
        }

        script = null;
        return false;
    }

    static string ResolveScriptName(string fallback, string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            string[] lines = SplitLines(text);
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                    continue;
                if (trimmed.StartsWith("@name ", StringComparison.OrdinalIgnoreCase))
                {
                    string name = trimmed.Substring("@name ".Length).Trim();
                    if (name.Length > 0)
                        return name;
                }
                break;
            }
        }

        return string.IsNullOrWhiteSpace(fallback) ? "Unnamed Script" : fallback.Trim();
    }

    internal static string[] SplitLines(string text)
    {
        return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }
}

public sealed class ConsoleScriptNamesProvider : IConsoleCompletionProvider
{
    public IEnumerable<string> GetCompletions(string partialValue)
    {
        IReadOnlyList<ConsoleScriptDefinition> scripts = ConsoleScriptCatalog.LoadScripts();
        var names = new List<string>(scripts.Count);
        for (int i = 0; i < scripts.Count; i++)
            names.Add(scripts[i].Name);
        return CompletionRanker.Rank(names, partialValue);
    }
}

[CommandPrefix("script")]
public static class ConsoleScriptCommands
{
    [ConsoleCommand("list", "List available console command scripts.")]
    public static string List()
    {
        IReadOnlyList<ConsoleScriptDefinition> scripts = ConsoleScriptCatalog.LoadScripts();
        if (scripts.Count == 0)
            return "console scripts: none found under Resources/ConsoleScripts";

        var sb = new StringBuilder();
        sb.AppendLine($"console scripts ({scripts.Count}):");
        for (int i = 0; i < scripts.Count; i++)
            sb.AppendLine($"- {scripts[i].Name}");
        return sb.ToString().TrimEnd();
    }

    [ConsoleCommand("run", "Run a console command script from Resources/ConsoleScripts.")]
    public static async Awaitable<string> Run(
        [CompletionSource(typeof(ConsoleScriptNamesProvider))] string name,
        CancellationToken cancellation = default)
    {
        if (!ConsoleScriptCatalog.TryFind(name, out ConsoleScriptDefinition script))
            return $"unknown console script: '{name}'. Run script.list.";

        ServiceLocator.TryGet<IConsoleService>(out var console);
        return await ConsoleScriptRunner.RunAsync(script, console, cancellation);
    }
}

public static class ConsoleScriptAliasCommands
{
    [ConsoleCommand("run-script", "Run a console command script from Resources/ConsoleScripts.")]
    public static async Awaitable<string> RunScript(
        [CompletionSource(typeof(ConsoleScriptNamesProvider))] string name,
        CancellationToken cancellation = default)
    {
        return await ConsoleScriptCommands.Run(name, cancellation);
    }
}

public static class ConsoleScriptRunner
{
    enum StepKind
    {
        Command,
        Defer,
        Sidecar,
        Timeout,
        WaitFrames,
        WaitSeconds,
        Name,
    }

    readonly struct Step
    {
        public readonly StepKind Kind;
        public readonly string Text;
        public readonly float Number;
        public readonly int SourceLine;
        public readonly string Raw;

        public Step(StepKind kind, string text, float number, int sourceLine, string raw)
        {
            Kind = kind;
            Text = text ?? "";
            Number = number;
            SourceLine = sourceLine;
            Raw = raw ?? "";
        }
    }

    public static async Awaitable<string> RunAsync(
        ConsoleScriptDefinition script,
        IConsoleService console,
        CancellationToken cancellation)
    {
        if (script == null)
            return "console script is null";

        if (!TryParse(script, out List<Step> steps, out string parseError))
            return parseError;

        string runId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var defers = new List<string>();
        bool failed = false;
        string failure = "";
        int executedCommands = 0;
        float timeoutSeconds = 120f;

        using (ConsoleScriptRunContext context = ConsoleScriptRuntime.Begin(script.Name, runId, steps.Count))
        {
            console?.PrintLine($"script '{script.Name}' started ({steps.Count} steps, run {runId})");
            try
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Step step = steps[i];
                    context.SetStep(i + 1, step.Raw);

                    try
                    {
                        switch (step.Kind)
                        {
                            case StepKind.Name:
                                break;
                            case StepKind.Timeout:
                                timeoutSeconds = step.Number;
                                context.SetTimeout(timeoutSeconds);
                                console?.PrintLine($"script timeout: {timeoutSeconds:F1}s");
                                break;
                            case StepKind.Defer:
                                defers.Add(step.Text);
                                console?.PrintLine($"script defer: {step.Text}");
                                break;
                            case StepKind.Sidecar:
                                context.AddSidecarCommand(step.Text);
                                console?.PrintLine($"script sidecar: {step.Text}");
                                break;
                            case StepKind.WaitFrames:
                                await WaitFramesAsync(Mathf.Max(0, Mathf.RoundToInt(step.Number)), cancellation);
                                break;
                            case StepKind.WaitSeconds:
                                await WaitSecondsAsync(Mathf.Max(0f, step.Number), cancellation);
                                break;
                            case StepKind.Command:
                                executedCommands++;
                                if (!await RunCommandStepAsync(step, timeoutSeconds, console, cancellation))
                                {
                                    failed = true;
                                    failure = $"line {step.SourceLine}: command failed: {step.Text}";
                                    i = steps.Count;
                                }
                                break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failed = true;
                        failure = $"line {step.SourceLine}: {ex.Message}";
                        break;
                    }
                }
            }
            finally
            {
                await RunDefersAsync(defers, timeoutSeconds, console);
            }
        }

        if (failed)
        {
            console?.PrintError($"script '{script.Name}' failed: {failure}");
            return $"script '{script.Name}' failed after {executedCommands} command(s): {failure}";
        }

        console?.PrintLine($"script '{script.Name}' completed ({executedCommands} command(s))");
        return $"script '{script.Name}' completed ({executedCommands} command(s))";
    }

    static async Awaitable<bool> RunCommandStepAsync(
        Step step,
        float timeoutSeconds,
        IConsoleService console,
        CancellationToken cancellation)
    {
        console?.PrintLine($"> {step.Text}");
        using var timeout = CreateTimeoutTokenSource(timeoutSeconds, cancellation);
        try
        {
            ConsoleCommandResult result = await CommandExecutor.ExecuteAsync(
                step.Text,
                console,
                timeout.Token,
                printOutput: true);
            return result.Success;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            console?.PrintError($"{step.Text}: timed out after {timeoutSeconds:F1}s");
            return false;
        }
    }

    static async Awaitable RunDefersAsync(
        List<string> defers,
        float timeoutSeconds,
        IConsoleService console)
    {
        for (int i = defers.Count - 1; i >= 0; i--)
        {
            string command = defers[i];
            console?.PrintLine($"defer> {command}");
            using var timeout = CreateTimeoutTokenSource(timeoutSeconds, CancellationToken.None);
            try
            {
                ConsoleCommandResult result = await CommandExecutor.ExecuteAsync(
                    command,
                    console,
                    timeout.Token,
                    printOutput: true);
                if (!result.Success)
                    console?.PrintWarning($"defer failed: {command}");
            }
            catch (OperationCanceledException)
            {
                console?.PrintWarning($"defer timed out after {timeoutSeconds:F1}s: {command}");
            }
            catch (Exception ex)
            {
                console?.PrintWarning($"defer error: {command}: {ex.Message}");
            }
        }
    }

    static CancellationTokenSource CreateTimeoutTokenSource(float timeoutSeconds, CancellationToken cancellation)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        if (timeoutSeconds > 0f)
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return cts;
    }

    static async Awaitable WaitFramesAsync(int frames, CancellationToken cancellation)
    {
        for (int i = 0; i < frames; i++)
            await Awaitable.NextFrameAsync(cancellation);
    }

    static async Awaitable WaitSecondsAsync(float seconds, CancellationToken cancellation)
    {
        float end = Time.unscaledTime + seconds;
        while (Time.unscaledTime < end)
            await Awaitable.NextFrameAsync(cancellation);
    }

    static bool TryParse(ConsoleScriptDefinition script, out List<Step> steps, out string error)
    {
        steps = new List<Step>();
        error = null;
        string[] lines = ConsoleScriptCatalog.SplitLines(script.Text);
        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i];
            string trimmed = raw.Trim();
            int sourceLine = i + 1;
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;

            if (!trimmed.StartsWith("@", StringComparison.Ordinal))
            {
                steps.Add(new Step(StepKind.Command, trimmed, 0f, sourceLine, trimmed));
                continue;
            }

            if (!TryParseDirective(trimmed, sourceLine, out Step step, out error))
                return false;

            steps.Add(step);
        }

        return true;
    }

    static bool TryParseDirective(string line, int sourceLine, out Step step, out string error)
    {
        step = default;
        error = null;
        string body = line.Substring(1).Trim();
        List<string> tokens = CommandParser.Tokenize(body);
        if (tokens.Count == 0)
        {
            error = $"line {sourceLine}: empty directive";
            return false;
        }

        string directive = tokens[0].ToLowerInvariant();
        switch (directive)
        {
            case "name":
                step = new Step(StepKind.Name, Tail(body, "name"), 0f, sourceLine, line);
                return true;
            case "timeout":
                if (tokens.Count != 2 || !float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float timeout))
                {
                    error = $"line {sourceLine}: @timeout requires seconds";
                    return false;
                }
                step = new Step(StepKind.Timeout, "", Mathf.Max(0f, timeout), sourceLine, line);
                return true;
            case "defer":
                return TailDirective(StepKind.Defer, body, "defer", sourceLine, line, out step, out error);
            case "sidecar":
                return TailDirective(StepKind.Sidecar, body, "sidecar", sourceLine, line, out step, out error);
            case "wait":
                return TryParseWait(tokens, sourceLine, line, out step, out error);
            default:
                error = $"line {sourceLine}: unknown directive '@{directive}'";
                return false;
        }
    }

    static bool TailDirective(
        StepKind kind,
        string body,
        string directive,
        int sourceLine,
        string raw,
        out Step step,
        out string error)
    {
        string text = Tail(body, directive);
        if (string.IsNullOrWhiteSpace(text))
        {
            step = default;
            error = $"line {sourceLine}: @{directive} requires a command";
            return false;
        }

        step = new Step(kind, text.Trim(), 0f, sourceLine, raw);
        error = null;
        return true;
    }

    static bool TryParseWait(
        List<string> tokens,
        int sourceLine,
        string raw,
        out Step step,
        out string error)
    {
        step = default;
        error = null;
        if (tokens.Count != 3 || !float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float amount))
        {
            error = $"line {sourceLine}: @wait requires 'frames N' or 'seconds N'";
            return false;
        }

        if (tokens[1].Equals("frames", StringComparison.OrdinalIgnoreCase))
        {
            step = new Step(StepKind.WaitFrames, "", Mathf.Max(0f, amount), sourceLine, raw);
            return true;
        }

        if (tokens[1].Equals("seconds", StringComparison.OrdinalIgnoreCase))
        {
            step = new Step(StepKind.WaitSeconds, "", Mathf.Max(0f, amount), sourceLine, raw);
            return true;
        }

        error = $"line {sourceLine}: @wait requires 'frames' or 'seconds'";
        return false;
    }

    static string Tail(string body, string directive)
    {
        if (body.Length <= directive.Length)
            return "";
        return body.Substring(directive.Length).Trim();
    }
}
