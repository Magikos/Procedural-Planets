# Sidekick wardrobe coverage

## Scope and acceptance

This trial reuses the imported Fantasy Kingdom and Fantasy Hero models. It tests the existing conversion without outfit-specific deformation tuning.

The four paired outfits are Rider, Soldier Male, Blacksmith Female, and Priest. The separate modular helmet uses the soldier body. A fifth Mage conversion remains saved separately; its tunic was too short for the long-robe case.

Numerical acceptance requires valid avatars, complete bone references, finite posed vertices, and five cached frames per converted mesh. Clothing triangle topology must survive the reviewed head cut. All thirteen review motions run against neutral, muscular, heavy, skinny, feminine, and mixed body settings.

Visual acceptance requires usable neck, wrist, ankle, and helmet fit in front, back, and bent poses. Record visible intersections as exceptions. Numerical stability does not prove collision clearance. Bryan approves the final look.

## Reproduction

Open `Assets/Scenes/Tests/SidekickWardrobeReview.unity` and enter Play mode. Use the named camera views for each pair. POLYGON references sit on the left. Sidekick conversions sit on the right. The helmet sample sits beside the soldier pair.

Use the existing motion controls, phase slider, and body sliders. Reset all four body sliders to zero for the neutral reference. Skeleton fit remains at 100.

## Results

The accepted accessory scene and gameplay default remain unchanged. These are review candidates, not a universal compatibility claim.

The initial full regression run passed 234 tests (job `c7a48becf39949ec85e7fbe8489e197f`). The first runner attempt reported `Test job failed to initialize (tests did not start within timeout)` before executing tests. The retry used a longer initialization timeout. The added priest case receives a focused follow-up run.

The helmet initially reported `Unmapped POLYGON bone: Head_Attachment`. Inspection found additional head, shoulder, and chest attachment bones. The author now maps them to their verified anatomical parents. This is a mapping extension, not a mesh-specific hand sculpt.

The mage's integrated hood demonstrates a limitation of the combined-head cut. The cut removes head-weighted hood geometry along with the source head. Its lower collar remains. Reusing that hood requires a separate headgear extraction and fit.

| Sample | Provisional fit class | Finding and remaining work |
|---|---|---|
| Rider clothing, boots, gloves | Automatic fit candidate | Existing conversion handles the silhouette and covered extremities. No outfit-specific vertex edits were applied. |
| Soldier armor, gauntlets, greaves | Automatic fit candidate | Walking and heavy crouch retain usable proportions. Plates still bend with skinning; rigid-piece behavior is not provided. |
| Blacksmith female clothing and apron | Automatic body fit; cloth refinement | Female-authored torso transfers to the Sidekick body shapes. The apron follows leg skinning; independent apron motion needs a separate pass. |
| Priest long robe | Adjustment required | Walking fit is usable. Bent poses pull the skirt with the legs and create sharp folds. The converter does not supply cloth clearance or self-collision. |
| Modular helmet | Small mapping adjustment completed | Head and attachment mapping now bake successfully. Neutral fit covers the head without a manual vertex sculpt. Other helmet styles remain untested. |
| Mage tunic and integrated hood | Custom headgear work | Body conversion succeeds, but the head-weight cut removes the upper hood. Extract and fit the hood separately. |

The live sweep evaluated 312 combinations: thirteen motions, six body settings, and four phases. Nine actors and sixty renderers produced zero non-finite used vertices. The maximum measured actor-local vertex radius was 3.06492639 m. This is a numerical integrity check, not a collision test. The six shape settings were neutral; each single shape at 100; and muscular 60, heavy 50, feminine 75 combined.

The focused follow-up passed all six wardrobe tests, including Priest (job `b18cd51f76584184ac1c1ab3386b9332`). Together with the preceding regression run, 235 distinct tests passed. Core, Planet, and Editor builds passed. Graphify update completed without API cost.

Evidence is under `local-only/actor-performance/2026-09-10/sidekick-swap/`. Files include `wardrobe-tests.json`, `wardrobe-priest-tests.json`, `wardrobe-live-sweep.json`, and `wardrobe-*-build.log`. Paired walking captures use `wardrobe-walk-0.png` through `wardrobe-walk-3.png`. Heavy crouch, mixed climbing back views, and skinny swimming captures use matching descriptive names. Early `wardrobe-neutral-*` captures include the superseded layout and Mage sample; use the walking captures for the saved scene.

These tests cover boots and gloves integrated into the selected combined meshes. They do not prove arbitrary modular wrist or ankle combinations. The next conversion tool should preserve explicit headgear extraction, covered-skin masks, cloth policy, and attachment mappings per outfit. A nearest-surface body-shape projection alone cannot infer those choices.

Additional inspected captures show the feminine blacksmith and helmet, plus muscular running armor. The final Play scene has nine actors, six converted renderers, zero missing scripts, and zero console errors. Unity remains on the walking armor comparison with neutral body settings.

Follow-up: the tested bake is now available through **Tools > Actors > Sidekick > Outfit Converter**. It creates versioned outputs, reuses matching cached assets, and records headgear/cloth review requirements. See [the converter workflow](../design/2026-09-11-sidekick-outfit-converter.md). This adds no new cloth simulation or automatic hood extraction.
