---
name: pp-build-and-env
description: Use when setting up the ProceduralPlanets working environment from scratch, cloning to a new machine, choosing the Unity version to install, running dotnet builds of the csproj files, hitting a build error that looks like a locked/shared intermediate DLL, wondering why Assembly-CSharp.csproj fails on missing Shapes files, asking what a third-party folder is or whether it can be touched, setting up or refreshing graphify, or deciding which dirs are generated and off-limits. Not for launching play mode or using the console — see pp-run-and-operate.
---

# Build and environment: recreate the working setup from scratch

This skill gets a zero-context engineer from `git clone` to a working editor + code-health build, and catalogs the environment traps that have already burned time. Facts verified 2026-07-06 on branch `code-refactor` at commit `ec0b1cd`.

## When NOT to use this

- Launching play mode, opening/using the debug console, capture output conventions → **pp-run-and-operate**
- What counts as proof that a change works → **pp-validation-and-evidence**
- Measuring (debug modes, counters, frame timing, graphify queries as a diagnostic) → **pp-diagnostics-and-tooling**
- Whether you're allowed to make a change at all → **pp-change-control**

## Exact versions (as of 2026-07-06)

| Thing | Version | Source of truth |
|---|---|---|
| Unity Editor | **6000.6.0a7** (revision `240d06e2411b`) — an **ALPHA** build | `ProjectSettings/ProjectVersion.txt` |
| URP + Shader Graph | 17.6.0 | `Packages/manifest.json` |
| Input System | 1.19.0 | `Packages/manifest.json` |
| dotnet SDK | 9.0.315 | `dotnet --version` |
| graphify CLI | 0.8.39 (Python, pip-installed, on PATH) | `graphify --version` |

**The alpha version matters.** Unity ties `Library/` serialization, package resolution, and script compilation to the exact editor version. Opening with a different 6000.x build forces a reimport and can silently change behavior; alpha builds also don't appear in Unity Hub's default install list — install via Hub's Archive/alpha channel or the deep link `unityhub://6000.6.0a7/240d06e2411b` (deep-link format is the standard Hub convention; the revision hash is verified from `ProjectVersion.txt`). UNVERIFIED: whether Unity still serves this alpha for download — if it's gone, escalate to Bryan before "upgrading" the project yourself; a version bump is a real change that needs his review.

Other notes:
- `com.unity.test-framework` 1.8.0 is in the manifest because Unity ships it by default. Project policy: **no test framework work** — don't propose tests (see CLAUDE.md).
- HotReload lives as an *embedded package* at `Packages/com.singularitygroup.hotreload/` (tracked in git, auto-included because it sits in `Packages/` with a `package.json` — it is deliberately absent from `manifest.json` dependencies).

## From-scratch checklist

