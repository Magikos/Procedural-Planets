# Animal animation and fish avoidance validation

Status: automated fixtures passed on September 10 after Bryan released Unity.
The combined run passed 123 tests (`6939087367444be68aed7b2d1ab19cf6`), followed by 10 animation performance tests (`937eee438fdb446f9b1866a902e1462c`).
The live visual checks below remain separate from those automated results.

## Changes

- Threat sources can carry an avoidance class. Existing faction-only callers retain their behavior.
- Small fish fear nearby actors outside their class, including passive animals, players, and sharks.
- Small fish share a class across freshwater and coastal habitats. The class is independent of their model asset.
- Sharks retain their existing faction policy rather than fleeing their own food.
- Explicit disguises retain faction-based handling and terrain screening remains in the shared query.
- Fish retain a detected threat position for three simulation seconds after detection ends.
- Escape uses twice the cruising speed and a faster turn response.
- Overhead threats produce lateral escape; depth limits prevent escape from driving fish through the bed or surface.
- Group threat registration is removed on disappearance, observation departure, clear, and disposal.

## Animation review

`CreatureAnimationView` still uses `ActorAnimationGraph` with generic avatars.
Humanoid bindings remain in the humanoid presenter. Animal callers do not supply humanoid contact arrays.
`FootPlacementSolver` and `ProceduralPoseRig` retain optional contact controls with defaults for existing animal callers.
Animal swimming releases land IK and uses secondary motion. Death settling remains separate from live pose evaluation.
`FishView` still owns its existing swim-only PlayableGraph. This pass does not replace that working presentation path.
These are source findings, not proof of live visual correctness.

## Completed code checks

- Core build: passed, zero warnings, zero errors.
- Planet build: passed, 19 warnings, zero errors.
- Final EditMode test assembly build: passed, 21 warnings including referenced Planet warnings, zero errors.
- Logs: `local-only/animal-validation/core-build.log`, `planet-build.log`, and `test-build.log`.
- No Unity test or Play mode action was executed in this pass.

## Queued automated tests

Run these EditMode fixtures after Unity is free:

1. `ProceduralPlanets.Tests.FishMovementTests`
2. `ProceduralPlanets.Tests.FishPopulationTests`
3. `ProceduralPlanets.Tests.CreatureThreatTests`
4. `ProceduralPlanets.Tests.CreatureSwimmingTests`
5. `ProceduralPlanets.Tests.ProceduralPoseTests`
6. `ProceduralPlanets.Tests.ActorAnimationGraphTests`
7. `ProceduralPlanets.Tests.ActorAnimationPerformanceTests`

New checks cover class filtering, passive animal threats, memory expiry, continued escape direction,
overhead threats, habitat containment, and all configured animal state graphs with skipped pose updates.
Existing checks cover disguise behavior, fish model animation and graph cleanup, animal swimming,
foot placement, generic graph layers, and presentation budgets.

## Queued live checks

- Fresh animal review: deer, wolf, rabbit, and a legless creature. Check walk, run, turn, rest, eat, swim, and death.
- Check birds in the Planet scene for normal flight and landing. Confirm no humanoid rig requirement or pose exception.
- Approach a small fish school as the player, then with a passive animal and a shark.
- Confirm same-class schools do not startle each other. Confirm an observer-only free camera causes no escape.
- Remove the detected threat. Confirm escape persists briefly and settles without repeated turnbacks.
- Approach fish from above in shallow water and near a shoreline. Confirm lateral travel stays underwater and inside the same body.
- Compare fish counts and frame timing before and after approach; record captures and logs before claiming visual or performance success.

Acceptance remains pending until these checks run. Record exact failures and evidence here.
