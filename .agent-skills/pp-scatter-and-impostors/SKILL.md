---
name: pp-scatter-and-impostors
description: Use for scatter prototype adoption, LOD transitions, impostor baking, atlas identity or import settings, and distant vegetation or prop defects. Covers source-library and generated props. General capture mechanics belong in pp-diagnostics-and-tooling.
---

# Scatter and impostors

Locate the owning stage before changing placement, geometry, bake settings, or shading.
Use the current source map below rather than copying constants from old investigation notes.

## Route the work

| Question | Read first |
|---|---|
| What gets placed and drawn? | `Assets/Scripts/Planet/Scatter/ScatterPrototype.cs`, `ScatterDtos.cs`, `ScatterField.cs`, `ScatterRenderer.cs` |
| Which mesh or card draws at this size? | `Assets/Scripts/Planet/Scatter/ScatterLodBatcher.cs`, `ScatterImpostorFactory.cs` |
| How is the atlas baked and interpreted? | `Assets/Scripts/Planet/Scatter/ScatterImpostorBaker.cs`, `Assets/Scripts/Planet/Props/GeneratedImpostorManifest.cs` |
| Bake imported source prototypes | `Assets/Editor/ScatterImpostorBakeTool.cs` |
| Bake runtime-generated prototypes | `Assets/Editor/GeneratedImpostorBakeTool.cs` |
| Validate the bake or transition | `Assets/Editor/ScatterImpostorValidator.cs`, `ScatterImpostorContactSheetTool.cs`, `ScatterLodSilhouetteAudit.cs` |
| Inspect a controlled gallery | `Assets/Editor/ScatterLodGalleryTool.cs` |

Paths in the same table cell share the first file's directory.
Search the relevant current files for command names, output paths, thresholds, and prerequisites before execution.
For vendor asset selection and import, use `pp-asset-catalog` and `pp-asset-integration`.

## Diagnose and change

1. Identify the actual runtime prototype, its meshes, materials, bounds, and source or generated bake route.
2. Record a scenario through `pp-validation-and-evidence`: seed, pose, quality, lighting, effective overrides, and transition range.
3. Separate missing placement, missing draw submission, invalid atlas, silhouette mismatch, lighting, and culling.
4. Compare mesh-only and card behavior using existing diagnostics. Change only the stage supported by evidence.
5. Reuse the shared batcher, factory, baker, and validator. Do not build a parallel preview renderer.

A source-library test does not prove the generated prototype's appearance.
Inspect the final injected mesh and material combination when the planet uses generated variants.
Check actual cache/manifest identity; a species or placement grouping key does not prove interchangeable appearance.
Do not assume matching GUIDs or names mean the saved atlas matches current geometry and materials.

## Bake and verify

Use the matching bake tool, after reading its current Play Mode requirement and output ownership.
Both source and generated bake entrypoints require Play Mode as checked on 2026-09-09.
Inspect view-grid size and cell resolution independently for each route.
The current generated factory and source-library tool use different settings; do not normalize them without a separate reason.
Validate color/coverage and surface-data textures according to their own importer rules.

Changing code or importer defaults does not update previously baked assets.
Regenerate or reimport affected outputs through the existing tool and inspect the saved results.
Do not delete old atlases after a partial or failed bake. Check the tool's cleanup conditions before a broad run.
Use `pp-change-control` migration checks when asset identity or serialized atlas references change.

Compare the near mesh, transition, distant card, silhouette, lighting, and shadows.
Use a controlled gallery for isolation and the actual planet for acceptance.
Report sample coverage, invalid or missing cards, outliers, and the exact bake route used.
Use focused existing tests if they cover the changed logic; tests do not replace Unity bake or visual evidence.

## History and maintenance

Read `.agent-memory/claude/project_scatter_lod_impostor.md` only when a symptom matches an earlier investigation.
That file contains superseded bake policies. Read its dated corrections before reusing a value or verdict.
Keep procedural knowledge here and historical evidence there; link instead of copying investigation narratives.

## Provenance and maintenance

Source routes and Play Mode checks inspected 2026-09-09 in the dirty working tree.
Reverify with `rg -n 'OctGridN|AtlasCellPixels|AppearanceHash' Assets/Scripts/Planet/Scatter`.
Reverify entrypoints with `rg -n 'MenuItem|isPlaying' Assets/Editor -g '*Impostor*.cs' -g '*ScatterLod*.cs'`.
Reverify test coverage with `rg --files Assets/Tests/EditMode -g '*Scatter*.cs' -g '*Impostor*.cs'`.
