---
name: pp-diagnostics-and-tooling
description: Use when you need to MEASURE instead of eyeball — pick a debug mode or F10 capture set, read or diff a capture .txt sidecar, interpret grass Cull/Draw rejection counters, frame timing avg/p95, overflow stats, the F6 HUD, generation timings, or graphify queries. Not for symptom→stage triage — see pp-debugging-playbook; what counts as proof — pp-validation-and-evidence; launching play mode / run requests — pp-run-and-operate.
---

# Diagnostics and tooling: measure, don't eyeball

Verified against code on branch `code-refactor`, 2026-07-06. Repo root:
`c:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets`. All paths below are repo-relative.

Every subsystem in this project ships instrumented: shader debug views, per-mode capture
sets, GPU rejection counters, a rolling frame timer, and a metadata sidecar written next to
every screenshot. When a question sounds like "does it look better / is it faster / is grass
sparser" the answer is a **number or an isolation image**, never an impression. This skill is
the catalog: what each instrument shows, how to read it, and the exact command to drive it.

Jargon (defined once):

- **Debug mode** — a named visualization the renderer switches into (e.g. `VolumeOnly`
  renders only the water volume pass). Set via `debug.mode <name>` in the console.
  Registered modes live in a central `DebugRegistry`; each has a global id like `water.24`.
- **F10 capture set** — a named list of debug modes. Pressing F10 in play mode (or running
  `debug.capture`) walks the list, screenshotting each mode to a PNG + `.txt` **sidecar**
  in `local-only/debug-screenshots/`. `debug.capture-set "<Name>"` selects the set; F7 or
  `debug.cycle-capture-set` cycles.
