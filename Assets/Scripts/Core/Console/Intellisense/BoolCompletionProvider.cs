using System.Collections.Generic;

/// <summary>
/// Default completion provider for <c>bool</c> / <c>bool?</c> parameters.
/// Auto-attached by <see cref="IntellisenseEngine"/>; no <see cref="CompletionSourceAttribute"/> needed.
/// </summary>
public sealed class BoolCompletionProvider : IConsoleCompletionProvider
{
    static readonly string[] Values = { "true", "false" };

    public IEnumerable<string> GetCompletions(string partialValue)
        => CompletionRanker.Rank(Values, partialValue);
}
