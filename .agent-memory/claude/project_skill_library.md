---
name: project-skill-library
description: "The .agent-skills/ library (16 skills + README router) built 2026-07-06 — what it is, how it was validated, and the CLAUDE.md drift it surfaced"
metadata: 
  node_type: memory
  type: project
  originSessionId: 5a0ee82f-d367-47b6-bbee-397761463f85
---

On 2026-07-06 a 16-skill library was authored at `.agent-skills/` (28 files: 16 SKILL.md,
README.md router, 4 gpu-rendering sub-files, 2 campaign phase files, 3 doc templates,
3 PowerShell diagnostic scripts). Audience: zero-context mid-level engineers and
Sonnet-class agents. Built by 16 parallel author agents + 3 reviewers
(factual/doctrine/usability) + fixer; all blocking and important findings applied.
Every console command, capture-set name, and "as of 2026-07-06" state claim was
verified against the tree; ~120 file:line citations spot-checked nearly all exact.

Start at [[.agent-skills/README.md]] (routing table). Each skill ends with a
"Provenance and maintenance" section of re-verification greps — run them before
trusting date-stamped facts; the tree wins on conflict.

CLAUDE.md drift surfaced by the build (skills state drift honestly; CLAUDE.md not edited):
init dependency graph HAS landed (`InitGraph<T>`, Kahn's, used by LoadingManager) though
CLAUDE.md calls it in-progress; `DependencyManager.WhenReady` does not exist anywhere;
the `CloudSettings` four-way split never landed; `ShaderGlobalIds` has 9 partials (Biome
extra); CLAUDE.md links deleted `docs/audit/2026-06-code-refactor/`. Also: QFSW/Shapes/
GrassFlow/StylizedGrass asset folders are gone from disk (stale root csprojs only);
graphify query/update hang in this checkout (audit G19 — skills say use a timeout).

Bryan's previously-unwritten visual-tuning rule is now codified in pp-change-control:
no visual-constant tuning without a before/after capture diff; never retune values
Bryan hand-picked; Bryan's eyes lock a look. Bryan's stated vision (recorded in
pp-research-frontier): full-planet 3rd-person experience — build structures, modify
terrain, eventually fly through the clouds; visual experience is the primary bar.

Auto-discovery (added same day, Bryan-requested): `.claude/skills/pp-*` holds generated
discovery stubs (same frontmatter, body points at source) so Claude Code lists the skills
natively; AGENTS.md has a "Project skill library" section routing Codex; CLAUDE.md has a
"Skill library" section. Stubs are generated — edit only `.agent-skills/`; regenerate a
stub if its source frontmatter changes (generator: awk frontmatter copy + pointer body).

**Why:** future sessions should load skills from `.agent-skills/` for project work
instead of re-deriving context, and should keep the library in sync when facts drift.
**How to apply:** route via README table; after changing code that a skill date-stamps,
run that skill's provenance greps and update the skill.
