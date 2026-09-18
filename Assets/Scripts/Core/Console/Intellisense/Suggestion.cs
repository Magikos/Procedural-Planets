/// <summary>
/// A single intellisense suggestion produced by <see cref="IntellisenseEngine"/>.
/// </summary>
public readonly struct Suggestion
{
    /// <summary>The command this suggestion resolves to.</summary>
    public readonly CommandData Command;

    /// <summary>
    /// Human-readable text shown in the popup.
    /// For alias suggestions this is the full signature; for param-value suggestions
    /// it is just the completion value.
    /// </summary>
    public readonly string DisplayText;

    /// <summary>
    /// Full input-buffer string to substitute when this suggestion is accepted.
    /// E.g. for an alias suggestion: "weather.wind-speed".
    /// For a param-value suggestion: "weather.state Clear".
    /// </summary>
    public readonly string CompletionText;
    public readonly int CompletionCursor;
    public readonly bool IsGroup;

    /// <summary>Start index of the matched substring within <see cref="DisplayText"/>.</summary>
    public readonly int MatchStart;

    /// <summary>Length of the matched substring within <see cref="DisplayText"/>.</summary>
    public readonly int MatchLength;

    /// <summary>Non-null when this is a parameter-value suggestion.</summary>
    public readonly ParameterData Parameter;

    public Suggestion(
        CommandData command,
        string displayText,
        string completionText,
        int matchStart,
        int matchLength,
        ParameterData parameter = null,
        int completionCursor = -1,
        bool isGroup = false)
    {
        Command = command;
        DisplayText = displayText ?? "";
        CompletionText = completionText ?? "";
        CompletionCursor = completionCursor < 0 ? CompletionText.Length : completionCursor;
        IsGroup = isGroup;
        MatchStart = matchStart < 0 ? 0 : matchStart;
        MatchLength = matchLength < 0 ? 0 : matchLength;
        Parameter = parameter;
    }
}
