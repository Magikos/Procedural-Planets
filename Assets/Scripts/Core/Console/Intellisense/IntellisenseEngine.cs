using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Produces a ranked list of <see cref="Suggestion"/> entries given the current input buffer.
///
/// Decision logic:
/// <list type="bullet">
///   <item>0 tokens or empty input → no suggestions.</item>
///   <item>1 token, no trailing space → suggest command aliases containing that prefix.</item>
///   <item>1 token + trailing space, or 2+ tokens → suggest parameter values for the
///   current slot using the registered <see cref="IConsoleCompletionProvider"/>.</item>
/// </list>
///
/// Enum-typed parameters receive an <see cref="EnumCompletionProvider"/> automatically.
/// Custom providers are specified with <see cref="CompletionSourceAttribute"/> on the
/// parameter and are instantiated once then cached.
/// </summary>
public sealed class IntellisenseEngine
{
    // Pagination lives in the renderer / controller — engine returns the full ranked list.
    // Caps are wide so even large enums or command registries fit without truncation in practice.
    const int MaxSuggestions = 200;
    static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    readonly Dictionary<Type, IConsoleCompletionProvider> _providerCache = new();
    readonly List<Suggestion> _results = new(MaxSuggestions + 1);

    /// <summary>
    /// Recalculate suggestions from the current input buffer.
    /// The cursor is assumed to be at the end of <paramref name="inputText"/>.
    /// Returns a stable internal list — do not hold a reference across calls.
    /// </summary>
    public IReadOnlyList<Suggestion> Update(string inputText)
    {
        _results.Clear();

        if (string.IsNullOrEmpty(inputText))
            return _results;

        var tokens = CommandParser.Tokenize(inputText);
        if (tokens.Count == 0)
            return _results;

        bool trailingSpace = char.IsWhiteSpace(inputText[inputText.Length - 1]);
        bool inParamMode = tokens.Count > 1 || trailingSpace;

        if (!inParamMode)
        {
            SuggestAliases(tokens[0]);
            return _results;
        }

        // First token must resolve to a known command; fall back to alias suggestions if not.
        if (!ConsoleRegistry.TryGet(tokens[0], out CommandData cmd))
        {
            SuggestAliases(tokens[0]);
            return _results;
        }

        // Determine which parameter slot the cursor is in and what partial text it carries.
        // tokens[0] = alias; tokens[1..] = already-typed param values (last may be partial).
        string partial;
        int paramIndex;

        if (trailingSpace)
        {
            // Cursor is after a space: starting a fresh parameter slot.
            paramIndex = tokens.Count - 1;   // number of fully-completed param tokens
            partial = "";
        }
        else
        {
            // Last token is the partial value being completed.
            paramIndex = tokens.Count - 2;   // 0-indexed slot
            partial = tokens[tokens.Count - 1];
        }

        if (paramIndex < 0 || paramIndex >= cmd.Parameters.Length)
            return _results;

        ParameterData param = cmd.Parameters[paramIndex];
        IConsoleCompletionProvider provider = GetProvider(param);

        if (provider == null)
            return _results;

        // Build the input prefix that precedes the partial token.
        int prefixCount = trailingSpace ? tokens.Count : tokens.Count - 1;
        var sb = new StringBuilder(tokens[0]);
        for (int i = 1; i < prefixCount; i++) { sb.Append(' '); sb.Append(tokens[i]); }
        string prefix = sb.ToString();

        foreach (string completion in provider.GetCompletions(partial))
        {
            if (_results.Count >= MaxSuggestions) break;

            int matchStart = string.IsNullOrEmpty(partial)
                ? 0
                : completion.IndexOf(partial, Cmp);

            _results.Add(new Suggestion(
                cmd,
                completion,
                prefix + " " + FormatCompletionValue(completion, param),
                matchStart < 0 ? 0 : matchStart,
                partial.Length,
                param));
        }

        return _results;
    }

    static string FormatCompletionValue(string completion, ParameterData parameter)
    {
        if (parameter?.Type != typeof(string) || string.IsNullOrEmpty(completion))
            return completion;

        bool needsQuotes = completion.Any(char.IsWhiteSpace);
        if (!needsQuotes)
            return completion;

        if (!completion.Contains('"'))
            return $"\"{completion}\"";
        if (!completion.Contains('\''))
            return $"'{completion}'";

        // The tokenizer intentionally has no escape syntax. This fallback remains executable
        // because final string parameters consume the remaining command tail.
        return completion;
    }

    // -------------------------------------------------------------------------
    // Alias suggestions
    // -------------------------------------------------------------------------

    void SuggestAliases(string partial)
    {
        // Rank 0: alias starts with the typed prefix.
        // Rank 1: alias contains the typed prefix anywhere.
        // Within each rank, sort alphabetically.
        var ranked = ConsoleRegistry.Commands.Values
            .Select(cmd =>
            {
                if (cmd.Alias.StartsWith(partial, Cmp))
                    return (cmd, rank: 0, matchStart: 0);
                int idx = cmd.Alias.IndexOf(partial, Cmp);
                if (idx >= 0)
                    return (cmd, rank: 1, matchStart: idx);
                return (cmd, rank: -1, matchStart: -1);
            })
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank)
            .ThenBy(x => x.cmd.Alias)
            .Take(MaxSuggestions);

        foreach (var (cmd, _, matchStart) in ranked)
        {
            _results.Add(new Suggestion(
                cmd,
                FormatSignatureDisplay(cmd),
                cmd.Alias,
                matchStart,
                partial.Length));
        }
    }

    static string FormatSignatureDisplay(CommandData cmd) => ParameterData.FormatCommandSignature(cmd, includeDefaults: false);

    // -------------------------------------------------------------------------
    // Completion provider resolution
    // -------------------------------------------------------------------------

    IConsoleCompletionProvider GetProvider(ParameterData param)
    {
        // Explicit [CompletionSource] attribute takes priority.
        if (param.CompletionProvider != null)
        {
            if (!_providerCache.TryGetValue(param.CompletionProvider, out IConsoleCompletionProvider provider))
            {
                try
                {
                    provider = (IConsoleCompletionProvider)Activator.CreateInstance(param.CompletionProvider);
                }
                catch (Exception ex)
                {
                    LoggerProvider.Get().Log(
                        LogLevel.Warning,
                        "IntellisenseEngine",
                        $"Could not instantiate {param.CompletionProvider.Name}: {ex.Message}");
                    provider = null;
                }
                _providerCache[param.CompletionProvider] = provider;
            }
            return provider;
        }

        // Auto-synthesize providers for enum-typed and bool-typed parameters
        // (including Nullable<EnumT> and bool?).
        Type t = param.Type;
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            t = t.GetGenericArguments()[0];

        if (t.IsEnum)
        {
            if (!_providerCache.TryGetValue(t, out IConsoleCompletionProvider provider))
            {
                provider = new EnumCompletionProvider(t);
                _providerCache[t] = provider;
            }
            return provider;
        }

        if (t == typeof(bool))
        {
            if (!_providerCache.TryGetValue(t, out IConsoleCompletionProvider provider))
            {
                provider = new BoolCompletionProvider();
                _providerCache[t] = provider;
            }
            return provider;
        }

        return null;
    }
}
