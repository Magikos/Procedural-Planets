---
name: pp-asset-integration
description: Adopt selected models, animation, textures, audio, or generated assets from Bryan's Unity scratch project into ProceduralPlanets. Use for dependency selection, GUID preservation, project material conversion, and import verification. Use pp-asset-catalog first when the source asset is not selected.
---

# Asset integration

Integrate the selected asset into the existing project workflow with verified references and runtime evidence.
An asset search alone does not authorize integration. An explicit integration request authorizes routine steps within its scope.

## Establish the selected asset

Use `pp-asset-catalog` when the source is unknown. Otherwise verify the supplied source path directly.
Read the relevant catalog entry, adoption-map entry, and catalog sections 11 and 13 before copying vendor files.
The catalog and adoption map live under `docs/research/`; their paths are in the catalog skill's source index.
Revalidate dated compatibility claims against `ProjectSettings/ProjectVersion.txt` and `Packages/manifest.json`.
Use `pp-change-control` for change classification and `pp-validation-and-evidence` for required runtime or visual evidence.

The project's asset adoption rule is harvest-only: vendor runtime C# does not ship, and vendor editor tools stay in scratch.
Its source is `.agent-memory/claude/project_game_vision.md`, under "Asset adoption rule".
Prefer exported models, clips, textures, audio, and generated results over vendor behavior components.
Current explicit user instructions take precedence over historical adoption verdicts.

## Select dependencies

Inspect the nearest existing project integration for the same asset kind before creating a new pipeline.
List the selected files, required dependencies, destination, and reference changes.
Check the current worktree for user changes at those destinations.
Exclude unrelated demos, controllers, postprocessors, lighting settings, packages, and project settings.
Do not copy an entire pack to resolve one missing reference.

Preserve source `.meta` files when copying assets whose serialized references must remain intact.
Check source GUIDs against destination `.meta` files before copying.
Reuse an existing matching asset when it is the intended dependency.
If the same GUID identifies different content, resolve the conflict before import; never overwrite or regenerate GUIDs blindly.
For deliberately independent copies, generate new identities and remap their internal references through a controlled Unity workflow.
Use Unity dependency inspection when text GUID inspection cannot establish the full dependency set.
Never switch the active MCP Editor to the scratch project without checking its identity and the user's task scope.

## Copy the file, never the package

Never install a vendor package into this repo to obtain an asset. Copy the file
out of `D:/Unity/Explore Assets`. If it is not there, install the pack **in that
scratch project**, then copy the file from it. Do not preserve the vendor's
directory structure, and do not bring its scripts, shaders, controllers, demos,
or presets.

Rename away the vendor. `SM_Chr_*`, `SK_HUMN_*`, `PolygonFantasyHero_*` and
`HumanF@*` are vendor names. Rename to the project convention `Thing_NN`, and
move the `.meta` with the file so the GUID survives and serialized references
stay intact. Renaming a YAML asset rewrites its internal `m_Name` — the bytes
change, the GUID does not.

Place by domain, not by kind: `Art/Creatures/Deer` holds that deer's mesh,
texture, material, clips, and prefab together. The top-level domains are `Audio`,
`Characters`, `Creatures`, `Effects`, `Interactions`, `Materials`, `Props`,
`Vegetation`. A folder under `Characters/` that names a role is named for the
role, not for the pack.

Write a `SOURCE.md` in every set folder, in our words: what was taken, the pack
and source path, what was adapted to our systems, and what was deliberately not
imported. The `AssetOrigin` block Unity writes into a `.meta` is vendor
metadata, not our provenance record.

When moving existing assets, create the destination folders in a pass of their
own **before** any move. `AssetDatabase.CreateFolder` is deferred inside
`StartAssetEditing()` and auto-uniquifies, so batching the two together produces
junk folders. Verify afterwards that no GUID vanished; a GUID that still
resolves cannot have broken a serialized reference. String-based consumers
(`LoadAssetAtPath`, folder-scoped `FindAssets`, path consts) are not covered by
that proof and must be edited separately.

## Adapt to the project

| Asset kind | Required inspection |
|---|---|
| Static model | Scale, axes, bounds, mesh and material slots, existing project material conventions |
| Skinned model or clip | Rig type, avatar, bone paths, root motion, loop settings, intended target rig |
| Scatter prop | Existing prototype authoring, placement settings, project materials, LOD and impostor workflow |
| Texture | Color space, alpha use, filtering, compression, mip settings required by its actual shader |
| Audio | Import settings, looping, intended playback use, existing project audio route |

Do not infer successful retargeting from a clip name or Humanoid flag.
Preview clips on the intended rig. Use the current project movement and gravity contracts.
Evaluate art on the project shader when that is how the adopted asset will render.
Copying a vendor shader is not the default solution for missing or pink materials.
Read `.agent-memory/claude/project_scatter_lod_impostor.md` only for scatter adoption; reconcile its dated history with current code.

## Verify and deliver

Check import and compile errors, missing scripts, missing references, and unexpected imported files.
Inspect the affected asset in Unity and exercise its intended project path.
For visual changes, capture the evidence required by `pp-validation-and-evidence`; distinguish agent checks from Bryan's visual approval.
Check that unrelated files and scratch source assets remain unchanged.
Do not revert, reset, or delete user changes when an import fails. Restrict recovery to this task's known additions and edits.

Report adopted source and destination paths, reused dependencies, required adaptations, and verification results.
Record completed adoption in the existing adoption map when useful for future searches.
Label imported-but-unverified assets explicitly; a successful copy is not a successful integration.

## Provenance and maintenance

Written 2026-09-09 from the existing asset catalog, adoption map, and project adoption rule.
Reverify policy with `rg -n 'Asset adoption rule|harvest-only' .agent-memory/claude/project_game_vision.md`.
Reverify versions with `Get-Content ProjectSettings/ProjectVersion.txt` and `Get-Content Packages/manifest.json`.
Reverify current integrations with scoped searches under `Assets/AssetPacks` and `Assets/Resources`.
Validate this skill with the skill-creator `scripts/quick_validate.py` command.
