---
name: pp-research-frontier
description: Use when asking what to build next, roadmap or research direction toward the full-planet 3rd-person vision, or "is this possible here" — fly through clouds, caves and terrain digging, character controller on the sphere, ocean buoyancy, far-field grass, biome seams, flat-looking terrain, multiplayer persistence. Not for executing the live cloud/grass visual work — see pp-visual-migration-campaign. Not for how to run an experiment to an accepted result — see pp-research-methodology.
---

# pp-research-frontier — open problems toward the full-planet vision

Bryan's stated vision (Phase-1 interview, 2026-07): **a full-planet 3rd-person experience
where the character can build structures, modify terrain, and eventually fly in the
clouds; the visual experience is the primary bar.**

This skill catalogs the open problems between the current codebase and that vision. Every
item is **open or candidate** — nothing here is approved, scheduled, or promised. For each
problem: why the standard/SOTA approach doesn't transfer to this project's constraints,
what asset this repo already has that generic solutions don't, the first three concrete
steps in this repo, and a falsifiable "you have a result when…" milestone.

Rules that bind all of it:

- Any code that follows from this skill goes through change control (pp-change-control):
  Bryan reviews findings before fixes, and Bryan's eyes lock any visual result. A
  milestone below being met is *evidence for review*, never self-approval.
- Run the experiment lifecycle from pp-research-methodology (predict numbers first,
  adversarial refutation, evidence bar).
- "F10 capture" = the debug-screenshot workflow (`debug.capture-set "<Set Name>"`, press
  F10 in play mode → PNGs + metadata into `local-only/debug-screenshots`) — details in
  pp-run-and-operate. "Weather grid" = the CPU/GPU weather sim that is the single source
  of truth for clouds/rain/wind — details in pp-weather-sim-reference. "Stamp" = a saved
  `SurfaceEditStamp` record (see problem 3).

## Where the master plan says this goes

`docs/PROJECT_PLAN.md` phase dependency graph (unchanged as of 2026-07-06): Phase 9
(marching cubes/caves) needs Phase 3; Phase 10 (character) needs 9; Phase 12 (Valheim-style
building) needs 3+10+11; Phase 14 (multiplayer) needs 3+10+11+12. Flight mode is Phase 10.4
(`docs/phases/09-phase10-character.md`). Phase 13 (LOD) is "parallel, any time". Phases 3-8
(foundation, biomes, water, celestial, moons, spawning/grass) are the shipped substrate the
problems below stand on.

## Problem index

| # | Problem | Status (2026-07-06) |
|---|---|---|
| 1 | Fly-through clouds (camera inside the volume) | open; blocked on cloud migration Phases 2-4 stabilizing |
| 2 | Planet-scale ground-cover continuity (grass far-field) | open DECISION — owned by pp-visual-migration-campaign |
| 3 | Deformable terrain + caves with stamp provenance | open; Phase 9 unstarted, substrate exists |
| 4 | CPU-queryable ocean swell (buoyancy/swimming) | open; GPU half landed in working tree |
| 5 | Seam-free planet-scale derived bakes (biome seam) | open; root cause known, fix designed, unscheduled |
| 6 | Terrain relief believability (normal-mapping flat) | open; several cycles tried, hypothesis untested |
| 7 | Character controller on a deformable cube-sphere | open; zero character code exists |
| 8 | Persistent, replicable world state (seed + stamps + overrides) | candidate; substrate half-built |

---

## 1. Fly-through clouds — camera inside the volumetric layer

**Why SOTA doesn't transfer.** Published real-time cloud systems (Horizon Zero Dawn/Nubis,
Frostbite — see `docs/research/2026-07-04-cloud-visual-research.md`) assume a ground-level
or flight-sim camera under or above the layer and lean on **temporal
reprojection/accumulation** to afford enough march steps. This project's renderer is a
single-pass fullscreen march through a spherical shell, and **temporal accumulation was
tried and reverted** (stated in that research doc's preamble; also the 2026-07-03 line
audit). Copying an AAA fly-through recipe means re-fighting the reverted-temporal battle.

The concrete inside-the-layer failure is visible in `Assets/Graphics/Shaders/Cloud.shader`
(frag, ~lines 312-330 as of 2026-07-06): `startDistance` comes from the outer-shell ray
hit, the camera-below-inner-radius case is handled (lines 320-324), but the whole marched
segment is divided by one fixed `_CloudViewSteps` count — with the camera *inside* the
shell the segment is huge, so per-step size balloons exactly where per-meter detail matters
most, and the weather texture (one value per grid cell) provides no sub-cell structure at
arm's length.

