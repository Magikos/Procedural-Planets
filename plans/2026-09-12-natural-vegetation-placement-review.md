# Natural vegetation placement — review and proposed design

Implementation was authorized afterward. See [implementation and validation](2026-09-12-vegetation-habitat-implementation.md).
The findings below retain their original review-time status.

**Findings only — no product code changed.**

The current placement system is a useful foundation, but it does not yet produce Bryan's requested connected vegetation patterns reliably.
Keep deterministic candidates and tile caching. Share habitat information across plant types, then apply different species rules.
Separate placement engines for lilies, reeds, trees, bushes, and flowers would duplicate the wrong part of the system.

Reviewed 2026-09-12 on `harvest-vertical-slice`, HEAD `d1e0f62`, with extensive concurrent uncommitted changes.
Scope: scatter placement, generated tree age/species selection, relevant authored plants, GPU grass density inputs, and placement test coverage.
No Unity calls, imports, builds, runtime tests, or captures ran. Existing background planner changes remain untouched.
This is not a complete rendering, harvesting, or lifecycle audit.

## Intended result

Bryan also requested wide-open plains. Treat these as complete landscapes, not only gaps between forests.
Some regions should retain long views with few or no trees across broad areas.
Vary grass height, flower colonies, and bare patches within biome limits without filling every opening with shrubs.
Woodland transitions belong where habitats meet; not every plain must progress toward forest.
Include a ground-level open-plains vista in the queued visual checks.

Bryan's reference shows uneven flower concentrations within continuous ground cover and substantial open areas.
His requested field-to-forest transition describes spatial composition. This review assumes simulated growth over years is not required.

| Region | Trees | Ground vegetation |
|---|---|---|
| Open field | Few trees or none | Grass with irregular flower colonies and open gaps |
| Woodland edge | Sparse trees, biased toward young forms | Grass, flowers, and suitable bushes between trees |
| Established woodland | More trees with mixed ages | Less grass under canopy; suitable undergrowth in gaps |
| Mature forest | Greater share of larger, older forms; occasional openings | Shade-tolerant ground cover with grass in brighter openings |
| Freshwater margin | Species permitted by local biome and bank conditions | Rooted reeds and grouped lilies within their separate habitat limits |

These are overlapping tendencies, not rigid concentric bands. Retain occasional older edge trees and younger interior trees.
Biome profiles should control the species and strength of each tendency. A sparse dry woodland should not inherit a dense wet-forest profile.

## Rechecked findings

| ID | Finding | Type | Impact | Effort | Fix risk | Confidence |
|---|---|---|---|---|---|---|
| N01 | Supposedly shared woodland maps differ by species | Bug | Undergrowth and trees disagree about clearings | M | Medium | High |
| N02 | Tree age follows variant index, without spatial age suitability | Missing requested behavior | Saplings and old trees have the same habitat eligibility | M | Medium | High |
| N03 | Species and age advance through the same variant index | Design limitation | Age-based selection also changes species composition | M | Medium | High |
| N04 | GPU grass density does not read local tree cover | Missing requested behavior | Forest openings and shaded interiors cannot control grass locally | M–L | Medium | High |
| N05 | Flower colony contrast weakens with strong shade preference | Design limitation | Habitat preference can erase independent species patches | M | Medium | High |
| N06 | Floating depth and aquatic habitat remain incomplete | Bug / missing eligibility | Deep water and unsuitable boundary candidates remain possible | M | Medium | High |
| N07 | Terrain bias has the opposite sign from the approved intent | Bug | Flatter ground receives more woodland bias | S | Medium | High |
| N08 | Existing gather parity bypasses active ecological rules | Test coverage | Passing tests do not establish natural placement or aquatic eligibility | M | Low | High |

S means hours; M means approximately a working day; L means multiple days. Estimates include focused tests but exclude visual iteration.
All production changes above, except test-only N08, can alter visuals and require matched validation.

### N01 — Shared woodland map

`Assets/Scripts/Planet/Scatter/ScatterClumping.cs:61` derives grove frequency from each prototype's patch scale.
Line 62 derives openness frequency from that grove frequency. Line 67 samples this supposedly shared field.
The same biome seed does not create the same map at different frequencies.

