# Plan 011: Generate connected rivers and waterfalls

**Date:** 2026-09-09  
**Planned at:** `d1e0f62` plus the active uncommitted working tree.  
**Status:** Implemented; final river and terrain fixtures report no failures. One console clipboard test fails. Visual review remains with Bryan.  
**Current next action:** Inspect the generated rivers with `river.visit` and review the captures linked in the implementation report.  
**Category:** Direction / design spike.  
**Effort:** L overall. **Risk:** High for terrain parity and water compositing.  
**Dependencies:** Existing lake solver, terrain sampling, water query, and water rendering systems. Revalidate their dirty-tree state before implementation.

- [x] Drainage graph and lake outlets.
- [x] Carved river networks and tributaries.
- [x] Shared river queries, placement masks, and rendering.
- [x] Waterfall sheets, receiving channels, impact foam, and bounded spray.
- [ ] Extended multi-seed, quality-tier, cancellation, and platform validation.

Bryan handed over Unity and authorized implementation on 2026-09-09.
The original proposal remains below for context. Current behavior and evidence are recorded in
[the implementation report](../docs/design/2026-09-09-rivers-and-waterfalls.md).
The validation table is a checklist, not an automatic scheduled job.

## Recommendation

Use the ocean as the drainage boundary, then discover how the land drains toward it.
This preserves Bryan's ocean-to-land idea while letting terrain determine the river network.
The existing lake solver already traverses outward from ocean cells.
Extend that work instead of adding a separate random uphill walker.

An uphill walker can make a descending line after reversal.
However, a descending line does not prove that the line follows a valley or has a useful catchment.
It can climb a ridge, miss lake outlets, or produce unrelated streams.
Keep uphill walking as an optional local shaping tool inside an established drainage corridor.

Assume branching means tributaries joining downstream.
From the ocean, that network appears to branch as we travel inland.
Downstream splits, braided channels, and deltas need additional rules and are deferred.
Assume convincing visible flow is required, with a stable flow value available to gameplay.
A fluid simulation, erosion simulation, and live flooding are not required for this first system.

## What already exists

| Existing code | Verified behavior | Required extension |
|---|---|---|
| `Assets/Scripts/Planet/Biomes/WaterSpillSolver.cs:20` | Priority-flood returns filled heights from ocean seeds. | Retain drainage receivers, deterministic processing order, and outlet information. |
| `Assets/Scripts/Planet/Biomes/WaterBodyMap.cs:75` | Six 192 × 192 faces, shared seam neighbors, raised basin levels, body IDs, shore fields. | Expose immutable drainage data from the same solve. Preserve lake classification. |
| `Assets/Scripts/Planet/ShapeGenerator.cs:57` | Side-effect-free analytic elevation. | Apply a shared channel modification after base terrain evaluation. |
| `Assets/Scripts/Planet/Planet.cs:518` | Terrain meshes generate before the lake solve. | Solve drainage and finalize channel geometry before final terrain meshes. |
| `Assets/Scripts/Planet/WaterQueryService.cs:51` | Queries body ID, still surface, radial normal, and bed depth. | Resolve narrow channels, sloped surfaces, and downstream velocity. |
| `Assets/Scripts/Planet/WaterMeshBuilder.cs` | Builds clipped ocean and raised-lake surfaces with depth and shoreline data. | Preserve this builder; add channel ribbons and junction geometry. |
| `Assets/Scripts/Planet/WaterVolumeRenderFeature.cs:112` | Finds `GameObject.Find("Water")` and draws one mesh. | Accept all water surfaces through explicit ownership and registration. |
| `Assets/Graphics/Shaders/WaterVolumePrepass.shader:81` | Classifies each fragment as lake or ocean from vertex data. | Add explicit river/sheet treatment without breaking packed data. |
| `Assets/Scripts/Planet/WaterPresentationController.cs`, `WaterSplashParticles.cs` | Water presentation, bounded splash particles, and interaction effects exist. | Reuse their ownership and pooling patterns for waterfall impacts. |

The August water architecture plan already names rivers as W13 and waterfalls as W14.
Its W6 query dependency now exists; its multiple-renderer W12 gap remains visible in current code.
Some old comments still describe a single water level.
Current mesh settings, water fields, and query code support raised lakes.
Do not rebuild those completed features from the older notes.

### Load-bearing current excerpts

`Assets/Scripts/Planet/Biomes/WaterSpillSolver.cs`:

```csharp
float candidate = elevation[next] > cellLevel ? elevation[next] : cellLevel;
if (candidate >= filled[next]) continue;

filled[next] = candidate;
heap.Push(next, candidate);
```

This retains a height, but no downstream receiver.
Do not select receivers later using only the steepest gradient of filled heights.
Filled depressions contain flats, where that rule is incomplete.