**Our asset.** Weather-grid-driven volume + planet-scale cube-sphere sampling are already
unified: `Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl` samples
`_CloudWeatherMap`/`_WeatherDynamicsMap` (6-face `TEXTURE2D_ARRAY` via `CubeFaceUv`) from
any 3D direction, so "which cloud am I inside and is it storming" is a solved query from
any camera position — most demos hand-author their weather texture on a flat plane.
Blue-noise ray offset (`_CloudBlueNoise`) already landed (cloud migration Phase 1).

**First three steps.**
1. Characterize before designing: use the free camera (`ICameraRigContext` /
   `CameraTeleportStore`) to park inside a cumulus cell (`cloud.debug-mode` sweeps to find
   one), archive an F10 "Cloud Diagnostics" set. Enumerate the actual failure modes
   (banding, slab entry pop at shell crossing, weather-cell flatness).
2. Predict then measure step economics: with camera at mid-shell, compute expected step
   size from `_CloudViewSteps` and shell thickness (`_CloudOuterRadius - _CloudInnerRadius`,
   `Cloud.shader:154`); prototype **distance-adaptive step size** (small near camera,
   growing with distance) in the existing march loop — this is a redistribution, not a
   budget increase.
3. Add a near-field detail-noise octave gated by `marchDistance` (the detail-noise
   early-out from migration Phase 1 already restructured `SampleCloud` for cheap gating).

**You have a result when** an F10 capture pair — one from just outside a cumulus cell, one
from inside it — shows (a) no visible transmittance pop at shell entry when scrubbing the
two, (b) interior structure that is not a uniform fog slab, and (c) frame time within the
budget recorded by `FrameTimingCounters` (`Assets/Scripts/Core/Services/FrameTimingModule.cs`)
for the current outside-view baseline. Bryan's eye judges the look; the pop and the ms are
falsifiable without him.

**Sequencing:** don't start until cloud migration Phases 2-4 stabilize the lighting model
and weather-shaped profiles — interior look built on the old lighting would be rework.

## 2. Planet-scale ground-cover continuity (grass far-field beyond 200 m)

**This is a live decision owned by pp-visual-migration-campaign** (grass plan Phase 3,
options a/b/c: re-land the paint blanket / re-enable the chunk mid-band / both). Do not
duplicate that menu here — the campaign skill has the gates and evidence.

The *frontier framing*, for when the decision work happens: as of 2026-07-06 the only live
grass layer is near-field blades (144 m full density / 200 m draw —
`Assets/Scripts/Core/QualityController.cs:41-42`); `_chunkGrassEnabled = false` and
`_grassBlanketEnabled = false` since the biome-stripe fight. Beyond 200 m: bare terrain.
The open research question generic engines don't answer is **orbit-to-ground LOD
continuity for ground cover on a whole sphere** — asset-store grass ends at a draw
distance because there's no orbit view to betray it.

**Our asset.** A regression harness already exists: the console script
`Assets/Resources/ConsoleScripts/Grass Edge Strip Probe.txt` (run via `script.run "Grass
Edge Strip Probe"`), plus the single-source canopy color (`GrassCanopyAlbedo`) designed to
make blades and terrain paint meet at one brightness, plus the validated *diagnosis* of
the biome stripes (a linear-coverage fix for `PlanetVertexColor.shader` found during the
probe sessions). Caveat, matching the campaign's grass-phases.md: the exact reverted fix
code is **not in the working tree** — it must be re-derived from the probe-session
evidence (UNVERIFIED which commit, if any, holds it).

**First three steps** are the campaign's Phase 3 — go there. **You have a result when**
the campaign's own exit check passes: an orbit-to-ground descent capture sequence with no
visible ring, stripe, or brightness step at any altitude, and clean strip-probe captures
at the two worst biome borders.

## 3. Deformable terrain and caves unified with stamp provenance

**Why SOTA doesn't transfer.** Standard voxel terrain (Minecraft-likes, Lague's marching
cubes demos, the Fluid-Planet reference named in
`docs/phases/08-phase9-marching-cubes.md`) persists **raw density deltas per chunk**:
large, opaque, unreplayable, and married to one chunk layout. This project has already
committed to the opposite model for surface edits, and terrain deformation should join
it rather than fork a second persistence scheme.

