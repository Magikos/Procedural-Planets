using System;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ConsoleCommandAttribute : Attribute
{
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
