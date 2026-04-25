# Phase 4: Enhanced Biome System
*Goal: Biomes that make geographic sense with smooth transitions*

## 4.1 — Latitude + Moisture Biome Placement
- [ ] Replace y-axis-only biome selection with latitude-based calculation (angle from poles)
- [ ] Add temperature gradient: hot at equator, cold at poles, with noise perturbation
- [ ] Add moisture noise layer (separate from terrain noise, different frequency/seed)
- [ ] Create temperature × moisture lookup table to determine biome type
- [ ] Ensure transitions are geographically logical (forest → grassland → desert, never tundra → desert)

## 4.2 — Biome Type Definitions
- [ ] Define biome enum: `Ocean, Beach, Tropical, Grassland, Forest, Taiga, Desert, Tundra, Mountain, Snow, Underwater, Cave`
- [ ] Create `BiomeDefinition` ScriptableObject: color gradient, tint, spawn rules, allowed neighbors, temperature range, moisture range
- [ ] Create `BiomeMap` ScriptableObject: holds the temperature × moisture → biome lookup
- [ ] Add biome adjacency validation (which biomes can neighbor each other)

## 4.3 — Biome Blending & Shader
- [ ] Implement smooth biome transition zones using noise-perturbed boundaries
- [ ] Add per-biome blend width settings
- [ ] Update planet shader to support new biome texture format
- [ ] Ocean biome determined by elevation (below sea level = ocean) regardless of temperature/moisture
- [ ] Biome data accessible per-vertex for spawning systems
