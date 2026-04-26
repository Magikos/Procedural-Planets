# Phase 4: Enhanced Biome System
*Goal: Biomes that make geographic sense with smooth transitions*

## 4.1 — Latitude + Moisture Biome Placement
- [x] Replace y-axis-only biome selection with latitude-based calculation (angle from poles)
- [x] Add temperature gradient: hot at equator, cold at poles, with noise perturbation
- [x] Add moisture noise layer (separate from terrain noise, different frequency/seed)
- [x] Create temperature × moisture lookup table to determine biome type
- [x] Ensure transitions are geographically logical (forest → grassland → desert, never tundra → desert)

## 4.2 — Biome Type Definitions
- [x] BiomeType enum already exists with 17 types (Core/Data/BiomeTypes.cs)
- [x] Create `BiomeDefinition` ScriptableObject: color gradient, tint per biome
- [x] Create `BiomeRegistry` ScriptableObject: holds the temperature × moisture → biome lookup
- [x] Elevation overrides: Ocean (below sea level), Beach (near sea level), Mountain (high elevation)
- [x] Blending at grid cell boundaries with configurable blend width

## 4.3 — Biome Blending & Shader
- [x] Implement smooth biome transition zones using noise-perturbed boundaries (via temperature/moisture noise)
- [x] Configurable blend width in BiomeRegistry
- [x] Ocean biome determined by elevation (below threshold) regardless of temperature/moisture
- [x] Biome data accessible per-vertex via BiomeResult struct (temperature, moisture, blend weight)
- [ ] Update planet shader to support new biome texture format (if needed after testing)

## Files Created
- `Assets/Scripts/Core/Interfaces/ITemperatureProvider.cs` — temperature evaluation interface
- `Assets/Scripts/Core/Interfaces/IMoistureProvider.cs` — moisture evaluation interface
- `Assets/Scripts/Core/Interfaces/IBiomeRegistry.cs` — biome lookup interface
- `Assets/Scripts/Planet/Biomes/BiomeDefinition.cs` — per-biome ScriptableObject
- `Assets/Scripts/Planet/Biomes/BiomeRegistry.cs` — temperature × moisture grid ScriptableObject
- `Assets/Scripts/Planet/Biomes/BiomeSettings.cs` — noise settings for temp/moisture
- `Assets/Scripts/Planet/Biomes/TemperatureProvider.cs` — latitude + noise implementation
- `Assets/Scripts/Planet/Biomes/MoistureProvider.cs` — noise-based moisture implementation

## Files Modified
- `Assets/Scripts/Planet/ColorSettings.cs` — replaced BiomeColorSettings with BiomeSettings reference
- `Assets/Scripts/Planet/ColorGenerator.cs` — refactored to use new temp/moisture/registry providers

## Known Bugs
- **Coastline rainbow strip**: At the ocean-to-land transition, a thin multi-colored strip appears tracing the coastline. Likely caused by the shader's elevation-based texture sampling interacting with the biome texture at very small elevation values near sea level. Expected to resolve when: (a) planet scale increases to ~5km radius, (b) water mesh is added (Phase 5) covering the transition zone, or (c) shader is updated to use vertex colors instead of texture lookup.

## Setup Instructions (in Unity)
1. Create BiomeDefinition assets: Right-click → Create → Planet → Biomes → Biome Definition (one per biome)
2. Create BiomeRegistry asset: Right-click → Create → Planet → Biomes → Biome Registry
3. Assign BiomeDefinitions to the registry's GridEntries (row-major: temp cold→hot, moisture dry→wet)
4. Assign Ocean/Beach/Mountain override biomes
5. Create BiomeSettings asset: Right-click → Create → Planet → Settings → Biome Settings
6. Assign the BiomeRegistry and configure temperature/moisture noise
7. Assign BiomeSettings to your ColorSettings asset