**Our asset.** The `SurfaceEditStamp` system: saved stamps are the durable world state;
path wear and scorch are **derived caches rebuilt from the stamp list** (comment and
implementation in `Assets/Scripts/Planet/Surface/SurfaceEditController.cs`; the class at
line 617 carries `kind/shape/operation/strokeId/direction/radiusMeters/strength/
createdUnixSeconds/regrowSeconds`; rebuild entry point
`ChunkedSurfaceProvider.RebuildPathWearFromStamps`, `ChunkedSurfaceProvider.cs:494`; save
file `surface-edits-{seed}.json` under `Application.persistentDataPath/ProceduralPlanets`).
Also already in place: `TerrainQuadtree` per-face chunk tree
(`Assets/Scripts/Planet/Surface/TerrainQuadtree.cs` — construction/subdivide/merge shipped;
its header says mesh jobs and LOD triggers are deliberately not yet built), and the
`IWorldAction` command interface with `Serialize()`/`Deserialize()` plus undo/redo history
in `WorldActionManager` (`Assets/Scripts/Core/Services/WorldActionManager.cs`) — currently
zero implementations (both files carry `FUTURE:` comments).

**First three steps.**
1. Design (findings doc first, per audit workflow): extend the stamp vocabulary with a
   `kind: "deform"` stamp whose fields express a density brush (direction, radius, signed
   strength, shape) — same JSON list, same rebuild-from-stamps contract. Decide the open
   question: is a cave *noise layer* seed-side (regenerable, not a stamp) while *digging*
   is stamps? (Recommended split — stamps stay small.)
2. Prototype one marching-cubes chunk whose density = cube-sphere shape noise (same
   `ShapeGenerator` seed path, so surfaces agree) minus replayed deform stamps. Mesh build
   async per repo law: `Awaitable` + Burst/compute, never coroutines (CLAUDE.md).
3. Wire the first `IWorldAction` implementation (`WorldActionType.TerrainDeform` already
   exists in the enum) whose Execute appends a deform stamp and triggers localized rebuild
   — undo/redo then falls out of `WorldActionManager` for free.

**You have a result when** you can dig a hole, quit, relaunch, and the rebuilt-from-stamps
mesh is bit-identical (hash the vertex buffer) to the pre-quit mesh — and deleting the
derived cache changes nothing. That's the falsifiable core of "stamps are the source of
truth"; visuals come later.

## 4. CPU-queryable ocean swell — buoyancy, swimming, boats

**Status caution:** the *geometry* half of this problem landed in the **uncommitted
working tree** (as of 2026-07-06): `Ocean.shader` now radially displaces the existing
ocean mesh in `vert` via `ComputeOceanSwell` (lines ~739-809) — exactly the settled
direction ("displace the existing spherical mesh, NOT a camera-following patch"; the
camera-patch approach was tried 2026-05-28 and rejected because a camera-following disc
slides relative to the water). Do not re-litigate that decision.

**Why SOTA doesn't transfer.** FFT ocean (the AAA default) produces a heightfield you
can't cheaply query on CPU at an arbitrary point, and it's planar — this ocean is a sphere
with per-vertex water data (depth/shore/body in vertex color) gating the swell. Generic
buoyancy assets assume `y = waterHeight(x, z)`; here "up" is radial and height is a sum of
analytic waves over planet-tangent axes.

**Our asset.** The swell is already **analytic and deterministic**: `ComputeOceanSwell`
sums three `EvaluateSurfaceWave` terms over axes from `BuildPlanetWaveAxes`, driven by
uniforms (`_SwellAmplitude`, `_SwellWavelength`, `_WaveSpeed`, wind). An analytic sum can
be mirrored exactly in C# — that was the stated reason for choosing Gerstner-style waves
over FFT in the first place.

**First three steps.**
1. Port `ComputeOceanSwell` + `EvaluateSurfaceWave` + `BuildPlanetWaveAxes` +
   `EvaluateSwellGating` to a plain C# service (services over MonoBehaviours), fed by the
   same DTO/globals values the shader gets. **Touch no shader code** — `Ocean.shader` is
   the caustics don't-touch zone; a CPU mirror needs zero shader edits.
