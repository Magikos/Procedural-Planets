# Generated tree harvesting

Status: Bryan approved feedback revision 2. The shared implementation applies to all generated harvestable trees.
Current next action: Preserve the approved fall/contact effects across species. Forest-scale gameplay inspection and reward balancing remain separate follow-up work.

Bryan approved the direction and implementation on 2026-09-16: preserve species variety, give forests mature scale, fell trees with branches, and process fallen trees. Particle effects accompany physical transitions. Sapling reward type remains a design choice; this implementation uses small wood yields.

## Behavior

- Default age variants are 0.25, 0.8, and 1.0. Species and scatter slot ordering remain stable.
- Generated geometry supplies reachable cut positions, matching stump/log sections, branch groups, hit points, and volume-based wood yield.
- Mature felling gives no immediate wood. The tree falls with its branches and foliage.
- The trunk settles against terrain without crown support. Ground-contact branch groups break during the fall, retaining outgoing debris at the contact pose.
- Remaining branch groups break when chopped. Chips, dust, and drifting leaves accompany processing.
- Falling crowns shed leaves throughout the fall. Landing emits dust along the trunk, with capacity reserved for impact effects.
- Trunk sections take repeated hits and yield wood once. Final masks prevent repeat rewards.
- Saplings bend, break through a debris transition, and give a small immediate wood yield.
- Standing damage and fallen processing progress survive delta-log reload and compaction. New records distinguish valid identity rotation from legacy unknown transforms.
- The planet harvest interactor picks trunk geometry and fallen sections. The approved humanoid chopping fixture remains unchanged.

## Source corrections

- Connect generated hit points, yield, and log meshes to runtime harvesting.
- Replace percentage-based cutting with a reachable height.
- Reuse one tube meshing/slicing path. Preserve bark UV distance across cuts.
- Correct inward cut-face winding and isolate cap normals/UVs. Add fresh wood with growth rings.
- Remove reversed triangles sharing needle vertices, which cancelled calculated normals. Foliage materials already render both sides.
- Preserve dead conifer identity instead of substituting broadleaf geometry.
- Reject invalid tree dimensions and parent-level references; clamp the age exponent input.
- Avoid harvest-part generation for non-harvested plant prototypes.
- Preserve outgoing geometry through branch removal, section harvest, and sapling destruction.
- Use trunk radius for ground placement. Branch bounds previously made large trees sink too far into slopes.

## Verification criteria

- No reward on mature felling or branch removal; exactly the configured total after all sections.
- Partial standing/section damage, removed branches, and exhausted sections survive reload and compaction.
- Cut faces point outward; bark wraps across the seam; needle normals remain nonzero.
- Trunk picking works above the base under rotated/scaled transforms.
- Falling and settling meet terrain support samples and preserve the final transform during renderer handoff.
- The complete sequence renders without replacing the whole tree with a cylinder.

## Evidence

Review scene: `Assets/Scenes/Tests/TreeHarvestReview.unity`. It uses production generation, materials, services, fall, effects, and part renderers. Its capsule is a two-metre reference, not proof of humanoid axe fitting.

Controls: Space strikes; Repeat chopping runs the cycle; reset buttons select mature/sapling. `Species` selects a generated species before Play Mode.

Final captures use actual Play Mode rendering at 15 captured frames per second. Frame directories include metadata for stage, strikes, time, and wood.

- `local-only/tree-harvest-2026-09-16/conifer.mp4`: 503 frames; completed with 71 wood.
- `local-only/tree-harvest-2026-09-16/broadleaf.mp4`: 335 frames; completed with 38 wood.
- `local-only/tree-harvest-2026-09-16/sapling.mp4`: 35 frames; completed with 1 wood.

Representative frames were inspected for falling branches, processed trunks, and the sapling debris transition. This inspection does not replace normal-speed user review.

Final regression: 78 tests passed, with zero failures or skipped tests. The suites cover generated geometry, falling, persistence, harvesting, interaction, scatter rendering, and world deltas. All 16 species generate finite geometry at three ages.

Core and Planet builds passed. Build logs retain existing analyzer-version and legacy serialization-field warnings. Logs are under `local-only/tree-harvest-2026-09-16/`.

Main-planet startup completed with 188 prototypes, including 75 generated trees, and the harvest interactor registered. `planet-startup.json` records measurements. `planet-ready.png` shows completed startup from orbit; it does not establish forest-scale visual acceptance. No planet trees were harvested during this startup check.

