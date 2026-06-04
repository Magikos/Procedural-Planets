using System;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandPrefixAttribute : Attribute
{
    public readonly string Prefix;

    public CommandPrefixAttribute(string prefix)
    {
        Prefix = prefix;
    }
}
