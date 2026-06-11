using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class SettingsService : ISettingsService
{
    readonly Dictionary<Type, object> _dtos = new();

    public SettingsService()
    {
        var atmosphereSo = Resources.Load<AtmosphereSettings>("Settings/AtmosphereSettings");
        _dtos[typeof(AtmosphereDto)] = AtmosphereDto.From(atmosphereSo);
    }

    public TDto GetSettings<TDto>()
    {
        if (!_dtos.TryGetValue(typeof(TDto), out var value))
            throw new InvalidOperationException(
                $"SettingsService: no DTO registered for {typeof(TDto).Name}. " +
                "Add construction in SettingsService's constructor.");
        return (TDto)value;
    }

    public void Update<TDto>(TDto next)
    {
        _dtos[typeof(TDto)] = next;
        EventBus<SettingsChangedEvent>.Raise(new SettingsChangedEvent(typeof(TDto)));
    }
}
