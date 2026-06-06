using System;

public sealed class ParameterData
{
    public string Name;
    public Type Type;
    public bool HasDefault;
    public object DefaultValue;
    public string Description;
    public Type CompletionProvider;

    /// <summary>
    /// Display-friendly type name for hints / signatures / usage strings.
    /// Maps primitives to C# keywords (`int`, `float`, etc.) and unwraps
    /// <c>Nullable&lt;T&gt;</c> to <c>T?</c> instead of showing the raw CLR
    /// <c>Nullable`1</c> form.
    /// </summary>
    public string DisplayTypeName => FormatTypeName(Type);

    public static string FormatTypeName(Type t)
    {
        if (t == null) return "?";
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            return FormatTypeName(t.GetGenericArguments()[0]) + "?";
        if (t == typeof(int)) return "int";
        if (t == typeof(float)) return "float";
        if (t == typeof(double)) return "double";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(string)) return "string";
        return t.Name;
    }

    /// <summary>
    /// Format a command's signature line. Shared by <c>CommandExecutor</c> usage hints,
    /// <c>IntellisenseEngine</c> popup display, and the <c>help</c> command output.
    /// <paramref name="includeDefaults"/> appends <c>=value</c> for optional parameters
    /// (used in usage hints and help; suppressed in the popup to keep rows compact).
    /// </summary>
    public static string FormatCommandSignature(CommandData cmd, bool includeDefaults)
    {
        if (cmd.Parameters.Length == 0) return cmd.Alias;

        var sb = new System.Text.StringBuilder(cmd.Alias);
        foreach (ParameterData p in cmd.Parameters)
        {
            if (p.HasDefault)
                sb.Append(includeDefaults
                    ? $" [{p.DisplayTypeName}: {p.Name}={p.DefaultValue}]"
                    : $" [{p.DisplayTypeName}: {p.Name}]");
            else
                sb.Append($" <{p.DisplayTypeName}: {p.Name}>");
        }
        return sb.ToString();
    }
}
