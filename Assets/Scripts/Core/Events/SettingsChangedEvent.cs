using System;

public readonly struct SettingsChangedEvent : IGameEvent
{
    public readonly Type DtoType;

    public SettingsChangedEvent(Type dtoType)
    {
        DtoType = dtoType;
    }
}
