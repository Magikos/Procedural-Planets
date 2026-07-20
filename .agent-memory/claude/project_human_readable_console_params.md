---
name: project-human-readable-console-params
description: "Backlog + convention — console command params should take human-readable values (0-1 or real units), converting to raw shader/physics coefficients internally"
metadata: 
  node_type: memory
  type: project
  originSessionId: 5a0ee82f-d367-47b6-bbee-397761463f85
---

**Backlog task (Bryan, 2026-07-06):** convert console command parameters that take raw
physical / shader-space coefficients (e.g. `0.002`, `0.000375`) into human-readable inputs
— a 0-1 fraction or a real unit (metres, %) — and convert to the internal coefficient
inside the setter. The math stays physical in the shader; the *command* speaks human.

**Why:** raw coefficients are unmemorable and non-linear (0.0005 vs 0.002 means nothing to
a human; the useful range is tiny and exponential). Bryan reasons in "0% / 50% / 75% fade",
not per-metre extinction rates. This recurs across many cloud/atmosphere/precip tuning
commands.

**Reference implementation (the template to copy):** `cloud.aerial-fade` in
`Assets/Scripts/Planet/Clouds/CloudController.cs`. Command takes 0-1; `CloudController`
converts to the shader's Beer-Lambert coefficient via
`density = -ln(1 - fade) / AerialReferenceDistance` before `Shader.SetGlobalFloat`. The
shader global `_CloudAerialDensity` stays a physical per-metre rate; `CloudConstants.AerialFade`
is authored 0-1. Pattern: human field on the controller → convert on upload → physical global.

**Candidates to audit/convert** (not exhaustive — do a full sweep when the task is picked up):
`cloud.density` (0-0.08 multiplier), `cloud.debug-threshold` (0-0.01),
`cloud.debug-saturation` (0.0005-0.02), and any `precipitation.*` / `atmosphere.*` /
`weather.*` setter exposing a raw coefficient or shader-space magnitude. Grep starting point:
`grep -rn "ConsoleCommand(" Assets/Scripts --include=*.cs` then inspect each numeric range.

**Convention (ADOPTED):** *console setters take human units (0-1 or real units) and convert
to internal coefficients; never expose a raw shader/physics coefficient as a command
parameter.* Promoted into the pp-change-control skill §4 non-negotiables (2026-07-17), so
future knobs follow it. Template: `AtmosphereController.MieCmd` / `CloudController.AerialFadeCmd`.

**Single-source refinement (2026-07-18):** the physical range lives ONCE as a `public const`
on the settings SO / owning state class (`CloudSettings.DensityMin/Max`,
`AtmosphereSettings.SunDiscBlendMin/Max`, `CloudDebugState.CondensationChange*Max`), referenced
by BOTH the SO `[Range(Min, Max)]` inspector attribute and the console mapping. Editing a range
= one-line edit on the SO; slider + console can't drift. Shaders can't be the source: globals
carry no range at runtime (editor-only `ShaderUtil` sees only per-material `Properties{Range()}`),
so the SO is the correct single source. Caught+fixed a pre-existing `sun-disc-blend` drift
(SO slider 0.01 vs console 0.05 → unified to 0.05).

**Status:** DONE (2026-07-17). Full sweep of cloud/atmosphere/precip/weather/rain/lightning
console commands. The rest were already human (metres, counts, 0-1, 0-2, enums). Converted the
four remaining raw-coefficient setters to 0-1: `cloud.density` (was 0-0.08), `cloud.debug-threshold`
(was 0-0.01), `cloud.debug-saturation` (was 0.0005-0.02), `atmosphere.sun-disc-blend`
(was 0.0001-0.05, `Lerp`/`InverseLerp` to keep the nonzero floor). `…HumanMax` consts hold the
mapping. Build clean. NOTE for Bryan: any muscle-memory raw values for these four now read as
0-1 (e.g. old `cloud.density 0.03` ≈ new `cloud.density 0.38`).
