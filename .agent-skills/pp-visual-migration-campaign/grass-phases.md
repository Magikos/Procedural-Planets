# Grass phases — detailed runbook

Mirror of `docs/design/2026-07-04-grass-visual-migration-plan.md` (the doc wins on
conflict). Read SKILL.md Step 0 first — do not enter a phase without knowing the
campaign position. All facts date-stamped 2026-07-06 unless noted.

Files this campaign may touch (per the plan, nothing else): `Grass.shader`,
`GrassNearFieldPlace.compute`, `BiomeGrassPlace.compute`, `GrassNearFieldController.cs`,
`PlanetGrassCoordinator.cs`, `PlanetVertexColor.shader` (Phase 3 only), grass quality
settings in `QualityController.cs`. Caustics untouched — and note Phase 1's include
warning below.

**Execution order is NOT numeric.** Revised 2026-07-05 (recorded in the plan's
"Sequencing, risk, decision gates" section): **0 → 1 → 3 → 4 → 2 → (5 parked)**.
Far-field (3) moved ahead of clumps (2) because the 200 m edge fades to *nothing* and no
near-side tuning fixes that. This file presents phases in execution order.

The standard capture sequence for every grass gate:

```
camera.teleport <saved-name>       # same viewpoint before AND after (camera.save-teleport <name> to create)
debug.capture-set "Grass"          # the default F10 set
debug.capture                      # or press F10; writes 11 PNG+txt pairs
```

"Grass" (registered as the *default* capture set, `GrassDebugModule.cs:129-140`) contains:
Off, AtmosphereBypass, WaterOff, BiomeMapPrimaryId, BiomeMapBlend, TerrainPrimaryAlbedo,
TerrainMixedAlbedo, TerrainSelectedAlbedo, GrassLodCoverage, TerrainSurfaceNormal,
TerrainFaceId. `debug.capture-set "Grass Visual"` (`GrassDebugModule.cs:141`) is the
clean-view single shot (mode Off only).

The numeric evidence lives in each capture's `.txt` sidecar:

- `--- Frame Timing ---` (`FrameTimingModule.cs:288`) — whole-frame CPU/GPU ms plus
  per-section CPU lines including `Near grass CPU:` (`FrameTimingModule.cs:332`,
  section `FrameTimingSection.NearGrass`).
- `--- GrassNearField ---` (`GrassDebugModule.cs:192`) — the placement ground truth:
  `Draw: emitted=… capacity=… buffer=… MB` (line 205) and
  `Cull: candidates=… density=… water=… slope=… distance=… distanceFade=… frustum=…
  faceArea=… rangeBudget=… overflow=…` (line 209).
- `--- Grass ---` — chunk-layer stats (`CullBlades: … emitted=… overflow=…`,
  `GrassDebugModule.cs:167`), relevant only if Phase 3 re-enables the chunk layer.

Useful companions: `grass.status` (master + per-layer state),
`grass.layer <Near|Chunk|Blanket> [true|false]` (per-layer toggle), `grass.overlay-status`
(blanket tuning values), `grass.debug-layer-colors` (blanket red / chunk blue / near
green), `script.run "Grass Edge Strip Probe"` (the far-overlay regression harness — the
plan abbreviates this to `run "…"`, but the console command is `script.run`,
`ConsoleScriptRunner.cs:118`), `planet.seed` / `planet.generate` (fix and regenerate the
world seed for determinism checks; `seed` does NOT auto-regenerate — run `planet.generate`
to apply).

Build check after any C# change (serial — see SKILL.md):

```bash
dotnet build ProceduralPlanets.Core.csproj && dotnet build ProceduralPlanets.Planet.csproj
```

Compute/shader edits don't surface in dotnet builds at all — Unity import + play mode
decides those.

## Working-tree state (verified 2026-07-06 — re-run the Provenance greps if later)

