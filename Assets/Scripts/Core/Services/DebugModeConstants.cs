// Named constants for the _OceanDebugMode shader global integer.
// HLSL mirror: Assets/Graphics/Shaders/Includes/DebugModes.hlsl
//
// Mode registration/names: WaterDebugModule.cs
// When adding a new mode update this file, DebugModes.hlsl, and WaterDebugModule.
public static class DebugModeConstants
{
    public const int Off = 0;

    // Water Surface (1-12)
    public const int WaterDepth = 1;
    public const int WaterShore = 2;
    public const int WaterBody = 3;
    public const int WaterLighting = 4;
    public const int WaterGlint = 5;
    public const int WaterNormals = 6;
    public const int WaterFoam = 7;
    public const int WaterMotionMask = 8;
    public const int WaterWaveHeight = 9;
    public const int WaterWaveSlope = 10;
    public const int WaterData = 11;
    public const int WaterAbsorption = 12;

    // Water Volume (13-17)
    public const int VolumeData = 13;
    public const int VolumeMask = 14;
    public const int VolumePath = 15;
    public const int VolumeLight = 16;
    public const int VolumeRefraction = 17;

    // Water Surface continued
    public const int FoamParts = 18;
    public const int SurfaceAlpha = 19;

    // Water Volume continued
    public const int VolumeBoundary = 20;
    public const int VolumeOptical = 21;

    // Water Surface continued
    public const int SurfaceContact = 22;
    public const int SurfaceBlend = 23;

    // Water Split
    public const int VolumeOnly = 24;
    public const int SurfaceOnly = 25;
    public const int WaterOff = 26;

    // Water Volume continued
    public const int VolumeContact = 27;
    public const int VolumeDilation = 28;
    public const int VolumeNoRefraction = 29;
    public const int VolumeOcclusion = 30;

    // Water Source
    public const int TerrainSourcePink = 31;
    public const int FoamPink = 32;

    // Water Volume continued
    public const int VolumeSphere = 33;

    // Terrain
    public const int TerrainFaceId = 34;

    // Water Volume: Sea ray
    public const int SeaRay = 35;
    public const int SeaVsMesh = 36;
    public const int SeaPath = 37;
    public const int SeaMatte = 38;
    public const int SeaSourceMatte = 39;

    // Atmosphere
    public const int AtmosphereBypass = 40;
    public const int VolumeAfterAtmosphere = 41;
    public const int AtmosphereWaterCut = 42;

    // Contribution heatmaps
    public const int VolumeContribution = 43;
    public const int AtmosphereContribution = 44;
    public const int PrecipitationContribution = 45;

    // Water Volume: Lip
    public const int VolumeLipPink = 46;
    public const int VolumeLipRawPink = 47;
    public const int VolumeLipDepthGate = 48;

    // Water Surface: Backface / polish / isolation
    public const int SurfaceBackfacePink = 49;
    public const int VolumeLipScenePink = 50;
    public const int WakeMask = 51;
    public const int SurfacePolish = 52;
    public const int SurfaceRawOpaque = 53;
    public const int SurfaceFxContrib = 54;
    public const int SurfaceAlphaParts = 55;
    public const int WaterNoPost = 56;
    public const int SurfaceFxProof = 57;
    public const int CausticsOnly = 58;
    public const int CausticsMask = 59;
    public const int CausticsLight = 60;
    public const int BottomDistortionOnly = 61;
    public const int BottomDistortionVector = 62;
    public const int CausticsPrism = 63;

    // Water Surface: night-lighting discovery
    public const int SurfaceNightTerms = 64;
    public const int SurfaceLumaHeat = 65;

    // Water Surface: wave / swell discovery
    public const int WaveSwell = 66;
    public const int WaveEnergy = 67;
    public const int WavePhase = 68;
    public const int WaveGrid = 69;

    // Water Surface: foam discovery
    public const int FoamOnSwell = 70;
    public const int FoamLocator = 71;

    // Water Surface: glint discovery
    public const int GlintLocator = 72;

    // Biome diagnostics — bypass clouds + atmosphere so the unlit biome visualization is
    // never obscured. See PlanetVertexColor.shader for the per-mode display logic.
    public const int BiomePrimaryId = 73;
    public const int BiomeTemperature = 74;
    public const int BiomeMoisture = 75;
    public const int BiomeLatitude = 76;
    public const int BiomeElevationBand = 77;

    /// <summary>Highest defined mode value. Update when adding new modes.</summary>
    public const int Max = 77;

    public static bool SuppressesWeatherPasses(int mode)
    {
        return (mode >= WaterDepth && mode <= WaterAbsorption)
            || mode == FoamParts
            || mode == SurfaceAlpha
            || mode == SurfaceContact
            || mode == SurfaceBlend
            || mode == SurfaceOnly
            || mode == FoamPink
            || mode == SurfaceBackfacePink
            || (mode >= WakeMask && mode <= SurfaceFxProof)
            || (mode >= CausticsOnly && mode <= CausticsPrism)
            || mode == SurfaceNightTerms
            || mode == SurfaceLumaHeat
            || mode == WaveSwell
            || mode == WaveEnergy
            || mode == WavePhase
            || mode == WaveGrid
            || mode == FoamOnSwell
            || mode == FoamLocator
            || mode == GlintLocator
            || (mode >= BiomePrimaryId && mode <= BiomeElevationBand);
    }
}
