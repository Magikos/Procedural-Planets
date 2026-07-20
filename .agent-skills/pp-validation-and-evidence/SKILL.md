---
name: pp-validation-and-evidence
description: Use when deciding whether a change is actually done, verified, or proven — "is it done", "does it work", before/after comparison, baseline capture, acceptance criteria, promotion to complete, perf-win claims, or when tempted to say "build succeeded" or "looks fine". Also when writing exit checks for a plan phase. Not for how to operate the capture/debug tools themselves — see pp-diagnostics-and-tooling. Not for who approves what — see pp-change-control.
---

# Validation and Evidence

There is **no test framework** in this project. That is deliberate — never propose one
(CLAUDE.md, "Tests"). Validation is evidence-based in-game verification: builds prove
code health, captures prove pixels, counters prove numbers, and Bryan's eyes prove the
look. A claim without the matching evidence tier is not a result; it is a hypothesis.

Definitions used throughout:
- **F10 capture set** — a named group of debug view modes; pressing F10 in play mode (or
  running `debug.capture`) screenshots each mode to PNG + a `.txt` metadata sidecar in
  `local-only/debug-screenshots/`.
- **Sidecar** — the `.txt` written next to each capture PNG: camera pose, per-domain
  stats blocks (e.g. `--- Grass ---`, `--- Frame Timing ---`).
- **Code-health build** — `dotnet build ProceduralPlanets.Planet.csproj` (and Core,
  **serially** — parallel builds collide on a shared intermediate DLL and fake a failure).

## The evidence ladder

Each claim type has a minimum evidence tier. Lower tiers never substitute for higher ones.

| Claim | Minimum evidence | Tools |
|---|---|---|
| "The C# compiles / no type errors" | Code-health build exit 0 | `dotnet build ProceduralPlanets.Planet.csproj` (then Core, serially) |
| "Unity accepts it (shaders, assets, serialization)" | Unity import + compile with no new errors | Unity editor console (Bryan runs; agents do not launch Unity) |
| "It runs / initializes / doesn't throw" | Fresh play-mode run, clean `Editor.log` slice | Editor.log; console output |
| "Startup/generation got faster" | `Generation timings` line from a **fresh play run** (script reload is NOT startup proof) | `Planet.cs` logs `Generation timings: initialize=..., terrain=..., colors=..., climate=..., water=..., total=...ms` at Debug level, tag `Planet` |
| "Behavior/placement is unchanged (refactor)" | Numeric invariance: same counts/stats on the same seed, before vs after | Sidecar stats blocks; `planet.seed`; see "Numeric invariance" below |
| "Visuals are unchanged / improved" | Before/after F10 capture diff, same viewpoint + seed + quality tier | Capture protocol below |
| "Frame cost went down" | Predicted direction stated first, then measured `FrameTimingCounters` section delta (avg + p95, 120-frame window) | `--- Frame Timing ---` sidecar block or detailed debug overlay |
| "The look is right" (any tuning) | **Bryan's visual sign-off on captures.** Never the agent's eye alone | Capture diff handed to Bryan; his verdict recorded in the plan tracker |

The evidence hierarchy in one line: **csproj build → Unity import → play-mode runtime →
F10 capture diff → Bryan's eyes.** Each tier gates the next; none skips forward.

## Acceptance-threshold discipline: define pass BEFORE running

Write the pass condition down before generating any evidence — otherwise every result can
be rationalized as success. This is the same predict-numbers-first bar as
pp-research-methodology. The 2026-07-04 migration plans (`docs/design/`) are the house
style; copy their exit-check form:

- Invariance form: "placement stats unchanged before/after (same emitted counts on the
  same seed)" — grass plan Phase 0.
- Coupling form: "cloud darkening above and shadow darkening below must track the same
  cells" — cloud plan Phase 0.
- Perceptual form with a threshold: "accept when grain reads as fine film grain (no
  worms/blotches) at default step count" — cloud plan Phase 1.
- Legibility form: "clear / cumulus / storm / raining must be tellable apart with the
  HUD off" — cloud plan Phase 2.

A phase without a written exit check is not a phase; it is drift.

## Before/after capture protocol (runbook)

Run this for ANY change that can move pixels — shader, compute, placement, lighting,
settings defaults. All commands are in-game console commands unless noted.

1. **Archive the baseline BEFORE touching code.** Set the domain capture set and fire it:
   `debug.capture-set "Cloud Diagnostics"` (or `"Grass"`), then F10 or `debug.capture`.
2. **Copy the baseline out of the live folder.** The pipeline prunes: it keeps only
   `6 runs × modes-per-run × 2` files in `local-only/debug-screenshots/` (as of
   2026-07-06, `DebugCapturePipeline.MaxCaptureRuns = 6`). An unarchived baseline gets
   silently deleted by later runs. Copy PNGs **and sidecars** to a dated, labeled folder,
   e.g. `local-only/debug-screenshots/baselines/2026-07-06-cloud-phase2-before/`
   (PowerShell: `Copy-Item local-only/debug-screenshots/F10-* <dest>`). The cloud plan
   tracker records baselines by timestamp (`20260704-1325xx`) — filenames are
   `F10-<modeId>-<modeName>-<yyyyMMdd-HHmmss-fff>`.
