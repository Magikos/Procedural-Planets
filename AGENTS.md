## Animation continuity

- All visible animation changes must blend smoothly by default. Treat unexplained pose snaps as defects.
- Before animation or procedural-pose changes, follow [the animation continuity rule](.agent-memory/animation-transitions.md).
- Instant visible changes require a specific, documented necessity. This includes interaction cancellation and target changes.

## Art assets: ours, not the vendor's

Assets are sourced and adapted to our systems. Nothing vendor-shaped enters this repo.

Rules:
- Never install a vendor package here to obtain an asset. Copy the file out of `D:/Unity/Explore Assets`. If it is not there, install the pack **in that scratch project**, then copy the file from it.
- Do not bring the vendor's directory structure, scripts, shaders, controllers, demos, or presets.
- The filename loses the vendor too. `SM_Chr_Monk_01.fbx` and `PolygonFantasyHero_Texture_01_A.png` are vendor names. Rename to the project convention `Thing_NN`.
- Carry the `.meta` with the file so the GUID survives and every serialized reference stays intact. Renaming a YAML asset rewrites its internal `m_Name` — the bytes change, the GUID does not.
- Layout is **domain-first**, not kind-first: a deer's mesh, texture, material, clips, and prefab belong together in `Art/Creatures/Deer`, because `Models/Creatures/Deer` holds one sixth of a deer. The top-level domains are `Audio`, `Characters`, `Creatures`, `Effects`, `Interactions`, `Materials`, `Props`, `Vegetation`.
- A folder under `Characters/` that names a role is named for the role, not for the pack that supplied it.
- Every set folder carries a `SOURCE.md` in our words: what was taken, from which pack and path, what was changed to fit our systems, and what was deliberately not imported. That file is the provenance record — not the `AssetOrigin` block Unity writes into a `.meta`.
- When moving existing assets, create the destination folders in a pass of their own **before** any move. `AssetDatabase.CreateFolder` is deferred inside `StartAssetEditing()` and auto-uniquifies.
- Verify afterwards that no GUID vanished. String-based consumers (`LoadAssetAtPath`, folder-scoped `FindAssets`, path consts) are not covered by that proof and must be edited separately.
- Load `.agent-skills/pp-asset-integration/SKILL.md` before asset import or adoption work.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, invoke the `skill` tool with `skill: "graphify"` before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

## Project skill library

The committed skill library root is `.agent-skills/`; its README lists the current skills.

Rules:
- Before non-trivial project work, read `.agent-skills/README.md` and load the skill(s) its routing table matches to your task. Read the matched `.agent-skills/<name>/SKILL.md` in full and follow it.
- Frontmatter `description` fields say when to load each skill; the README table routes by symptom/task.
- Each skill ends with a "Provenance and maintenance" section of re-verification commands. Date-stamped facts can drift — on any conflict, the working tree wins over the skill; update the skill.
- `.claude/skills/pp-*` are discovery stubs for Claude Code only. Never edit them; edit the source under `.agent-skills/`. If a source skill's frontmatter changes, regenerate its stub to match.

## Console commands

- Before adding or changing console commands, read `docs/design/console-command-authoring.md`.
- Use domain command adapters and the shared `CommandCatalog`.
- Preserve old command names as aliases when renaming commands.
- Declare a group and reviewed release policy. New commands default to development-only unless player access is explicitly reviewed.
- Return `ConsoleCommandResult.Fail` for expected rejection. Test rejection through `CommandExecutor`.
- Run `ConsoleRegressionTests` after command changes. Do not bypass `CommandContract` or `ConsoleCommandBuildValidator`.

## Shared project memory

The committed cross-agent memory root is `.agent-memory/`.

Rules:
- Before non-trivial project work, read `.agent-memory/MEMORY.md`.
- Search `.agent-memory/claude/MEMORY.md` and `.agent-memory/codex/memory_summary.md` when the shared index points to relevant historical detail.
- Treat explicit user instructions and current code or documentation as newer than memory. Revalidate stale or checkout-specific claims before acting on them.
- When Bryan explicitly asks Codex to remember or forget project information, update `.agent-memory/` rather than the user-level Codex memory store.
- Never add credentials, access tokens, private keys, or sensitive captures to committed memory.

## Context and tool output

- Load only task-relevant skills and linked memory topics. A new session does not require broad onboarding.
- Reuse files already read unless they changed or their contents are missing from context.
- Filter tool results before returning them: select relevant fields, counts, changes, and file locations.
- Save large logs outside tracked source. Return the exit code, summary, exact failures, and the full log path.
- Start shell output budgets at 2000 tokens. Increase them when required instructions or evidence need more space.
- Narrow searches by directory and file type. Use file lists or counts when full matching lines are unnecessary.
- Treat truncation as incomplete evidence. Retrieve missing sections before making a decision that depends on them.