`Assets/Scripts/Planet/WaterQueryService.cs`:

```csharp
float level = _bodies.LevelAt(dir, _oceanLevel);
float surfaceRadius = _planetRadius * (1f + level) + _surfaceOffset;

ushort bodyId = _bodies.SampleBodyId(dir);
if (bodyId == 0) return false;
```

This coarse field can describe lakes but cannot define a narrow river bank precisely.
Do not paint a five-metre stream into an entire coarse cell and treat that cell as wet.

## Drainage generation

1. Sample base terrain using the current analytic sampler and cube-face mapping.
2. Extend `WaterSpillSolver` to retain a receiver toward an already settled downstream cell.
3. Use explicit cell-index tie breaks and settlement order to resolve equal heights without cycles.
4. Identify lake inlet and outlet links using the existing body catalog and basin boundaries.
5. Accumulate contributing surface area downstream in reverse topological order.
6. Select stream reaches by contributing area, then refine their paths within local terrain corridors.

Use spherical cell area, rather than a raw count, when accumulating catchments.
Cube-face projection changes cell area across each face.
Start with uniform runoff per square metre.
Current climate generation occurs later, so rainfall-dependent generation would otherwise create an initialization cycle.
Later runoff can consume a stable climate input after its dependency is explicitly resolved.

The flood graph provides connectivity and spill heights.
It does not provide a finished water profile or a physically calibrated discharge model.
Use actual downhill terrain where possible, and use the flood ordering to resolve flats and depressions.
Keep lakes as water bodies with inlet and outlet links; avoid visible river stripes through their interiors.

Every reach must terminate at another reach, a lake, or an ocean.
For a world without ocean seeds, return an explicit no-outlet result.
Preserve the current lake fallback instead of letting maximum sentinel heights become river elevations.
Closed-basin rivers can become a later, explicit destination policy.

Each reach stores stable identity, downstream linkage, spherical centerline samples, surface height, bed height, width, and flow speed.
Store cross-section distance and downstream distance for meshing and flow animation.
Keep generated topology independent of camera position and render quality.
Version the generator in saved-world metadata before shipping seed-derived regeneration changes.
Same-seed floating-point behavior across target platforms requires testing; current comments do not prove it.

## Channels and terrain

Rivers need beds and banks.
Placing water ribbons over unchanged noise will leave exposed sheets, buried water, and grass through the channel.
Use a bounded channel profile that lowers terrain within the bank envelope.
Blend back to the original terrain outside that envelope.

Choose a water profile that descends toward the outlet and meets the receiving water level.
Place the bed below that profile with positive channel depth.
At each confluence, tributaries and the main channel share one junction level and one combined downstream flow.
Do not sum overlapping carve depths at junctions; combine channel targets through a defined union rule.
Reject or reroute any reach that requires an excessive cut or raised terrain embankments.

Solve on base terrain, derive channels, then validate the resulting surface before publishing it.
Preserve lake levels and spill controls during the first slice.
Reject channels that breach another basin or alter an unrelated outlet.
Do not repeatedly carve and solve until a result appears stable without a bounded convergence rule.

The target generation sequence is:

1. Initialize base terrain inputs.
2. Solve basins and drainage on base terrain.
3. Refine channels, junctions, and waterfall profiles.
4. Publish immutable terrain modifications and water geometry data.
5. Build final terrain meshes, biome data, water meshes, and placement caches.
6. Configure gameplay queries and publish world readiness.

Terrain evaluation already appears in several forms:

- Managed: `ShapeGenerator.SampleElevation` and `AnalyticGroundSampler`.
- Burst mesh: `Surface/PlanetChunkMeshJob.cs` and the `TerrainFace.cs` job path.
- Burst placement: `Scatter/ScatterGatherBurst.cs`, called by `ScatterGatherJob.cs`.
- GPU mesh: `Assets/Resources/GpuPlanetTerrain.compute`.

These are parity obligations, not four places to invent independent carving logic.
Use one immutable channel dataset and one shared C# evaluator where compilation permits it.
Use a matching HLSL evaluator and a tested data layout for compute consumers.
Extend `IBurstElevationSource` and its callers to carry the channel data when needed.
Inspect all active terrain backends before changing any one path.

Index reach segments into spatial cells with conservative bounds.
Evaluate only local candidate segments for bed, bank, and water queries.
The current coarse map remains suitable for drainage discovery and broad lookup.
Use precise segment cross-sections for channel occupancy, including junctions.
Share these data with CPU/Burst placement and GPU grass consumers.
Avoid a second coarse river mask with different bank boundaries.

Keep channel generation separate from player edit persistence.
Saved `SurfaceEditStamp` records remain authoritative for player edits.
Generated river geometry derives from world inputs and generator version.
Invalidate dependent terrain, biome, vegetation, and water caches together on regeneration.

