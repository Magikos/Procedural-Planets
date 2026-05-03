// FUTURE: DayNightChangedEvent — for systems that react to day/night transitions
// (e.g. NPC schedules, creature spawning, lighting changes).
// Raised by CelestialManager. No listeners yet.
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

// FUTURE: MoonPhaseChangedEvent — for systems that react to moon phase changes
// (e.g. werewolf mechanics, tidal effects, magic intensity).
// Raised by CelestialManager. No listeners yet.
public struct MoonPhaseChangedEvent : IGameEvent
{
    public float Phase;

    public MoonPhaseChangedEvent(float phase)
    {
        Phase = phase;
    }
}