1. `git clone` → checkout `code-refactor` (the active branch; `main` is the PR target). A **dirty working tree is normal and sacred** on Bryan's machine — active work lives uncommitted. Never reset/clean it.
2. Install **exactly Unity 6000.6.0a7** through Unity Hub (see alpha note above). Include the Windows build support module you need; the project targets standalone Windows.
3. Install dotnet SDK 9.x (`dotnet --version` should report 9.0.3xx).
4. Open the project folder in Unity Hub. **First import is long** (full `Library/` build: shader compilation, texture import — expect many minutes). Do not kill the editor mid-import.
5. In the editor, open `Assets/Scenes/Planet.unity` (the main scene; `Assets/Scenes/Tests/` holds `Clouds.unity`, `Grass.unity`, `Water.unity` isolation scenes).
6. Enter play mode and verify the debug console opens with the backquote key (`` ` `` — binding defined at `Assets/Scripts/Core/Services/InputMapService.cs:123`). Console anatomy and everything past this point → **pp-run-and-operate**.
7. Let Unity regenerate the `.csproj`/`.sln` files (they are gitignored; Unity writes them on first script compile). Until it does, `dotnet build` targets may be stale or missing.
8. Optional but expected: install graphify (see graphify section) and confirm `graphify-out/graph.json` freshness.

## Code-health builds (dotnet)

These are **documented convention**, not run as part of authoring this skill — `docs/design/2026-07-04-cloud-visual-migration-plan.md` prescribes them as the C#-side compile check ("Compile check via Unity import (C# side: `dotnet build ProceduralPlanets.Planet.csproj`)").

| Command | What it checks | Notes |
|---|---|---|
| `dotnet build ProceduralPlanets.Planet.csproj` | Planet assembly (bulk of gameplay/rendering code) | Primary code-health check |
| `dotnet build ProceduralPlanets.Core.csproj` | Core assembly (console, services, boot) | Run when Core files touched |
| `dotnet build ProceduralPlanets.Sampling.csproj` / `.Editor.csproj` | Sampling / editor assemblies | Rarely needed |
| `dotnet build Assembly-CSharp.csproj` | **Do not use.** | Known-broken: references removed `Assets/Plugins/Shapes/...` sources (see third-party section) |

Append `--no-restore` for speed on repeat builds (established usage in the repo's history).

### TRAP: serial-only builds

Building Core and Planet **in parallel collides on a shared intermediate DLL** (file-write lock on the shared obj output). This is a known, repeatedly-hit failure recorded in the repo's committed memory (`.agent-memory/codex/MEMORY.md` and 2026-05-21 ad-hoc notes: "A parallel build attempt hit the known shared intermediate DLL write collision, but the serial rerun passed").

Rule: **build one csproj at a time. If a build fails with a file-lock/write error on an intermediate DLL, rerun serially before calling it a real regression.**

### Build success is not proof

`dotnet build` passing means the C# compiles — nothing more. Unity import + play mode + capture evidence decide whether a change actually works; see **pp-validation-and-evidence**.

## Assembly layout (asmdefs)

An *asmdef* (`.asmdef`) is Unity's assembly-definition file; each one becomes its own compiled assembly and generated `.csproj`. All project asmdefs, verified 2026-07-06:

| Assembly | Path | References (dependency direction) |
|---|---|---|
| `ProceduralPlanets.Core` | `Assets/Scripts/Core/` | Unity.InputSystem only (bottom of the stack; `allowUnsafeCode: true`) |
| `ProceduralPlanets.Planet` | `Assets/Scripts/Planet/` | → Core, URP runtime, RP Core runtime, InputSystem, Burst, Mathematics, Collections (`allowUnsafeCode: true`) |
| `ProceduralPlanets.Sampling` | `Assets/Scripts/` (root, covers `PoissonDisc*.cs`) | → Core, Planet |
| `ProceduralPlanets.Editor` | `Assets/Editor/` | → Core, Planet; `includePlatforms: ["Editor"]` |
| `Wingman` | `Assets/Plugins/Wingman/` | none (third-party, editor-only via `#if UNITY_EDITOR`) |

Dependency direction: **Core ← Planet ← {Sampling, Editor}**. Core must never reference Planet. Scale: 261 tracked C# files under `Assets/Scripts` (verify: `git ls-files "Assets/Scripts/*.cs" | wc -l`).

## Third-party inventory

Third-party directories are **not ours to refactor** — no project rules (comments, ILogger, DTOs) apply inside them; leave them byte-identical.

### Present on disk (as of 2026-07-06)

| Asset | Path | Role | Touch policy |
|---|---|---|---|
| Wingman | `Assets/Plugins/Wingman/` (git-tracked) | Editor-only inspector utility (clipboard/inspector tooling, all code under `#if UNITY_EDITOR`) | Don't edit. Excluded from graphify via `.graphifyignore` |
| Hot Reload (Singularity Group) | `Packages/com.singularitygroup.hotreload/` (git-tracked embedded package) | Live C# patching in the editor — "change code and get immediate updates" | Don't edit. Excluded from graphify. Dormant convenience; nothing in project code depends on it |

### Removed from disk — only stale generated csprojs remain

These Asset Store assets were removed from `Assets/` (never git-tracked; QFSW's csproj last regenerated 2026-06-03, GrassFlow's 2026-06-02). Their `.csproj` files still sit at repo root because Unity regenerates csprojs but never deletes orphans. **The source folders do not exist; no project code references them** (verified: zero hits for `QFSW|GrassFlow|StylizedGrass` in `Assets/Scripts`).

| Stale csproj(s) | Was | Status |
|---|---|---|
| `QFSW.QC.*.csproj` (12 files) | QFSW Quantum Console | Removed; the project now ships its own console (`Assets/Scripts/Core/Console/`) |
| `ShapesRuntime/ShapesEditor/ShapesSamples.csproj` | Shapes (vector drawing) | Removed; **cause of the `Assembly-CSharp.csproj` build failure** |
| `GrassFlow.csproj`, `GrassFlowEditor.csproj` | GrassFlow (GPU grass) | Removed; project grass is custom (compute-based) |
| `sc.stylizedgrass.*.csproj` | Stylized Grass Shader | Removed |
| `AssetInventory.*.csproj` + `AudioTool.*`, `Brain.*`, `Automator.*`, `Database.*` (all point into `Assets/AssetInventory/Reuse/...`) | AssetInventory editor tool + its bundled sub-assemblies | Removed |
| `ImpossibleRobert.Common*.csproj` | ImpossibleRobert common lib (AssetInventory dependency) | Removed |
| `CodeStage.Package2Folder.csproj` | Package2Folder (bundled inside AssetInventory ThirdParty) | Removed |

Do not "clean up" the stale csprojs as drive-by work — they're gitignored local files on Bryan's machine; deleting them is harmless in principle but is his call.

## graphify

graphify is a pip-installed Python CLI (v0.8.39) that maintains a knowledge graph of the codebase at `graphify-out/`.

- Layout: `graphify-out/{graph.json, GRAPH_REPORT.md, manifest.json, cost.json, cache/}` plus dated snapshot dirs (`2026-06-14/` … `2026-07-02/`) holding historical graphs. No `wiki/` exists as of 2026-07-06.
- Use: `graphify query "<question>"` first for any codebase question; `graphify path "<A>" "<B>"` for relationships; `graphify explain "<concept>"` for one concept. (`AGENTS.md`/`CLAUDE.md` mandate query-first when `graphify-out/graph.json` exists.)
- **After any code edit: `graphify update .`** — AST-only, no API cost.
- Historical trap: `graphify query`/`update` hung on 2026-07-06 after the graph ingested `Library/PackageCache` and `local-only/` (audit G19). It completed again on 2026-07-09; still run graphify commands with a timeout, and on hang fall back to `rg`/`rg --files` and note the skip. See Known traps #7.
- Freshness check: `GRAPH_REPORT.md` header states "Built from commit: `ec0b1cd2`"; compare against `git rev-parse HEAD`. Stale graph → `graphify update .`.
- Fresh machine: `pip install graphify` then `graphify install --platform claude` copies the skill into the platform config dir (subcommand verified via `graphify --help`; exact pip package name UNVERIFIED — confirm with `pip show graphify` on the source machine if install fails).
- `.graphifyignore` (repo root) excludes `Assets/Plugins/Wingman/`, `Packages/com.singularitygroup.hotreload/`, `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, `local-only/`, and `graphify-out/` itself. Keep third-party and generated content out of the graph.

## Generated / untracked directories — what NOT to touch

| Path | What it is | Policy |
|---|---|---|
| `Library/`, `Temp/`, `obj/`, `Logs/`, `UserSettings/`, `.utmp/` | Unity-generated caches/state (gitignored) | Never edit, never commit; safe to let Unity rebuild but deleting `Library/` costs a full reimport |
| `*.csproj`, `*.sln`, `*.slnx`, `*.lscache` | Generated by Unity/IDE (gitignored) | Never hand-edit; Unity overwrites them |
| `local-only/` | Untracked reference material (external projects: `Clouds-master`, `FFT-Ocean-main`, AssetRipper exports, papers) **and capture output** at `local-only/debug-screenshots/` (F10 PNG + `.txt` sidecars) | Read-only reference; gitignored (`.gitignore` line: `local-only`). Capture conventions → **pp-run-and-operate** |
| `graphify-out/` | graphify output | Only touch via `graphify` commands; dirty files here are expected |
| `ProfilerCaptures/`, `extensions/`, `atmosphere_diagnostics.txt` | Local leftovers on Bryan's machine (untracked/empty or scratch) | Ignore |
| `docs/agent-conversation/*` | gitignored cross-agent scratchpad (dir tracked, contents ignored) | Writable scratch per **pp-docs-and-memory** |

## .agent-memory reading order

`.agent-memory/` is the committed cross-agent memory. Read in this order:

1. `.agent-memory/MEMORY.md` — canonical index + precedence rules (explicit instructions > current code/docs > shared memory > agent-specific history).
2. `.agent-memory/claude/MEMORY.md` — Claude's topic index (refactor arc, console arc, chunk seams, …).
3. `.agent-memory/codex/memory_summary.md` + `.agent-memory/codex/MEMORY.md` — Codex history (water saga, cloud seams, evidence-led debugging).

Memory can be stale; revalidate dates/branches before acting on it. It is background, not a source you cite as load-bearing.

## Logs

- Unity editor log: `%LOCALAPPDATA%\Unity\Editor\Editor.log` (verified present on this machine, alongside `Editor-prev.log` for the previous session).
- The generation-timing line to grep for after a planet build: `"Generation timings: initialize=...ms, terrain=...ms, ..."` — emitted from `Assets/Scripts/Planet/Planet.cs:333`. It's the standard evidence line for startup/generation perf claims.

## Known traps (with the stories)

1. **Parallel dotnet builds** — Core + Planet built simultaneously lock the same intermediate DLL and fail. Happened repeatedly during the 2026-05 water-artifact work; every time, the serial rerun passed. Always rerun serially before reporting a compile regression.
2. **`Assembly-CSharp.csproj` is permanently broken** — it still references deleted `Assets/Plugins/Shapes/...` sources. This has been failing since at least 2026-05-21 and is not a regression. Build the four `ProceduralPlanets.*` csprojs instead.
3. **Wrong Unity version** — anything other than 6000.6.0a7 triggers reimport and unreviewed behavioral drift. The alpha is a deliberate pin; don't upgrade unilaterally.
4. **"It compiles" declared as "it works"** — the costliest historical failures (water artifact saga, grass-blanket fight) involved changes that compiled fine and looked wrong. Compile is step zero; see **pp-validation-and-evidence**.
5. **Editing generated csprojs or third-party dirs** — Unity overwrites the former; the latter are not ours (and the caustics don't-touch rule in CLAUDE.md is the precedent for how badly "harmless" touches go).
6. **Treating the dirty working tree as mess to clean** — uncommitted changes on `code-refactor` ARE the active work. No `git reset`, `git clean`, `git checkout --` without Bryan's explicit instruction.
7. **graphify query/update previously hung in this checkout** — audit G19 (`docs/audit/2026-07-03-general-code-audit.md:357-359`) called it "an operational blocker" on 2026-07-06 after the graph ingested `Library/` and `local-only/`. It completed again on 2026-07-09. Keep timeouts on graphify commands; on hang, fall back to `rg`/`rg --files` without burning session time.

## Provenance and maintenance

Facts above verified 2026-07-06 against commit `ec0b1cd`. Re-verify with:

| Claim | Command |
|---|---|
| Unity version + revision | `cat ProjectSettings/ProjectVersion.txt` |
| URP / package versions | `cat Packages/manifest.json` |
| dotnet SDK | `dotnet --version` |
| asmdef set + references | `rg --files --iglob "*.asmdef" Assets Packages` then `cat` each |
| Build convention (Planet csproj) | `grep -n "dotnet build" docs/design/2026-07-04-cloud-visual-migration-plan.md` |
| Serial-trap record | `rg -n "intermediate DLL" .agent-memory/codex/MEMORY.md` |
| Third-party dirs absent | `ls Assets/Plugins; ls Assets` (only Wingman under Plugins) |
| Stale csproj → missing sources | `grep -o 'Compile Include="[^"]*"' ShapesRuntime.csproj \| head -3` then `ls "Assets/Plugins/Shapes"` (fails) |
| Console open key | `grep -n "OpenConsole" Assets/Scripts/Core/Services/InputMapService.cs` |
| Generation timings line | `rg -n "Generation timings" Assets/Scripts` |
| graphify version / freshness | `graphify --version`; `head -15 graphify-out/GRAPH_REPORT.md` vs `git rev-parse HEAD` |
| local-only ignored | `git check-ignore -v local-only` |
| C# file count | `git ls-files "Assets/Scripts/*.cs" \| wc -l` |