## Water rendering and queries

Build river ribbons with enough cross-section vertices to support banks, width changes, and lighting.
Use radial up and transported tangent frames so ribbons remain stable at planet poles.
Join tributaries with dedicated junction geometry; overlapping transparent ribbons will create visible seams.
Merge the mouth into the receiving body without double-drawing its surface.
Split meshes for culling and LOD, but keep topology and shared boundary vertices stable.

Replace the single-mesh lookup with a collection owned by `PlanetWaterSurface` or its water coordinator.
Register the current ocean/lake mesh through the same collection.
Each entry needs its mesh, transform, material, bounds, and interface treatment.
Keep surface and prepass geometry displacement identical.
Preserve the private nearest-water depth buffer and camera-depth contract introduced by current water work.

The packed prepass data is not a free body-ID channel.
Audit `WaterVolumeData.hlsl`, `WaterVolumePrepass.shader`, `Ocean.shader`, and atmosphere consumers together.
Assign surface kinds deliberately and preserve shoreline quantization and freeze data.
Do not overload the existing lake/ocean blend with a river identity.

Extend `IWaterQueryService` rather than adding a separate gameplay river service.
Return the channel surface, its slope-derived normal, bed depth, stable identity, and downstream velocity.
Preserve existing lake and ocean callers while introducing any new sample fields.
Apply the same footprint precedence at mouths and confluences across queries and rendering.
Camera immersion, swimming, fish, rain contacts, and vegetation must agree on the water location.
Initial current velocity can remain informational; current-driven swimming is a separate behavior decision.

### Make flow visible

Use the centerline distance as the downstream texture coordinate.
Move detail normals and foam downstream using reach speed.
Blend two repeating animation phases to hide resets and limit visible stretching.
At confluences, blend directional flow through the junction instead of changing UV direction abruptly.

Use slow, broad movement in pools and faster, stretched detail through narrow or steep reaches.
Add foam at rapids, confluences, banks, and waterfall impacts.
Keep the foam moving along the water; changing noise in place will not communicate transport.
Gate ocean swell on river surfaces so upstream wind waves do not dominate the current.
Reuse water lighting, reflection, depth coloration, and freeze behavior where their assumptions remain valid.

## Waterfalls

Identify candidate falls from a significant drop over a short horizontal distance.
Slope alone is insufficient: a long steep stream is a rapid, and small noisy steps are not waterfalls.
Require a supported upstream lip, downstream landing, minimum drop, and valid clearance.
Use authored thresholds during the first slice, then tune them from saved captures.

Build a falling sheet from the lip toward the landing with downstream momentum and radial gravity.
Use downward texture transport, edge breakup, whitewater, and bounded spray.
Add impact foam and a receiving pool with valid bed depth.
Reuse native particles and existing pooling patterns; do not use fluid particles to form the main sheet.
Cull spray and reduce its budget with distance and quality.
Use a local looped water sound only after a suitable asset or synthesis treatment is selected.

A falling sheet is not a deep lake volume.
The current radial height query cannot represent every vertical sheet position at one direction.
Keep the sheet as a separate render treatment with bounded optical thickness.
Do not make the air behind it submerged or tint all background geometry as deep water.
Its receiving pool remains ordinary queryable water.
The prepass must distinguish a sheet from a volume surface, or use a separate sheet path with compatible occlusion.
Decide that contract in the waterfall slice before enabling fog, caustics, or camera immersion for sheets.

## Delivery sequence

| Slice | Concrete output | Size / risk | Acceptance |
|---|---|---|---|
| A: Drainage | Receiver graph, contributing area, lake outlets, deterministic diagnostic output. | M / medium | No cycles; every selected reach has a destination; repeated outputs match. |
| B: One watershed | One main channel, two tributaries, a raised-lake connection, carved terrain. | L / high | Continuous downhill profile; bed/query/mesh parity; no vegetation inside the channel. |
| C: Flow presentation | Registered river surfaces, junction meshes, moving detail and foam. | M / high | Flow reads downstream at ground level; all water passes agree. |
| D: One waterfall | Lip, falling sheet, spray, receiving pool, impact foam. | M / high | Connected endpoints; no false underwater region behind the sheet. |
| E: Planet coverage | Multiple seeded watersheds, mesh culling/LOD, reload and quality support. | L / high | Seams remain closed; bounded generation and frame costs; existing water remains correct. |

Implement slices in order.
The first visual target is one convincing watershed, not hundreds of unvalidated rivers.
A full erosion simulation, live flood levels, dams, boats, and deltas remain outside this proposal.

## Written validation queue

The implementation report records completed checks and remaining coverage.
Coordinate Editor ownership before continuing validation.
Use the existing NUnit EditMode assembly; do not add a test framework.
`WaterBodyCombinedSampleTests.cs` and `ScatterGatherParityTests.cs` provide current test patterns.