- **Sidecar** — the `.txt` metadata file written next to each capture PNG
  (`DebugCaptureMetadataBuilder` + each module's `AppendMetadata`). Contains camera pose,
  sun state, and per-domain stat blocks. It is the primary machine-readable evidence.
- **Timed capture set** — a capture set with `TimingSamplesPerMode > 0`; per mode it resets
  `FrameTimingCounters` and waits for N sample frames before screenshotting, so the sidecar
  frame-timing block reflects *that mode only*.

Console commands that drive everything here (owner: `Assets/Scripts/Core/Services/DebugCaptureController.cs`):

| Command | Effect | Source |
|---|---|---|
| `debug.mode <name>` | Set active debug visualization by registered name (tab-completes) | `DebugCaptureController.cs:290` |
| `debug.capture-set "<Set Name>"` | Select the F10 capture set by display name | `DebugCaptureController.cs:305` |
| `debug.capture` | Trigger the F10 sequence now (closes console during shots, reopens after) | `DebugCaptureController.cs:317` |
| `debug.cycle-capture-set` | Advance to next set (F7 equivalent) | `DebugCaptureController.cs:283` |
| `debug.overlay <bool>` | F6 debug HUD on/off | `DebugCaptureController.cs:252` |
| `debug.detailed-debug <bool>` | F9 detailed HUD section on/off | `DebugCaptureController.cs:260` |
| `debug.profiling` | Toggle high-FPS profiling frame-rate target (F11) | `DebugCaptureController.cs:268` |

Hotkeys: F6=debug HUD, F7=cycle F10 set, F8=freeze sun, F9=detailed HUD, F10=capture,
F11=FPS cap. **P is NOT precipitation** — the detailed-HUD hint "P=Precip"
(`DebugOverlayHud.cs:71`) is stale: `TogglePrecipitation` has no keyboard binding
(`InputMapService.cs:104`) and P toggles the path paint brush
(`SurfacePathMousePainter.cs:102`), which paints wear into the world. Use the console
command `debug.precipitation`; full gotcha in pp-run-and-operate.

Capture files land flat in `local-only/debug-screenshots/` as
`F10-<modeId>-<modeName>-<yyyyMMdd-HHmmss-fff>.png/.txt` (console-script runs insert a
script prefix after `F10-`). Retention is automatic: the directory is pruned to
`MaxCaptureRuns(=6) × modes-in-current-set × 2` files (`DebugCapturePipeline.cs:31,270-272`)
— old evidence disappears, so harvest sidecars you care about promptly.

## Fast path

- Latest capture bundle: run
  `.agent-skills/pp-diagnostics-and-tooling/scripts/Get-LatestCaptureBundle.ps1 -Detail`.
- Before/after sidecars: run
  `.agent-skills/pp-diagnostics-and-tooling/scripts/Compare-CaptureSidecars.ps1 <before.txt> <after.txt>`.
- Debug catalog drift: run
  `.agent-skills/pp-diagnostics-and-tooling/scripts/Find-DebugModes.ps1`.
- If a symptom is not yet stage-owned, switch to `pp-debugging-playbook` before tuning.

---

## 1. Debug-mode catalogs and interpretation

The point of a debug mode is a **binary conclusion**: artifact visible here ⇒ owner is X;
not visible ⇒ eliminate X. Route conclusions through pp-debugging-playbook's stage-ownership
method; the tables below tell you what each instrument isolates.

### 1a. Water / terrain-composite modes (the big set)

Int values live in `Assets/Scripts/Core/Services/DebugModeConstants.cs` and drive the
`_OceanDebugMode` shader global; display names are registered in
`Assets/Scripts/Core/Services/WaterDebugRegistration.cs`. 100+ modes as of 2026-07-06; run
`scripts/Find-DebugModes.ps1 -Section mode-constants` for the authoritative live list.
Key isolation modes and how to read them:

| Mode (id) | What it isolates | If artifact visible here → conclusion |
|---|---|---|
| `Off` (0) | Production render, no debug | Baseline — every comparison starts and ends here |
| `VolumeOnly` (24) | Water volume pass alone | Artifact lives in `WaterVolume.shader` path |
| `SurfaceOnly` (25) | Water surface pass alone | Artifact is surface-local (Ocean.shader surface) |
| `WaterOff` (26) | All water rendering disabled | Artifact is NOT water — stop touching water shaders |
| `TerrainSourcePink` (31) | Terrain source color under water painted hot pink | Pink at the artifact ⇒ terrain/source color bleeding through the volume composite (not foam) |
| `FoamPink` (32) | Foam contribution painted pink | Pink at the artifact ⇒ foam owns it |
| `TerrainFaceId` (34) | Cube-face id per terrain pixel | Straight/square artifact edges aligned with face boundaries ⇒ per-face classification problem |
| `SeaRay` (35) | Analytic sea-sphere ray hit | Lights a low-horizon contour ⇒ curved sea-path/analytic coverage branch, not foam or mesh overlap |
| `SurfaceRawOpaque` (53) | Surface color forced opaque, no blend | Rich here but washed in `Off` ⇒ composite/final-stack presentation failure, effects ARE generated |
| `WaterNoPost` (56) | Water without post/composite stages | Same logic: separates generation from presentation |
| `SurfaceFxProof` (57) | Surface effects (foam/wakes/glint) proof view | Effects present here, absent in `Off` ⇒ integration failure of that layer |
| `BottomDistortionOnly` (61) | Only shallow-water bottom distortion layer | First layer of the layer-first water rebuild; must be visible in normal view before adding layers above it |
| `AtmosphereBypass` (40) | Skip atmosphere post over water | Artifact disappears ⇒ atmosphere composite owns it |
| `VolumeMask` (14) / `VolumeBoundary` (20) | Effective volume coverage / boundary | Coverage doesn't match visible water ⇒ mask too strict/loose near shore |
| `FoamParts` (18) / `SurfaceAlpha` (19) | Foam components / surface alpha | Tracks the artifact exactly at shoreline ⇒ surface foam/alpha problem |
| `CausticsOnly` (58) etc. | Caustics isolation | **Look, don't touch** — caustics are a no-edit zone (CLAUDE.md "Don't touch") |

The same int enum also carries biome (73-80, plus `GrassLodCoverage` 86 /
`BiomeAltitudeCooling` 87), terrain-texture (81-85 and 91-96; 88-90 in between are water
temperature/freeze/ice modes), and performance-stage modes (97-105) — one shared id space,
registered by `BiomeDebugModule.cs`, `TerrainDebugModule.cs`, and `WaterDebugRegistration.cs`.

- **Biome modes** (`BiomePrimaryId`, `BiomeTemperature`, `BiomeMoisture`, `BiomeLatitude`,
  `BiomeElevationBand`, `BiomeAltitudeCooling`, `BiomeMapPrimaryId`, `BiomeMapBlend`,
  `BiomeMapFlatColor`, `GrassLodCoverage`, `TerrainSelectedAlbedo`, `TerrainSunLighting`,
  `TerrainSurfaceNormal`, `TerrainSurfaceAO`, `TerrainSurfaceRoughness`): each renders one
  input channel of the biome/terrain shading stack. Stripes visible in `BiomeMapPrimaryId`
  but not `BiomeTemperature`/`BiomeMoisture` ⇒ map bake, not climate inputs. Flat-looking
  terrain with a healthy `TerrainSurfaceNormal` ⇒ lighting compression downstream, not data.
- **Terrain geography modes** (`TerrainCoastMask`, `TerrainSlopeMask`, `TerrainSnowMask`,
  `TerrainOverrideComposite`, `TerrainPrimaryAlbedo`, `TerrainMixedAlbedo`): mask-vs-albedo
  split. Wrong texture in `TerrainMixedAlbedo` but correct masks ⇒ blend stage owns it.

### 1b. Cloud modes

Two surfaces control the same `_CloudDebugMode` shader global: `debug.mode CloudWeather`
(registry path, `CloudDebugModule.cs:42-51`) and `cloud.debug-mode <View>` (direct enum,
`CloudController.cs:349`). Enum: `CloudDebugState.View`
(`Assets/Scripts/Planet/Clouds/CloudDebugState.cs`), shader branches at
`Assets/Graphics/Shaders/Cloud.shader:433-457`.

| View (int) | Renders | If wrong here → conclusion |
|---|---|---|
| `Off` (0) | Production clouds | Baseline |
| `Weather` (1) | Weather-grid cloud coverage, dark-blue→cyan ramp | Coverage wrong at grid level ⇒ weather sim/SphericalWeatherGrid, not raymarch |
| `Storm` (2) | Storm intensity, blue→red | Storm channel of the weather grid |
| `Density` (3) | Sampled cloud density, black→white | Coverage OK (view 1) but density wrong ⇒ noise/threshold shaping in the cloud sampler |
| `OpticalDepth` (4) | Accumulated optical depth (1−transmittance), blue→gold | Density OK but final look wrong ⇒ march accumulation/lighting, not sampling |
| `SilverLining` (5) | Silver-lining term | Rim-lighting term isolation |
| `MoistureSource` (6) | Moisture-source channel, brown→cyan | Evolution input: where moisture is fed |
| `CondensationChange` (7) | Condensation sign, red=drying green=condensing; tune with `cloud.debug-threshold` / `cloud.debug-saturation` (`CloudController.cs:358,367`) | Evolution dynamics visualization |
| `CloudPrecipitationSignal` (8) | Rain signal derived by the cloud shader | Signal 8 present but 9 absent (or vice versa) ⇒ mismatch between cloud-derived and weather-grid rain — the clouds-vs-rain coupling contract (see pp-weather-sim-reference) |
| `WeatherPrecipitationSignal` (9) | Rain signal sampled straight from the weather grid | Ground truth for the rain channel |

Cube-face seams: a seam visible in `Weather` (1) is a weather-grid cube-face UV problem;
a seam only in `Density`/`OpticalDepth` is raymarch/sampling. History in pp-failure-archaeology.

### 1c. Grass diagnostics

Grass has no shader debug-mode enum of its own; it uses **proof toggles + GPU counters**.
As of 2026-07-03 the chunk and blanket layers are disabled (`_chunkGrassEnabled = false`,
`_grassBlanketEnabled = false`); near-field is the live layer.

Console proof toggles (owners: `GrassDebugModule.cs` `[CommandPrefix("grass")]`,
`PlanetGrassCoordinator.cs`):

| Command | Use |
|---|---|
| `grass.status` | Master + per-layer requested/active state in one line |
| `grass.layer <Near\|Chunk\|Blanket> [bool]` | Enable/disable one layer — the binary isolation tool |
| `grass.debug-tint 1` + `grass.debug-tint-color 1,0,1` | Paint every rendered blade — proves whether blades exist where you're looking |
| `grass.debug-layer-colors true` | Blanket=red, chunk=blue, near=green — attributes a blade to its layer |
| `grass.render-mode <Physical\|Hybrid\|Cluster>` | Switch blade geometry representation without rebuild |
| `grass.overlay-status` | Print far-overlay strength/brightness and whether the terrain material actually has the properties (`PlanetGrassCoordinator.cs:341`) |
| `grass.overlay-strength <0-1>` / `grass.surface-brightness <0.3-1.5>` | Far-overlay coverage / painted-surface brightness (visual-tuning gated — see pp-change-control) |

There is **no `grass.force-density` command** (verified 2026-07-06 by grepping
`force-density` across Assets — zero hits); density questions are answered by the counters
below plus `quality.set`.

**Rejection counters** (the actual measurement): both grass compute placements count every
candidate and every rejection reason into a GPU stats buffer, surfaced per F10 sidecar.

Near-field (`Assets/Resources/GrassNearFieldPlace.compute:63-73`, 11 slots
`NF_STAT_CANDIDATE_CELLS`(0) … `NF_STAT_RANGE_BUDGET_REJECTED`(10)) prints as
(`GrassDebugModule.cs:204-209`):

```
Draw: emitted=93794, visualBlades=1406910, bladesPerInstance=15, vertexCount=54, capacity=1000000, buffer=45.8 MB
Cull: candidates=3526848, density=201646, water=0, slope=0, distance=1078234, distanceFade=212576, frustum=0, faceArea=1940598, rangeBudget=0, overflow=0
```

Read it as a funnel: `candidates − (density + water + slope + distance + distanceFade +
frustum + faceArea + rangeBudget + overflow) ≈ emitted`. "Grass is sparse" becomes "which
rejection bucket grew?": `density` ⇒ biome/coverage input; `water`/`slope` ⇒ surface
classification; `distance*` ⇒ LOD ranges; `faceArea` ⇒ cube-face page bounds; `overflow` > 0
or `emitted` == `capacity` ⇒ buffer exhausted — raise capacity or expect missing blades.

Chunk grass (`Assets/Resources/BiomeGrassPlace.compute:17-32`, 16 slots including
`STAT_OVERFLOW_REJECTED_BLADES`=14) prints as `CullLanes:` / `CullBlades:` lines
(`GrassDebugModule.cs:166-167`) with the same funnel logic at lane then blade granularity;
`CullBlades: overflow=` is the chunk-side buffer-exhaustion signal.

Also in the sidecar: `--- GrassNearField ---` `Grid:` line shows `reason=` (why the last
placement dispatch ran: `FaceChanged`, `PageChanged`, …) and `dispatchesTotal` — a runaway
`dispatchesTotal` between two captures means placement is re-dispatching every frame.

### 1d. Atmosphere and precipitation

- Atmosphere **modes** live on the water mode list (`AtmosphereBypass`,
  `VolumeAfterAtmosphere`, `AtmosphereWaterCut`, `AtmosphereContribution` — ids 40-44).
  `AtmosphereDebugModule.cs` is metadata-only: it contributes the `--- Atmosphere ---`
  sidecar block (radius/sea/densityOrigin globals, viewSteps/sunSteps, terrain aerial
  perspective distances). Artifact survives `AtmosphereBypass` ⇒ not atmosphere.
- Precipitation: `precipitation.debug-mode <Off|RainMask|RainDots|StormDots>`
  (`PrecipitationController.cs:10-16,391`). `RainMask` shows where the rain volume thinks
  it is raining; `RainDots`/`StormDots` mark rain/storm cells as dots. Rain visible in
  `WeatherPrecipitationSignal` (cloud view 9) but not `RainMask` ⇒ the precipitation
  renderer isn't consuming the grid signal. `precipitation.particle-proof <Off|Dust|Rain|...>`
  (`PrecipitationController.cs:439`) forces a local particle profile for visual validation;
  `PrecipitationContribution` (water mode 45) isolates its composite contribution.
- Weather grid ground truth: `weather.export-grid` writes a full grid JSON + raw cell CSV;
  `weather.diagnostics` writes a diagnostics file (`WeatherManager.cs:20-26`). Prefer the
  export over screen-space views when the question is "what is IN the grid".

---

## 2. Capture-set catalog

Registered via `DebugRegistry.RegisterCoreCaptureSets` (`DebugRegistry.cs:351-384`, 7 core
sets) plus per-module registrations. Full live list: `scripts/Find-DebugModes.ps1 -Section
capture-sets`. As of 2026-07-06, 27 sets. **This table is the library's catalog home** —
sibling skills keep only the sets they use and point here. The ones you'll actually reach for:

| Set (say it exactly) | Members / behavior | Registered at |
|---|---|---|
| `Cloud Diagnostics` | Off, Weather, Density, OpticalDepth, Storm, MoistureSource, CondensationChange, CloudPrecipitationSignal, WeatherPrecipitationSignal (9 shots) | `CloudDebugModule.cs:53` |
| `Water Artifact` | 18 modes: Off, VolumeOnly, SurfaceOnly, WaterOff, VolumeOcclusion, lip-pink family, TerrainSourcePink, FoamPink, atmosphere splits | `WaterDebugRegistration.cs:103` |
| `Grass` (**the boot default** — several modules call `RegisterDefaultCaptureSet` and the last registration wins, `DebugRegistry.cs:462-463`; module order Water→Biome→Terrain→Grass, `DebugCaptureController.cs:97-100`, so a bare F10 on fresh boot runs this set. Verified 2026-07-06) | Off, AtmosphereBypass, WaterOff, BiomeMapPrimaryId, BiomeMapBlend, TerrainPrimaryAlbedo, TerrainMixedAlbedo, TerrainSelectedAlbedo, GrassLodCoverage, TerrainSurfaceNormal, TerrainFaceId | `GrassDebugModule.cs:129` |
| `Grass Visual` | Off only — one production shot + full sidecar stats; cheapest before/after evidence | `GrassDebugModule.cs:141` |
| `Performance Baseline` | Off only | `DebugRegistry.cs:353` |
| `Performance Water Isolation` (timed, 60 samples/mode) | Off, AtmosphereBypass, WaterNoPost, SurfaceOnly, VolumeOnly, WaterOff | `DebugRegistry.cs:355` |
| `Performance Water Volume Stages` (timed; also suppresses weather passes) | WaterOff, CausticsOnly, BottomDistortionOnly, VolumeOptical, VolumeOnly, Off | `DebugRegistry.cs:362` |
| `Performance Weather Stages` (timed) | PerfWeatherNone/Clouds/Precipitation/Atmosphere/All, Off | `DebugRegistry.cs:369` |
| `Performance Cloud Steps` (timed) | PerfCloud72x8/48x8/72x4/48x4, Off — pipeline force-sets `_CloudViewSteps`/`_CloudLightSteps` per mode (`DebugCapturePipeline.cs:340-364`) | `DebugRegistry.cs:376` |
| `Current Mode Only` | F10 just cycles + shoots the current mode | `DebugRegistry.cs:382` |
| `Full Loop` | Every registered mode — huge; avoid (retention math scales with set size) | `DebugRegistry.cs:383` |
| Water topic sets | `Water/Atmosphere`, `Water Interface`, `Water Precipitation`, `Water Glint`, `Frozen Water`, `Water Caustics`, `Water Foam`, `Water Waves`, `Water Surface Finish`, `Water Surface Isolation`, `Water Night`, `Water Wakes`, `Water Volume Deep Dive` (40 modes) | `WaterDebugRegistration.cs:112-201` |
| `Biome`, `Terrain Geography`, `Terrain Textures` | Domain channel walks | `BiomeDebugModule.cs:81`, `TerrainDebugModule.cs:86,98` |

Timed sets freeze the sun at a fixed local time, disable vsync, raise the frame-rate
target, reset `FrameTimingCounters` per mode, and wait for 60 timing samples before the
screenshot (`DebugCapturePipeline.cs:141-176`) — their sidecar `--- Frame Timing ---` block
is a clean per-mode measurement, not a rolling mixture.

---

## 3. Sidecar anatomy and diffing

Built by `DebugCaptureMetadataBuilder.Build` (`Assets/Scripts/Core/Services/DebugCaptureMetadataBuilder.cs`):
header (`Image`, `Source`/`Saved` resolution, `Mode`, `CaptureSet`, `Time`), then
`--- Camera ---` (position, orientation, frustum planes, lat/lon, planet/sea radii),
`--- Runtime ---` (FPS, vsync, quality tier, sun frozen/direction/elevation, precipitation
and particle state, wind, camera climate), then every registered module block in
registration order: `--- Water ---`, `--- Biome Assignment ---`, `--- Terrain Geography ---`,
`--- GrassRuntime ---`, `--- Grass ---`, `--- GrassNearField ---`, `--- Atmosphere ---`,
`--- ScaleRef ---`, `--- Clouds ---`, `--- Memory ---`, `--- Frame Timing ---`,
`--- DebugConsole ---`. A module whose service is absent prints `Controller: missing` —
that line is itself evidence (e.g. `--- Grass --- Controller: missing` while chunk grass
is disabled is expected as of 2026-07-06).

To diff two sidecars, don't eyeball 128 lines — run the comparer (git-bash or PowerShell):

```
powershell -File .agent-skills/pp-diagnostics-and-tooling/scripts/Compare-CaptureSidecars.ps1 <before.txt> <after.txt>
```

It parses `Section/Key: value` maps, skips volatile keys (Image/Time/FPS/Pending; pass
`-All` to include), and token-diffs `k=v, k=v` lines. Real output (two `cloud.00-Off`
captures, 2026-07-05):

```
~ Runtime/SunElevationDeg
    A: 25.75
    B: 63.21
~ Frame Timing/Rolling
    CPU avg : 24.83 ms -> 18.95 ms
    p95 : 25.90 ms -> 18.95 ms
~ GrassNearField/Grid
    reason : PageChanged -> FaceChanged
    dispatchesTotal : 144 -> 2
16 differing key(s).
```

Rule: a before/after pair is only comparable if `Camera/Position`, `Runtime/SunDirection`,
and the weather keys do NOT appear in the diff. If they do, the captures weren't pinned —
re-run via a console script that teleports and freezes time first (see pp-run-and-operate).
`camera.teleport LastDebugCapture` restores the exact pose of the newest capture: the store
saves the pose at capture time and can also reconstruct it by parsing the newest `F10-*.txt`
sidecar (`CameraTeleportStore.cs:278-335`).

---

## 4. Frame timing: reading and before/after protocol

`FrameTimingCounters` (`Assets/Scripts/Core/Services/FrameTimingModule.cs`) instruments
exactly 5 CPU sections (`SectionCount=5`, line 50): `SurfaceVisibility` (terrain LOD/
visibility tick), `Water`, `Clouds`, `NearGrass`, `ChunkGrass` — plus whole-frame CPU/GPU
from Unity's FrameTimingManager and a derived `Uninstrumented CPU` (whole frame minus
sections). Rolling window = 120 frames (line 51). Terrain has no standalone section by
design: its CPU lives in SurfaceVisibility and its render cost is whole-frame GPU (comment,
lines 6-8).

