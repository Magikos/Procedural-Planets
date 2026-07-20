# Cube-Sphere Fundamentals — as implemented here

Part of `pp-gpu-rendering-reference`. Verified against the working tree 2026-07-06.

Every spherical dataset in this project — weather grid, climate map, biome atlases,
surface radius/normal atlases, grass placement cells — is stored as **six square textures
(or texture-array slices), one per cube face**, and addressed by `(face, uv)`. A world
direction maps to a face by its dominant axis, and to UV by **gnomonic projection**
(divide the other two components by the dominant one). The payoff over lat-long: near-uniform
texel density, no polar pinch. The cost: face seams and area distortion, both of which
have bitten this project and both of which have established handling patterns below.

## Face layout (all conventions agree on this part)

From `Assets/Graphics/Shaders/Includes/WeatherCubeFace.hlsl` header:

```
Face layout: 0=+Y, 1=-Y, 2=-X, 3=+X, 4=+Z, 5=-Z
```

Face selection is identical everywhere: compare `abs(direction)` components; ties break
in the order Y, then X, then Z (`absY >= absX && absY >= absZ` → face 0/1, etc.).

## THE pitfall: three UV-orientation conventions coexist

Face *selection* is shared; UV *orientation within a face* is not. There are three
matched forward/inverse pairs. **A dataset written with one pair must be read with the
same pair.** Mixing pairs produces content that is flipped/rotated per face — visually,
sharp diagonal or face-edge-shaped seams.

### Pair A — "axisA/axisB basis" (weather grid, CoordinateConverter forward)

Forward (`Assets/Scripts/Core/Utilities/CoordinateConverter.cs`, `CubeFaceToUnitSphere`):

```csharp
Vector3 localUp = CubeFaceDirections[face];
Vector3 axisA = new Vector3(localUp.y, localUp.z, localUp.x);   // swizzle of the face axis
Vector3 axisB = Vector3.Cross(localUp, axisA);
Vector3 pointOnCube = localUp + u * axisA + v * axisB;          // u,v in [-1,1]
return pointOnCube.normalized;
```

Exact inverses of Pair A:

- HLSL: `CubeFaceUv` in `WeatherCubeFace.hlsl` (used by `WeatherSampling.hlsl`, so by
  Cloud.shader, CloudShadows.hlsl, Precipitation.shader, WeatherParticles.shader).
- C#: `SphericalWeatherGrid.UnitSphereToWeatherCubeFace`
  (`Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs`) and
  `CoordinateConverter.UnitSphereToCubeFaceUvExact`.

The inverse math: `u = dot(dir, axisA) / |dot(dir, localUp)|`, then `uv = u*0.5+0.5`.

### Pair B — legacy `CoordinateConverter.UnitSphereToCubeFace`

An **older UV orientation that is NOT the inverse of Pair A's forward.** It still exists
because some consumers (biome atlases, diagnostic grids) were baked with it. The code
warns about this explicitly — `SphericalWeatherGrid.cs`:

```csharp
// Inverse of CubeFaceToUnitSphere above. Do not use CoordinateConverter.UnitSphereToCubeFace
// here: its UV orientation is not the inverse of this weather grid's face axes.
```

and `CoordinateConverter.cs`:

```csharp
// Exact inverse of CubeFaceToUnitSphere. UnitSphereToCubeFace uses an older UV
// orientation; biome atlases and diagnostic grids need this basis.
```

### Pair C — grass/terrain compute convention (explicit per-face component formulas)

Forward (`FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere`,
`Assets/Scripts/Planet/Grass/FaceSpaceCellRangeBuilder.cs` — the comment says it is the
"C# mirror of CubeFaceToUnitSphere in BiomeGrassPlace.compute / GrassNearFieldPlace.compute"):

```csharp
case 0:  p = new Vector3(u, 1f, -v); break;     // +Y
case 1:  p = new Vector3(-u, -1f, -v); break;   // -Y
case 2:  p = new Vector3(-1f, -v, -u); break;   // -X
case 3:  p = new Vector3(1f, -v, u); break;     // +X
case 4:  p = new Vector3(-v, u, 1f); break;     // +Z
default: p = new Vector3(-v, -u, -1f); break;   // -Z
```

Inverse: `FaceSpaceCellRangeBuilder.DirectionToFaceUv` (same file), mirroring the HLSL
`DirectionToFaceUv` in the grass computes.

### The rule, and the incident behind it

**The historical cloud seam** (pre-2026-07, fixed; recorded in
`.agent-memory/codex/memory_summary.md`): sharp diagonal, cube-face-shaped seams in the
cloud layer. The winning diagnostic route was the `CloudWeather` debug view *first* —
which proved the seam was already present in the sampled weather field, not in cloud
lighting. Root cause: cube-face UV orientation was not aligned across weather
generation, shader sampling, cloud shadows, and the CPU weather query path. The fix was
alignment (one matched pair everywhere in that chain), **not** any lighting change.

Operational rules that came out of it:

1. Before writing face-space code, identify which pair the target dataset uses. Weather
   textures → Pair A. Grass/surface atlases + placement cells → Pair C. Legacy biome
   atlas consumers → Pair B.
