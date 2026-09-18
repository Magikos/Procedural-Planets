public readonly struct WaterQualityProfile
{
    public readonly int ReflectionSteps, ShaftSteps, RippleLimit, ProbeResolution;
    public readonly float ProbeInterval;

    WaterQualityProfile(int reflectionSteps, int shaftSteps, int rippleLimit, int probeResolution, float probeInterval)
    {
        ReflectionSteps = reflectionSteps;
        ShaftSteps = shaftSteps;
        RippleLimit = rippleLimit;
        ProbeResolution = probeResolution;
        ProbeInterval = probeInterval;
    }

    public static WaterQualityProfile ForTier(string tier) => tier switch
    {
        "Low" => new(8, 8, 8, 0, 0f),
        "Medium" => new(12, 16, 16, 64, 3f),
        _ => new(20, 24, 24, 128, 2f)
    };
}
