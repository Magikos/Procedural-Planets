# ProceduralPlanets Skill Library

Twenty-three skills for working on this repo at senior standard with zero prior context.
Each skill is self-contained: `<name>/SKILL.md`, frontmatter description = when to load
it, body = the runbook. This page is a router only — every fact lives in a skill.

## Conventions (read once)

1. **Dates are load-bearing.** Facts stamped "as of 2026-07-06" can drift. Every skill
   ends with a "Provenance and maintenance" section of one-line re-verification commands —
   run them before relying on a stamped fact.
2. **The tree wins.** On any conflict between a skill and the working tree, code, or
   `docs/` — the repo is right; the skill is stale. Fix the skill, don't trust it.
3. **The dirty worktree is normal and sacred.** Check the current branch with `git rev-parse --abbrev-ref HEAD`.
   Never discard, stash over, or "clean up" changes you didn't make.
4. **This directory is the single source.** `.claude/skills/pp-*` are generated discovery
   stubs (Claude Code) and AGENTS.md routes Codex here. Edit skills only in this
   directory; if a skill's frontmatter description changes, regenerate its stub to match.

## Broad onboarding only

For requested project-wide onboarding, read in this order: `pp-architecture-contract` → `pp-build-and-env` →
`pp-run-and-operate` → `pp-change-control`. Then load others on demand via the routing
table below.

For focused tasks, load only the skills matched by the routing table.
A new session alone does not require the onboarding sequence.
Read each required file once per session unless it changes or missing context requires another read.

## Routing: symptom / task → skill

| You are here | Load |
|---|---|
| Scatter prototypes, LOD transitions, impostor baking, or distant prop defects | pp-scatter-and-impostors |
| Creature residency, behavior, movement, rigs, clips, or survival scenarios | pp-creature-and-animation |
| Animation polish, unnatural motion, contact errors, or rendered humanoid/creature motion review | pp-animation-quality-review |
| Maintain skills, reconcile stale guidance, or verify discovery and reference links | pp-skill-maintenance |
| Find assets in the scratch project, search owned packs, refresh catalog coverage | pp-asset-catalog |
| Import or adopt selected models, clips, textures, audio, or generated assets | pp-asset-integration |
| Visual artifact, "X looks wrong", triage needed | pp-debugging-playbook |
| "Was this tried before?", about to retry an old idea, revert history | pp-failure-archaeology |
| Add/change a setting, quality tier, feature flag; "what's the current flag state?" | pp-settings-and-flags |
| Is this change allowed? Visual tuning, commit/review gates, Unity asset or serialization migration | pp-change-control |
| Is it done/proven? Evidence, before/after protocol, record or replay a validation scenario | pp-validation-and-evidence |
| Measure something: debug modes, capture sets, counters, frame timing | pp-diagnostics-and-tooling |
| Run the game, console commands, keybindings, where captures land | pp-run-and-operate |
| Clone/build/environment broken, Unity/dotnet/graphify setup, build traps | pp-build-and-env |
| Where does code live? What breaks if I change X? Is this refactor safe? | pp-architecture-contract |
| How does rendering X work (clouds/grass/water/atmosphere/cube-sphere)? | pp-gpu-rendering-reference |
| Weather grid channels, evolution loop, sim→visual coupling contract | pp-weather-sim-reference |
| Driving/resuming the live cloud+grass visual migration | pp-visual-migration-campaign |
| About to guess instead of derive: stage ownership, GPU cost, formula parity | pp-proof-and-analysis-toolkit |
| Turning a hunch into an accepted (or retired) result | pp-research-methodology |
| What to build next; roadmap toward the full-planet 3rd-person vision | pp-research-frontier |
| Writing a design doc/audit/research note; updating .agent-memory | pp-docs-and-memory |
| Audit code; validate/consolidate prior findings; propose behavior-preserving refactors | pp-code-audit |

## Skill inventory

| Skill | One-line scope |
|---|---|
| pp-scatter-and-impostors | Current source routes, bake prerequisites, prototype identity, transitions, and planet validation |
| pp-creature-and-animation | Simulation/presentation ownership, rigs, behavior transitions, persistence, and scenario validation |
| pp-animation-quality-review | Authored-source comparisons, complete motion review, contact measurements, and animation evidence |
| pp-skill-maintenance | Verify and repair routing, stale guidance, duplication, references, and discovery stubs |
| pp-asset-catalog | Search existing catalogs and scratch assets; verify candidates, reuse status, and survey coverage |
| pp-asset-integration | Select dependencies, preserve references, adapt assets, and verify adoption in Unity |
| pp-change-control | How changes are classified, gated, reviewed; non-negotiables with rationale and the incident behind each; the visual-tuning gate |
| pp-debugging-playbook | Symptom→triage tables per domain; stage-ownership method; binary proof modes; the traps that cost real time |
| pp-failure-archaeology | The chronicle: 12 investigations as symptom→root cause→evidence→status, so settled battles stay settled |
| pp-architecture-contract | Load-bearing design decisions + why; invariants; known weak points; subsystem map |
| pp-gpu-rendering-reference | Rendering theory as implemented here (hub + cube-sphere/clouds/grass/water-atmosphere-precipitation sub-files) |
| pp-weather-sim-reference | Weather grid as single source of truth: channel contract, evolution, coupling law |
| pp-settings-and-flags | Every config axis: SO→DTO pairs, quality tiers, feature flags, add-a-setting checklist |
| pp-build-and-env | Recreate the environment from scratch; build commands; known traps; third-party inventory |
| pp-run-and-operate | Play mode, console anatomy, camera/capture workflow, artifact map, verified keybindings |
| pp-diagnostics-and-tooling | Measure instead of eyeball: debug-mode catalogs with interpretation, capture sets, counters; ships scripts/ |
| pp-validation-and-evidence | The evidence bar; before/after capture protocol; what "done" means here |
| pp-docs-and-memory | Docs-of-record conventions and templates; comment/logger doctrine; memory update rules |
| pp-visual-migration-campaign | Executable decision-gated campaign for the cloud+grass visual migration (cloud-phases.md, grass-phases.md) |
| pp-proof-and-analysis-toolkit | First-principles recipes with worked examples from this repo's history |
| pp-research-frontier | Open problems toward the vision: SOTA gap, our asset, first three steps, falsifiable milestone each |
| pp-research-methodology | Hunch→accepted result: predict numbers first, adversarial refutation, adopt-or-retire lifecycle |
| pp-code-audit | Findings-only whole-repo audit, prior-finding reconciliation, complexity reduction, and refactoring handoff |
