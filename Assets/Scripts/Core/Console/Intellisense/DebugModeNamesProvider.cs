using System.Collections.Generic;

/// <summary>
/// Completion provider for the <c>debug.mode</c> parameter. Lists all registered debug
/// mode names (e.g. <c>Off</c>, <c>CloudDensity</c>, <c>WaterMask</c>).
/// </summary>
public sealed class DebugModeNamesProvider : IConsoleCompletionProvider
{
    public IEnumerable<string> GetCompletions(string partialValue)
    {
        if (!ServiceLocator.TryGet<DebugRegistry>(out var registry))
            return System.Array.Empty<string>();

        var names = new List<string>(registry.ModeIds.Count);
        for (int i = 0; i < registry.ModeIds.Count; i++)
            names.Add(registry.GetModeById(registry.ModeIds[i]).Name);

        return CompletionRanker.Rank(names, partialValue);
    }
}
