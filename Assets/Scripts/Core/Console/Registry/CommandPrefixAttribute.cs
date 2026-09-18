using System;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandPrefixAttribute : Attribute
{
    public ConsoleReleasePolicy ReleasePolicy { get; set; } = ConsoleReleasePolicy.Unreviewed;
    public string Group { get; set; }
    public readonly string Prefix;

    public CommandPrefixAttribute(string prefix)
    {
        Prefix = prefix;
    }
}
