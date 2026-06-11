public static class CloudDebugState
{
    public enum View
    {
        Off = 0,
        Weather = 1,
        Storm = 2,
        Density = 3,
        OpticalDepth = 4,
        SilverLining = 5,
        MoistureSource = 6,
        CondensationChange = 7
    }

    public static View Mode = View.Off;
    public static float CondensationChangeThreshold = 0.0002f;
    public static float CondensationChangeSaturation = 0.004f;
}
