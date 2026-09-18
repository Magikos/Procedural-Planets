using UnityEngine;

public sealed record MoonDto(float CycleDays, float StartPhase, float Inclination, float NodeAngle,
    float Distance, float Diameter, float Brightness, float Detail, float Earthshine, Color Tint, Material Material)
{
    public static MoonDto From(MoonSettings source) => new(source.CycleDays, source.StartPhase,
        source.Inclination, source.NodeAngle, source.Distance, source.Diameter, source.Brightness,
        source.Detail, source.Earthshine, source.Tint, source.Material);

    public bool TryValidate(out string error)
    {
        error = null;
        if (!InRange(CycleDays, MoonSettings.MinCycleDays, MoonSettings.MaxCycleDays)) error = "Cycle days must be between 0.1 and 365.";
        else if (!InRange(StartPhase, 0f, 1f)) error = "Starting phase must be between 0 and 1.";
        else if (!InRange(Inclination, 0f, MoonSettings.MaxInclination)) error = "Inclination must be between 0 and 45 degrees.";
        else if (!InRange(NodeAngle, 0f, 360f)) error = "Node angle must be between 0 and 360 degrees.";
        else if (!InRange(Distance, MoonSettings.MinDistance, MoonSettings.MaxDistance)) error = "Distance must be between 2 and 100 planet radii.";
        else if (!InRange(Diameter, MoonSettings.MinDiameter, MoonSettings.MaxDiameter)) error = "Diameter must be between 0.1 and 30 degrees.";
        else if (!InRange(Brightness, 0f, MoonSettings.MaxBrightness)) error = "Brightness must be between 0 and 4.";
        else if (!InRange(Detail, 0f, MoonSettings.MaxDetail)) error = "Detail must be between 0 and 2.";
        else if (!InRange(Earthshine, 0f, MoonSettings.MaxEarthshine)) error = "Earthshine must be between 0 and 0.1.";
        else if (!InRange(Tint.r, 0f, 1f) || !InRange(Tint.g, 0f, 1f) || !InRange(Tint.b, 0f, 1f) || !InRange(Tint.a, 0f, 1f)) error = "Tint components must be between 0 and 1.";
        return error == null;
    }

    static bool InRange(float value, float min, float max) => float.IsFinite(value) && value >= min && value <= max;
}