Sidecar block format (`FrameTimingModule.AppendMetadata`, lines 285-297):

```
--- Frame Timing ---
Whole frame: CPU=18.67 ms GPU=15.62 ms
Rolling: CPU avg=18.95 ms, p95=31.60 ms, last=18.67 ms, n=120; GPU avg=15.44 ms, ...
Surface/terrain CPU: avg=0.79 ms, p95=0.94 ms, last=0.93 ms, n=120
Water CPU:          avg=0.01 ms, ...
Clouds CPU:         avg=0.01 ms, ...
Near grass CPU:     avg=0.04 ms, ...
Chunk grass CPU:    avg=0.00 ms, ...
Uninstrumented CPU: avg=18.11 ms, p95=30.82 ms, last=17.66 ms, n=120
```

How to read: **compare avg for trend, p95 for hitching**; `n<120` means the window hasn't
filled since the last reset — treat as provisional. The instrumented sections measure the
*CPU driver cost* of each subsystem; GPU-heavy features (cloud raymarch, grass draw) show
up in whole-frame GPU, which is why the per-stage performance capture sets exist.

The perf-claim PROTOCOL (predict direction first, pin pose/seed/tier, before AND after,
quote avg + p95 with `n`, fresh run for startup claims) is owned by
pp-validation-and-evidence — follow it there. The tool mechanics for executing it:

