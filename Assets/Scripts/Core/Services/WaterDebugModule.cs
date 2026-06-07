using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class WaterDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("water");
    public static readonly DebugCaptureSetId Artifact = new DebugCaptureSetId(Module, "artifact");
    public static readonly DebugCaptureSetId Atmosphere = new DebugCaptureSetId(Module, "atmosphere");
    public static readonly DebugCaptureSetId Interface = new DebugCaptureSetId(Module, "interface");
    public static readonly DebugCaptureSetId Precipitation = new DebugCaptureSetId(Module, "precipitation");
    public static readonly DebugCaptureSetId SurfaceFinish = new DebugCaptureSetId(Module, "surface-finish");
    public static readonly DebugCaptureSetId SurfaceIsolation = new DebugCaptureSetId(Module, "surface-isolation");
    public static readonly DebugCaptureSetId Caustics = new DebugCaptureSetId(Module, "caustics");
    public static readonly DebugCaptureSetId Wakes = new DebugCaptureSetId(Module, "wakes");
    public static readonly DebugCaptureSetId VolumeDeepDive = new DebugCaptureSetId(Module, "volume-deep-dive");
    public static readonly DebugCaptureSetId Night = new DebugCaptureSetId(Module, "night");
    public static readonly DebugCaptureSetId Waves = new DebugCaptureSetId(Module, "waves");
    public static readonly DebugCaptureSetId Foam = new DebugCaptureSetId(Module, "foam");
    public static readonly DebugCaptureSetId Glint = new DebugCaptureSetId(Module, "glint");
    public static readonly DebugCaptureSetId Biome = new DebugCaptureSetId(Module, "biome");
    public static readonly DebugCaptureSetId Frozen = new DebugCaptureSetId(Module, "frozen");
    public static readonly DebugCaptureSetId TerrainGeography = new DebugCaptureSetId(Module, "terrain-geography");

    public static DebugModeId Mode(int localId)
    {
        return new DebugModeId(Module, localId);
    }
}

public sealed class WaterDebugModule : IDebugModule, IDebugModeApplier, IDebugCaptureMetadataProvider, IDebugOverlayContributor
{
    public DebugModuleId Id => WaterDebugIds.Module;
    public DebugModuleId ModuleId => WaterDebugIds.Module;

    Renderer _cachedWaterRenderer;
    Mesh _cachedWaterMesh;
    WaterDebugStats _waterDebugStats;
    float _nextWaterDebugRefreshTime;
    // Mesh-static histogram (depth/shore/body min/max/avg + mask aggregates) is recomputed only
    // when the underlying mesh changes; per-refresh work shrinks to the camera-sample search.
    WaterMeshStatsCache _meshStatsCache;

    static readonly int _oceanDebugModeId = Shader.PropertyToID("_OceanDebugMode");
    static readonly int _waterFocusModeId = Shader.PropertyToID("_WaterFocusMode");
    static readonly int _oceanFocusModeId = Shader.PropertyToID("_OceanFocusMode");
    static readonly int _waveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
    static readonly int _waveScaleId = Shader.PropertyToID("_WaveScale");
    static readonly int _waveSpeedId = Shader.PropertyToID("_WaveSpeed");
    static readonly int _waveNormalStrengthId = Shader.PropertyToID("_WaveNormalStrength");
    static readonly int _waterMotionStrengthId = Shader.PropertyToID("_WaterMotionStrength");
    static readonly int _sunGlitterIntensityId = Shader.PropertyToID("_SunGlitterIntensity");
    static readonly int _shoreFoamIntensityId = Shader.PropertyToID("_ShoreFoamIntensity");
    static readonly int _whitecapIntensityId = Shader.PropertyToID("_WhitecapIntensity");
    static readonly int _wakeFoamIntensityId = Shader.PropertyToID("_WakeFoamIntensity");
    static readonly int _wakeNormalStrengthId = Shader.PropertyToID("_WakeNormalStrength");
    static readonly int _waterWakeCountId = Shader.PropertyToID("_WaterWakeCount");
    static readonly int _shallowDepthId = Shader.PropertyToID("_ShallowDepth");
    static readonly int _deepDepthId = Shader.PropertyToID("_DeepDepth");
    static readonly int _shoreFoamDepthId = Shader.PropertyToID("_ShoreFoamDepth");
    static readonly int _shoreFoamSoftnessId = Shader.PropertyToID("_ShoreFoamSoftness");
    static readonly int _biomeAssignmentModeId = Shader.PropertyToID("_BiomeAssignmentMode");
    static readonly int _biomeVoronoiSeedCountId = Shader.PropertyToID("_BiomeVoronoiSeedCount");
    static readonly int _biomeVoronoiCleanupChangesId = Shader.PropertyToID("_BiomeVoronoiCleanupChanges");
    static readonly int _biomeVoronoiDistinctBiomesId = Shader.PropertyToID("_BiomeVoronoiDistinctBiomes");
    static readonly int _biomeVoronoiBuildMsId = Shader.PropertyToID("_BiomeVoronoiBuildMs");
    static readonly int _biomeVoronoiAtlasResolutionId = Shader.PropertyToID("_BiomeVoronoiAtlasResolution");
    static readonly int _freezingEnabledId = Shader.PropertyToID("_FreezingEnabled");
    static readonly int _lakeFreezeStartId = Shader.PropertyToID("_LakeFreezeStart");
    static readonly int _lakeFreezeCompleteId = Shader.PropertyToID("_LakeFreezeComplete");
    static readonly int _oceanFreezeStartId = Shader.PropertyToID("_OceanFreezeStart");
    static readonly int _oceanFreezeCompleteId = Shader.PropertyToID("_OceanFreezeComplete");
    static readonly int _frozenWaterBodiesId = Shader.PropertyToID("_FrozenWaterBodies");
    static readonly int _partiallyFrozenWaterBodiesId = Shader.PropertyToID("_PartiallyFrozenWaterBodies");
    static readonly int _liquidWaterBodiesId = Shader.PropertyToID("_LiquidWaterBodies");
    static readonly int _terrainOverrideEnabledId = Shader.PropertyToID("_TerrainOverrideEnabled");
    static readonly int _coastSliceId = Shader.PropertyToID("_CoastSlice");
    static readonly int _coastBelowSeaDepthId = Shader.PropertyToID("_CoastBelowSeaDepth");
    static readonly int _coastStartHeightId = Shader.PropertyToID("_CoastStartHeight");
    static readonly int _coastEndHeightId = Shader.PropertyToID("_CoastEndHeight");
    static readonly int _coastTilingId = Shader.PropertyToID("_CoastTiling");
    static readonly int _slopeSliceId = Shader.PropertyToID("_SlopeSlice");
    static readonly int _slopeStartDegreesId = Shader.PropertyToID("_SlopeStartDegrees");
    static readonly int _slopeFullDegreesId = Shader.PropertyToID("_SlopeFullDegrees");
    static readonly int _slopeTilingId = Shader.PropertyToID("_SlopeTiling");
    static readonly int _snowSliceId = Shader.PropertyToID("_SnowSlice");
    static readonly int _snowFullTemperatureId = Shader.PropertyToID("_SnowFullTemperature");
    static readonly int _snowFadeEndTemperatureId = Shader.PropertyToID("_SnowFadeEndTemperature");
    static readonly int _snowTilingId = Shader.PropertyToID("_SnowTiling");

