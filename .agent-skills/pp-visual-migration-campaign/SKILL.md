---
name: pp-visual-migration-campaign
description: Use when driving, resuming, reviewing, or reporting on the cloud+grass visual migration — the plans in docs/design/2026-07-04-cloud-visual-migration-plan.md and 2026-07-04-grass-visual-migration-plan.md. Triggers: "continue the cloud plan", "Phase 2 capture comparison", "clouds don't read as storm/rain", "cloud grain", "grass vertex lighting", "the 200 m grass edge", "far-field grass decision", "re-enable the blanket/chunk layer", "update the tracker", "which phase are we in". Not for general rendering triage — see pp-debugging-playbook; not for volumetrics/grass theory — see pp-gpu-rendering-reference; not for weather-grid internals — see pp-weather-sim-reference.
---

# Cloud + Grass Visual Migration Campaign

This is the executable runbook for the project's hardest live problem: migrating clouds
from "functional volumetric layer" to "weather you can read from the sky", and grass from
"uniform fuzz that ends at 200 m" to "tufted, lit ground cover with invisible LOD
transitions". It compiles two design docs into a decision-gated campaign an agent can pick
up cold.

**SYNC CONTRACT — read this first.** This skill *mirrors* two docs of record:

- `docs/design/2026-07-04-cloud-visual-migration-plan.md`
- `docs/design/2026-07-04-grass-visual-migration-plan.md`

On any conflict, **the design docs and the working tree win** and this skill is the thing
that is stale. The working tree moves daily and the docs' "Active Tracker" checklists can
themselves lag the tree (that has already happened — see the drift log in
[cloud-phases.md](cloud-phases.md)). Run the Provenance greps at the bottom whenever you
suspect drift, and run the State Assessment below **every time you pick this campaign up**,
even if you think you know where it stands.

Vocabulary used throughout (defined once):

- **F10 capture** — in play mode, F10 (or console `debug.capture`) cycles the active
  *capture set* (a named list of debug visualization modes), producing one PNG + one `.txt`
  metadata **sidecar** per mode in `local-only/debug-screenshots/`. Filenames:
  `F10-<module>.<modeNum>-<ModeName>-<yyyyMMdd-HHmmss>-<ms>.png/.txt`. A **bundle** = all
  files sharing one `yyyyMMdd-HHmmss` burst.
- **Sidecar** — the `.txt` next to each PNG. It carries the numeric evidence: a
  `--- Frame Timing ---` section (whole-frame CPU/GPU ms + per-section CPU including
  `Near grass CPU`), a `--- GrassNearField ---` section (`Draw: emitted=…`,
  `Cull: … overflow=…`), cloud raymarch state, and camera weather sample.
- **Weather coupling contract** — the weather grid is the single source of truth. Clouds
  may only consume `_CloudWeatherMap` (r=condensation, g=storm, b=moisture-source) and
  `_WeatherDynamicsMap` (r=humidity, g=precip water, b=rain rate). No phase introduces
  cloud state the sim doesn't drive. (Details: pp-weather-sim-reference.)
- **Gate** — a phase exit: capture diff + expected observation + **Bryan sign-off**. No
  gate passes on the agent's eye alone.
- **Tracker** — the `## Active Tracker` checkbox list at the top of each design doc.
  Checkboxes are updated only after Bryan signs off — ask before checking a box.

Shell note: `grep`/`ls` commands below are git-bash (the Bash tool); PowerShell
equivalents are given where the syntax differs. All console commands (`cloud.…`,
`grass.…`, `debug.…`) run in the in-game debug console during play mode.

---

## Step 0 — Campaign state assessment (ALWAYS do this first)

Do not write code, tune anything, or trust the trackers until you have located the
campaign's true position. Numbered procedure; run from the repo root.

1. **Read both Active Trackers.**
   `docs/design/2026-07-04-cloud-visual-migration-plan.md` lines 1-25 and
   `docs/design/2026-07-04-grass-visual-migration-plan.md` lines 1-20.
   As of 2026-07-06 they say: cloud = "Phase 2 lighting code implemented; capture
   comparison pending"; grass = "Phase 0 cleanup mostly landed; visual work waits until
   the cloud sampling pass is stable". Treat these as claims to verify, not facts.

2. **Verify the audit-fix floor** (the 2026-07-03 line-audit A-series that Phase 0 of both
   plans consumed). All four verified present 2026-07-06:

   ```bash
   grep -n "0xFFFFFFFFu" Assets/Resources/BiomeGrassPlace.compute
   # expect a rollback InterlockedAdd near line 327  → A1 fixed
   grep -rn "WeatherCloudGloom" Assets/Graphics/Shaders/
   # expect: helper defined WeatherSampling.hlsl:47-55, called from
   # Cloud.shader:~388 AND CloudShadows.hlsl:~58  → A2 fixed (gloom unified)
   grep -n "WeatherPrecipitationSignal = 9" Assets/Scripts/Planet/Clouds/CloudDebugState.cs
   # expect line ~14  → A3 fixed (console can reach debug mode 9)
   grep -n "TryGet(out _weather)" Assets/Scripts/Planet/Clouds/CloudController.cs
   # expect line ~150  → A4 fixed
   ```

   Any of these missing → the tree has been rolled back or you are on the wrong
   branch/worktree. STOP and ask Bryan which tree is current before anything else.

