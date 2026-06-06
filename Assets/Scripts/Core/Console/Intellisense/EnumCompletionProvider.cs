using System;
using System.Collections.Generic;

/// <summary>
/// Default completion provider for enum-typed parameters. Auto-attached by
/// <see cref="IntellisenseEngine"/> for any enum parameter without an explicit
/// <see cref="CompletionSourceAttribute"/>.
/// </summary>
public sealed class EnumCompletionProvider : IConsoleCompletionProvider
{
    readonly string[] _names;

    public EnumCompletionProvider(Type enumType)
    {
        if (enumType == null) throw new ArgumentNullException(nameof(enumType));
        if (!enumType.IsEnum) throw new ArgumentException($"{enumType.Name} is not an enum.", nameof(enumType));
        _names = Enum.GetNames(enumType);
    }

    public IEnumerable<string> GetCompletions(string partialValue)
        => CompletionRanker.Rank(_names, partialValue);
}
