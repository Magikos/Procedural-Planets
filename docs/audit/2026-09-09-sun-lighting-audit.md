# Audit Summary

**Current status, 2026-09-09:** Bryan approved all five fixes. They are implemented; 70 focused Unity tests pass.
The original findings below describe the pre-fix tree. The final validation record appears at the end.

**Findings only — no code changed.**

Reviewed 2026-09-09 on `harvest-vertical-slice`, HEAD `d1e0f62`, with extensive concurrent working-tree changes.
The review used current source, including modified celestial, atmosphere, terrain, and console code.
Five findings remain open: four medium-severity bugs and one medium-severity synchronization risk.
No previous findings were resolved by this review. No previous audit files were removed.

Scope: sun direction, time conversion, shadow alignment, atmosphere publication, sun-disc masking, and shared surface lighting.
Moon math and existing tests were inspected where they share sun state.
This is not a full water, cloud, moon presentation, or render-pipeline audit.

Unity remains with another agent. Builds, Unity tests, shader compilation, and captures were not run.
The [validation queue](../../plans/2026-09-09-sun-lighting-validation-queue.md) records deferred work.

## What came back clean

- `MoonOrbit.Frame` produces a unit sun direction. `CelestialManager` aims the directional light along its negative.
- The local-noon inverse selects the maximum sun elevation in the permitted daily plane. Its pole rejection is appropriate.
- Shadow elevation uses the correct sign and sine ratio for validated settings between zero and 90 degrees.
- `MoonOrbit.Advance` validates progress and elapsed time. Invalid cycle duration stops advancement rather than dividing by zero.
- `PlanetSunLighting.hlsl` already shares daylight and cast-shadow functions across surface shaders.
- Existing `MoonOrbitTests` cover phase invariance, duration validation, and projection scaling. They were read, not executed.

Graphify query completed. Direct source reads verified the findings.
PowerShell evaluations confirmed the horizon discontinuity and nonzero specular response with negative surface illumination.
These evaluations reproduce scalar formulas; they are not GPU or Unity test results.

# Findings

## S01 — Atmosphere re-enable leaves optical depth unbound

**Category:** Bug. **Severity:** Medium. **Effort:** S. **Fix Risk:** LOW. **Confidence:** HIGH.

**Description:** Disable and re-enable an initialized atmosphere controller without regenerating the planet.
`OnDisable` clears its global texture. `OnEnable` restores subscriptions but does not restore the texture.
Clean static properties prevent another upload, and only baking binds the texture.

**Evidence:** `Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs:60`, `:85`, `:143`, `:242`.
`Assets/Graphics/Shaders/Includes/Atmosphere.hlsl:83` samples the missing optical-depth texture.

**Impact:** Sun extinction loses the baked optical-depth data after re-enable.

**Recommendation:** Rebind the existing texture on re-enable. Use the existing bake path when the texture is unavailable.
**Refactor Option:** None.
**Behavior note:** Restores rendering after component re-enable. Validate a matched capture pair.

## S02 — Manual sun direction does not survive an update

**Category:** Bug. **Severity:** Medium. **Effort:** S. **Fix Risk:** MED. **Confidence:** HIGH.

**Description:** Run `light.direction` while an initialized `CelestialManager` controls the sun.
The command changes the light and shader global without changing authoritative celestial state.
The next celestial and atmosphere updates restore the previous direction. Freezing time does not stop these writes.

**Evidence:** `Assets/Scripts/Planet/LightingDebugCommands.cs:30`, `:130`;
`Assets/Scripts/Planet/CelestialManager.cs:163`, `:250`;
`Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs:90`.

**Impact:** Lighting diagnostics report success but cannot hold the requested direction.
The local-noon fallback has the same problem when the celestial pole prevents time conversion.

**Recommendation:** Route manual direction through the celestial owner with an explicit override and reset operation.
Preserve the existing fallback for scenes without a celestial owner.
**Refactor Option:** Reuse one direction publication path for commands and normal time progression.
**Behavior note:** Makes the diagnostic direction persist. Define how normal time resumes.

## S03 — Shader sun and shadow light can use different frames

**Category:** Bug. **Severity:** Medium. **Effort:** S. **Fix Risk:** LOW. **Confidence:** HIGH for ordering risk.

**Description:** The atmosphere publishes `_SunParams` in its `Update`.
The celestial manager advances time and rotates the light in a separate `Update`.
No inspected execution-order declaration makes the producer run first.

**Evidence:** `Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs:90`;
`Assets/Scripts/Planet/CelestialManager.cs:160`, `:250`.
Both script metadata files omit execution-order overrides.

**Impact:** When atmosphere runs first, surface lighting uses the previous sun direction while cast shadows use the current direction.
At 60 FPS and a 120-second day, the difference is 0.05 degrees. Faster days amplify it.
Current live ordering and visible severity remain unverified.

**Recommendation:** Publish the sun global after all sun updates, through the existing late publication convention or the celestial owner.
**Refactor Option:** Share S02's authoritative publication path. Do not add execution-order attributes.
**Behavior note:** Synchronizes lighting and shadows without changing the orbit.

## S04 — Sun horizon visibility jumps at the planet tangent

**Category:** Bug. **Severity:** Medium. **Effort:** S. **Fix Risk:** MED. **Confidence:** HIGH.

**Description:** Above `1.002 * _SeaLevelRadius`, the shader applies its soft horizon transition only to intersecting rays.
The intersection flag changes abruptly at the tangent.

**Evidence:** `Assets/Graphics/Shaders/Star.shader:109` through `:116`.
Normalized clearance `-0.000001` produces visibility `0.499999250`; `+0.000001` produces `1.000000000`.

