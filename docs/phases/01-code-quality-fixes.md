# Code Quality Fixes (Do Before New Phases)

- [ ] **Fix MinMax not resetting** — `ShapeGenerator._elevationMinMax` is never reset in `UpdateSettings`; add `_elevationMinMax = new MinMax()` at the start of `UpdateSettings` (matches E07 reference behavior)
- [ ] **Fix `_resolution` naming** — Rename public field `_resolution` to `Resolution` on Planet.cs to match project PascalCase convention for public fields
- [ ] **Add null guards to Planet.GeneratePlanet** — Check `_shapeSettings` and `_colorSettings` are assigned before proceeding; log warning if missing
- [ ] **Fix PoissonDiscSampling determinism** — Replace `Random.Range` / `Random.value` with `System.Random(seed)` parameter to match the sphere variant's deterministic approach
- [ ] **Optimize PoissonDiscSphereSampling** — Replace O(n²) `IsValid` loop with spatial hashing for large point counts
- [ ] **Add FaceRenderMask** — Port the `FaceRenderMask` enum from E07 reference for debugging individual cube faces
- [ ] **Fix mesh bounds** — Call `mesh.RecalculateBounds()` after mesh generation in TerrainFace to prevent culling issues at large radii
- [ ] **Cache unit sphere points in TerrainFace** — Store `pointOnUnitSphere` array during `ConstructMesh` so `UpdateUVs` doesn't recalculate them
- [ ] **Set up Assembly Definitions** — Create asmdef files to separate: Core, Planet, Editor, Tests — keeps compile times fast as codebase grows
- [ ] **Set up test framework** — Basic test project using Unity Test Framework for procedural systems (noise determinism, biome assignment, etc.)
