---
name: pp-failure-archaeology
description: Use before retrying any previously-attempted approach, and when a symptom looks familiar - shoreline thin line, "washed transparent sheet" water, through-planet water artifact, diagonal/cube-face cloud seam, grainy dithered clouds, biome stripes or bright wash at biome borders, dark grass ring at 200 m, jagged/blocky path edges, faint chunk-boundary color seams, terrain looks flat despite normal maps, grass visible but sparse, atmosphere clipping terrain. Also when asking "why is the grass blanket/chunk layer disabled", "was X tried before", or "can I touch caustics" (no). Not for live triage steps - see pp-debugging-playbook. Not for executing the current cloud/grass campaign - see pp-visual-migration-campaign.
---

# ProceduralPlanets Failure Archaeology

The chronicle of every major investigation, dead end, rejected fix, and revert in this
project. Each entry: **symptom → root cause → evidence → status**. Purpose: nobody
re-fights a settled battle, and nobody retries a retired approach without new evidence.

**Status legend**

| Status | Meaning |
|---|---|
| SETTLED | Root cause found, fix landed and validated |
| RETIRED | Approach rejected. Never retry without the stated reopening evidence |
| OPEN | Unsolved. Current best hypothesis recorded |

**How to use this file:** before proposing a fix, scan the ledger below. If your symptom
or your planned approach appears, read that entry first. If an entry says RETIRED, the
burden of proof is on you to produce the reopening evidence before writing code.

Jargon used throughout: **F10 capture** = pressing F10 in play mode captures each debug
mode in the active capture set as PNG + `.txt` metadata sidecar into
`local-only/debug-screenshots` (select sets via console `debug.capture-set "<Name>"`).
**Sidecar** = that metadata text file. **Off** = the normal production view (debug mode 0).

## Fast path

Search this file for the symptom, failed approach, or file you are about to touch. If the
matching entry is `SETTLED` or `RETIRED`, do not retry it without the entry's reopening
evidence. If no entry matches, return to `pp-debugging-playbook` and prove stage ownership.
Read the matching entry first; the full chronicle is optional.

## Ledger

