using System;
using System.Collections.Generic;

/// <summary>
/// Shared ranking helper for completion providers. Returns prefix matches first,
/// then substring matches, alphabetically within each rank. Empty <paramref name="partial"/>
/// yields every candidate in original order.
/// </summary>
public static class CompletionRanker
{
    static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    public static IEnumerable<string> Rank(IEnumerable<string> candidates, string partial)
    {
        if (string.IsNullOrEmpty(partial))
        {
            foreach (string s in candidates)
                if (!string.IsNullOrEmpty(s)) yield return s;
            yield break;
        }

        var prefixed = new List<string>();
        var substrings = new List<string>();
        foreach (string s in candidates)
        {
            if (string.IsNullOrEmpty(s)) continue;
            if (s.StartsWith(partial, Cmp)) prefixed.Add(s);
            else if (s.IndexOf(partial, Cmp) >= 0) substrings.Add(s);
        }
        prefixed.Sort(StringComparer.OrdinalIgnoreCase);
        substrings.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string s in prefixed) yield return s;
        foreach (string s in substrings) yield return s;
    }
}