| Marker | State |
|---|---|
| `_chunkGrassEnabled` | `false` (`PlanetGrassCoordinator.cs:18`) — chunk layer OFF (current value: see pp-settings-and-flags) |
| `_grassBlanketEnabled` | `false` (`PlanetGrassCoordinator.cs:21`) — blanket OFF; bare terrain beyond 200 m (current value: see pp-settings-and-flags) |
| A1 overflow rollback | LANDED — `InterlockedAdd(_GrassDrawArgs[1], 0xFFFFFFFFu)` at `BiomeGrassPlace.compute:327` (near-field original at `GrassNearFieldPlace.compute:422`) |
| A5 radius-sampler clamps | LANDED — `clamp(int2(floor(p)), …)` at `GrassNearFieldPlace.compute:149,229,253` and `BiomeGrassPlace.compute:84,98,119` |
| C1/C4/C5 dead deletions | LANDED — `_lastTickCamera` and `SphericalWeatherGrid.EdgeSnappedUv` grep to zero hits under `Assets/Scripts/Planet/Clouds/` (unrelated `EdgeSnappedUv` methods legitimately exist in `ClimateMapGpuData.cs` and `VoronoiBiomeField.cs` — not C4 regressions); the redundant `chunk != null` is gone; `GetUniformWorldScale` has one definition (`FaceSpaceCellRangeBuilder.cs:246`), copies deleted |
| B2 MPB const | LANDED — `props.SetFloat(ChunkFadeId, ChunkPeakCoverage)` in `GrassChunkRuntime.Create` (line 70), not per-frame |
| D1 shared include | LANDED (partial — see drift log) — `Includes/GrassPlacementCommon.hlsl` exists, `#include`d at `BiomeGrassPlace.compute:3` and `GrassNearFieldPlace.compute:18` |
| C2 suppression path | STILL PRESENT — `SuppressionRadiusFraction = 0f` (`GrassNearFieldController.cs:44`); keep-vs-delete is Bryan's open call |
| C3 frustum-cull path | STILL PRESENT — `_FrustumCullEnabled` (`BiomeGrassPlace.compute:56`), `PassesFrustum` (line 156); same open call |
| Phase 1 | NOT STARTED — `CloudShadowFactor` called at `Grass.shader:357`, inside `GrassFragment` (starts line 315) |

## Drift log (plan text vs landed code — tree wins)