    struct WaterDebugStats
    {
        public bool Valid;
        public int Vertices;
        public int Triangles;
        public float DepthMin, DepthMax, DepthAvg;
        public float ShoreMin, ShoreMax, ShoreAvg;
        public float BodyMin, BodyMax, BodyAvg;
        public float TemperatureMin, TemperatureMax, TemperatureAvg;
        public float MotionMaskAvg, MotionMaskMax, MotionMaskSample;
        public float NormalMaskAvg, NormalMaskMax, NormalMaskSample;
        public float SampleDepth, SampleShore, SampleBody;
        public float SampleTemperature;
        public float MotionEligiblePercent;
        public float NormalEligiblePercent;
    }

    struct MeshIntegrityStats
    {
        public int DegenerateTriangles;
        public int BoundaryEdges;
        public int NonManifoldEdges;
        public int OpenEdgeVertices;
        public float RadiusErrorAvgMeters;
        public float RadiusErrorMaxMeters;
    }

    public void Register(DebugRegistry registry)
    {
        RegisterModes(registry);
        RegisterCaptureSets(registry);
        registry.RegisterModeApplier(this);
        registry.RegisterMetadataProvider(this);
        registry.RegisterOverlayContributor(this);
    }

    public void ApplyDebugMode(DebugModeDefinition mode)
    {
        Shader.SetGlobalInt(_oceanDebugModeId, mode.Id.LocalId);
    }

    public void ClearDebugMode()
    {
        Shader.SetGlobalInt(_oceanDebugModeId, DebugModeConstants.Off);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        AppendWaterDebugMetadata(sb, context.Runtime);
        AppendBiomeAssignmentMetadata(sb);
        AppendTerrainGeographyMetadata(sb);
    }

    static void AppendBiomeAssignmentMetadata(StringBuilder sb)
    {
        int mode = Shader.GetGlobalInt(_biomeAssignmentModeId);
        int atlasResolution = Shader.GetGlobalInt(_biomeVoronoiAtlasResolutionId);
        sb.AppendLine();
        sb.AppendLine("--- Biome Assignment ---");
        sb.AppendLine($"Mode: {(mode == 1 ? "Voronoi" : "DirectClimateGrid")}");
        if (mode != 1)
            return;

        sb.AppendLine(
            $"Voronoi: seeds={Shader.GetGlobalInt(_biomeVoronoiSeedCountId)}, " +
            $"distinct={Shader.GetGlobalInt(_biomeVoronoiDistinctBiomesId)}, " +
            $"cleanupChanges={Shader.GetGlobalInt(_biomeVoronoiCleanupChangesId)}, " +
            $"atlas={atlasResolution}x{atlasResolution}x6, " +
            $"buildMs={Shader.GetGlobalInt(_biomeVoronoiBuildMsId)}");
    }

    static void AppendTerrainGeographyMetadata(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("--- Terrain Geography ---");
        sb.AppendLine($"Enabled: {Shader.GetGlobalFloat(_terrainOverrideEnabledId) > 0.5f}");
        sb.AppendLine(
            $"Coast: slice={Shader.GetGlobalInt(_coastSliceId)}, " +
            $"belowSea={Shader.GetGlobalFloat(_coastBelowSeaDepthId):F2}m, " +
            $"height={Shader.GetGlobalFloat(_coastStartHeightId):F2}-{Shader.GetGlobalFloat(_coastEndHeightId):F2}m, " +
            $"tiling={Shader.GetGlobalFloat(_coastTilingId):F3}");
        sb.AppendLine(
            $"Slope: slice={Shader.GetGlobalInt(_slopeSliceId)}, " +
            $"degrees={Shader.GetGlobalFloat(_slopeStartDegreesId):F1}-{Shader.GetGlobalFloat(_slopeFullDegreesId):F1}, " +
            $"tiling={Shader.GetGlobalFloat(_slopeTilingId):F3}");
        sb.AppendLine(
            $"Snow: slice={Shader.GetGlobalInt(_snowSliceId)}, " +
            $"temperature={Shader.GetGlobalFloat(_snowFullTemperatureId):F3}-{Shader.GetGlobalFloat(_snowFadeEndTemperatureId):F3}, " +
            $"tiling={Shader.GetGlobalFloat(_snowTilingId):F3}");
    }

    public void DrawOverlay(DebugRuntimeState state)
    {
        if (!state.ShowDetailedDebug)
            return;

        DrawWaterDebugOverlay(state);
    }

