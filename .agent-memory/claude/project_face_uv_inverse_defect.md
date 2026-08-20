---
name: project_face_uv_inverse_defect
description: D12 — CoordinateConverter.UnitSphereToCubeFace was NOT an inverse; every ground query read a mirrored point
metadata:
  type: project
---

**Fixed 2026-08-19, commit `8fdd1d2`.** Kept because the failure mode is invisible and easy to reintroduce.

`CoordinateConverter.UnitSphereToCubeFace` looked like the inverse of `CubeFaceToUnitSphere` and was not.
It picked the **correct face** but mirrored the UV inside it — v flipped on faces 0 and 1, both axes on 2
and 3, swapped and flipped on 4. **Round-trip error: mean 55°, max 109° — 4.8 km at R=5000.**
`UnitSphereToCubeFaceUvExact` round-trips to **1.3 mm**.

**Why nobody noticed for so long.** It returns a *plausible* radius — right planet, right face, wrong place
— so terrain height queries came back in the correct range and nothing visibly exploded. Every ground query
went through it: `Planet.TryGetSurfaceRadius` → `IPlanetSurfaceProvider.TryGetLocalSurfaceRadius`, which
**both** providers implemented with it. That covered character grounding, `FreeCameraController`,
`ScaleReferenceMarkers`, grass altitude and `TrySampleClimate`.

**Measured against `AnalyticGroundSampler` over 300 random directions:**

| | agree within 1 m | mean error | max |
|---|---|---|---|
| before | 1 / 300 | 101.41 m | 380.65 m |
| after | 300 / 300 | 0.008 m | 0.205 m |

Elevation range is about ±250 m, so the "before" numbers are effectively uncorrelated values.

**Which one is correct, and how to tell.** `UnitSphereToCubeFaceUvExact` uses the same
`localUp / axisA = (up.y, up.z, up.x) / axisB = cross(up, axisA)` basis that `PlanetChunkMeshJob` builds
its vertices from, so a direction resolves to the UV the mesh was actually generated at. Independent
corroboration: the analytic sampler already agreed with what the world *looks* like — lakes sit in their
basins, scatter sits on the ground — so the chunk query was the outlier, not the analytic one.

`UnitSphereToChunkCoord` had the same bug: it paired the old inverse with `CubeFaceToUnitSphere` inside
`ChunkCoordToUnitSphere`, so a coordinate round trip moved.

**The old function is deleted**, not left beside its replacement. If something reintroduces a
"UnitSphereToCubeFace", check it round-trips before trusting it — that check is three lines and would have
caught this years earlier:

```
UnitSphereToCubeFaceUvExact(d, out f, out uv);  Vector3.Angle(d, CubeFaceToUnitSphere(f, uv)) < 0.02
```

**Note there are at least three cube-face UV conventions in the tree** (this pair, plus
`FaceSpaceCellRangeBuilder`'s used by grass/scatter/`WaterBodyMap`, plus the HLSL mirror in
`WaterLevelField.hlsl`). They are not interchangeable. Related: [[project_water_architecture_build]].

## Audit of every other projection pair (2026-08-20) — all clean

One pair being silently wrong is reason to check the rest. Round-tripped 3000 random directions:

| pair | result |
|---|---|
| `CoordinateConverter` exact ↔ forward | max 0.0198 deg = 1.7 m |
| `FaceSpaceCellRangeBuilder` `DirectionToFaceUv` ↔ `CubeFaceToUnitSphere` | max 0.0198 deg = 1.7 m |
| `WaterLevelGrid.Index` vs `WaterBodyMap`'s own indexer | **0 / 3000 cell mismatches** |
| `ChunkCoord` round trip | max 1.21 deg against a 1.41 deg cell — inside one cell |

The 1.7 m worst case on the first two is `Mathf.Clamp01` at exact face corners, not a mirror error — the
mean is 0.000015 deg. **D12 was the only broken pair.** Re-run this audit whenever a projection is added.
