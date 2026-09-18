# Vegetation habitat implementation — 2026-09-12

Bryan authorized implementation after reviewing the scatter findings.
The changes extend the existing deterministic scatter, tile cache, generated meshes, and grass placement paths.
No interaction source, scene, or prefab changed. No commit was made.

## Changes

- `VegetationHabitat.cs` supplies a world-seeded woodland field independent of each species' colony scale.
- Blended biome membership controls woodland potential. Forest, tropical, and taiga permit denser woodland than grassland and steppe.
- Flat terrain favours openings. Broad open regions remain possible within and between woodland regions.
- Habitat suitability and species colonies now act independently. Strong shade preference no longer removes colony contrast.
- Generated trees retain their species pool, variant count, and slot allocation. A separate age permutation removes the fixed primary-species/youngest-stage assignment.
- Cover favours younger shapes at woodland edges and older shapes inside woodland. The age distributions overlap.
- Both grass kernels use the shared woodland formula. Maximum cover reduces grass density to 15% of its previous value.
- Both scatter gathers retain bed altitude for floating plants. Their render anchor remains on the water surface.
- Water identity comes from the existing map/catalog, cached by both identities and uploaded through the existing native input lifecycle.
- Verified river hits take precedence over the body grid. Lake reeds and cattails can use slow freshwater reaches.
- Validation rejects invalid clumping, age, habitat, and flow settings.

The clumping revision affects other opted-in prototypes too. Their old counts are not preserved.
Rock grounding, slope conformity, water clearance, and authored rock assets remain unchanged.

## Aquatic authoring

Altitude means signed bed height relative to the local water surface, in world metres.

| Asset | Bed altitude | Spacing | Colony scale | Water identity / flow limit |
|---|---|---|---|---|
| Lake Lily | -2 to -0.15 m | 1.5 m | 12 m | Lake only; river hits rejected |
| Lake Reeds | -0.8 to 0.5 m | 2.5 m | 18 m | Freshwater; maximum 0.5 m/s |
| Lake Cattails | -0.6 to 0.35 m | 2.5 m | 14 m | Freshwater; maximum 0.4 m/s |
| IceBog Reeds Prototype | -0.8 to 3 m | Existing | Existing | Existing biome rules |
| Swamp Reeds Prototype | -0.8 to 3 m | Existing | Existing | Existing biome rules |
| LMHPOLY Beach Reed | -0.8 to 3 m | Existing | Existing | Existing coastal biome rules |

The last three assets retain their upper bounds. Their new lower bounds prevent arbitrarily deep placement.
No shelter signal was invented. River lilies remain excluded.

## Compatibility and cached images

Existing prototype slot IDs and generated variant counts remain unchanged. Saved records are not rewritten.
Tree geometry can change at an existing slot because age assignment changed.
Lily, lake reed, and cattail spacing changes can change candidate grid levels and instance IDs.
The new habitat field changes accepted locations after regeneration.

The age changes invalidated 62 tree atlas entries. Those entries were rebaked with the existing baker and import configuration.
The scoped bake preserved other manifest entries. It did not delete orphans or rebuild unrelated plant/rock cards.
A subsequent lookup found all 74 required tree cards, with zero missing. Broadleaf and conifer images were inspected.
Three previously missing forage-grass cards remain outside this tree-cache update.

Dirty working-tree originals and the atlas cache were preserved under `local-only/validation/vegetation-implementation-originals/`.

## Validation

- Core build passed with zero warnings and errors.
- Final Planet build passed with 19 warnings and zero errors. Log: `local-only/validation/vegetation-planet-build-final.log`.
- Unity imported the C#, shaders, and authored assets. Fresh Play Mode runs generated the planet successfully.
- Final focused EditMode run passed 76 tests, with zero failures or skips, in 2.7077778 seconds.
- Job: `d37c804322e241ce8b81afc41e29885d`. Full results: `local-only/validation/vegetation-final-tests.json`.
- `git diff --check` passed for the modified tracked code paths.
- `graphify update .` completed. Log: `local-only/validation/vegetation-graphify-update.log`.

Tests cover baseline and active-habitat managed/Burst parity, transformed aquatic placement, ocean rejection, imported depth bands,
CPU/GPU habitat agreement, open plains, slope direction, age preference, grass suppression, independent flower overlap, and existing scatter invariants.
The aquatic parity fixture uses a raised lake and a translated, rotated planet at twice normal scale.

The live lake sample used seed `1691104419` and a 60 m gather around the recorded baseline pose.
It contained 125 lily pads at depths from 0.1572266 to 1.999023 m, with zero depth violations.
Of those pads, 119 had another pad within 3 m. This supports local grouping, not a planet-wide distribution claim.
Result: `local-only/validation/vegetation-lake-runtime.txt`.

## Captures and limits

After images and metadata: `local-only/debug-screenshots/baselines/2026-09-12-vegetation-after/`.

- `forest-matched.png`: the former forest viewpoint now lies in an opening.
- `lake-matched.png`: open bank with shallow lily placement.
- `grassland-matched.png` and `steppe-matched.png`: recorded baseline viewpoints.
- `mature-woodland.png`: a separate sample with cover 1, trees, and reduced grass.
- Archived `F10-*` files: lake diagnostic capture set.

Matched images reuse baseline seed, camera pose, and quality. Weather was uncontrolled.
The earlier baseline used `Camera.Render` without F10 diagnostics. These images are not a controlled lighting or performance comparison.
The first `forest-after.png` used a biome teleport that selected another point. Use `forest-matched.png` for the recorded pose.
Synchronous scatter counts are not rendered counts or frame timings. No performance improvement is claimed.

Cover estimates habitat suitability, not actual tree-crown occupancy. It does not update when trees are chopped.
The finite generated variant pool limits the age shapes available to an individual species. No time-based growth was added.
Biome profiles use biome types and blended membership. No new climate simulation or groundwater model was added.
Bryan's visual review remains required to approve the look.

## Corrected setup errors and warnings

The initial Planet dotnet build used stale project files and reported `CS0246` for `ScatterWaterHabitat`.
Unity compiled the new files. Regenerating the IDE project files corrected the dotnet build.
Direct probes reported these errors:

```text
Line 1: `UnityEditor.SyncVS' is inaccessible due to its protection level
Line 1: `DebugCapturePipeline' is inaccessible due to its protection level
```

Reflection through the existing entrypoints completed project regeneration and the F10 capture.
Existing foliage tint warnings remain. The final console contained 18 analyzer warnings and a test-result-save message classified as `Exception`.
It contained no reported runtime or compiler failure. Sample: `local-only/validation/vegetation-final-console.json`.

## Unity handoff

Interaction System explicitly authorized the Unity window.
Scatter restored `Assets/Scenes/Tests/SidekickInteractionReview.unity` as the only loaded scene, active and clean, with 41 roots.
Unity reported `playing=False; changing=False; compiling=False` after the final tests.
Scatter released Unity with no pending tests, imports, captures, or Play operations. Interaction accepted ownership.
