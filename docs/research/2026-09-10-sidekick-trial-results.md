# Sidekick trial results

> Renamed since this was written (2026-09-18). The names below are the originals
> and stay as the record of what was done on the date in the filename. Current
> equivalents: `Assets/Art/Characters/SyntyHero/` is `Assets/Art/Characters/Human/`;
> `Sidekick*` types, scenes and menu paths are `Human*`; a baked `*_Sidekick.prefab`
> is `*_Fit.prefab`; our copies of source art drop the vendor prefix, so
> `SM_Chr_Rider_01.fbx` is `Rider_01.fbx` and `SM_Prop_Chest_01.fbx` is `Chest_01.fbx`.
> A bare `SM_*` or `SK_HUMN_*` still names a sub-mesh inside a source FBX and is
> unchanged. "Synty Sidekick Characters" is a pack name and is also unchanged.

Status: The animated candidate and fitting scene are implemented. Bryan accepted the comparison scene's initial look on 2026-09-10. Final outfit and garment compatibility remain open.

Baseline: `harvest-vertical-slice`, `d1e0f624ead0448f70a867f20a9389e367527293`, with existing uncommitted project work.
Unity: `6000.7.0a5`. Editor ownership transferred to this task on 2026-09-10.
The original Planet scene was running and clean at handoff. The gameplay character default remains unchanged.

## Open the trial

| Scene | Purpose |
|---|---|
| `Assets/Scenes/Tests/SidekickStyleReview.unity` | Both characters, deer, wolf, architecture, and accessory fitting |
| `Assets/Scenes/Tests/SidekickBodyReview.unity` | Bare Sidekick, Sidekick with POLYGON torso/arms, and original POLYGON; experimental fit and native body-shape sliders |
| `Assets/Scenes/Tests/SidekickAnimationReview.unity` | Sidekick using the existing movement, water, and traversal review course |
| `Assets/Scenes/Tests/HumanoidAnimationReview.unity` | Original POLYGON comparison and restoration path |

Enter Play mode in the fitting scene. Use the left panel to select motion, camera view, or attachments.
Pause and scrub the pose to inspect intersections. Both characters use the same clip and normalized phase.
Use RMB with WASD for free camera movement; Q/E changes camera height.
Edit attachment transforms in the Hierarchy outside Play mode to save permanent fitting changes.
Play-mode fitting edits require copying component values before stopping and pasting them back afterward.
The scene generator rejects an existing scene to protect saved fitting work.

The fitting scene samples clips directly. It does not apply ground-contact corrections or gameplay equipment logic.
Use the animation review scene to evaluate the motor and procedural contacts.

## Verified character integration

The candidate is the owned `Starter_01` export from `D:/Unity/Explore Assets/Assets/Synty/SidekickCharacters/Characters/Starter/Starter_01/`.
It includes elaborate armour, a helmet, and back equipment in its combined mesh.
These built-in items constrain accessory fitting on this particular preset. They are not limitations of every Sidekick preset.

Unity accepts its saved Humanoid avatar. `HumanoidRigBinding` resolves its required limb chains without renaming bones.
The candidate uses the existing animation clips, animation view, procedural solvers, and movement system.
The Sidekick movement scene remeasures crawl cycle distances for its proportions.

| Measure | Current POLYGON | Sidekick Starter_01 |
|---|---|---|
| Active skinned renderers | 13 | 1 |
| Summed mesh vertices | 7,418 | 26,338 |
| Avatar binding | Valid Humanoid | Valid Humanoid |
| Gameplay animation controller | Existing project graph | Same project graph |

Vertex counts describe these outfits, not the entire character families. Rendering cost has not been profiled under a controlled actor workload.

## Accessory findings

Bryan accepted the scene's initial look. Individual accessory acceptance remains open. These technical findings apply only to the selected items and body preset.

