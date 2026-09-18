# Atmosphere improvements

Status: Implemented and validated in Unity.
Readiness, texture ownership, and settings validation fixes are complete.

The review found three concrete defects in the current working tree on `harvest-vertical-slice`, HEAD `d1e0f62`.
Existing concurrent changes remain intact.

| Finding | Evidence | Planned correction |
|---|---|---|
| A01: Disabled or uninitialized atmospheres can schedule a fullscreen pass | `AtmosphereRenderFeature.TryGetLiveController` checks object existence only | Add readiness to the existing runtime interface and require it before scheduling |
| A02: Resizing the optical-depth texture releases GPU storage but leaves its native object alive | `AtmosphereController.BakeOpticalDepth` clears the reference after `Release`; `OnDestroy` also only releases | Use one release-and-destroy path for replacement and teardown |
| A03: Invalid atmosphere settings reach texture allocation and exponential shader math | DTO has no validation; `rayleigh` accepts negative coefficients; scalar clamps preserve NaN | Validate snapshots before consumption and reject invalid commands through the shared executor |

A01 reconciles the atmosphere portion of prior audit F08. Cloud and precipitation readiness remain outside this task.
Prior optical-depth dispatch bounds finding F02 is already fixed in `OpticalDepth.compute`.
The recent sun-publication and atmosphere re-enable fixes remain present.

The review found consistent optical-depth normalization between the compute bake and shader sampling.
The Rayleigh/Mie phase functions and midpoint optical-depth accumulation require no formula rewrite for valid inputs.
No scattering coefficient, artistic constant, or quality default will change.
The stale shader comment claiming no lookup table will be corrected.

Use the existing DTO, controller, runtime interface, and console adapter convention.
Preserve all command names and release policies when moving command methods out of the renderer owner.
Validate invalid input, disabled/readiness behavior, texture resize/destruction, and existing sun/shader/console regressions.
Run serial Core and Planet builds, a fresh Unity check, and `graphify update .`.

## Validation results

- Unity discovered and passed all 77 selected EditMode tests, including seven new atmosphere cases. No tests failed or skipped.
- The tests exercise real compute texture creation, resize, destruction, and atmosphere re-enable behavior.
- Command rejection tests run through `CommandExecutor`. Invalid input preserves the current snapshot.
- Core and Planet builds passed with zero errors. Core reported two analyzer-version warnings; Planet reported 19 existing warnings.
- The first test compile required adding the existing URP runtime reference to the test assembly. After importing the assembly definition, Unity loaded the new tests.
- An earlier 70-test run used old assemblies and was excluded from atmosphere validation.
- `graphify update .` completed. The scoped whitespace check passed.
- No new atmosphere visual comparison or full planet PlayMode run was performed. The changes preserve valid scattering formulas and authored tuning.
- The original, clean `HumanoidAnimationReview.unity` scene was retained and play mode restored.

Evidence: `local-only/debug-screenshots/baselines/2026-09-09-atmosphere/tests-passed.json`, `build-core.log`, `build-planet.log`, and `graphify-update.log`.