Current examples all use biome 7:

- `Assets/Resources/Settings/Scatter/Forrest Prototype.asset:22`: 220 m.
- `Assets/Resources/Settings/Scatter/Forest Fern Prototype.asset:22`: 60 m.
- `Assets/Resources/Settings/Scatter/LMHPOLY Forest Mushroom Red.asset:22`: 18 m.

Separate landscape structure from species colony scale. Reuse one woodland sample across these consumers.
This confirms September 9 finding V03 remains open.

### N02 and N03 — Age and species

`Assets/Scripts/Planet/Trees/TreeInjection.cs:215` selects species using `ordinalInBiome + variant`.
Lines 221–223 also choose age from the variant index.
Lines 253–271 retain the parent placement rules while replacing meshes, identity, scale jitter, and grouping.
`Assets/Scripts/Planet/Scatter/ScatterGatherJob.cs:153` has no age-suitability input in the density decision.

Generated age shapes already exist. Reuse them, but select species separately from age.
Use local woodland structure to bias age probabilities and density together.
Do not generate every combination of species, age, and appearance unless the authored selection needs it.
Prototype IDs, saved chopped-tree records, and impostor identity need an explicit compatibility review before restructuring variants.

### N04 — Grass and actual cover

`Assets/Resources/GrassNearFieldPlace.compute:371` obtains density from blended biome parameters.
Lines 393–418 apply water, slope, and additional density rejection. They do not sample local tree cover.
`Assets/Resources/BiomeGrassPlace.compute:203` likewise starts with biome density.
`Assets/Graphics/Shaders/Includes/GrassPlacementParamBlend.hlsl:45` combines biome density and membership weights.
`Assets/Resources/GrassNearFieldPlace.compute:451` uses climate moisture for tint, not a local canopy-density rule.

Existing grass "CanopyColor" settings describe grass rendering color transitions. They do not establish tree-canopy occupancy.
Biome density can make all forest grass sparser, but cannot distinguish an interior clearing from nearby closed canopy.

Supply a stable local cover estimate to grass placement. Reuse existing GPU placement and biome blending.
Do not use current-frame shadow maps to spawn or remove grass as the sun moves.
Potential woodland structure is a useful first approximation; label it honestly until accepted trees supply a closer canopy estimate.

### N05 — Flower colonies and overlap

`Assets/Scripts/Planet/Scatter/ScatterClumping.cs:100` fades the species grove factor toward one as absolute shade preference increases.
At shade preference -1 or +1, that species factor contributes no colony contrast.
`Assets/Resources/Settings/Scatter/Grassland Wildflowers Prototype.asset:23` uses -0.9, strongly reducing its species patch signal.

Independent species distributions can already overlap through separate slot candidates.
See `ScatterGatherJob.cs:119` and `ScatterPlacementMath.cs:62` under `Assets/Scripts/Planet/Scatter/`.
This is useful and should remain. The problem is coupling colony contrast to shade preference, not the absence of separate random generators.

Apply habitat suitability and colony membership independently. Let both species occur where their soft patch edges overlap.
If combined coverage becomes excessive, use a shared local coverage limit rather than making entire patches mutually exclusive.
Do not apply tree-trunk collision rules to flowers and bushes indiscriminately.

### N06 — Lilies, reeds, and rocks

`Assets/Scripts/Planet/Scatter/ScatterField.cs:348` still supplies zero bed altitude for floating plants.
`Assets/Scripts/Planet/Scatter/ScatterGatherJob.cs` retains the equivalent branch.
The river-aware surface radius is present; it does not establish shallow-water eligibility.

Current Lake Lily uses spacing 12 m, patch scale 9 m, clumpiness 0.95, and no altitude bounds.
See `Assets/Resources/Settings/Scatter/Lake Lily.asset:17`.
It needs both actual depth eligibility and enough candidates to form multi-pad colonies.

Lake Reeds and Lake Cattails have a maximum altitude of 3 m but no minimum altitude.
See the corresponding files under `Assets/Resources/Settings/Scatter/`, starting at line 27.
Their LakeShore biome filter cannot replace an exact candidate depth check.
Use explicit submerged and bank bands with freshwater and flow information where supported.

