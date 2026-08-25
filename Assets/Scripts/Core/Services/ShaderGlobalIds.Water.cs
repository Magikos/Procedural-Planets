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

    // The colour deep water settles to, as PlanetWaterSurface computes it from PlanetDto.WaterColor and
    // WaterDto.DeepBaseColor. The water shaders get it as the material's own _DeepColor; this global exists
    // so Atmosphere.shader can reach the same value, because it renders the view THROUGH the water from
    // below and had its own hardcoded copy that had drifted to roughly half the authored brightness.
    //
    // Deliberately NOT named _DeepColor: a material property of the same name shadows the global wherever
    // that material is bound, which is exactly how the wave parameters went wrong before.
    public const string WaterDeepColor = "_WaterDeepColor";

    // The two magnitudes in Atmosphere.shader's submerged composite. Published by PlanetWaterSurface from
    // WaterDto; nothing else reads them.
    //
    // UnderwaterNightScale multiplies the ambient floor the water column falls to after dark. It exists so
    // that floor can move without _NightAmbientIntensity, which lights every surface on the planet at once:
    // how readable the water is at midnight is a water art call, not a world-wide one.
    //
    // UnderwaterShaftIntensity scales the sun-shaft scattering coefficient. Only the magnitude - the
    // per-channel ratio stays an authored constant in the shader, so the shafts keep their colour and the
    // knob moves the one axis a person actually wants.
    public const string UnderwaterNightScale = "_UnderwaterNightScale";
    public const string UnderwaterShaftIntensity = "_UnderwaterShaftIntensity";
}