3. **Pin the viewpoint.** Save the camera pose: `camera.save-teleport <name>`. After any
   restart, `camera.teleport <name>` restores it exactly. The newest F10 sidecar's pose
   is also auto-importable as `camera.teleport LastDebugCapture` (parsed from the
   sidecar's `Position:` / `Forward:` / `Surface view:` lines).
4. **Pin the seed.** Record `planet.seed` output (planet seed + world seed). If the
   change touches generation/placement, the after-run must use the same seed —
   `planet.generate <seed>` regenerates with it (`planet.seed N` alone does NOT
   regenerate).
5. **Pin the quality tier.** Record `quality.get`; restore with `quality.set <index>`
   (`quality.list` enumerates). A tier change silently changes step counts, densities,
   and draw distances — it will masquerade as your change.
6. **Make the change.** Code-health build. Unity import. Fresh play-mode run (not a
   script reload).
7. **After-capture:** teleport to the saved pose, same seed, same tier, same capture set,
   F10. Archive to a matching `...-after/` folder.
8. **Diff.** Pixels: open before/after pairs side by side (Bryan judges). Numbers: diff
   the sidecars — git-bash `diff before/F10-...txt after/F10-...txt` or PowerShell
   `Compare-Object (Get-Content a.txt) (Get-Content b.txt)`. Expected diffs: timestamp,
   frame timing jitter. Everything else must be explained by the change.
9. **Record the verdict in the plan's Active Tracker** with capture timestamps, exactly
   like the cloud plan does ("Phase 1 capture comparison: `20260705-051115/051118`,
   Bryan saw no odd behavior").

## Numeric invariance checks (refactors and "no behavior change" claims)

The sidecar is the instrument. For grass, the `--- Grass ---` block prints (as of
2026-07-06, from `GrassDebugModule.AppendMetadata`):

- `Draw: emitted=<n>, visualBlades=..., capacity=..., buffer=... MB`
- `Cull: candidates=..., density=..., water=..., slope=..., distance=..., distanceFade=...,
  frustum=..., faceArea=..., rangeBudget=..., overflow=...`

Invariance claim = every count identical on the same seed, same pose, same tier, before
vs after. One caveat: the stats come back via `AsyncGPUReadback`, so capture after the
scene settles (a couple of seconds stationary), not on the first frame after a dispatch.
`overflow` must be 0 in both runs — a nonzero overflow means the comparison itself is
invalid (capacity clipped the counts).

Determinism is real here and is the reproducibility mechanism: the world seed propagates
`Planet` → `ShapeGenerator.Initialize(seed)` → `NoiseFilterFactory.CreateNoiseFilter(settings,
seed + layerIndex)` (per-noise-layer offset), per `docs/PROJECT_PLAN.md`. Same seed →
same terrain → same placement counts. If counts differ on the same seed, the change
altered behavior — that is a finding, not noise.

## Perf claims

This section is the library's home for the perf-claim PROTOCOL. Tool mechanics (which
timed capture set isolates which axis, per-stage subtraction, sidecar diffing with
`Compare-CaptureSidecars.ps1`) live in pp-diagnostics-and-tooling §4; matching the right
instrument to a claim and the classic failure modes live in pp-proof-and-analysis-toolkit
Recipe 6.

1. **State the predicted direction and rough magnitude first**, in writing, before
   measuring — plan style: "measure NearGrass frame section before/after — expect the
   largest single grass GPU win available."
2. Measure with `FrameTimingCounters` (sections: SurfaceVisibility, Water, Clouds,
   NearGrass, ChunkGrass; 120-frame rolling window reporting avg / p95 / last / n).
   Both numbers land in the `--- Frame Timing ---` sidecar block and the detailed debug
   overlay, so the before/after captures already carry them.
3. Same pose, seed, tier as the baseline; let the window fill (n approaching 120) while
   stationary before capturing.
4. Report the delta as avg AND p95. A win in avg with a p95 regression is not a win.
5. Startup perf specifically: only a **fresh play-mode run** with a fresh `Editor.log`
   slice containing the `Generation timings` line counts. Build success and
   script-reload success are explicitly not startup proof (learned during the June
   startup-perf work).

## What "done" means

A change is promoted to done only when ALL of these hold:

- [ ] Code-health build passes (Planet + Core, serial).
- [ ] Unity import + fresh play-mode run clean (no new errors/warnings in Editor.log).
- [ ] The phase's pre-written exit check passes with archived evidence (captures and/or
      sidecar numbers, before AND after).
- [ ] Anything visible: **Bryan has looked at the captures and said so.** His eyes lock
      the look; agent judgment never substitutes (see pp-change-control for the gate).
- [ ] The owning plan doc's Active Tracker checkbox is updated with the evidence
      reference (capture timestamps) — for the live migration plans, **ask Bryan before
      checking any box** (pp-visual-migration-campaign owns that protocol) — and
      `graphify update .` has been run if code changed (set a timeout — known hang in
      this checkout, see pp-build-and-env Known traps).

