#ifndef DEBUG_MODES_INCLUDED
#define DEBUG_MODES_INCLUDED

// Named constants for the _OceanDebugMode global integer.
// C# mirror: DebugModeConstants.cs
// Mode registration/names: WaterDebugModule.cs
//
// When adding a new mode:
//   1. Add RegisterMode call in WaterDebugModule.RegisterModes()
//   2. Add the constant here
//   3. Add the matching constant in DebugModeConstants.cs
//   4. Update any bypass lists that need to include the new mode

#define DEBUG_OFF                         0

// --- Water Surface (1-12) ---
#define DEBUG_WATER_DEPTH                 1
#define DEBUG_WATER_SHORE                 2
#define DEBUG_WATER_BODY                  3
#define DEBUG_WATER_LIGHTING              4
#define DEBUG_WATER_GLINT                 5
#define DEBUG_WATER_NORMALS               6
#define DEBUG_WATER_FOAM                  7
#define DEBUG_WATER_MOTION_MASK           8
#define DEBUG_WATER_WAVE_HEIGHT           9
#define DEBUG_WATER_WAVE_SLOPE            10
#define DEBUG_WATER_DATA                  11
#define DEBUG_WATER_ABSORPTION            12

// --- Water Volume (13-17) ---
#define DEBUG_VOLUME_DATA                 13
#define DEBUG_VOLUME_MASK                 14
#define DEBUG_VOLUME_PATH                 15
#define DEBUG_VOLUME_LIGHT                16
#define DEBUG_VOLUME_REFRACTION           17

// --- Water Surface continued ---
#define DEBUG_FOAM_PARTS                  18
#define DEBUG_SURFACE_ALPHA               19

// --- Water Volume continued ---
#define DEBUG_VOLUME_BOUNDARY             20
#define DEBUG_VOLUME_OPTICAL              21

// --- Water Surface continued ---
#define DEBUG_SURFACE_CONTACT             22
#define DEBUG_SURFACE_BLEND               23

// --- Water Split ---
#define DEBUG_VOLUME_ONLY                 24
#define DEBUG_SURFACE_ONLY                25
#define DEBUG_WATER_OFF                   26

// --- Water Volume continued ---
#define DEBUG_VOLUME_CONTACT              27
#define DEBUG_VOLUME_DILATION             28
#define DEBUG_VOLUME_NO_REFRACTION        29
#define DEBUG_VOLUME_OCCLUSION            30

// --- Water Source ---
#define DEBUG_TERRAIN_SOURCE_PINK         31
#define DEBUG_FOAM_PINK                   32

// --- Water Volume continued ---
#define DEBUG_VOLUME_SPHERE               33

// --- Terrain ---
#define DEBUG_TERRAIN_FACE_ID             34

// --- Water Volume: Sea ray ---
#define DEBUG_SEA_RAY                     35
#define DEBUG_SEA_VS_MESH                 36
#define DEBUG_SEA_PATH                    37
#define DEBUG_SEA_MATTE                   38
#define DEBUG_SEA_SOURCE_MATTE            39

// --- Atmosphere ---
#define DEBUG_ATMOSPHERE_BYPASS           40
#define DEBUG_VOLUME_AFTER_ATMOSPHERE     41
#define DEBUG_ATMOSPHERE_WATER_CUT        42

// --- Contribution heatmaps ---
#define DEBUG_VOLUME_CONTRIBUTION         43
#define DEBUG_ATMOSPHERE_CONTRIBUTION     44
#define DEBUG_PRECIPITATION_CONTRIBUTION  45

// --- Water Volume: Lip ---
#define DEBUG_VOLUME_LIP_PINK             46
#define DEBUG_VOLUME_LIP_RAW_PINK         47
#define DEBUG_VOLUME_LIP_DEPTH_GATE       48

// --- Water Surface: Backface / polish / isolation ---
#define DEBUG_SURFACE_BACKFACE_PINK       49
#define DEBUG_VOLUME_LIP_SCENE_PINK       50
#define DEBUG_WAKE_MASK                   51
#define DEBUG_SURFACE_POLISH              52
#define DEBUG_SURFACE_RAW_OPAQUE          53
#define DEBUG_SURFACE_FX_CONTRIB          54
#define DEBUG_SURFACE_ALPHA_PARTS         55
#define DEBUG_WATER_NO_POST               56
#define DEBUG_SURFACE_FX_PROOF            57
#define DEBUG_CAUSTICS_ONLY               58
#define DEBUG_CAUSTICS_MASK               59
#define DEBUG_CAUSTICS_LIGHT              60
#define DEBUG_BOTTOM_DISTORTION_ONLY      61
#define DEBUG_BOTTOM_DISTORTION_VECTOR    62
#define DEBUG_CAUSTICS_PRISM              63

// --- Water Surface: night-lighting discovery ---
#define DEBUG_SURFACE_NIGHT_TERMS         64
#define DEBUG_SURFACE_LUMA_HEAT           65

// --- Water Surface: wave / swell discovery ---
#define DEBUG_WATER_WAVE_SWELL            66
#define DEBUG_WATER_WAVE_ENERGY           67
#define DEBUG_WATER_WAVE_PHASE            68
#define DEBUG_WATER_WAVE_GRID             69

// --- Water Surface: foam discovery ---
#define DEBUG_WATER_FOAM_ON_SWELL         70
#define DEBUG_WATER_FOAM_LOCATOR          71

// --- Water Surface: glint discovery ---
#define DEBUG_WATER_GLINT_LOCATOR         72

// --- Biome diagnostics (73-86) ---
// Modes 73-77 read per-vertex biome data. Modes 78-80 read the per-chunk
// biome map used by the texture-mode production terrain path. Modes 81-82
// verify the production terrain albedo and lighting inputs.
#define DEBUG_BIOME_PRIMARY_ID            73
#define DEBUG_BIOME_TEMPERATURE           74
#define DEBUG_BIOME_MOISTURE              75
#define DEBUG_BIOME_LATITUDE              76
#define DEBUG_BIOME_ELEVATION_BAND        77
#define DEBUG_BIOME_MAP_PRIMARY_ID        78
#define DEBUG_BIOME_MAP_BLEND             79
#define DEBUG_BIOME_MAP_FLAT_COLOR        80
#define DEBUG_TERRAIN_SELECTED_ALBEDO     81
#define DEBUG_TERRAIN_SUN_LIGHTING        82
// Phase B step 8: triplanar PBR signal visualization.
#define DEBUG_TERRAIN_SURFACE_NORMAL      83
#define DEBUG_TERRAIN_SURFACE_AO          84
#define DEBUG_TERRAIN_SURFACE_ROUGHNESS   85
#define DEBUG_GRASS_LOD_COVERAGE          86

// Convenience: last defined mode. Update when adding new modes.
#define DEBUG_MODE_MAX                    86

#endif
