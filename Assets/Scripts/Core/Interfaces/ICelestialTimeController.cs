public interface ICelestialTimeController
{
    float TimeOfDay { get; }
    bool IsTimeFrozen { get; }
    void SetTimeFrozen(bool frozen);
    void SetTimeOfDay(float timeOfDay);
    bool TrySetLocalTimeOfDay(float localTimeOfDay);
    void ToggleTimeFrozen();
}