The generated impostor bake completed with 174 atlases, zero skipped entries, and two obsolete atlas removals. The saved manifest is `Assets/Resources/Settings/GeneratedImpostors.asset`. The editor reported no console errors after baking.

The knowledge graph was updated after source changes. No humanoid animation fixtures changed.

The initial geometry baseline uses simple materials in `before.png`, with measurements in `before.txt`. It does not establish production shading or planet placement quality.

## Limits

The fall uses terrain-sampled controlled motion, not free rigidbody simulation. The trunk follows terrain slope and taper; branches never hold it up. Tree-to-tree collisions and crushing damage are not implemented. Terrain contact uses bounded geometry samples. A contacting branch group breaks as a group, rather than splitting each twig independently.

Rewards and strike counts are initial balance values. Saplings yield wood rather than a new sticks item. Broadleaf junctions remain intersecting tubes.

The main planet uses its existing interaction host. Humanoid animation integration remains separate. Visual acceptance remains Bryan's decision.

## Feedback revision 2 — flat trunks and stronger effects

Bryan requested more dust, falling leaves, and breakup effects after reviewing the first captures. He rejected branches supporting the fallen broadleaf trunk. This feedback supersedes the initial crown-supported resting behavior.

The rest solver now fits the tapered trunk to the terrain. During falling motion, ground-contact groups detach through the existing debris event. The stored log retains those removed groups, including when reloading during the fall. Branch loss does not grant wood or reduce the trunk reward.

Effects use the existing swarm particle shader through a resource material, which retains the shader in builds. Separate chip, leaf, and dust motion replaces the single short chip burst. Dust expands, rises, and fades. Leaves drift and tumble. Emission limits reserve room for impacts without deleting particles already in flight.

### Verification

- All 82 focused tests passed: zero failures or skipped tests. New checks cover flat trunks, slope/scale/planet-up changes, branch contact masks, and unchanged total wood.
- Core and Planet builds passed. The final Planet build reported 19 existing analyzer and legacy serialization warnings, with zero errors.
- Unity compiled the source. Hot Reload initially reported that new types required a Unity recompile; the full compile resolved that requirement.
- All three fresh Play Mode cycles completed. Console checks found no exceptions, particle warnings/errors, or C# compilation errors.
- Unity was returned to Animations with Play Mode stopped and the clean `EquipmentReview.unity` scene open.

Final videos are under `local-only/tree-harvest-feedback-2026-09-16/`:

| Cycle | Video | Frames at 15 fps | Wood |
|---|---|---:|---:|
| Broadleaf | `broadleaf.mp4` | 352 | 38 |
| Conifer | `conifer.mp4` | 544 | 71 |
| Sapling | `sapling.mp4` | 88 | 1 |

The sibling frame directories contain per-frame particle counts, harvest state, and stored transforms. Initial revision 2 captures remain in `initial-*` directories. They preceded the final dust visibility and upward-emission adjustment. The original revision 1 videos remain under `local-only/tree-harvest-2026-09-16/`.

Replay uses `TreeHarvestReview.unity`, seed 719, ages 0.8 and 0.25, and repeated strikes every 0.8 seconds. Mature captures use camera position `(32,21,-52)` looking at `(0,12,-8)`. The sapling capture uses `(10,5,-14)` looking at `(0,3,0)`. The capsule remains a two-metre reference. Metadata records the actual Unity version and quality level. The fixture resets only its own transient harvest store.

Sampled rendered frames show the low trunk, detached branch transitions, leaf shower, and stronger dust. Frame inspection does not certify normal-speed motion. The sky has visible stars in this capture session; this background difference is not a tree change. Bryan subsequently approved these examples: "These look good."

### Approval and species coverage

Bryan asked that all trees use the same behavior, without videos for every species. Source inspection confirmed that this is already shared runtime behavior, not review-scene-specific code. `TreeInjection.TryReplace` generates harvest geometry for every replacement tree, including dead variants. `TreeFallSystem`, `TreeHarvestService`, and `ChopFxSystem` consume that geometry without restricting behavior to the three captured examples.

The shared rules retain species differences: saplings break into debris; mature trunks fall and become harvestable sections; contacting branch groups break; leaf effects require actual foliage. Dead trees therefore produce wood debris and dust without green leaves. Collectable ferns do not enter the tree-chopping path.

Existing validation covers finite harvest geometry for all 16 generator definitions at three ages. The 82-test regression and three runtime captures remain the recorded execution evidence. This coverage check required no further runtime changes, Unity access, or videos. It is not a claim that every species received an individual runtime capture.
