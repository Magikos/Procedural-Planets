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
    /// <summary>True if the method's last parameter is <see cref="System.Threading.CancellationToken"/>.
    /// That parameter is hidden from <see cref="Parameters"/> and injected at invocation time
    /// by <see cref="CommandExecutor"/>.</summary>
    public bool HasCancellationToken;
    // Cached scene instance for MonoTargetType.Single. Checked for null/destroyed before use;
    // repopulated via FindAnyObjectByType on first call or after the cached object is destroyed.
    internal UnityEngine.Object CachedSingleTarget;
}