    static void RegisterModes(DebugRegistry registry)
    {
        RegisterMode(registry, DebugModeConstants.Off, "Off", "Water");
        RegisterMode(registry, DebugModeConstants.WaterDepth, "Depth", "Water Surface");
        RegisterMode(registry, DebugModeConstants.WaterShore, "Shore", "Water Surface");
        RegisterMode(registry, DebugModeConstants.WaterBody, "Body", "Water Surface");
        RegisterMode(registry, DebugModeConstants.WaterLighting, "Lighting", "Water Surface");
        RegisterMode(registry, DebugModeConstants.WaterGlint, "Glint", "Water Surface");
        RegisterMode(registry, DebugModeConstants.WaterNormals, "Normals", "Water Surface");
        RegisterMode(registry, DebugModeConstants.WaterFoam, "Foam", "Water Surface");
        RegisterMode(registry, DebugModeConstants.WaterMotionMask, "MotionMask", "Water Surface");
        RegisterMode(registry, DebugModeConstants.WaterWaveHeight, "WaveHeight", "Water Surface");
        RegisterMode(registry, DebugModeConstants.WaterWaveSlope, "WaveSlope", "Water Surface");
        RegisterMode(registry, DebugModeConstants.WaterData, "WaterData", "Water Surface");
        RegisterMode(registry, DebugModeConstants.WaterAbsorption, "Absorption", "Water Surface");
        RegisterMode(registry, DebugModeConstants.VolumeData, "VolumeData", "Water Volume");
        RegisterMode(registry, DebugModeConstants.VolumeMask, "VolumeMask", "Water Volume");
        RegisterMode(registry, DebugModeConstants.VolumePath, "VolumePath", "Water Volume");
        RegisterMode(registry, DebugModeConstants.VolumeLight, "VolumeLight", "Water Volume");
        RegisterMode(registry, DebugModeConstants.VolumeRefraction, "VolumeRefraction", "Water Volume");
        RegisterMode(registry, DebugModeConstants.FoamParts, "FoamParts", "Water Surface");
        RegisterMode(registry, DebugModeConstants.SurfaceAlpha, "SurfaceAlpha", "Water Surface");
        RegisterMode(registry, DebugModeConstants.VolumeBoundary, "VolumeBoundary", "Water Volume");
        RegisterMode(registry, DebugModeConstants.VolumeOptical, "VolumeOptical", "Water Volume");
        RegisterMode(registry, DebugModeConstants.SurfaceContact, "SurfaceContact", "Water Surface");
        RegisterMode(registry, DebugModeConstants.SurfaceBlend, "SurfaceBlend", "Water Surface");
        RegisterMode(registry, DebugModeConstants.VolumeOnly, "VolumeOnly", "Water Split");
        RegisterMode(registry, DebugModeConstants.SurfaceOnly, "SurfaceOnly", "Water Split");
        RegisterMode(registry, DebugModeConstants.WaterOff, "WaterOff", "Water Split");
        RegisterMode(registry, DebugModeConstants.VolumeContact, "VolumeContact", "Water Volume");
        RegisterMode(registry, DebugModeConstants.VolumeDilation, "VolumeDilation", "Water Volume");
        RegisterMode(registry, DebugModeConstants.VolumeNoRefraction, "VolumeNoRefraction", "Water Volume");
        RegisterMode(registry, DebugModeConstants.VolumeOcclusion, "VolumeOcclusion", "Water Volume");
        RegisterMode(registry, DebugModeConstants.TerrainSourcePink, "TerrainSourcePink", "Water Source");
        RegisterMode(registry, DebugModeConstants.FoamPink, "FoamPink", "Water Source");
        RegisterMode(registry, DebugModeConstants.VolumeSphere, "VolumeSphere", "Water Volume");
        RegisterMode(registry, DebugModeConstants.TerrainFaceId, "TerrainFaceId", "Terrain");
        RegisterMode(registry, DebugModeConstants.SeaRay, "SeaRay", "Water Volume");
        RegisterMode(registry, DebugModeConstants.SeaVsMesh, "SeaVsMesh", "Water Volume");
        RegisterMode(registry, DebugModeConstants.SeaPath, "SeaPath", "Water Volume");
        RegisterMode(registry, DebugModeConstants.SeaMatte, "SeaMatte", "Water Volume");
        RegisterMode(registry, DebugModeConstants.SeaSourceMatte, "SeaSourceMatte", "Water Volume");
        RegisterMode(registry, DebugModeConstants.AtmosphereBypass, "AtmosphereBypass", "Atmosphere");
        RegisterMode(registry, DebugModeConstants.VolumeAfterAtmosphere, "VolumeAfterAtmosphere", "Water Atmosphere");
        RegisterMode(registry, DebugModeConstants.AtmosphereWaterCut, "AtmosphereWaterCut", "Atmosphere");
        RegisterMode(registry, DebugModeConstants.VolumeContribution, "VolumeContribution", "Water Volume");
        RegisterMode(registry, DebugModeConstants.AtmosphereContribution, "AtmosphereContribution", "Atmosphere");
        RegisterMode(registry, DebugModeConstants.PrecipitationContribution, "PrecipitationContribution", "Precipitation");
        RegisterMode(registry, DebugModeConstants.VolumeLipPink, "VolumeLipPink", "Water Volume");
        RegisterMode(registry, DebugModeConstants.VolumeLipRawPink, "VolumeLipRawPink", "Water Volume");
        RegisterMode(registry, DebugModeConstants.VolumeLipDepthGate, "VolumeLipDepthGate", "Water Volume");
        RegisterMode(registry, DebugModeConstants.SurfaceBackfacePink, "SurfaceBackfacePink", "Water Surface");
        RegisterMode(registry, DebugModeConstants.VolumeLipScenePink, "VolumeLipScenePink", "Water Volume");
        RegisterMode(registry, DebugModeConstants.WakeMask, "WakeMask", "Water Surface");
        RegisterMode(registry, DebugModeConstants.SurfacePolish, "SurfacePolish", "Water Surface");
        RegisterMode(registry, DebugModeConstants.SurfaceRawOpaque, "SurfaceRawOpaque", "Water Surface Isolation");
        RegisterMode(registry, DebugModeConstants.SurfaceFxContrib, "SurfaceFxContrib", "Water Surface Isolation");
        RegisterMode(registry, DebugModeConstants.SurfaceAlphaParts, "SurfaceAlphaParts", "Water Surface Isolation");
        RegisterMode(registry, DebugModeConstants.WaterNoPost, "WaterNoPost", "Water Split");
        RegisterMode(registry, DebugModeConstants.SurfaceFxProof, "SurfaceFxProof", "Water Surface Isolation");
        RegisterMode(registry, DebugModeConstants.CausticsOnly, "CausticsOnly", "Water Caustics");
        RegisterMode(registry, DebugModeConstants.CausticsMask, "CausticsMask", "Water Caustics");
        RegisterMode(registry, DebugModeConstants.CausticsLight, "CausticsLight", "Water Caustics");
        RegisterMode(registry, DebugModeConstants.BottomDistortionOnly, "BottomDistortionOnly", "Water Foundation");
        RegisterMode(registry, DebugModeConstants.BottomDistortionVector, "BottomDistortionVector", "Water Foundation");
        RegisterMode(registry, DebugModeConstants.CausticsPrism, "CausticsPrism", "Water Caustics");
        RegisterMode(registry, DebugModeConstants.SurfaceNightTerms, "NightTerms", "Water Night");
        RegisterMode(registry, DebugModeConstants.SurfaceLumaHeat, "LumaHeat", "Water Night");
        RegisterMode(registry, DebugModeConstants.WaveSwell, "WaveSwell", "Water Waves");
        RegisterMode(registry, DebugModeConstants.WaveEnergy, "WaveEnergy", "Water Waves");
        RegisterMode(registry, DebugModeConstants.WavePhase, "WavePhase", "Water Waves");
        RegisterMode(registry, DebugModeConstants.WaveGrid, "WaveGrid", "Water Waves");
        RegisterMode(registry, DebugModeConstants.FoamOnSwell, "FoamOnSwell", "Water Foam");
        RegisterMode(registry, DebugModeConstants.FoamLocator, "FoamLocator", "Water Foam");
        RegisterMode(registry, DebugModeConstants.GlintLocator, "GlintLocator", "Water Glint");
        RegisterMode(registry, DebugModeConstants.BiomePrimaryId, "BiomePrimaryId", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeTemperature, "BiomeTemperature", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeMoisture, "BiomeMoisture", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeLatitude, "BiomeLatitude", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeElevationBand, "BiomeElevationBand", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeAltitudeCooling, "BiomeAltitudeCooling", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeMapPrimaryId, "BiomeMapPrimaryId", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeMapBlend, "BiomeMapBlend", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeMapFlatColor, "BiomeMapFlatColor", "Biome");
        RegisterMode(registry, DebugModeConstants.TerrainSelectedAlbedo, "TerrainSelectedAlbedo", "Biome");
        RegisterMode(registry, DebugModeConstants.TerrainSunLighting, "TerrainSunLighting", "Biome");
        RegisterMode(registry, DebugModeConstants.TerrainSurfaceNormal, "TerrainSurfaceNormal", "Biome");
        RegisterMode(registry, DebugModeConstants.TerrainSurfaceAo, "TerrainSurfaceAO", "Biome");
        RegisterMode(registry, DebugModeConstants.TerrainSurfaceRoughness, "TerrainSurfaceRoughness", "Biome");
        RegisterMode(registry, DebugModeConstants.GrassLodCoverage, "GrassLodCoverage", "Biome");
        RegisterMode(registry, DebugModeConstants.WaterTemperature, "WaterTemperature", "Frozen Water");
        RegisterMode(registry, DebugModeConstants.WaterFreeze, "WaterFreeze", "Frozen Water");
        RegisterMode(registry, DebugModeConstants.WaterIceContribution, "WaterIceContribution", "Frozen Water");
        RegisterMode(registry, DebugModeConstants.TerrainCoastMask, "TerrainCoastMask", "Terrain Geography");
        RegisterMode(registry, DebugModeConstants.TerrainSlopeMask, "TerrainSlopeMask", "Terrain Geography");
        RegisterMode(registry, DebugModeConstants.TerrainSnowMask, "TerrainSnowMask", "Terrain Geography");
        RegisterMode(registry, DebugModeConstants.TerrainOverrideComposite, "TerrainOverrideComposite", "Terrain Geography");
    }