- **D1 extraction is smaller than the plan's sketch.** The plan says "extract the ~150
  duplicated lines (`BlendGrassParams`, hashes, cube-face math, bilinear corner
  blending…)". What landed is a 73-line `GrassPlacementCommon.hlsl` containing
  `BiomeGrassParams` + `GrassBladeInstance` structs, `HashUint`/`Hash01`,
  `CubeFaceToUnitSphere`, `SurfaceStateReject`, and `GrassPlacementBilinearTexels`.
  `BlendGrassParams` no longer exists as a symbol; each compute keeps its own
  resource-bound `SampleGrassParamsBilinear` (`BiomeGrassPlace.compute:139`,
  `GrassNearFieldPlace.compute:269`) built on the shared texel helper, hooked through a
  `#define GRASS_PLACEMENT_PARAM_BUFFER` per compute. This satisfies the plan's own
  caveat ("keep texture/resource samplers local unless the underlying resource layout is
  unified") — do not "finish" the extraction to match the plan's line estimate.
- **Audit line numbers have shifted** since the includes landed: A1's near-field
  precedent is now `GrassNearFieldPlace.compute:422` (audit said 571-578); the chunk fix
  sits at `BiomeGrassPlace.compute:327` (audit quoted the pre-fix code at 477-483).
- **The plan's `run "Grass Edge Strip Probe"` is shorthand.** The command is
  `script.run "Grass Edge Strip Probe"` (`script.run-script` is an alias). The script is
  `Assets/Resources/ConsoleScripts/Grass Edge Strip Probe.txt`.
- **The plan's "Gate after Phase 2" note predates the 2026-07-05 reorder.** It says
  "approve the look before far-field work matches terrain paint to it" — written when 2
  preceded 3. With clumps now LAST, the operative consequence inverts: when Phase 2
  lands, **re-verify the blanket↔canopy brightness handoff from Phase 3**, because the
  blanket must match the *new* clumped canopy. Budget that re-check into Phase 2's gate.

---

## Phase 0 — Correctness floor — LANDED except one decision (verify, don't redo)

Landed (all verified 2026-07-06, table above): A1 overflow rollback, A5 clamps, C1/C4/C5
dead deletions, D1 shared placement include, B2 rider. The tracker's `[x] Phase 0
cleanup` box is consistent with the tree.

**Open remainder:** the `[ ] C2/C3 keep-vs-delete decision` box. Both paths are still in
the tree (table above). Per the audit's Codex feedback these are delete candidates but
are "tied to the still-deferred chunk-grass architecture decision" — present the choice
to Bryan (keep as tuning knob vs delete suppression path + `PassesFrustum` + planes
uniform + stat plumbing) as a standalone cleanup, never mixed into a visual phase.
Note: Phase 4 builds a *new* compaction kernel and does not resurrect C3's placement-time
cull — deleting C3 does not conflict with Phase 4.

**Regression check (run whenever placement is touched, in any later phase):**

```
planet.seed                    # record the seed
debug.capture-set "Grass"
debug.capture                  # sidecar carries the Draw/Cull counts
# … make the change, rebuild, re-enter play mode, same seed (planet.seed <n> + planet.generate if needed), same teleport …
debug.capture
```

Expected: `--- GrassNearField ---` `Draw: emitted=` and every `Cull:` counter identical
before/after on the same seed and viewpoint. **If placement counts differ on the same
seed → the change altered placement behavior. STOP and bisect the diff** — do not
rationalize a count drift as "probably fine"; the near-field determinism chain
(stable cell hash → position-seeded blade hash) is a verified invariant the audit
cross-checked clean.

**Exit check (quoted from the plan):** "placement stats unchanged before/after (same
emitted counts on the same seed), diff of the two computes shows only includes +
kernel-specific code." Already satisfied for the landed items; the C2/C3 box is checked
only after Bryan's call and the resulting cleanup lands. Ask Bryan before checking any
tracker box.

## Phase 1 — Lighting: cheaper AND better — NEXT UP, **blocked on the cloud Phase 2 gate**

**Entry condition:** the cloud campaign's Phase 2 capture-comparison gate has passed with
Bryan's sign-off (see [cloud-phases.md](cloud-phases.md)). The dependency is real, not
ceremonial: this phase moves `CloudShadowFactor` sampling per-blade, so cloud sampling
(blue noise + unified gloom) must be stable first or you'll be re-verifying grass against
a moving cloud target. The grass tracker's "after Cloud Phase 1 captures" precondition is
already satisfied; "waits until the cloud sampling pass is stable" is what the pending
cloud Phase 2 comparison decides.

What moves from `GrassFragment` (lines 351-378, all currently per-pixel) into
`GrassVertex` (starts line 188), interpolated as a lit color: planet-normal/sun-direction
math (`PlanetSunDirection`, line 353), `daylight` (line 355), `surfaceDirect` (line 356),
**`CloudShadowFactor` (line 357** — a 3-step unrolled march, each step a weather sample +
3D shape-noise fetch, `CloudShadows.hlsl:63-109`; this is the expensive one), the night
blend (lines 373-377), and the backlit term (lines 369-371). What stays per-fragment:
the dither clip (line 317), the cluster-card clip block (lines 319-333), and the
normal-dependent wrap term (lines 363-365) using the interpolated normal — per the plan
and R3.

**Fence:** Phase 1 moves the *call site* of `CloudShadowFactor`; it does **not** edit
`CloudShadows.hlsl`. That include feeds `Ocean.shader` (untouchable caustics) — if you
find yourself editing the include to make this phase work, stop and re-read the plan.
Also fence: do not retune any lighting constant (wrap 0.72/0.28, backlit 0.16, night
0.65, …) "while in there" — Bryan's visual-tuning gate applies; the phase's cost claim is
*identical look, lower cost*. The R8 tip specular (step 3) is a **new visible lighting
term** and therefore cannot ride inside that identical-look claim — it gets its own
sub-gate (see step 3).

**Steps:**

1. Before-capture: ground level in dense grass (Savanna/Grassland), HUD off, saved
   teleport, `debug.capture-set "Grass"` + `debug.capture`. Also capture once under a
   cloud shadow (`weather.frame-storm` finds the strongest cell, then descend) — this is
   the "grass under a cloud shadow still darkens" evidence. Record the sidecar's
   whole-frame GPU ms and `Near grass CPU` line.
2. Restructure `Grass.shader` per the move list above. `dotnet` builds don't compile
   shaders — Unity import is the compile check.
3. **Tip specular (R8) — SEPARATE SUB-GATE, only after step 4's identical-look A/B has
   passed:** two lines in the now-vertex lighting path, `spec *= t * 0.12`-style height
   masking (t = blade-height fraction, already computed in the vertex path). This is a
   deliberate visible change, so it can never pass an "identical-to-eye" check — capture
   its **own** before/after pair on top of the verified vertex-lighting state and present
   the declared tip-specular delta to Bryan as its own look approval. Do not bundle it
   into the cost A/B. Biome masking (lush/wet only) is a later option — don't build it
   now.
4. After-capture (vertex-lighting move only, before the R8 specular): identical
   teleports, identical set, plus the cloud-shadow shot.
5. **Blade↔terrain brightness seam re-check at 200 m** (plan step 1.3): frame the
   near-field draw boundary (`NearFieldDrawDistance = 200`,
   `QualityController.cs:42`) side-on; the canopy-color handoff converges to
   `GrassCanopyAlbedo` (used by both `Grass.shader` and `GrassColor.hlsl`) — verify it
   still matches with vertex lighting.

**Expected observations at the gate:**

- Whole-frame GPU ms at ground level in dense grass: **DOWN** (this phase is "the biggest
  available grass GPU win" — R3). The plan's metric: "Measure NearGrass frame section
  before/after"; the `Near grass CPU:` sidecar line must at minimum not regress (the win
  itself is GPU-side fragment work, so read the whole-frame GPU number for the payoff).
- A/B PNGs identical-to-eye at blade scale — judged on the vertex-lighting move alone
  (before the R8 tip specular is added; the specular's own pair shows only the declared
  tip-highlight delta).
- `Draw: emitted=` unchanged (this phase must not touch placement).

**Branch instructions:**

- GPU ms NOT down → verify the expensive term actually moved:
  `grep -n "CloudShadowFactor" Assets/Graphics/Shaders/Grass.shader` must hit inside
  `GrassVertex` (before line 315), not `GrassFragment`. If it moved and there's still no
  win, capture wasn't measuring the hot case — recapture at ground level with grass
  filling the frame before concluding anything.
- Visible lighting difference at blade scale → a per-pixel term was moved that shouldn't
  have been (the wrap term is the usual suspect). Restore that one term to the fragment
  and re-diff; do not tune constants to compensate.
- Grass shadow darkening no longer tracks the ground shadow under the same cloud →
  that's a cloud-side regression, not a grass one — run cloud-phases.md Phase 0's
  regression check before touching grass again.
- Emitted counts changed → you touched placement by accident. STOP and bisect (Phase 0
  regression check).

**Exit check (quoted from the plan):** "A/B captures identical-to-eye at blade scale;
NearGrass ms measurably down; grass under a cloud shadow still darkens correctly (shadow
now per-blade — acceptable by construction, verify visually)." Gate note (this file's
resolution of a tension in the plan): the quoted identical-look check applies to the
vertex-lighting cost move; the R8 tip specular is gated separately as a declared visible
delta with its own Bryan look approval (step 3). Then: capture diff(s) to
Bryan → sign-off → ask before checking the three Phase-1 tracker boxes, and record the
approved bundle timestamp next to them.

## Phase 3 — Far-field decision + transitions — **OPEN DECISION, Bryan's call before ANY code**

Runs after Phase 1 in the revised order. Beyond 200 m there is currently nothing
(both far layers off — state table above).

**Entry condition:** Phase 1 signed off, AND Bryan has picked (a), (b), or (c) below.
**Writing far-field code before the decision is recorded is a campaign violation.** The
menu is presented verbatim-faithful from the plan; the plan ranks them and recommends,
but the recommendation is not the decision.

### The decision menu (plan's ranking, plan's words condensed; decision belongs to Bryan)

- **(a) Re-land the blanket with the linear-coverage fix** — the stripe root cause was
  found and fixed during the probe sessions (linear coverage + toe cut in
  `PlanetVertexColor.shader`, later reverted wholesale with the blanket). Re-apply the
  data-driven version, gated by the same `GrassCanopyAlbedo` single-source color so paint
  and blades meet at one brightness. Cheapest full-planet coverage; the probe script is
  the regression harness and already exists.
  *Evidence obligations:* strip-probe bundles clean at the worst biome borders BEFORE
  asking to flip `_grassBlanketEnabled` on by default; orbit-to-ground descent sequence.
  *Caution:* the exact reverted fix is not in the working tree — it must be re-derived
  from the probe-session evidence (UNVERIFIED which commit, if any, holds it; the paint
  machinery itself is present: `EvaluateGrassOverlay` at `PlanetVertexColor.shader:694`,
  `ApplyGrassSurfaceAlbedo` at line 746).
- **(b) Re-enable the chunk layer** as the 200-420 m mid band (audit G6 — reserved as
  Bryan's decision). More real geometry, more cost, still ends somewhere needing (a).
  *Evidence obligations:* chunk-layer cost from `--- Grass ---` sidecar stats + frame
  timing at the band; the triple-fade check (below); A1's rollback is already in place
  for the capacity edge. Band constants as of 2026-07-06: `ChunkFadeInStart = 128`,
  `ChunkPeakDistance = 220` (`GrassPlacementController.cs:9-10`),
  `ChunkPeakCoverage = 0.42` (`GrassChunkRuntime.cs:16`).
- **(c) Both:** chunk mid-band + blanket far — the original three-layer design.
  *Evidence obligations:* union of (a) and (b), plus the two extra seams (near→chunk,
  chunk→blanket) each verified in the descent sequence.

Plan's recommendation (for the record, not a pre-decision): "(a) blanket first — nearly
free, probe harness exists — with (b) chunk mid-band held as the known upgrade if
grazing-angle paint reads flat at eye level."

### Fenced wrong path — walking back into the biome-stripe fight

`grass.layer blanket true` "to see how it looks" is the fenced move (SKILL.md wrong-paths
table). The ONLY sanctioned route to a live blanket is option (a) with the
linear-coverage fix re-applied and `script.run "Grass Edge Strip Probe"` evidence at the
worst biome borders. The probe script (verified in tree) captures six full "Grass"-set
bundles across states — baseline, blanket off, blanket on at zero strength, and a
surface-brightness ladder (0.35 / 0.7 / 1.0) — and its own header documents the diagnosis
table (strips in GrassLodCoverage view → coverage term; in BiomeMapBlend view → biome
atlas, not grass; gone with blanket off → painted overlay; scale with brightness → tint
glow). Aim the camera at the suspect strip BEFORE running it; it does not move the
camera, and it restores blanket/strength/brightness via `@defer` when done.

### Transition work (same regardless of the pick — this is where "sharp visible lines" lived)

1. Overlay window and blade fades stay slaved to `IGrassQualitySettings`
   (single-authority — G5 from the July-1 audit; keep it that way; the interface and its
   only implementation live in `QualityController.cs`).
2. Coverage handoff verified with the strip probe at biome borders (the historical
   failure mode).
3. If (b): revisit the triple-fade — dither + geometry shrink + albedo darken are all
   driven by `visualEdgeFade` (`Grass.shader:218-225`; the albedo-darken term is
   `edgeShade` at line 342). With vertex lighting from Phase 1, the albedo-darken term is
   the first candidate to drop if the band reads as a dark ring — as a proposed change
   with captures, not a silent tweak.

**Exit check (quoted from the plan):** "orbit-to-ground descent capture sequence: no
visible ring, stripe, or brightness step at any altitude. Strip-probe captures clean at
the two worst biome borders (Grassland/Desert, Savanna/Forest)." Then capture diff →
Bryan sign-off → ask before checking the tracker box (record the decision letter next to
it).

## Phase 4 — Budget headroom: frustum compaction — MEASURE-GATED

**Entry condition:** Phase 1's before/after timing exists AND still shows near-field
vertex cost worth chasing. No measurement → no phase. (R4's estimate: ~40-60% of the ~1M
placed instances are behind the camera at any moment; kernel cost ≈ 0.1-0.2 ms.)

1. **Compaction kernel** (R4): a per-frame pass reading the persistent 1M-instance
   buffer, frustum-testing each instance with sway slack (InfiniteGrass's 1.1×/1.5×
   clip-space margins), appending survivors to a second buffer + args that drive the
   indirect draw. Placement and persistence untouched — no page-shift or rotation holes
   by construction. This is NEW code, not a revival of C3's placement-time cull (which
   is the wrong layer — culling at placement leaves holes when the camera rotates; the
   near-field `Cull: … frustum=` stat is currently always 0 for exactly that reason).
2. **Spend the winnings deliberately — the default is distance, not framerate** (2026-07-05
   reprioritization, recorded in the plan): raise `NearFieldFullDensityDistance` /
   `NearFieldDrawDistance` (current 144/200, `QualityController.cs:41-42`; target
   ~220/300) so the near→far boundary moves out and covers fewer pixels. That is a
   settings edit in `DefaultGrassQualitySettings` — present it as a settings change with
   captures.
3. **Buffer capacity check required:** 300 m draw ≈ 2.25× the disc area of 200 m. Verify
   `Draw: emitted=` against `capacity=` (`DefaultCapacityInstances = 1_000_000`,
   `GrassNearFieldController.cs:45`, ~48 MB at 48 bytes/blade) and watch
   `Cull: … overflow=` in the sidecar. Overflow firing → raise capacity or spacing — a
   deliberate choice presented to Bryan, not an auto-bump.

**Branch instructions:**

- Popping on fast camera turns → slack margins too tight; widen toward the reference
  values before touching anything else.
- `emitted=` after compaction not ≈ the visible fraction → the test is wrong (near-plane
  or slack math); do not ship a kernel that culls more than geometry.
- Placement counts (pre-compaction candidates) changed at all → you touched placement,
  not compaction. STOP and bisect.

**Exit check (quoted from the plan):** "rendered-instance count from stats ≈ visible
fraction; no popping on fast camera turns (slack sufficient); net frame win recorded."
Capture diff (including timing sidecars) → Bryan sign-off → ask before checking the
tracker box.

## Phase 2 — Tufts: clump identity — RUNS LAST (polish pass), 1-2 days

The change that most alters the *look*: every blade currently rolls independent
height/yaw/tint → uniform fuzz. Port the Ghost-of-Tsushima clump model (R5).

**Entry condition:** Phases 1, 3, 4 signed off. This phase redistributes blade identity;
it must not change density or cost.

1. In `GrassPlacementCommon.hlsl` (the Phase-0 include — the right home since both
   computes need identical clump math): `clumpId = hash(cellIndex / CLUMP_CELLS, face,
   seed)`; derive per-clump height multiplier, lean direction, and a small tint shift.
   The near-field's cell hash already uses the 73856093/19349663 prime pattern
   (`GrassNearFieldPlace.compute:298-302`) — the chunk compute's lane indexing differs,
   so the shared helper takes the cell index as a parameter. `biome.Shape.w` is
   ClumpStrength (`GrassPlacementCommon.hlsl:6` documents the packing; DTO source
   `GrassPlacementDtos.cs:29`, from `GrassClumpStrength`) — currently near-dormant; it
   blends independent-blade ↔ clump-coherent behavior so biomes keep authorship.
2. Pass clump lean per blade. `GrassBladeInstance` is 3×float4 = 48 bytes
   (`GrassPlacementCommon.hlsl:13-18`); `Grass.shader` consumes only `input.color.rgb`
   (line 344), so **`Color.a` is free** — repack before considering any stride increase,
   and audit the layout if you must grow it.
3. Apply clump lean in `Grass.shader` blade construction: add to the existing `leanWS`
   bend (line 238), **NOT to wind — wind stays global** (the wind path is
   `_WindDirection`/`_WindSpeedMps`, lines 155-164, shared with weather).
4. Tune `CLUMP_CELLS` (clump world size ≈ 0.5-1.5 m) per biome via existing biome params
   if one size doesn't read everywhere — one deliberate pass with captures.
5. **Clump shape: prefer nearest-jittered-seed (Voronoi) assignment over square grid
   blocks** — 9-cell lookup in the placement compute; organic clump borders instead of
   visible squares. (Concept validated against Hoskins' "Rolling hills" Shadertoy,
   reviewed 2026-07-05 — CC BY-NC-SA, **concept only, no code**; its ray-marched
   architecture is not applicable to our instanced blades.)

Note: per-blade patch variation today comes from `SmoothPatchNoise`
(`Grass.shader:130`, used for gust envelope + height/width/tint at lines 175, 207-209) —
it modulates amplitude but gives every blade independent identity; that's the thing
clumps replace as the identity source. Don't stack a second identity system on top —
if clump identity lands, propose what happens to the patch-noise terms.

**Branch instructions:**

- `Draw: emitted=` changed on the same seed → clump code leaked into density/rejection.
  STOP and bisect — the phase's contract is "redistribution of identity, not density".
- Visible square/grid clump borders → step 5's Voronoi assignment, not tuning.
- Blanket (if live from Phase 3) no longer matches the clumped canopy at the 200 m
  handoff → re-run the Phase-3 handoff verification against the new look; the blanket
  must match the *new* canopy (drift-log item 4). This re-check is part of THIS phase's
  gate.

**Exit check (quoted from the plan):** "side-by-side captures Savanna/Grassland at
5 m / 50 m / 150 m: fields read as tufts with varied crowns, not carpet. Blade count
unchanged (this is redistribution of identity, not density)." The plan's own gate note:
clumps change the field's character — Bryan approves the look explicitly. Capture diff →
sign-off → ask before checking the tracker box.

## Phase 5 — Alive-ness — PARKED until characters/gameplay

Do not start on campaign time. Recorded so the parts aren't reinvented:

- **Trail/bend RT** replacing the 8-slot interactor cap (R6; `MaxInteractors = 8`,
  `GrassInteractorRegistry.cs:10`; the general audit's D6 flagged trail starvation at a
  full roster) — when the character controller lands; subsumes the release-sample
  machinery.
- **Ripple impulses** (R9 / GrassFlow) — same RT, when gameplay wants shockwaves.
- Wind↔weather already works (shared `_WindDirection`/`_WindSpeedMps`,
  `ShaderGlobalIds.Cloud.cs:42-43`). Optional rider: scale gust flutter by local `storm`
  — one weather sample in the vertex path Phase 1 already touches.

Debug-sphere workflow if you need to eyeball interactors meanwhile:
`grass.interactor-spawn` / `grass.interactor-status` / `grass.interactor-despawn`.

---

## Provenance and maintenance

Everything above verified against the working tree on **2026-07-06** (branch
`code-refactor`, dirty on top of `ec0b1cd`). Re-verify before trusting any volatile fact:

```bash
# Layer toggles and Phase 1 position (the two headline markers)
grep -n "_grassBlanketEnabled\|_chunkGrassEnabled" Assets/Scripts/Planet/PlanetGrassCoordinator.cs   # lines 18, 21
grep -n "CloudShadowFactor" Assets/Graphics/Shaders/Grass.shader   # 357 = fragment = Phase 1 not started

# Phase 0 landed markers
grep -n "0xFFFFFFFFu" Assets/Resources/BiomeGrassPlace.compute Assets/Resources/GrassNearFieldPlace.compute   # 327 / 422
grep -n "clamp(int2(floor(p))" Assets/Resources/GrassNearFieldPlace.compute Assets/Resources/BiomeGrassPlace.compute
grep -n "GrassPlacementCommon" Assets/Resources/BiomeGrassPlace.compute Assets/Resources/GrassNearFieldPlace.compute   # :3 / :18
grep -rn "_lastTickCamera\|EdgeSnappedUv" Assets/Scripts/Planet/Clouds   # expect NO hits (C1/C4 deleted; the EdgeSnappedUv hits in Assets/Scripts/Planet/Biomes are unrelated methods, not regressions)

# Phase 0 open remainder (C2/C3 still present until Bryan decides)
grep -n "SuppressionRadiusFraction" Assets/Scripts/Planet/Grass/GrassNearFieldController.cs   # = 0f, line 44
grep -n "PassesFrustum\|_FrustumCullEnabled" Assets/Resources/BiomeGrassPlace.compute

# Quality distances and capacity quoted in Phases 1/4
grep -n "NearFieldFullDensityDistance => \|NearFieldDrawDistance => " Assets/Scripts/Core/QualityController.cs   # 144 / 200
grep -n "DefaultCapacityInstances" Assets/Scripts/Planet/Grass/GrassNearFieldController.cs   # 1_000_000, line 45

# Phase 3 machinery
grep -n "EvaluateGrassOverlay\|ApplyGrassSurfaceAlbedo" Assets/Graphics/Shaders/PlanetVertexColor.shader   # 694 / 746
grep -n "ChunkFadeInStart\|ChunkPeakDistance" Assets/Scripts/Planet/Grass/GrassPlacementController.cs   # 128 / 220
ls "Assets/Resources/ConsoleScripts/Grass Edge Strip Probe.txt"

# Phase 2 packing facts
grep -n "struct GrassBladeInstance" -A 5 Assets/Graphics/Shaders/Includes/GrassPlacementCommon.hlsl   # 3×float4
grep -n "input.color" Assets/Graphics/Shaders/Grass.shader   # only .rgb consumed → Color.a free
grep -n "Shape;" Assets/Graphics/Shaders/Includes/GrassPlacementCommon.hlsl   # w = clump strength

# Console surface used by this file
grep -n 'ConsoleCommand("' Assets/Scripts/Core/Services/GrassDebugModule.cs Assets/Scripts/Planet/PlanetGrassCoordinator.cs Assets/Scripts/Planet/Grass/GrassInteractorCommands.cs
grep -n 'ConsoleCommand("run"' Assets/Scripts/Core/Console/Scripting/ConsoleScriptRunner.cs   # script.run, line 118
grep -n '"seed"\|"generate"' Assets/Scripts/Planet/Planet.cs | head -4

# Sidecar section strings quoted above
grep -n "GrassNearField ---\|Draw: emitted=\|Cull: candidates=" Assets/Scripts/Core/Services/GrassDebugModule.cs   # 192 / 205 / 209
grep -n "Frame Timing ---\|Near grass CPU" Assets/Scripts/Core/Services/FrameTimingModule.cs   # 288 / 332
```

If a grep contradicts this file, the tree wins — update this file and tell Bryan. If the
design doc closes the far-field decision, gains phases, or reorders again, re-sync this
file, [cloud-phases.md](cloud-phases.md), and SKILL.md in the same session that notices.
