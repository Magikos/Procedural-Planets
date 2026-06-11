public static class PlanetConstants
{
    // Frozen-water sim thresholds (0-1 normalized temperature)
    public const float LakeFreezeStartTemperature01 = 0.36f;
    public const float LakeFreezeCompleteTemperature01 = 0.26f;
    public const float OceanFreezeStartTemperature01 = 0.20f;
    public const float OceanFreezeCompleteTemperature01 = 0.10f;

    // Ice rendering
    public const float IceOpacity = 0.88f;
    public const float IceRoughness = 0.72f;
    public const float IceNormalStrength = 0.35f;
    public const float IceBreakupScale = 95f;

    // Coast blending
    public const float CoastBelowSeaDepth = 8f;
    public const float CoastStartHeight = 2f;
    public const float CoastEndHeight = 20f;
    public const float CoastTiling = 0.08f;

    // Exposed rock on slopes
    public const float SlopeStartDegrees = 28f;
    public const float SlopeFullDegrees = 48f;
    public const float SlopeTiling = 0.075f;

    // Climate snow
    public const float SnowFullTemperature01 = 0.28f;
    public const float SnowFadeEndTemperature01 = 0.42f;
    public const float SnowTiling = 0.09f;
}
