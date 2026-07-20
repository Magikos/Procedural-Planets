---
name: pp-debugging-playbook
description: Use when a ProceduralPlanets artifact needs its owning stage found — shoreline lines, underwater edge bleed, washed transparent water, cloud cube-face seams or wedges, cloud grain/banding, rain clouds not darkening, sparse grass, grass layer seams, biome stripes, chunk-boundary color seams, flat-looking terrain, boot-order failures, or repeated F10 capture rounds with no progress. Not for capture-tooling mechanics — see pp-diagnostics-and-tooling; settled history — see pp-failure-archaeology.
---

# ProceduralPlanets Debugging Playbook

How to find the stage that owns an artifact before changing anything, per domain. This
project's costliest failures were all diagnosis failures, not fix failures: weeks were lost
tuning constants downstream of a broken upstream stage. This playbook exists so that never
repeats.

Jargon used throughout:

- **F10 capture set** — a named list of debug visualization modes. `debug.capture-set
  "<Set Name>"` selects it in the in-game console; pressing F10 (or running `debug.capture`)
  screenshots each mode in the set into `local-only/debug-screenshots`, each PNG paired with
  a `.txt` **sidecar** of runtime metadata (FPS, quality tier, per-domain counters).
- **Debug mode** — a shader/system visualization (e.g. `CloudWeather`, `VolumeOnly`)
  selected by `debug.mode <name>` or a domain command like `cloud.debug-mode <enum>`.
- **Stage ownership** — which pipeline stage the artifact first appears in:
  **sim/source data → sampling → rendering/lighting → composite/post**.
- **Proof mode** — a binary/extreme debug mode (forced hot-pink, forced opacity, a disabled
  pass) whose only job is to make one hypothesis unmistakably true or false.

Mechanics of the capture pipeline, counters, and mode catalog live in
pp-diagnostics-and-tooling. This skill is about *choosing what to look at and in what order*.

---

## 1. The method: prove stage ownership FIRST

Every rendering artifact travels a pipeline. Find the **earliest** stage where it is
visible, and fix only that stage. Never tune stage N while stage N−1 is unproven.

1. **Reproduce from the viewpoint that best exposes the artifact.** Bryan's workflow is to
   rerun targeted captures from that exact viewpoint; the modes that still light up decide
   the next branch — not theory.
2. **Select the domain capture set** (table below) and run one F10 round.
3. **Walk the captures upstream-first.** If the artifact is already visible in the most
   upstream mode (e.g. `CloudWeather`, `WaterData`), STOP — the owner is the data/sim
   stage. Everything downstream is just faithfully rendering a broken input.
4. **Only if upstream is clean**, move one stage downstream and repeat.
5. **Fix the owning stage, re-capture from the same viewpoint, compare.** A fix that only
   improves proof modes but leaves the normal `Off` image unchanged has not fixed anything.

Domain → capture-set routing (set names verified 2026-07-06; the full catalog with
members and registration lines lives in pp-diagnostics-and-tooling §2):

| Domain | Capture set name |
|---|---|
| Water artifacts | `Water Artifact` |
| Clouds/weather | `Cloud Diagnostics` |
| Grass | `Grass` (the boot default set), `Grass Visual` |
| Biome | `Biome` |
| Terrain | `Terrain Geography`, `Terrain Textures` |
| Perf isolation | `Performance Baseline`, `Performance Cloud Steps`, `Performance Water Isolation`, etc. |
| Single mode / everything | `Current Mode Only`, `Full Loop` |

Prefer the targeted set over `Full Loop` — the full loop produces too many screenshots to
review (this is an explicit Bryan preference).

## 2. Binary/extreme proof modes before value tuning

Bryan's stated rule, verbatim from the water saga: **"hard diagnostic lines: isolate the
cause first, then fix that cause."** Concretely:

- Prefer a test that is **impossible to misread**: force a region hot pink
  (`TerrainSourcePink`, mode 31), force full opacity, bypass a pass entirely (`WaterOff`,
  mode 26; `AtmosphereBypass`, mode 40), render one layer alone (`VolumeOnly` 24 /
  `SurfaceOnly` 25 / `BottomDistortionOnly` 61).
- Design the extreme test so that **if the suspected branch is responsible, the artifact
  must visibly move**. If it doesn't move, leave that branch quickly — that's a result,
  not a failure.
- A small constants tweak is never a diagnostic. If you're changing a value to "see if it
  helps," you have skipped isolation.