| # | Battle | Status | Date range |
|---|---|---|---|
| 1 | Water artifact saga (shoreline lines, source bleed, washed sheet) | SETTLED (several sub-branches RETIRED) | 2026-05-20 → 05-26 |
| 2 | Underwater lip prepass / through-planet regression | SETTLED + standing prohibition | 2026-05-22 |
| 3 | Cloud cube-face UV seam | SETTLED | 2026-05-31 |
| 4 | Cloud temporal-accumulation experiment | RETIRED | 2026-07-01 → 07-03 |
| 5 | Biome-stripe / grass-blanket fight | Root cause SETTLED; blanket layer parked, re-land is an OPEN decision | 2026-07-01 → 07-03 |
| 6 | Caustics breakage | Standing prohibition (don't touch) | pre-2026-05 |
| 7 | Atmosphere radius + star-sphere revert | SETTLED | 2026-05-01 |
| 8 | Grass LOD G-series (partial ship + reverts) | SETTLED with reverts recorded; G6 OPEN | 2026-07-01 → 07-02 |
| 9 | Chunk biome seam (top-K blend) | OPEN (mitigated, accepted for now) | 2026-05-31 |
| 10 | Normal-mapping-flat terrain | OPEN | 2026-05-31 |
| 11 | Path wear representation + jagged hard-disc edges | SETTLED (two theories RETIRED) | 2026-06-30 → 07-01 |
| 12 | Grass scale/density routing | SETTLED gate | 2026-06-02 |

---

## 1. The water artifact saga — SETTLED (costliest battle in the project)

**Symptoms (a family, all on the water render path):** a thin shoreline-like line where
water meets terrain; a near-surface silhouette at grazing angles; a low-horizon far-shore
contour visible only from a camera near the water surface looking along the planet's
curve; terrain source color bleeding through the water composite; and finally a
production `Off` view that read as a **"washed transparent sheet"** even while debug proof
modes showed convincing raw surface effects.

**Why it was expensive:** weeks of knob-twiddling on foam/alpha/glint/matte constants
before isolating which subsystem owned each artifact. The project's core debugging
doctrine ("hard isolation before tuning", binary/extreme proof modes, stage ownership)
was forged here. See pp-debugging-playbook for the doctrine itself.

**The chain of root causes and pivots (each step evidence-led):**

1. **Binary isolation split the render path.** Debug modes `VolumeOnly` (24, only
   `WaterVolume.shader`), `SurfaceOnly` (25, only `Ocean.shader`), `WaterOff` (26)
   proved the shoreline line lived in the full-screen **water-volume composite/prepass**
   path: it survived in `Off` and `VolumeOnly`, vanished in `SurfaceOnly`/`WaterOff`.
2. **Refraction theory RETIRED.** `VolumeNoRefraction` (29) looked identical to
   `VolumeOnly` → refraction was not the cause. Reopen only if those two modes diverge.
3. **Terrain source-color bleed confirmed.** `TerrainSourcePink` (31) turned the contour
   hot pink while `FoamPink` (32) did not → the "shoreline foam" theory died; the visible
   line was terrain color already rendered behind the composite. Fix levers landed in
   `WaterVolume.shader`: `sourceOcclusion`, `sourcePathOcclusion`, `sourceMatte`,
   `brightSourceBleed`.
4. **Strict interior mask RETIRED.** A hard `volumeInteriorMask` fixed underwater edge
   bleed but caused an above-water **"sheet/shelf" regression** (only the top surface
   colored). Kept instead: the softer `volumeWaterMask = waterMask * volumeEdgeMask *
   volumeBodyMask`. Never reintroduce a hard interior mask near shore.
5. **Cube-face continuity was NOT the low-horizon root cause.** `TerrainFaceId` (34) and
   a rebuilt global direction-space water graph in `WaterMeshBuilder` (regenerated mesh
   vert count 219,813 → 217,960 proved the patch was active) did not remove the
   low-horizon line. Shoreline overlap policy set here: slight under-terrain push
   (~`shoreRange * 0.22`, clamped) is **accepted** when terrain depth hides it.
6. **Analytic sea-path branch + the stop rule.** `SeaRay` (35) / `SeaVsMesh` (36) /
   `SeaPath` (37) / `SeaMatte` (38) / `SeaSourceMatte` (39) probed the grazing-angle
   path/depth model. Late conclusion, a stop rule not a recipe: **if `SeaSourceMatte`
   lights the contour region but `Off`/`VolumeOnly` keep the visible line, stop stacking
   matte/opacity/transmittance tweaks in `WaterVolume.shader` and pivot to
   coverage/geometry** (screen-space horizon occluder, analytic sea-sphere coverage,
   mesh/prepass overlap).
7. **The "washed transparent sheet" endgame → layer-first rebuild.** 2026-05-24: proof
   modes (`WaterNoPost`, `SurfaceOnly`, `SurfaceRawOpaque`, `SurfaceFxProof`) showed the
   shader *generates* good effects while `Off` stayed washed out → the failure was in
   final composite/presentation, not effect generation. Bryan chose to **start over as a
   layer-by-layer rebuild**: bottom distortion (`BottomDistortionOnly`) first, then base
   tint/depth transparency, then surface normals/ripples, then foam/shore wash/wakes,
   glint last — each layer must be *unmistakable in normal `Off` view* before adding the
   next. RETIRED: resuming the abandoned all-at-once tuning loop.
8. **Side discovery:** the cloud quality tier was misclassified — `QualityLevel: 0 (PC)`
   was treated as "low" by index. Fixed by classifying quality by *name* in
   `QualityController`; sidecars then reported `CloudQuality: tier=High`.

Follow-on (2026-05-26): a hard-edged cutout in glint was traced upstream via `WaterData`
debug mode to mesh-provided vertex-color metadata (`R=depth01, G=shore01, B=body01`) —
i.e. `WaterMeshBuilder`, not glint. Same lesson: prove stage ownership before polish.

**Status: SETTLED** as doctrine plus the fixes above. Water debug modes and the
`WaterArtifact` capture set still exist (`Assets/Scripts/Core/Services/WaterDebugRegistration.cs`).

## 2. Underwater lip prepass — SETTLED, with a standing prohibition

**Symptom:** underwater shoreline gaps (water volume missing at the wet/dry boundary
when the camera is submerged).

**Approach:** `WaterMeshBuilder` generates a separate `WaterVolumeLip` mesh along
shoreline edges; `WaterVolumeRenderFeature` can draw it into `_WaterInterfaceTexture`
via a relaxed `WaterVolumeLipPrepass`.

**The regression:** F10 evidence (2026-05-22, runs `181748`/`181812`/`181843`) proved
that drawing the lip prepass **globally with `ZTest Always`** creates a new above-water
**through-planet artifact** — water visible through the entire planet when the camera is
above sea level.

**The fix (verify it's still in place before any lip work):** the relaxed lip pass draws
only when the camera is inside the water mesh —
`Assets/Scripts/Planet/WaterVolumeRenderFeature.cs:87`:
`bool drawRelaxedVolumeLip = renderableVolumeLipMesh != null && IsCameraInsideWaterMesh(...)`.
`WaterVolumePrepass.shader` still contains `ZTest Always` passes (lines ~149, ~164) —
they are safe *only* behind that gate.

**Rule: never re-enable a global always-depth lip pass.** Reopen evidence: none — the
through-planet failure is geometric, not tunable. Also settled here: earlier underwater
*glow* artifacts were precipitation/debug ownership problems, not water; light shafts are
an atmosphere camera effect that fades at the water surface.

**Status: SETTLED.** (Whether the lip fully closes the underwater gap remained under
validation at handoff; the prohibition is permanent regardless.)

## 3. Cloud cube-face UV seam — SETTLED (2026-05-31)

**Symptom:** sharp diagonal / cube-face-shaped seams ("wedges") in the cloud layer.

**Diagnosis method (the reusable part):** F10 `Cloud Diagnostics` set — the seam was
already visible in the **`CloudWeather`** debug mode, proving it was owned by the weather
field, not cloud density/lighting/composite. The same wedge propagated into
`CloudDensity`, `CloudOpticalDepth`, and `Off`.

**First attempt (partial):** edge-snapping border texels in `WeatherEvolution.compute` —
valid but the wedge persisted in `CloudWeather`.

**Root cause:** inverse mismatch — weather generation used `CubeFaceToUnitSphere(face, uv)`
but the shader-side `CubeFaceUv(direction)` was *not its inverse* (several faces flipped
or rotated during sampling).

**Fix:** align cube-face UV orientation across all four sampling paths:
`Includes/WeatherSampling.hlsl`, `WeatherEvolution.compute`, `Includes/CloudShadows.hlsl`,
and the CPU query in `Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs`. Bryan
visually circled the planet with no seam found. Later hardened structurally: the helpers
were extracted to a single `Assets/Graphics/Shaders/Includes/WeatherCubeFace.hlsl`
included by both WeatherSampling and CloudShadows (refactor item WEATHER-16), so the four
copies can no longer drift.

**If a seam returns:** `CloudWeather` shows it → weather cube-face sampling/evolution;
`CloudWeather` clean but `CloudDensity`/`Off` show it → downstream (density, raymarch,
lighting). Do not tune lighting for a seam you haven't stage-owned.

**Status: SETTLED.**

## 4. Cloud temporal-accumulation experiment — RETIRED (2026-07-03)

**Context:** the 2026-07-01 cloud-rain audit (W1) diagnosed the grainy "dithered" cloud
look: full-strength white-noise jitter at every march level, no resolve, frozen speckle.
Its fix menu explicitly warned that temporal sub-pixel rotation **without a TAA/history
resolve turns static grain into crawling grain**.

**What happened:** a temporal-accumulation experiment was built anyway, then **reverted
by Bryan before the 2026-07-03 line audit**. Per that audit's preamble
(`docs/audit/2026-07-22-consolidated-code-audit.md`, prior-audit reconciliation): the
single-pass march was
kept; the **pass-ordering and per-step jitter changes were retained**. Also skipped in
the same wave: `_CloudMaxDistance` march cap — measured irrelevant here because the max
shell chord is ≈3.7 km on this planet, so a 30 km-style cap never engages.

**What replaced it:** the blue-noise ray offset (cloud migration plan Phase 1, shipped —
`Assets/Graphics/Shaders/BlueNoise.png` bound as the ray-offset texture; A/B captures
`20260705-051115/051118`, Bryan saw no odd behavior).

**Reopen evidence:** a full accumulation design — history buffer with depth-aware
reprojection/rejection (e.g. paired with the half-res render + bilateral upsample option
from audit W1) and a before/after capture set. A bare frame-index jitter rotation is not
a reopening; it is the exact thing that failed.

**Status: RETIRED.**

## 5. The biome-stripe / grass-blanket fight — root cause SETTLED; re-land OPEN

**Symptoms:** visible stripes and crisp painted borders at biome boundaries in the
far-grass terrain paint ("blanket"); a **bright wash along biome-blend borders**; a
brightness ring/halo where painted ground met 3D blades; painted overlay glowing at
partial coverage.

**The fight (2026-07-01 → 07-02):** the G-series grass-LOD fixes (entry 8) were
implemented with Bryan's approval, then two were rolled back on in-game evidence:

- **G3 reverted in full (2026-07-02).** `grassCoverage = smoothstep(0.05, 0.55, farWeight)`
  in `PlanetVertexColor.shader` re-sharpens every soft edge (biome borders become crisp
  painted lines) — the diagnosis was correct, but widening the remap to `0.95` left a
  **bright wash along biome-blend borders**, and the linear orbit attenuation weakened
  the blanket from altitude. Codex's pre-implementation caution ("measure before baking,
  it reduces peak coverage wherever farWeight rarely reaches 1") was confirmed exactly.
  The original coverage formula stands.
- **G2's overlay brightness reverted** to the hand-tuned absolute color (0.46 through the
  shared 0.76 canopy scale): the brighter shared-canopy paint **glowed as a halo at
  partial coverage**. The residual 200 m brightness mismatch is a *lighting-model*
  difference (wrap-diffuse blades vs terrain N·L), not an albedo constant — no single
  brightness value can hide it at all sun angles; tune live via `grass.surface-brightness`.

**How it ended (as of 2026-07-03, verified in code):** the blanket layer is off —
`_grassBlanketEnabled = false` (`Assets/Scripts/Planet/PlanetGrassCoordinator.cs:21`),
the chunk grass layer is off (`_chunkGrassEnabled = false`, line 18; current values: see
pp-settings-and-flags), and
`PlanetVertexColor.shader` was **reverted wholesale to HEAD**. The visible result: dense
near-field blades to 200 m, then bare terrain.

**Crucially, the stripe root cause WAS found before the wholesale revert:** a
linear-coverage + toe-cut formulation in `PlanetVertexColor.shader`, validated during the
strip-probe sessions — it was reverted *with* the blanket, not because it failed. But the
**exact reverted code is not in the working tree**: it must be re-derived from the
probe-session evidence (UNVERIFIED which commit, if any, holds it — the campaign's
grass-phases.md carries the same caveat). What survives is the diagnosis and the
regression harness: console script `script.run "Grass Edge Strip Probe"`
(`Assets/Resources/ConsoleScripts/Grass Edge Strip Probe.txt`).

**Status:** stripes root-caused (SETTLED as diagnosis; the fix code must be re-derived);
the blanket itself is parked pending the
far-field decision — option (a) in `docs/design/2026-07-04-grass-visual-migration-plan.md`
Phase 3 is "re-land the blanket with the linear-coverage fix." **Do not re-enable the
blanket flag ad hoc**; re-landing goes through that plan's gate with strip-probe captures
at the two worst biome borders (Grassland/Desert, Savanna/Forest). Bryan names this fight
one of the three costliest — the cost was tuning visual constants without a capture-diff
harness at biome borders. Related earlier lesson (commit `a37390b`): the far overlay's
distance weights never drove the surface albedo — `ApplyGrassSurfaceAlbedo` is
distance-independent; only `envCoverage * strength` plus grading/brightness set the
ground look; knobs added for overlay-start/end and noise/fiber were removed as inert.

## 6. Caustics — standing prohibition

**The rule (CLAUDE.md "Don't touch"):** caustics in `Assets/Graphics/Shaders/Ocean.shader`
and related code **look correct; every touch breaks them**. Audit findings against
caustics are flag-only — no code changes. Every audit since honors this (e.g. the
2026-07-01 grass-LOD audit lists caustics under "Scope not audited"; the 2026-07-03 line
audit closes with "Caustics untouched").

**Origin:** repeated breakage during the water arc whenever caustics code was edited;
Bryan names it one of the three costliest failures and the origin of the don't-touch
rule. UNVERIFIED: no committed record of the specific breaking edits survives in the repo
— which is itself the reason the rule is absolute rather than conditional.

**Status: prohibition, permanent.** There is no reopening evidence defined. If a caustics
defect is ever found, the finding is flagged to Bryan, never fixed in-line.

## 7. Atmosphere radius + star-sphere revert — SETTLED (commit `55814e3`, 2026-05-01)

**Symptom:** broken atmosphere rendering after an experiment; stars misbehaving with the
camera far plane.

**What was reverted:** `AtmosphereController.OnPlanetGenerated` had been changed to
derive `_planetRadius` from `PlanetSettings.PlanetRadius * (1 + ElevationMin)` (the
lowest terrain point, found via `FindAnyObjectByType<Planet>()`), with the shader cutoff
at `_planetRadius * 0.99`. Commit `55814e3` reverted to the simple, correct form:
`_planetRadius = evt.PlanetRadius` from `PlanetGeneratedEvent`, cutoff
`_planetRadius - 5f` ("slightly below planet surface so atmosphere doesn't clip through
terrain"). Same commit fixed stars: star sphere reduced to **8× planet radius** and
camera far clip raised to **100k** so the sphere renders.

**Lesson:** atmosphere geometry derives from the generation event's radius, not from
terrain-elevation-derived estimates; and scene-scale changes (star sphere) must be
checked against camera clip planes. Verify with `git show --stat 55814e3`.

**Status: SETTLED.**

## 8. Grass LOD G-series — SETTLED with reverts recorded; G6 OPEN

The former 2026-07-01 grass-LOD audit (reconciled in
`docs/audit/2026-07-22-consolidated-code-audit.md`) diagnosed the
"sharp visible lines" in the near→mid→far grass handoff. Status per finding (from the
audit's own status line plus the 07-04 plan, which calls the series "partially
shipped/reverted"):

| Finding | What it was | Outcome |
|---|---|---|
| G1 | 144-200 m band faded 3× at once (density thin × 9-level Bayer dither × width shrink + albedo darken) → concentric banding + dark ring | LANDED: fades made alpha/dither-only via interleaved gradient noise (IGN); `GrassDither.hlsl` |
| G2 | Blade canopy color vs painted overlay built from different constants, lit by different models → brightness step at 200 m that moves with the sun | LANDED (shared `GrassCanopyAlbedo` in `GrassColor.hlsl`) **except** overlay brightness, reverted to hand-tuned — see entry 5 |
| G3 | `smoothstep(0.05, 0.55, farWeight)` re-sharpens soft edges | LANDED then **FULLY REVERTED 2026-07-02** — see entry 5 |
| G4/G5 | Nine transition constants in six files, several stale (tuned for a ~400-500 m draw later cut to 200 m without retune) | LANDED: distances live in `IGrassQualitySettings` — per Codex amendment there is **no separate `GrassLodProfile`**; do not create a second distance authority |
| G6 | Chunk (mid) grass layer: dead weight — delete or promote | **DELIBERATELY NOT EXECUTED — Bryan's call, still open.** ~1,050 lines ship disabled; also the C2/C3 dead-path findings in the 07-03 audit are tied to this decision |
| G7 | Near-field pops in/out whole at the 350/500 m altitude gate (48 MB realloc) | LANDED as altitude fade, but only **after** `_GrassChunkFade` semantics changed to pure alpha (Codex caution: reusing it as-was would have shrunk+darkened grass during climb) |
| PERF-1/2 | Per-frame service/settings resolution; per-frame constant material writes | LANDED |

**Lessons encoded:** (a) implement through the existing quality-settings authority, never
a parallel constants home; (b) shader knobs that carry geometry/albedo side effects can't
be repurposed for a new fade without changing their semantics first; (c) audit fixes that
touch tuned visuals get in-game verification and may be reverted — that is the process
working, not failing. Record reverts in the audit status line (as done here).

## 9. Chunk biome seam — OPEN (mitigated, accepted 2026-05-31)

**Symptom:** faint chunk-boundary color seams on the planet surface in the top-K biome
blend bake.

**Root cause (known):** `BiomeMapBaker.SampleTopKPerTexel`
(`Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs`) runs a 5×5 kernel over a per-chunk
biome-id grid built from `chunk.CpuBiomeData`, which only covers that chunk's UV [0,1].
At a shared edge, neighbor chunks' kernels each look only inward → different top-K
distributions → faint seam. **The kernel cannot see across chunk borders.**

**Mitigation shipped:** edge-replication sampling (every texel gets the full 25 samples;
out-of-range cells replicate the nearest valid cell). Reduced, not eliminated. Bryan saw
it in an F10 `BiomeMapFlatColor` capture and accepted "pretty good for now."

**Known true fix (unimplemented):** extend the high-res biome-id grid by KernelRadius
cells per side, populated by *direct noise evaluation* (TemperatureProvider,
MoistureProvider, ShapeGenerator) instead of vertex-grid sampling — ~10% bake overhead;
all four neighbors then produce identical border IDs. Cheap alternative: bilinear-sample
the parent chunk's `CpuBiomeData` outside leaf bounds. Per-biome triplanar surface detail
may mask the residual seam entirely, in which case defer indefinitely.

**Status: OPEN** (accepted polish debt; do not re-diagnose from scratch).

## 10. Normal-mapping-flat terrain — OPEN

**Symptom:** terrain looks flat in normal play despite the triplanar normal + ARM
pipeline being wired end-to-end (shipped 2026-05-31). Several investigation cycles did
not solve it.

**Confirmed working (do not re-verify from zero):** all three texture arrays
(`_BiomeAlbedoArray`/`_BiomeNormalArray`/`_BiomeArmArray`) load 16/16 slots from source
PNGs; normal debug viz (mode 83, `(surfaceNormalWS - geometricNormal) * 20`) shows vivid
non-trivial perturbation; AO/roughness modes 84/85 show source content. A real bug was
found and fixed along the way: `ScaleTangentNormal` collapsed `tn.z` to 0 when scaled xy
exceeded unit length (flipping normals sideways) — replaced with `normalize(tn)`. Still
flat after the fix, per Bryan's eye.

**Best hypothesis (untested):** lighting-range compression in the analytic-sun `dayLight`
curve. This entry is the library's home for the current value. Historical curve (memory,
2026-05-31): `dayLight = lerp(0.34, 1.08, terrainDiffuse)`. **Current working tree
(verified 2026-07-06, `PlanetVertexColor.shader:1124`):**
`dayLight = lerp(0.24, 1.12, terrainDiffuse) * lerp(0.36, 1.0, ao) * reliefShadow` —
the endpoints have already been widened once and an AO + relief-shadow term added
(by whom/when is not recorded in the repo), yet Bryan still reads the terrain as flat.
Contributing suspects: source ground-PBR normals are subtle by design; triplanar tiling
0.065 (~15 m per tile) makes per-pixel variation sub-pixel at altitude; a hemispheric
ambient term may brighten regardless of normal.

**Next steps when reopened:** (1) check for an ambient term not scaled by
`dot(geomN, sunDir)`; (2) widen the diffuse response curve further — noting one widening
already landed without closing the issue; (3) control-test with
procedurally generated obvious-bump normals (sine/noise) to rule out subtle sources.
Bryan accepted moving past this 2026-05-31 to keep momentum.
Re-verify the live curve: `grep -n "dayLight = lerp" Assets/Graphics/Shaders/PlanetVertexColor.shader`.

**Status: OPEN.**

## 11. Path wear representation + jagged hard-disc edges — SETTLED

**What path wear IS (so nobody redesigns it):** an **R8, 128×128 per-chunk wear texture**
(`Assets/Scripts/Planet/Surface/PlanetChunk.cs:287`, `TextureFormat.R8`, linear),
**vector-baked from saved `SurfaceEditStamp` records — not a true SDF field.** Stamps are
the source of truth; the wear textures (and scorch, and future edit textures) are derived
caches rebuilt by replaying stamps. `path.*` and `scorch.*` console commands
(`Assets/Scripts/Planet/SurfacePathDebugCommands.cs`) are wrappers over the shared
`SurfaceEditController` (`Assets/Scripts/Planet/Surface/SurfaceEditController.cs`).

**RETIRED theory #1 — "blocky edges = 64px texture resolution."** The 2026-06-30
agent-conversation design round proposed a dedicated higher-res wear field on this
premise. Wrong: the grass computes already sample the mask **bilinearly**; removal was
continuous the whole time. The actual jagged-look root cause was **additive stamp
stacking within one drag** — dozens of overlapping soft discs summed to a hard 255 core
with only a thin grid-aligned feathered rim. Fixed by **max-compositing within a stroke**
(`_strokeContribution` in `ChunkedSurfaceProvider`, stroke-id grouping in the stamp
replay).

**RETIRED theory #2 — "store a true SDF and evaluate per blade per footstep."** Rejected
as unscalable and unnecessary; SDF math is used only at *write* time (brush deposit),
the field is sampled once per candidate blade.

**Jagged HARD-DISC edges specifically:** hard-disc is a deliberate stamp shape and was
kept; its aliased edge was fixed by **antialiasing the baked mask edge** —
`const float HardDiscEdgePixels = 1.25f`
(`Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:38`, applied in the
`PathWearMask` falloff at lines ~950/1004/1035/1184) — rather than removing hard-disc
from the mouse tool.

**Status: SETTLED.** Reopen the resolution question only for sub-meter *footstep* trails
below one texel — footsteps don't exist in the codebase yet.

## 12. Grass scale/density routing — SETTLED gate (2026-06-02)

**Symptom sequence:** grass markers historically landed wrong → fixed → then "grass is
visible but very sparse."

**The settled gate:** marker placement is validated when F10 sidecars report
`Markers: hasDrop=True, lastSuccess=True, status=mesh-visible-terrain, count=6` and
`MarkerProjection: meshHits=5, fallbacks=0`. **Do not reopen marker placement while those
hold.** Sparse coverage is a *density/representation* question, routed by instrumentation:

1. Read the rejection counters first (candidate cells, density-zero, biome/state-mask,
   water, slope, distance/cull, random-roll, overflow — these landed in the grass debug
   stats and were the primary tool in later strip-probe work).
2. If most candidates die in one gate → fix that gate.
3. If instances are emitted in volume but the field still reads sparse → improve *blade
   representation* (tufts / cross-card clusters), not raw count.

**RETIRED:** raising density constants blind, and re-editing marker placement on
instinct. **Status: SETTLED** as a routing rule; representation work continues in the
grass migration plan (clumps = Phase 2).

---

## Cross-cutting morals (the chronicle compressed)

1. **Isolate before tuning.** Every RETIRED entry above except #6 died to a binary/extreme
   proof mode, not to a better constant.
2. **Stage ownership first.** CloudWeather before cloud lighting; WaterData before glint;
   TerrainSourcePink before foam.
3. **Build success ≠ visual proof; proof modes ≠ production proof.** The washed-sheet
   endgame: every ingredient proved, composite still wrong (see
   pp-validation-and-evidence).
4. **Visual constants Bryan hand-tuned get reverted if a "principled" replacement looks
   worse in-game** (G2 brightness, G3 remap). Capture-diff before retune; Bryan's eyes
   lock the look (see pp-change-control).
5. **Reverts are recorded in the doc of record's status line** — that is why this
   chronicle could be written. Keep doing it (see pp-docs-and-memory).

## When NOT to use this

- **Live triage of a current artifact** — symptom→debug-mode routing tables and the
  isolation method live in **pp-debugging-playbook**. This file tells you whether the
  battle was already fought; that one tells you how to fight it.
- **Executing the current cloud/grass visual work** — the sequenced, gated campaign is
  **pp-visual-migration-campaign** (this file is its history section).
- **The rules these incidents produced** (visual-tuning gate, findings-before-fixes,
  caustics lock as policy) — **pp-change-control** owns rule + rationale; this file owns
  the incident detail.
- **How the debug modes / capture sets themselves work** — **pp-diagnostics-and-tooling**
  and **pp-run-and-operate**.
- **Open research directions** (not failed attempts, just unbuilt) — **pp-research-frontier**.

## Provenance and maintenance

Written 2026-07-06 from: `.agent-memory/codex/MEMORY.md` + `memory_summary.md` +
`raw_memories.md` (water saga, lip prepass, cloud seam, grass routing — restated above,
paths not load-bearing), `.agent-memory/claude/*.md` (chunk seam, normal-mapping,
refactor/console arcs), `docs/audit/2026-07-01-*.md` + `2026-07-03-*.md` (preambles carry
the revert records), `docs/design/2026-07-04-*-migration-plan.md`,
`docs/agent-conversation/2026-06-30-surface-path-wear-field.md`, and git history on
branch `code-refactor` at `ec0b1cd` + dirty tree.

Re-verify volatile facts before relying on them (git-bash, repo root):

```bash
# Entry 2 — lip pass still gated on camera-inside-water
grep -n "drawRelaxedVolumeLip" Assets/Scripts/Planet/WaterVolumeRenderFeature.cs
# Entry 5 — blanket/chunk layers still disabled
grep -n "_grassBlanketEnabled\|_chunkGrassEnabled" Assets/Scripts/Planet/PlanetGrassCoordinator.cs
# Entry 7 — atmosphere revert commit
git show --stat 55814e3
# Entry 8 — G-series status line (reverts recorded in the doc of record)
grep -n "Former.*grass-lod" docs/audit/2026-07-22-consolidated-code-audit.md
# Entry 4 — temporal-accumulation revert record
grep -n "Former.*grass-cloud" docs/audit/2026-07-22-consolidated-code-audit.md
# Entry 11 — hard-disc antialias constant + R8 wear texture
grep -n "HardDiscEdgePixels" Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs
grep -n "TextureFormat.R8" Assets/Scripts/Planet/Surface/PlanetChunk.cs
# Entry 3 — cube-face helpers consolidated
ls Assets/Graphics/Shaders/Includes/WeatherCubeFace.hlsl
# Entry 5/8 — strip-probe harness still present
ls "Assets/Resources/ConsoleScripts/Grass Edge Strip Probe.txt"
```

When a battle in the ledger changes state (an OPEN closes, a RETIRED approach is reopened
with evidence, a new revert lands), update the entry and the ledger row in the same
change, and date-stamp it. New major investigations get a new numbered entry — a battle
belongs here once it has consumed more than a session or produced a revert.
