# Project skill maintenance — 2026-09-09

Scope: the 22 committed project skills, their supporting Markdown, README routing, and Claude discovery stubs.
Source checks used the dirty `harvest-vertical-slice` working tree. Existing unrelated edits were preserved.
This pass checked structure across the library and selected current-state claims. It was not a full runtime or factual audit.

## Added skills

| Skill | Purpose |
|---|---|
| `pp-scatter-and-impostors` | Source and generated bake routes, identity, importer settings, transitions, and planet evidence |
| `pp-creature-and-animation` | Authority/presentation boundaries, behavior, rigs, lifecycle, persistence, and repeatable scenarios |
| `pp-skill-maintenance` | Evidence-based repairs to routing, stale claims, duplication, references, and generated discovery |

Each skill has a source file, UI metadata, and a generated discovery stub.
Current source maps replace copied implementation histories.

## Verified repairs

| Issue | Repair and evidence |
|---|---|
| Invalid YAML descriptions | Quoted the descriptions in `pp-research-methodology` and `pp-visual-migration-campaign`; both now pass the validator. |
| Incomplete discovery | Generated the missing `pp-code-audit` stub and the three new stubs. Synchronized `pp-failure-archaeology` and the two repaired descriptions. |
| Broken water-reference links | Corrected the relative depth of two links in `pp-gpu-rendering-reference/water-atmosphere-precipitation.md`. Both target design files exist. |
| Stale test-framework guidance | Updated architecture, proof, audit, and validation guidance. `CLAUDE.md` Tests explicitly permits the existing framework; manifest contains Unity Test Framework 1.8.0. |
| Old Editor/package requirements | Build guidance now records Unity `6000.7.0a5`, revision `a15235a53881`, and URP/Shader Graph `17.7.0` from current project files. Launch guidance points to `ProjectVersion.txt`. |
| Machine-local version claims | Build guidance now resolves installed dotnet and Graphify versions instead of asserting old recorded installations. |
| Stale branch routing | README and shared memory now query the current branch rather than assuming `code-refactor`. Historical branch labels remain in dated records. |
| Obsolete caustics prohibition | The rendering router now describes fragile caustics. The current change-control skill records that the prohibition was lifted. |
| Superseded scatter bake settings | Added a dated correction to scatter memory and shortened its memory-index entry to a pointer. Factory/generated settings are 8×8 views with 64-pixel cells; the source-library tool retains its distinct route. |

The scatter correction cites `Assets/Scripts/Planet/Scatter/ScatterImpostorFactory.cs`,
`Assets/Editor/GeneratedImpostorBakeTool.cs`, and `Assets/Editor/ScatterImpostorBakeTool.cs`.
It does not claim a fresh bake, equal route behavior, or visual approval.

## Validation

- All 22 source skills pass skill-creator `quick_validate.py` with UTF-8 enabled.
- All 22 discovery frontmatter blocks match their source skills.
- Every skill has one routing row, one inventory row, and a provenance section.
- No unresolved active local Markdown links remain in the checked skill library.
- Two dated template-link examples remain placeholders by design.
- Explicit source paths in the three new skills resolve.
- `git diff --check` passes for the maintenance scope.

Validation initially found malformed YAML and a Windows default-encoding failure.
The descriptions were repaired; the validator ran successfully with UTF-8 and temporary PyYAML support.

Routing walkthroughs covered generated atlas defects, interrupted creature behavior, and stale skill claims.
These route to the three new skills, then existing validation or policy owners as needed.
This was a manual routing review, not an independent agent or Unity execution test.

## Remaining limits

- Historical shader formulas, counter layouts, capture retention, command inventories, and campaign completion were not re-proven in this pass.
- Historical benchmark results and visual verdicts retain their original dates and scope.
- The asset catalog remains a historical survey with a current folder snapshot, not a full current asset review.
- Source-library and generated impostor settings differ. Source inspection alone does not establish whether further code changes are needed.
- No builds, EditMode tests, live Editor operations, atlas bakes, or runtime captures were run for these documentation changes.
- No product code changed, so the required code-change Graphify update did not apply.

Future maintenance should load the relevant skill and verify its owning source before treating a dated claim as current behavior.
