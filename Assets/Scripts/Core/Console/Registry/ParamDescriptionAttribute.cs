using System;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParamDescriptionAttribute : Attribute
{
    public readonly string Description;

    public ParamDescriptionAttribute(string description)
    {
        Description = description;
    }
}
