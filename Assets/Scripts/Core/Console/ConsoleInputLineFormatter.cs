using System.Collections.Generic;
using UnityEngine;

// Builds the colored span list for the console input line: async spinner prefix, prompt,
// syntax-highlighted typed text with cursor, ghost completion, and parameter-slot hints.
// Reuses span lists; argument hints share the command parser's source context.
public sealed class ConsoleInputLineFormatter
{
    readonly List<TextSpan> _inputSpans = new();
    readonly List<TextSpan> _typedSpans = new();

    public List<TextSpan> Build(
        ConsoleTheme theme,
        string typed,
        int cursorPos,
        bool cursorOn,
        IReadOnlyList<Suggestion> suggestions,
        bool ghostActive,
        bool pending,
        string pendingDots)
    {
        _inputSpans.Clear();

        // Animated async indicator before the prompt.
        if (pending)
            _inputSpans.Add(new TextSpan(theme.InputGhost, pendingDots + " "));
        _inputSpans.Add(new TextSpan(theme.InputPrompt, "> "));

        // Build typed-text spans into a temp list so we can inject the cursor at the right offset.
        _typedSpans.Clear();
        AppendTypedSpans(theme, _typedSpans, typed);
        InsertCursorIntoSpans(_typedSpans, cursorPos, cursorOn ? Color.white : Color.clear);
        _inputSpans.AddRange(_typedSpans);

        bool atEnd = cursorPos == typed.Length;
        if (ghostActive && atEnd)
        {
            string completion = suggestions[0].CompletionText;
            _inputSpans.Add(new TextSpan(theme.InputGhost, completion.Substring(typed.Length)));
            return _inputSpans;
        }
        if (typed.Length == 0 || pending) return _inputSpans;
        var tokens = CommandParser.Tokenize(typed);
        if (tokens.Count == 0 || !ConsoleRegistry.TryGet(tokens[0], out var command)) return _inputSpans;
        if (CommandParser.TryGetArgument(typed, cursorPos, command, out int index, out _, out _, out _))
        {
            var parameter = command.Parameters[index];
            string hint = parameter.HasDefault
                ? $"[{parameter.Name}: {parameter.DisplayTypeName}, optional]"
                : $"<{parameter.Name}: {parameter.DisplayTypeName}>";
            if (!string.IsNullOrEmpty(parameter.Description)) hint += " " + parameter.Description;
            _inputSpans.Add(new TextSpan(theme.InputHintOptional, "   " + hint));
        }
        return _inputSpans;
    }

    static void AppendTypedSpans(ConsoleTheme theme, List<TextSpan> spans, string typed)
    {
        if (typed.Length == 0) return;
        int spaceIdx = typed.IndexOf(' ');
        if (spaceIdx < 0)
        {
            spans.Add(new TextSpan(theme.InputCommand, typed));
        }
        else
        {
            spans.Add(new TextSpan(theme.InputCommand, typed.Substring(0, spaceIdx)));
            AppendSyntaxSpans(theme, spans, typed.Substring(spaceIdx));
        }
    }

    static void InsertCursorIntoSpans(List<TextSpan> spans, int cursorPos, Color cursorColor)
    {
        int running = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            int spanLen = spans[i].Text.Length;
            if (running + spanLen >= cursorPos)
            {
                int splitPos = cursorPos - running;
                if (splitPos == 0)
                {
                    spans.Insert(i, new TextSpan(cursorColor, "|"));
                    return;
                }
                if (splitPos == spanLen)
                {
                    spans.Insert(i + 1, new TextSpan(cursorColor, "|"));
                    return;
                }
                TextSpan original = spans[i];
                spans[i] = new TextSpan(original.Color, original.Text.Substring(0, splitPos));
                spans.Insert(i + 1, new TextSpan(cursorColor, "|"));
                spans.Insert(i + 2, new TextSpan(original.Color, original.Text.Substring(splitPos)));
                return;
            }
            running += spanLen;
        }
        spans.Add(new TextSpan(cursorColor, "|"));
    }

    static void AppendSyntaxSpans(ConsoleTheme theme, List<TextSpan> spans, string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] is '"' or '\'')
            {
                int end = text.IndexOf(text[i], i + 1);
                int len = (end < 0 ? text.Length : end + 1) - i;
                spans.Add(new TextSpan(theme.InputString, text.Substring(i, len)));
                i = end < 0 ? text.Length : end + 1;
            }
            else
            {
                int start = i;
                while (i < text.Length && text[i] != '"' && text[i] != '\'') i++;
                if (i > start)
                    spans.Add(new TextSpan(theme.InputValue, text.Substring(start, i - start)));
            }
        }
    }
}
