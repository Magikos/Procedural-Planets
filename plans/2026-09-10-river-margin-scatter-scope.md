# River margin scatter — proposed scope

**Status:** Findings and proposal only. No Assets changes, imports, builds, tests, or Unity calls.
Baseline: `harvest-vertical-slice`, `d1e0f62`, with concurrent river and background-planner changes, inspected 2026-09-10.
Preserve those changes. Coordinate shared files before implementation.

## Visual target

Bryan supplied two river references on 2026-09-10.
Both show grouped lilies beside upright reeds or cattails, with substantial open water between groups.
The first emphasizes bank pockets. The second emphasizes close foreground plants against an open water surface.
Use these relationships for sheltered margins. Buildings, bridges, lighting, and color grading are outside this contribution.
The screenshots do not establish numeric depth or velocity thresholds.

## Existing support

| Need | Current support | Missing condition |
|---|---|---|
| Bed-grounded reeds | Analytic carved ground, slope and altitude gates | Explicit shallow freshwater habitat; current reeds have no lower altitude bound |
| Floating lilies | Local river/basin water radius and radial placement | Bed-depth gate, flow-speed gate, freshwater identity, shelter evidence |
| Grounded rocks | Analytic ground, slope conformity, footprint sinking | River-margin targeting without depending only on LakeShore biome |
| Dense plant groups | Deterministic candidates and clumping | Lily candidate density compatible with colony size |

Use existing assets, verified as files in the project:

- `Assets/Resources/Settings/Scatter/Lake Reeds.asset`: spacing 6 m, maximum altitude 3 m, no minimum altitude.
- `Assets/Resources/Settings/Scatter/Lake Cattails.asset`: spacing 4 m, maximum altitude 3 m, no minimum altitude.
- `Assets/Resources/Settings/Scatter/Lake Lily.asset`: spacing 12 m, colony scale 9 m, clumpiness 0.95, floating enabled.
- `Assets/Resources/Settings/Scatter/Lake Rocks.asset`: spacing 28 m, terrain conformity 1, minimum water clearance 0.05 m.

PlantInjection can replace plant geometry. Verify the effective runtime route before judging the asset meshes.
Visual suitability remains unverified in Unity. No new asset purchase, import, or generator is proposed.

## Current code evidence

- `ScatterField.cs:139` and `ScatterGatherJob.cs:144` now select river water radius. Both ground paths include river carving.
- `ScatterField.cs:348` still substitutes zero altitude for floating plants. The Burst path retains the same defect.
- `WaterQueryJobData.cs:23` contains the shared `WaterQueryKernel`; its result includes body depth, ocean identity, and velocity.
- `Rivers/RiverField.cs:13` stores segment speed and waterfall status in `Shape`.
- `Rivers/RiverField.cs:89` restricts successful water sampling to the channel width. It is not a general dry-bank proximity query.
- `Rivers/RiverGenerator.cs:75` assigns segment speed; pool shaping at `:143` reduces that speed.

All source paths above are under `Assets/Scripts/Planet/`.
Speed is segment-level. The query does not establish that a bank pocket is sheltered merely because it is shallow or near an edge.
Do not label a reach as sheltered from speed alone. For a strict first pass, withhold river lilies where shelter is unknown.
Reeds and cattails currently depend on LakeShore biome membership. River height support alone does not guarantee that membership along every reach.

## Bounded implementation proposal

1. Preserve actual bed depth separately from floating transform height in both scatter paths.
2. Add an opt-in aquatic habitat rule to the existing prototype, DTO, and native parameter flow.
3. Reuse shared water sampling semantics for freshwater identity, depth, and world-space speed. Avoid a second hydrology model.
4. Place reeds and cattails on the carved bed within an authored shallow bank band, in irregular groups.
5. Place lilies only within an eligible shallow, slow, freshwater region with explicit shelter evidence.
6. Reuse existing grounded rocks along suitable margins. Preserve open channel views and sparse banks.
7. Tune lily candidates and colony scale together after baseline captures. Keep other vegetation density unchanged.

Expected code scope: `ScatterPrototype.cs`, `ScatterDtos.cs`, `ScatterField.cs`, `ScatterGatherJob.cs`, existing placement helpers,
and focused scatter tests. Use one shared habitat predicate where possible.
`ScatterTileCache.cs` is a coordination dependency only if immutable water-query inputs require new plumbing.
Do not replace its completed background planner or move candidate work onto the main thread.
Coordinate any water-query lifetime or bank-query extension with the river owner before editing those files.
Keep shader files, river mesh generation, fish planning, and unrelated tree-clumping corrections outside this contribution.

## Queued validation

This is a written backlog, not scheduled execution. The river task owns Unity after Performance releases it.

- Record unchanged transition captures with sparse or absent decoration before evaluating shading.
- Test dry, shallow, deep, freshwater, ocean, fast-flow, and unknown-shelter cases in both gather paths.
- Test raised basins, supported transform scales, channel edges, and waterfall-adjacent regions.
- Verify managed/Burst parity, deterministic tile revisits, and unchanged background-planner ownership.
- Capture grouped plants from bank and water-level viewpoints. Keep open water visible for shading comparison.
- Compare gather candidate counts and timings before accepting denser lily placement.

Depth, velocity, and shelter thresholds remain to be defined before implementation and visual tuning.
The existing broader review and queue remain in `2026-09-09-vegetation-scatter-review.md`
and `2026-09-09-vegetation-scatter-validation-queue.md`.
