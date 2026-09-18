public enum ScatterWaterHabitat : byte
{
    Unrestricted = 0,
    Freshwater = 1,
    LakeOnly = 2,
}

public static class ScatterWaterPlacement
{
    public static bool Passes(ScatterWaterHabitat habitat, WaterBodyKind kind, bool river, float speed,
        float maxSpeed, bool floating, float bedAltitude)
    {
        if (floating && bedAltitude >= 0f) return false;
        if (habitat == ScatterWaterHabitat.Unrestricted) return true;
        if (river) return habitat == ScatterWaterHabitat.Freshwater && speed <= maxSpeed;
        return kind == WaterBodyKind.Lake;
    }
}
