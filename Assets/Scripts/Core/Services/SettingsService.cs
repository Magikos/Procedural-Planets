using System;
using System.Collections.Generic;

public sealed class SettingsService : ISettingsService
{
    readonly Dictionary<Type, object> _dtos = new();

    public bool IsFrozen { get; private set; }

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
        if (next is null)
            throw new ArgumentNullException(nameof(next));
        if (!_dtos.ContainsKey(typeof(TDto)))
            throw new InvalidOperationException(
                $"SettingsService: cannot update unregistered DTO {typeof(TDto).Name}.");

        _dtos[typeof(TDto)] = next;
        EventBus<SettingsChangedEvent>.Raise(new SettingsChangedEvent(typeof(TDto)));
    }

    public void Register<TDto>(TDto initial)
    {
        if (IsFrozen)
            throw new InvalidOperationException(
                $"SettingsService: registration is frozen; {typeof(TDto).Name} must be registered during world construction.");
        if (initial is null)
            throw new ArgumentNullException(nameof(initial));
        if (!_dtos.TryAdd(typeof(TDto), initial))
            throw new InvalidOperationException(
                $"SettingsService: DTO {typeof(TDto).Name} is already registered.");
    }

    public void Override<TDto>(TDto next)
    {
        if (IsFrozen)
            throw new InvalidOperationException(
                $"SettingsService: cannot apply construction override for {typeof(TDto).Name} after registration is frozen.");
        if (next is null)
            throw new ArgumentNullException(nameof(next));
        if (!_dtos.ContainsKey(typeof(TDto)))
            throw new InvalidOperationException(
                $"SettingsService: cannot override unregistered DTO {typeof(TDto).Name}.");

        _dtos[typeof(TDto)] = next;
    }

    public bool IsRegistered<TDto>() => _dtos.ContainsKey(typeof(TDto));

    public void ValidateRequired(IReadOnlyCollection<Type> requiredTypes)
    {
        if (requiredTypes == null)
            throw new ArgumentNullException(nameof(requiredTypes));

        List<string> missing = null;
        foreach (Type type in requiredTypes)
        {
            if (type != null && !_dtos.ContainsKey(type))
                (missing ??= new List<string>()).Add(type.Name);
        }

        if (missing != null)
            throw new InvalidOperationException(
                $"SettingsService: required DTOs are not registered: {string.Join(", ", missing)}.");
    }

    public void Freeze()
    {
        IsFrozen = true;
    }
}
