using System;

public interface IConsoleArgumentParser
{
    bool CanParse(Type type);

    /// <summary>
    /// Attempt to parse a single token (or peek ahead for multi-token types like Vector3).
    /// </summary>
    /// <param name="tokens">All remaining tokens starting at the parameter's first token.</param>
    /// <param name="type">Target parameter type.</param>
    /// <param name="value">On success, the parsed value.</param>
    /// <param name="consumed">Number of tokens consumed (≥ 1 on success, 0 on failure).</param>
    /// <param name="error">On failure, human-readable error message.</param>
    bool TryParse(System.Collections.Generic.IReadOnlyList<string> tokens, int startIndex, Type type, out object value, out int consumed, out string error);
}
