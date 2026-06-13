using System.Collections.Generic;
using UnityEngine;

// Builds the colored span list for the console input line: async spinner prefix, prompt,
// syntax-highlighted typed text with cursor, ghost completion, and parameter-slot hints.
// Reuses internal span lists so per-frame span building stays allocation-free.
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

        // Ghost completion and parameter hint only when the cursor is at the end of the line
        // (mid-line editing makes inline hints visually confusing).
        bool atEnd = cursorPos == typed.Length;
        if (!atEnd) return _inputSpans;

        if (ghostActive)
        {
            string completion = suggestions[0].CompletionText;
            _inputSpans.Add(new TextSpan(theme.InputGhost, completion.Substring(typed.Length)));
            return _inputSpans;
        }

        if (typed.Length == 0 || pending) return _inputSpans;

        // Param-slot ghost: show <type: name> for every remaining slot.
        // The current in-progress slot (or the next slot to start) is omitted from the ghost —
        // we don't redundantly hint at what the user is already typing.
        var tokens = CommandParser.Tokenize(typed);
        if (tokens.Count == 0) return _inputSpans;
        if (!ConsoleRegistry.TryGet(tokens[0], out var hintCmd)) return _inputSpans;
        if (hintCmd.Parameters.Length == 0) return _inputSpans;

        bool trailingSpace = char.IsWhiteSpace(typed[typed.Length - 1]);
        // showFrom = first param slot to display as ghost.
        //   "alias" / "alias " (alias-only)            → 0   (show every slot)
        //   "alias 5" (mid-typing slot 0)              → 1   (slot 0 in-progress, show slot 1 onwards)
        //   "alias 5 " (slot 0 done, starting slot 1)  → 1   (slot 1 about to start, show it + rest)
        // Unified: showFrom = max(0, tokens.Count - 1).
        int showFrom = tokens.Count - 1;
        if (showFrom < 0) showFrom = 0;
        if (showFrom >= hintCmd.Parameters.Length) return _inputSpans;

        bool first = true;
        for (int i = showFrom; i < hintCmd.Parameters.Length; i++)
        {
            ParameterData p = hintCmd.Parameters[i];
            bool needsLeadingSpace = !first || !trailingSpace;
            string body = p.HasDefault
                ? $"[{p.DisplayTypeName}: {p.Name}]"
                : $"<{p.DisplayTypeName}: {p.Name}>";
            Color color = p.HasDefault ? theme.InputHintOptional : theme.InputHintRequired;
            _inputSpans.Add(new TextSpan(color, (needsLeadingSpace ? " " : "") + body));
            first = false;
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
            if (text[i] == '"')
            {
                int end = text.IndexOf('"', i + 1);
                int len = (end < 0 ? text.Length : end + 1) - i;
                spans.Add(new TextSpan(theme.InputString, text.Substring(i, len)));
                i = end < 0 ? text.Length : end + 1;
            }
            else
            {
                int start = i;
                while (i < text.Length && text[i] != '"') i++;
                if (i > start)
                    spans.Add(new TextSpan(theme.InputValue, text.Substring(start, i - start)));
            }
        }
    }
}