    static void RegisterCaptureSets(DebugRegistry registry)
    {
        registry.RegisterCaptureSet(WaterDebugIds.Artifact, "Water Artifact",
            Modes(DebugModeConstants.Off, DebugModeConstants.VolumeOnly, DebugModeConstants.SurfaceOnly,
                DebugModeConstants.WaterOff, DebugModeConstants.VolumeOcclusion, DebugModeConstants.VolumeLipPink,
                DebugModeConstants.VolumeLipRawPink, DebugModeConstants.VolumeLipDepthGate,
                DebugModeConstants.SurfaceBackfacePink, DebugModeConstants.VolumeLipScenePink,
                DebugModeConstants.TerrainSourcePink, DebugModeConstants.FoamPink,
                DebugModeConstants.AtmosphereBypass, DebugModeConstants.VolumeAfterAtmosphere,
                DebugModeConstants.AtmosphereWaterCut, DebugModeConstants.VolumeContribution,
                DebugModeConstants.AtmosphereContribution, DebugModeConstants.PrecipitationContribution));
        registry.RegisterCaptureSet(WaterDebugIds.Atmosphere, "Water/Atmosphere",
            Modes(DebugModeConstants.Off, DebugModeConstants.VolumeOnly, DebugModeConstants.WaterOff,
                DebugModeConstants.AtmosphereBypass, DebugModeConstants.VolumeAfterAtmosphere,
                DebugModeConstants.AtmosphereWaterCut, DebugModeConstants.AtmosphereContribution));
        registry.RegisterCaptureSet(WaterDebugIds.Interface, "Water Interface",
            Modes(DebugModeConstants.Off, DebugModeConstants.WaterData, DebugModeConstants.VolumeMask,
                DebugModeConstants.VolumePath, DebugModeConstants.VolumeBoundary,
                DebugModeConstants.VolumeContact, DebugModeConstants.VolumeDilation,
                DebugModeConstants.VolumeSphere, DebugModeConstants.TerrainFaceId,
                DebugModeConstants.SeaRay, DebugModeConstants.SeaVsMesh, DebugModeConstants.SeaPath,
                DebugModeConstants.VolumeLipPink, DebugModeConstants.VolumeLipRawPink,
                DebugModeConstants.VolumeLipDepthGate, DebugModeConstants.SurfaceBackfacePink,
                DebugModeConstants.VolumeLipScenePink));
        registry.RegisterCaptureSet(WaterDebugIds.Precipitation, "Water Precipitation",
            Modes(DebugModeConstants.Off, DebugModeConstants.AtmosphereBypass,
                DebugModeConstants.AtmosphereWaterCut, DebugModeConstants.AtmosphereContribution,
                DebugModeConstants.PrecipitationContribution));
        registry.RegisterDefaultCaptureSet(WaterDebugIds.Glint, "Water Glint",
            Modes(DebugModeConstants.Off, DebugModeConstants.SurfaceOnly,
                DebugModeConstants.GlintLocator, DebugModeConstants.WaterGlint,
                DebugModeConstants.WaterLighting, DebugModeConstants.WaterWaveSlope,
                DebugModeConstants.WaterNormals));
        // Biome set becomes the default for the current biome diagnostic phase. Atmosphere and
        // clouds are bypassed for these modes (DebugModeConstants.SuppressesWeatherPasses +
        // Atmosphere.shader ShouldBypassAtmosphereForWaterDebug), so unlike orbit captures of
        // Off/AtmosphereBypass the planet surface is never obscured by weather.
        registry.RegisterDefaultCaptureSet(WaterDebugIds.Biome, "Biome",
            Modes(DebugModeConstants.Off, DebugModeConstants.AtmosphereBypass,
                DebugModeConstants.WaterOff,
                DebugModeConstants.BiomePrimaryId, DebugModeConstants.BiomeTemperature,
                DebugModeConstants.BiomeMoisture, DebugModeConstants.BiomeLatitude,
                DebugModeConstants.BiomeElevationBand, DebugModeConstants.BiomeAltitudeCooling,
                DebugModeConstants.BiomeMapPrimaryId,
                DebugModeConstants.BiomeMapBlend, DebugModeConstants.BiomeMapFlatColor,
                DebugModeConstants.TerrainSelectedAlbedo, DebugModeConstants.TerrainSunLighting,
                DebugModeConstants.TerrainSurfaceNormal, DebugModeConstants.TerrainSurfaceAo,
                DebugModeConstants.TerrainSurfaceRoughness, DebugModeConstants.GrassLodCoverage,
                DebugModeConstants.TerrainFaceId));
        registry.RegisterCaptureSet(WaterDebugIds.Frozen, "Frozen Water",
            Modes(DebugModeConstants.Off, DebugModeConstants.SurfaceOnly,
                DebugModeConstants.WaterBody, DebugModeConstants.WaterTemperature,
                DebugModeConstants.WaterFreeze, DebugModeConstants.WaterIceContribution,
                DebugModeConstants.WaterMotionMask, DebugModeConstants.WaterNormals,
                DebugModeConstants.WaterFoam));
        registry.RegisterCaptureSet(WaterDebugIds.TerrainGeography, "Terrain Geography",
            Modes(DebugModeConstants.Off, DebugModeConstants.WaterOff,
                DebugModeConstants.BiomeTemperature, DebugModeConstants.BiomeElevationBand,
                DebugModeConstants.TerrainCoastMask, DebugModeConstants.TerrainSlopeMask,
                DebugModeConstants.TerrainSnowMask, DebugModeConstants.TerrainOverrideComposite,
                DebugModeConstants.TerrainSelectedAlbedo, DebugModeConstants.TerrainSurfaceNormal,
                DebugModeConstants.TerrainSurfaceRoughness));
        registry.RegisterCaptureSet(WaterDebugIds.Caustics, "Water Caustics",
            Modes(DebugModeConstants.Off, DebugModeConstants.VolumeOnly,
                DebugModeConstants.CausticsOnly, DebugModeConstants.CausticsPrism,
                DebugModeConstants.CausticsMask, DebugModeConstants.CausticsLight,
                DebugModeConstants.BottomDistortionOnly));
        registry.RegisterCaptureSet(WaterDebugIds.Foam, "Water Foam",
            Modes(DebugModeConstants.Off, DebugModeConstants.SurfaceOnly,
                DebugModeConstants.FoamLocator, DebugModeConstants.WaterFoam,
                DebugModeConstants.FoamParts, DebugModeConstants.FoamOnSwell,
                DebugModeConstants.WaveSwell, DebugModeConstants.WaterWaveSlope));
        registry.RegisterCaptureSet(WaterDebugIds.Waves, "Water Waves",
            Modes(DebugModeConstants.Off, DebugModeConstants.SurfaceOnly,
                DebugModeConstants.WaveGrid, DebugModeConstants.WaveSwell,
                DebugModeConstants.WavePhase, DebugModeConstants.WaveEnergy,
                DebugModeConstants.WaterWaveHeight, DebugModeConstants.WaterWaveSlope,
                DebugModeConstants.WaterNormals));
        registry.RegisterCaptureSet(WaterDebugIds.SurfaceFinish, "Water Surface Finish",
            Modes(DebugModeConstants.Off, DebugModeConstants.AtmosphereBypass, DebugModeConstants.WaterNoPost,
                DebugModeConstants.SurfaceOnly, DebugModeConstants.WaterDepth, DebugModeConstants.WaterData, DebugModeConstants.WaterLighting,
                DebugModeConstants.WaterGlint, DebugModeConstants.WaterNormals, DebugModeConstants.WaterFoam,
                DebugModeConstants.WaterMotionMask, DebugModeConstants.WaterWaveHeight, DebugModeConstants.WaterWaveSlope,
                DebugModeConstants.FoamParts, DebugModeConstants.SurfaceAlpha,
                DebugModeConstants.SurfaceContact, DebugModeConstants.SurfaceBlend,
                DebugModeConstants.WakeMask, DebugModeConstants.SurfacePolish,
                DebugModeConstants.SurfaceRawOpaque, DebugModeConstants.SurfaceFxContrib,
                DebugModeConstants.SurfaceAlphaParts, DebugModeConstants.SurfaceFxProof));
        registry.RegisterCaptureSet(WaterDebugIds.SurfaceIsolation, "Water Surface Isolation",
            Modes(DebugModeConstants.Off, DebugModeConstants.AtmosphereBypass, DebugModeConstants.WaterNoPost,
                DebugModeConstants.VolumeOnly, DebugModeConstants.SurfaceOnly,
                DebugModeConstants.SurfaceRawOpaque, DebugModeConstants.SurfaceFxContrib,
                DebugModeConstants.SurfaceAlphaParts, DebugModeConstants.SurfaceAlpha,
                DebugModeConstants.SurfaceBlend, DebugModeConstants.SurfacePolish,
                DebugModeConstants.SurfaceFxProof));
        registry.RegisterCaptureSet(WaterDebugIds.Night, "Water Night",
            Modes(DebugModeConstants.Off, DebugModeConstants.SurfaceOnly, DebugModeConstants.VolumeOnly,
                DebugModeConstants.WaterOff, DebugModeConstants.WaterLighting,
                DebugModeConstants.SurfaceLumaHeat, DebugModeConstants.SurfaceNightTerms));
        registry.RegisterCaptureSet(WaterDebugIds.Wakes, "Water Wakes",
            Modes(DebugModeConstants.Off, DebugModeConstants.WakeMask, DebugModeConstants.WaterFoam,
                DebugModeConstants.FoamParts, DebugModeConstants.WaterNormals,
                DebugModeConstants.WaterWaveHeight, DebugModeConstants.WaterWaveSlope,
                DebugModeConstants.SurfacePolish, DebugModeConstants.SurfaceFxProof));
        registry.RegisterCaptureSet(WaterDebugIds.VolumeDeepDive, "Water Volume Deep Dive",
            Modes(DebugModeConstants.Off, DebugModeConstants.WaterShore, DebugModeConstants.WaterFoam,
                DebugModeConstants.WaterData, DebugModeConstants.WaterAbsorption,
                DebugModeConstants.VolumeMask, DebugModeConstants.VolumePath,
                DebugModeConstants.FoamParts, DebugModeConstants.SurfaceAlpha,
                DebugModeConstants.VolumeBoundary, DebugModeConstants.VolumeOptical,
                DebugModeConstants.SurfaceContact, DebugModeConstants.SurfaceBlend,
                DebugModeConstants.VolumeOnly, DebugModeConstants.SurfaceOnly, DebugModeConstants.WaterOff,
                DebugModeConstants.VolumeContact, DebugModeConstants.VolumeDilation,
                DebugModeConstants.VolumeNoRefraction, DebugModeConstants.VolumeOcclusion,
                DebugModeConstants.VolumeLipPink, DebugModeConstants.VolumeLipRawPink,
                DebugModeConstants.VolumeLipDepthGate, DebugModeConstants.SurfaceBackfacePink,
                DebugModeConstants.VolumeLipScenePink, DebugModeConstants.TerrainSourcePink,
                DebugModeConstants.FoamPink, DebugModeConstants.VolumeSphere,
                DebugModeConstants.TerrainFaceId, DebugModeConstants.SeaRay,
                DebugModeConstants.SeaVsMesh, DebugModeConstants.SeaPath,
                DebugModeConstants.SeaMatte, DebugModeConstants.SeaSourceMatte,
                DebugModeConstants.CausticsOnly, DebugModeConstants.CausticsMask,
                DebugModeConstants.CausticsLight, DebugModeConstants.BottomDistortionOnly,
                DebugModeConstants.BottomDistortionVector, DebugModeConstants.CausticsPrism));
    }

