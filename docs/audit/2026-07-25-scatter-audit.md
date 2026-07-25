# Scatter subsystem audit — 2026-07-25

Branch `scatter-placement`. Covers the placement + render slice (SP1/SP2) after the
analytic-surface + background-gather work. Findings-first; open items marked **Needs Review**
are Bryan's call (aesthetic/design) and were deliberately not auto-changed.

Milestone tag: `scatter-surface-async-v1` (commit `939250d`).

---

## F1 — Placement sampled the streaming LOD mesh (props floated)  ·  FIXED

- **Category:** Bug · **Severity:** High · **Status:** Fixed (`002dab7`)
- **Description:** `ScatterField` snapped props to `ChunkedSurfaceProvider.TryGetLocalSurfaceRadius`,
  i.e. whatever LOD chunk was resident. Over a coarse chunk (a distant basin) the mesh reads back
  near sea level, so props floated on lakes; LOD flips shifted props under the camera.
- **Evidence:** props visibly floating over water in Bryan's `floating_trees` capture;
  `ChunkedSurfaceProvider.cs:296-304` samples the resident leaf chunk.
- **Fix:** new `ISurfaceGroundSampler` seam (returns ground radius + normal, so it already fits
  future marching-cubes/SDF terrain) backed by `AnalyticGroundSampler` over
  `ShapeGenerator.SampleElevation` — the LOD-independent, deterministic field the meshes are built
  from. Props now snap to the true surface; `scatter.verify` still PASS.

## F2 — Synchronous main-thread gather (travel jitter)  ·  FIXED

- **Category:** Bug (perf) · **Severity:** High · **Status:** Fixed (`939250d`)
- **Description:** the renderer re-gathered synchronously on the main thread every 10 m of camera
  travel; a 150 m gather is 100–300 ms (dominated by the finest-spacing prototype), producing a
  periodic hitch while panning stayed smooth.
- **Evidence:** `scatter.count` reports 189–298 ms per gather; symptom matched exactly
  (stall on travel, smooth on pan).
- **Fix:** `PlanetTransformSnapshot` + `ScatterField.GatherContext` capture transform + per-gen
  config on the main thread; `ScatterRenderer` double-buffers a gather run via
  `Awaitable.BackgroundThreadAsync` and swaps on the main thread. One gather in flight; regen/
  teardown cancels via token. Main thread now only kicks + draws. Validated in the live loop.

## F3 — Props render dark  ·  Needs Review (aesthetic)

- **Category:** Style · **Severity:** Low · **Status:** Needs Review
- **Description:** trees/rocks/bushes read darker than the lit terrain.
- **Evidence:** ruled out a gamma bug — `Generic_01_A` atlas imports sRGB=True. Cause is Synty's
  dark low-poly art plus the diagnostic grid scene's low ambient (~0.21) and a low test-sun.
- **Recommendation:** verify brightness on the real planet (atmosphere-lit sky) before tuning;
  if still dark, raise `_BaseColor` tint on `SyntyProps.mat` or the scene ambient. Not a code fix.

## F4 — No impostors; 150 m gather hard cap (empty far field)  ·  Open

- **Category:** Architecture · **Severity:** Medium · **Status:** Open (SP3)
- **Description:** `ScatterRenderer.RegionMeters = 150` caps draw distance, and octahedral impostors
  (the SP3 design target) are unbuilt, so past ~150 m nothing fills in — trees just dither out.
- **Recommendation:** the planned SP3 work — banded gathers to extend range + impostors baked on a
  background thread. Sizeable; own design pass.

## F5 — Far-horizon dither stipple at the cull ring  ·  Open

- **Category:** Style · **Severity:** Low · **Status:** Open
- **Description:** the screen-space dither fade shows as stippled canopies at the cull distance.
- **Recommendation:** fold into the F4 banded-gather work (smooth the fade band).

## F6 — Only 3 prototypes wired  ·  Needs Review (aesthetic)

- **Category:** Maintainability · **Severity:** Low · **Status:** Needs Review
- **Description:** Forest = Tree + Rock, Grassland = Bush. `Flowers_01`, `Bush_02`, `Rock_03`,
  `Tree_Pine_01` are imported but unwired; no grass-tuft prototype exists.
- **Recommendation:** which biome / density / whether flowers are worth their fine-spacing gather
  cost are Bryan's calls — now informed by `scatter.density`. Wiring is mechanical once decided.

---

## Tooling added

- `scatter.density [region] [gridCells]` — per-prototype counts with implied-vs-target spacing,
  plus an ASCII heatmap of the tangent-plane distribution (biome-edge falloff visible at a glance).

## Notes / process

- Slope is now measured from the analytic surface normal (2 m probe) instead of chunk samples; a
  small behaviour shift from the old cell-scale epsilon. `scatter.verify` PASS confirms determinism
  and order/region independence held.
- Recompiling while in Play mode leaves the running domain stale (HotReload can't patch new
  structs/volatile fields); stop + start Play to load new scatter code before runtime testing.
