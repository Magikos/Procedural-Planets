using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Default completion provider for enum-typed parameters.
/// Automatically used by <see cref="IntellisenseEngine"/> when a parameter is an enum
/// and no explicit <see cref="CompletionSourceAttribute"/> is present on that parameter.
/// </summary>
public sealed class EnumCompletionProvider : IConsoleCompletionProvider
{
    readonly Type _enumType;

    public EnumCompletionProvider(Type enumType)
    {
        if (enumType == null) throw new ArgumentNullException(nameof(enumType));
        if (!enumType.IsEnum) throw new ArgumentException($"{enumType.Name} is not an enum.", nameof(enumType));
        _enumType = enumType;
    }

    public IEnumerable<string> GetCompletions(string partialValue)
    {
        StringComparison cmp = StringComparison.OrdinalIgnoreCase;
        return Enum.GetNames(_enumType)
            .Where(n => string.IsNullOrEmpty(partialValue) || n.StartsWith(partialValue, cmp));
    }
}