3. **Locate the cloud phase position** by code markers:

   ```bash
   grep -n "_CloudBlueNoise" Assets/Graphics/Shaders/Cloud.shader
   # hits at ~19-21 and ~332-333  → Phase 1 (blue-noise offset) landed
   grep -n "CloudBeerPowder\|CloudMultiScatter\|_CloudAmbientSky" Assets/Graphics/Shaders/Cloud.shader
   # hits (~110, ~118, ~51, ~393-397)  → Phase 2 code landed
   grep -rn "AerialDensity" Assets/
   # NO match as of 2026-07-06  → Phase 3 not started; a match means it has
   grep -rn "CloudCurl\|StratusProfile\|Cumulonimbus" Assets/
   # NO match as of 2026-07-06  → Phase 4 not started
   ```

4. **Locate the grass phase position:**

   ```bash
   ls Assets/Graphics/Shaders/Includes/GrassPlacementCommon.hlsl
   # exists, and is #included by BOTH computes (BiomeGrassPlace.compute:3,
   # GrassNearFieldPlace.compute:18)  → grass Phase 0.4 extraction landed
   grep -n "_grassBlanketEnabled\|_chunkGrassEnabled" Assets/Scripts/Planet/PlanetGrassCoordinator.cs
   # lines 18 and 21; BOTH false as of 2026-07-06 (near-field blades are the
   # only live layer; bare terrain beyond 200 m)
   grep -n "CloudShadowFactor" Assets/Graphics/Shaders/Grass.shader
   # hit at ~357, which is INSIDE GrassFragment (fragment stage starts ~315)
   # → grass Phase 1 (vertex-stage lighting) NOT started. When Phase 1 lands,
   # this call moves into GrassVertex (~188) and the fragment keeps only clips.
   ```

5. **Inventory the capture evidence** (newest bundles = what was last measured):

   ```powershell
   Get-ChildItem local-only\debug-screenshots -Filter "F10-*" |
     ForEach-Object { if ($_.Name -match '(\d{8}-\d{6})') { $matches[1] } } |
     Sort-Object -Unique | Select-Object -Last 10
   ```

   Known bundles as of 2026-07-06 (all verified on disk):
   | Bundle | What it is |
   |---|---|
   | `20260704-1325xx` | Cloud Phase 0 exit baseline (18 files, cloud modes 0-9) — the "before" for the whole migration |
   | `20260705-051115/051118` | Cloud Phase 1 comparison pair — Bryan reviewed, "no odd behavior" |
   | `20260705-0533xx` | Newest cloud-set bundle (18 files, modes 0-9). Post-dates the Phase 2 code. **Probably** the pending Phase 2 "after" — but the tracker box is unchecked. Confirm with Bryan / the sidecar metadata before treating it as the comparison capture; if unconfirmed, take a fresh one. |

6. **Reconcile.** Build a three-line status: (a) what the trackers claim, (b) what the
   code markers show, (c) what the newest bundle covers. If all three agree → proceed at
   the tracker's "Current next action". If they disagree → the tree + captures win;
   report the discrepancy to Bryan and propose the tracker edit (do not edit yet).

**Position as of 2026-07-06:** cloud campaign is at the **Phase 2 capture-comparison
gate** (code in, evidence pending Bryan); grass campaign is at **Phase 0 complete,
Phase 1 blocked on that cloud gate**. The cross-dependency is deliberate: grass Phase 1
moves `CloudShadowFactor` sampling into the grass vertex stage, so cloud sampling
(blue noise + gloom) must be stable and signed off first.

---

## Campaign map

Cloud phases (strictly ordered): **0 → 1 → 2 → [BRYAN GATE] → 3 → 4 → (5 optional)**.
Grass phases (order revised 2026-07-05, per the design doc): **0 → 1 → 3 → 4 → 2 → (5 parked)**
— far-field (3) moves ahead of clumps (2) because the 200 m edge fades to *nothing*, and
no near-side tuning fixes that; Phase 3 also requires **Bryan's a/b/c decision before any
code** (see the menu in [grass-phases.md](grass-phases.md)).

