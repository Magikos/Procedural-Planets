using System.Collections.Generic;

/// <summary>
/// Completion provider that returns all registered command aliases.
/// Used by the <c>help</c> command's first parameter so that
/// <c>help [space]</c> opens an intellisense popup listing every command.
/// </summary>
public sealed class CommandNamesProvider : IConsoleCompletionProvider
{
    public IEnumerable<string> GetCompletions(string partialValue)
    {
        foreach (string alias in ConsoleRegistry.Commands.Keys)
        {
            if (string.IsNullOrEmpty(partialValue) ||
                alias.StartsWith(partialValue, System.StringComparison.OrdinalIgnoreCase))
                yield return alias;
        }
    }
}
