using System;
using System.Reflection;

public sealed class CommandData
{
    public string Alias;
    public string Description;
    public MonoTargetType TargetType;
    public Type DeclaringType;
    public MethodInfo Method;
    public ParameterData[] Parameters;
    public Type ReturnType;
    public bool IsAsync;
}
