public interface ICelestialTimeController
{
    bool IsTimeFrozen { get; }
    void ToggleTimeFrozen();
}