2. Add a console command on that service (e.g. `water.swell-at-camera`) printing predicted
   swell height at the camera's radial foot point.
3. Prove agreement: the `WaveSwell` debug view already surfaces vertex-evaluated
   `swellHeight` (`Ocean.shader:87`; mode constant in
   `Assets/Scripts/Core/Services/DebugModeConstants.cs`). Capture it with the CPU
   prediction in the console at N points and compare.

**You have a result when** CPU-predicted swell height matches the shader's
vertex-evaluated `swellHeight` within a stated tolerance (predict the tolerance *before*
measuring, per pp-research-methodology) at ≥5 sample points across open ocean, pond, and
shore-fade zones. Fails if wind/time parameters drift between the two evaluations — that
failure is the finding.

## 5. Seam-free planet-scale derived bakes (chunk biome seam)

**Why SOTA doesn't transfer.** Texture-space dilation/padding — the standard atlas-seam
fix — doesn't apply: the seam is not a UV-border artifact but a **data visibility**
problem. `BiomeMapBaker.SampleTopKPerTexel`
(`Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs:129`, called at :63) runs a 5×5 top-K
kernel over a per-chunk biome-id grid; at a chunk edge the kernel can only look inward, so
two chunks sharing a border compute different top-K blends → faint color seam. Mitigated
2026-05-31 (edge-replication sampling), not eliminated; Bryan accepted it as "pretty good
for now."

This generalizes: **any** derived per-chunk bake (grass far-field blanket coverage,
future per-biome surface textures, deform-stamp caches from problem 3) will hit the same
class of seam. Solving it once, as a pattern, is the frontier item.

**Our asset.** The ground-truth fields are directly evaluable outside chunk bounds:
`TemperatureProvider` (`Assets/Scripts/Planet/Biomes/TemperatureProvider.cs:26`),
`MoistureProvider` (`MoistureProvider.cs:23`), and `ShapeGenerator` are deterministic
functions of direction+seed — the bake just doesn't call them today (it samples the
chunk's vertex grid, which stops at UV [0,1]).

**First three steps** (the designed fix, from the 2026-05-31 investigation — unimplemented):
1. Extend the high-res biome-id grid by `KernelRadius` cells per side, filling the apron
   by direct provider evaluation instead of vertex-grid sampling (≈10% bake overhead,
   132²/128² grid). All four neighbors then agree in the shared border region by
   construction.
2. Cheap fallback if provider plumbing is heavy: bilinear-sample the parent chunk's
   `CpuBiomeData` (covers a larger region) for out-of-bounds cells.
3. Verify with the F10 `BiomeMapFlatColor` capture at the exact border Bryan flagged
   2026-05-31, before/after.

**You have a result when** a pixel-diff strip along a known chunk border in the
`BiomeMapFlatColor` capture shows no discontinuity above noise floor — and, the stronger
claim, when the same apron pattern applied to a *second* bake domain (e.g. the grass
blanket coverage, if problem 2 lands option (a)) kills its border artifact too.

## 6. Terrain relief believability (the "normal-mapping flat" problem)

