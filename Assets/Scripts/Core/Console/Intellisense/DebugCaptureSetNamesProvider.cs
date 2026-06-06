using System.Collections.Generic;

/// <summary>
/// Completion provider for the <c>debug.capture-set</c> parameter. Lists all registered
/// capture set names (e.g. <c>Current Mode Only</c>, <c>Full Loop</c>, <c>Cloud Diagnostics</c>).
/// </summary>
public sealed class DebugCaptureSetNamesProvider : IConsoleCompletionProvider
{
    public IEnumerable<string> GetCompletions(string partialValue)
    {
        if (!ServiceLocator.TryGet<DebugRegistry>(out var registry))
            return System.Array.Empty<string>();

        var names = new List<string>(registry.CaptureSetCount);
        for (int i = 0; i < registry.CaptureSetCount; i++)
            names.Add(registry.GetCaptureSet(i).Name);

        return CompletionRanker.Rank(names, partialValue);
    }
}