    static void RegisterMode(DebugRegistry registry, int localId, string name, string category)
    {
        registry.RegisterMode(WaterDebugIds.Mode(localId), name, category);
    }

    static DebugModeId[] Modes(params int[] localIds)
    {
        DebugModeId[] modes = new DebugModeId[localIds.Length];
        for (int i = 0; i < localIds.Length; i++)
            modes[i] = WaterDebugIds.Mode(localIds[i]);
        return modes;
    }

    void AppendWaterDebugMetadata(StringBuilder sb, DebugRuntimeState state)
    {
        ICameraRigContext cameraContext = state.CameraContext;
        Renderer waterRenderer = GetWaterRenderer();
        sb.AppendLine("--- Water ---");

        if (waterRenderer == null)
        {
            sb.AppendLine("Renderer: missing");
            return;
        }

        Material mat = waterRenderer.sharedMaterial;
        sb.AppendLine($"Shader: {(mat != null && mat.shader != null ? mat.shader.name : "missing")}");
        sb.AppendLine($"Focus: ocean={GetMaterialFloat(mat, _oceanFocusModeId):F2}, waterGlobal={Shader.GetGlobalFloat(_waterFocusModeId):F2}, debug={state.CurrentModeId}:{state.CurrentModeName}");
        sb.AppendLine($"Wave: amp={GetMaterialFloat(mat, _waveAmplitudeId):F2}, scale={GetMaterialFloat(mat, _waveScaleId):F2}, speed={GetMaterialFloat(mat, _waveSpeedId):F2}, normal={GetMaterialFloat(mat, _waveNormalStrengthId):F2}, motion={GetMaterialFloat(mat, _waterMotionStrengthId):F2}, shimmer={GetMaterialFloat(mat, _sunGlitterIntensityId):F2}");
        sb.AppendLine($"SurfaceFx: shoreFoam={GetMaterialFloat(mat, _shoreFoamIntensityId):F2}, whitecaps={GetMaterialFloat(mat, _whitecapIntensityId):F2}, wakeFoam={GetMaterialFloat(mat, _wakeFoamIntensityId):F2}, wakeNormal={GetMaterialFloat(mat, _wakeNormalStrengthId):F2}, wakeSources={Shader.GetGlobalInt(_waterWakeCountId)}");
        sb.AppendLine($"DepthFoam: shallow={GetMaterialFloat(mat, _shallowDepthId):F2}, deep={GetMaterialFloat(mat, _deepDepthId):F2}, foamWidth={GetMaterialFloat(mat, _shoreFoamDepthId):F2}, shoreRange={GetMaterialFloat(mat, _shoreFoamSoftnessId):F2}");
        sb.AppendLine(
            $"FrozenWater: enabled={GetMaterialFloat(mat, _freezingEnabledId) > 0.5f}, " +
            $"lake={GetMaterialFloat(mat, _lakeFreezeCompleteId):F3}-{GetMaterialFloat(mat, _lakeFreezeStartId):F3}, " +
            $"ocean={GetMaterialFloat(mat, _oceanFreezeCompleteId):F3}-{GetMaterialFloat(mat, _oceanFreezeStartId):F3}, " +
            $"bodies frozen/partial/liquid={Shader.GetGlobalInt(_frozenWaterBodiesId)}/" +
            $"{Shader.GetGlobalInt(_partiallyFrozenWaterBodiesId)}/{Shader.GetGlobalInt(_liquidWaterBodiesId)}");

        if (state.WeatherProvider != null && cameraContext != null)
            AppendWeatherMetadata(sb, state.WeatherProvider, cameraContext);

        RefreshWaterDebugStats(waterRenderer, cameraContext);
        if (!_waterDebugStats.Valid)
        {
            sb.AppendLine("MeshData: missing vertex colors");
            return;
        }

        WaterDebugStats s = _waterDebugStats;
        sb.AppendLine($"Mesh: verts={s.Vertices}, tris={s.Triangles}");
        if (state.IncludeHeavyDiagnostics && cameraContext != null
            && waterRenderer.TryGetComponent(out MeshFilter waterFilter) && waterFilter.sharedMesh != null
            && TryAnalyzeMeshIntegrity(waterFilter.sharedMesh, waterFilter.transform, cameraContext.PlanetCenter, cameraContext.SeaLevelRadius, out MeshIntegrityStats waterIntegrity))
        {
            sb.AppendLine($"MeshIntegrity: degTris={waterIntegrity.DegenerateTriangles}, boundaryEdges={waterIntegrity.BoundaryEdges}, nonManifoldEdges={waterIntegrity.NonManifoldEdges}, openEdgeVerts={waterIntegrity.OpenEdgeVertices}, seaRadErrAvgM={waterIntegrity.RadiusErrorAvgMeters:F3}, seaRadErrMaxM={waterIntegrity.RadiusErrorMaxMeters:F3}");
        }

        MeshFilter volumeLipFilter = GetWaterVolumeLipFilter(waterRenderer);
        Mesh volumeLipMesh = volumeLipFilter != null ? volumeLipFilter.sharedMesh : null;
        if (volumeLipMesh != null)
        {
            int volumeLipTriangles = volumeLipMesh.subMeshCount > 0 ? (int)(volumeLipMesh.GetIndexCount(0) / 3) : 0;
            sb.AppendLine($"VolumeLipMesh: active={volumeLipFilter.gameObject.activeInHierarchy}, verts={volumeLipMesh.vertexCount}, tris={volumeLipTriangles}");
            if (state.IncludeHeavyDiagnostics && cameraContext != null
                && TryAnalyzeMeshIntegrity(volumeLipMesh, volumeLipFilter.transform, cameraContext.PlanetCenter, cameraContext.SeaLevelRadius, out MeshIntegrityStats lipIntegrity))
            {
                sb.AppendLine($"VolumeLipIntegrity: degTris={lipIntegrity.DegenerateTriangles}, boundaryEdges={lipIntegrity.BoundaryEdges}, nonManifoldEdges={lipIntegrity.NonManifoldEdges}, openEdgeVerts={lipIntegrity.OpenEdgeVertices}, seaRadErrAvgM={lipIntegrity.RadiusErrorAvgMeters:F3}, seaRadErrMaxM={lipIntegrity.RadiusErrorMaxMeters:F3}");
            }
        }
        else
        {
            sb.AppendLine("VolumeLipMesh: missing");
        }

        sb.AppendLine($"DataRanges: depth={s.DepthMin:F3}-{s.DepthMax:F3} avg={s.DepthAvg:F3}, shore={s.ShoreMin:F3}-{s.ShoreMax:F3} avg={s.ShoreAvg:F3}, body={s.BodyMin:F3}-{s.BodyMax:F3} avg={s.BodyAvg:F3}, temp={s.TemperatureMin:F3}-{s.TemperatureMax:F3} avg={s.TemperatureAvg:F3}");
        sb.AppendLine($"CameraSample: depth={s.SampleDepth:F3}, shore={s.SampleShore:F3}, body={s.SampleBody:F3}, temp={s.SampleTemperature:F3}, motionMask={s.MotionMaskSample:F3}, normalMask={s.NormalMaskSample:F3}");
        sb.AppendLine($"MotionMask: avg={s.MotionMaskAvg:F3}, max={s.MotionMaskMax:F3}, eligible={s.MotionEligiblePercent:F1}%");
        sb.AppendLine($"NormalMask: avg={s.NormalMaskAvg:F3}, max={s.NormalMaskMax:F3}, eligible={s.NormalEligiblePercent:F1}%");
    }