- Pick the timed set that isolates your axis: `Performance Weather Stages` for weather
  features, `Performance Cloud Steps` for step counts, `Performance Water Isolation` /
  `Performance Water Volume Stages` for water, `Performance Baseline` otherwise.
- `debug.capture-set "<set>"` → `debug.capture`, then diff the matching per-mode sidecars
  with `Compare-CaptureSidecars.ps1`. The per-mode delta between stages (e.g.
  `PerfWeatherClouds` minus `PerfWeatherNone` whole-frame GPU) is the feature's cost; the
  before/after delta of that same subtraction is your result. Deltas within ~1 ms are
  noise — say so instead of claiming a win.

One-shot generation cost (not per-frame): planet generation logs
`Generation timings: initialize=..ms, terrain=..ms, colors=..ms, climate=..ms, water=..ms,
total=..ms` at Debug level (`Assets/Scripts/Planet/Planet.cs:330-335`); the `--- Biome
Assignment ---` sidecar line carries `buildMs` for the Voronoi atlas.

---

## 5. Debug overlay HUD (F6/F9)

`DebugOverlayHud` (`Assets/Scripts/Core/Services/DebugOverlayHud.cs`) is the live-glance
surface: camera position/lat-lon, FPS, whole-frame CPU/GPU. F9 (`debug.detailed-debug true`)
expands it with the controls cheat-sheet, current capture set + mode count, and every
module's `DrawOverlay` contribution — cloud mode + weather sample under camera
(`CloudDebugModule.cs:94-117`), grass layer state + emitted counts
(`GrassDebugModule.cs:212-238`), and the full frame-timing window refreshed every 0.5 s
(`FrameTimingModule.cs:299-319`). Use the HUD to *watch* a number move while testing
interactively; use sidecars when you need the number as evidence.