Rocks already have terrain grounding and slope conformity. Keep them outside rules requiring positive water depth.
The prior river contribution remains a proposal under `plans/river-margin-candidate/`; it has not become active placement code.
No source currently establishes river-pocket shelter. Continue withholding river lilies in the proposed integration until that source exists.
See [the bounded river integration review](river-margin-candidate/integration-review.md) for the exact input and planner constraints.

### N07 — Terrain bias

`Assets/Scripts/Planet/Scatter/ScatterClumping.cs:72` adds a positive bias on flatter ground.
Line 80 converts larger values into greater woodland density.
That reverses the flat-clearing preference recorded in `docs/design/2026-08-15-forest-clumping.md`.
Correct the direction after separating the shared landscape field. Test it outside saturated response regions.
This confirms September 9 finding V04 remains open.

### N08 — Test coverage

`Assets/Tests/EditMode/ScatterGatherParityTests.cs:96` constructs prototypes without active clumping or floating placement.
The fixture disables the water-level grid at line 173.
`ScatterClumping.Keep` therefore returns before evaluating the spatial rules.
A scoped search found no current EditMode assertions for Clumpiness, ShadePreference, woodland maturity, or tree-cover response.

Keep parity tests, but add assertions about the intended habitat behavior.
Two implementations can match perfectly while both produce the wrong landscape.

## Recommended architecture

Use one common placement pipeline with different responses per plant type.
The shared parts should be data and deterministic math, not a new framework of per-type factories.

1. **Biome and habitat:** existing biome weights, terrain slope, altitude, and available water data restrict suitable species.
2. **Woodland structure:** a continuous, world-seeded field describes the transition from open land to established woodland.
3. **Species patches:** independent, smaller fields control local colonies and allow overlap.
4. **Type response:** trees use structure and age probabilities; ground cover uses cover preference; aquatic plants use depth and water identity.
5. **Placement:** retain stable candidates, IDs, grounding, cached tiles, and the existing render paths.

Blend biome responses to shared spatial samples at boundaries. Do not restart the landscape pattern at every hard biome boundary.
Keep the world seed and planet coordinate convention consistent across managed, Burst, and GPU consumers.
Reuse existing climate and biome data where available. Extending scatter inputs is required before claiming per-candidate moisture suitability.

### Different rules, not unrelated engines

| Type | Distinct rules needed | Shared behavior retained |
|---|---|---|
| Trees | Species suitability, age probabilities, density response; trunk spacing only if observed overlap requires it | Candidates, IDs, terrain, woodland context |
| Bushes | Species-specific edge/open/shade response and colony size | Habitat and patch sampling |
| Flowers | Strong local colonies, soft edges, overlapping species, light preference | Habitat and patch sampling |
| Grass | Biome baseline modulated by local cover and existing terrain/path/water gates | Existing GPU generation and buffers |
| Reeds/cattails | Rooted shallow-water or moist-bank eligibility and flow limits | Ground sampling, clumping, biome context |
| Lily pads | Freshwater depth, floating anchor, colony density; river shelter remains deferred | Water inputs and deterministic candidates |
| Rocks | Ground contact, slope conformity, appropriate biome distribution | Terrain and stable candidates; no plant-depth requirement |

No full ecological simulation is needed to achieve this visual target.
Tree growth over time, seasonal succession, and immediate grass regrowth after harvesting are separate features.

## Smallest ordered implementation plan

1. **Establish shared woodland structure.** Correct N01 and N07 with active clumping tests. Preserve the current candidate grid.
2. **Place age distributions deliberately.** Address N02/N03 with existing generated shapes and explicit identity compatibility.
3. **Connect ground cover.** Give flowers and bushes independent colony masks. Feed the same landscape context to GPU grass.
4. **Improve canopy correspondence if needed.** Derive cached cover from deterministic accepted trees and their size, including neighboring tile margins.
5. **Implement aquatic habitat separately.** Follow the coordinated river proposal, then tune lily colonies against the supplied references.

