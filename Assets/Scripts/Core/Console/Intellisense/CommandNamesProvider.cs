using System.Collections.Generic;

/// <summary>
/// Completion provider that returns all registered command aliases. Used by the
/// <c>help</c> command's first parameter so <c>help [space]</c> lists every command
/// and <c>help &lt;partial&gt;</c> filters by substring (prefix matches ranked first).
/// </summary>
public sealed class CommandNamesProvider : IConsoleCompletionProvider
{
    public IEnumerable<string> GetCompletions(string partialValue)
        => CompletionRanker.Rank(ConsoleRegistry.Commands.Keys, partialValue);
}