- Only after the owner is proven do you tune values — and visual tuning is itself gated
  (capture-diff required, Bryan's eyes lock the look; see pp-change-control).

## 3. Symptom → triage tables

All mode names/IDs verified against `Assets/Scripts/Core/Services/DebugModeConstants.cs`
and the module registrations, 2026-07-06.

### Water

Pipeline: mesh water metadata (`WaterMeshBuilder`, vertex colors R=depth01 G=shore01
B=body01) → `WaterData` sampling → surface (`Ocean.shader`) + volume
(`WaterVolume.shader`) → composite/post. **Caustics are don't-touch** — findings against
them are flag-only.

| Symptom | First check | Then | Owner if it lights up |
|---|---|---|---|
| Thin shoreline line | `VolumeOnly` (24) vs `SurfaceOnly` (25) vs `WaterOff` (26) | Line survives `WaterOff` → not water at all; investigate terrain/other rendering | Whichever isolated layer still shows it |
| Line tracks the exact shoreline | `FoamParts` (18), `SurfaceAlpha` (19) | `TerrainSourcePink` (31) vs `FoamPink` (32): pink marks the contour → terrain source color bleeding through the volume composite, not foam | Surface foam/alpha vs source-color bleed |
| Hard-edged cutout in glint/effects | `WaterData` (11) | Same cutout in `WaterData` → upstream mesh metadata (`WaterMeshBuilder`), stop tuning glint | Mesh water-data stage |
| Low-horizon far-shore contour (camera near surface, looking along curvature) | `SeaRay` (35), `SeaVsMesh` (36), `SeaPath` (37) | `SeaRay` lights the contour but `SeaVsMesh`/`SeaPath` stay weak → analytic/raster coverage gate too strict | Analytic sea-path coverage, not foam/matte |
| Final `Off` looks like a washed transparent sheet while proof modes look rich | `WaterNoPost` (56), `SurfaceRawOpaque` (53), `SurfaceFxProof` (57) | If proof modes show real color/detail, the failure is **composite/presentation**, not effect generation. Pivot rule below. | Final stack/composite |
| Square/straight shoreline shapes | `TerrainFaceId` (34) | Face-shaped → cube-face or per-face water classification boundary | Face classification, not shading |
| Underwater shoreline gap / through-planet water | Sidecar `VolumeLipMesh: active=…` line | Lip prepass must be gated to camera-inside-water only; never a global `ZTest Always` lip pass (that regression shipped once) | Lip prepass gating |

**Washed-sheet pivot rule** (this ended the water saga): when `Off` stays washed-out while
`WaterNoPost`/`SurfaceRawOpaque`/`SurfaceFxProof` prove the ingredients exist, stop ALL
alpha/foam/glint/wave tuning. Rebuild layer-by-layer, each layer proven **in the normal
`Off` view** before the next is added: bottom distortion (`BottomDistortionOnly`, 61) →
tint/depth body → surface normals/ripples → foam/wakes → glint last. A layer visible only
in its proof mode is not done.

### Clouds / weather

Pipeline: weather grid sim (`SphericalWeatherGrid.cs`, `WeatherEvolution.compute`) →
cube-face sampling (`Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl`,
`WeatherCubeFace.hlsl`) → raymarch/lighting (`Cloud.shader`) → shadows/gloom coupling
(`CloudShadows.hlsl`). Console: `cloud.debug-mode <View>` (enum in
`Assets/Scripts/Planet/Clouds/CloudDebugState.cs`; as of 2026-07-06 it reaches
`WeatherPrecipitationSignal = 9` — the 2026-07-03 audit A3 gap where the enum stopped at 8
has been fixed).

| Symptom | First check | Then | Owner if it lights up |
|---|---|---|---|
| Sharp diagonal / face-shaped cloud seam | `CloudWeather` (mode 1) in `Cloud Diagnostics` | Seam in `CloudWeather` → weather field / cube-face sampling. Compare `CubeFaceToUnitSphere(face, uv)` against every `CubeFaceUv(direction)` (shader AND CPU query path) for inverse mismatch | Weather sampling, NOT lighting (this exact diagnosis won in 2026-05; see traps) |
| Seam absent in `CloudWeather`, present in `CloudDensity` (3) / `CloudOpticalDepth` (4) / `Off` | Raymarch/density/lighting stage | Density threshold, march, lighting | Downstream cloud rendering |
| Grain / banding in clouds | Sidecar `Raymarch: viewSteps=…, lightSteps=…, jitter=…` line | `quality.cloud-steps` multiplier (0.33–1); `Performance Cloud Steps` capture set compares 72×8/48×8/72×4/48×4 step combos | Step count vs jitter tradeoff — tuning territory, capture-diff required |
| Rain clouds don't look darker / sky vs ground gloom mismatch | Both paths must call the shared gloom: `WeatherCloudGloomFromRain`/`WeatherCloudGloom` in `WeatherSampling.hlsl:47-55` (used by `Cloud.shader:388` and `CloudShadows.hlsl:58`, as of 2026-07-06; formula home: pp-weather-sim-reference) | If they've diverged again, that's a regression of audit finding A2 (two formulas drifted while a comment claimed they matched) | Gloom-term drift between sky and shadow paths |
| Precipitation signal looks wrong | `CloudPrecipitationSignal` (8, storm-gated) vs `WeatherPrecipitationSignal` (9, raw field) | 9 shows the field, 8 shows the gate — comparing them splits "sim wrong" from "gate wrong" | Weather dynamics vs storm gating |

### Grass

Three layers exist; as of 2026-07-06 only near-field is live:
`PlanetGrassCoordinator.cs:18,21` has `_chunkGrassEnabled = false` and
`_grassBlanketEnabled = false` (current value: see pp-settings-and-flags; the
blanket/biome-stripe fight ended with layers disabled
and `PlanetVertexColor.shader` reverted — see pp-failure-archaeology). Runtime toggles:
`grass.status`, `grass.enabled`, `grass.layer <Near|Chunk|Blanket> <bool>`,
`grass.debug-layer-colors` (blanket red, chunk blue, near green).

| Symptom | First check | Then | Owner if it lights up |
|---|---|---|---|
| Sparse coverage | Sidecar rejection counters, NOT density constants. Near-field: `Cull: candidates=…, density=…, water=…, slope=…, distance=…, faceArea=…, rangeBudget=…, overflow=…` (`GrassDebugModule.cs:209`). Chunk layer: `CullLanes:`/`CullBlades:` lines (`:166-167`) | Most candidates die in one gate → fix that gate. Many instances emitted yet still reads thin → representation problem: pivot to tuft/cross-card/cluster work (`grass.render-mode`), not raw count | Gating vs representation — the counters split it |
| Grass at planet center / origin | Overflow or fallback-texture sampling | Overflow rollback exists in both computes as of 2026-07-06 (`BiomeGrassPlace.compute:327` rolls back `_GrassDrawArgs[1]`); 1×1 fallback radius texture OOB reads were audit finding A5 | Compute placement edge cases |
| Layer seams / double density at handoff | `grass.debug-layer-colors` to color each layer | Check fade bands where layers overlap | Layer fade/handoff, not placement |
| Biome stripes in grass color | Biome set: `BiomeMapPrimaryId` (78), `BiomeMapBlend` (79) | Stripes in the biome map → upstream biome data; grass is innocent | Biome bake, not grass shading |
| Scale/placement doubt | Sidecar `Markers: … status=mesh-visible-terrain` + `MarkerProjection: meshHits=…, fallbacks=…` (`ScaleReferenceDebugModule.cs:30`) | If these still validate, placement is a **closed gate** — do not reopen without regression evidence | — |

### Terrain / biome

| Symptom | First check | Then | Owner |
|---|---|---|---|
| Faint chunk-boundary color seams | `BiomeMapFlatColor` (80) | Known accepted issue: `BiomeMapBaker.SampleTopKPerTexel` runs a 5×5 kernel per texel that cannot see across chunk bounds → neighboring chunks blend different top-K sets at the shared edge. Mitigated 2026-05-31 by edge-replication sampling; true fix is extending the id grid by kernel radius via direct noise evaluation | Biome bake kernel — don't chase it in shaders |
| Terrain looks flat despite normal maps | `TerrainSurfaceNormal` (83), `TerrainSurfaceAo` (84), `TerrainSurfaceRoughness` (85) | Mode 83 showing vivid perturbation proves the data pipeline works; the flatness lives in lighting-range compression in the `dayLight` lerp of `PlanetVertexColor.shader` (current endpoints and history: pp-failure-archaeology entry 10 — the tree has already widened them once). Open issue; don't re-audit the texture pipeline | Lighting response curve, not the normal data |
| Face-shaped terrain/water classification edges | `TerrainFaceId` (34) | Cube-face boundary logic | Face topology code |

### Init / boot

| Symptom | Check | Rule |
|---|---|---|
| Service missing at startup, exception from `ServiceLocator.Get<>` | Is the consumer inside the init graph? `Get<>` is only for must-exist services; optional deps use `TryGet` + null-tolerant update loop. Precedent: `CloudController.Initialize` used to hard-`Get` a service from `Start()` (audit A4); as of 2026-07-06 it's `TryGet` (`CloudController.cs:150`) | Ordering belongs in `IEarlyInitialize`/`ILateInitialize` via `LoadingManager`, never `[DefaultExecutionOrder]` or `Start()`-order luck |
| Works in one boot, dead the next | Boot-order coupling: something in `Start()`/`OnEnable()` assumes another system registered first | Move the dependency into the init-phase system or resolve lazily |
| World service stale after regeneration | Service retained across `WorldReadyEvent` | Never retain world services across world replacement; re-resolve |

### Console

| Symptom | Check |
|---|---|
| Command unknown | `help` lists all commands; commands are `prefix.name` (20 prefixes). Domain commands live on the owning service |
| Command "succeeded" but nothing changed | Console command errors never reach the logger (audit G17, open as of 2026-07-03) — read the console output itself, and confirm the setter marks its dirty flag so the shader-global upload actually happens |
| Capture set won't select | `debug.capture-set` with the exact registered name, quoted if it contains a space: `debug.capture-set "Water Artifact"` |

## 4. The traps — each cost real time

| Trap | The story | The rule |
|---|---|---|
| **Knob-twiddling without isolation** | The water saga (2026-05): dozens of F10 rounds tuning foam, alpha, matte, and dilation constants against a shoreline line whose owner was never proven. Each tweak changed debug values; the `Off` image never moved. It ended only when binary splits (`VolumeOnly`/`SurfaceOnly`/`WaterOff`) and hot-pink proof modes (`TerrainSourcePink`) identified terrain source-color bleed — then the fix was small | No constant changes until a binary test has named the owning stage |
| **Tuning downstream of a broken upstream** | The cloud cube-face seam (2026-05-31): the instinct was density/lighting tuning. `CloudWeather` mode showed the seam already in the weather field. First fix attempt (border-texel snapping in `WeatherEvolution.compute`) was valid but partial; the root cause was `CubeFaceUv(direction)` not being the inverse of `CubeFaceToUnitSphere(face, uv)` — faces flipped/rotated during sampling. Fix aligned cube-face UV across shader sampling, evolution, cloud shadows, and the CPU query path | Weather-field-first diagnosis won. Check the most upstream mode before touching anything downstream |
| **Trusting `dotnet build` as visual proof** | Repeated incident class: builds and script reloads pass, conclusion "fixed" — then Unity reimport/regeneration shows nothing changed, or startup "improvements" that no fresh play-mode run ever confirmed | Build success is code-health only. Unity import + play mode + fresh capture decides. Evidence standards: pp-validation-and-evidence |
| **Parallel csproj build collision** | `ProceduralPlanets.Core.csproj` and `.Planet.csproj` built in parallel collide on a shared intermediate DLL and fail spuriously | Build serially; rerun serially before calling it a regression. Details: pp-build-and-env |
| **Re-opening a validated upstream gate** | Grass (2026-06): after marker projection was fixed, the instinct on every sparse-grass complaint was to re-suspect placement. But sidecars still said `status=mesh-visible-terrain`, `MarkerProjection: meshHits=5, fallbacks=0` — placement was proven. Time went to placement anyway before the real branch (rejection counters → representation) | If the latest sidecars validate a gate, do not reopen it without regression evidence. Move to the next failing gate |
| **Polishing an unproven composite** | The "washed transparent sheet": effects existed in every proof mode, the final image stayed wrong, and polish continued to compound on a broken stack until Bryan called "start over" into the layer-first rebuild | When proof modes and `Off` disagree, the composite is the suspect — stop polishing |
| **Global fix for a local problem** | Underwater lip: a `ZTest Always` lip pass fixed the underwater shoreline gap and created through-planet artifacts above water | Gate experimental passes to the exact condition they serve (camera inside water mesh) |
| **Fighting a fight that's been settled** | Biome-stripe/grass-blanket: ended with the blanket layer disabled and `PlanetVertexColor.shader` reverted. Re-deriving that outcome wastes a session | Check pp-failure-archaeology before starting any investigation that smells familiar |

## 5. STOP rules

Stop and re-plan when any of these fire:

1. **Two consecutive F10 rounds with no visible change in the artifact** → you are tuning,
   not diagnosing. Design one binary isolation step (forced color, forced opacity, disabled
   pass) that must move the artifact if your current hypothesis is right.
2. **Your fix improves proof modes but not `Off`** → composite/presentation owns it; stop
   working the layer you're on.
3. **You're about to edit a stage whose upstream mode you haven't captured** → capture it
   first.
4. **You're about to change a constant Bryan hand-tuned, or judge a visual result by your
   own eye** → gated; see pp-change-control. Bryan's eyes lock looks.
5. **You're mid-audit** → audits are findings-only until Bryan marks decisions. No fixing.
6. **The artifact is in caustics** → flag-only, don't touch (`Ocean.shader` caustics rule).

## 6. Picking the discriminating experiment

The next capture should be the single observation that best **splits the hypothesis space**,
not the one that best confirms your favorite theory.

1. List the live hypotheses as pipeline stages (data / sampling / lighting / composite).
2. Pick the mode or forced test whose outcome differs maximally between them:
   - `CloudWeather` clean vs dirty splits "sim/sampling" from "everything downstream" in one image.
   - `TerrainSourcePink` vs `FoamPink` splits "source bleed" from "foam" in one image.
   - `WaterOff` still showing the line eliminates the entire water system in one image.
   - Grass counters split "gating" from "representation" in one sidecar line.
   - Mode 8 vs mode 9 splits "precipitation field wrong" from "storm gate wrong".
3. Predict the outcome for each hypothesis **before** capturing. If two hypotheses predict
   the same image, the experiment is weak — find a sharper one.
4. An experiment that can only confirm (never refute) is not an experiment. Prefer the test
   you expect to fail.

Worked examples with numbers live in pp-proof-and-analysis-toolkit; the research-grade
version of predict-first is pp-research-methodology.

## When NOT to use this

- **How to run captures, read sidecars, list every debug mode, frame timing, graphify
  queries** → pp-diagnostics-and-tooling (measurement mechanics; this skill assumes them).
- **Launching play mode, console anatomy, where files land** → pp-run-and-operate.
- **The full history of a past investigation** (water saga blow-by-blow, blanket fight,
  caustics incident) → pp-failure-archaeology. This skill embeds only the lesson.
- **What counts as evidence / before-after protocol** → pp-validation-and-evidence.
- **First-principles analysis recipes and method theory** → pp-proof-and-analysis-toolkit.
- **Whether you're allowed to change what you found** → pp-change-control.
- **Build/environment failures** (csproj, asmdef, Unity version) → pp-build-and-env.
- **Cloud/grass visual migration decisions** → pp-visual-migration-campaign.

## Provenance and maintenance

Facts verified against the working tree on branch `code-refactor` (dirty, on top of
`ec0b1cd`) on 2026-07-06. Sources mined: `.agent-memory/codex/MEMORY.md` +
`memory_summary.md` (water saga, cloud seam, evidence-led routing),
`.agent-memory/codex/skills/proceduralplanets-water-artifact-debug/SKILL.md` (absorbed,
mode names re-verified), `.agent-memory/claude/project_chunk_biome_seam.md` and
`project_normal_mapping_flat.md`, `docs/audit/2026-07-03-grass-cloud-line-audit.md`.
`.agent-memory/` paths are background only; every load-bearing fact is restated above.

Re-verify before trusting (git-bash, repo root):

```bash
# Water mode names/IDs (VolumeOnly=24 … BottomDistortionOnly=61)
grep -n "VolumeOnly\|TerrainSourcePink\|BottomDistortionOnly" Assets/Scripts/Core/Services/DebugModeConstants.cs
# Capture set names
grep -rn "RegisterCaptureSet\|RegisterDefaultCaptureSet" Assets/Scripts/Core/Services --include=*.cs | grep '"'
# Cloud debug enum reaches WeatherPrecipitationSignal = 9 (A3 fixed)
grep -n "WeatherPrecipitationSignal" Assets/Scripts/Planet/Clouds/CloudDebugState.cs
# Gloom unification still shared (A2 fixed)
grep -rn "WeatherCloudGloom" Assets/Graphics/Shaders
# Grass overflow rollback still present (A1 fixed)
grep -n "0xFFFFFFFFu" Assets/Resources/BiomeGrassPlace.compute
# CloudController resolves weather via TryGet (A4 fixed)
grep -n "TryGet(out _weather)" Assets/Scripts/Planet/Clouds/CloudController.cs
# Grass layer flags (near-field only live)
grep -n "_chunkGrassEnabled\|_grassBlanketEnabled" Assets/Scripts/Planet/PlanetGrassCoordinator.cs
# Rejection-counter sidecar lines
grep -n "CullLanes\|CullBlades\|candidates=" Assets/Scripts/Core/Services/GrassDebugModule.cs
# Console commands cited
grep -rn 'ConsoleCommand("capture-set"\|ConsoleCommand("mode"\|ConsoleCommand("debug-mode"' Assets/Scripts
```

If any A-series re-check fails, the audit fix regressed — treat as a finding, not something
to silently re-fix (change control applies).
