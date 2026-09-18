# Smallest complete aquatic integration — 2026-09-10

**Status:** Proposal only. No Assets edits or Unity access.
This document supersedes the earlier candidate's proposed `RequireShelter` and `knownSheltered` integration.
Those candidate fields have no current data source and must not be imported into production.

## Existing immutable inputs

`ScatterField.GatherContext` already captures the level grid, its resolution, river field, prototype DTOs, and placement rules.
The candidate already samples carved ground before its placement decision.
`ScatterGatherJob` receives the equivalent native inputs.
`ScatterTileCache.EnsureWaterLevel` copies levels once by source-array identity; teardown completes pending jobs before disposing native inputs.
The background plan operates independently of these per-candidate decisions.

Missing information is body classification. Water height is not proof of water presence or freshwater.
`WaterBodyMap.BodyIdGrid` and `WaterBodyCatalog` already supply that classification, without additional terrain queries.
River hits and segment speed already come from `RiverFieldData.Sample`.
WaterQueryKernel gives river hits precedence over body-grid identity. Its velocity uses segment speed transformed to world units.

## Proposed data addition

Add one immutable byte grid of existing `WaterBodyKind` values alongside the water-level grid.
Derive each byte from BodyIdGrid and its matching catalog once per map/catalog generation.
Body ID zero or a missing catalog entry maps to None. Do not infer freshwater from `kind != Ocean`.
Capture classification and heights from the same map instance on the main thread.
Keep the derived grid cached across captures; never rebuild it per frame, batch, or candidate.

Copy the byte grid into one native array through the existing water-input upload lifecycle.
Key replacement on the captured map/catalog generation, not only the height-array reference.
Keep valid placeholder arrays when inputs are absent, with explicit resolution/availability guards.
Complete pending readers before replacing or disposing either native grid, as the existing lifecycle requires.
Missing body data rejects opted-in aquatic candidates but does not suppress ordinary land props.

This adds approximately one byte per water-grid cell, not a second terrain/noise snapshot.
Do not borrow a mutable service or construct WaterQueryJobSnapshot per candidate.
No changes to CapturePlan, BuildPlanAsync, priority ordering, ready snapshots, or fish jobs are needed.

## Shared candidate decision

Use a small opt-in habitat enum: Unrestricted, Freshwater, LakeOnly.
This describes existing water identity, not inferred shelter.
Reuse existing minimum and maximum altitude fields as signed bed-altitude bounds.
Do not add a second pair of depth limits that duplicates those fields.
Add a speed ceiling only for opted-in aquatic habitat, if speed filtering remains in the agreed first slice.
Validate finite, nonnegative speed limits through ScatterLibraryDto.EnsureValid.

For each opted-in candidate:

1. Sample carved bed radius using the existing gather path.
2. Sample the river once, retaining hit status, radius, and speed. Reuse the result instead of calling SeaRadiusAt again.
3. If the river hits, use its surface and freshwater classification, matching current query precedence.
4. Otherwise require the exact candidate's grid kind to be Lake. None and Ocean reject.
5. For LakeOnly, reject every river hit. Do not use Profile.y, speed, or width to claim shelter.
6. Compute bed altitude as `(bedRadius - waterRadius) * worldScale`. Require negative altitude for submerged aquatic habitat.
7. Apply the existing authored altitude gates to that signed value.
8. Compare river speed in world units with the authored speed ceiling; still lake speed is zero under current query semantics.
9. Keep grounded reeds on the bed. Place floating lilies at water radius plus the existing world-space visual offset.

Keep these habitat decisions in one pure helper shared by both gathers.
Feed primitives from existing immutable inputs; constructing a full WaterSample is unnecessary when terrain and water have already been sampled.
Reuse WaterQueryKernel's identity precedence and speed units. Add parity cases against that kernel, including raised lakes and transformed planets.
Use one canonical water radius. The query's optional surface offset and scatter's visual offset must not both enter depth.
Do not route local radii through unrelated rendered wave height.

Biome membership remains an independent species-distribution filter. Existing Lake/LakeShore membership must not become proof of freshwater.
For river reeds, a verified river hit may supply aquatic habitat membership so a terrestrial coarse biome does not reject them first.
Limit that override to opted-in reeds/cattails. Do not alter global biome classification or unrestricted prototypes.
For lakes, retain current biome membership in the first pass to avoid an unrelated distribution expansion.

