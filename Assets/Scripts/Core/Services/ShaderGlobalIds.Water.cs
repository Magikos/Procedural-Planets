public static partial class ShaderGlobalIds
{
    public const string WaterQuality = "_WaterQuality";
    public const string WaterCameraPosition = "_WaterCameraPosition";
    public const string WaterCameraSurface = "_WaterCameraSurface";
    public const string WaterRippleCount = "_WaterRippleCount";
    public const string WaterRippleOrigins = "_WaterRippleOrigins";
    public const string WaterRippleNormals = "_WaterRippleNormals";
    public const string WaterRippleParams = "_WaterRippleParams";
    public const string WaterReflectionCube = "_WaterReflectionCube";
    public const string WaterReflectionPreviousCube = "_WaterReflectionPreviousCube";
    public const string WaterReflectionBlend = "_WaterReflectionBlend";
    public const string WaterReflectionOrigin = "_WaterReflectionOrigin";
    public const string OceanDebugMode = "_OceanDebugMode";
    public const string DebugSuppressWeatherPasses = "_DebugSuppressWeatherPasses";
    public const string WaterFocusMode = "_WaterFocusMode";
    public const string OceanFocusMode = "_OceanFocusMode";
    public const string WaterVolumeEnabled = "_WaterVolumeEnabled";
    public const string WaterVolumeData = "_WaterVolumeData";
    public const string WaterSurfaceDepth = "_WaterSurfaceDepth";
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

    // The FRAGMENT wave field's size and height. Globals for the same reason as the swell above, but the
    // consumer is different: Atmosphere.shader bands its underwater sun shafts and shapes Snell's window
    // off the SHORT waves these describe, because focusing goes as surface curvature and curvature as
    // 1/wavelength squared - the long swell focuses hundreds of metres down, a metre-scale ripple about
    // fifteen. Consumed by Includes/WaterDisplacement.hlsl (ComputeWaterRipple).
    public const string WaveAmplitude = "_WaveAmplitude";
    public const string WaveScale = "_WaveScale";
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
    // How far the rendered water sheet sits above the solved level. WaterLevelField.hlsl adds it so the
    // shaders answer the same "where is the surface" as the mesh and WaterQueryService.
    // Published by PlanetWaterSurface from WaterMeshBuilder.SurfaceOffsetFor.
    public const string WaterSurfaceOffset = "_WaterSurfaceOffset";

    // The same field carried further onto dry land, for height-above-water questions rather than wetness.
    // Published by PlanetWaterSurface; consumed by Includes/WaterLevelField.hlsl.
    public const string ShoreLevelTex = "_ShoreLevelTex";
    public const string ShoreLevelRes = "_ShoreLevelRes";

    // Where the water volume tint fades in, as a fraction of DeepDepth. The shoreline feather is a depth
    // ramp rather than a distance ramp so it widens by itself on a shallow bank and tightens on a steep one.
    // Consumed by Includes/WaterVolumeData.hlsl; published by PlanetWaterSurface from WaterDto.
    public const string WaterEdgeFadeStart = "_WaterEdgeFadeStart";
    public const string WaterEdgeFadeEnd = "_WaterEdgeFadeEnd";
    public const string WaterEdgeFadeEndOcean = "_WaterEdgeFadeEndOcean";

    // Shared underwater optics, published from WaterDto by PlanetWaterSurface.
    public const string UnderwaterNightScale = "_UnderwaterNightScale";
    public const string UnderwaterShaftIntensity = "_UnderwaterShaftIntensity";
    public const string UnderwaterFogColor = "_UnderwaterFogColor";
    public const string UnderwaterVisibility = "_UnderwaterVisibility";
    public const string UnderwaterShaftWidth = "_UnderwaterShaftWidth";
    public const string UnderwaterSurfaceDetail = "_UnderwaterSurfaceDetail";
}