| Order | Check | Expected result |
|---|---|---|
| Q1 | Capture baseline ocean, raised lake, shoreline, underwater view, generation timings, and frame timings. | Saved poses and quality settings identify the unchanged baseline. |
| Q2 | Add drainage fixtures: bowl, slope, plateau, equal-height saddles, multiple outlets, no ocean, and seam crossings. | Finite output; no cycles; valid destinations; repeatable receivers and IDs. |
| Q3 | Add catchment and junction checks. | Downstream area equals local area plus incoming areas; tributaries join once. |
| Q4 | Compare channel profiles with all active terrain and placement backends. | Proposed local tolerance: 0.01 m for sampled channel heights; identical wet/dry decisions outside the defined boundary tolerance. |
| Q5 | Test a narrow channel, mouth, confluence, pole, cube seam, and LOD boundary. | No widened coarse-cell wetness, cracks, duplicate surfaces, or foliage through water. |
| Q6 | Check player swimming, camera immersion, fish queries, and rain contacts on rivers and lake mouths. | Each consumer resolves the intended surface and body. |
| Q7 | Capture river surface-only, volume-only, interface, and lit views, plus a short motion sequence. | Continuous coverage; moving features travel downstream; no recurring UV reset or junction seam. |
| Q8 | Capture waterfall front, side, behind-sheet, lip, and pool views. | Endpoint gap <= 0.10 m; supported lip; connected landing; dry air behind the sheet. |
| Q9 | Repeat generation, cancellation, reload, and quality changes. | No retained meshes, materials, buffers, emitters, or prior-world data; geometry remains seed-stable. |
| Q10 | Run existing water, scatter, ground-query, swimming, and console regression tests where changed. | All relevant tests pass; record exact failed output if any test fails. |
| Q11 | Compare baseline and river builds at identical poses across quality levels. | Report generation time, resident memory, draw count, CPU/GPU frame time, and particle count. Agree budgets before scaling coverage. |

Build checks must run serially after Unity has refreshed generated project files:

```powershell
dotnet build ProceduralPlanets.Core.csproj --no-restore
dotnet build ProceduralPlanets.Planet.csproj --no-restore
dotnet build ProceduralPlanets.Tests.EditMode.csproj --no-restore
```

Expected result: exit code 0 and no compilation errors.
These commands compile code; they do not execute Unity tests or validate shaders.
Use the Editor's existing Test Runner for the listed fixtures after ownership is available.
Current `ProjectSettings/ProjectVersion.txt` specifies Unity `6000.7.0a5`; older skill version notes are stale.
After implementation, run `graphify update .` to refresh code relationships.

## Implementation boundaries and review checks

Read the current project skills and shared memory before implementation.
Use settings ScriptableObjects for authoring and immutable DTOs at runtime.
Use world-owned services, explicit initialization dependencies, cancellation, `Awaitable`, and `ILogger`.
Clone runtime materials and dispose all generated resources through their owner.
Follow `docs/design/console-command-authoring.md` if adding diagnostic commands.

Expected scope includes water, terrain sampling, surface generation, and the placement consumers named above.
New files should remain under `Assets/Scripts/Planet/Rivers`, shared water includes, and existing test directories as appropriate.
Keep gameplay contracts in Core and match its assembly boundary.
Do not modify unrelated creature, animation, weather, or asset-pack work.
Coordinate shared `Planet.cs`, water shaders, and settings files before implementation.
Do not reset or stash the active dirty tree.

Revalidate `git diff d1e0f62 --` for each touched path and read untracked files before editing.
The commit alone does not capture this plan's source baseline.
Reassess the plan if another agent has already introduced drainage, carving, or renderer registration.
Stop the affected slice if a channel breaches unrelated basins, a backend cannot consume channel data, or a sheet requires unsupported volume semantics.
Resolve that specific contract before proceeding to planet coverage.

## Sources and unresolved points

- Existing direction: `docs/design/2026-08-17-water-architecture-plan.md`, W12-W14 and W19. This proposal updates their integration details against current code.
- Algorithm reference: [Barnes, Lehman, and Mulla: Priority-Flood](https://arxiv.org/abs/1511.04463). It supports depression processing and drainage-direction extensions. Our proposed spherical integration needs its own seam and determinism tests.
- Reference implementation: [Barnes2013-Depressions](https://github.com/r-barnes/Barnes2013-Depressions), including the flow-direction variant. Reuse our solver first; no new dependency is proposed.

Unresolved tuning includes river density, minimum visible width, channel-cut limits, waterfall drop thresholds, and performance budgets.
Resolve these using the first watershed and measured captures.
No runtime performance or visual correctness claim follows from this source inspection.
