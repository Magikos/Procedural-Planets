using System.Collections.Generic;
using System.Text;

public static class CommandParser
{
    /// <summary>
    /// Tokenize a raw command line into whitespace-separated tokens with quoted-string support.
    /// Both single and double quotes are honoured; tokens inside quotes preserve internal spaces.
    /// </summary>
    public static List<string> Tokenize(string line)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) return result;

        var current = new StringBuilder();
        char quote = '\0';
        bool inQuote = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuote)
            {
                if (c == quote)
                {
                    inQuote = false;
                    // Empty quoted token still counts.
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
                continue;
            }

            if (c == '"' || c == '\'')
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                inQuote = true;
                quote = c;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
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