---

## 6. graphify as a code diagnostic

`graphify query "<question>"` / `path "<A>" "<B>"` / `explain "<concept>"` against
`graphify-out/` answers structural questions (what calls X, how do A and B connect) with a
scoped subgraph — CLAUDE.md says to prefer it for codebase questions.

**Caveat (2026-07-06):** audit G19 (`docs/audit/2026-07-03-general-code-audit.md:357-359`)
reports `graphify query` and `graphify update` **hang with no output in this checkout** —
the graph ingested `Library/PackageCache` and `local-only/` (24,665 files), drowning
project signal. Until the excludes land: set a timeout when you try graphify, and fall back
to `Grep`/`Glob` + `graphify-out/wiki/index.md` (if present) without burning time. Re-test
before relying on it; if it works again, `graphify update .` after code changes remains the
rule.

---

## 7. Ships with this skill: scripts/

All three are read-only, PowerShell 5.1-safe, and locate the repo root relative to their
own path (no arguments needed when run from the checkout). Run via
`powershell -File .agent-skills/pp-diagnostics-and-tooling/scripts/<name>.ps1`.

### Get-LatestCaptureBundle.ps1

Clusters `F10-*.txt` sidecars by filename timestamp (default 120 s gap = one F10 run) and
prints the newest bundle(s): set, camera, sun, per-mode file list. Options: `-Bundles N`,
`-Detail` (adds frame-timing + grass draw lines per capture), `-GapSeconds`, `-CaptureDir`.
Real output (2026-07-06):

