---
name: project-water-tech-research
description: "2026-08-16 water tech survey — our water is stronger than docs claim, one verified wave bug, and the CPU-height-query keystone"
metadata: 
  node_type: memory
  type: project
  originSessionId: e48b33b5-7c28-4488-a1d6-7796f25eeac1
  modified: 2026-08-17T13:31:40.369Z
---

2026-08-16 survey of external water tech (D:\Unity\Explore Assets + Asset Store cache + `local-only/`) against our sphere. Doc: `docs/research/2026-08-16-water-tech-research.md`, recommendations **W1–W28**. Read-only; nothing implemented.

**Our water is further along than every prior doc claims.** The 2026-08-11 adoption map's "flat water surface, no waves on the sphere" is **STALE** — `ComputeOceanSwell` (`Ocean.shader:756-794`) already sums 3 directional sines in a wind-aligned tangent frame and displaces **radially**, with analytic normals, wind coupling, and pond/deep/shore amplitude gating. Plus an analytic ray-sphere water volume with per-channel absorption and triplanar chromatic caustics.

**W1 is the keystone and it's unusually cheap for us.** Our wave primitive has **no horizontal displacement**, so a CPU height query needs **no fixed-point inversion** — the thing that costs Gerstner/FFT 4× and burns GDWaterKart 15 texel reads. A byte-exact CPU mirror is 3 `sin` + 3 `cos`. Insertion point `PlanetSurfaceGrounding.cs:38`. Unlocks buoyancy/swimming/boats/splashes.

> **CORRECTION 2026-08-25 — do not use the paragraph above to scope W6.** "No inversion" is true and still
> the reason a CPU mirror is cheap, but it does NOT make the water query small. The architecture plan
> ([docs/design/2026-08-17-water-architecture-plan.md](../../docs/design/2026-08-17-water-architecture-plan.md), W6)
> refutes analytic mirroring outright: the swell is gated by per-vertex `depth01`/`shore01`/`body01` and
> zeroed by `EvaluateFreezeFactor`, so **near shore and on frozen bodies an analytic mirror is wrong by 100%
> of amplitude** — precisely where wading and swim-entry happen. W6 needs the MESH: triangle interpolation
> (exact), a retained face grid, and the `originalVertexCache` that `WaterMeshBuilder.cs:150` currently
> discards. 4.65% of mesh vertices are shoreline clip vertices with no grid counterpart.
>
> I nearly told Bryan "W6 is small" off this paragraph. Read the plan's W6 section before scoping it.

**W-BUG-1 — CORRECTED 2026-08-17, first version was WRONG.** The subagent claimed all modes degenerate together at `±(A×B)` (13.4% of ocean glassy), reasoning `positionTS=(0,0)` there. **Phase zero ≠ phase GRADIENT zero.** Re-derived: `theta = k·dot(L, dirTS.x·A + dirTS.y·B)`, so each mode is a plane wave with its own fixed 3-D `D̂ᵢ`; local frequency on the sphere is `k·sin(angle(L̂,D̂))`. So each mode degenerates at **its own** `±D̂ᵢ`, and `±(A×B)` is where waves are **sharpest**, not flattest. Real defect = **clustering**: all three `D̂ᵢ` lie in `span{A,B}` → all 6 poles on one wind-aligned great circle within ~90°. Severity mild — at `D̂₁` only mode 1 (0.58 of 1.04 amplitude) flattens, detail drops to ~46%, not glassy. Some degeneracy is unavoidable (hairy ball). Fix = spread modes 2/3 through `(A, cross(A,B))`; it's a **visual change**, needs a capture-diff.
**Lesson: verify subagent analytic claims by re-deriving. This one survived into a doc of record and a user-facing summary before I checked it.**
Also: `_SwellAmplitude`/`_SwellWavelength` are **material-authored only** (latent CPU/GPU divergence — fix before W1); volume `_SeaLevelRadius` is **wave-blind by ±5 m** vs the displaced surface.

**2026-08-17 MEASURED (first-ever GPU water numbers; both timed capture sets run, teleport `Lake1`, 60-sample windows).** Robust, reproduced 3/3 runs: **the 938,167-tri water mesh is FREE** (`SurfaceOnly` within ±0.13 ms of `WaterOff`), and **the fullscreen volume composite is the entire water cost** (`VolumeOnly` − `WaterOff` = +2.44 / +2.80 / +1.33 ms). NOT robust: absolute water total swings 0.54–3.11 ms between runs, and one run has `Off` < `VolumeOnly` (impossible) — between-mode noise ≈ effect size, so **volume stage decomposition failed** (`VolumeOptical` +0.14, `CausticsOnly` +0.36, `BottomDistortionOnly` +0.94 = all noise; needs a GPU profiler, not the frame timer).
**This CORRECTED W16 in the doc** — I had called mesh LOD "the largest single frame-cost lever available"; it is not a perf lever at all, only a prerequisite for high-frequency displacement (W22). Water perf work starts at `WaterVolume.shader` (raymarch `viewSteps=16/sunSteps=8`, triplanar Voronoi caustics, full camera-colour copy).
Also measured: **`Uninstrumented CPU` = 21.70 ms of a 22.11 ms CPU frame — 98% of CPU time has no counter.** Not water-specific; worth its own look.

