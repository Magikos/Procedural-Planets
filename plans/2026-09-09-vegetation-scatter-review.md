# Vegetation scatter review — 2026-09-09

**Findings only — no code changed.**

The scatter engine has useful foundations. Improve its habitat rules and spatial fields before replacing its candidate generator.
Lily pads need shallow-water eligibility and enough candidates to form colonies. Increasing clumpiness alone will not supply either.

Baseline: `harvest-vertical-slice`, HEAD `d1e0f624ead0448f70a867f20a9389e367527293`, with extensive pre-existing uncommitted work.
Evidence describes the working tree, including authored assets and generated-plant replacement code.
No source, assets, scenes, builds, or Unity state changed during this review.

Scope: managed and Burst scatter placement, clumping, relevant tree/plant assets, generated plant integration, and placement tests.
Rendering received a limited architecture check. This is not a shader, impostor-quality, harvesting, or GPU grass audit.
The separate GPU grass review is `docs/audit/2026-09-09-grass-audit.md`.

## Priorities

| ID | Finding | Priority | Effort | Fix risk | Confidence |
|---|---|---|---|---|---|
| V01 | Floating plants discard the bed depth before eligibility checks | High | M | Medium | High |
| V02 | Lily spacing is too sparse for the authored colony scale | High, visual design | M | Medium | High for configuration; visual effect unmeasured |
| V03 | Species do not share the same woodland field | High | M | Medium | High |
| V04 | Terrain bias reverses the approved clearing preference | Medium | S | Medium | High |
| V05 | Placement parity tests bypass clumping and floating plants | Medium | S–M | Low | High |

Effort includes focused regression coverage. S means hours; M means approximately one working day, excluding visual review.
All placement fixes change visuals. These are proposals, not approved implementation.

## V01 — Preserve depth when placing floating plants

**Category:** Bug / habitat limitation.

Both gather paths sample the ground radius and local water radius. They then set `altitudeMeters` to zero for `OnWater` prototypes.
Consequently, altitude eligibility cannot distinguish a shallow bank, deep lakebed, or dry point within a coarse lake-biome cell.

Evidence:

- `Assets/Scripts/Planet/Scatter/ScatterField.cs:324`: biome membership comes from a coarse cell center.
- `Assets/Scripts/Planet/Scatter/ScatterField.cs:343`: the floating branch separates the placement radius but replaces ground altitude with zero.
- `Assets/Scripts/Planet/Scatter/ScatterGatherJob.cs:139`: the Burst path duplicates the same floating branch.
- `Assets/Scripts/Planet/Scatter/ScatterPlacementMath.cs:39`: the eligibility helper only receives the supplied altitude and clearance rules.
- `Assets/Resources/Settings/Scatter/Lake Lily.asset:27`: both altitude gates are disabled; no depth limit exists elsewhere in this path.

This explains why deeper lake areas remain eligible. Dry-edge candidates are also possible when the coarse biome sample is submerged.
An actual visible dry-edge artifact was not observed during this review.

Keep signed bed altitude as an eligibility input. Use the water radius only for the floating transform.
Reject non-submerged candidates, then apply a shallow-water depth band. Reuse the existing altitude gates where their semantics fit.
A soft density falloff can make the deep edge less abrupt. Author its range against the intended plant size and lake terrain.
Apply the same decision in managed and Burst paths. Preserve the existing per-basin water-level lookup.

**Behavior note:** Pads disappear from unsuitable depths and may move along lake boundaries.
**Refactor option:** Keep habitat eligibility separate from transform anchoring; no new placement framework is needed.

## V02 — Give lily colonies enough candidates

**Category:** Visual design limitation.

The current lily asset already enables strong clumping. Its values are `SpacingMeters: 12`, `Clumpiness: 0.95`, and `PatchScaleMeters: 9`.
Its `Weight` is `1.34`. Therefore, the reported appearance is not caused by clumping being disabled.

Evidence:

