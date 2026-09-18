---
name: pp-asset-catalog
description: Search Bryan's Unity scratch project, asset catalog, and owned-package list for reusable models, animation, audio, materials, tools, or code references. Use before buying or building an asset equivalent, or when refreshing catalog coverage. Search only; use pp-asset-integration for adoption.
---

# Asset catalog

Find existing assets with verified paths and enough evidence to choose the next action.
Search does not authorize importing, downloading, purchasing, or opening another Unity project.

## Search route

1. Read [the source index](references/source-index.md). Select the relevant catalog section and candidate packs.
2. Search that section and the adoption map. Treat dated verdicts as leads, not current compatibility proof.
3. Check candidate paths in the scratch project. Use the folder snapshot to find uncatalogued candidates.
4. Search filenames within selected packs before reading text assets or source code.
5. Check the target project's assets before recommending another copy.

Interpret the request as an asset need, not an exact filename. For swimming, search `swim`, `water`, and relevant locomotion packs.
Distinguish a swimming clip from a swimming controller, icon, document, or demo scene.
Exclude `.meta` duplicates from result lists, but inspect importer metadata when verifying rigs or GUIDs.
Do not read binary FBX or texture files as text.

## Scoped commands

Run these from the ProceduralPlanets repository. Replace the example pack and search terms with verified candidates.

```powershell
rg -n -i 'swim|water locomotion' docs/research/2026-08-10-external-asset-catalog.md docs/research/2026-08-11-asset-adoption-map.md
$scratchAssets = 'D:/Unity/Explore Assets/Assets'
Test-Path -LiteralPath $scratchAssets
rg --files "$scratchAssets/Kevin Iglesias" -g '*.fbx' -g '*.FBX' -g '*.anim' | Select-String -Pattern 'swim|water'
rg --files Assets -g '*.fbx' -g '*.FBX' -g '*.anim' | Select-String -Pattern 'swim|water'
```

If the root is missing, use historical records to report leads and state that local availability is unverified.
Do not conclude that the library lacks an asset from one failed filename search.
Broaden to adjacent categories and the owned-package list, then report the exact coverage of any negative result.
An owned product is not necessarily downloaded. A cached package is not necessarily extracted into the scratch project.

## Verify and report

Return a short ranked table: candidate, asset kind, exact path, availability, reuse status, and relevant limitation.
Label evidence as file-verified, metadata-verified, Unity-verified, or historical only.
For clips, inspect rig type, avatar references, root motion, and clip names where metadata exposes them.
Leave visual quality and retargeting unknown until an appropriate Unity preview verifies them.
For code references, inspect arbitrary-up assumptions and world dependencies; finding prior art does not authorize adopting vendor runtime code.

Check GUID matches when both source and target `.meta` files exist.
Use content hashes for a few ambiguous candidate files; do not hash the entire library for an ordinary search.
Matching filenames alone do not establish that an asset is already imported.
Include catalog section pointers and the next useful verification step. Never return a full library listing.

## Refresh coverage

The source index is the compact router. The research catalog owns reviewed pack descriptions.
Keep raw file inventories out of `SKILL.md` and the shared memory index.
For targeted refreshes, enumerate only the selected pack and update only its affected catalog entries.
Record the scan date, exact root, scope, and whether the pass checked filenames, metadata, or Unity behavior.
Preserve historical verdicts when current evidence changes them; add a dated correction with its evidence.

For a broad refresh, compare top-level folders with `references/scratch-folders.txt` first.
Folder additions and removals show coverage gaps; folder timestamps cannot prove unchanged nested files.
Refresh relevant existing packs when requested or when their evidence is stale.
Replace the folder snapshot only after a successful enumeration. Update its verification date in the source index.
An unavailable drive must not replace the snapshot with an empty list.

## Provenance and maintenance

Verified 2026-09-09: the scratch Assets root exists and contains 131 top-level folders.
The historical catalog describes survey rounds from 2026-08-10; folder counts are not pack counts.
Recheck the root and folder count with `Get-ChildItem -LiteralPath 'D:/Unity/Explore Assets/Assets' -Directory`.
Recheck catalog sections with `rg -n '^## ' docs/research/2026-08-10-external-asset-catalog.md`.
Validate this skill with the skill-creator `scripts/quick_validate.py` command.
