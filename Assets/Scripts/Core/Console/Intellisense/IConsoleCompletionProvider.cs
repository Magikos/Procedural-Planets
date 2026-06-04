using System.Collections.Generic;

/// <summary>
/// Supplies value completions for a specific command parameter.
/// Implement this interface and reference it via <see cref="CompletionSourceAttribute"/>
/// to provide custom suggestions (e.g. biome names, scene names).
/// Enum parameters receive an <see cref="EnumCompletionProvider"/> automatically —
/// no attribute required.
/// </summary>
public interface IConsoleCompletionProvider
{
    /// <summary>
    /// Return candidate values that match <paramref name="partialValue"/>.
    /// An empty or null <paramref name="partialValue"/> should return all candidates.
    /// </summary>
    IEnumerable<string> GetCompletions(string partialValue);
}
