using System;
using System.Collections.Generic;
using System.Linq;

public sealed class IntellisenseEngine
{
    // Pagination lives in the renderer / controller — engine returns the full ranked list.

    static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    readonly Dictionary<Type, IConsoleCompletionProvider> _providerCache = new();
    readonly List<Suggestion> _results = new();

    public IReadOnlyList<Suggestion> Update(string inputText, int cursor = -1)
    {
        _results.Clear();
        inputText ??= "";
        cursor = cursor < 0 ? inputText.Length : Math.Clamp(cursor, 0, inputText.Length);
        var tokens = CommandParser.TokenizeSpans(inputText);
        if (tokens.Count == 0)
        {
            foreach (var family in CommandCatalog.Families)
            {
                string completion = family.Key + ".";
                _results.Add(new Suggestion(family.First(), $"{completion} ({family.Count()} commands)",
                    completion, 0, 0, isGroup: true));
            }
            return _results;
        }
        if (tokens.Count == 0 || cursor <= tokens[0].End)
        {
            int start = tokens.Count == 0 ? cursor : tokens[0].Start;
            int end = tokens.Count == 0 ? cursor : tokens[0].End;
            SuggestAliases(inputText.Substring(start, Math.Max(0, cursor - start)), inputText, start, end);
            return _results;
        }
        if (!ConsoleRegistry.TryGet(tokens[0].Value, out var cmd) || !ConsoleCommandPolicy.CanExecute(cmd))
            return _results;
        if (!CommandParser.TryGetArgument(inputText, cursor, cmd, out int index, out int argStart, out int argEnd, out string partial))
            return _results;
        var parameter = cmd.Parameters[index];
        var provider = GetProvider(parameter);
        if (provider == null) return _results;
        foreach (string completion in provider.GetCompletions(partial))
        {
            string value = FormatCompletionValue(completion, parameter);
            int match = completion.IndexOf(partial, Cmp);
            _results.Add(new Suggestion(cmd, completion,
                inputText.Substring(0, argStart) + value + inputText.Substring(argEnd),
                Math.Max(0, match), match < 0 ? 0 : partial.Length, parameter, argStart + value.Length));
        }
        return _results;
    }

    static string FormatCompletionValue(string completion, ParameterData parameter)
    {
        if (parameter?.Type != typeof(string) || string.IsNullOrEmpty(completion))
            return completion;

        bool needsQuotes = completion.Any(char.IsWhiteSpace) || completion.Contains('"') || completion.Contains('\'');
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

    void SuggestAliases(string partial, string input, int start, int end)
    {
        foreach (var entry in CommandCatalog.MatchNames(partial))
        {
            int match = entry.alias.IndexOf(partial, Cmp);
            string display = entry.alias == entry.cmd.Alias ? FormatSignatureDisplay(entry.cmd)
                : entry.alias + " → " + entry.cmd.Alias;
            _results.Add(new Suggestion(entry.cmd, display,
                input.Substring(0, start) + entry.alias + input.Substring(end),
                Math.Max(0, match), match < 0 ? 0 : partial.Length, null, start + entry.alias.Length));
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
