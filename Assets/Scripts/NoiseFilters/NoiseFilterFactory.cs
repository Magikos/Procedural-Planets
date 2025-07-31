public static class NoiseFilterFactory
{
    public static INoiseFilter CreateNoiseFilter(NoiseSettings settings)
    {
        switch (settings.Filter)
        {
            case NoiseSettings.FilterType.Simple:
                return new SimpleNoiseFilter(settings);
            case NoiseSettings.FilterType.Rigid:
                return new RigidNoiseFilter(settings);
            default:
                throw new System.ArgumentException("Unknown filter type: " + settings.Filter);
        }
    }
}