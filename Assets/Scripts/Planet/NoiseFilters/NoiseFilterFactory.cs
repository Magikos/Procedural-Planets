public static class NoiseFilterFactory
{
    public static INoiseFilter CreateNoiseFilter(NoiseSettings settings, int seed = 0)
    {
        return settings.Filter switch
        {
            NoiseSettings.FilterType.Simple => new SimpleNoiseFilter(settings, seed),
            NoiseSettings.FilterType.Rigid => new RigidNoiseFilter(settings, seed),
            _ => throw new System.ArgumentException($"Unknown filter type: {settings.Filter}")
        };
    }
}