2. If you see per-face flipped/rotated content or diagonal seams, suspect a convention
   mismatch before anything else, and prove it at the data layer (weather/biome debug
   views) before touching rendering. (Triage flow: `pp-debugging-playbook`.)
3. New inverse implementations must be validated as *exact* inverses of the forward they
   pair with — round-trip a grid of directions.

## Area distortion and the two corrections in use

Gnomonic projection is not equal-area: a fixed-size UV cell near a face edge/corner
covers less sphere area than at face center (up to ~5.2x less at corners: the area
element scales as `(1 + u² + v²)^(-3/2)` with u,v in [-1,1]).

**Correction 1 — probabilistic keep (grass placement density).**
`GrassNearFieldPlace.compute`, `CubeFaceAreaKeep`:

```hlsl
// Fixed-size face-UV cells cover less world-space area near cube-face edges.
// Keep probability matches the cube-to-sphere area ratio relative to face center.
float2 signedUv = uv * 2.0 - 1.0;
float denom = max(1.0 + dot(signedUv, signedUv), 1e-6);
return saturate(rsqrt(denom * denom * denom));   // (1+u²+v²)^(-3/2)
```

Without this, blade density would visibly *increase* toward face edges (same cell count,
less area). Rejections are counted in `NF_STAT_FACE_AREA_REJECTED`.

**Correction 2 — metric measurement (converting meters to UV).**
`FaceSpaceCellRangeBuilder.ComputeMetersPerUV` finite-differences the forward mapping at
the camera's face UV to get local meters-per-UV, and conservatively takes the **min** of
the u and v scales ("never sparser than spec near distortion zones"). Used to size the
near-field placement disc in UV space.

## Crossing face edges (the grass seam machinery)

A camera-centered disc of grass can straddle a face edge. Handling lives in
`Assets/Scripts/Planet/Grass/FaceSpaceCellRangeBuilder.cs` +
`Assets/Scripts/Core/Utilities/CubeFaceTopology.cs`:

- `BuildRanges` returns 1–5 `FaceSpaceCell` ranges: `[0]` = primary face square around
  the camera projection; one extra tight rectangle per overflowed edge, mirrored onto
  the neighbor face via `CubeFaceTopology.TryMirrorUv`.
- Ranges are snapped to `pageCellSize` pages so sub-cell camera motion doesn't trigger
  re-dispatch, and `cellUvWidth` is **fixed at controller construction** so the same
  (face, cellU, cellV) always maps to the same world position — the foundation of
  stable per-blade hashing (see [grass.md](grass.md)).
- **Corner straddle (3 faces meeting) is intentionally NOT covered**; it is surfaced as
  `FaceSpaceRangeResult.UncoveredCornerStraddle` ("SeamRisk") and the algorithm proceeds.
  As of 2026-07-06 this is a known accepted gap, not a bug to silently "fix".
- Each range gets a proportional emission quota (`_NearFieldRangeBudget` +
  `_NearFieldRangeCounts`) so a big primary-face dispatch cannot starve a narrow
  neighbor strip of instance-buffer slots.

The weather grid, by contrast, needs no explicit seam handling on the read side: it is
sampled per-direction (`CubeFaceUv` picks the face per sample) with `Clamp` wrap and
bilinear filtering, so the worst case at an edge is a half-texel blend discontinuity at
weather-cell scale (32–512 texels per face — coarse by design). A related, still-open
cousin: the *biome top-K blend* chunk-boundary seams (kernel can't see across chunk
borders) — that is terrain-side, tracked in `.agent-memory/claude/project_chunk_biome_seam.md`.

## The weather cube grid concretely

`SphericalWeatherGrid` (`Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs`):

- Storage: `Tex2DArray`, 6 slices, `ARGBHalf`, resolution = closest power of two of the
  setting clamped to [32, 512]. Two ping-pong pairs (weather + dynamics).
- CPU mirror arrays are filled progressively via async GPU readback
  (`ApplyWeatherFaceReadback` / `ApplyDynamicsFaceReadback`, one face at a time).
- `GetCell` converts a world position to (face, x, y) via Pair A's inverse, after an
  optional `sampleRotation` — the same role `_CloudWeatherRotation` plays in HLSL
  (`SampleWeather` in `WeatherSampling.hlsl` rotates the direction before `CubeFaceUv`).
  Channel meanings and evolution → `pp-weather-sim-reference`.

## Provenance and maintenance

```
grep -n "Face layout" Assets/Graphics/Shaders/Includes/WeatherCubeFace.hlsl
grep -n "Do not use CoordinateConverter" Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs
grep -n "older UV" Assets/Scripts/Core/Utilities/CoordinateConverter.cs
grep -n "CubeFaceAreaKeep" Assets/Resources/GrassNearFieldPlace.compute
grep -n "UncoveredCornerStraddle" Assets/Scripts/Planet/Grass/FaceSpaceCellRangeBuilder.cs
```

Seam-incident history restated from `.agent-memory/codex/memory_summary.md` (additional
background only; the facts above stand on the code).
