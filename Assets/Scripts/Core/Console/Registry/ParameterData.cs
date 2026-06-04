using System;

public sealed class ParameterData
{
    public string Name;
    public Type Type;
    public bool HasDefault;
    public object DefaultValue;
    public string Description;
    public Type CompletionProvider;
}
