using System;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ConsoleCommandAttribute : Attribute
{
    public ConsoleReleasePolicy ReleasePolicy { get; set; } = ConsoleReleasePolicy.Unreviewed;
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public string Example { get; set; } = "";
    public readonly string Alias;
    public readonly string Description;
    public readonly MonoTargetType TargetType;

    public ConsoleCommandAttribute(
        string alias,
        string description = null,
        MonoTargetType targetType = MonoTargetType.Static)
    {
        Alias = alias;
        Description = description ?? "";
        TargetType = targetType;
    }
}