```
BUNDLE  2026-07-05 05:33:10  (9 captures)
  CaptureSet: Cloud Diagnostics (cloud.diagnostics)
  Camera:     3140.85, -3998.71, -1176.75   SunElevationDeg: 63.21   FPS: 55.3

  cloud.00     Off                          05:33:10.963  png+txt
  cloud.01     CloudWeather                 05:33:11.681  png+txt
  ...
  Sidecar of first capture: ...\F10-cloud.00-Off-20260705-053310-963.txt
```

First stop after Bryan reports "captures are in" — it tells you what you actually have
before you Read any PNG.

### Compare-CaptureSidecars.ps1

Sidecar differ described in section 3. `Compare-CaptureSidecars.ps1 <A.txt> <B.txt> [-All]`.

### Find-DebugModes.ps1

Re-enumerates this skill's catalogs from source — run it whenever code may have drifted
before trusting any table above. Sections: `mode-constants` (DebugModeConstants ints),
`mode-names` (RegisterMode call sites: what `debug.mode` accepts), `capture-sets`
(display name + kind + file:line), `cloud-views` (CloudDebugState.View), `console`
(`[CommandPrefix]`/`[ConsoleCommand]` census — 159 commands / 20 prefixes as of 2026-07-06).
`-Section all` (default) or one of the above. Sample:

