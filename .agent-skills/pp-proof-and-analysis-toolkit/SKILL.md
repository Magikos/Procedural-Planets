---
name: pp-proof-and-analysis-toolkit
description: Use when you are about to guess instead of derive — deciding which pipeline stage owns a visual artifact, checking whether two shaders really compute "the same" term, reasoning about InterlockedAdd/GPU counter correctness, estimating whether an optimization is worth doing, matching the right instrument to a perf claim, proving a refactor changed no behavior, or judging whether a code comment's promise is true. Also when repeated tuning passes show no progress. Not for symptom-to-cause triage tables — see pp-debugging-playbook.
---

# Proof and Analysis Toolkit

Eight first-principles recipes, each with a worked example from this repo's actual
history. The theme: **prove it, don't eyeball it.** Every recipe converts a vague
judgment call ("this probably owns the bug", "these probably match", "this is probably
faster") into a derivation or a decisive observation.

Vocabulary used throughout (defined once):

- **F10 capture set** — a named list of debug render modes. `debug.capture-set "<Set
  Name>"` selects it in the in-game console; pressing F10 captures each mode as a PNG +
  a `.txt` metadata **sidecar** into `local-only/debug-screenshots`.
- **Debug mode** — a shader visualization path selected by an integer global (e.g. cloud
  mode 1 = `CloudWeather` shows the raw weather field instead of lit clouds).
- **Indirect draw** — `Graphics.RenderPrimitivesIndirect` reads its instance count from a
  GPU buffer that a compute shader wrote; the CPU never sees the count.
- **Weather grid** — the cube-face `Texture2DArray` pair (`_CloudWeatherMap`,
  `_WeatherDynamicsMap`) that is the single source of truth for clouds, rain, and gloom.

## Fast path

- Visual artifact owner unknown: Recipe 1, then Recipe 2.
- Two shader paths/comments claim the same math: Recipe 4 or Recipe 8.
- GPU append/counter correctness: Recipe 3.
- Optimization claim: Recipe 5 for cost estimate, Recipe 6 for the right instrument.
- Behavior-preserving refactor claim: Recipe 7.

---

## Recipe 1 — Stage-ownership proof

**Reach for this when** a visual artifact could plausibly live in any of several pipeline
stages and you are tempted to start tuning the last stage (the shader you understand
best). Ownership is provable; prove it first.

### Steps

1. Write down the pipeline as an ordered list of stages, sim → screen. For clouds:

   | # | Stage | Code | Debug view that shows its output |
   |---|-------|------|----------------------------------|
   | 1 | Weather simulation | `SphericalWeatherGrid.cs` + `Assets/Graphics/Shaders/WeatherEvolution.compute` | `CloudWeather` (cloud mode 1) — raw field |
   | 2 | Weather sampling | `Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl` (`CubeFaceUv` in `WeatherCubeFace.hlsl`) | same view (it renders *through* the sampler — see failure mode 1) |
   | 3 | Cloud shading | `Cloud.shader` raymarch | `CloudDensity` (3), `CloudOpticalDepth` (4) |
   | 4 | Composition | `CloudRenderFeature.cs` / render pass blit | `Off` (0) — final image |

2. Capture the artifact in each debug view, upstream first. Use the registered capture
   set when one exists — `debug.capture-set "Cloud Diagnostics"` covers modes 0-9
   (registered in `Assets/Scripts/Core/Services/CloudDebugModule.cs`). Set a single mode
   with `cloud.debug-mode` (as of 2026-07-06 the enum reaches all 9 modes;
   `CloudDebugState.cs`).
3. Find the **earliest** view where the artifact appears. Ownership is that stage or the
   view's own sampling path — everything downstream is exonerated.
4. Only then open the owning stage's code.

### Worked example — the cloud cube-face seam (2026-05-31)

Symptom: sharp diagonal, cube-face-shaped seams in the cloud layer. The lighting-stage
explanations were seductive (light-march banding, silver-lining edge). The decisive
capture: the seam was already visible in `CloudWeather` — the raw weather field, before
any density, lighting, or composition math ran. That single image eliminated stages 3 and
4. Root cause: cube-face UV **orientation** disagreed between weather generation, shader
sampling, cloud shadows, and the CPU weather query path. The fix aligned UV orientation
across all four consumers — zero lighting changes. Sampling for all shader consumers now
funnels through one `CubeFaceUv` in `WeatherCubeFace.hlsl`, included by
`WeatherSampling.hlsl`, so the class of bug is structurally harder to reintroduce.

### Failure modes of the method

- **The debug view renders through downstream code.** `CloudWeather` is drawn by
  `Cloud.shader` itself, so it exercises the *sampling* stage even while bypassing
  lighting. Know exactly what each view bypasses before treating it as "raw". If a view
  shows the artifact, ownership is "that stage **or the view's sampling path**".
- **Upstream-visible ≠ upstream-wrong.** A legitimate sharp storm front in the field can
  be *mishandled* downstream. The upstream view proves where the signal originates, not
  that the origin is a bug — compare against what the sim *should* produce.
- **Missing intermediate views.** If two adjacent stages have no view between them, add a
  debug mode before concluding. Guessing across a gap is the thing this recipe exists to
  prevent.

---

## Recipe 2 — Binary isolation

**Reach for this when** repeated tuning passes leave the visible result unchanged, or
when two or more branches could own an artifact and no existing debug view separates
them. This is the repo's hardest-won lesson: the water artifact saga (one of the three
costliest failures on record) was weeks of knob-twiddling *before* isolation.