For stage 4, never derive cover solely from currently visible or loaded renderer instances.
Camera motion must not change plant eligibility. Cached cover should remain reproducible across tile load order and world reload.
If forest-structure-based cover already meets the target, measure the remaining discrepancy before adding a tree-derived cache.

## Queued acceptance checks

No new test framework is required. Extend the existing NUnit suite and use the project's capture workflow after Unity handoff.

| Check | Pass condition |
|---|---|
| Shared structure | Changing a flower colony scale cannot move the woodland map |
| Biome transition | Compatible species and density responses blend without a hard vegetation boundary |
| Age transition | Mean age and tree occupancy increase across authored edge-to-interior samples, while mixed ages remain possible |
| Species independence | Changing age preference does not silently replace the selected species |
| Ground cover | Closed-canopy samples contain less grass than matched open samples within the same biome |
| Flower colonies | Two species retain distinct colony centers and both appear in their overlapping edges |
| Habitat separation | Dry and deep candidates reject for lilies; valid bank rocks retain existing placement |
| Determinism | Same seed, transform, and settings preserve IDs across traversal order, tile reload, and managed/Burst paths |
| GPU agreement | GPU ground-cover samples agree with the shared field within predefined tolerances |
| Performance | Record candidate counts, buffer overflow, gather cost, and grass cost before and after at matched viewpoints |

Record seed, pose, quality, active generated-asset route, and effective settings before the baseline.
Use one meadow-to-forest traverse, one overlapping-flower view, and one shallow-to-deep lake-bank view.
Write numerical acceptance thresholds before execution; no spatial statistics were measured during this review.
Bryan reviews matched captures for the final visual decision.

## Prior review reconciliation and limits

September 9 V01–V05 remain open in the current runtime source. This review adds the age/species and GPU grass coupling gaps.
The shared background planner and river water-radius work remain in place and are not findings to undo.
The original scatter architecture already supports deterministic biome-aware placement and overlapping prototype distributions.
Those parts should remain. No replacement renderer, new asset pack, or per-frame ecosystem simulation is proposed.

Graphify queries supplied navigation; current source established the findings. No graph rebuild or semantic extraction ran.
Tests remain a written backlog. This document does not certify runtime or visual correctness.

## Lake-vista reference — supplied 2026-09-12

Bryan supplied a real lake photograph as an additional composition target.
The photograph shows a shaded grassy foreground, an open near bank, broad water, and an irregular wooded far shore.
The left shoreline projects into the water and carries taller, denser trees. The distant tree line has varied height and color.
Sparse upright plants occupy portions of the near bank without enclosing the whole view.

Use this as a landscape-scale target alongside the earlier close views of flowers and aquatic plants.
Do not reproduce the picnic table or other foreground objects as part of vegetation placement.

- Preserve some naturally open shoreline stretches where players can see across the lake.
- Allow mature woodland to reach the shore in other places. Do not force every bank through a young-tree transition.
- Form uneven wooded points, gaps, and coves rather than a constant-width ring of trees.
- Keep broad open water. Restrict aquatic plant colonies to suitable pockets instead of filling every shallow edge.
- Vary tree heights and species within biome limits while retaining a coherent distant woodland silhouette.
- Use foreground grass, shrubs, and shade to frame the water without placing vegetation across every sightline.

Create these openings through stable terrain and habitat patterns, not camera-dependent removal of trees.
The photo's foreground shade is a lighting observation, not proof of reduced grass density caused by shade.
Do not infer tree age, water depth, or seasonal growth rules from this single photograph.

### Additional queued acceptance view

Choose an accessible ground-level lake-bank viewpoint with an open foreground and wooded opposite shore.
Record the seed, pose, quality, and lighting before comparing changes.
Inspect near-bank openness, sparse plant pockets, shoreline shape, and the far tree-line silhouette together.
Walk along the bank and revisit the viewpoint. Vegetation must remain fixed as the camera moves.
The far shore must retain its woodland silhouette through LOD transitions; source placement alone cannot prove this.
Bryan reviews whether the resulting vista captures the reference's composition within the game's art style.
No Unity validation or visual implementation occurred when adding this reference.
