---
name: pp-skill-maintenance
description: Use when asked to maintain, reconcile, or clean the project skill library, or update guidance after a subsystem change. Check stale claims, routing, duplication, references, templates, and discovery stubs against current evidence. Does not authorize source-code fixes or changing user policy.
---

# Project skill maintenance

Repair guidance using current evidence. Keep historical findings separate from instructions for today's work.
The source library is `.agent-skills/`; `.claude/skills/pp-*` entries are generated discovery stubs.
Use skill-creator guidance for structural edits and `pp-docs-and-memory` for records or memory changes.

## Scope the pass

For a library-wide request, inspect every skill's frontmatter, routing entry, local links, and provenance section.
Search bodies and supporting files for contradictions, stale commands, paths, versions, counts, and duplicated policy.
Read complete relevant sections before editing. A search hit alone is not enough evidence to change a rule.
For a subsystem request, inspect its matching skills and callers rather than loading the entire library.
Record the existing dirty state. Preserve unrelated edits even when they share a skill file.

## Verify before editing

| Claim | Verification source |
|---|---|
| User policy or approval | Current explicit instructions; existing policy source and dated decisions |
| Editor/package version | `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`, lock file when needed |
| API, symbol, command, default | Current owning code; console authoring contract for command changes |
| Test availability | Current test files and assembly configuration; distinguish presence from a passing test run |
| Paths and catalog coverage | Filesystem existence, scoped inventory, and recorded scan date |
| Runtime or visual success | The actual recorded run and artifacts; current source alone cannot re-prove an old observation |
| Historical rejection | Dated evidence and subsequent corrections; retain the original event when superseded |

Do not convert an old implementation detail into permanent policy.
Do not infer permission changes from code. When policy sources conflict, preserve the conflict and identify the newer evidence.
Avoid destructive provenance commands, full builds, bakes, or live Editor mutations merely to refresh documentation.
An unavailable source makes its claim unverified; it does not make the claim false.

## Repair

1. Fix verified errors in the source skill or reference.
2. Replace duplicated volatile facts with a pointer to the owning source or skill.
3. Put detailed procedures behind a relevant link; keep the entrypoint focused on routing and constraints.
4. Mark superseded history with a dated correction instead of deleting the investigation record.
5. Update README routing when activation scope changes. Keep one inventory entry per skill.
6. Regenerate discovery stubs from source frontmatter; do not hand-edit their descriptions independently.

Do not stamp an entire document "verified today" after checking only one section.
Do not broaden cleanup into runtime code changes. Report code inconsistencies with both source locations.
Preserve required validation, error handling, approval rules, and unresolved limitations while reducing duplication.
Existing user authorization persists; maintenance does not introduce another approval round for already approved edits.

## Validate and report

Run skill-creator `scripts/quick_validate.py` for each skill, using available PyYAML support.
Check generated frontmatter against source and verify local reference targets.
Interpret paths using each document's stated base; distinguish examples, historical paths, and missing active references.
Check inventory count and duplicate names. Review the diff for unintended policy or scope changes.
Walk representative requests through revised routing to confirm the correct skill and reference would load.

Report checked scope, verified repairs, remaining unsupported claims, and skipped live validation.
For a broad pass, write a dated maintenance record under `docs/research/` with evidence paths and unresolved items.
Do not claim a complete factual audit when the pass checked structure and selected current-state claims only.

## Provenance and maintenance

Created 2026-09-09 from the project's source/stub rules, shared-memory precedence, and demonstrated library drift.
Reverify library membership with `Get-ChildItem .agent-skills -Directory` and README routing.
Reverify current rules in `AGENTS.md` and `.agent-memory/MEMORY.md` before a broad pass.
