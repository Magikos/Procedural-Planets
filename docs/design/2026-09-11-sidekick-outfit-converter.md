# Sidekick outfit converter

Status: Implemented and validated. Bryan approved the wardrobe trial and asked to continue.

## Scope

The first reusable converter supports the imported Fantasy Kingdom combined character bodies. It uses the existing landmark fit, four body-shape bake, and skin-palette conversion. It does not claim support for arbitrary meshes or other rigs.

The original wardrobe scene remains the visual reference. This change packages the tested pipeline; it does not retune the accepted fit.

## Use

1. Stop Play mode.
2. Open **Tools > Actors > Sidekick > Outfit Converter**.
3. Select the Kingdom outfit and an output revision.
4. Mark integrated headgear and loose clothing where present.
5. Confirm that you inspected the source structure.
6. Select **Bake or reuse cached outfit**.

The converter selects the resulting Sidekick prefab. Outputs live under `Assets/Art/Characters/Human/Converted/<part>_<revision>/`. Each folder contains the original comparison prefab, Sidekick prefab, cached mesh, and `Conversion.json`.

## Preservation and cache behavior

The converter builds in a temporary preview scene and a unique staging folder. It validates the Humanoid avatar, mesh references, bones, materials, and five shape frames before publishing through Unity's asset move API.

The receipt records source identity, input dependencies, output file hashes, revision, and review flags. Matching inputs reuse existing assets. Changed dependencies, changed flags, edited outputs, missing files, and unreadable receipts do not trigger an overwrite. Use a new revision to retain the prior result.

No existing authored assets are migrated or renamed. The cache can become stale when source art, reference prefabs, materials, or converter code changes.

## Explicit limitations

- The default head cut removes head-weighted source headgear as well as the original head.
- For `SM_Chr_Mage_01`, the headgear flag preserves reviewed teal cloth (`497D7E`) and gold trim (`C59E60`). The hood stays in the combined mesh. This is a source-specific palette rule, not general headgear extraction.
- Other integrated headgear still requires separate fitting. The flag records that follow-up.
- Loose clothing retains source skin weights. The cloth flag records required follow-up; it does not add simulation.
- Boots and gloves in these combined bodies do not prove arbitrary modular seam compatibility.
- The output remains a fitting candidate until visual review.

See [the wardrobe fit matrix](../research/2026-09-11-sidekick-wardrobe-matrix.md) for tested examples and exceptions.

## Validation

Required checks: safe output names; stable GUIDs and receipts on cache hits; rejection of edited outputs and changed inputs; preservation of the active scene; and comparison with the accepted rider conversion.

The first test run found `Cannot create a new scene additively with an untitled scene unsaved.` The converter now creates a preview scene and instantiates both authoring objects directly into it. The test checks the original active scene, root count, and dirty state after conversion. No user scene is saved to work around this restriction.

A later test reported `Expected: <System.IO.IOException> But was: null` after editing a cached mesh. Unity retained its earlier dependency hash during that frame. Output validation now hashes the saved prefab, mesh, and metadata files directly. The test edits a real generated mesh, saves it, and verifies that conversion refuses to overwrite it.

All thirteen focused tests passed in job `9347bd2d3c2848eda3e0c192282b28c1`. This includes the five converter checks and eight existing Kingdom/wardrobe cases. Core, Planet, and Editor builds passed. Graphify update passed.

Retained `v1` conversions exist for Rider and Priest. The Priest receipt marks cloth review as required. Both outputs exactly match their accepted wardrobe meshes in base vertices, triangles, UVs, and all five positional shape frames. The maximum measured frame difference was zero. A repeat Rider call reused the cached output.

Evidence: `local-only/actor-performance/2026-09-10/sidekick-swap/converter-tests.json`, `converter-parity.json`, and `converter-*-build.log`.

A fresh Play check used both newly generated prefabs in the existing review harness with muscular 60, heavy 50, and feminine 75. They animated with zero console errors. Captures: `converter-cached-rider-play.png` and `converter-cached-priest-play.png`. The temporary runtime replacements were discarded on leaving Play mode.
