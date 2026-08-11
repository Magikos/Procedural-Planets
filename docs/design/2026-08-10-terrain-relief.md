# Terrain-relief diagnosis — result

_Executed 2026-08-10 (branch `scatter-placement`). Promotes plan
[plans/002-terrain-relief-experiment.md](../../plans/002-terrain-relief-experiment.md). Method: agent
self-serve captures (pinned pose + frozen oblique sun) with a quantitative relief-contrast metric = luma
std-dev over a fixed surface window, so effect sizes are numbers, not eyeballs._

## Setup (held fixed across every cell)

- Pose: `camera.teleport "Terrain Texture Oblique"` (dist 5360, looking ~73° down at ground), a hill spanning
  a **grass** face (left) and a **bare-dirt** face (right) — two surface types in one frame.
- Sun: `time.set-local 0.30` → elevation 0.31 (~18°, oblique — the diagnostic angle; noon saturates both
  diffuse terms). Frozen: `time.freeze true`, `weather.freeze true`, `weather.wind-speed 0`.
- Control surface: the single runtime `Planet/VertexColor (runtime)` material; props set via
  `Material.SetFloat` (live, no recompile) except the H2 curve, a temp shader probe (reverted, diff clean).
- Metric: luma std-dev over a fixed **open-dirt window** (and a **grass window** for cross-surface).
- Captures in `local-only/agent-captures/terrain-relief/` (`tr_baseline`, `tr_str12`, `tr_widened_str2`, …).

## Prediction (before results)

Prior belief (memory `project_normal_mapping_flat`): "data pipeline confirmed working; lighting compression
(H2) likely the cause." Predicted H2-curve would be the dominant lever.

## Results — relief-contrast (luma std-dev)

| Cell | dirt window | vs no-normal floor | grass window |
|---|---|---|---|
| strength 0 (no normal map) | 0.0609 | — (macro/albedo floor) | — |
| **baseline** (strength 2, tiling 0.055, narrow curve) | **0.0649** | +6.6% | 0.0724 |
| strength 12 | 0.0929 | +53% | 0.0642 (−11%) |
| tiling 0.055 → 0.0055 | 0.0752 | +23% | — |
| widened curve `lerp(0.02,1.35)`, strength 2 | 0.0697 | — | — |
| widened curve, strength 0 | 0.0590 | — | — |

Derived normal-map contribution (str2 − str0): **narrow curve 0.0040 → widened curve 0.0107 (2.7×)**.

Mode 83 (×20 normal-delta probe): vivid speckle everywhere → normals reach the shader and perturb (÷20 the
raw perturbation is subtle, ~0.011). Mode 82 (H2 negative control) unchanged by the curve edit, as predicted.

## Refutation table

| Hypothesis | Verdict | Evidence |
|---|---|---|
| H1-placeholder / H3 (normals absent / not reaching shader) | **REFUTED** (regression pass) | mode 83 vivid speckle, count=16/16 slices |
| **H1-amplitude** — source relief weak at default strength | **CONFIRMED, PRIMARY (bare-ground only)** | default strength 2 adds only +6.6% over the no-normal floor; strength 12 = +53% and visibly granular ground |
| **H1-tiling** — 0.055 too high-frequency (sub-pixel at altitude) | **CONFIRMED, secondary** | tiling /10 = +23%; but introduces far-slope grid aliasing |
| **H2-curve** — `dayLight` floor 0.24 / slope compresses relief | **CONFIRMED, minor** | widening amplifies the normal contribution 2.7× (0.004→0.011) + adds macro contrast; but it amplifies a weak signal, so absolute gain is small |

## Verdict: **MIXED — primary H1-amplitude, scoped to bare-ground biomes**

The prior (H2-dominant) is **refuted as the primary cause.** Bare terrain reads flat because the normal map
is near-invisible at the **default `_BiomeNormalStrength = 2`** — it contributes ~6% of surface variation, so
the ground is essentially its flat albedo. The biggest single lever is **normal strength** (str12 = +53% on
dirt, a night-and-day visual change to granular ground). Tiling frequency and the lighting curve are real but
secondary/minor.

**Scope (important):** the strength lever helps **bare-ground** biomes (dirt/rock/desert) only. In the grass
biome the window contrast did **not** rise (−11%) — the grass blade/blanket layer dominates those pixels, so
terrain-normal strength is invisible there. Grass biomes also don't *look* flat (the grass is their texture).
So this is a bare-ground-terrain fix, not a global one.

## Recommended fix (separate `pp-change-control` change — NOT applied by this diagnosis)

- **Raise `_BiomeNormalStrength`** from 2 toward **~5–6** (not 12 — strength 12 aliases the far tiling). This
  is the highest-leverage change and directly convicts the flatness. Value is a `pp-change-control` F10 A/B
  call (Bryan's eye).
- **Optionally lower `_BiomeTriplanarTiling`** a little (0.055 → ~0.03) to enlarge the texel footprint so the
  authored detail isn't sub-pixel — but watch the far-slope grid aliasing the extreme /10 showed; a mild
  change only.
- **Optionally widen the `dayLight` curve** slightly (e.g. floor 0.24 → ~0.15) for macro contrast/drama — a
  minor polish lever, not the fix. Re-verify grass/terrain brightness parity if touched (the look arc tuned
  the foliage/grass ambient floors to match terrain).
- Cross-biome caveat: confirm the strength value on one more bare-ground biome (desert/rock) before shipping
  it globally; grass/forest floors are unaffected.

## Shader tree state

`git diff -- Assets/Graphics/Shaders/PlanetVertexColor.shader` is empty — the temp curve probe was reverted
and force-reimported. No visual constant was changed and committed by this diagnosis.
