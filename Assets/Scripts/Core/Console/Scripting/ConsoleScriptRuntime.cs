using System;
using System.Collections.Generic;
using System.Text;

public sealed class ConsoleScriptRunContext : IDisposable
{
    readonly List<string> _sidecarCommands = new();
    bool _disposed;

    internal ConsoleScriptRunContext(string scriptName, string runId, int totalSteps)
    {
        ScriptName = scriptName;
        RunId = runId;
        TotalSteps = totalSteps;
    }

    public string ScriptName { get; }
    public string RunId { get; }
    public int TotalSteps { get; }
    public int StepIndex { get; private set; }
    public string StepCommand { get; private set; }
    public float TimeoutSeconds { get; private set; } = 120f;
    public IReadOnlyList<string> SidecarCommands => _sidecarCommands;

    public void SetStep(int stepIndex, string stepCommand)
    {
        StepIndex = stepIndex;
        StepCommand = stepCommand ?? "";
    }

    public void SetTimeout(float seconds)
    {
        TimeoutSeconds = Math.Max(0f, seconds);
    }

    public void AddSidecarCommand(string commandLine)
    {
        if (!string.IsNullOrWhiteSpace(commandLine))
            _sidecarCommands.Add(commandLine.Trim());
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ConsoleScriptRuntime.End(this);
    }
}

public static class ConsoleScriptRuntime
{
    static readonly Stack<ConsoleScriptRunContext> ContextStack = new();

    public static ConsoleScriptRunContext Current => ContextStack.Count > 0 ? ContextStack.Peek() : null;

    public static ConsoleScriptRunContext Begin(string scriptName, string runId, int totalSteps)
    {
        var context = new ConsoleScriptRunContext(scriptName, runId, totalSteps);
        ContextStack.Push(context);
        return context;
    }

    internal static void End(ConsoleScriptRunContext context)
    {
        if (context == null || ContextStack.Count == 0)
            return;

        if (ReferenceEquals(ContextStack.Peek(), context))
        {
            ContextStack.Pop();
            return;
        }

        var restore = new Stack<ConsoleScriptRunContext>();
        while (ContextStack.Count > 0)
        {
            ConsoleScriptRunContext current = ContextStack.Pop();
            if (ReferenceEquals(current, context))
                break;
            restore.Push(current);
        }

        while (restore.Count > 0)
            ContextStack.Push(restore.Pop());
    }

    public static string GetCaptureFilePrefix()
    {
        ConsoleScriptRunContext context = Current;
        if (context == null)
            return "";

        string script = DebugScreenshotFiles.SanitizeFilePart(context.ScriptName);
        string run = DebugScreenshotFiles.SanitizeFilePart(context.RunId);
        return $"{script}-{run}-step{context.StepIndex:000}";
    }

    public static void AppendMetadata(StringBuilder sb)
    {
        ConsoleScriptRunContext context = Current;
        if (context == null)
            return;

        sb.AppendLine("--- Console Script ---");
        sb.AppendLine($"Script: {context.ScriptName}");
        sb.AppendLine($"RunId: {context.RunId}");
        sb.AppendLine($"Step: {context.StepIndex}/{context.TotalSteps}");
        sb.AppendLine($"StepCommand: {context.StepCommand}");
        sb.AppendLine($"TimeoutSeconds: {context.TimeoutSeconds:F1}");

        IReadOnlyList<string> sidecars = context.SidecarCommands;
        if (sidecars.Count > 0)
        {
            sb.AppendLine("Sidecar:");
            for (int i = 0; i < sidecars.Count; i++)
                AppendSidecarCommand(sb, sidecars[i]);
        }
        sb.AppendLine();
    }

    static void AppendSidecarCommand(StringBuilder sb, string commandLine)
    {
        sb.AppendLine($"> {commandLine}");
        ConsoleCommandResult result = CommandExecutor.ExecuteImmediate(commandLine);
        if (!result.Success)
        {
            sb.AppendLine($"  ERROR: {result.Error}");
            return;
        }

        if (!result.HasOutput)
        {
            sb.AppendLine("  (no output)");
            return;
        }

        string[] lines = result.Output.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
                continue;
            sb.AppendLine($"  {lines[i]}");
        }
    }
}
