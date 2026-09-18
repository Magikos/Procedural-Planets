# Unity asset and serialization migration checks

Apply the relevant checks when changing serialized data or asset identity.
Do not run a repository-wide migration for a scoped rename.
Use `pp-settings-and-flags` for setting ownership and SO-to-DTO wiring; this reference owns preservation across changes.

## Establish the migration boundary

Record the old and new field, type, path, or identity; affected asset families; expected value mapping; and allowed behavior changes.
Search existing project migration code before writing another converter.
Identify scenes, prefabs, variants, ScriptableObjects, importer settings, and persistent data that actually depend on the change.
Distinguish serialized references from string paths such as resource lookups, editor tooling paths, and configuration entries.

Capture a representative pre-change asset with non-default values and known references.
Include a prefab instance override or variant when that asset family uses them.
Record the current dirty state and preserve task-relevant originals outside tracked source when recovery requires them.
A source-control revision does not preserve uncommitted authored values by itself.

## Select the preservation method

| Change | Preservation check |
|---|---|
| Serialized field rename | Use the existing project pattern and `FormerlySerializedAs` where applicable. Verify old authored values after reload. |
| Field type, units, or structure change | Define explicit conversion, bounds, defaults, and missing-value behavior. A rename attribute is not a data converter. |
| Enum changes | Inspect stored numeric values and consumers. Preserve established values or explicitly migrate their meanings. |
| Class, namespace, assembly, or managed-reference change | Inspect the actual serialized representation and supported Unity migration mechanism. Verify old instances load; a field rename attribute is insufficient. |
| Asset or folder move | Prefer Unity asset move operations. Preserve the asset's `.meta` identity and check string-based path consumers separately. |
| Subasset replacement or regeneration | Check both GUID and local file ID references. Keeping the containing file's GUID alone is insufficient. |
| Persistent save change | Reuse the existing versioned reader or migration path. Verify old copied data and the newly written format. |

Unity documents `FormerlySerializedAs` for retaining serialized values across field renames.
Do not remove compatibility attributes until the required old asset population no longer depends on them.
[Unity field rename API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Serialization.FormerlySerializedAsAttribute.html)

Unity stores asset identity and import settings in `.meta` files.
If moving files outside Unity, move their `.meta` files with them and coordinate Editor refresh.
Never resolve an identity problem by deleting `.meta` files indiscriminately.
[Unity asset metadata](https://docs.unity3d.com/6000.0/Documentation/Manual/AssetMetadata.html)

Check the return value from `AssetDatabase.MoveAsset`; a non-empty string reports an error.
Do not continue dependent operations after a failed move.
[Unity asset move API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.MoveAsset.html)

## Apply and regenerate

1. Limit conversion to identified assets. Report skipped or rejected inputs explicitly.
2. Make reruns safe through the existing version or completion mechanism; avoid applying numeric conversions twice.
3. Use Unity serialization APIs when text replacement cannot safely preserve nested data, overrides, or references.
4. Review the resulting diff before saving or reserializing unrelated assets.
5. Identify derived outputs that depend on changed data, such as baked atlases, generated meshes, or runtime snapshots.
6. Regenerate only affected outputs through their existing workflow. Editing the generator does not update previously baked files.

A default initializer does not prove that existing assets received a new value.
Do not overwrite authored values with defaults unless the requested migration requires that mapping.
Avoid broad force-reserialization as an incidental cleanup step.

## Verify persistence and recovery

Reload or reopen affected assets after saving; do not judge success only from live in-memory objects.
Compare representative values, object references, missing-script state, and prefab overrides against the recorded baseline.
Exercise the migrated runtime path and any required regeneration through the existing validation workflow.
Use the scenario record from `pp-validation-and-evidence` when replay requires controlled state.
For persistent data, read a copied old fixture, convert it, write it, and read it again.

Before a broad conversion, verify recovery on a small representative copy.
Restore code, authored data, and generated outputs as a compatible set when recovery requires all three.
Restore only this task's changes. A whole-file checkout can erase earlier user edits even when this task also touched the file.
Stop dependent writes when conversion or preservation checks fail. Preserve the original input and exact error for diagnosis.

Report migrated, unchanged, rejected, and unverified asset groups with evidence paths.
Separate code-health, Unity reload, runtime, and visual approval results.

## Provenance and maintenance

Added 2026-09-09. Project version at authoring: `6000.7.0a5`.
The linked Unity 6.0 documentation establishes the basic APIs; it does not validate every behavior in the project's alpha Editor.
Check `ProjectSettings/ProjectVersion.txt` and current version documentation for type or managed-reference migrations.
Revalidate preservation with the actual project asset forms before extending a converter's scope.