| Sample | Technical verdict | Observed work or limitation |
|---|---|---|
| `SM_Wep_Sword_01` | Easy rigid attachment; grip work remains | Fits by a saved hand transform without mesh edits; existing locomotion does not provide a dedicated sword grip |
| `SM_Wep_Shield_01` | Easy rigid attachment; motion restrictions remain | Fits by a saved hand transform; crawling with it causes expected ground/body conflicts and needs equipment-aware poses or stowing |
| `SM_Chr_Attach_Priest_Hat_01` | Modification required for this preset | Head attachment works, but the hat conflicts with the combined mesh's existing helmet and plume |
| `SM_Chr_Mage_Cape_01` from `FantasyKingdom_Capes` | Modification required | The seven-bone cape rig can follow the chest; it intersects back equipment and has no added secondary motion or collision |
| `Chr_Torso_Male_01` from the current POLYGON character | Unconverted reference; remapping and fit work required | Nine referenced bones have no exact name matches on Sidekick; the garment remains displayed on its original skeleton |

Different bone names do not prove conversion is impossible. Humanoid semantic mapping can help establish correspondence.
The torso still needs a bind-pose, weight, and body-fit trial before estimating conversion effort.
No full wardrobe conversion was attempted. No item is classified as universally impossible to adapt.

The fitting scene includes the current Polyperfect deer and wolf with their existing materials and idle clips.
It also includes a Fantasy Kingdom doorway/wall and thatched roof sample using the matching palette through the project shader.
Legacy cape and torso references remain visible separately from fitted attachments.

## Material inspection

The first rear-view render prompted a suspected texture-mapping issue. Inspection did not confirm a UV mismatch.
Synty's shader samples `_ColorMap` through UV0, which the project material also uses.
The front view resolves the armour palette and face. The color map uses point filtering without mipmaps or compression.

The project material uses `Planet/PropLit`. It does not reproduce Sidekick's optional dirt, cuts, eye-edge, metallic, or emission effects.
The scene therefore assesses the character under the project's material convention, not exact vendor-demo shading.
The isolated scenes publish common daylight through `ActorReviewLighting` and restore previous shader globals when disabled.