**Impact:** The mask has a hard brightness discontinuity despite its smoothstep.
The sun disc and stars can show a sharp edge at an elevated horizon.
Later opaque rendering can hide portions of this artifact.

**Recommendation:** Apply the clearance transition on both sides of the tangent for planet-facing rays.
Keep rays pointing away from the planet fully visible.
**Refactor Option:** None.
**Behavior note:** Changes horizon pixels. Preserve the selected softness and verify continuity with captures.

## S05 — Terrain specular does not reject unlit surface normals

**Category:** Bug. **Severity:** Medium. **Effort:** S. **Fix Risk:** MED. **Confidence:** HIGH.

**Description:** Terrain computes a Blinn highlight without multiplying by surface illumination or rejecting negative `N dot L`.
Planet daylight and shadow-map visibility cannot replace this surface-normal test.

**Evidence:** `Assets/Graphics/Shaders/PlanetVertexColor.shader:1229` already computes `terrainDiffuse`;
`:1254` through `:1259` omit it from specular lighting.

**Impact:** A slope or normal-map detail facing away from the sun can retain a sunlight highlight.
For view direction equal to the surface normal, `N dot L = -0.1` gives `N dot H = 0.670820`.
At smoothness `0.2`, the existing specular magnitude remains `0.001558` before color and visibility factors.
This proves the missing gate, not the visible severity in the current scene.

**Recommendation:** Gate or scale specular with the existing `terrainDiffuse`.
Guard the half-vector when view and sun directions cancel.
**Refactor Option:** None. Reuse the existing illumination value.
**Behavior note:** Changes terrain highlights, including wet surfaces. Capture before and after correction.

# Refactoring Plan

These are proposed slices, not implementation authorization.

1. Restore atmosphere texture binding for S01. Validate disable/re-enable without regeneration.
2. Unify direction ownership and publication for S02/S03. Validate frozen commands and both update orders.
3. Correct the horizon mask for S04. Validate tangent continuity and outward-facing rays.
4. Correct the terrain highlight gate for S05. Validate lit and unlit normal-map details.

Use the existing controller and shared shader helpers. No new subsystem or dependency is needed.
The queue defines regression fixtures, capture conditions, and expected results for each slice.

# Prior Audit Reconciliation

| Prior item | Status | Current evidence |
|---|---|---|
| `current.md`, F-2026-08-18-dup-shader-lighting | PARTIAL | Shared daylight and cast-shadow helpers now exist in `PlanetSunLighting.hlsl`. Other duplicate shader regions remain outside this scoped review. |
| 2026-07-25 scatter F3, dark props | REJECTED as a new defect | Historical appearance decision, not evidence of wrong sun math. Existing artistic floors were retained. |
| 2026-07-22 former G2, albedo/lighting mismatch | REJECTED | Prior visual change was reverted. This review does not reopen that tuning decision. |

The 2026-09-07 weather audit contains no matching finding requiring reconciliation here.
Its weather findings remain outside this scoped review.
Known grazing-shadow softening is deliberate and supported by prior capture history.
The global time tooltip also disagrees with the event's fixed reference meridian. This is a documentation follow-up, not an orbit rewrite recommendation.

# Questions for the User

None required to complete the review. Select `fix`, `defer`, or `wontfix` before implementation.

No product source changed. No Graphify update was required.

## Unity follow-up — 2026-09-09

Bryan handed over Unity after the source review. The queued baseline run passed all 63 tests, with zero failures or skips.
Runtime probes reproduced S01, S02, and S03. GPU probes reproduced S04 and S05 using extracted production shader code.
The [validation record](../../plans/2026-09-09-sun-lighting-validation-queue.md#executed-results--2026-09-09) contains exact measurements and evidence paths.

S03 is now observed in the live scene, beyond the earlier ordering-risk analysis.
S04/S05 are confirmed formula defects; their visual severity at ground level remains unmeasured.
Orbit captures exist for sunrise, noon, sunset, and midnight. Final appearance approval remains pending.
No fixes were applied. The original animation review scene was restored in unpaused Play mode after testing.

## Approved implementation — 2026-09-09

S01 through S05 are resolved in code. This supersedes the original findings-only status for the implementation follow-up.

| Finding | Change | Verification |
|---|---|---|
| S01 | `AtmosphereController.OnEnable` refreshes bindings; missing or released textures use the existing bake path | Re-enable and released-texture regressions pass; live re-enable preserves `BakedOpticalDepth` |
| S02 | `CelestialManager` owns the override; commands use shared `SunLighting` publication | Manual direction persists; reset, time changes, unfreezing, and pole fallback work |
| S03 | Removed atmosphere's sun publication and serialized celestial dependency | Moving live sun has shader error `0`; light error `6.143906e-8` |
| S04 | `PlanetHorizonVisibility` applies its fade across the tangent while preserving outward rays | GPU continuity and outward-ray tests pass |
| S05 | `PlanetSunSpecular` gates direct highlights and safely normalizes the half-vector | GPU unlit and opposing-vector tests pass |

The prior light-transform duplication in `CelestialManager.UpdateSun` and `LightingDebugCommands.ApplySunDirection` now uses `SunLighting.Apply`.
Production shaders and GPU regressions call the same functions in `PlanetSunLighting.hlsl`.

Core and Planet builds passed. All 70 focused Unity tests passed; zero failed or skipped.
The [validation record](../../plans/2026-09-09-sun-lighting-validation-queue.md#implemented-fixes--2026-09-09) records warnings, the corrected test setup, and visual evidence.
Bryan's visual acceptance remains separate from implementation and automated correctness.
