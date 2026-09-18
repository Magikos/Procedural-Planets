using System.Collections.Generic;
using System.Text;

public static class CommandParser
{
    public readonly struct Token
    {
        public readonly string Value;
        public readonly int Start;
        public readonly int End;
        public Token(string value, int start, int end) { Value = value; Start = start; End = end; }
    }

    public static List<string> Tokenize(string line)
    {
        var values = new List<string>();
        foreach (var token in TokenizeSpans(line)) values.Add(token.Value);
        return values;
    }

    // Source spans let completion replace an argument without rewriting its neighbours.
    public static List<Token> TokenizeSpans(string line)
    {
        var result = new List<Token>();
        if (string.IsNullOrWhiteSpace(line)) return result;
        int i = 0;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i])) { i++; continue; }
            int start = i;
            char quote = line[i] is '\'' or '"' ? line[i++] : '\0';
            int valueStart = i;
            if (quote != '\0')
            {
                while (i < line.Length && line[i] != quote) i++;
                string value = line.Substring(valueStart, i - valueStart);
                if (i < line.Length) i++;
                result.Add(new Token(value, start, i));
            }
            else
            {
                while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '\'' && line[i] != '"') i++;
                result.Add(new Token(line.Substring(start, i - start), start, i));
            }
        }
        return result;
    }

    public static bool TryGetArgument(string text, int cursor, CommandData command,
        out int parameterIndex, out int start, out int end, out string partial)
    {
        cursor = System.Math.Clamp(cursor, 0, text.Length);
        var spans = TokenizeSpans(text);
        var values = new List<string>();
        foreach (var token in spans) values.Add(token.Value);
        int tokenIndex = 1;
        for (int i = 0; i < command.Parameters.Length; i++)
        {
            var parameter = command.Parameters[i];
            start = tokenIndex < spans.Count ? spans[tokenIndex].Start : cursor;
            bool tail = parameter.Type == typeof(string) && i == command.Parameters.Length - 1;
            bool valid = true;
            int consumed = 0;
            if (tail) consumed = spans.Count - tokenIndex;
            else valid = ConsoleArgumentParsers.TryParse(values, tokenIndex, parameter.Type, out _, out consumed, out _);
            consumed = System.Math.Max(1, consumed);
            int last = System.Math.Min(spans.Count - 1, tokenIndex + consumed - 1);
            end = tail ? text.Length : tokenIndex < spans.Count ? spans[last].End : cursor;
            if (cursor <= end || tail || !valid)
            {
                if (cursor < start) start = end = cursor;
                parameterIndex = i;
                partial = string.Join(" ", Tokenize(text.Substring(start, System.Math.Max(0, cursor - start))));
                return true;
            }
            tokenIndex += consumed;
        }
        parameterIndex = -1;
        start = end = cursor;
        partial = "";
        return false;
    }

    /// <summary>
    /// Bind a token list to a command's parameters using registered argument parsers.
    /// The alias has already been consumed; <paramref name="argTokens"/> is just the arg list.
    /// </summary>
    public static bool TryBind(CommandData cmd, IReadOnlyList<string> argTokens, out object[] args, out string error)
    {
        args = null;
        error = null;
        ParameterData[] parameters = cmd.Parameters;
        object[] bound = new object[parameters.Length];
        int tokenIndex = 0;

        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterData p = parameters[i];

            if (tokenIndex >= argTokens.Count)
            {
                if (p.HasDefault)
                {
                    bound[i] = p.DefaultValue;
                    continue;
                }

                error = $"missing required argument '{p.Name}' ({p.Type.Name})";
                return false;
            }

            // A final string parameter is naturally command-tail text. Accept the remaining
            // tokens as one value so both quoted and unquoted multi-word names/messages work.
            if (p.Type == typeof(string) && i == parameters.Length - 1)
            {
                bound[i] = string.Join(" ", System.Linq.Enumerable.Skip(argTokens, tokenIndex));
                tokenIndex = argTokens.Count;
                continue;
            }

            if (!ConsoleArgumentParsers.TryParse(argTokens, tokenIndex, p.Type, out object value, out int consumed, out string parseError))
            {
                error = $"argument '{p.Name}': {parseError}";
                return false;
            }

            bound[i] = value;
            tokenIndex += consumed;
        }

        if (tokenIndex < argTokens.Count)
        {
            error = $"too many arguments (extra: '{string.Join(" ", System.Linq.Enumerable.Skip(argTokens, tokenIndex))}')";
            return false;
        }

        args = bound;
        return true;
    }
}