Cross-links:
- Grass Phase 1 starts only after the cloud Phase 2 gate passes (tracker: "after Cloud
  Phase 1 captures" — already satisfied — plus "waits until the cloud sampling pass is
  stable", which the pending Phase 2 comparison decides).
- Cloud Phase 4.1 must not start without a shared cloud-density story between
  `Cloud.shader` and `CloudShadows.hlsl` (paired-edit rule or a shared helper), or the
  sky/ground will drift again — that is exactly the class of bug A2 was.
- `CloudShadows.hlsl` is included by `Grass.shader`, `Ocean.shader` (line ~645),
  `PlanetVertexColor.shader`, and `WaterVolume.shader` (verified 2026-07-06). Every edit
  to it must compile-check all consumers, and **Ocean.shader contains the untouchable
  caustics** — an include-level change that shifts caustics visuals is a revert, not a fix.

Per-phase runbooks with exact commands, expected numbers, and branch instructions:
- [cloud-phases.md](cloud-phases.md) — cloud Phases 0-5 + drift log
- [grass-phases.md](grass-phases.md) — grass Phases 0-5 + the far-field decision menu

Build check after any C# change (serial — parallel Core+Planet builds collide on a shared
intermediate DLL):

```bash
dotnet build ProceduralPlanets.Core.csproj && dotnet build ProceduralPlanets.Planet.csproj
```

Build success is code-health only. Shader changes and all visual claims are decided by
Unity import + play-mode captures, never by the build.

---

## Fenced wrong paths (do not re-walk these)

| Path | Status | Why / what instead |
|---|---|---|
| Cloud temporal accumulation / EMA reprojection | **RETIRED — reverted** | Built, Bryan rejected the artifacts, reverted (2026-07-03 audit preamble records it). The single-pass march is the keeper. Do not retry without new evidence AND Bryan's approval; if step budget ever becomes the wall again, the specific recipe is Frostbite 4×4-pattern quarter-res + reprojection (cloud research doc §8) — as a proposal, not a re-derivation. |
| Re-enabling the grass blanket without solving biome stripes | **RETIRED pending** | The blanket is off (`_grassBlanketEnabled = false`) after the biome-stripe fight; `PlanetVertexColor.shader` was reverted wholesale. The only sanctioned route back is grass Phase 3 option (a) with the linear-coverage fix re-derived (the exact reverted code is not in the working tree — see grass-phases.md) and re-applied, plus `script.run "Grass Edge Strip Probe"` evidence at the worst biome borders. Flipping the flag to "see how it looks" is walking back into the fight. |
| Touching caustics | **FORBIDDEN** | CLAUDE.md don't-touch rule (`Ocean.shader` + related caustics code). Relevant here because `CloudShadows.hlsl` feeds `Ocean.shader` — see the include warning above. Findings against caustics are flag-only. |
| Tuning visual constants without a capture baseline | **FORBIDDEN** | Bryan's visual-tuning gate: no constant changes without a before-capture; never retune values Bryan hand-picked; one *deliberate* tuning pass per phase with F10 evidence, not knob-nudging. The water-artifact saga (knob-twiddling before isolation) is the incident behind this rule — see pp-change-control. |
| New cloud state the weather sim doesn't drive | **FORBIDDEN** | The coupling contract above. Every phase reads the existing weather channels; no phase writes new cloud state. A "quick local override texture" or hand-placed cloud violates the plan's hard requirement. |
| "Looks better to me" as a gate result | **FORBIDDEN** | Success is measurable: emitted-instance counts, frame-timing ms, capture diffs Bryan approves. |

---

## Validation and promotion protocol (every phase, no exceptions)

1. **Before touching code:** capture the "before" bundle at a reproducible location.
   Use `camera.save-teleport <name>` / `camera.teleport <name>` so before/after share the
   viewpoint; `weather.frame-storm` frames the strongest storm cell when one is needed.
   Set the right capture set first: `debug.capture-set "Cloud Diagnostics"` (cloud work),
   `debug.capture-set "Grass"` (grass work; it is the boot default set — see
   pp-diagnostics-and-tooling §2), or
   `debug.capture-set "Grass Visual"` (clean-view single shot). Then `debug.capture` or F10.
2. **Code + build check** (serial dotnet builds above). Unity import must succeed;
   shader errors surface in the editor console, not dotnet.
3. **After:** identical capture sequence, same teleport, same set. If the phase changes
   timing-relevant work, capture at ground level in dense grass / under the cloud deck so
   the sidecar's Frame Timing section measures the hot case.
4. **Diff:** PNG pairs side by side + sidecar numbers extracted into the report
   (emitted counts, `Cull: … overflow=`, `Whole frame … GPU`, `Near grass CPU`). State
   the expected observation from the phase's exit check and whether it held. If you
   cannot run play mode yourself, hand Bryan the exact console sequence and file names
   you need — the sequences in the phase files are copy-pasteable.
