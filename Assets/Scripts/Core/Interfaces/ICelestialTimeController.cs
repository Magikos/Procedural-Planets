using UnityEngine;

public interface ICelestialTimeController
{
    float TimeOfDay { get; }
    bool IsTimeFrozen { get; }
    Vector3 SunDirection { get; }
    float MoonCycleProgress { get; }
    float MoonFullness { get; }
    int MoonPhaseIndex { get; }
    bool IsMoonPhaseHeld { get; }
    bool TrySetMoonPhase(float progress);
    void SetMoonPhaseHeld(bool held);
    void SetTimeFrozen(bool frozen);
    void SetTimeOfDay(float timeOfDay);
    bool TrySetLocalTimeOfDay(float localTimeOfDay);
    void ToggleTimeFrozen();
}