Note on `test.console.*`: those nine commands (`colors`, `async`, `async-result`,
`async-fail`, `async-cancellable`, `enum`, `error`, `types`, `spam` in
`Assets/Scripts/Core/Console/Commands/TestConsoleCommands.cs`) are interactive
proof-of-life for the **console subsystem itself** — parsers, async spinner,
cancellation, scrollback. They validate console plumbing, not game features. Only this
one precedent exists (`test.console`); whether new subsystems get their own `test.<system>`
command suite is Bryan's call — no sanction for the pattern is on record. Either way it
would be interactive proof-of-life, not a test framework.

## Anti-patterns (each has burned this project)

| Anti-pattern | Why it fails | Instead |
|---|---|---|
| "Build succeeded, so it works" | csproj build is code-health only; shaders, serialization, and runtime behavior are invisible to it | Climb the ladder: import → play → capture |
| "Looks fine to me" on one after-screenshot | After-only evidence is unfalsifiable — nothing to compare against, and the agent's eye doesn't lock looks | Baseline first; Bryan judges the pair |
| Tuning judged from memory of the old look | Memory of pixels is unreliable; the water saga was weeks of knob-twiddling without isolation or baselines | Capture-diff every tuning pass; one deliberate retune with F10 evidence, not knob-nudging |
| Skipping the baseline "because the change is small" | The prune deletes it retroactively anyway; small changes cause the subtlest regressions | Steps 1–2 of the runbook are unconditional |
| Comparing across different seed / pose / quality tier | Any of the three explains arbitrary diffs; the comparison proves nothing | Pin all three (runbook steps 3–5) |
| Claiming a startup win after a script reload | Domain reload ≠ cold generation path | Fresh play run + fresh `Generation timings` line |
| Declaring a perf win from `last=` ms | Single-frame numbers are jitter | avg + p95 over a filled 120-frame window |
| Retuning constants Bryan hand-picked | His picked values encode sign-offs you can't see | Flag, don't retune; see pp-change-control |
| Parallel Core+Planet builds "failing" | Shared intermediate DLL write collision, not a real error | Rerun serially before calling regression |

## When NOT to use this

- **How to operate the tools** — debug-mode catalog, capture-set mechanics, counter
  internals, graphify queries, measurement scripts: **pp-diagnostics-and-tooling**.
- **Who approves and when** — change classification, the visual-tuning gate's rationale,
  audit findings-only boundaries: **pp-change-control**.
- **Designing the experiment itself** — hypothesis lifecycle, adversarial refutation,
  predict-numbers-first methodology: **pp-research-methodology**.
- **Launching play mode / where artifacts land**: **pp-run-and-operate**.
- **Diagnosing a failure you found** (this skill only tells you the evidence is bad):
  **pp-debugging-playbook**.

## Provenance and maintenance

All claims verified against the repo on 2026-07-06, branch `code-refactor`. Re-verify with:

- Exit-check exemplars: `docs/design/2026-07-04-cloud-visual-migration-plan.md` and
  `...-grass-visual-migration-plan.md` (Active Tracker + per-phase "Exit check" lines).
- Capture filename + prune math: `grep -n "F10-\|MaxCaptureRuns\|keepFiles" Assets/Scripts/Core/Services/DebugCapturePipeline.cs` (name format ~line 258, prune ~line 271).
- Capture commands: `grep -n "capture-set\|\"capture\"" Assets/Scripts/Core/Services/DebugCaptureController.cs` (lines ~305, ~317).
- Teleports + sidecar pose import: `grep -n "ConsoleCommand\|LastDebugCapture\|F10-\*.txt" Assets/Scripts/Core/Services/CameraTeleportStore.cs`.
- Seed commands + timing log: `grep -n "ConsoleCommand(\"seed\"\|ConsoleCommand(\"generate\"\|Generation timings" Assets/Scripts/Planet/Planet.cs` (lines ~562, ~578, ~333).
- Seed propagation: `grep -n "seed" Assets/Scripts/Planet/ShapeGenerator.cs Assets/Scripts/Planet/NoiseFilters/NoiseFilterFactory.cs`; `grep -n "seed" docs/PROJECT_PLAN.md`.
- Grass stats fields: `grep -n "emitted\|Cull:" Assets/Scripts/Core/Services/GrassDebugModule.cs`; counters in `Assets/Scripts/Planet/Grass/GrassNearFieldController.cs` (`StatsCount = 11`).
- Frame timing sections/window: `grep -n "FrameTimingSection\|RollingWindowSize" Assets/Scripts/Core/Services/FrameTimingModule.cs`.
- Quality commands: `grep -n "ConsoleCommand" Assets/Scripts/Core/QualityController.cs` (lines ~173–203).
- test.console commands: `grep -n "ConsoleCommand" Assets/Scripts/Core/Console/Commands/TestConsoleCommands.cs`.
- No-test-framework rule and audit boundaries: `CLAUDE.md` ("Tests", "Audit workflow").
- Fresh-run / build-vs-visual doctrine background: `.agent-memory/codex/memory_summary.md` (additional background only; facts restated above).