**2026-08-17 DONE (W3 + W-BUG-2), verified:** deleted the wake system (2 files, 4 globals, 2 material props, `WakeMask` mode, "Water Wakes" set), the entire `WaterVolumeLip` subsystem (generation, 2 shader passes, render-feature plumbing, `IsCameraInsideWaterMesh`, 4 debug modes), and the misleading `Water CPU` HUD line (`FrameTimingSection.Water` had exactly one writer — the wake controller). Promoted `_SwellAmplitude`/`_SwellWavelength` into `PlanetWaterSurface.cs` at shader defaults (5.0/90) — no-op today, prevents silent CPU/GPU divergence once W1 lands. Evidence: 4/4 csproj clean, Unity console zero errors, 3 shaders `supported=True errors=0`, `WaterVolumePrepass` 3 passes→1, loaded-assembly reflection confirms wake symbols gone, regenerated planet = **481,682v/938,167t byte-identical to the 2026-08-12 sidecar**.

**Dead weight confirmed (now removed):** wake system publishes 4 globals **no shader reads**; `WaterVolumeLip` never instantiated yet computed every gen; `FrameTimingSection.Water` instrumented only on the inert wake publish, so "Water CPU 0.00 ms" **measures nothing**. Two timed water capture sets exist and have **never been run** — we have zero GPU water measurements.

**Best external sources:** Tessendorf `ocean water.pdf` (Jacobian foam, still SOTA) · **Poseidon** (CPU/GPU parity via shared time global + shared noise texture; Bézier wave that **discards the lateral term** → sphere-portable) · **Polyart Dreamscape** (only complete URP ocean system; also height-only; terrain-heightmap shore damping; `_FlowPivot` radial mode = correct lake waves) · **DWP2** (per-triangle buoyancy, clipping code already gravity-agnostic, ports in ~12 lines; ships ZERO Burst) · **ECM2** (swim + immersion-depth buoyancy already written against `-GetGravityDirection()`, ships a Planet Walk example — cheapest buoyancy tier) · **RTG2 §30.3** (the ONLY technique surveyed that is sphere-safe as published).

**Dead ends — don't re-search:** Thalassophobia (Built-in RP + GrabPass, unlit, **zero waves**; "URP" overlay byte-identical to base and a version downgrade) · Obi Fluid **not installed** (Rope only) · ARTnGAME Oceanis/InfiniRIVER are **welcome-screen icons only, packs absent** · FS Swimming (hardcodes `Vector3.up`) · `fastcaustics.pdf` (flat receiver plane, unfixable) · `Environment-Project` (all submodules empty). Full negative list at §7.1 of the doc.

**Rivers are viable** — `ShapeGenerator.SampleElevation` is pure/thread-safe/arbitrary-resolution, and the cube-face seam is already solved twice in our tree. Two blockers: we **cannot carve** (height is analytic in 4 places that must agree) and there is **one global sea level**. Recommended first strategy is heuristic polylines grown **uphill from coastal seeds then reversed** — descends to the ocean by construction, walks over `Vector3` directions so **there is no seam**. Ribbon mesh = `TreeTubeMesher.BuildCappedTube` with `sides=2`; the mesh UV **is** the flow map.

**Open structural question for Bryan: per-body water level** — unlocks mountain lakes + rivers + waterfalls together, highest blast radius.

Related: [[reference-local-only]] (its `*_unity_guide.md` files are fake papers — verified in this survey), [[project-gameplay-roadmap]], [[project-lake-biome]], [[project-ocean-scatter]].

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Water tech research](project_water_tech_research.md) — 2026-08-16 survey (doc `docs/research/2026-08-16-water-tech-research.md`, W1–W28). **Our water is stronger than every prior doc claims** — "no waves on the sphere" is STALE; `ComputeOceanSwell` already displaces radially with wind coupling. **W1 keystone: our waves have NO horizontal displacement → CPU height query needs NO inversion, 3 `sin` calls** (insert at `PlanetSurfaceGrounding.cs:38`). **W-BUG-1 CORRECTED (first version wrong — phase zero ≠ gradient zero):** each swell mode degenerates at its own `±D̂ᵢ`, `±(A×B)` is where waves are *sharpest*; real defect is that all 3 poles cluster on one wind-aligned great circle, severity mild (~46% detail dip, not glassy). Dead: wake globals (no shader reads), `WaterVolumeLip` (never instantiated), `Water CPU` HUD (measures nothing). Best sources: Poseidon (CPU/GPU parity + height-only Bézier wave), Polyart Dreamscape, DWP2 (buoyancy, ~12-line sphere port), ECM2 (swim, already gravity-agnostic). Dead ends listed so nobody re-searches.
