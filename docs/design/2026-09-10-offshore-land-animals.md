# Offshore land animals

## Diagnosis

The live Planet contained 1,810 recorded land residents, including 562 homes over deep water and 32 active offshore land animals.
These counts exclude fliers. They are a diagnostic sample, not the total planet population.

`ResolveSlot` fell back to a saved position when no suitable home existed. That fallback bypassed the water habitat check.
Existing residents also returned before rechecking their home. Ordinary movement had no water-entry policy outside resource approaches.

## Correction

- Saved positions must pass the same water habitat check before becoming fallback homes.
- Existing homes are rechecked when their slots are planned.
- A saved animal without a valid home remains in the save log. It is not marked dead or deleted.
- `CreatureWaterAvoidance` blocks casual deep-water entry. Fleeing animals may enter lakes or the ocean.
- Active fleeing keeps control of swimming direction. Swimmers head toward their dry home after fleeing ends.
- Fliers and species with explicitly aquatic altitude bands keep their previous movement path.

## Validation

- Core build: zero warnings and errors.
- Planet build: 19 warnings, zero errors. Logs: `local-only/animal-validation/ocean-build.log` and `ocean-core-build.log`.
- 123 EditMode tests passed: job `6939087367444be68aed7b2d1ab19cf6`.
- 10 animation performance tests passed: job `937eee438fdb446f9b1866a902e1462c`.
- The persistence test verifies no underwater materialization and recovery of the same identity when habitat becomes valid again.
- The new movement check distinguishes casual lake entry, emergency lake entry, and ocean entry.
- This run also executed the automated fish and animal animation queue from September 9.

Live post-restart verification found 362 land residents in the current region, zero underwater homes, and zero active offshore land animals.
The old and new resident totals cover different observation histories; they are not a population-loss measurement.
A direct observation check at the previously affected ocean location `(-300, 4600, 1900)` reported 136.50 m water depth and zero live land animals.
The check restored the original observer afterward.

Bryan clarified that ocean entry must remain available during escape. The policy now permits both ocean and lake entry while fleeing.
Active fleeing also bypasses the return-home steering. The focused persistence/swimming rerun passed 36/36 tests, job `b87c19b00ff54db0a6a2d74d12f15580`.
Planet build passed with 19 warnings and zero errors. Unity was left stopped for the Rivers agent.
