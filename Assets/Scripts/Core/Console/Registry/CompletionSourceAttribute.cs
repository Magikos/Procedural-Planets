using System;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CompletionSourceAttribute : Attribute
{
    public readonly Type ProviderType;

    public CompletionSourceAttribute(Type providerType)
    {
        ProviderType = providerType;
    }
}