The scratch installation had a Sidekick downloader but lacked its shader resources.
The official [Sidekick tool release](https://github.com/SyntyStudios/SidekicksToolRelease/releases/tag/1.2.4) supplied shader source for inspection.
The downloaded package remains under `local-only/`; no vendor tools or shaders were imported into the game project.

## Validation

- Targeted regression job `abea2907b238441f940b3ba8f7c1fb08`: 132 passed, zero failed, zero skipped.
- That run includes 19 `HumanoidAnimationTests` cases on each character, plus the selected shared contact, movement, and camera fixtures.
- Fitting controls job `2119fb9c688b4e118e339a2c3211a4ef`: one passed, zero failed, zero skipped.
- The fitting controls test verifies repeated clip changes, both animated rigs, fixed display roots, and disable/re-enable behavior.
- A 13-clip sweep sampled 60 phases per clip on both characters: all positions remained finite and maximum display-root drift was zero.
- The movement scene walked approximately 3.12 metres over 120 manual 1/60-second ticks, entered surface swimming, and entered diving.
- The movement smoke also exercised crawl and reset. This is not a full visual clearance assessment of every traversal station.
- Planet and Editor code-health builds passed. Existing project warnings remain in the saved build logs.
- Repeated Play-mode sessions produced no new runtime errors in the inspected console slices.

Initial implementation compilation reported missing Playables extension methods. Adding the required namespace resolved those errors before testing.
The initial animal-idle lookup found no clips through prefab dependencies. The scene now uses each animal's existing `CreatureVisualSettings.Idle`.

Evidence directory: `local-only/actor-performance/2026-09-10/sidekick-swap/`.
It contains `regression-results.json`, `motion-sweep.json`, `movement-smoke.json`, saved fitting transforms, build logs, and captures.
Key captures include `rigid-accessories.png`, `walk-accessories.png`, `crawl-accessories.png`, and `cape-fit.png`.

## Remaining decision

Bryan's initial visual approval supports continuing the Sidekick trial alongside the animals and buildings.
The recommended next step is a simpler Sidekick outfit with removable equipment, followed by one legacy torso conversion trial.
Record garment fitting effort and deformation through movement before estimating the broader wardrobe work.
The original POLYGON character remains intact. Switching the gameplay default and broad clothing conversion are not part of this completed trial setup.

## Body-part experiment — 2026-09-10

Bryan requested a bare-body comparison and a Sidekick assembled with POLYGON parts.
The new scene preserves the previous style scene and its fitted accessories.
It retains the deer, wolf, doorway, and roof references under common lighting.

The bare character uses 22 human base meshes from the installed Sidekick pack, including underwear and a separate head.
The hybrid replaces its torso, upper arms, and lower arms with five parts from the current POLYGON hero.
Its head, hands, hips, legs, and feet remain Sidekick parts. It is not a complete outfit conversion.
Both new prefabs use the validated Sidekick avatar and contain 22 skinned renderers.

### Experimental mapping

The first attempt directly combined target bone transforms with source bind poses. Different bone axes caused severe twisting.
The revised method translates weighted anatomical anchors in a common rest frame and rebuilds the target bind poses.
Spine anchors preserve normalized hip-to-neck height; equal-numbered spine bones occupy different heights in these rigs.
Original garment topology, UVs, materials, and skin weights remain intact in the derived meshes.
The `SkeletonFit` blend shape exposes original proportions at zero and the landmark fit at 100.
This method is an experiment for the five selected parts. It does not prove general wardrobe compatibility.

The right panel exposes muscular, heavy, skinny, and masculine/feminine shapes on native Sidekick parts.
Those shapes are not transferred to POLYGON parts. Moving these sliders deliberately exposes the remaining seam mismatch.
The left panel retains synchronized animation playback, pause/scrub, and camera views for all three characters.

### Rendering diagnosis and limits

Edited mesh assets retained stale GPU skinning data in this Editor, although CPU baking showed the revised geometry correctly.
Uploading readable mesh data and rebuilding renderer bindings resolved the inspected live rendering discrepancy.
The review component performs this refresh after animation initialization on its first late update. This workaround remains scoped to the fitting scene.
Early repeated camera captures were byte-identical after scene changes; final evidence uses direct camera rendering.
`body-cpu.png` is a static diagnostic capture and is not live-rendering acceptance evidence.

Neck and wrist seams, exposed-skin colour, and deformation at extreme poses still require visual review.
The next algorithm step is seam correspondence and body-shape transfer, after Bryan reviews this fixed-body-size fit.
No gameplay default or source POLYGON mesh changed.

### Body experiment validation

Targeted job `2915c266be714fbfa3fe42cb5d5f1a53` passed 77 tests with no failures or skips.
The run covers 19 humanoid cases on each of four prefabs, plus the existing review-control test.
The two new fixtures cover the bare and hybrid prefabs. Test names truncate their long paths in runner output.
Full results are saved in the evidence directory as `body-tests.json`.
Core, Planet, and Editor code-health builds passed; existing warnings remain in `body-*-build.log`.
The final fresh Play session initialized the upload workaround automatically and rendered the live hybrid correctly in the inspected idle view.
A 13-clip sweep sampled 12 phases per clip on all three actors: 156 poses, zero display-root drift, and finite converted vertices.
The largest converted-part bounds diagonal in that sweep was approximately 1.321 metres. This is a sanity check, not seam acceptance.
The native heavy slider reached 100, and the POLYGON fit slider reached zero and reset to 100. Results are in `body-sweep.json`.
The inspected final console contained no errors. `body-startup-verified.png` records the fresh-session close view.

## POLYGON body-shape transfer — 2026-09-10

Bryan accepted the neutral hybrid as a first fit and requested muscular, heavy, skinny, and feminine shapes on its POLYGON parts.
All five converted meshes now contain those four frames, plus the existing `SkeletonFit` frame.
The new frames are `POLYGONBlends.defaultBuff`, `POLYGONBlends.defaultHeavy`, `POLYGONBlends.defaultSkinny`, and `POLYGONBlends.masculineFeminine`.
Earlier notes that POLYGON shape transfer was unimplemented describe the previous stage and are superseded by this section.

The Editor bake projects each fitted garment vertex onto the nearest triangle of a shared bare-body reference surface.
It interpolates the native body-shape displacement with barycentric weights and recalculates normals using the garment's topology.
The reference includes the head, torso, arms, hands, and hips. Native source shapes each contain one frame at 100%.
Existing garment offsets from the body are retained. The method does not weld neutral seams or recolour exposed skin.
Rigid details such as armour and straps deform with the transferred surface field; their thickness is not independently constrained.
Extreme combinations have no additional corrective shapes. The new endpoints await Bryan's visual review.

The bake took approximately 4.29 seconds for 3,320 converted vertices and 20 new frames in this run.
Maximum reference-surface distances were 0.0804 m for the torso, 0.0573 m for upper arms, and approximately 0.039 m for lower arms.
These distances include garment offsets; they are not seam-error measurements.
The mesh assets cache the result. Runtime uses blend-shape weights, with no surface search or mesh generation per frame.
At `SkeletonFit = 100`, all sliders drive native and converted parts together.
Reducing the fit amount also reduces converted body-shape weights, so zero retains the original POLYGON proportions.
The original POLYGON comparison actor remains unchanged.

`Tools/Actors/Sidekick/Bake POLYGON Body Shapes` rebakes only the shape frames.
The existing refit command now rebakes them after changing fitted geometry.
The pre-change derived meshes and authoring scripts are archived under `body-shapes-before/` in the evidence directory.

Validation:

- Job `4c9454e291ab47979d6d36c8e51b0fe2`: five tests passed, zero failed or skipped.
- Four cases verify saved nonzero finite frames on all five parts, shared slider control, fit-zero behaviour, and reset.
- The existing playback-control regression also passed. Full results are in `shapes-tests.json`.
- Repeating the bake produced identical vertex deltas for all frames and unchanged base vertices and triangles; see `shapes-repeatability.json`.
- Core, Planet, and Editor builds passed. The inspected final runtime console contained no errors.
- Live captures cover muscular, heavy, skinny, feminine, and heavy crawling. Files are `shape-muscular.png`, `shape-heavy-first.png`, `shape-skinny.png`, `shape-feminine-first.png`, and `shape-heavy-crawl.png`.
- The rapid animation sweep returned identical baked bounds across shape presets. Its `shapes-animation-sweep.json` is diagnostic only, not confirmation of shape deformation.

The first build reported `No overload for method 'FirstOrDefault' takes 2 arguments`; the implementation now uses the supported LINQ overloads.
The first test run reported `Assertion failed on expression: 'ShouldRunBehaviour()'` after calling `LateUpdate` through `SendMessage` in EditMode.
Tests now call `ApplyBodyShapes` directly. The rerun passed without suppressing the assertion.

## Full-body extension — 2026-09-10

`SidekickFullBodyReview.unity` compares bare Sidekick, the accepted upper-body hybrid, a new full-body hybrid, and the original POLYGON hero. Animals and building samples remain in the scene.

The new candidate adds POLYGON hips, legs with boots, and gloves. Ten converted meshes contain 6,294 vertices and 40 cached body-shape frames. Ten native head and facial renderers remain. The shared fitting and surface-transfer algorithms generate the assets during authoring. Runtime sliders apply saved frame weights.

The source leg names disagree with their bones; mapping follows the bones. The gloves have fewer finger chains than Sidekick. Several fingers therefore move together. Terminal finger and toe mappings retain the source tip offset.

Validation:

- All 104 tests passed, with zero failures or skips. Evidence: `local-only/actor-performance/2026-09-10/sidekick-swap/full-body-tests.json`.
- Core, Planet, and Editor builds passed. Graphify update completed.
- The scene has no missing scripts, meshes, bone references, or materials. The final console check returned no errors.
- Hash comparison found zero changes to the accepted upper-body scene, prefab, and five derived meshes.
- Live captures cover heavy, muscular walking, skinny running, feminine crouching, heavy crawling, and mixed-shape swimming.

The captures support this initial fitting trial. They do not establish universal clothing compatibility or gameplay contact correctness. Skin colour, seams, glove detail, and user acceptance remain open. The gameplay default remains unchanged.

## Skin matching and second outfit — 2026-09-10

Bryan accepted the initial full-body trial and asked to continue. The next pass matches exposed POLYGON body skin to the Sidekick head. It preserves the head, clothing, armour, and leather colours.

`SidekickSkinAuthor` identifies the reviewed Fantasy Hero skin swatch (255, 204, 174) and assigns those triangles to the existing Sidekick material. Their UVs select its native skin texel (213, 165, 123). Clothing triangles retain their original UVs and material. Four Outfit 01 parts gain a material section. The pass does not edit vendor textures or shaders. It does not add a per-frame fitting operation.

Matched camera captures: `skin-matched-before.png` and `skin-matched-after.png` in the existing local evidence folder. Both use neutral shapes, idle phase 0.4, camera position (-1.2, 1.7, -3.4), and focus (-1.2, 1.05, 0). The before image temporarily restores original skin UVs/materials on runtime mesh copies. The after image restores the saved converted meshes. The lightweight review scene uses direct Camera.Render captures; it has no planet seed or F10 domain capture system.

The neck inspection found closed source surfaces after position welding for edge counting. Colour matching reduces the visible join. This pass does not change or weld seam geometry. The heavy neck rear capture remains available as `refinement-heavy-neck-back.png`.

`SidekickOutfitReview.unity` compares bare Sidekick, Outfit 01 on Sidekick, Outfit 02 on Sidekick, and Outfit 02 on POLYGON. Animals and building samples remain. Outfit 02 uses ten existing `_Male_02` meshes from `ModularCharacters.fbx`. It uses the same fitting rules and 40 generated shape frames, with no outfit-specific fitting offsets. No new vendor import occurred.

Validation:

- All 129 tests passed, zero failed or skipped; job `72d8505043f54fe5a96d94a727f46793`. Full results: `refinement-outfit-tests.json`.
- Core, Planet, and Editor builds passed; `refinement-*-build.log` files contain the output.
- All ten Outfit 01 meshes retain identical saved body-shape frames and bind poses; see `refinement-shape-preservation.json`.
- Outfit 02 live captures cover neutral front, heavy crouching, skinny running, muscular back, and feminine walking. See `outfit02-*.png`.
- The first test job did not initialize: `Test job failed to initialize (tests did not start within timeout)`. The stable Editor rerun passed.
- Hot Reload briefly reported `The name 'SidekickSkinAuthor' does not exist in the current context` while the new file imported. Full compilation and the later asset generation succeeded.

This supports a second outfit from the same pack. It does not establish compatibility with different rigs, palettes, dresses, or loose cloth. The source gloves still couple several fingers. Clothing collision and close seam detail remain visual review items. Gameplay still uses the original default.

Final fresh Play run: `SidekickOutfitReview.unity` had zero missing scripts, meshes, bones, or materials. The console returned zero errors or warnings. `outfit-comparison-final.png` records the four-character comparison. Unity remains in Play mode with shared motion and body-shape controls available.

## Fantasy Kingdom robe and peasant outfit — 2026-09-10

Bryan approved testing loose clothing and another POLYGON pack. The trial imports one 3.36 MB character FBX from `D:/Unity/Explore Assets/Assets/Synty/PolygonFantasyKingdom/Models/FantasyKingdom_Characters.fbx`. Its bytes match the source. The source GUID had no collision. Existing Kingdom materials and palette serve both outfits. No vendor scripts, shaders, or new textures were imported.

`SidekickKingdomReview.unity` compares the original monk and peasant with their Sidekick conversions. The monk supplies a robe and loose sleeves; the peasant supplies a layered tunic. Both use combined character meshes rather than the Fantasy Hero modular sections.

The new preparation step removes source head triangles from derived copies. It uses the combined Head/Eyes/Eyebrows/Jaw influence and excludes triangles that touch a vertex above 50%. This is a reviewed boundary for these two assets. It preserves source vertex indexing; unused head vertices remain stored but do not render. It does not modify the source mesh.

The same joint-anchor fitting, nearest-triangle body-shape transfer, and skin-palette conversion handle both bodies. Only the expected converted-part count changes from ten modular sections to one combined body. Bone mappings reuse the reviewed Fantasy Hero mapping and add Root/head/facial entries. Both inspected palettes use the same source skin swatch. Each candidate stores four body-shape frames plus SkeletonFit.

Initial live captures: `kingdom-first.png`, `kingdom-heavy-crouch.png`, `kingdom-skinny-run.png`, `kingdom-feminine-walk.png`, `kingdom-muscular-idle.png`, `kingdom-heavy-crawl.png`, and `kingdom-mixed-swim.png`. They reside in `local-only/actor-performance/2026-09-10/sidekick-swap/`. These use the lightweight review scene's direct Camera.Render capture path.

The original references initially failed four movement assertions. The source importer mapped Humanoid Hips to stationary `Root`. The local importer now maps it to `Hips`; the scratch source remains unchanged. The Sidekick candidates passed those checks before the correction. Archived first run: `kingdom-tests-before-avatar-fix.json`.

Exact initial failures, repeated for both original references:

```text
Exit must return the pelvis above the ground.
Expected: greater than 0.75f
But was:  -0.00364392996f

The clip displacement must not be added a second time at phase 0.5
Expected: less than 0.200000003f
But was:  0.631103933f
```

The robe remains a skinned garment that follows the source leg weights. This trial does not add cloth motion, self-collision, or body collision. The first pose captures support rig and shape transfer, but do not prove all-pose collision clearance. Head cut boundaries, collar detail, and extreme combinations remain visual review items.

Kingdom validation completed:

- All 215 tests passed, zero failed or skipped, after the avatar correction. Job: `e403996b51c54569894784ee46d73332`. Full results: `kingdom-tests.json`.
- Core, Planet, and Editor builds passed. Logs: `kingdom-core-build.log`, `kingdom-planet-build.log`, and `kingdom-editor-build.log`.
- Graphify update completed with exit code 0; see `kingdom-graphify.log`.
- A fresh Play run had zero missing scripts, meshes, bones, or materials. The console returned zero errors or warnings.
- Original references now resolve Humanoid hips to `Hips`; candidates resolve to `pelvis`.
- `kingdom-corrected-walk.png` shows all four characters after the avatar fix. `kingdom-comparison-final.png` records the neutral comparison.

Result: rig and shape transfer work for these two combined Fantasy Kingdom characters, in addition to the two modular Fantasy Hero outfits. This required a source-head preparation step and a source-avatar correction, not new garment-fitting offsets. No claim of universal clothing or cloth-physics compatibility follows. Bryan's visual acceptance of the Kingdom candidates remains open.

## Front robe overlap and accessories — 2026-09-10

Bryan identified the overlap between the front leg panels in the supplied GIF. The dark thigh patch is not the reported defect.

The original monk mesh also intersects itself during walking. A CPU skinning diagnostic checked left/right cloth triangles over 24 walk phases. It found 45 intersecting triangle-pair samples, with a maximum of seven in one phase. These counts are diagnostic samples, not a count of distinct visible defects. The lower skirt contains triangles that bridge the left and right leg influences.

Temporary experiments changed weights or narrowed the lower robe. None established a complete repair. Symmetric weights reduced cloth-only crossings to 14 pair samples. Uniform height-based weights reduced them to seven. Pinning cloth to the pelvis removed cloth self-crossings, but produced other cloth/body crossings. None of these experiments changed a saved mesh. `robe-intersection-diagnostic.cs.txt` records the original cloth-only diagnostic in the local evidence folder. The overlap remains open. A dedicated skirt deformation or collision treatment must preserve foot clearance as well as panel separation.

`SidekickAccessoryReview.unity` adds sixteen individually selectable attachments across the four original/Sidekick comparison actors. It preserves the Kingdom scene and garment meshes. The scene reuses the priest hat and mage cape. It imports the explorer backpack and belt pouch from the same owned Kingdom pack. All use the existing Kingdom atlas and material. Animals and building samples remain available for style review.

| Item | Trial implementation | Observed limit / next work |
|---|---|---|
| Priest hat | Rigid attachment to Head with a neutral fit offset | Appears usable in inspected poses; other hats need their own fit |
| Explorer backpack | Rigid attachment to UpperChest | Heavy crouching exposes body overlap; add body-shape-dependent mount offsets and inspect straps |
| Belt pouch | Rigid attachment to Hips | Skinny body leaves excess clearance; mount position must follow hip shape |
| Mage cape | Original seven-bone chain attached to UpperChest | No independent chain motion or collision; crawling exposes the stiff cape behavior |

Rigid attachments do not use the garment blend-shape transfer. Their local fit remains fixed while the parent bone animates. The cape retains its source skin and bones. No runtime mesh fitting, cloth solver, or per-frame mesh generation was added.

Validation:

- All 215 existing character tests passed, zero failed or skipped. Job `db7c778cbd01419aa59e28cba7e81193`; `accessory-tests.json` stores the result.
- A live sweep checked 260 pose/body combinations: thirteen motions, four phases, and neutral plus four individual body extremes. All sixteen attachment transforms retained their parent and local offset with finite world positions. This is attachment integrity evidence, not collision clearance proof.
- All accessory meshes and cape bones resolved. The scene had zero missing scripts or accessory materials. The fresh Play console returned zero errors or warnings.
- Core, Planet, and Editor builds passed. Logs use the `accessory-*-build.log` names. Graphify update passed.
- Inspected captures include `accessory-heavy-crouch.png`, `accessory-skinny-run-front.png`, `accessory-feminine-crawl-cape.png`, and `accessory-muscular-walk-back.png`.

The accessories support the same visual palette, but their mechanical fit is not universal. Shape-aware mounts and loose-cloth behavior are separate follow-up work. The robe overlap is not fixed. Gameplay still uses the original default character.

## Body-shape accessory mounts — 2026-09-11

The accessory scene now stores body-shape offsets on both Sidekick backpacks and both Sidekick belt pouches. The authoring step projects a reviewed mount anchor onto the nearest clothing triangle. It interpolates the existing four garment shape frames with the shared closest-point barycentric helper. It then transforms the resulting movement into the attachment parent's coordinates.

`SidekickAccessoryFit` combines these cached vectors with the current body sliders and skeleton-fit amount. It writes an absolute local position relative to the saved neutral position. Repeated updates do not accumulate movement. No runtime surface search, mesh bake, or new mesh asset is required. Bone animation still supplies attachment rotation and pose movement.

The heavy crouch capture shows the backpack moving out from the enlarged back. The skinny run capture shows the pouch following the reduced hip shape. This pass preserves the accepted neutral placement. It does not resize straps, rotate the mount with surface normals, or prevent hand/accessory collision. Extreme combinations still require visual review.

Validation:

- All 216 tests passed, including the new combined-shape, fit-scaling, repeated-update, and reset regression. Job `defac6ebdd5548d48725f90967092fa5`; result `mount-tests.json`.
- A fresh Play sweep evaluated 312 pose/body combinations across all thirteen motions. All four fitted mounts retained their parent and finite positions.
- Core, Planet, and Editor builds passed with existing warnings. Evidence logs: `mount-core-build.log`, `mount-planet-build.log`, and `mount-editor-build.log`.
- Before/after captures: `accessory-heavy-crouch.png` / `mount-heavy-crouch-after.png`, and `accessory-skinny-run-front.png` / `mount-skinny-run-after.png`.
- A first bake call encountered a type-load error during Unity's domain reload. The later stable bake succeeded and saved all four components.

Cape inspection confirmed that Unity's native Cloth component can initialize on the mage cape, producing 190 simulation vertices from its 731 mesh vertices. The temporary component was removed. No cape simulation was saved or motion quality validated. The next cloth trial needs shoulder constraints, body colliders, and explicit handling of pose scrubbing. The robe overlap remains unresolved.

## Native cape cloth trial — 2026-09-11

The accessory scene now contains native Unity Cloth on all four cape attachments. Initial trials on the original thick mesh remained rigid with full stretch constraints. Reducing stretch constraints demonstrated simulation movement but distorted the shape. Two subdivision passes provided additional bending geometry. The retained trial uses a derived mesh with 4,655 vertices and 6,016 triangles; its original surface and texture mapping remain intact in the undeformed pose.

Each cape has 3,010 simulation vertices and 813 shoulder pins. Torso, pelvis, and thigh capsule proxies provide approximate body collision. The simulation uses gravity, tethers, continuous collision, and a 120 Hz solver setting. Subdivision occurs once during authoring. Cloth simulation runs at runtime; it is not a cached animation result.

The new panel controls simulation and resets. Selecting another motion or scrubbing a paused pose resets cloth history. Pausing animation allows the cloth to settle. Body-shape sliders switch to rigid skinning because the collision proxies currently cover only the neutral body. Resetting the sliders restores cloth simulation.

This remains a visual trial. Floor, hand, backpack, and self-collision are absent. Crawling and other extreme poses can expose those limits. The source FBX, original garment meshes, and gameplay default remain unchanged. The robe front-panel overlap is still open.

Validation: the existing 216 tests passed in job `1d01d3cccf3d48f689983642c4eebf67`. The new cape test passed separately after import in job `d52a0e67aa5442f8b1af0ccbb327e9b3`. It checks source vertices, UV corners, bind poses, surface area, bounds, and normalized skin weights. The combined result is 217 passed, zero failed. Core, Planet, and Editor builds passed with existing warnings. Graphify update passed. Results and logs use `cape-*` names in the existing local evidence folder.

Captured comparisons include `cape-physics-crouch-trial.png` (coarse mesh), `cape-subdivided-trial.png`, `cape-saved-walk.png`, and `cape-saved-crouch.png`. Bryan's visual review remains the acceptance step.

The fresh Play stability sweep held each of thirteen motions for 45 rendered frames. All active cloth vertices stayed finite; the maximum local vertex radius was 1.258238 m. This checks numerical stability, not collision clearance. Live checks also confirmed the heavy-body fallback, simulation toggle, and reset behavior. The final console returned zero errors or warnings. Unity remains in Play mode on the Sidekick cape walking view.

## Accessory motion and robe clearance — 2026-09-11

The separate `Assets/Scenes/Tests/SidekickAccessoryMotionReview.unity` contains the next trial. The previous scene remains available for comparison.

- Pouches move 35 mm toward the body and swing from their upper attachment.
- Sidekick hats move down 45 mm.
- Capes use the original mesh and six cape bones with the existing `BoneChainSpring` solver. Six animated body capsules and floor contact support the cape.
- Both monk robes omit covered shin triangles and gain lower-cloth clearance. Feet remain visible below the hem. The source prefabs remain unchanged.
- The cup, strap, and fittings form a separate mesh. They move to a rear backpack attachment and swing independently.

The review panel includes an accessory secondary-motion toggle. Geometry changes remain cached; spring motion runs each frame. The shared solver supplies damping, gravity, angle limits, and contact projection. This adapter adds review resets and body capsule support.

The local catalog contains Tail Animator 2 and Magica Cloth 1.12.13. No vendor runtime code was imported. See the external asset catalog section 15.5 and the derived assets' `SOURCE.md`.

All 229 EditMode tests passed in job `a5fbc5eb3212440a8caeb37dfc2565cc`. This includes triangle partition, foot preservation, cached shape preservation, and existing spring tests. Core, Planet, and Editor builds passed. Graphify update passed. Evidence uses `refined-*` names in the existing local evidence folder.

The first shape test failed exact equality with `Values differ at index [840]`. Unity omitted sparse shape displacements when rebuilding frames. The largest measured difference was 7.35 micrometres. The test now permits 10 micrometres per vertex; no geometry change was needed.

The inspected walking poses no longer show the exposed shin patches. The relocated cup clears the arm in the inspected pose. These checks do not prove universal clearance. Bone contacts approximate the cape surface; cloth edges, extreme poses, and combined accessories can still intersect. The robe change does not implement general garment self-collision.

The live sweep covered thirteen motions for 585 editor updates with eight active accessory chains. All positions stayed finite. The maximum reported contact error reached 0.860136 m across the extreme poses. This is a failed full-motion clearance check, despite numerical stability. The cape remains a trial for further contact work. `refined-live-sweep.json` records the measurements. `motion-cape-final.png` shows the saved capsule configuration during walking. The final console returned zero errors.