    static void AppendWeatherMetadata(StringBuilder sb, IWeatherProvider weatherProvider, ICameraRigContext cameraContext)
    {
        Vector3 samplePosition = cameraContext.CameraTransform.position;
        Vector3 fromCenter = cameraContext.CameraTransform.position - cameraContext.PlanetCenter;
        if (cameraContext.SeaLevelRadius > 0f && fromCenter.sqrMagnitude > 0.0001f)
            samplePosition = cameraContext.PlanetCenter + fromCenter.normalized * cameraContext.SeaLevelRadius;

        WeatherSample weather = weatherProvider.SampleWeather(samplePosition);
        float wind01 = Mathf.Clamp01(weatherProvider.WindSpeed / 5f);
        float waveState = Mathf.Clamp01(0.18f + wind01 * 0.82f);
        float foamState = Mathf.Clamp01(0.12f + wind01 * 0.58f + weather.StormIntensity * 0.72f);
        sb.AppendLine($"Weather: wind={weatherProvider.WindSpeed:F2}, wave={waveState:F2}, foam={foamState:F2}, storm={weather.StormIntensity:F2}, rain={weather.Precipitation:F2}, state={weather.State}");
    }

    void DrawWaterDebugOverlay(DebugRuntimeState state)
    {
        ICameraRigContext cameraContext = state.CameraContext;
        Renderer waterRenderer = GetWaterRenderer();
        GUILayout.Space(6);
        GUILayout.Label("Water Debug");

        if (waterRenderer == null)
        {
            GUILayout.Label("Water renderer: missing");
            return;
        }

        Material mat = waterRenderer.sharedMaterial;
        GUILayout.Label($"Shader: {(mat != null && mat.shader != null ? mat.shader.name : "missing")}");
        GUILayout.Label($"Focus: ocean={GetMaterialFloat(mat, _oceanFocusModeId):F1}, waterGlobal={Shader.GetGlobalFloat(_waterFocusModeId):F1}, debug={state.CurrentModeId}:{state.CurrentModeName}");
        GUILayout.Label($"Wave: amp={GetMaterialFloat(mat, _waveAmplitudeId):F2}, scale={GetMaterialFloat(mat, _waveScaleId):F1}, speed={GetMaterialFloat(mat, _waveSpeedId):F2}, normal={GetMaterialFloat(mat, _waveNormalStrengthId):F2}, motion={GetMaterialFloat(mat, _waterMotionStrengthId):F2}, shimmer={GetMaterialFloat(mat, _sunGlitterIntensityId):F2}");
        GUILayout.Label($"SurfaceFx: shoreFoam={GetMaterialFloat(mat, _shoreFoamIntensityId):F2}, whitecaps={GetMaterialFloat(mat, _whitecapIntensityId):F2}, wakeFoam={GetMaterialFloat(mat, _wakeFoamIntensityId):F2}, wakeSources={Shader.GetGlobalInt(_waterWakeCountId)}");
        GUILayout.Label($"Depth/Foam: shallow={GetMaterialFloat(mat, _shallowDepthId):F1}, deep={GetMaterialFloat(mat, _deepDepthId):F1}, foamWidth={GetMaterialFloat(mat, _shoreFoamDepthId):F1}, shoreRange={GetMaterialFloat(mat, _shoreFoamSoftnessId):F1}");

        if (state.WeatherProvider != null && cameraContext != null)
            DrawWeatherOverlay(state.WeatherProvider, cameraContext);

        if (Time.unscaledTime >= _nextWaterDebugRefreshTime || waterRenderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != _cachedWaterMesh)
        {
            RefreshWaterDebugStats(waterRenderer, cameraContext);
            _nextWaterDebugRefreshTime = Time.unscaledTime + 0.75f;
        }

        if (!_waterDebugStats.Valid)
        {
            GUILayout.Label("Mesh water data: missing vertex colors");
            return;
        }

        WaterDebugStats s = _waterDebugStats;
        GUILayout.Label($"Mesh: verts={s.Vertices}, tris={s.Triangles}");
        GUILayout.Label($"Data ranges: depth {s.DepthMin:F2}-{s.DepthMax:F2} avg {s.DepthAvg:F2}, shore {s.ShoreMin:F2}-{s.ShoreMax:F2} avg {s.ShoreAvg:F2}, body {s.BodyMin:F2}-{s.BodyMax:F2} avg {s.BodyAvg:F2}");
        GUILayout.Label($"Camera sample: depth={s.SampleDepth:F2}, shore={s.SampleShore:F2}, body={s.SampleBody:F2}, motionMask={s.MotionMaskSample:F2}, normalMask={s.NormalMaskSample:F2}");
        GUILayout.Label($"Motion mask: avg={s.MotionMaskAvg:F2}, max={s.MotionMaskMax:F2}, eligible>{0.05f:F2}={s.MotionEligiblePercent:F1}%");
        GUILayout.Label($"Normal mask: avg={s.NormalMaskAvg:F2}, max={s.NormalMaskMax:F2}, eligible>{0.05f:F2}={s.NormalEligiblePercent:F1}%");
        GUILayout.Label("F10 sets: Water Artifact is concise; use F7 to cycle focused, current-mode, or full-loop captures.");
    }

