public static partial class ShaderGlobalIds
{
    public const string OceanDebugMode = "_OceanDebugMode";
    public const string DebugSuppressWeatherPasses = "_DebugSuppressWeatherPasses";
    public const string WaterFocusMode = "_WaterFocusMode";
    public const string OceanFocusMode = "_OceanFocusMode";
    public const string WaterVolumeEnabled = "_WaterVolumeEnabled";
    public const string WaterVolumeData = "_WaterVolumeData";
    public const string WaterInterfaceTexture = "_WaterInterfaceTexture";
    public const string FrozenWaterBodies = "_FrozenWaterBodies";
    public const string PartiallyFrozenWaterBodies = "_PartiallyFrozenWaterBodies";
    public const string LiquidWaterBodies = "_LiquidWaterBodies";

    // Vertex displacement and the freeze curve are globals, not material properties: Ocean.shader and
    // WaterVolumePrepass.shader are separate materials that must rasterise the identical surface, and a
    // per-material copy of any of these silently desynchronises them. Consumed by
    // Includes/WaterDisplacement.hlsl; published by PlanetWaterSurface from WaterDto.
    public const string SwellAmplitude = "_SwellAmplitude";
    public const string SwellWavelength = "_SwellWavelength";
    public const string WaveSpeed = "_WaveSpeed";
    public const string FreezingEnabled = "_FreezingEnabled";
    public const string LakeFreezeStart = "_LakeFreezeStart";
    public const string LakeFreezeComplete = "_LakeFreezeComplete";
    public const string OceanFreezeStart = "_OceanFreezeStart";
    public const string OceanFreezeComplete = "_OceanFreezeComplete";

    // Per-direction water surface height, one cube-face slice each. Replaces _SeaLevelRadius for anything
    // asking where the water surface is HERE rather than where the planet's ocean sits. Resolution 0 means
    // no field is published and every consumer falls back to the global radius.
    // Published by WaterLevelTexture; consumed via Includes/WaterLevelField.hlsl.
    public const string WaterLevelTex = "_WaterLevelTex";
    public const string WaterLevelRes = "_WaterLevelRes";
    public const string WaterLevelBaseRadius = "_WaterLevelBaseRadius";
}