- `Assets/Resources/Settings/Scatter/Lake Lily.asset:17`: the spacing and clumping configuration.
- `Assets/Scripts/Planet/Scatter/ScatterQuadtree.cs:23`: spacing selects a candidate grid level.
- `Assets/Scripts/Planet/Scatter/ScatterQuadtree.cs:37`: each cell supplies one jittered candidate for each prototype.
- `Assets/Scripts/Planet/Scatter/ScatterClumping.cs:103`: clumping only multiplies acceptance by a value at most one.
- `Assets/Scripts/Planet/Props/PlantInjection.cs:28`: lilies use one generated variant.
- `Assets/Scripts/Planet/Props/PlantInjection.cs:328`: that variant generates one flattened pad with nominal size `1.05`.
- `Assets/Scripts/Planet/Props/PlantInjection.cs:179`: replacement retains authored placement rules through a DTO copy.

The colony scale is smaller than nominal pad spacing. The algorithm can remove isolated pads, but cannot add neighbors around a surviving pad.
This favors sparse patches rather than dense groups of pads. Runtime colony counts and nearest-neighbor distances remain unmeasured.

Start with a finer lily candidate grid inside a shallow-water mask. Use a colony field to leave open water between dense groups.
Keep within-colony spacing separate from colony diameter and total lake coverage.
Reuse deterministic candidates and existing species grouping first. A parent-and-child colony generator is unnecessary unless this approach fails visual review.

Changing spacing can change the quadtree level packed into `ScatterId`. Check saved identity assumptions before changing established interactive prototypes.
Do not change `SlotId` values or apply a global tree-spacing retune as part of the lily fix.

**Behavior note:** Lily coverage, grouping, candidate counts, and possibly IDs change.
**Refactor option:** None initially. This needs habitat and authoring changes before a new sampling algorithm.

## V03 — Make woodland openness shared across species

**Category:** Bug / architecture mismatch.

`ScatterClumping.Keep` uses the biome seed for openness, but derives its frequency from each prototype's colony scale.
Sharing a seed does not share a spatial field when the frequencies differ.

Evidence:

- `Assets/Scripts/Planet/Scatter/ScatterClumping.cs:61`: `groveFreq` depends on `patchScaleMeters`; `openFreq` derives from `groveFreq`.
- `Assets/Scripts/Planet/Scatter/ScatterClumping.cs:67`: openness samples that differing frequency.
- `Assets/Resources/Settings/Scatter/Forrest Prototype.asset:22`: Forest Tree uses 220 metres.
- `Assets/Resources/Settings/Scatter/Forest Fern Prototype.asset:22`: Forest Fern uses 60 metres and shade preference `0.6`.
- `Assets/Resources/Settings/Scatter/LMHPOLY Forest Mushroom Red.asset:22`: Forest Mushroom uses 18 metres and shade preference `0.8`.

All three belong to biome 7. They therefore follow different woodland maps despite the shade-setting promise.
Undergrowth does not reliably occupy tree stands. Species with different scales can also refill another species' clearings.

Give the biome one openness scale, independent of species colony scale. Feed that same woodland field to tree and undergrowth rules.
Retain the per-species grove field for local variations. Do not implement per-frame tree searches to approximate shade.
This remains a potential canopy map, not actual light or tree occupancy after harvesting.

**Behavior note:** Stand boundaries and undergrowth placement change.
**Refactor option:** Separate shared woodland evaluation from species colony evaluation within the existing clumping code.

## V04 — Correct the terrain-bias direction

**Category:** Bug.

The approved forest design specifies flatter clearings and more wooded slopes.
The code adds `(slopeCos - 0.85f) * TerrainInfluence`, then maps larger values to greater woodland density.
Flat ground has a larger `slopeCos`, so the current bias does the opposite.

Evidence:

- `Assets/Scripts/Planet/Scatter/ScatterClumping.cs:72`: the positive flat-ground bias.
- `Assets/Scripts/Planet/Scatter/ScatterClumping.cs:80`: the increasing woodland response.
- `docs/design/2026-08-15-forest-clumping.md:75`: the approved shared-field and terrain-aware decisions.

Correct the bias direction. Test it independently from maximum-slope rejection, which serves a different purpose.
Use positions in the response transition; saturated samples can conceal the incorrect sign.