5. **Bryan sign-off.** Present evidence; Bryan's eyes lock the look. Only after his
   explicit approval do you edit the design doc's Active Tracker checkbox — **ask before
   checking any box**, and record the approved bundle timestamp next to the checkbox the
   way the existing entries do (e.g. "capture archived `20260705-051115/051118`").
6. **Post-change hygiene:** `graphify update .` after C# edits (set a timeout — known
   hang in this checkout, see pp-build-and-env Known traps); prune any change-history
   comments you touched (CLAUDE.md comment doctrine).

Escalate to Bryan immediately (don't burn session time) when: a gate's expected
observation fails twice, a fenced path looks like the only way forward, placement counts
shift on an unchanged seed, or the trackers/tree/captures three-way disagree.

---

## When NOT to use this

- **General rendering bug triage** (water artifacts, atmosphere, terrain seams, "why is X
  pink") → pp-debugging-playbook (stage-ownership method, proof modes).
- **Theory behind the techniques** (Beer-Powder, multi-scatter octaves, cube-sphere math,
  indirect-draw grass) → pp-gpu-rendering-reference. This skill tells you *when and
  whether*, that one tells you *how it works*.
- **Weather grid channels, evolution, and the coupling contract in depth** →
  pp-weather-sim-reference.
- **Capture mechanics, debug-mode catalog, counters** → pp-diagnostics-and-tooling;
  what counts as evidence → pp-validation-and-evidence.
- **Change classification and the review rules themselves** → pp-change-control.
- **Past dead ends outside this campaign** (water saga, atmosphere revert) →
  pp-failure-archaeology.

---

## Provenance and maintenance

Everything here was verified against the working tree on **2026-07-06** (branch
`code-refactor`, dirty on top of `ec0b1cd` — the dirty tree is normal and sacred).
Re-verify before trusting any volatile fact:

```bash
# Campaign position markers (Step 0 is the full procedure)
grep -n "Status:" docs/design/2026-07-04-cloud-visual-migration-plan.md docs/design/2026-07-04-grass-visual-migration-plan.md
grep -n "_grassBlanketEnabled\|_chunkGrassEnabled" Assets/Scripts/Planet/PlanetGrassCoordinator.cs
grep -n "CloudShadowFactor" Assets/Graphics/Shaders/Grass.shader
grep -rn "AerialDensity\|CloudCurl\|StratusProfile" Assets/   # cloud Phase 3/4 markers
ls Assets/Graphics/Shaders/Includes/GrassPlacementCommon.hlsl

# Audit-fix floor (A1-A4)
grep -n "0xFFFFFFFFu" Assets/Resources/BiomeGrassPlace.compute
grep -rn "WeatherCloudGloom" Assets/Graphics/Shaders/
grep -n "WeatherPrecipitationSignal = 9" Assets/Scripts/Planet/Clouds/CloudDebugState.cs
grep -n "TryGet(out _weather)" Assets/Scripts/Planet/Clouds/CloudController.cs

# Console command surface used by this campaign
grep -n 'ConsoleCommand("' Assets/Scripts/Planet/Clouds/CloudController.cs Assets/Scripts/Planet/WeatherManager.cs Assets/Scripts/Planet/PlanetGrassCoordinator.cs Assets/Scripts/Core/Services/DebugCaptureController.cs Assets/Scripts/Core/Services/GrassDebugModule.cs
grep -n "Cloud Diagnostics" Assets/Scripts/Core/Services/CloudDebugModule.cs   # capture-set name, line ~53
grep -n '"Grass"\|"Grass Visual"' Assets/Scripts/Core/Services/GrassDebugModule.cs  # lines ~129, ~141

# Tuning constants quoted in the phase files
grep -n "StormDarkening\|PowderStrength\|SilverLiningStormSuppression\|MultiScatter" Assets/Scripts/Planet/Clouds/CloudConstants.cs
grep -n "ViewSteps = 48\|MinViewSteps = 24" Assets/Scripts/Planet/Clouds/CloudSettings.cs
grep -n "NearFieldFullDensityDistance\|NearFieldDrawDistance" Assets/Scripts/Core/QualityController.cs   # 144 / 200
grep -n "DefaultCapacityInstances" Assets/Scripts/Planet/Grass/GrassNearFieldController.cs   # 1_000_000

# CloudShadows.hlsl consumer set (caustics-adjacency check)
grep -rn "CloudShadowFactor" Assets/Graphics/Shaders/ --include="*.shader"

# Capture bundle inventory (PowerShell)
# Get-ChildItem local-only\debug-screenshots -Filter "F10-*" | Sort-Object LastWriteTime | Select-Object -Last 20
```

If a grep result contradicts this skill, the tree wins: update this skill (and tell
Bryan), never "correct" the tree to match the skill. If either design doc gains phases,
reorders them, or closes the far-field decision, this skill and both phase files must be
re-synced in the same session that notices it.