**Why this is a research item, not a bug.** The pipeline is proven working: 16/16 normal
texture slots load from source, debug modes show real per-pixel normal perturbation, a
`ScaleTangentNormal` z-collapse bug was found and fixed — and terrain **still looks flat**
to Bryan's eye in normal play (several cycles, 2026-05-31, accepted-and-parked). The
leading untested hypothesis is **lighting-range compression**: the custom analytic-sun
terrain lighting maps diffuse into a narrow band —
`Assets/Graphics/Shaders/PlanetVertexColor.shader:1124` as of 2026-07-06:
`dayLight = lerp(0.24, 1.12, terrainDiffuse) * lerp(0.36, 1.0, ao) * reliefShadow` — so
even large normal-driven `terrainDiffuse` swings become small brightness deltas. (The
historical memory records the lerp as `0.34, 1.08`; the working tree has already widened
it to `0.24, 1.12` and added a `reliefShadow` term — re-check the line before working.
This fact's ledger home is pp-failure-archaeology entry 10.)
Generic advice ("crank normal strength" — `_BiomeNormalStrength` exists, default 2.0, and
was already tried) doesn't fix a response-curve ceiling.

**Our asset.** A capture-diff culture and debug modes that isolate each stage
(`DEBUG_TERRAIN_SURFACE_NORMAL` mode 83 shows the perturbation exists; 84/85 show AO/
roughness content), so the remaining unknown is narrowly "does the lighting equation
transmit it."

**First three steps** (all are *experiments* — visual constants are gated,
pp-change-control):
1. Control test that bypasses source-texture subtlety: temporarily substitute a
   procedurally obvious normal map (sine bumps) into `_BiomeNormalArray` and capture — if
   terrain STILL reads flat, source textures are exonerated and the lighting equation is
   convicted. Binary, cheap, no tuning.
2. Check for a hemispheric/ambient term that brightens independently of the perturbed
   normal (the parked investigation's own first instruction); if found, scale it by
   `dot(geomN, sunDir)` in an experiment branch and capture.
3. Only then, one deliberate response-curve widening pass on the `dayLight` lerp endpoints
   with before/after F10 captures for Bryan.

**You have a result when** the sine-bump control test produces visibly carved terrain in
a capture at normal play altitude (hypothesis: lighting transmits ⇒ source maps too
subtle) or visibly does NOT (hypothesis: compression ⇒ fix the curve). Either outcome is
a result; "tweaked values, looks maybe better" is not.

## 7. Character controller on a procedurally deformed cube-sphere

**Why SOTA doesn't transfer.** Off-the-shelf character controllers (Unity's
`CharacterController`, most asset-store 3rd-person kits) hard-assume world-space +Y up and
static colliders. Here gravity is radial (`docs/phases/09-phase10-character.md`: align
"up" to the away-from-center normal, ground-detect by raycast toward planet center), the
walkable surface is chunked meshes that **regenerate under the player's feet** when
deformation (problem 3) lands, and the camera must survive terrain popping between LODs.
Valheim-style building (Phase 12) then needs stable ground truth for foundations on that
same moving substrate.

**Verified gap:** grep for `CharacterController|PlayerController|ThirdPerson` in
`Assets/Scripts` returns nothing (2026-07-06). What exists is a free camera
(`IFreeCameraService`, `ICameraRigContext`, `CameraTeleportStore`).

**Our asset.** The world is already *reactive to an actor* in ways most projects bolt on
later: `GrassInteractorRegistry` (`Assets/Scripts/Planet/Grass/GrassInteractorRegistry.cs`)
bends grass around registered interactors; the stamp system paints path wear where
something travels (`SurfacePathMousePainter` proves the brush loop end-to-end with the
mouse). A character drops into both as just another client — walking through a field
leaves bent grass and, if wired to path stamps, a worn trail. That feedback loop is the
visual-experience differentiator, and it's nearly free here.

**First three steps.**
1. Plain-class `SphericalGravityService` + a capsule MonoBehaviour (Unity messages justify
   the MB per CLAUDE.md): radial gravity, up-alignment slerp, camera-relative WASD on the
   tangent plane. Walk on the *current* chunked terrain via mesh colliders on
   `PlanetChunk` meshes — deformation-proofing comes with problem 3, not before.
2. Register the character in `GrassInteractorRegistry` and emit soft-disc path stamps
   through `SurfaceEditController.TryPaintBrush` on sustained walking — reuse, don't fork,
   the mouse painter's parameters.
3. 3rd-person camera as a mode beside the existing free camera behind `ICameraRigContext`
   so every debug/capture surface keeps working (the F10 pipeline reads camera context —
   don't break the evidence machine).

**You have a result when** the character can walk a complete great-circle loop of the
planet without falling through a chunk boundary or losing up-alignment (log the max
surface-normal error along the loop), and an F10 capture behind the character shows bent
grass + a worn trail. Loop completion is binary; the trail is the vision moment.

## 8. Persistent, replicable planet state (candidate — farthest out)

**Why SOTA doesn't transfer.** Voxel/survival multiplayer (Phase 14 targets Mirror,
server-authoritative, per `docs/phases/13-phase14-multiplayer.md`) normally streams bulk
chunk data because the world state *is* the chunk data. Bandwidth and save size scale with
world edits' spatial footprint, not their count.

**Our asset.** This project's entire mutable world is converging on three tiny,
deterministic inputs: **seed** (regenerates all terrain/biomes/weather seeds), **stamp
list** (all surface edits — and, if problem 3 holds the line, all deformation), and
**typed settings overrides** (`WorldSettingsOverride<TDto>` via `WorldLoadRequest`, per
the CLAUDE.md ServiceLocator rules). A joining client needs kilobytes, not chunk streams.
`IWorldAction.Serialize()/Deserialize()` was designed for the RPC path from day one
(`Assets/Scripts/Core/Interfaces/IWorldAction.cs` — `FUTURE` comment, zero
implementations). This only works if determinism actually holds — that's the research
question, and it's testable long before any networking exists.

**First three steps** (no Mirror, no networking — determinism first):
1. Determinism audit (findings doc): enumerate every world-state input that is NOT
   (seed | stamps | DTO override) — e.g. weather grid evolution time, `createdUnixSeconds`
   regrow decay in stamps — and classify each as replayable, syncable-scalar, or leak.
2. Two-process test by hand: same seed, copy `surface-edits-{seed}.json` between the two
   `persistentDataPath` folders, F10 capture the same teleport pose
   (`CameraTeleportStore`) in both; pixel-diff terrain/paths.
3. Route one edit path through `WorldActionManager.ExecuteAsync` end-to-end (problem 3's
   step 3 is the natural candidate) so the action-log-as-replication-substrate claim gets
   its first real datapoint.

**You have a result when** two independently launched instances given identical
(seed, stamp file, overrides) produce pixel-identical terrain and path-wear captures at
the same pose — and you can name, in a findings doc, every input that had to be excluded
to get there. The exclusion list IS the multiplayer design input.

---

## When NOT to use this

- **Executing the live cloud/grass visual migration** (gloom unification, Beer-Powder,
  clump identity, far-field option a/b/c mechanics) → **pp-visual-migration-campaign**.
  Problem 2 here is only the frontier framing around that campaign's decision.
- **How to take a hunch to an accepted result** (evidence bar, predict-first, refutation)
  → **pp-research-methodology**. This skill supplies the hunches; that one supplies the
  process.
- **How the current systems work** (cloud march, weather channels, grass pipeline) →
  **pp-gpu-rendering-reference** / **pp-weather-sim-reference**.
- **What went wrong before** (water saga, biome stripes, reverted temporal accumulation)
  → **pp-failure-archaeology**.
- **Whether a change is even allowed** (caustics, visual tuning gates, audit workflow) →
  **pp-change-control**.

## Provenance and maintenance

All statuses date-stamped 2026-07-06, branch `code-refactor`, dirty working tree (normal).
Re-verify before acting (git-bash, repo root):

| Claim | Re-check |
|---|---|
| Cloud march start/step logic, inside-shell handling | `grep -n "_CloudInnerRadius\|_CloudViewSteps" Assets/Graphics/Shaders/Cloud.shader` |
| Temporal accumulation reverted | preamble of `docs/research/2026-07-04-cloud-visual-research.md` |
| Grass 144/200 m distances | `grep -n "NearFieldFullDensityDistance\|NearFieldDrawDistance" Assets/Scripts/Core/QualityController.cs` |
| Grass layers still disabled | `grep -n "_chunkGrassEnabled\|_grassBlanketEnabled" Assets/Scripts/Planet/PlanetGrassCoordinator.cs` |
| Grass far-field still an open decision | Phase 3 tracker in `docs/design/2026-07-04-grass-visual-migration-plan.md` |
| Stamp fields / rebuild contract | `grep -n "class SurfaceEditStamp" Assets/Scripts/Planet/Surface/SurfaceEditController.cs`; `grep -n "RebuildPathWearFromStamps" Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs` |
| Ocean swell landed in vert | `grep -n "ComputeOceanSwell" Assets/Graphics/Shaders/Ocean.shader` |
| IWorldAction still unimplemented | `grep -rn "IWorldAction" Assets/Scripts --include=*.cs` (implementations beyond the interface/manager?) |
| Biome seam kernel | `grep -n "SampleTopKPerTexel" Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs` |
| Terrain lighting curve current values | `grep -n "dayLight = lerp" Assets/Graphics/Shaders/PlanetVertexColor.shader` |
| No character code yet | `grep -rn "CharacterController\|PlayerController\|ThirdPerson" Assets/Scripts --include=*.cs` |
| Quadtree still pre-mesh-jobs | header comment of `Assets/Scripts/Planet/Surface/TerrainQuadtree.cs` |
| Phase dependency graph | `docs/PROJECT_PLAN.md` |

If a re-check contradicts this file, the repo wins — update the problem's status line and
keep the framing only if the gap still exists.