### Steps

1. List the candidate branches (e.g. "foam alpha" vs "terrain source color bleeding
   through the volume composite").
2. Design a probe that replaces a continuous suspect with an **extreme or binary**
   signal: forced hot-pink color, forced full opacity, a disabled pass, a bypassed
   composite. The test for a good probe: **each possible observation eliminates at least
   one branch.** If both outcomes are consistent with the same branch set, the probe is
   decoration, not a probe.
3. Write down the predicted observation for each branch *before* running.
4. Run one F10 capture. Route by what actually lit up.
5. Remove or gate the probe when the investigation closes (dead-code rule).

### Worked example — the water saga probes

The probes that ended the saga are permanent debug modes in
`Assets/Scripts/Core/Services/DebugModeConstants.cs` (verified 2026-07-06):

- `TerrainSourcePink = 31` (line 55) — forces the terrain source color hot pink under
  the water composite. If the artifact contour turns pink, the contour **is** terrain
  source color bleeding through the volume composite; foam is exonerated. If it doesn't,
  and its sibling `FoamPink` does mark it, the contour is foam. Continuous color values
  could never make that call — pink can't be confused with anything legitimate.
- `SeaRay = 35` (line 65) — binary visualization of whether the camera ray passes behind
  the sea-level sphere. It separated "analytic sea coverage too weak" from "surface mask
  too strict" for the low-horizon far-shore contour.

Decision rules distilled from that investigation (restated from the internal water
runbook; treat as canonical):

- If a hard probe does **not** move the artifact, leave that branch immediately.
- If the final `Off` image looks wrong while proof modes look right, the failure is in
  the composite/presentation stack, not in effect generation — stop tuning generation.
- If a probe run leaves you unable to say which branch was eliminated, the probe was
  badly designed; redesign it rather than running it again.

### Failure modes of the method

- **Non-decisive probe** — see step 2's test. The most common miss.
- **The probe perturbs the system**: forcing opacity changes blend behavior; disabling a
  pass changes depth interactions. Prefer probes that recolor rather than restructure,
  and sanity-check that the artifact's *geometry* is unchanged under the probe.
- **Probe left running.** Ship-blocking globals and forgotten forced branches. Every
  probe added for an investigation is deleted or registered as a proper debug mode.

---

## Recipe 3 — Concurrency correctness for GPU counters

**Reach for this when** a compute kernel claims slots with `InterlockedAdd` and anything
(an indirect draw, a readback, another kernel) consumes the counter as a count. Do not
reason by analogy to single-threaded code — derive.

### The pattern and its proof

Slot-claim with rollback, as implemented (verified 2026-07-06) in
`Assets/Resources/BiomeGrassPlace.compute:322-329`:

```hlsl
uint slot;
InterlockedAdd(_GrassDrawArgs[1], 1u, slot);
if (slot >= (uint)_MaxBladeInstances)
{
    AddStat(STAT_OVERFLOW_REJECTED_BLADES, 1u);
    InterlockedAdd(_GrassDrawArgs[1], 0xFFFFFFFFu);
    return; // buffer full - quit the whole lane
}
```

(`0xFFFFFFFFu` ≡ −1 mod 2³². The near-field twin is
`Assets/Resources/GrassNearFieldPlace.compute:416-424`.)

Derivation that the final counter equals exactly `capacity` when `N > capacity` threads
attempt claims:

1. Each of the N threads performs one `+1`; the returned pre-add values are **unique** —
   atomicity guarantees slots 0…N−1 are handed out exactly once each.
2. Exactly N−capacity threads receive `slot ≥ capacity`; each performs exactly one `−1`.
3. Final value = N − (N−capacity) = capacity. Interleaving cannot change this because
   integer addition mod 2³² is associative and commutative — the sum is
   order-independent.
4. No winner slot is ever double-issued: rollbacks are only performed by threads whose
   add returned ≥ capacity, and such an add left the counter ≥ capacity+1, so after the
   rollback the counter is still ≥ capacity. Once the counter reaches capacity it never
   drops below it, so no later add can return a slot < capacity. Buffer writes at
   `slot < capacity` are therefore in-bounds and unique.
5. The counter transiently overshoots (up to N) *during* the dispatch — harmless, because
   the indirect draw reads the args buffer only after the dispatch completes.

### Worked example — audit finding A1 (2026-07-03, fixed by 2026-07-06)

`GrassNearFieldPlace.compute` had the rollback; `BiomeGrassPlace.compute` (the chunk
path) had the identical claim **without** it. Consequence, derived not observed: on
overflow the indirect args held `capacity + overflow`, so `RenderPrimitivesIndirect`
rendered phantom instances past the end of the blade buffer — out-of-bounds reads return
0 on DX11, producing degenerate zero-size blades at the world origin plus wasted vertex
work (undefined on other APIs). The 2026-07-03 line audit flagged it (finding A1); the
grass migration plan's Phase 0 landed the one-line rollback, now present as quoted above.
The lesson generalizes: when the same pattern exists twice, diff them — one of them is
usually the fixed version of the other (see Recipe 4).

### Failure modes of the method

- **Proving the wrong invariant.** The derivation above proves the *settled* value.
  If any in-dispatch logic reads the counter and assumes it never exceeds capacity, the
  transient overshoot breaks it. State which moment your invariant applies to.
- **Paired counters.** A stat counter (`STAT_OVERFLOW_REJECTED_BLADES`) and the args
  counter are updated non-atomically with respect to each other; treat cross-counter sums
  as approximate during a dispatch, exact only after it.
- **Assuming append semantics.** This pattern replaced `AppendStructuredBuffer`
  deliberately (see the header comment in `GrassNearFieldPlace.compute`) — don't "fix" it
  back; the explicit counter is what lets the same buffer drive the indirect draw with no
  `CopyCount`.

---

## Recipe 4 — Formula-consistency proof across shader stages

**Reach for this when** two shaders (or a shader and C#) claim to compute "the same"
term — lighting, fade, gloom, a gate. "Looks similar" is not a check. Diff the
expressions symbolically, then plug in one adversarial number.

### Steps

1. Extract both expressions verbatim into a scratch note, substituting shared helper
   bodies until both are in terms of the same primitive inputs.
2. Diff symbolically. Any structural difference (an extra `smoothstep`, a different gate,
   a raw vs. gated input) is a finding, whatever the comments say.
3. Pick input values that maximize the divergence and evaluate both by hand.
4. State the broken invariant in domain terms ("the ground is darker than the cloud
   above it") — that's what makes the finding reviewable.

### Worked example — audit finding A2, the gloom divergence (2026-07-03)

The sky (Cloud.shader) computed gloom as `max(storm, gatedRain)` where `gatedRain` is
rain gated by storm intensity. The ground shadow path computed
`max(storm, smoothstep(0.12, 0.6, rawRain))` — raw rain, no storm gate, plus a
steepening the sky lacked. Plug in a moderately-rainy, low-storm cell — `storm = 0.2`
(below the rain gate threshold, so `gatedRain = 0`), `rawRain = 0.5`:

- Sky gloom = max(0.2, 0) = **0.2**
- Ground gloom = max(0.2, smoothstep(0.12, 0.6, 0.5)) = max(0.2, 0.888) = **0.89**

t = (0.5−0.12)/0.48 = 0.792; smoothstep = t²(3−2t) = 0.627 × 1.417 = 0.888. The ground
darkened ~4.4× more than the cloud above it — the exact "rain clouds don't look darker"
complaint, made *inconsistent* between sky and surface. No amount of eyeballing two
90-line shaders finds a 4.4× invariant break; one substitution does.

Two extra twists, both instructive:

- The shadow-path code carried a comment claiming it matched Cloud.shader. It didn't
  (Recipe 8).
- The Codex cross-review found the audit's own quoted shadow code was **stale against
  the working tree** — the real divergence at fix time was simpler (shadows used
  storm-only). Symbolic diffs are only valid against the code revision you're fixing.
  Re-extract from the current tree; never fix from a quoted snippet in a findings doc.

**Resolution (verified in tree 2026-07-06):** one shared helper owns the formula.
`WeatherSampling.hlsl:47-55` defines `WeatherCloudGloomFromRain(storm, signal) =
max(saturate(storm), saturate(signal))` and `WeatherCloudGloom(direction, storm)`;
`Cloud.shader:388` and `CloudShadows.hlsl:58` both call it. That is the durable fix
shape: **a formula two stages must agree on lives in one include**, not in two files
with a comment promising sameness.

### Failure modes of the method

- Diffing against stale sources (docs, audits, memory) instead of the tree — see above.
- Stopping at symbolic equality while the **inputs** differ (same formula fed `rawRain`
  in one path and `gatedRain` in the other is still divergent). Trace inputs to their
  producers.
- Declaring divergence where a compensating term exists elsewhere in one path. Diff the
  full path from shared input to final use, not one line.

---

## Recipe 5 — GPU cost estimation before optimizing

**Reach for this when** deciding whether an optimization is worth doing, or which of two
shapes to pick. Count invocations × cost per invocation for both the current and the
proposed shape *before* writing code. The estimate's job is to predict direction and to
expose the variable the answer actually depends on.

### Steps

1. Cost per invocation: count texture fetches (dominant) and notable ALU (transcendental,
   loops) per shader invocation.
2. Invocation count: pixels shaded (resolution × coverage × overdraw) for fragment work;
   `instances × vertices-per-instance` for vertex work; `threads` for compute.
3. Multiply. Compare. If the comparison flips on an unknown (coverage, instance count),
   **that unknown is the measurement to take first.**

### Worked example — grass blade lighting per-pixel vs per-vertex (R3, 2026-07-04)

Current state (verified 2026-07-06): `Grass.shader:357` calls `CloudShadowFactor` in the
**fragment** stage. `CloudShadows.hlsl:96-104` is a 3-step unrolled march; each step's
`SampleCloudShadowDensity` (`:35-61`) does 1 weather `Texture2DArray` fetch, then — when
condensation > 0.001 — 1 `Texture3D` shape-noise fetch and 1 dynamics fetch (via
`WeatherCloudGloom`). So **3-9 texture fetches per grass pixel**, for a value that is
effectively constant across a 20 cm blade. (The 2026-07-04 research doc said ~6; the
A2 fix's gloom unification added the dynamics fetch — estimates drift, recount.)

- Per-pixel side: 1080p = 2,073,600 px; grass covering ~60% of a ground-level view with
  modest overdraw ≈ 1.5M shaded fragments → **~4.5-13.5M fetches/frame** on cloud
  shadow alone.
- Per-vertex side (the R3 proposal): near-field instances are 3 cards × 18 verts = **54
  vertices per instance** (`GrassChunkRuntime.cs:11-15`), capacity 1,000,000
  (`GrassNearFieldController.cs:45`). Fetches = emitted × 54 × 3-9.

The comparison **flips on emitted-instance count**: break-even where emitted × 54 ≈
shaded pixels, i.e. ~28k instances against 1.5M fragments. At 100k+ emitted the raw
vertex-side fetch count exceeds the pixel side — the naive "vertex is cheaper" claim is
false in that regime, and the real win comes from the pieces the count doesn't capture
symmetrically: the per-pixel ALU (sun math, daylight curves, `pow` backlight) also moves,
and distant instances add vertices while contributing few pixels. So the estimate's
output is not "do it" but: **read `Draw: emitted=` from the grass sidecar first, then
measure** — which is exactly how the grass migration plan sequences Phase 1 ("measure
NearGrass before/after"). An estimate that ends in "measure X first" has done its job.

Known per-invocation caveats to carry into any estimate (from project rules):
`ComputeShader.Dispatch` has ~50-100 μs launch overhead — don't compute-shader trivial
workloads; `AsyncGPUReadback` adds 1-2 frames of latency — don't use it for same-frame
results.

### Failure modes of the method

- **Counting only one side** (the classic: counting saved fragment work, not added vertex
  work). Always produce both columns.
- **Ignoring overdraw and early-out paths.** `CloudShadowFactor` has four early returns
  before any fetch; estimates are upper bounds unless you reason about hit rates.
- **Treating the estimate as the result.** It picks what to measure; the frame timings
  decide (Recipe 6). Never commit an optimization on the strength of the estimate alone.

---

## Recipe 6 — Matching the right instrument to a perf claim

**Reach for this when** any change claims a performance effect — before believing it,
and especially before writing "faster" in a doc or commit.

The step-by-step perf-claim PROTOCOL (predict direction first in writing, pin
pose/seed/tier, before/after captures, quote avg AND p95 with `n`, fresh play run for
startup claims) is owned by **pp-validation-and-evidence**; the capture-set mechanics
(which timed set isolates which axis, sidecar diffing) by **pp-diagnostics-and-tooling §4**.
What this recipe adds is the instrument-matching discipline:

1. **Know your instruments** (`Assets/Scripts/Core/Services/FrameTimingModule.cs`,
   verified 2026-07-06): `FrameTimingCounters` has five **CPU** sections —
   `SurfaceVisibility`, `Water`, `Clouds`, `NearGrass`, `ChunkGrass` — measured by
   `using (FrameTimingCounters.Measure(section))` scopes, plus whole-frame CPU and GPU
   ms from Unity's `FrameTimingManager`, all over a 120-frame rolling window reporting
   avg/p95/last/n. Every F10 sidecar carries the block under `--- Frame Timing ---`.
   **Sections are CPU-only**: a shader-side change (like Recipe 5's vertex-lighting move)
   shows up in the whole-frame **GPU** number, not in the `NearGrass` section — matching
   the wrong instrument to the claim is the top way to "measure" nothing.
2. **Derive which number your prediction must move** before capturing anything: CPU
   driver cost → the owning section line; shader/draw cost → whole-frame GPU; startup →
   the `Generation timings` log line, never a warm reload.
3. After running the protocol, compare against the prediction. Direction wrong or
   magnitude wildly off → you don't understand the system yet; that's a finding, not
   noise to explain away.

### Failure modes of the method

- Comparing runs with different scenes/weather/altitude — the sidecar records mode,
  quality, and camera context precisely so you can check comparability; use it.
- Reading `last=` instead of the windowed avg/p95 (single frames are noise).
- The GPU number can be invalid on some frames (`FrameTimingManager` returns nothing or
  garbage; values are sanitized to −1 and excluded) — check `n=` before trusting it.
- Declaring victory from build success. Build success is a code-health check only;
  play-mode evidence decides (project rule).

---

## Recipe 7 — Determinism / invariance check

**Reach for this when** a refactor **must not change behavior** (extractions, include
unification, dead-code deletion) and there is no test framework to lean on (deliberate
project stance). Use the system's own aggregate statistics as a behavioral checksum.

### Steps

1. Identify a deterministic output the system already reports. Grass placement is
   deterministic by construction: near-field cells are anchored in stable
   `(face, cellU, cellV)` coordinates — same world cell → same hash → same blade
   properties, independent of camera motion (header contract in
   `GrassNearFieldPlace.compute`). Same seed + same viewpoint ⇒ same emitted counts.
2. Record the checksum before: F10 sidecar's grass block carries `Draw: emitted=…` and
   the full rejection breakdown `Cull: candidates=…, density=…, water=…, slope=…,
   distance=…, …, overflow=…` (`GrassDebugModule.cs:204-209`).
3. Apply the refactor. Recapture at the same seed/viewpoint. Every number must match
   exactly — these are integer counts, not floats; "close" is a failure.
4. Pair with a source-level check when duplicates were unified: the grass plan's Phase 0
   exit check reads *"placement stats unchanged before/after (same emitted counts on the
   same seed), diff of the two computes shows only includes + kernel-specific code."*
   That two-part form — behavioral checksum + structural diff — is the template.

### Worked example — grass plan Phase 0 (landed by 2026-07-06)

Phase 0 of the 2026-07-04 grass visual-migration plan extracted ~150 duplicated lines
(placement structs, hashing, cube-face math, param blending) into a shared include used
by both placement computes — code that "must stay bit-identical" and had already drifted
once historically. The exit check above is what made a 150-line move reviewable without
a test framework: identical emitted counts on the same seed is a strong accidental-change
detector because every placement decision (hash, gate, blend) feeds the count.

### Failure modes of the method

- **Checksum too coarse.** Identical totals can hide compensating errors (+5 here, −5
  there). The rejection breakdown (per-cause counters) is the defense — compare all of
  them, not just `emitted`.
- **Stats themselves buggy.** The 2026-07-03 audit's A6 found off-face cells
  misattributed to the distance-rejected counter. A checksum is only as trustworthy as
  its instrumentation; if a counter looks wrong, audit the counter before the refactor.
- **Readback latency.** Emitted counts arrive via `AsyncGPUReadback` (1-2 frames);
  capture after the scene settles, not on the first frame after a dispatch.
- **Hidden nondeterminism**: camera-dependent paging means viewpoint is part of the
  seed. Pin the exact camera position (the sidecar records it).

---

## Recipe 8 — Comment-vs-code audit

**Reach for this when** reading any comment that makes a *claim*: "same as X", "matches",
"mirrors", "returns null", "kept in sync", "already handled". Treat every such comment as
an unverified assertion with a decent base rate of being false — this repo has caught two
in one week.

### Steps

1. Grep for claim-shaped comments in the area you're touching:

   ```
   rg -n "same as|matches|mirrors|identical|kept in sync|returns null|already" Assets/Scripts Assets/Graphics --ignore-case
   ```

2. For each: locate the code the claim points at and verify it **against the current
   tree** (Recipe 4 for formulas; a read of the callee for behavioral promises).
3. False claim → it's a finding (audit workflow: findings first, Bryan reviews before
   fixes). The fix is usually structural — make the claim unnecessary (shared helper,
   single owner) rather than re-truing the comment, per the project's comment doctrine
   (comments state non-obvious WHY only; "same as X" comments are drift magnets).

### Worked examples

- **A2's false "same gloom term" comment** (2026-07-03 audit): the shadow path carried a
  comment claiming it used the same gloom term as Cloud.shader while computing a
  divergent formula (Recipe 4's numbers). Resolution deleted the claim by deleting the
  duplication — both paths now call `WeatherCloudGloom` in `WeatherSampling.hlsl`.
- **G1's null promise over a throwing call** (2026-07-03 general audit):
  `WeatherManager.PrecipitationDebugControl` carried `// Returns null if no precipitation
  system is wired up` above `ServiceLocator.Get<IPrecipitationDebugControl>()` — and
  `Get<>` **throws** when the service is absent (project contract; `TryGet<>` is the
  null-returning form). All three call sites null-checked a value that could never be
  null: dead checks guarding a live exception in any scene without a precipitation
  controller. **Fixed as of 2026-07-06** — `WeatherManager.cs:120-121` now reads
  `ServiceLocator.TryGet(out IPrecipitationDebugControl control) ? control : null;`,
  making the comment true.
- **Meta-example**: the A2 audit entry itself quoted stale code (caught in Codex
  cross-review). Findings docs, memory files, and this skill are all claim-shaped
  comments at one remove — the recipe applies to them too, which is why every claim here
  carries a date and the provenance section below carries re-verification commands.

### Failure modes of the method

- Verifying the comment against the code *next to it* instead of the code it points at
  (the claim in `CloudShadows` was about `Cloud.shader`).
- Fixing the comment instead of the divergence — a re-trued "same as X" comment starts
  rotting the day it's written.
- Skipping "obviously true" claims. G1's comment read as documentation of intent; intent
  is exactly what drifts.

---

## When NOT to use this

- **You have a symptom and need a next step** — pp-debugging-playbook has the
  symptom→triage tables per domain and the time-sink traps; come back here when triage
  says "prove ownership" or "design a probe".
- **You need to know what counts as evidence, or the before/after capture protocol for a
  visual change** — pp-validation-and-evidence owns the evidence bar and promotion rules.
- **You're running a hunch→experiment→accepted-result lifecycle** (prediction registers,
  adversarial refutation) — pp-research-methodology.
- **You need the catalog of measurement tools** (all debug modes, capture sets, counters,
  graphify queries) — pp-diagnostics-and-tooling.
- **You want the history of a past investigation** rather than the method it exemplifies
  — pp-failure-archaeology.

---

## Provenance and maintenance

All claims verified against the working tree on **2026-07-06** (branch `code-refactor`,
dirty on top of `ec0b1cd` — dirty is normal here). Worked-example history is restated
from the former grass/cloud A1, A2, A6, B1 and general G1 findings reconciled in
`docs/audit/2026-07-22-consolidated-code-audit.md`,
`docs/research/2026-07-04-grass-cloud-reference-recommendations.md` (R3),
`docs/design/2026-07-04-grass-visual-migration-plan.md` (Phase 0/1 exit checks), and the
cloud-seam / water-saga records in `.agent-memory/codex/` (additional background only).

Re-verify volatile facts (git-bash, repo root):

```bash
# A1 rollback still present in both computes
grep -n "0xFFFFFFFFu" Assets/Resources/BiomeGrassPlace.compute Assets/Resources/GrassNearFieldPlace.compute
# A2 unified gloom helper and both call sites
grep -n "WeatherCloudGloom" Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl Assets/Graphics/Shaders/Cloud.shader Assets/Graphics/Shaders/Includes/CloudShadows.hlsl
# G1 TryGet fix
grep -n "PrecipitationDebugControl" Assets/Scripts/Planet/WeatherManager.cs
# CloudShadowFactor still per-pixel in Grass.shader (moves to vertex in grass plan Phase 1)
grep -n "CloudShadowFactor" Assets/Graphics/Shaders/Grass.shader
# Water probe mode ids
grep -n "TerrainSourcePink\|SeaRay" Assets/Scripts/Core/Services/DebugModeConstants.cs
# Frame timing sections and window size
grep -n "enum FrameTimingSection\|RollingWindowSize" Assets/Scripts/Core/Services/FrameTimingModule.cs
# Grass sidecar checksum lines
grep -n "Draw: emitted\|Cull: candidates" Assets/Scripts/Core/Services/GrassDebugModule.cs
# Vertex-count constants for the Recipe 5 arithmetic
grep -n "VerticesPerVisualBlade\|BladeVertexCount\|DefaultCapacityInstances" Assets/Scripts/Planet/Grass/GrassChunkRuntime.cs Assets/Scripts/Planet/Grass/GrassNearFieldController.cs
# Cloud debug mode roster (Recipe 1 table)
grep -n "RegisterMode(registry" Assets/Scripts/Core/Services/CloudDebugModule.cs
```

Facts most likely to drift: `Grass.shader:357` per-pixel `CloudShadowFactor` (goes away
when grass plan Phase 1 lands — Recipe 5's worked example then becomes historical, like
A1/A2); exact line numbers throughout (grep, don't trust); the per-step fetch count in
`SampleCloudShadowDensity` (recount after any cloud-shadow edit); A6 stat misattribution
status (UNVERIFIED whether fixed as of 2026-07-06 — check `NF_STAT_DISTANCE_REJECTED`
usage in `GrassNearFieldPlace.compute` before trusting the distance-rejected counter as
a checksum component).
