# Codex Rendering History

Search the Codex indexes before reopening these topics:

- Water: `WaterArtifact`, `TerrainSourcePink`, `SeaRay`,
  `BottomDistortionOnly`, `WaterVolumeLip`, `WaterData`,
  `SurfaceFxContrib`.
- Clouds: `CloudWeather`, `CubeFaceUv`, `WeatherSampling`.
- Grass: `mesh-visible-terrain`, `MarkerProjection`, rejection counters,
  density instrumentation.
- Paths: path wear is an R8 vector-baked mask, not true SDF. Keep hard-disc
  support; jagged hard-disc edges were fixed by antialiasing the baked
  hard-disc mask edge in `ChunkedSurfaceProvider.PathWearMask` with
  `HardDiscEdgePixels` rather than removing hard-disc from the mouse tool.
- Surface edits: saved `SurfaceEditStamp` records are the source of truth.
  Path wear, scorch, and future terrain/edit textures are derived caches that
  can be rebuilt from stamps. Keep `path.*` and `scorch.*` console commands as
  wrappers over the shared `SurfaceEditController`.
