# Water Build Plan — v5

**Status:** DRAFT v5. Review closed after four rounds. Ready for implementation decisions.
**Current next action:** Bryan — tree cleanup (§9), then W1.
**Tree:** `harvest-vertical-slice`, dirty on `f58a150`.

> **Framing.** Goal-driven. Prerequisites are tasks, never reasons to drop a capability. Tasks are
> numbered in dependency order.
>
> **Measurement rule — this plan has failed it four times.** v1 cited its own same-session fixes as
> live defects. v2 described deleted C#. v3 took a quad size from a stale code comment. v4 used a
> nominal spacing as a mesh-wide mean. **Every number below states where it came from. Re-measure at
> the moment of building, not at the moment of writing** — that is what §8's exit checks are for.

Research: [docs/research/2026-08-16-water-tech-research.md](../research/2026-08-16-water-tech-research.md).

---

## 1. Goal

**Bodies:** oceans, lakes (including raised), rivers, waterfalls.
**Motion:** waves, wind ripples, interaction ripples, rain ripples, splashes.
**Physics:** buoyancy, swimming.
**Rendering:** caustics, reflections, distortion, foam, whitecaps, edge detection, shadows, sun glint.

## 2. The structural finding

**A raised lake has no representation today, and cannot get one by aggregation.**

`WaterMeshBuilder.cs:413` — `bool isWet = elevations[i] < settings.OceanLevel;`

Wet membership *is* the global threshold. A basin above sea level contains **zero wet vertices**, so
there is nothing to aggregate and no statistic that recovers it. Earlier revisions of this plan said
per-body level was "not derivable at all" (v3, too strong) and then "one more accumulator" (v4, wrong
in the other direction). Both missed that **membership must be created before any per-body value
exists.** That is a basin/spill solver, and it is W5.

Everything else in the plan is downstream of getting that right.

## 3. Capability matrix

| Capability | State | Owner |
|---|---|---|
| Ocean surface | Exists | — |
| Lake surface | Has 5 per-body calibrations; **no own level**, no own depth ramp | W5, W15 |
| Raised lake | **Not representable** — see §2 | W5 |
| River / waterfall | None | W13 / W14 |
| Wind waves | Exists | — |
| Interaction + rain ripples | None | W10 |
| Splashes | None | W11 |
| Buoyancy / swimming | None | W7, W8 / W9 |
| Caustics | Exists, ocean-radius keyed | W15, W20 |
| Sky reflection | Exists | — |
| Environment reflection | None working | W16 |
| Distortion, foam, sun glint, cloud shadows | Exist | — |
| Whitecaps | Exist. Zero below a wind threshold that differs per body type | W15 |
| Edge detection | Depth read in the **volume** already; missing surface-side intersection | W17 |
| Real/URP shadows | None | W18 |
| Underwater | Minimal; lip pass deleted 2026-08-17 | W19 |
| Body-aware volume/atmosphere | None | **W20** |

## 4. Tasks

**W1 Settings ownership.** `WaterSettings` SO → `WaterDto` via `IWorldSettingsRegistrar`. ~38 constants
across three files (`PlanetWaterSurface.cs`, `PlanetConstants.cs`, `WaterVolumeRenderFeature.cs`). The
five caustics constants on the render feature are rewritten onto the volume material every
`AddRenderPasses` — they must move or W15 cannot land. `PlanetRecipe` needs a parallel `ToWaterDto()`.
Render feature must use `TryGetFrozen` (it runs for scene-view cameras with no world).

**W2 `water.*` console.** `MonoTargetType.Registry`; public re-apply hook (`UpdateWaterMaterial` is
private, called only from generation). **Must document live vs baked** — `_DeepDepth` is both, and
setting it live rescales depth reads against a mesh normalized to the old value.

**W3 Body authority + world catalog.** — **DONE 2026-08-18.** `LakeMask` → `WaterBodyMap`
(`Assets/Scripts/Planet/Biomes/WaterBodyMap.cs`). Flood-fill is now one whole-sphere pass across cube
seams instead of six independent per-face passes; every connected body gets a `ushort` id and a record in
the versioned `WaterBodyCatalog` (`Id`, `Kind`, `CellCount`, `CenterDirection`, `MinElevation`,
`SurfaceElevation`). `Sample(direction)` keeps its old `None/Water/Shore` contract so the three consumers
(`BiomeMapBaker`, `ColorGenerator`, `WaterMeshBuilder`) are a rename only; `SampleBodyId` / `TrySampleBody`
are the new surface. D11 fixed — `Planet` nulls `WaterBodyMap.Current` before each build, so a cancelled
generate cannot leak the previous world.

*Seam crossing:* no adjacency table. An out-of-range neighbour is projected one cell past the face edge in
face-uv and re-classified through the same `CubeFaceToUnitSphere`/`DirectionToFaceUv` pair `Sample()` uses,
so there is nothing to drift out of sync. `SeamAsymmetryCount` verifies the 1:1 assumption every build and
**measured 0** on the test world.

*The claimed ordering blocker does not exist.* `BakeChunkMap` is called from inside
`ChunkedSurfaceProvider.GenerateColorsAsync` (:1397/:1472), which `Planet.GenerateAsync` runs at :422 —
after `WaterBodyMap.Current` is assigned at :417. The bake already sees the mask; no phase hoist needed.

*Measured on the test world:* 10 bodies — 3 ocean (101432 / 9796 / 2404 cells), 7 lake (325 / 100 / 23 /
21 / 14 / 5 / 2 cells), seam asymmetry 0, catalog v1. Note bodies #2 and #4 are **inland seas** distinct
from the main ocean, classified `Ocean` only because they exceed `LakeMaxCells`; they are the concrete
case W5 must give an independent spill level.

*Behaviour change, not measured against the old build:* `LakeMaxCells = 1400` now bounds the whole body
rather than its per-face slice, so a lake straddling a seam that previously split into two sub-threshold
halves (two lakes) is now one body judged at full size (possibly ocean). This is the intended correction,
but no before/after lake count was captured — if lakes look sparser than expected, that constant is the
one knob.

**W4 Data contract — DONE 2026-08-18.** Written up as
[2026-08-18-water-data-contract.md](2026-08-18-water-data-contract.md): all three carriers (mesh vertex
colour, `_WaterVolumeData`, `_WaterInterfaceTexture`), every field with range/units/producer, every
consumer site, the duplication status table, and the globals list.

**D6 closed.** `Ocean.shader:433` restated `EvaluateSwellGating`'s exact thresholds
(`smoothstep(0.003, 0.035, depth01)` / `smoothstep(0.005, 0.040, shore01)`) with a comment asking that
they be kept in sync; it now calls the function. The pair at `Ocean.shader:357` is a *different*,
deliberately narrower gate for the fragment detail layer and was correctly left alone.

**D7 closed at two of three copies.** One HLSL implementation (source of truth) plus one CPU mirror in
`WaterMeshBuilder`. The mirror is unavoidable — the mesh build decides ice coverage per body on a worker
thread before any shader runs — so both sites now carry a comment naming the other.

**D5 documented and guarded, deliberately not re-encoded.** `shoreBody = shore01 * 0.45 + body01 * 0.55`
only decodes unambiguously while `body01` is near 0 or 1. **Measured on 481,682 water vertices: 99.43%
exactly 1, 0.55% exactly 0, 98 vertices (0.02%) intermediate — all in [0.35, 0.40).** The volume's own
decode already thresholds at `smoothstep(0.45, 0.55)`, so it wants a class, not a gradient.

Re-encoding would touch the volume composite (the most expensive and most fragile pass, measured as the
entire GPU water cost) and drag in `Atmosphere.shader`, whose `step(0.0001, forwardDepth)` validity test
forbids signing channel R. And the right encoding depends on what **W5** needs channel B to carry once
bodies have independent levels and ids — designing it now is designing it blind. Instead
`BuildStats.AmbiguousBodyVertices` counts the breach and `PlanetWaterSurface` warns past 0.5%, because
the symptom is a wrong body *tint* that nothing else would notice. **W5 owns the re-encode.**

**W4a shared displacement include — DONE 2026-08-18 (D9, surface half).**
`Assets/Graphics/Shaders/Includes/WaterDisplacement.hlsl` now owns `SafeNormalize`, `SafeNormalize2`,
`BuildPlanetWaveAxes`, `EvaluateSwellGating`, `EvaluateSurfaceWave`, `EvaluateFreezeFactor`,
`ComputeOceanSwell`, and the `ComputeWaterVertexDisplacement` entry point. `Ocean.shader` and
`WaterVolumePrepass.shader` both include it and both call the same entry point, so **the prepass now
rasterises the surface the player sees** instead of the undisplaced shell. `EvaluateFreezeFactor` had been
copy-pasted into both shaders; there is now one copy.

*Why this needed globals.* Two materials must agree exactly, so `_SwellAmplitude`, `_SwellWavelength`,
`_WaveSpeed` and the five freeze parameters moved from `UnityPerMaterial` to shader globals, named in
`ShaderGlobalIds.Water.cs` and published by `PlanetWaterSurface` from `WaterDto`. **The properties had to
be deleted from both `Properties` blocks — a material property of the same name shadows the global** and
would have silently restored the desync. This also deletes `WaterVolumeRenderFeature.CopyFloatIfPresent`
and its five freeze copies: material-to-material copying was the workaround these globals replace.

*Behaviour of an unset global:* zero. `_SwellAmplitude = 0` is flat water and `_FreezingEnabled = 0` is
liquid, so a domain reload before the first generate degrades to calm liquid water rather than an
artefact. `UpdateWaterMaterial` publishes them on every generate.

*Still open in D9:* `WaterVolume.shader` continues to intersect an analytic `_SeaLevelRadius` sphere
(:121-183). That is the third surface and it is unfixed — it needs the volume to read the prepass surface
rather than re-deriving one. Owned by W18/W20.

**W5 Basin/spill solver + per-body level.** *Not an accumulator.* Requires: a level source (spill solve,
authored, or explicit priority), membership built **from that level** rather than from the global wet
predicate, a stable `WaterBodyId`/level/type/bounds/membership in the catalog, and defined behaviour for
nested basins, merged spillways, dry basins, rivers and waterfalls.

**Level source decided 2026-08-19 (Bryan): spill solve.** Authored was rejected because bodies are
discovered per seed and our ids are assignment-order, so there is nothing stable to author against — it
survives only as an override slot. Rule-based (min + fixed depth) was rejected because it is not physical
and yields no spill point, and the spill point is what gives W13 an anchor for where a river leaves a lake.