```
  'Cloud Diagnostics'              [Std    ] Assets\Scripts\Core\Services\CloudDebugModule.cs:53
  'Grass'                          [Default] Assets\Scripts\Core\Services\GrassDebugModule.cs:129
```

---

## When NOT to use this

- **You have a symptom and don't know which stage owns it** — pp-debugging-playbook
  (symptom→triage tables, binary/extreme proof method). This skill assumes you know *what*
  to measure.
- **You're deciding whether evidence is sufficient to call something done/faster/fixed** —
  pp-validation-and-evidence (evidence tiers, promotion rules, the visual-tuning gate).
- **You need to get the game running, drive the camera, or phrase a run request to Bryan** —
  pp-run-and-operate (play mode, console anatomy, console scripts, where artifacts land).
- **The question is why the tooling is shaped this way** — pp-architecture-contract;
  historical investigations that produced these modes — pp-failure-archaeology.

## Provenance and maintenance

Everything above was verified against source on 2026-07-06, branch `code-refactor`.
Fastest re-verification: `powershell -File .agent-skills/pp-diagnostics-and-tooling/scripts/Find-DebugModes.ps1`
regenerates all mode/set/console catalogs from source. Targeted one-liners (git-bash):

- Retention + filename shape: `grep -n "MaxCaptureRuns\|keepFiles\|F10-" Assets/Scripts/Core/Services/DebugCapturePipeline.cs`
- Core capture sets: `grep -n "RegisterCoreCaptureSets" -A 33 Assets/Scripts/Core/Services/DebugRegistry.cs`
- Water mode ints: `grep -n "public const int" Assets/Scripts/Core/Services/DebugModeConstants.cs`
- Cloud views + shader branches: `grep -n "= [0-9]" Assets/Scripts/Planet/Clouds/CloudDebugState.cs; grep -n "_CloudDebugMode ==" Assets/Graphics/Shaders/Cloud.shader`
- Frame timing sections/window: `grep -n "SectionCount\|RollingWindowSize\|enum FrameTimingSection" -A 8 Assets/Scripts/Core/Services/FrameTimingModule.cs`
- Grass counters: `grep -n "#define NF_STAT_" Assets/Resources/GrassNearFieldPlace.compute; grep -n "#define STAT_" Assets/Resources/BiomeGrassPlace.compute`
- Grass sidecar lines: `grep -n "Draw: emitted\|Cull: candidates" Assets/Scripts/Core/Services/GrassDebugModule.cs`
- Console commands for capture: `grep -n "ConsoleCommand" Assets/Scripts/Core/Services/DebugCaptureController.cs`
- Generation timings: `grep -n "Generation timings" Assets/Scripts/Planet/Planet.cs`
- LastDebugCapture teleport: `grep -n "LastDebugCapture\|F10-\*.txt" Assets/Scripts/Core/Services/CameraTeleportStore.cs`
- graphify hang status: `grep -n "G19" docs/audit/2026-07-03-general-code-audit.md` — and just try `graphify query "test"` with a timeout.

Volatile facts date-stamped 2026-07-06 in-text: capture-set count (27), console census
(159/20), disabled grass layers, the graphify hang, and every file:line. Line numbers drift
first — prefer the grep one-liners over trusting a stale number.
