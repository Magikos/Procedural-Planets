using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Splits a string with Unity-style <c>&lt;color=...&gt;...&lt;/color&gt;</c> markup into
/// a sequence of typed colour spans. Tags do NOT nest in v1 (per console design);
/// any <c>&lt;/color&gt;</c> closes back to the default colour.
/// </summary>
public static class ConsoleColorTagParser
{
    static readonly Dictionary<string, Color> NamedColors = new(System.StringComparer.OrdinalIgnoreCase)
    {
        { "red",     Color.red },
        { "green",   Color.green },
        { "blue",    Color.blue },
        { "yellow",  Color.yellow },
        { "cyan",    Color.cyan },
        { "magenta", Color.magenta },
        { "white",   Color.white },
        { "black",   Color.black },
        { "grey",    Color.grey },
        { "gray",    Color.grey },
        { "orange",  new Color(1f, 0.6f, 0.1f, 1f) },
    };

    public static void Parse(string text, Color defaultColor, List<TextSpan> result)
    {
        result.Clear();
        if (string.IsNullOrEmpty(text)) return;

        var sb = new StringBuilder(text.Length);
        Color current = defaultColor;

        int i = 0;
        while (i < text.Length)
        {
            // Escape: '\<' produces a literal '<' (used so command output can quote markup).
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '<')
            {
                sb.Append('<');
                i += 2;
                continue;
            }

            if (text[i] == '<')
            {
                if (TryParseOpenTag(text, i, out Color c, out int len))
                {
                    if (sb.Length > 0) { result.Add(new TextSpan(current, sb.ToString())); sb.Clear(); }
                    current = c;
                    i += len;
                    continue;
                }
                if (TryParseCloseTag(text, i, out int closeLen))
                {
                    if (sb.Length > 0) { result.Add(new TextSpan(current, sb.ToString())); sb.Clear(); }
                    current = defaultColor;
                    i += closeLen;
                    continue;
                }
            }

            sb.Append(text[i]);
            i++;
        }

        if (sb.Length > 0)
            result.Add(new TextSpan(current, sb.ToString()));
    }

    static bool TryParseOpenTag(string text, int start, out Color color, out int length)
    {
        color = Color.white;
        length = 0;

        const string prefix = "<color=";
        if (start + prefix.Length >= text.Length) return false;
        for (int k = 0; k < prefix.Length; k++)
            if (char.ToLowerInvariant(text[start + k]) != prefix[k]) return false;

        int closing = text.IndexOf('>', start + prefix.Length);
        if (closing < 0) return false;

        string value = text.Substring(start + prefix.Length, closing - start - prefix.Length).Trim();
        if (!TryResolveColor(value, out color)) return false;

        length = closing - start + 1;
        return true;
    }

    static bool TryParseCloseTag(string text, int start, out int length)
    {
        length = 0;
        const string close = "</color>";
        if (start + close.Length > text.Length) return false;
        for (int k = 0; k < close.Length; k++)
            if (char.ToLowerInvariant(text[start + k]) != close[k]) return false;
        length = close.Length;
        return true;
    }

    static bool TryResolveColor(string value, out Color color)
    {
        if (value.StartsWith("#"))
            return ColorUtility.TryParseHtmlString(value, out color);

        return NamedColors.TryGetValue(value, out color);
    }
}
