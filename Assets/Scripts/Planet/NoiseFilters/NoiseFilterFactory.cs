public static class NoiseFilterFactory
{
    public static INoiseFilter CreateNoiseFilter(NoiseSettings settings, int seed = 0)
    {
        switch (settings.Filter)
        {
            case NoiseSettings.FilterType.Simple:
                return new SimpleNoiseFilter(settings, seed);
            case NoiseSettings.FilterType.Rigid:
                return new RigidNoiseFilter(settings, seed);
            default:
                throw new System.ArgumentException("Unknown filter type: " + settings.Filter);
        }
    }
}