    static void DrawWeatherOverlay(IWeatherProvider weatherProvider, ICameraRigContext cameraContext)
    {
        Vector3 samplePosition = cameraContext.CameraTransform.position;
        Vector3 fromCenter = cameraContext.CameraTransform.position - cameraContext.PlanetCenter;
        if (cameraContext.SeaLevelRadius > 0f && fromCenter.sqrMagnitude > 0.0001f)
            samplePosition = cameraContext.PlanetCenter + fromCenter.normalized * cameraContext.SeaLevelRadius;

        WeatherSample weather = weatherProvider.SampleWeather(samplePosition);
        float wind01 = Mathf.Clamp01(weatherProvider.WindSpeed / 5f);
        float waveState = Mathf.Clamp01(0.18f + wind01 * 0.82f);
        float foamState = Mathf.Clamp01(0.12f + wind01 * 0.58f + weather.StormIntensity * 0.72f);
        GUILayout.Label($"Weather/waves: wind={weatherProvider.WindSpeed:F2}, wave={waveState:F2}, foam={foamState:F2}, storm={weather.StormIntensity:F2}, rain={weather.Precipitation:F2}, state={weather.State}");
    }

    Renderer GetWaterRenderer()
    {
        if (_cachedWaterRenderer != null && _cachedWaterRenderer.enabled && _cachedWaterRenderer.gameObject.activeInHierarchy)
            return _cachedWaterRenderer;

        GameObject waterObject = GameObject.Find("Water");
        if (waterObject != null && waterObject.TryGetComponent(out Renderer waterRenderer))
        {
            _cachedWaterRenderer = waterRenderer;
            return _cachedWaterRenderer;
        }

        Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material mat = renderers[i].sharedMaterial;
            if (mat != null && mat.shader != null && mat.shader.name == "Planet/Ocean")
            {
                _cachedWaterRenderer = renderers[i];
                return _cachedWaterRenderer;
            }
        }

