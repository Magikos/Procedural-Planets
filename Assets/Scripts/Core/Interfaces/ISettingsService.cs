using System;
using System.Collections.Generic;

public interface ISettingsService
{
    bool IsFrozen { get; }

    TDto GetSettings<TDto>();
    void Update<TDto>(TDto next);
    void Register<TDto>(TDto initial);
    void Override<TDto>(TDto next);
    bool IsRegistered<TDto>();
    void ValidateRequired(IReadOnlyCollection<Type> requiredTypes);
    void Freeze();
}

public interface IWorldSettingsOverride
{
    Type DtoType { get; }
    void Apply(ISettingsService settings);
}

public sealed class WorldSettingsOverride<TDto> : IWorldSettingsOverride
{
    public Type DtoType => typeof(TDto);
    public TDto Value { get; }

    public WorldSettingsOverride(TDto value)
    {
        Value = value;
    }

    public void Apply(ISettingsService settings)
    {
        settings.Override(Value);
    }
}