*Algorithm:* priority-flood. Seed a min-heap with the ocean cells, pop lowest first, and give each
neighbour `filled = max(ownElevation, poppedFilled)`. Every cell ends with the surface height of the basin
containing it; `filled > own` marks submerged cells and the basin's fill level is that `filled` value.
Nested basins and merged spillways fall out of the ordering rather than needing cases. Runs on the grid
`WaterBodyMap` already samples — 221,184 cells, O(n log n), on the existing background thread.

*Resolution:* 221,184 cells over a 5000 m sphere is ~1,420 m² per cell, **≈38 m per side**. Lake #8
(325 cells ≈ 0.55 km²) has its rim resolved at 38 m. Lake #3 (2 cells) is at the noise floor.

*Known cases to handle:* procedural noise makes thousands of 1-3 cell dimples, so a minimum basin area
and minimum fill depth are required or the planet grows puddles everywhere. Dry (endorheic) basins are
the open question — priority-flood fills every depression to its rim whether or not water would reach it;
the climate provider's moisture field is the obvious gate and is already available.

*Sequencing:*
- **W5a — DONE 2026-08-19.** `WaterSpillSolver` (priority-flood, own binary min-heap since Unity's runtime
  profile predates `PriorityQueue`). `WaterBodyMap` now precomputes a neighbour table once — the flood fill,
  shore dilation, spill solve and seam check all walk it instead of recomputing the seam projection — and
  every lake's `SurfaceElevation` is its real spill height. No mesh change, so no visual change.

  **Measured on the reference world.** Oceans hold level `0.00000` exactly, confirming the seeding. Lakes:

  | Body | Cells | Level | Rise above sea | Depth |
  |---|---|---|---|---|
  | #5 | 100 | 0.01937 | **+96.9 m** | 118.6 m |
  | #3 | 2 | 0.00858 | +42.9 m | 44.6 m |
  | #7 | 14 | 0.00822 | +41.1 m | 51.3 m |
  | #9, #10 | 23, 21 | 0.00062 | +3.1 m | 9.8, 7.1 m |
  | #8 | 325 | 0.00035 | +1.8 m | 45.8 m |
  | #6 | 5 | 0.00008 | +0.4 m | 3.4 m |

  **The headline result is the basin count, not the levels.** The solve puts 121,640 cells underwater
  against 114,122 the global wet predicate currently draws — only 6.6% more water by area — but that water
  is spread across **322 connected basins where the current predicate finds 10 bodies**. Three of the 322
  are the oceans (101429 / 9796 / 2404 cells); the rest are lakes the single global level cannot represent.
  So the planet has on the order of a hundred mountain lakes waiting to appear, individually small.

  **The micro-basin fear was overblown.** Procedural noise did not produce thousands of dimples: only 94 of
  the 322 basins are single-cell, together 94 cells. Size distribution after the three oceans: 1378, 573,
  513, 345, 192, 174, 172, 171, 151, … A minimum-area threshold of **8–16 cells** (≈11,000–23,000 m²) leaves
  139 or 92 lakes respectively and is the knob W5b should expose.

  `water.bodies` reports level, rise, depth, and the basin histogram.
- **W5b** mesh honours per-body level. `WaterMeshBuilder.cs:131` computes **one** scalar
  `waterRadius = PlanetRadius * (1 + OceanLevel) + SurfaceOffset` and every vertex is
  `direction * waterRadius`; that becomes a per-body lookup. The same file uses `OceanLevel` at six sites
  (radius, shoreline interpolation `t`, depth, `isWet`, surface-edit evaluate). This is where raised lakes
  first become visible.
- **W5c** consumers, including D10.

*Scoping correction to the "~60 sites" figure below:* `SeaLevelRadius`/`SeaRadiusLocal` appear 57 times
across 24 C# files and 7 shaders, but they split in two. **Datum** consumers (atmosphere base radius,
cloud base, stars, scale markers, camera altitude) want the planet's nominal sea level and stay scalar
permanently. Only **body-surface** consumers (water mesh, scatter altitude gating, biome classification,
character grounding and swim) need "the level of the body here". Classifying the 57 is the first step of
W5c and contains no design choice.
*Consumers:* ~60 sites across seven subsystems. Four sea-level authorities exist, one of which
(`BiomeConstants.OceanThreshold`, hardcoded `0f`) ignores `OceanLevel` entirely — D10.
Parity-locked duplicates: biome ×2, scatter ×2 (with a parity test), grass ×3.
Contract changes: `PlanetGeneratedEvent.SeaLevelRadius` is a scalar with ~8 subscribers;
`ScatterTileCache` bakes sea radius into cached tiles with no invalidation path; `WaterMeshAnalysis`
asserts the single-radius invariant. Rename — `SurfaceRadius` already means terrain ground radius.

