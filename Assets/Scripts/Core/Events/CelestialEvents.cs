public struct DayNightChangedEvent : IGameEvent
{
    public float TimeOfDay;
    public bool IsDay;

    public DayNightChangedEvent(float timeOfDay, bool isDay)
    {
        TimeOfDay = timeOfDay;
        IsDay = isDay;
    }
}

public struct MoonPhaseChangedEvent : IGameEvent
{
    public float Phase;

    public MoonPhaseChangedEvent(float phase)
    {
        Phase = phase;
    }
}