## Rocks and banks

**The earlier PassesHabitat helper rejects legitimate dry or waterline bank rocks.**
It requires positive BodyDepth and valid freshwater identity. Bank rocks do not require either.
It can also reject submerged rocks deeper than plant habitat limits, despite valid ground contact.

Lake Rocks and ordinary rock prototypes must remain Unrestricted and grounded.
Their current slope, conformity, scale, and water-clearance rules continue unchanged.
Lake Rocks currently has water clearance 0.05 m, so it already rejects submerged bed rocks when clearance applies.
Changing that clearance is a separate visible asset decision, not part of freshwater eligibility.
The proposed first slice preserves existing bank rocks; it does not guarantee new rocks along every river.
General dry-bank targeting needs a bank query, which current RiverField.Sample does not supply beyond channel width.

The original TryAnchor helper accepts positive-radius dry ground when onWater is false.
Nevertheless, leave unrestricted prototypes on their current anchor path to avoid unnecessary behavior changes.
Only opted-in aquatic prototypes use the new predicate.

## Which existing lilies change

Only `Assets/Resources/Settings/Scatter/Lake Lily.asset` currently authors OnWater = 1.
PlantInjection retains its placement settings, so generated and source geometry routes inherit these changes.

| Current candidate | Proposed result |
|---|---|
| Submerged lake candidate within the new depth band | Retains its ID, position, yaw, and scale if spacing and density remain unchanged |
| Deep lake candidate outside the band | Removed |
| Dry point admitted through a coarse Lake biome cell | Removed |
| Exact waterline point | Removed by strict submersion |
| River hit, including a slow widened reach | Removed or withheld under LakeOnly |
| Ocean point with incidental coarse Lake membership | Removed |
| Point with missing or unclassified body data | Withheld |
| Shallow lake point rejected by existing biome/clumping rules | Remains rejected; this slice does not add new colony candidates |

Lake Lily currently has both altitude bounds disabled. Preserving bed altitude alone does not impose a shallow limit.
Complete authoring must enable a negative minimum altitude for maximum depth and a negative maximum altitude for minimum depth.
The numeric band remains a review decision. Do not silently convert fixture values into asset defaults.
Retain spacing 12 m, clumpiness 0.95, patch scale 9 m, and SlotId 68 in this first eligibility slice.
Colony-density tuning follows separately, preserving a clean comparison and avoiding grid-level/ID changes now.

LakeOnly preserves eligible lake placement by policy. It does not prove physical shelter for every lake location.
No shelter claim or shelter field is included. River lilies remain deferred until a real source exists.

## Exact file scope after coordination

- ScatterPrototype.cs and ScatterDtos.cs: habitat selection, optional speed ceiling, validation, and mapping.
- ScatterGatherJob.cs: native parameter mapping, classification input, shared habitat invocation.
- ScatterField.cs: captured classification data and the matching managed decision.
- One small shared aquatic helper: identity/depth/speed decisions and bed-versus-anchor handling.
- ScatterTileCache.cs: additional native water-input upload and disposal only; planner algorithm untouched.
- Lake Lily, Lake Reeds, and Lake Cattails assets: explicit opt-ins and reviewed altitude/speed bands.
- Existing scatter tests plus focused aquatic regressions: identity, limits, kernel semantics, and full managed/Burst parity.

No rock asset, shader, river mesh, fish planner, or general tree-clumping edits.
Without the coordinated input-upload change, full Burst freshwater eligibility is incomplete; do not ship a managed-only fix.
All file names above resolve under their existing Assets locations. No files have been imported or edited there.

## Validation still queued

Check missing maps, dry shoreline, lake depth boundaries, ocean boundaries, river precedence, and known river-hit exclusion for lilies.
Verify unrestricted bank rocks match the baseline exactly.
Verify retained lily IDs/transforms remain unchanged when candidate-grid settings remain unchanged.
Compare managed and Burst candidate sets and test classification against existing WaterQueryKernel semantics.
Regenerate while a gather is in flight to verify atomic map capture and reader completion before disposal.
Measure added gather work without changing the background planner.
Keep the river owner's undecorated shader baseline separate from decoration captures.
