using System;
using System.Collections.Generic;

public sealed class SettingsService : ISettingsService
{
    readonly Dictionary<Type, object> _dtos = new();

    public TDto GetSettings<TDto>()
    {
        if (!_dtos.TryGetValue(typeof(TDto), out var value))
            throw new InvalidOperationException(
                $"SettingsService: no DTO registered for {typeof(TDto).Name}. " +
                "The owning consumer must Register the DTO before requesting it.");
        return (TDto)value;
    }

    public void Update<TDto>(TDto next)
    {
        _dtos[typeof(TDto)] = next;
        EventBus<SettingsChangedEvent>.Raise(new SettingsChangedEvent(typeof(TDto)));
    }

    public void Register<TDto>(TDto initial)
    {
        _dtos[typeof(TDto)] = initial;
    }

    public bool IsRegistered<TDto>() => _dtos.ContainsKey(typeof(TDto));
}