**Behavior note:** Groves move relative to terrain. Existing density tuning needs matched captures.
**Refactor option:** None.

## V05 — Test habitat meaning as well as implementation parity

**Category:** Test coverage.

The existing parity fixture uses Grassland, Forest, and Desert prototypes. It leaves clumping disabled and sets `OnWater` false.
Its water-level grid is also disabled. Passing this fixture cannot prove the active lily or clumping behavior.

Evidence:

- `Assets/Tests/EditMode/ScatterGatherParityTests.cs:96`: the prototype helper uses default clumping arguments and `OnWater: false` positionally.
- `Assets/Tests/EditMode/ScatterGatherParityTests.cs:171`: the job uses `WaterLevelRes = 0`.
- `Assets/Scripts/Planet/Scatter/ScatterDtos.cs:40`: clumping defaults to zero.
- `Assets/Scripts/Planet/Scatter/ScatterClumping.cs:58`: zero clumping returns before the spatial field executes.

Extend existing NUnit fixtures with active clumping, shade preferences, floating plants, and nonuniform basin levels.
Add semantic assertions for shallow-water eligibility, shared openness, and terrain bias.
Parity alone permits both implementations to make the same mistake.

**Behavior note:** Preserving; tests only.
**Refactor option:** None. Use the existing test framework.

## What came back clean

- Candidate IDs and parent tiles remain deterministic and independent of camera traversal.
- Managed and Burst gathers already call the same `ScatterClumping.Keep` function.
- Generated variants retain species grouping and inherited placement settings.
- Both floating paths already use local basin water levels. Global sea level is not the primary lily defect.
- `Planet.cs:170` wires `AnalyticGroundSampler` into scatter. The old streaming-LOD ground-sampling defect remains resolved.

These are source observations, not fresh runtime passes. No performance improvement is claimed.

## Proposed order

1. Capture current lake and woodland baselines after the Editor handoff. Run existing regressions.
2. Add focused habitat and clumping regressions alongside each approved fix.
3. Correct lily depth eligibility, then tune candidate spacing and colony coverage together.
4. Separate shared woodland scale from species colony scale, then correct terrain bias.
5. Compare matched captures and density statistics. Check gather cost before accepting denser lily candidates.

Preserve stable identity, fixed tiles, DTO validation, cancellation, and the current rendering pipeline.
Defer broader ecosystem simulation, collision-aware placement, and replacement sampling algorithms until a measured need exists.

## Prior audit reconciliation

The July scatter audits were read. Their placement concerns do not describe these new habitat findings.

| Earlier item | Current treatment | Evidence or limit |
|---|---|---|
| 2026-07-25 F1: streaming terrain placement | RESOLVED | `Assets/Scripts/Planet/Planet.cs:170` uses analytic ground sampling |
| 2026-07-25 F2: synchronous renderer gather | RESOLVED architecturally | `ScatterTileCache.cs:469` uses a background path; `:566` schedules a gather job. No new timing claim |
| 2026-07-25 F6 / 2026-07-26 N7: sparse library wiring | SUPERSEDED | Current library contains the reviewed tree, fern, mushroom, and aquatic prototypes |
| 2026-07-22 F09: old grass-clump system | Separate historical issue | This review concerns `ScatterField` and `ScatterClumping`, not the retired grass-clump renderer |

July lighting, LOD, material mutation, shadow, and dither findings remain outside this placement review.
This document does not close them or replace those audits. The `ScatterRenderer.cs:91` instancing write still exists; it is not a new finding.
The accepted coarse biome sampling policy remains intact. V01 requires exact water eligibility without removing that optimization.
The documented clumping density reduction is also not reported as a new defect.

## Validation status

[Unity validation queue](2026-09-09-vegetation-scatter-validation-queue.md): written backlog only, awaiting Editor release.
No tests, builds, runtime measurements, or captures ran. Proposed tests do not exist until implementation adds them.
Graphify provided scoped navigation with an approximately 1,800-token output budget. No graph generation or paid semantic extraction ran.
Every substantive finding was checked against current source.

No further input is required to complete this review. Implementation remains a separate decision.
