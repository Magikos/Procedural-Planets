// planned: consumed by systems that react to day/night transitions — NPC schedules, creature
// spawning, lighting changes. Raised by CelestialManager already; no listeners yet, which is
// expected until those systems exist.
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

// planned: consumed by systems that react to moon phase changes — werewolf mechanics, tidal
// effects, magic intensity. Raised by CelestialManager already; no listeners yet, which is
// expected until those systems exist.
public struct MoonPhaseChangedEvent : IGameEvent
{
    public float Phase;

    public MoonPhaseChangedEvent(float phase)
    {
        Phase = phase;
    }
}