        return null;
    }

    static MeshFilter GetWaterVolumeLipFilter(Renderer waterRenderer)
    {
        if (waterRenderer == null)
            return null;

        Transform lip = waterRenderer.transform.Find("WaterVolumeLip");
        return lip != null ? lip.GetComponent<MeshFilter>() : null;
    }

    static bool TryAnalyzeMeshIntegrity(Mesh mesh, Transform meshTransform, Vector3 planetCenter, float expectedRadius, out MeshIntegrityStats stats)
    {
        stats = default;
        if (mesh == null)
            return false;

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length < 3)
            return false;

        Dictionary<ulong, int> edgeUse = new Dictionary<ulong, int>(triangles.Length);
        HashSet<int> openVertices = new HashSet<int>();

        int validTriangles = 0;
        float radiusErrorSum = 0f;
        int radiusSamples = 0;

        for (int i = 0; i <= triangles.Length - 3; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                continue;

            validTriangles++;

            Vector3 a = vertices[i0];
            Vector3 b = vertices[i1];
            Vector3 c = vertices[i2];
            float areaSq4 = Vector3.Cross(b - a, c - a).sqrMagnitude;
            if (areaSq4 <= 0.0000000001f)
                stats.DegenerateTriangles++;

            IncrementEdgeCount(edgeUse, i0, i1);
            IncrementEdgeCount(edgeUse, i1, i2);
            IncrementEdgeCount(edgeUse, i2, i0);
        }

        foreach (KeyValuePair<ulong, int> kv in edgeUse)
        {
            if (kv.Value == 1)
            {
                stats.BoundaryEdges++;
                DecodeEdge(kv.Key, out int ea, out int eb);
                openVertices.Add(ea);
                openVertices.Add(eb);
            }
            else if (kv.Value > 2)
            {
                stats.NonManifoldEdges++;
            }
        }

        stats.OpenEdgeVertices = openVertices.Count;

        if (expectedRadius > 0f && meshTransform != null)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = meshTransform.TransformPoint(vertices[i]);
                float radius = Vector3.Distance(world, planetCenter);
                float error = Mathf.Abs(radius - expectedRadius);
                radiusErrorSum += error;
                stats.RadiusErrorMaxMeters = Mathf.Max(stats.RadiusErrorMaxMeters, error);
                radiusSamples++;
            }
        }

        if (radiusSamples > 0)
            stats.RadiusErrorAvgMeters = radiusErrorSum / radiusSamples;

        return validTriangles > 0;
    }

    static void IncrementEdgeCount(Dictionary<ulong, int> edgeUse, int a, int b)
    {
        ulong key = EncodeEdge(a, b);
        edgeUse.TryGetValue(key, out int count);
        edgeUse[key] = count + 1;
    }

    static ulong EncodeEdge(int a, int b)
    {
        uint min = (uint)Mathf.Min(a, b);
        uint max = (uint)Mathf.Max(a, b);
        return ((ulong)min << 32) | max;
    }

    static void DecodeEdge(ulong key, out int a, out int b)
    {
        a = (int)(key >> 32);
        b = (int)(key & 0xFFFFFFFFu);
    }

    void RefreshWaterDebugStats(Renderer waterRenderer, ICameraRigContext cameraContext)
    {
        _waterDebugStats = default;

        if (waterRenderer == null || !waterRenderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
            return;

        Mesh mesh = filter.sharedMesh;
        Color[] colors = mesh.colors;
        if (colors == null || colors.Length == 0)
            return;

        Vector3[] vertices = mesh.vertices;
        int count = Mathf.Min(colors.Length, vertices != null ? vertices.Length : colors.Length);
        if (count <= 0)
            return;

        // The mesh-static aggregates (min/max/avg/percent) only change when the underlying mesh
        // does, so recompute them lazily and reuse for all subsequent refreshes.
        if (_meshStatsCache.Source != mesh)
            _meshStatsCache = RecomputeMeshStats(mesh, colors, count);

        // Camera-direction-best vertex stays per-refresh; it's now a single dot product per
        // vertex instead of the full histogram pass.
        Vector3 localCamera = cameraContext != null ? waterRenderer.transform.InverseTransformPoint(cameraContext.CameraTransform.position) : Vector3.zero;
        Vector3 localCameraDir = localCamera.sqrMagnitude > 0.0001f ? localCamera.normalized : Vector3.up;
        int sampleIndex = 0;
        float bestAlignment = -2f;
        if (vertices != null)
        {
            for (int i = 0; i < count; i++)
            {
                if (vertices[i].sqrMagnitude <= 0.0001f) continue;
                float alignment = Vector3.Dot(vertices[i].normalized, localCameraDir);
                if (alignment > bestAlignment)
                {
                    bestAlignment = alignment;
                    sampleIndex = i;
                }
            }
        }

        var stats = new WaterDebugStats
        {
            Valid = true,
            Vertices = _meshStatsCache.Vertices,
            Triangles = _meshStatsCache.Triangles,
            DepthMin = _meshStatsCache.DepthMin,
            DepthMax = _meshStatsCache.DepthMax,
            DepthAvg = _meshStatsCache.DepthAvg,
            ShoreMin = _meshStatsCache.ShoreMin,
            ShoreMax = _meshStatsCache.ShoreMax,
            ShoreAvg = _meshStatsCache.ShoreAvg,
            BodyMin = _meshStatsCache.BodyMin,
            BodyMax = _meshStatsCache.BodyMax,
            BodyAvg = _meshStatsCache.BodyAvg,
            TemperatureMin = _meshStatsCache.TemperatureMin,
            TemperatureMax = _meshStatsCache.TemperatureMax,
            TemperatureAvg = _meshStatsCache.TemperatureAvg,
            MotionMaskAvg = _meshStatsCache.MotionMaskAvg,
            MotionMaskMax = _meshStatsCache.MotionMaskMax,
            NormalMaskAvg = _meshStatsCache.NormalMaskAvg,
            NormalMaskMax = _meshStatsCache.NormalMaskMax,
            MotionEligiblePercent = _meshStatsCache.MotionEligiblePercent,
            NormalEligiblePercent = _meshStatsCache.NormalEligiblePercent,
        };

        Color sample = colors[Mathf.Clamp(sampleIndex, 0, colors.Length - 1)];
        stats.SampleDepth = Mathf.Clamp01(sample.r);
        stats.SampleShore = Mathf.Clamp01(sample.g);
        stats.SampleBody = Mathf.Clamp01(sample.b);
        stats.SampleTemperature = Mathf.Clamp01(sample.a);
        stats.MotionMaskSample = FocusMotionMask(stats.SampleDepth, stats.SampleShore, stats.SampleBody);
        stats.NormalMaskSample = FocusNormalMask(stats.SampleDepth, stats.SampleShore, stats.SampleBody);

        _cachedWaterMesh = mesh;
        _waterDebugStats = stats;
    }

    static WaterMeshStatsCache RecomputeMeshStats(Mesh mesh, Color[] colors, int count)
    {
        var cache = new WaterMeshStatsCache
        {
            Source = mesh,
            Vertices = mesh.vertexCount,
            Triangles = mesh.subMeshCount > 0 ? (int)(mesh.GetIndexCount(0) / 3) : 0,
            DepthMin = 1f,
            ShoreMin = 1f,
            BodyMin = 1f,
            TemperatureMin = 1f,
        };

        int motionEligible = 0;
        int normalEligible = 0;
        for (int i = 0; i < count; i++)
        {
            Color c = colors[i];
            float depth = Mathf.Clamp01(c.r);
            float shore = Mathf.Clamp01(c.g);
            float body = Mathf.Clamp01(c.b);
            float temperature = Mathf.Clamp01(c.a);
            float motionMask = FocusMotionMask(depth, shore, body);
            float normalMask = FocusNormalMask(depth, shore, body);

            cache.DepthMin = Mathf.Min(cache.DepthMin, depth);
            cache.DepthMax = Mathf.Max(cache.DepthMax, depth);
            cache.DepthAvg += depth;
            cache.ShoreMin = Mathf.Min(cache.ShoreMin, shore);
            cache.ShoreMax = Mathf.Max(cache.ShoreMax, shore);
            cache.ShoreAvg += shore;
            cache.BodyMin = Mathf.Min(cache.BodyMin, body);
            cache.BodyMax = Mathf.Max(cache.BodyMax, body);
            cache.BodyAvg += body;
            cache.TemperatureMin = Mathf.Min(cache.TemperatureMin, temperature);
            cache.TemperatureMax = Mathf.Max(cache.TemperatureMax, temperature);
            cache.TemperatureAvg += temperature;
            cache.MotionMaskAvg += motionMask;
            cache.MotionMaskMax = Mathf.Max(cache.MotionMaskMax, motionMask);
            cache.NormalMaskAvg += normalMask;
            cache.NormalMaskMax = Mathf.Max(cache.NormalMaskMax, normalMask);

            if (motionMask > 0.05f) motionEligible++;
            if (normalMask > 0.05f) normalEligible++;
        }

        float invCount = 1f / count;
        cache.DepthAvg *= invCount;
        cache.ShoreAvg *= invCount;
        cache.BodyAvg *= invCount;
        cache.TemperatureAvg *= invCount;
        cache.MotionMaskAvg *= invCount;
        cache.NormalMaskAvg *= invCount;
        cache.MotionEligiblePercent = motionEligible * 100f * invCount;
        cache.NormalEligiblePercent = normalEligible * 100f * invCount;

        return cache;
    }

    struct WaterMeshStatsCache
    {
        public Mesh Source;
        public int Vertices;
        public int Triangles;
        public float DepthMin, DepthMax, DepthAvg;
        public float ShoreMin, ShoreMax, ShoreAvg;
        public float BodyMin, BodyMax, BodyAvg;
        public float TemperatureMin, TemperatureMax, TemperatureAvg;
        public float MotionMaskAvg, MotionMaskMax;
        public float NormalMaskAvg, NormalMaskMax;
        public float MotionEligiblePercent;
        public float NormalEligiblePercent;
    }

    static float FocusMotionMask(float depth, float shore, float body)
    {
        float depthRelease = SmoothStep(0.02f, 0.18f, depth);
        float shoreRelease = SmoothStep(0.02f, 0.18f, shore) * 0.58f;
        return body * Mathf.Clamp01(Mathf.Max(depthRelease, shoreRelease));
    }

    static float FocusNormalMask(float depth, float shore, float body)
    {
        float depthRelease = SmoothStep(0.012f, 0.10f, depth);
        float shoreRelease = SmoothStep(0.012f, 0.10f, shore) * 0.72f;
        return body * Mathf.Clamp01(Mathf.Max(depthRelease, shoreRelease));
    }

    static float SmoothStep(float edge0, float edge1, float value)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, value));
    }

    static float GetMaterialFloat(Material mat, int id)
    {
        return mat != null && mat.HasProperty(id) ? mat.GetFloat(id) : float.NaN;
    }
}
