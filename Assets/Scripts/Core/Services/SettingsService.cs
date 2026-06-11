using System;
using System.Collections.Generic;

public sealed class SettingsService : ISettingsService
{
    readonly Dictionary<Type, object> _dtos = new();

    public SettingsService()
    {
        // DTO construction lives here, one explicit Resources.Load + From(SO) per DTO.
        // Empty for now; first DTO lands with the GrassPlacementController migration.
    }

    public TDto GetSettings<TDto>() where TDto : struct
    {
        if (!_dtos.TryGetValue(typeof(TDto), out var value))
            throw new InvalidOperationException(
                $"SettingsService: no DTO registered for {typeof(TDto).Name}. " +
                "Add construction in SettingsService's constructor.");
        return (TDto)value;
    }

    public void Update<TDto>(TDto next) where TDto : struct
    {
        _dtos[typeof(TDto)] = next;
        EventBus<SettingsChangedEvent>.Raise(new SettingsChangedEvent(typeof(TDto)));
    }
}
