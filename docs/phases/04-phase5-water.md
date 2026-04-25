# Phase 5: Water System
*Goal: Oceans with depth, shoreline foam, underwater zones, and continental terrain*

## 5.1 — Continental Terrain Layer
- [ ] Add continental noise layer: very low frequency simplex noise defining landmass vs ocean basin
- [ ] Continental noise multiplied with terrain noise — land areas get full terrain, ocean basins are flat/deep
- [ ] Sea level parameter cuts through continental noise to create coastlines
- [ ] Ensure distinct continents with navigable ocean channels between them
- [ ] Continent shapes varied by seed — some worlds have many small islands, others have large landmasses

## 5.2 — Ocean Mesh & Shader
- [ ] Add `SeaLevel` property to ShapeSettings (normalized elevation threshold)
- [ ] Create separate ocean mesh (sphere at sea level radius) with transparency
- [ ] Ocean shader: depth-based color, basic wave animation (vertex displacement), shoreline foam
- [ ] Depth fog when camera is near/below water surface
- [ ] Update biome system to classify below-sea-level as Ocean/Underwater

## 5.3 — Ocean Visual Polish
- [ ] Ocean color gradient based on depth (shallow = turquoise, deep = dark blue)
- [ ] Fresnel-based reflection on ocean surface
- [ ] Caustics projection on shallow ocean floor
- [ ] Wave height variation based on distance from shore

## 5.4 — Underwater
- [ ] Underwater biome zones based on ocean depth
- [ ] Underwater color/fog post-processing when camera submerges
- [ ] Underwater-specific spawn rules (coral, seaweed, rocks) — spawning system from Phase 8

## 5.5 — Rivers (Stretch)
- [ ] Flow simulation: trace downhill paths from high elevation to ocean
- [ ] Carve river channels by modifying terrain elevation
- [ ] River mesh strips following carved paths
- [ ] River shader with flow-direction UVs and foam
