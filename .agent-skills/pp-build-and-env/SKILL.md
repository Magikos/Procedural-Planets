---
name: pp-build-and-env
description: Use when setting up the ProceduralPlanets working environment from scratch, cloning to a new machine, choosing the Unity version to install, running dotnet builds of the csproj files, hitting a build error that looks like a locked/shared intermediate DLL, hitting stale csproj/sln errors that reference source files which no longer exist (or Hot Reload saying "File is not part of any project"), asking what a third-party folder is or whether it can be touched, setting up or refreshing graphify, or deciding which dirs are generated and off-limits. Not for launching play mode or using the console — see pp-run-and-operate.
---

# Build and environment: recreate the working setup from scratch

This skill gets a zero-context engineer from `git clone` to a working editor + code-health build, and catalogs the environment traps that have already burned time. Facts verified 2026-07-06 on branch `code-refactor` at commit `ec0b1cd`.

## When NOT to use this

- Launching play mode, opening/using the debug console, capture output conventions → **pp-run-and-operate**
- What counts as proof that a change works → **pp-validation-and-evidence**
- Measuring (debug modes, counters, frame timing, graphify queries as a diagnostic) → **pp-diagnostics-and-tooling**
- Whether you're allowed to make a change at all → **pp-change-control**

## Versions and owning sources

Editor and manifest values rechecked 2026-09-09. Resolve machine-local tools before use.

| Thing | Version | Source of truth |
|---|---|---|
| Unity Editor | **6000.7.0a5** (revision `a15235a53881`) — an **ALPHA** build | `ProjectSettings/ProjectVersion.txt` |
| URP + Shader Graph | 17.7.0 | `Packages/manifest.json` |
| Input System | 1.19.0 | `Packages/manifest.json` |
| dotnet SDK | Resolve the installed SDK | `dotnet --version` |
| graphify CLI | Resolve the installed CLI and interpreter | `graphify --version` |

**The alpha version matters.** Use the Editor version and revision recorded in `ProjectSettings/ProjectVersion.txt`. Check availability before selecting an install; do not reuse an old alpha download link. Opening with another version can change import behavior.

Other notes:
- The project uses NUnit EditMode tests in `Assets/Tests/EditMode` (verified 2026-09-09). Use the existing test framework for focused regressions; no framework installation is needed.
- HotReload lives as an *embedded package* at `Packages/com.singularitygroup.hotreload/` (tracked in git, auto-included because it sits in `Packages/` with a `package.json` — it is deliberately absent from `manifest.json` dependencies).

## From-scratch checklist

1. `git clone` → checkout `code-refactor` (the active branch; `main` is the PR target). A **dirty working tree is normal and sacred** on Bryan's machine — active work lives uncommitted. Never reset/clean it.
2. Install **exactly Unity 6000.7.0a5** through Unity Hub (see alpha note above). Include the Windows build support module you need; the project targets standalone Windows.
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
| `dotnet build Assembly-CSharp.csproj` | **No longer exists.** | Every script now lives in an asmdef, so Unity stops generating it. See "csproj files are disposable" below. |

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

### Removed from disk — ✅ their stale csprojs were deleted 2026-08-12

These Asset Store assets were removed from `Assets/` and **no project code references any of them**: QFSW Quantum Console (superseded by `Assets/Scripts/Core/Console/`), **Shapes** (vector drawing), GrassFlow, Stylized Grass Shader, AssetInventory and its bundled sub-assemblies, ImpossibleRobert common, Package2Folder.

Their orphaned `.csproj` files lingered at repo root for months because **Unity regenerates csprojs but never deletes orphans**. They have now been deleted and the projects regenerated.

### csproj files are disposable — delete them when they misbehave

`*.csproj` and `*.sln` are **gitignored and untracked**. They are IDE artifacts only: Unity compiles from asmdefs and ignores them entirely. So a stale csproj can never break a Unity build — but it *will* break `dotnet build`, IDE navigation, and Hot Reload.

**Symptoms of staleness:** `dotnet build` failing on files that do not exist; Hot Reload logging `File is not part of any project` for newly created scripts; an IDE showing assemblies for packages that were removed long ago.

**Fix, and it is safe:**

```powershell
Remove-Item *.csproj, *.sln -Force      # gitignored, untracked, regenerated
```

then in Unity invoke `UnityEditor.SyncVS.SyncSolution()` (or just double-click any script in the Project window).

**Result on 2026-08-12: 39 csproj → 6, with zero missing file references** (previously 33 of 39 referenced deleted sources, including `Assembly-CSharp.csproj` at 8/8 missing).

The six legitimate projects are `ProceduralPlanets.{Core,Editor,Planet,Sampling,Tests.EditMode}` and `Wingman`.

⚠️ **`Assembly-CSharp.csproj` no longer regenerates, and that is correct** — Unity only emits it for scripts outside an asmdef, and there are none. Any historical note calling it "permanently broken because of missing Shapes sources" was describing a stale artifact, **not a missing dependency. Shapes is not needed and must not be imported to 'fix' it.**

⚠️ `SyncSolution()` regenerates the csprojs but did **not** restore the `.sln`. Double-click a script from Unity, or open the folder directly in Rider.

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
2. **~~`Assembly-CSharp.csproj` is permanently broken~~ — RESOLVED 2026-08-12.** The old story: it references deleted `Assets/Plugins/Shapes/...` sources and had been failing since 2026-05-21. The actual cause was a **stale generated artifact**, not a missing dependency — 33 of 39 csprojs referenced deleted sources. Deleting `*.csproj`/`*.sln` (gitignored, untracked) and regenerating produced 6 clean projects with zero missing references, and `Assembly-CSharp.csproj` correctly no longer exists because every script lives in an asmdef. **Do not import Shapes to "fix" this.** See "csproj files are disposable" above.
3. **Wrong Unity version** — anything other than 6000.7.0a5 triggers reimport and unreviewed behavioral drift. The alpha is a deliberate pin; don't upgrade unilaterally.
4. **"It compiles" declared as "it works"** — the costliest historical failures (water artifact saga, grass-blanket fight) involved changes that compiled fine and looked wrong. Compile is step zero; see **pp-validation-and-evidence**.
5. **Editing generated csprojs or third-party dirs** — Unity overwrites the former; the latter are not ours (and the caustics don't-touch rule in CLAUDE.md is the precedent for how badly "harmless" touches go).
6. **Treating the dirty working tree as mess to clean** — uncommitted changes on `code-refactor` ARE the active work. No `git reset`, `git clean`, `git checkout --` without Bryan's explicit instruction.
7. **graphify query/update previously hung in this checkout** — prior G19, now F05 in
   `docs/audit/2026-07-22-consolidated-code-audit.md`, records the generated-content
   ingestion. It completed again on 2026-07-09. Keep timeouts on graphify commands; on
   hang, fall back to `rg`/`rg --files` without burning session time.

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
| csprojs are clean (no missing sources) | `ls *.csproj` (expect 6) — then for each, check every `<Compile Include="Assets\...">` path exists |
| csproj/sln are untracked | `git ls-files "*.csproj" "*.sln"` (expect empty) |
| Console open key | `grep -n "OpenConsole" Assets/Scripts/Core/Services/InputMapService.cs` |
| Generation timings line | `rg -n "Generation timings" Assets/Scripts` |
| graphify version / freshness | `graphify --version`; `head -15 graphify-out/GRAPH_REPORT.md` vs `git rev-parse HEAD` |
| local-only ignored | `git check-ignore -v local-only` |
| C# file count | `git ls-files "Assets/Scripts/*.cs" \| wc -l` |