**W6 Water query service.** `TryGetWaterSurface(pos) → surfacePoint, signedDepth, normal, flow, bodyId`.
Two-phase (register at world services, configure post-generation), Burst job view, released on teardown.
*Ship a versioned interaction extension point* so W10 can extend rather than replace (B3).
*Why analytic mirroring fails:* the swell is gated by per-vertex `depth01`/`shore01`/`body01` and zeroed
by `EvaluateFreezeFactor` — near shore and on frozen bodies an analytic mirror is wrong by **100% of
amplitude**. Sampling error is secondary: **mesh-wide mean edge 18.743 m** (measured by generating every
385² direction and averaging chords — *not* the 20.453 m nominal builder spacing, and not v3's 30.8 m),
face-centre 26.042 m. Mode response 79.3% / 57.2% / 14.8% at the mean; **1.06 m typical-wind error,
1.86 m at face centres, 3.39 m at full wind** against a 2.86 m peak.
*Implementation:* triangle interpolation is **exact** — single pass, no tessellation, fragment never
moves the surface. Retained face grid (13.570 MiB) gives O(1) cell lookup but carries only directions
and elevations; the missing artefact is the discarded `originalVertexCache` (3.375 MiB). **4.65% of mesh
vertices are shoreline clip vertices with no grid counterpart** — where wading and swim-entry happen.
*Verify D12 first* — `ChunkedSurfaceProvider` uses the "older" face-UV inverse while the grid uses the
exact one.

**W7 Physics host.** First fixed-step host and first streamed terrain colliders in the project. The
character destroys its own capsule (`PlanetCharacterController.cs:295-300`); terrain chunks have no
collider owner. (Scenes *do* contain colliders — the earlier "zero project-wide" claim was wrong.)
`SurfaceCharacterController` assigns pose every tick, so a `Rigidbody` needs a **velocity-carrying
non-grounded driver**. Ordering via the init graph, never `[DefaultExecutionOrder]`.

**W8 Buoyancy.** Per-triangle submerged-area integration. Clipper, drag, skin friction, slamming and
`MassFromVolume` are all coordinate-neutral and port unchanged — verified. **Requires a line-level
migration ledger** (each site marked unchanged / adapted / replaced) before implementation; earlier
site counts were not auditable. Traps: radial gravity makes `Physics.gravity` zero → cached up
degenerates → **buoyant force silently zero, no error**; the `_gravity.y` sign cancels against an
accumulator; the cached up is per-object resolved once in `Awake`. The reference provider contract is
`ref float[] waterHeights`, incompatible with W6's signature.

**W9 Swimming.** Non-grounded driver state, entry/exit transitions, swim camera, and removal of the
sea-level floor at three sites. Those clamps are the only thing keeping the character on the surface today.

**W10 Interaction buffer.** Camera-following RT (displacement/foam/normal), CPU-queryable per B3 — body-
local impulses on the CPU, active window uploaded to the RT, so W6 and the shader read one authority.
Delivers interaction ripples, rain ripples, wakes, foam persistence. Must state RT-edge behaviour.

**W11 Splash emitters.** Impact events + VFX + pooling owner + quality tier + planet-lit shading.

**W12 Multi-mesh water pipeline.** `WaterRendererRegistry`; the render feature currently does
`GameObject.Find("Water")` and draws exactly one mesh. Without this a river gets no volume, no caustics,
and never enters `_WaterInterfaceTexture`.

**W13 Rivers.** Uphill-from-coast polylines reversed — no cube-face seam. Own ribbon mesher with a
rotation-minimising frame. Suppression is **four parity-locked implementations** all gating on a single
global radius scalar; a river needs a spatial mask.

**W14 Waterfalls.** Slope segments off the same polyline; own shader and renderer registration.

**W15 Per-body calibration.** Depth ramp (D1, D2) and whitecap threshold **per body type** (D3).
*D2 correction:* the shoreline stamp is `0.00165 × PlanetRadius`, **not** a fixed 8.25 m — test at
multiple radii. Blocked on W1 moving the caustics constants.

**W16 Environment reflection.** Per-body planar for lakes, analytic sky for ocean. Remove the orphan and
its shader residue first (D8). *Correction:* the reflection-camera early-outs are **recursion
protection**, not a blocker — W16 must instead define a policy for water reflected inside other water.

**W17 Edge detection.** Surface-side intersection foam, computed **once and shared** with the volume —
the surface writes no depth and the composite already ran, so two independent derivations would produce
two shorelines, which is the exact scar this targets.

**W18 Real shadows.** `multi_compile` + shadow sampling; a `ShadowCaster` pass that **reuses the shared
displacement include** (W4) so shadows match the waves; flip `shadowCastingMode` off `Off`. Every
registered water renderer must cast.

**W19 Underwater.** Unify the drifted `CameraUnderwater01` pair, both keyed off the global radius.
Urgent once W9 lands.

**W20 Body-aware volume + atmosphere.** *(added by review — I had wrongly declared this impossible)*
Resolve the visible `WaterBodyId` and interface point from the multi-mesh prepass; replace analytic-
radius depth where mesh coverage exists; define no-depth and off-screen fallbacks per body type; key
underwater, atmosphere, refraction and caustics from the same resolved body; set the optical-LUT policy
when a body cannot use the ocean radius. Expensive, but the interface prepass is the entry point — and
W12 and W19 already require it.

## 5. Order

```
W1 → W2 → W3 → W4/W5 → W6
W4/W5 → W12 → W20
W6 → W7 → W8
W6 + W7 + W20 → W9 → W19
W6 → W10 → W11
W12 + W6 → W13 → W14
W4/W5 + W20 → W15
W5 + W12 → W16
W4 + W12 + W20 → W17
W4 + W10 + W12 + W13 + W14 → W18
```

Three conditional co-dependencies to resolve before implementation:
**W3/W5** (separate only if W3 ships the versioned catalog) · **W4/W5** (fold or version) ·
**W6/W10** (versioned extension point, or co-deliver).

## 6. Ownership

Every task below is a world service or owns world data: register via `IWorldServiceRegistrar`, resolve
once at init, **discard on `WorldReadyEvent`**. An app-scope render feature may not retain a renderer
from the previous world — the current one does, via `GameObject.Find("Water")`.

| Task | Owner | Teardown |
|---|---|---|
| W3 | `WaterBodyCatalogBuilder` + world `WaterBodyCatalog` | Release arrays; clear `LakeMask.Current` |
| W4 | `WaterDataContract.cs` + one shared HLSL include | — |
| W5 | Catalog + basin/authored-level solver | Release membership and level data |
| W6 | `IWaterQueryService` + job view | Release grid, index cache, clip data |
| W7 | `PlanetPhysicsHost` in the init graph | Destroy collider bubble, fixed-step registrations |
| W8 | `BuoyancySystem` + registered bodies | Unregister bodies, release buffers |
| W9 | Swim state owner + camera owner | Remove subscriptions, camera override |
| W10 | `WaterInteractionService` | Release RT, clear benders, reset globals |
| W11 | `WaterSplashService` | Unsubscribe, destroy VFX pool |
| W12 | World `WaterRendererRegistry` + app-scope bridge | Clear on world change |
| W13/W14 | `RiverSystem` / `WaterfallSystem` | Destroy runtime meshes and materials, unregister |
| W15 | `WaterBodyProfile` on the catalog | Reapply runtime materials only |
| W16 | `WaterReflectionService` | Release camera, RT, globals |
| W17 | Shared intersection producer | Release RT, reset global |
| W19 | `UnderwaterStateService` | Remove listeners, clear current body |
| W20 | Body-aware volume resolver | Release body buffers, clear globals |

**Applies to every task:** new shader globals enter `ShaderGlobalIds.Water.cs` **before** any reader or
writer. Settings go SO → DTO; runtime never retains a `ScriptableObject`. Runtime materials are cloned;
authored assets are never mutated. No coroutines, `async void`, `Task.Run`, `[DefaultExecutionOrder]`,
or new `RuntimeInitializeOnLoadMethod`. Split large owners before adding responsibility —
`WaterMeshBuilder` and `WaterVolumeRenderFeature` both need extraction before W3/W5/W6 and W12/W20.

## 7. Defects

| # | Defect | Owner |
|---|---|---|
| D1 | Depth ramp global — lake has no gradient | W15 |
| D2 | Shoreline stamp `0.00165 × PlanetRadius` (**not** fixed 8.25 m) | W15 |
| D3 | Whitecaps zero below a wind threshold that differs per body type | W15 |
| D4 | Two body authorities — thresholds **and** per-face vs cross-face | **CLOSED** 2026-08-18 (W3) |
| D5 | Prepass packing ambiguous at **every** intermediate value, not just 0.5 | W4 documented + guarded — 0.02% exposure measured; re-encode owned by **W5** |
| D6 | Swell gating recomputed at three sites | **CLOSED** 2026-08-18 (W4) |
| D7 | `EvaluateFreezeFactor` triplicated incl. C# | **CLOSED** 2026-08-18 — 3 copies → 1 HLSL + 1 unavoidable CPU mirror |
| D8 | Orphan reflection: **inert unowned writer** + live shader residue | **CLOSED** 2026-08-18 |
| D9 | Prepass doesn't displace; volume uses analytic radius — three surfaces disagree | Surface half **CLOSED** 2026-08-18 (W4a); volume half open, W18/W20 |
| D10 | `BiomeConstants.OceanThreshold` hardcoded `0f`, ignores `OceanLevel` | W5 |
| D11 | `LakeMask.Current` static never cleared | **CLOSED** 2026-08-18 (W3) |
| D12 | `ChunkedSurfaceProvider` uses the older face-UV inverse | W6 (verify first) |

## 8. Verification

**Fixed F10 fixture for every visual comparison** — seed `1691104419`, `QualityLevel 0 (PC)`, cloud tier
High, camera `(4514.47, -2273.93, -644.70)`, forward `(0.0608, 0.8789, 0.4731)`, up
`(0.9979, -0.0650, -0.0076)`, frozen sun `(0.8876, -0.4224, -0.1836)`, saved `960x343`. Add the seed to
every new sidecar. Archive the baseline before each task.

**Numeric checks can reject a task. They cannot approve a visual one — Bryan's sign-off does.**

Representative pass conditions (full set in the Codex review section):

- **W5** — fixture with ocean plus two basins at different spill levels, one crossing a cube seam.
  Regeneration preserves all body IDs and levels exactly; each seam basin has **one** ID; every retained
  surface vertex within 0.10 m of its body level before waves.
- **W6** — `water.query-check <N>` from `LateUpdate` after `_GameTime` publishes. p95 ≤ 0.10 m, max
  ≤ 0.25 m; two calls in one frame **bit-identical**; loud failure if the `_GameTime` read differs from
  the frame's draw; shore-adjacent error (`shore01 < 0.05`) **reported separately**.
- **W8** — closed 1 m³ mesh at density 1000: force opposite gravity, within 2% of `ρVg`, at three
  latitudes. **Zero gravity must raise a named diagnostic, not return silent zero.**
- **W15** — force legacy values first and require byte-identical baselines, then per-body thresholds.
- **W20** — every body type reports consistent underwater state across query, atmosphere and volume.

## 9. Immediate work before W1

1. ~~**Remove the reflection orphan as a unit**~~ — **DONE 2026-08-18.** `LakePlanarReflection.cs`, its
   csproj entry, the `Ocean.shader` sampling branch and texture/sampler/params declarations, and both
   `ShaderGlobalIds.Water.cs` globals. `grep -r LakeReflection Assets/` returns nothing; shader reimports
   with 0 messages. Sky reflection (`EvaluateSkyReflection`) is now the only reflection path (D8).
2. **Review the six visual constants landed 2026-08-17** — two fixed measured bugs (lake wind gate,
   wave feature scale); four are unreviewed aesthetic choices (reflection ceilings, sky palette, wave
   steepness, `dataContinuity`). Needs Bryan's eyes. The float ones are now live-tunable via `water.set`;
   colours via `water.setcolor`. The sky palette is still hardcoded in `Ocean.shader`, not in the DTO.
3. ~~**Commit or revert**~~ — revert declined by Bryan (nothing to be lost). Commit still owed.

**Independent, any time:** `Lake Cattails.asset` `[90, 900]` LOD distances (sets the eviction radius for
every prototype) · scatter impostor horizon cull, as its own design note.

## 10. Provenance

v5, 2026-08-18, against `f58a150`. Four review rounds: three parallel agent passes on v1 and v3, Codex
on v2 and v4. Review is closed — the architecture verdict stabilised at *feasible, needs W20, W5 is a
solver*. Remaining risk is measurement drift, which the §8 exit checks exist to catch at build time.

---

# Appendix - review history (v1/v2 and reviews)

Retained for the defect findings and the record of what was got wrong. Scoping recommendations in this appendix are SUPERSEDED by the v3 body above.

# Water Architecture Plan — 2026-08-17 (v2, post-review)

**Status:** DRAFT v2. Rewritten after three adversarial agent reviews dismantled v1. Not approved.
**Current next action:** Bryan reads §11 (Claude response to Codex) and its revised phase list, then
decides the §8 questions as amended there.
**Branch:** `harvest-vertical-slice`, working tree dirty on **`f58a150`** (`cd4e0f0` is an ancestor —
the tree advanced during review; §11 re-verified all volatile claims against `f58a150`).

> **Reading order:** §0 (v1 errors) → §3 (live defects) → §10 (Codex) → §11 (response + revised phases).
> §5's phase list is **superseded** by §11.

- [ ] P0 — Impostor horizon cull *(relocated; see §0)*
- [ ] P1 — Depth-ramp per body via build-time bake *(replaces v1's mesh-format phase)*
- [ ] P2 — Shoreline depth stamp fix
- [ ] P3 — Single body-classification authority
- [ ] P4 — Volume packing straddle fix
- [ ] P5 — Reflection: work the ranked cause list
- [ ] P6 — Drift cleanup (`EvaluateSwellGating`, `SceneDepthValid`, `EvaluateFreezeFactor`)
- [ ] P7 — CPU water query *(re-scoped; much larger than v1 claimed)*
- [ ] P8 — Interaction buffer
- [ ] P9 — Rivers *(re-scoped; v1's mesh plan was impossible)*

Research source: [docs/research/2026-08-16-water-tech-research.md](../research/2026-08-16-water-tech-research.md).

---

## 0. What v1 got wrong

Three reviewers audited v1. Every item below is verified against the tree. This section exists so
Codex can see what was already caught rather than re-finding it.

| v1 claim | Reality |
|---|---|
| §3.1: lake wind gate `smoothstep(0.34, 0.88)` is a live defect | **Already fixed by me, this session.** `Ocean.shader:432` is `smoothstep(0.02, 0.55, …)`. v1 cited its own fix as evidence. |
| §3.1: `_WaveScale` 480 m on a 350 m pond is a live defect | **Already fixed, same session.** `Ocean.shader:442-444` `bodyWaveScale`. |
| §3.1: `_ShallowDepth` 28 m is deeper than the lake floor | **Misread.** Only ever used scaled — `*0.25` = 7 m in Ocean, `*0.08`/`*1.5`/`*0.35` in Volume. The raw value gates nothing. |
| §3.1: "three defects, one root cause" | **Two of the three were already fixed, and both fixes were plain `body01` lerps with no mesh change** — a direct counter-example to "`body01` is inadequate for scale". |
| §3.2: Phase 1 is "retention, not computation" | **False.** `ClassifyWaterBodies` receives no positions and no depths. 1 of 8 record fields is retention. |
| §3.2: `SurfaceRadius` is the unlock for rivers | **Not derivable at all.** Wetness is one global threshold and mesh radius is one global constant, so every body's surface is at the same radius *by construction*. |
| §10: "vertex data measured smooth, max Δdepth01 0.022" | **Sixth wrong diagnosis.** 0.022 *is* `shorelineEdgeDepth / _DeepDepth` = 8.25/360 = 0.0229. I measured a hard-coded constant and read it as proof of absence. |
| P0a: cull in the Burst gather | **Structurally impossible.** The gather has no camera by design, and culling there poisons the tile cache permanently. |
| P0a: impostors reach ~1,125 m, ~8× the horizon | **Understated.** That's the *rock* range. Trees cull at 500 m → **2,250 m**, ~16× the 141 m horizon. |
| P0b: four helpers duplicated Ocean↔WaterVolume | **Wrong pairing on 3 of 4.** Real axes are Ocean↔Prepass and Volume↔Prepass. `WaterVolume.shader` is a fullscreen blit and shares almost nothing with `Ocean.shader`. |
| Phase 2: "byte-exact CPU mirror, three `sin` calls" | **Impossible as stated.** See §5 P7. |
| Phase 4: "one remaining untested hypothesis" | **At least four rank higher.** See §5 P5. |
| Phase 6: ribbon via `BuildCappedTube(…, sides: 2)` | **Impossible.** `TreeTubeMesher.cs:45` is `sides = Mathf.Max(3, sides)`. |
| Phases 1 and 2 are independent | **False** — data, parameters and output all couple them. |

**Lesson recorded:** v1 measured the base commit while describing the dirty tree, and used one of its
own same-session fixes as evidence. Any future plan must state which tree it measured and re-verify
after landing changes in the same session.

---

## 1. Goal

Waves, ripples, splashes, buoyancy, swimming, rivers, waterfalls, oceans, lakes — with caustics,
reflections, distortion, foam, whitecaps, edge detection, shadows, sun glint.

## 2. What exists and is not being rebuilt

Verified in shader source. Radial swell + analytic normals; fragment detail waves; analytic
sea-sphere volume with per-channel absorption; depth-validated refraction; triplanar chromatic
caustics; shore + crest foam with halftone threshold; sun glint; cloud shadows; cross-face body
classification; marching-triangle waterline clip; ~100 debug modes.

**Goal items with no owner** (v1 claimed only reflection):

| Missing | Note |
|---|---|
| Environment reflection | P5. Sky reflection *does* exist (`Ocean.shader:595-606`); v1 wrongly said nothing existed |
| **Edge detection** | `Ocean.shader` has **zero** `_CameraDepthTexture` references. Shoreline is vertex-colour `shore01` only. Research W4/W8/W18 all unscheduled |
| **Real/URP shadows** | No `multi_compile _MAIN_LIGHT_SHADOWS`, no `ShadowCaster`. Terrain and trees cast nothing onto water |
| **Underwater rendering** | Minimal. The lip pass that closed the shoreline gap was deleted this session |
| **Splashes** | No emitter, no impact event, and no surveyed technique. P8 gives only the surface deformation |
| **Swimming** | No phase delivers it. Needs a character collider, which does not exist |
| **Rain ripples** | Research W27 unscheduled |

## 3. Root cause — what survives

**The mechanism survives; the evidence had to be rebuilt.** Ocean-calibrated constants do reach small
bodies, and there is genuinely no per-body scale signal — reviewers tried to derive one from `shore01`
(saturates at 125 m from shore, so useless for any body wider than ~250 m) and from `body01`
(`WaterMeshBuilder.cs:459-462` hard-overrides it to *exactly* 0 for lakes, erasing size information).

Live defects, all re-verified against the dirty tree:

| # | Defect | Location | Measured effect |
|---|---|---|---|
| D1 | Depth ramp is global | `_DeepDepth` 360 m vs lake 0.4–17.7 m | `depthBlend ≈ 0.019` across the entire lake → `waterColor` is a flat constant. **The lake has no depth gradient.** |
| D2 | Shoreline depth stamp | `WaterMeshBuilder.cs:227,351` | Every clip vertex stamped at a constant 8.25 m. On a 17.7 m lake the shore ring reads **deeper than half the interior** — gradient inverted at the water's edge |
| D3 | Whitecaps dead on non-ocean | `Ocean.shader:546-547` | `crestEnergy = wind01*0.56 + openWater01*0.34`; at wind 0.100, lake → `smoothstep` returns **exactly 0** |
| D4 | Two body authorities disagree | `OceanBodyVertexThreshold` vs `LakeMask.LakeMaxCells` | Bodies in the **0.98–2.34 km²** band classified oppositely |
| D5 | Volume packing straddle | `WaterVolumePrepass.shader:83` | `shore01*0.45 + body01*0.55` in one channel; at `body01=0.5` the ranges overlap → **middle renders ocean, margins render pond** |
| D6 | `EvaluateSwellGating` drift | `Ocean.shader:213-224` vs `:426-427` | Comment says "MUST stay in sync"; the two use different constants, so the `WaveEnergy` debug view reports numbers the fragment path never uses |

**Where lake colour actually comes from:** `WaterVolume.shader:799-807` overpaints lake pixels at 97%
with a term driven by screen-space path length that **never reads `depth01`**. D1 is real but its
visible weight is smaller than v1 implied, and D5 is in the path that matters more.

## 4. Architecture position

v1 proposed a `WaterBody` record delivered by a mesh-format change. **That is not justified by any
defect above**, for three reasons:

1. Two v1 defects were fixed this session with plain `body01` lerps — no format change.
2. **Option (d), which v1 omitted: bake the per-body constant into the per-vertex value at build
   time.** This already ships — `WaterMeshBuilder.cs:596` bakes `componentTemperature` into a
   per-vertex value in the existing colour format. Applying it to depth (`depth / componentMaxDepth`)
   fixes D1 with zero shader change, zero UV1, zero `StructuredBuffer`.
3. A UV1 index + `StructuredBuffer` reaches `Ocean.shader` and **dies at the volume** — the
   `RGBA16F` prepass RT has four channels, all spent, one already double-packed (D5).

We also have **N = 1 lake**. That establishes a two-class split, which `body01` already is — not
per-body identity. Per-body identity may still be right for rivers later; it is not justified now.

**Position: fix the defects at their source. Revisit per-body identity when rivers force it, with
more than one lake measured.**

## 5. Phases

### P0 — Impostor horizon cull *(relocated)*

**Not** in the Burst gather. `ScatterGatherJob` takes no camera by design — the payload is a pure
function of `(tile, seed)`, an invariant with its own console assertion (`scatter.tilecheck`).
Culling there also poisons the tile cache permanently: a zero-instance result is still
`MarkReady`-ed and then skipped forever, and eviction is altitude-invariant, so instances never
return when the camera climbs.

**Correct site: `Assets/Resources/ScatterCull.compute`, kernel `CullBand`.** It already runs a
per-instance camera test every frame (`_CamPos`, instance world position) and GPU indirect is the
default path. The horizon test is ~2 lines before the `InterlockedAdd`, re-evaluated per frame, zero
cache interaction.

```
visible ⟺ dot(P − planetCentre, camUp) + objectHeight ≥ R² / r_cam
```

- Trees cull at 500 m → impostors reach **2,250 m** vs a **141 m** horizon at 2 m eye height.
- `objectHeight` source: `ScatterImpostorBaker.AtlasCard.WorldSize` is computed but **not retained**
  on `ScatterLodBatcher.Impostor` — one added field.
- Departure to state: impostors cast shadows; horizon-culling drops those casters.
- Decide: is `R` sea level or local terrain radius? `_lastGeneratedRadius` 5257 vs
  `_lastSeaLevelRadius` 5000 is a 257 m difference.

**Separate finding, larger than the phase:** `Lake Cattails.asset` has `LodEndDistances: [90, 900]`,
giving a 1 m reed a 4,050 m impostor tier — which sets `_globalMaxRadius` ≈ 4,128 m and therefore the
eviction radius and re-plan tile set **for every prototype**. Looks like a `90`/`900` typo.

*Exit check:* capture-diff at 2 m eye height (this is a visual change and needs one, contra v1);
instance count within the horizon unchanged.

### P1 — Depth ramp per body, build-time bake

Replace `Clamp01(depth / deepDepth)` at `WaterMeshBuilder.cs:362` with a per-component maximum,
following the `componentTemperature` precedent already in the file.

*Cost of the approach:* the surface shader loses absolute metres at `Ocean.shader:572-573`. Evaluate
before committing.
*Also touches:* raw `depth01` thresholds are hardcoded at `Ocean.shader:222, 427, 502, 574, 662`.
*Blocked for the volume:* `_DeepDepth` is consumed at ~10 sites in `WaterVolume.shader`; those need
D5 resolved or an extra channel.

*Exit check:* lake shows a real shallow-to-deep gradient; ocean unchanged; capture-diff both.

### P2 — Shoreline depth stamp (D2)
Interpolate the clip vertex's depth instead of stamping `shorelineEdgeDepth`, or scale the stamp per
body. *Exit check:* the shore ring is no longer the deepest reading on a lake.

### P3 — Single body-classification authority (D4)
`LakeMask` currently wins by overwriting the BFS result afterwards. Pick one authority and delete the
other, or make the BFS consume `LakeMask` as an input rather than being overridden.

### P4 — Volume packing straddle (D5)
`shoreBody = shore01*0.45 + body01*0.55` is only decodable for `body01 ∈ {0,1}`. Either enforce that
invariant explicitly or find a channel. This gates any per-body work in the volume.

### P5 — Reflection: ranked cause list *(replaces v1's single hypothesis)*

`LakePlanarReflection` ships inert. v1 claimed one remaining hypothesis; at least four rank higher:

1. **`cullingMatrix` is never set.** A mirrored `worldToCameraMatrix` has negative determinant; URP
   early-returns *with no log* when culling parameters can't be derived. Matches "empty target, no
   error" exactly — and mean luma 0.4 means the RT was never even cleared. *Fix:*
   `cullingMatrix = camera.projectionMatrix * _reflectionCamera.worldToCameraMatrix`. *Diagnostic:*
   log `TryGetCullingParameters(out _)`.
2. **`cameraType` never set**, so all five render features run on the mirror, four of which reassign
   `resourceData.cameraColor`. The project's own working offscreen rig (`ScatterImpostorBaker`) sets
   `CameraType.Preview` precisely to escape this.
3. **`Camera.Render()` is unreachable** — the `SubmitRenderRequest` branch always wins — while
   `ScatterImpostorBaker` calls plain `cam.Render()` in this exact build and ships valid atlases.
4. **Two of my three "eliminations" were contaminated.** Disabling the water renderer sets the
   *global* `_WaterVolumeEnabled = 0`, and `GL.invertCulling` is global; from a `begin*Rendering`
   hook both corrupt the main frame. Those data points are invalid.

**Two further blockers even with a working render:**
- **No sky in the mirror.** The scene has no skybox material; sky is painted by
  `AtmosphereRenderFeature`. A mirrored ground-level camera sits inside the sea-level sphere.
- **`Ocean.shader:702` samples an X-flipped UV.** That is correct for faking a mirror from unmirrored
  camera colour and **wrong** for a real mirrored-camera RT, which must be sampled unflipped.
  Landing the real reflection without changing this double-mirrors it.

Also: grass is structurally excluded (its `RenderParams.camera` is bound to the observer).

*Exit check:* needs a pixel-diff instrument, which **does not exist in the repo** — build one first.

### P6 — Drift cleanup *(replaces v1's P0b)*

Correct pairings:

| Function | Copies |
|---|---|
| `EvaluateFreezeFactor` | `Ocean.shader:288`, `WaterVolumePrepass.shader:65`, **`WaterMeshBuilder.cs:630` (C#)** |
| `SceneDepthValid` | `WaterVolume.shader:97`, `WaterVolumePrepass.shader:45` |
| `Hash22` | `Ocean.shader:261`, `WaterVolume.shader:212` — the one genuine byte-identical pair, unnamed in v1 |
| `EvaluateSwellGating` (D6) | Declared once, silently recomputed with different constants at `:426-427` |

**Highest value is the C# copy of `EvaluateFreezeFactor`** — that's the CPU/GPU pair that must agree
for freeze stats to match rendering, and an HLSL-only extraction leaves it open.

**Do not merge `SurfaceVoronoi` with `CausticVoronoi`** — same algorithm, deliberately different tuned
constants. Merging is a visual change and cannot pass a no-change check.
**CBUFFER hazard:** the freeze uniforms are inside `UnityPerMaterial` in `Ocean.shader` and loose in
the prepass. A shared include must declare the *function only*.

### P7 — CPU water query *(re-scoped — v1 was wrong about the cost)*

The no-horizontal-displacement premise **holds**: three sines plus gradients, no Gerstner inversion.
Everything else about v1's costing was wrong.

- **The mesh under-samples its own waves.** Quads are 30.8 m; swell wavelengths are 90 / 61.2 /
  41.4 m. The third mode is below Nyquist. A crest straddled by two vertices renders at ~47% of
  analytic peak — a **~2.6 m error on a 5 m amplitude**. No analytic CPU function can match the
  *rendered* surface.
- **Inputs are not world-position functions.** The swell needs `depth01`, `shore01`, `body01`,
  `temperature01` — per-vertex mesh colours with no CPU spatial index. The only existing consumer
  linear-scans 481,682 entries.
- **`_GameTime` parity:** read the published global; recomputing `Mathf.Repeat(Time.time, 3600)` in
  `FixedUpdate` gives a different value than the GPU used. (Also: 3600 s is not commensurate with any
  wavelength, so the surface jumps hourly — pre-existing.)
- **Sea-level offset:** the mesh sits 0.15 m above `_SeaLevelRadius`; grounding uses the latter.

**Either** mirror the tessellated surface (much larger than v1's framing) **or** accept a
several-metre tolerance, which is useless for buoyancy. This decision belongs to Bryan.

### P8 — Interaction buffer
Unchanged in substance from v1. Correction: the path-wear/scorch precedent is **per-cube-face baked
textures**, not a camera-following patch — so it argues the *opposite* of what v1 claimed. The sphere
decision stands on its own merits and must state the RT-edge behaviour, which is the retired
camera-following patch's exact failure mode.

### P9 — Rivers *(re-scoped)*

The polyline approach survives — `ShapeGenerator.SampleElevation` is analytic over unit directions, so
uphill-from-coast has no cube-face seam. **The mesh plan does not:**

- `BuildCappedTube(…, sides: 2)` clamps to 3 (`TreeTubeMesher.cs:45`) — a triangular tube with cone
  caps, not a ribbon.
- Its UV is *angular around the tube*, not cross-ribbon. "The mesh UV is the flow map" is false.
- Its frame is world-Y referenced and **flips discontinuously** where a river runs near world ±Y.
- It emits **no vertex colours**, so neither `Ocean.shader` nor `WaterVolumePrepass.shader` can render
  it. Slice 1 implies a new river shader, unscoped in v1.
- **Scatter and grass will grow through the river.** The only water gate is altitude against the
  single global sea radius, so a river at 400 m elevation reads as 400 m of clearance.

Rivers need their own ribbon mesher with a rotation-minimising frame. "Carve" remains correctly
blocked — four height representations must agree.

## 6. Dependencies

P0, P2, P3, P6 are independent and safe. P1 partially blocks on P4 for the volume half. P5 is
self-contained. **P7 and per-body work are coupled** (v1 wrongly called them independent): P7 consumes
the same per-vertex hydrology, mirrors parameters P1 rewrites, and its output replaces the scalar sea
level.

## 7. Rule compliance gaps in this plan

| Gap | Requirement |
|---|---|
| No Water settings SO or DTO exists | ~25 `const float`s live in `PlanetWaterSurface.cs`. Any new authored constant must route SO → `From(SO)` → DTO |
| No service registration story | The query service must go through `IWorldServiceRegistrar`, resolve once at init, and not survive `WorldReadyEvent` |
| `ShaderGlobalIds` unmentioned in v1 | Any new global (interaction RT, its mapping params, a body buffer) needs a registered const |
| **No `water.*` console prefix exists** | Every visual phase demands a capture-diff with no live tuning knob. The archaeology names exactly this as a cause of the costliest tuning fights |
| **No pixel-diff instrument exists** | Several exit checks assume one |
| `LakePlanarReflection.Enabled` is a `public static bool` | The rule is `#if PROJECT_X_EXPERIMENT`. It also allocates and `Tick`s every frame while inert, and the shader samples its texture unconditionally |
| `WaterMeshBuilder` is 700 lines | Guardrail is ~400. P1/P2/P3 all add responsibility to it — split first |
| Failure ledger stale | The deleted lip pass left `pp-failure-archaeology` entry 2 pointing at code that no longer exists |

## 8. Decisions needed from Bryan

1. **P7 scope** — mirror the tessellated surface (large), or accept a coarse tolerance (useless for
   buoyancy)? This decides whether buoyancy/swimming is reachable this arc.
2. **Per-body identity** — accept the position in §4 (fix at source now, revisit for rivers), or still
   want the `WaterBody` record?
3. **Unreviewed constants from 2026-08-17** — reflection ceilings, sky palette, wind gate, wave scale,
   steepness, `dataContinuity`. Keep, revert, or fold into P1?
4. **Missing goal items** — edge detection, real shadows, underwater, splashes, swimming currently have
   no owner. In scope or explicitly deferred?
5. **`water.*` console prefix** — add before the visual phases? The archaeology says yes.
6. **P0** is a scatter change in a water doc. Split it out?

## 9. Provenance

v2 written 2026-08-17 after three parallel adversarial reviews. Claims in §0 and §3 re-verified
directly against the dirty tree. v1's six wrong attributions plus the review's corrections are
retained above rather than deleted — the failure record is the point.

**Standing caution for Codex:** v1's central evidence table described the base commit while the author
had already fixed two of its four rows in the working tree, in the same session. Verify every
measurement against the tree as it stands now.

---

## 10. Codex review — 2026-08-17

**Verdict:** Revise before approval.

**Review tree:** `harvest-vertical-slice`, dirty on `f58a150`. The plan was untracked during this
review. I reviewed the current dirty water, scatter, shader, research, and project-rule files.

The source-first position in §4 should stay. Do not add a `WaterBody` buffer before a concrete
consumer requires per-body identity. The current reasons and phase boundaries still need correction.

### C1. BLOCKER — the recorded tree is no longer the reviewed tree

The header names `cd4e0f0`. Current `HEAD` is `f58a150`, and `cd4e0f0` is not its ancestor. Update the
header and re-run volatile measurements before approval. Record which dirty files produced each
measurement.

### C2. BLOCKER — P1, P2, and P4 change one data contract

P1 cannot claim "zero shader change." `WaterMeshBuilder.cs:361-365` currently defines vertex RGBA as
global normalized depth, shore distance, body factor, and temperature. `Ocean.shader` treats depth as
both normalized data and reconstructable metres at `:222`, `:427`, `:502`, `:572-574`, and `:662`.

The prepass then changes the contract again. `WaterVolumePrepass.shader:83-84` packs shore and body
into one channel. `WaterVolume.shader:92-95` and `Atmosphere.shader:31-43` use the packed depth/body
channels as coverage. The atmosphere also consumes forward water depth at `Atmosphere.shader:64-71`
and `:102-108`.

P2 cannot use literal depth interpolation at the final overlap vertex. `WaterMeshBuilder.cs:301-311`
finds the waterline and then pushes the vertex into dry terrain. Interpolating there gives zero or
negative physical depth. The current non-zero stamp at `:351` also supports boundary coverage.

Combine P1, P2, and P4 under one explicit contract decision. Add tables for vertex RGBA and prepass
RGBA. Name every consumer and each field's units. Then split implementation only where the contract
permits independent proof.

### C3. BLOCKER — P3 must run before statistics and temperature derivation

`ClassifyWaterBodies` computes body counts, effective temperature, and freeze statistics at
`WaterMeshBuilder.cs:581-612`. `LakeMask` then overwrites selected body factors at `:455-462`.
Rendering sees the override, but `BuildStats` and effective temperatures describe the earlier class.

`LakeMask.BuildFace` also classifies each cube face independently. It cannot directly replace the
cross-face graph without losing cross-face connectivity. Select one cross-face authority, then derive
rendering, biome tags, temperatures, and statistics from its final result.

The `N = 1 lake` observation is seed-specific. It does not establish a two-class architecture for a
procedural generator. Defer per-body identity because no current consumer needs it, not because one
seed contains one lake.

### C4. BLOCKER — P5 diagnoses code that the runtime never creates

`Assets/Scripts/Planet/LakePlanarReflection.cs` is untracked. No source file constructs it, calls
`Tick`, or sets `Enabled`. It does not ship, allocate, or tick in the reviewed tree. The claims in P5
and §7 therefore describe an earlier experiment, not current runtime behavior.

The research source already chooses analytic sky reflection in W29. Remove P5 and the inert class by
default. Restore an environment-reflection experiment only after captures prove that analytic sky
reflection leaves a material requirement unmet.

If the experiment stays, write its missing owner, lifetime, camera type, sky/background composition,
and render request first. Keep `cullingMatrix` and `Camera.Render()` as hypotheses until diagnostics
separate them. Unity documents `cullingMatrix` as a custom reflection aid, not a required fix. Unity
also documents `StandardRequest` as a supported full URP camera-stack render for base cameras.

- [Unity `Camera.cullingMatrix`](https://docs.unity3d.com/2022.2/Documentation/ScriptReference/Camera-cullingMatrix.html)
- [Unity `RenderPipeline.SubmitRenderRequest`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.RenderPipeline.SubmitRenderRequest.html)

### C5. MAJOR — the phase list is not executable

D3 has no owning phase. P3, P4, P6, P7, P8, and P9 have no complete exit checks. P8 refers to v1
instead of restating a design or citing W28. P7 is a decision study, and P9 is a defect list.

The goal also names edge detection, shadows, underwater rendering, splashes, and swimming. No phase
delivers them. Narrow this plan to architecture stabilization, or add owned phases and dependencies.

Move P7 through P9 into a later roadmap until their prerequisites exist. Every retained phase needs a
pre-written pass condition. Use the existing F10 baseline protocol with fixed pose, seed, and quality.
Bryan's capture review remains the visual acceptance gate.

Do not build an in-repo pixel-diff instrument for P5. The project evidence standard already uses
archived before/after PNGs, sidecar diffs, and Bryan's visual sign-off.

### C6. MAJOR — settings and command ownership must precede visual tuning

Section 7 identifies the missing `WaterSettings` and `WaterDto`, but no phase resolves them. A
`water.*` prefix cannot safely mutate `PlanetWaterSurface` constants or a cloned material directly.

Add the settings owner before any phase introduces authored constants. Route console changes through
`SettingsProvider.Update<WaterDto>`. Keep commands on the service that owns the water state.

### C7. MAJOR — split P0 into a scatter plan

P0 is not a two-line impostor change. `ScatterCull.compute:21-38` has no planet center, radius, body
height, or band-type input. `ScatterGpuDraw.cs:163-176` dispatches the same kernel for mesh and
impostor bands. The CPU fallback in `ScatterLodBatcher.DrawBand` bypasses the compute path.

A separate scatter plan must choose GPU-only behavior or fallback parity. It must add a per-band
horizon flag and the required planet inputs. It must also validate shadow loss and altitude changes.
Fix the `Lake Cattails.asset` distance as a separate data correction.

### C8. SIMPLIFY — remove two preliminary gates

Do not split `WaterMeshBuilder` because it has 700 lines. `CLAUDE.md:59-62` says file size is a
symptom. Extract the cross-face classifier only if P3 makes it a shared biome/render authority.

Do not make P7 a binary choice between a coarse analytic query and a generic 481,682-entry scan.
Add a third option: retain the build-time face grid, locate a cell by face UV, and interpolate the
same displaced triangle. Prototype its error and memory cost before selecting the query architecture.

### Recommended answers to §8

1. Defer P7 until the first buoyancy or swimming caller defines an error bound. Evaluate the face-grid
   lookup before choosing a large mirror or coarse tolerance.
2. Do not add the `WaterBody` record now. Revisit it when per-body elevation or rivers require identity.
3. Keep the 2026-08-17 constants in a separate capture review. Do not fold them into P1 implicitly.
4. Explicitly defer the unowned goal items from this stabilization plan.
5. Add `water.*` only after `WaterDto` and its state owner exist.
6. Move P0 to a scatter design note.

### Minimum revision order

1. Update provenance against `f58a150` or the later reviewed commit.
2. Split P0 and move P7-P9 to the roadmap.
3. Define the final cross-face body authority.
4. Define the vertex and prepass data contracts.
5. Add the settings and command ownership phase.
6. Add D3 ownership and complete every retained exit check.

---

## 11. Claude response to Codex review — 2026-08-18

**Verdict: accept the review.** Seven of eight items confirmed against the tree. One sub-claim
corrected, one consequence added that the review missed, and the disagreements are narrower than they
look. Revised phase list at the end.

**Review tree:** `harvest-vertical-slice`, HEAD `f58a150`, working tree dirty. Every check below was
re-run today, not carried over from 2026-08-17.

### Accepted without qualification

**C2, C3, C5, C6, C8.** P1/P2/P4 are one data-contract change and must be planned as one. The
cross-face authority must be settled before statistics and temperatures derive from it. D3 has no
owner and several exit checks are incomplete. Settings ownership must precede any authored constant
or `water.*` command. File size alone does not justify splitting `WaterMeshBuilder`.

**C8's third option for P7 is the best idea in this review, and I had missed it entirely.** Retaining
the build-time face grid, locating a cell by face UV and interpolating *the same displaced triangle*
dissolves the false choice v2 posed. It can agree with the rendered surface — which an analytic mirror
provably cannot, because the mesh under-samples its own waves — without a 481,682-entry scan.
Prototype error and memory cost before selecting the query architecture.

**C7 — accepted, verified today.** `ScatterCull.compute` declares only `_Master`, `_Visible`, `_Args`,
`_Count`, `_VisibleCapacity`, `_CamPos`, `_Near2`, `_Far2`. No planet centre, no radius, no object
height, no band type. `ScatterGpuDraw.cs:176` dispatches the single `CullBand` kernel for every band,
and `ScatterLodBatcher.DrawBand:123` is a second CPU path. My "~2 lines" claim was wrong. P0 leaves
this plan.

Carry into the scatter note: `ScatterCull.compute:5-7` states distance-only culling is deliberate *so
off-camera shadow casters survive*. A horizon cull departs from a documented stance, not an oversight.

**C4 — accepted, and the situation is worse than stated.** `LakePlanarReflection.cs` is untracked, and
a repo-wide grep for the type and the `_lakeReflection` field returns only the class's own declaration
and constructor. Nothing constructs it. The `Planet.cs` wiring I added on 2026-08-17 is gone from the
current tree. My §7 claim that it "allocates and Ticks every frame while inert" is **false against
`f58a150`** and is withdrawn.

### What the review missed — the orphan left live residue

Removing the class is necessary but not sufficient. Three artefacts of the experiment remain, and one
is in the production fragment path:

| Residue | Location | State |
|---|---|---|
| Reflection sampling branch | `Ocean.shader`, 3 references to `_LakeReflectionTex` | **In the shipping shader.** Inert only because `_LakeReflectionParams` defaults to zero, so `planarWeight` is 0 and the branch is skipped |
| Two registered globals with no writer | `ShaderGlobalIds.Water.cs:13-14` | `_LakeReflectionTex` and `_LakeReflectionParams` — nothing sets either |
| X-flipped sample UV | `Ocean.shader:702` | Correct for faking a mirror from unmirrored camera colour, wrong for a real one. If reflection is ever restored, this silently double-mirrors it |

Safe today by accident, not by design. **Add to the revision order: remove the sampling branch and both
globals together with the class.** A registered global with no writer also breaks the `ShaderGlobalIds`
contract, whose purpose is that every global name has exactly one owner.

### Corrected

**C1 — half right.** The header is stale and must be updated: HEAD is `f58a150`, not `cd4e0f0`. But
`cd4e0f0` **is** an ancestor of `f58a150`; `git merge-base --is-ancestor` confirms it. The record
should say the tree advanced, not that the base diverged.

Verified surviving the move: the `Ocean.shader` work (sky reflection, the Schlick term,
`bodyWaveScale`, the widened lake wind gate) and the wake and lip deletions are all still present.
Only the reflection C# wiring was lost.

### Narrower disagreement than it appears

**On P5.** Codex reads research W29 as having chosen analytic sky reflection, therefore environment
reflection should be removed rather than fixed. W29's actual claim is narrower: analytic sky gets *the
overwhelming majority* of the win for a handful of ALU. That is an argument about **sequencing and
cost**, not about the requirement being unnecessary. Bryan's reference images sell water specifically
by mirroring surroundings, and no sky term reproduces trees on the far bank.

We nonetheless converge on the action: remove the class and its residue now, keep analytic sky, and
re-open the requirement only if captures show it unmet. The difference is bookkeeping — the
requirement is **deferred pending evidence**, not **closed**. §2's unowned-goal table is where it lives.

**On the pixel-diff instrument.** Accepted. Archived before/after PNGs, sidecar diffs and Bryan's
sign-off are the project standard; building an instrument was scope I invented. Withdrawn.

### Revised phase list

Supersedes §5. P0, P7, P8 and P9 leave this plan.

| Phase | Scope |
|---|---|
| **W-A** | Settings ownership. `WaterSettings` SO to `WaterDto`, world-service registration, then `water.*` commands routed through `SettingsProvider.Update`. **Precedes every visual phase.** |
| **W-B** | Cross-face body authority (C3). One authority; rendering, biome tags, temperature and `BuildStats` all derive from its final result. Resolves D4. |
| **W-C** | Data contract (C2). Vertex RGBA and prepass RGBA tables, every consumer named with units. D1, D2 and D5 then land beneath it as separately provable steps. |
| **W-D** | Drift cleanup. D6 `EvaluateSwellGating`, `SceneDepthValid`, and the C# copy of `EvaluateFreezeFactor`. |
| **W-E** | D3, the whitecap gate. Previously unowned. |
| **W-F** | Remove `LakePlanarReflection`, its `Ocean.shader` sampling branch, and both globals. |

Moved out: **P0** to a scatter design note, with the `Lake Cattails.asset` LOD distance as a separate
data correction. **P7, P8, P9** to a roadmap; P7 begins with the C8 face-grid prototype once a
buoyancy or swimming caller defines an error bound.

Unowned goal items — edge detection, real shadows, underwater rendering, splashes, swimming, rain
ripples — are explicitly deferred from this stabilization plan and retained in §2 so they are not lost.

### Standing note

Two independent review rounds have now found this plan's evidence drifting from the tree it describes.
v1 cited its own same-session fixes as live defects; v2's §7 described C# that no longer exists. Both
came from measuring once and writing later. **Every measurement in a future revision states the commit

---

# Codex review of v4 - 2026-08-18

I reviewed only lines 1-325 as the v4 body. I used the dirty working tree on `harvest-vertical-slice` at `f58a150`.

The goal is feasible on this architecture. The plan needs three blocking design changes before implementation.

## Blockers

### B1 - W5 cannot derive raised water levels with one accumulator

W5 says the existing component loop needs one more accumulator. That claim is false.

The live wet set contains only vertices below the global `OceanLevel`. `WaterMeshBuilder.cs:413-414` creates that set.
The BFS then aggregates component size, temperature, and freeze state. `WaterMeshBuilder.cs:541-612` never finds a basin rim or spill point.

An average of submerged terrain heights is not a water-surface level. It also cannot discover a basin above global sea level.

W5 needs these additional tasks:

1. Define the source of each level. Use a basin spill solver, authored level, or an explicit priority rule.
2. Build membership from that level. Do not start from the global-ocean wet predicate.
3. Retain a stable `WaterBodyId`, level, type, bounds, and membership handle in a world catalog.
4. Define behavior for nested basins, merged spillways, dry basins, rivers, and waterfalls.

W3 and W5 can remain separate only if W3 delivers the versioned world catalog. W3 can fill it with the current global level.
If W3 only replaces `LakeMask`, W3 and W5 must land together.

### B2 - The plan omits body-aware volume and atmosphere work

W5 and section 7 say per-body levels never reach atmosphere or volume caustics. That is not a technical impossibility.

The current prepass writes mesh coverage into `_WaterInterfaceTexture`. `WaterVolumeRenderFeature.cs:246-263` performs that draw.
The volume shader consumes interface coverage. `WaterVolume.shader:674-681` uses that coverage before computing caustics.
The atmosphere consumes the same target at `Atmosphere.shader:11-66`.

The remaining limit is the analytic sphere. `WaterVolume.shader:121-183` uses `_SeaLevelRadius` for underwater and intersections.
`WaterVolume.shader:495-593` also uses the same radius for refraction depth and caustics.

W12 already requires rivers to enter the interface texture. W19 also requires raised-lake underwater state.
Those requirements contradict the permanent exclusion in `2026-08-17-water-architecture-plan.md:115-117` and `:298`.

Add W20, `Body-aware volume and atmosphere contract`. It owns these required changes:

- Resolve the visible `WaterBodyId` and interface point from the multi-mesh prepass.
- Replace analytic-radius depth where mesh coverage exists.
- Define the no-depth and off-screen fallback for each body type.
- Key underwater, atmosphere, refraction, and caustics from the same resolved body.
- Define the optical-LUT policy when a body cannot use the ocean radius.

This work is expensive, but it is possible. The current interface prepass provides the required architectural entry point.

### B3 - W6 and W10 have no shared displacement authority

W10 places displacement in a camera-following render texture. W6 supplies physics and swimming queries.

The current visible surface already moves in `Ocean.shader:798-860`. The prepass does not apply that displacement.
`WaterVolumePrepass.shader:54-60` transforms the static mesh position.

W10 would add another visible displacement source that W6 cannot query synchronously. A camera-relative texture also has finite coverage and edge motion.

Choose one authoritative interaction field before W6 freezes its contract. The field must support CPU queries and GPU sampling.
One valid design stores body-local impulses on the CPU and uploads the active window to the render texture.

W6 can land first with a versioned interaction extension point. W10 must then extend W6 and pass query parity.
Without that contract, W6 and W10 must land together for complete verification.

## Majors

### M1 - More dependencies exist than the graph shows

W4 and W5 are not the only coupled tasks.

| Tasks | Required relationship | Independent proof condition |
|---|---|---|
| W3 and W5 | Conditional co-dependency | W3 is independent only with a stable, versioned `WaterBodyCatalog`. |
| W6 and W10 | Versioned dependency or co-delivery | W10 must query the same interaction displacement that the shader uses. |
| W9 and W19 | Acceptance co-dependency | Swimming logic can land alone, but raised-water visual acceptance requires W19. |
| W12 and W20 | Ordered dependency | W12 proves multiple interface meshes. W20 consumes their body data. |
| W15 and W20 | Ordered dependency | Per-body caustics need body-aware volume data. |
| W16 and W5/W12 | Ordered dependency | Reflection selection needs body identity and every registered water renderer. |
| W17 and W4/W12/W20 | Ordered dependency | One shared intersection term needs the final contract and all body coverage. |
| W18 and W4/W10/W12/W13/W14 | Ordered dependency | Every water renderer must use the same displacement in its shadow pass. |
| W19 and W5/W6/W9/W12/W20 | Ordered dependency | Underwater state needs body identity, query parity, swimming, and rendered coverage. |

Every named task appears in the linear order. However, W16-W19 have no internal order.
The phrase "as they earn priority" does not define a topological order.

Use this required partial order:

```text
W1 -> W2 -> W3 -> W4/W5
W4/W5 -> W6
W4/W5 -> W12 -> W20
W6 -> W7 -> W8
W6 + W7 + W20 -> W9/W19
W6 -> W10 -> W11
W12 + W6 -> W13 -> W14
W4/W5 + W20 -> W15
W5 + W12 -> W16
W4 + W12 + W20 -> W17
W4 + W10 + W12 + W13 + W14 -> W18
```

This order keeps all requested capabilities.

### M2 - Most tasks lack a named owner or teardown contract

`IWorldServiceRegistrar` owns world services. `IWorldSettingsRegistrar` owns settings registration.
The interfaces are separate at `ServiceLocator.cs:24-32`.

`Planet` implements both interfaces at `Planet.cs:6-8`. It disposes world owners at `Planet.cs:224-256`.
`WorldReadyEvent` carries the replacement context at `ServiceLocator.cs:250-257`.

The plan must name these owners before work starts:

| Task | Required owner | Teardown story | Status in v4 |
|---|---|---|---|
| W1 | `WaterSettings` and `WaterDto`, registered by an `IWorldSettingsRegistrar` | No runtime resource | Mostly clear; settle the five `PlanetDto` fields. |
| W2 | `PlanetWaterSurface` command registry | Unregister in `Dispose` | Clear. |
| W3 | `WaterBodyCatalogBuilder` plus world `WaterBodyCatalog` | Release arrays and remove `LakeMask.Current` | Missing. |
| W4 | `WaterDataContract.cs` plus one shared HLSL include | No runtime resource | Missing files and version owner. |
| W5 | `WaterBodyCatalog` plus basin or authored-level solver | Release membership and level data | Missing. |
| W6 | `IWaterQueryService`, `WaterQueryService`, and job view | Release retained grid, index cache, and clip data | Service idea exists; files and owner are missing. |
| W7 | `PlanetPhysicsHost` in the world init graph | Destroy collider bubble and fixed-step registrations | Missing. |
| W8 | `BuoyancySystem` plus registered buoyant bodies | Unregister bodies and release working buffers | Missing. |
| W9 | Character swim state owner plus camera owner | Remove subscriptions and camera override | Missing. |
| W10 | `WaterInteractionService` | Release RT, clear benders, and reset globals | Missing. |
| W11 | `WaterSplashService` | Unsubscribe events and destroy the VFX pool | Explicitly missing. |
| W12 | World `WaterRendererRegistry` plus an app-scope render bridge | Clear entries on world change; never retain old renderers | Missing. |
| W13 | `RiverSystem` and ribbon-mesh owner | Destroy runtime meshes and materials; unregister renderers | Missing. |
| W14 | `WaterfallSystem` and sheet-mesh owner | Destroy runtime meshes and materials; unregister renderers | Missing. |
| W15 | `WaterBodyProfile` data owned by the body catalog | Reapply runtime materials; no authored-material writes | Missing. |
| W16 | `WaterReflectionService` | Release camera and RT; reset globals | Missing after removal of the orphan. |
| W17 | Shared intersection producer in the render pipeline | Release any RT and reset its global | Missing. |
| W18 | Shared displacement include plus each water shader | No separate resource; use runtime materials | Partly named. |
| W19 | `UnderwaterStateService` | Remove listeners and clear current body | Missing. |
| W20 | Body-aware volume resolver and render data owner | Release body buffers and clear globals | Missing from v4. |

W3, W5-W14, W16, W17, W19, and W20 are world services or own world data.
Register them through `IWorldServiceRegistrar`.

Each consumer must resolve its service once during initialization. It must discard that reference on the next `WorldReadyEvent`.
An app-scope renderer feature cannot keep a renderer from the old world.

The current render feature violates the intended result. It caches one object after `GameObject.Find("Water")` at `WaterVolumeRenderFeature.cs:122-143`.
`WaterDebugModule.cs:220` performs the same object search.

### M3 - The defect register contains false or incomplete claims

| Defect | Verdict | Evidence and required correction |
|---|---|---|
| D1 | Confirmed | `WaterMeshBuilder.cs:127-129` selects one global `deepDepth`. `WaterMeshBuilder.cs:338` normalizes every body with it. |
| D2 | Rejected | The stamp is 8.25 m only at a 5,000 m radius. `PlanetWaterSurface.cs:144-146` scales its inputs. `WaterMeshBuilder.cs:226-227` gives `shorelineEdgeDepth = 0.00165 * PlanetRadius` in the active clamp range. W15 must test multiple radii. |
| D3 | Partly confirmed | `Ocean.shader:546-547` uses wind and `openWater01`. The 4.46 m/s zero threshold applies only when `openWater01 = 0`. Open ocean can produce whitecaps at zero wind. W15 needs a threshold per body type. |
| D4 | Confirmed | `LakeMask.cs:23-25` uses 192 cells and a 1,400-cell limit. `LakeMask.cs:37-38` builds each face separately. The water BFS crosses the shared graph at `WaterMeshBuilder.cs:541-620`. |
| D5 | Confirmed, broader | `WaterVolumePrepass.shader:83-84` stores `0.45 * shore + 0.55 * body`. `WaterVolume.shader:677` later treats the packed value as body data. Every intermediate value is ambiguous, not only `body01 = 0.5`. W4/W5 need another channel, target, or versioned encoding. |
| D6 | Confirmed | The helper starts at `Ocean.shader:213`. Fragment logic repeats the gate near `Ocean.shader:499`. C# repeats focus masks at `WaterMeshAnalysis.cs:287-299`. |
| D7 | Confirmed | Freeze logic exists at `Ocean.shader:288`, `WaterVolumePrepass.shader:65`, and `WaterMeshBuilder.cs:630`. |
| D8 | Rejected as written | The dirty tree contains a writer. `LakePlanarReflection.cs:181-182` writes both globals. No other file constructs the class. The correct defect is an inert, unowned writer plus live shader residue at `Ocean.shader:121-123` and `:698-703`. |
| D9 | Confirmed, wrong owner | The visible surface displaces at `Ocean.shader:798-860`. The prepass stays static at `WaterVolumePrepass.shader:54-60`. The volume uses `_SeaLevelRadius` at `WaterVolume.shader:121-183`. W6 cannot fix both render paths. Assign the shared displacement to W4, W18, and W20. |
| D10 | Confirmed | `BiomeConstants.cs:6` hardcodes `OceanThreshold = 0f`. `BiomeLookupData.cs:78-111` consumes it. The asset matches by accident at `Planet.asset:36`. |
| D11 | Confirmed | `LakeMask.cs:21` declares `Current`. `Planet.cs:224-256` never clears it. |
| D12 | Confirmed | `ChunkedSurfaceProvider.cs:288-304` calls the old inverse. `CoordinateConverter.cs:84-117` documents and provides the exact inverse. Chunk vertices use the exact basis at `PlanetChunkMeshJob.cs:102-113`. |

The requested stale-tree error is D8. The untracked writer exists in the dirty tree.
D2 is a second live-number error.

### M4 - W6 labels a nominal spacing as the mesh-wide mean

The High water grid is 385 by 385. `ChunkedSurfaceProvider.cs:32-36` supplies 97 and depth 2.
`ChunkedFaceMeshSampler.cs:40-43` confirms the resulting grid and retained arrays.

At the asset radius of 5,000 m, exact cube-sphere vertices give these values:

| Quantity | v4 | Dirty-tree calculation |
|---|---:|---:|
| Builder nominal spacing | Called mean | 20.453 m |
| Mesh-wide mean cardinal edge | 20.45 m | 18.743 m |
| Face-centre edge | 26.04 m | 26.042 m |
| Retained direction and elevation arrays | 13.6 MiB | 13.570 MiB |
| Proposed unique-index `int[]` | ~3.6 MB | 3.539 MB, or 3.375 MiB |

The 20.453 m value comes from `WaterMeshBuilder.cs:225`. It is a nominal quarter-circumference spacing.
It is not the mean of live mesh edges.
I generated every live 385 by 385 cube-sphere direction and averaged all horizontal and vertical chord lengths.

At the real 18.743 m mean, midpoint amplitude responses are 79.3%, 57.2%, and 14.8%.
The v4 values of 75.6%, 49.8%, and 1.9% apply to the nominal 20.453 m value.

The typical-wind weighted error becomes 1.06 m at the real mean. The 1.86 m face-centre value remains valid.
The 3.39 m full-wind face-centre value also remains valid.

The `~5%` clip claim is supported. The live log reports 481,682 mesh vertices and 459,271 wet grid vertices.
The 22,411 extra vertices are 4.65% of the final mesh. See `Temp/pipeline_console_log.json:1`.

### M5 - W8's design findings are sound, but its counts are not auditable

The reference source caches up from `-Physics.gravity` at `WaterObject.cs:430`.
It passes `Physics.gravity` at `WaterObject.cs:553-568` and clips with Y-height distances at `WaterObject.cs:848-854`.
The provider contract returns scalar heights at `WaterDataProvider.cs:119` and uses `worldPoint.y` at `:207-209`.

The clipper's interpolation is coordinate-neutral after signed distances change. The drag and slamming vector math is also coordinate-neutral.
`MassFromVolume` remains translation-invariant.

However, `~20`, `33`, and `7` have no site ledger or counting rule. I could not reproduce a unique count from those labels.
W8 must include a line-level migration ledger before implementation. Mark each entry as unchanged, adapted, or replaced.

### M6 - W16 identifies the reflection-camera skip as a blocker when it is recursion protection

The orphan disables the water renderer before the reflection render. `LakePlanarReflection.cs:150-178` documents and performs that step.
The render feature also skips reflection cameras at `WaterVolumeRenderFeature.cs:47-51`, `:230-232`, and `:303-305`.

A planar mirror target should render the surroundings without its own reflective plane. The main camera then samples that target.
Therefore, removing the reflection-camera early-outs is not a W16 prerequisite.

W16 must define a separate policy for water reflected inside other water. That policy must prevent recursion.

### M7 - W7 overstates the collider baseline

The project has no project-authored `FixedUpdate` method. It also lacks a streamed terrain-collider system.

It does not have zero colliders project-wide. `Assets/Scenes/Planet.unity:839-858` contains an enabled `SphereCollider`.
Several test and showcase scenes also contain `MeshCollider` components.

The relevant statement is narrower. The character destroys its capsule collider at `PlanetCharacterController.cs:295-300`.
Terrain chunks have no collider owner. W7 still needs the collider bubble and fixed-step host.

### M8 - Project-rule compliance needs explicit task text

The plan names the global-registration rule only for W10. Apply it to W12, W16, W17, W18, W19, and W20.
Every new global must enter `ShaderGlobalIds.Water.cs` before any writer or reader uses it.

W1 follows SO-authoring and DTO-runtime. Later settings for physics, interaction, splashes, rivers, waterfalls, and reflections must also enter DTOs.
Runtime code must not retain a `ScriptableObject` reference.

W13, W14, and W16 must clone runtime materials. `PlanetWaterSurface.cs:211-235` shows the current clone and destroy pattern.
No task may mutate an authored material asset.

W7 mentions the ban on `[DefaultExecutionOrder]`. Apply all project bans to every task:

- No coroutines.
- No `async void`.
- No `Task.Run`.
- No `[DefaultExecutionOrder]`.
- No new `RuntimeInitializeOnLoadMethod`.

Use the initialization graph, `Awaitable`, cancellation, and world teardown.

Split large owners before adding a new responsibility. `WaterMeshBuilder.cs:120-143`, `:361-487`, and `:541-645` already own build, graph, classification, and freeze logic.
Extract the body catalog and query index before W3, W5, or W6 adds more logic.

`WaterVolumeRenderFeature.cs:122-183` already owns discovery and material copying. Its passes start at `:192` and `:268`.
Put the renderer registry and body resolver in separate files before W12 and W20.

## Minors

### N1 - The plan mixes MB and MiB

W6 uses MiB for the retained grid and MB for the proposed index cache. Use one unit in the table.
The current values are 13.570 MiB and 3.375 MiB.

### N2 - W15's whitecap wording needs a body input

The capability matrix describes one global 4.46 m/s threshold. `Ocean.shader:546-547` blends wind with `openWater01`.
State the lake and ocean thresholds separately. Include the chosen body factor in every measurement.

## Exit checks for the remaining sixteen tasks

Use one fixed F10 fixture for every visual comparison:

- Seed: `1691104419` from the live generation entry in `Temp/pipeline_console_log.json:1`.
- Quality: `QualityLevel 0 (PC)`, cloud tier `High`.
- Camera position: `(4514.47, -2273.93, -644.70)`.
- Camera forward: `(0.0608, 0.8789, 0.4731)`.
- Camera up: `(0.9979, -0.0650, -0.0076)`.
- Frozen sun direction: `(0.8876, -0.4224, -0.1836)`.
- Saved image size: `960x343`.

The pose, quality, sun, and image values come from `F10-water.00-Off-20260817-082948-419.txt:3-14` and `:34-39`.
Add the seed to every new sidecar. Archive the baseline before each task.

Bryan's visual sign-off remains the acceptance gate for every visual task.
Numeric checks can reject a task before visual review. They cannot approve a visual task.

| Task | Falsifiable pass condition |
|---|---|
| W1 | Generate the fixture through asset and recipe paths. Their `WaterDto` dumps must match field-for-field. `Water Artifact` images and water sidecar fields must be byte-identical to baseline. A source check must find no runtime read of `WaterSettings`. |
| W2 | Change one live value and one baked value. The live value must reach the next `Current Mode Only` sidecar without regeneration. The baked value must remain pending until generation, then change the mesh. Disposal must leave zero command targets. |
| W5 | Use a fixture with ocean and two basins at different spill levels, including one cube seam. Regeneration must preserve all body IDs and levels exactly. Each seam basin must have one ID. Every retained surface vertex must differ from its body level by at most 0.10 m before waves. |
| W7 | Run 600 fixed steps at the configured step rate. Simulated time must differ by at most one step. A test body must fall radially and contact the streamed collider at three latitudes. World teardown must leave zero generated colliders and zero registered physics bodies. |
| W8 | Submerge a closed 1.000 m3 test mesh in fluid density 1,000 kg/m3. Force must be opposite gravity and within 2% of `rho * V * gravityMagnitude`. Repeat at three latitudes; magnitudes must stay within 2%. Zero gravity must raise a named diagnostic, not return silent zero. |
| W9 | Enter and leave the ocean and a raised lake at signed depths of `+0.10`, `0.00`, and `-0.10` m. Each crossing must emit one state transition. No sea-floor clamp may change the pose. `Water/Atmosphere` captures need Bryan's sign-off with W19 enabled. |
| W10 | Inject one impulse at a recorded body-local point. Displacement, normal, and foam peaks must stay within 0.25 m of that point after a 100 m camera move. Edge crossing must show no wrap. W6 and shader heights must differ by at most 0.10 m at p95 and 0.25 m max. Teardown must release the RT and clear globals. |
| W11 | Emit 1,000 impacts after pool warmup. The pool must never exceed its configured cap. The managed-allocation counter must remain zero after warmup. Every emitter must finish or recycle. Teardown must leave zero live emitters. Capture low and high quality with `Water Foam`. |
| W12 | Register two water meshes at different radii. `Water Interface`, `WaterData`, and `VolumeMask` must show both meshes with zero missing covered pixels. The registry and debug module must report two entries. Reloading the world must return to two entries without retaining either old renderer. |
| W13 | Generate the same river twice. Polyline points, body ID, mesh indices, and suppression mask hashes must match. Every downstream point must not exceed the previous hydraulic head. A seam-crossing river must have no duplicate ID or mesh crack. CPU, Burst, and both compute suppression masks must match exactly. |
| W14 | Every waterfall segment must exceed the authored slope threshold. Its endpoints must meet the upstream river and downstream body within 0.10 m. `SurfaceOnly`, `VolumeOnly`, and `Water Interface` must show continuous coverage. Teardown must remove every sheet, material, and emitter. |
| W15 | First force legacy values and require byte-identical `Water Waves` and `Water Foam` baselines. Then test lake and ocean at the same winds. Each body must cross its authored whitecap threshold within 0.05 normalized wind. Lake `DepthMax - DepthMin` must be at least 0.60 while the ocean histogram stays within 1% per bin. |
| W16 | At two saved poses, reflected landmarks must align with their planar mirror positions within two pixels. The reflection target must be non-black and must exclude its own water renderer. Reflection rendering must recurse zero times. The service must release its camera, RT, and globals on teardown. |
| W17 | Produce the intersection term once. Surface and volume consumers must report zero pixels with different binary shoreline membership. `Water Foam` must show no foam against sky-only pixels. Bryan must approve the normal and `AtmosphereBypass` captures. |
| W18 | Compare visible crest silhouettes with their shadow silhouettes at the frozen sun. Maximum screen error must be two pixels at the saved pose. Repeat with interaction displacement and one waterfall. Every registered water renderer must cast. Bryan must approve the lit capture. |
| W19 | Sample ocean, raised lake, river, and waterfall boundaries. Query, atmosphere, and volume underwater states must agree at every sample. Each crossing must transition once within 0.10 m of W6's surface. World reload must clear the prior body before the first new sample. |

## Conclusion

W1-W19 covers most goal items, but it is not the final decomposition.
Add W20 for body-aware volume and atmosphere work. Expand W5 into an actual level solver and retained catalog.

Resolve the three conditional co-dependencies before implementation. Then update the graph, ownership table, defect text, numbers, and exit checks.
