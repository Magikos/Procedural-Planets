// The water debug catalog: every water debug mode and F10 capture set the WaterDebugModule exposes.
// Purely declarative registry data, split out so the module file holds the runtime metadata/overlay/
// analysis logic rather than the long static mode tables.
static class WaterDebugRegistration
{
    public static void Register(DebugRegistry registry)
    {
        RegisterModes(registry);
        RegisterCaptureSets(registry);
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
        RegisterMode(registry, DebugModeConstants.WaterTemperature, "WaterTemperature", "Frozen Water");
        RegisterMode(registry, DebugModeConstants.WaterFreeze, "WaterFreeze", "Frozen Water");
        RegisterMode(registry, DebugModeConstants.WaterIceContribution, "WaterIceContribution", "Frozen Water");
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
        registry.RegisterCaptureSet(WaterDebugIds.Frozen, "Frozen Water",
            Modes(DebugModeConstants.Off, DebugModeConstants.SurfaceOnly,
                DebugModeConstants.WaterBody, DebugModeConstants.WaterTemperature,
                DebugModeConstants.WaterFreeze, DebugModeConstants.WaterIceContribution,
                DebugModeConstants.WaterMotionMask, DebugModeConstants.WaterNormals,
                DebugModeConstants.WaterFoam));
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
}
