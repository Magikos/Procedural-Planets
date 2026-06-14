using UnityEngine;

public interface ICelestialTimeController
{
    float TimeOfDay { get; }
    bool IsTimeFrozen { get; }
    Vector3 SunDirection { get; }
    void SetTimeFrozen(bool frozen);
    void SetTimeOfDay(float timeOfDay);
    bool TrySetLocalTimeOfDay(float localTimeOfDay);
    void ToggleTimeFrozen();
}
