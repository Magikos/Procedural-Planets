# Project rules

This file captures the agreed-upon rules for the ProceduralPlanets codebase. It is loaded into every Claude session. Rules are operative — read them as "do this / don't do that," not as suggestions. If a rule conflicts with what you find in code, the rule wins (the code is drift to be corrected, not a counter-example).

The 2026-06-10 audit lives at [docs/audit/2026-06-code-refactor/](docs/audit/2026-06-code-refactor/) and is the current source of refactor findings. The active perf-maintainability plan is [docs/design/2026-06-08-performance-maintainability-plan.md](docs/design/2026-06-08-performance-maintainability-plan.md).

---

## Architecture

### Settings: SO authoring, DTO runtime

- `ScriptableObject` settings are **editor-only authoring surfaces**. Runtime never reads an SO directly outside boot.
- Runtime consumers read immutable **snapshot DTOs** (records or readonly structs).
- DTOs live next to the SO they snapshot, with a static `From(SO)` factory.
- Each active `WorldContext` owns one domain-agnostic `ISettingsService`. `SettingsProvider` resolves that active world registry and never creates a fallback.
- World setting owners implement `IWorldSettingsRegistrar`. `SceneBootstrap` builds and validates every required DTO, then freezes registration before other world initializers run.
- Consumers resolve and cache DTOs with `SettingsProvider.GetSettings<TDto>()`; runtime changes use `Update<TDto>`.
- Settings changes raise `EventBus<SettingsChangedEvent>`. Consumers re-fetch on receipt.
- No console command mutates the SO asset. Console-command setters update the runtime DTO via the service.
- `Material` assets are cloned on first use. Runtime never writes shader properties or keywords on an SO-referenced material asset.

### SOs are narrow

- One SO per domain concern. Break god-SOs into many targeted SOs so DTOs can compose only what they need.
- Example: `CloudSettings` → `CloudRenderSettings`, `CloudEvolutionSettings`, `WeatherGridSeedSettings`, `RainFormationSettings`. Each DTO composes the slice its consumer needs.

### Services over MonoBehaviours

- Default to a **plain class service**. Use `MonoBehaviour` only when you need a Unity message (`OnTriggerEnter`, `OnDrawGizmos`, `OnValidate`, `[ContextMenu]`, custom inspector, `OnBecameVisible`, etc.).
- One **orchestrator MonoBehaviour** drives many services by forwarding `Update`/`FixedUpdate`/`LateUpdate` to them.
- The orchestrator also owns deterministic disposal in reverse init order.
- Editor-only authoring affordances on SOs/MBs are legitimate and should not be stripped to satisfy "default to plain class."

### Boot path discipline

- All initialization goes through `IEarlyInitialize` / `ILateInitialize` driven by `LoadingManager`.
- **No `[DefaultExecutionOrder]`.** Ordering belongs in the init phase system.
- **No `RuntimeInitializeOnLoadMethod`** except `LoadingManager.CreateInstance` (sanctioned because the loading overlay must paint before any `IEarlyInitialize` runs). Self-test fixtures don't count.
- Dependency declaration over priority numbers: services declare `Type[] Dependencies`; a generic dependency-resolved init graph topologically orders them. *(In-progress design — until landed, declare deps with a `// Depends on: …` comment and pick a Priority that respects them.)*
- External observers (UI, debug, gameplay code outside the init graph) may use `DependencyManager.WhenReady<T>()`. Services *inside* the init graph declare deps formally — never `await WhenReady<T>()` as a substitute.

### ServiceLocator

- `ServiceLocator.Register<>` is application scope only.
- Scene and runtime-world services use `IWorldServiceRegistrar` or `RegisterWorld<>` and resolve from the active `WorldContext`.
- The active world context is replaced for each loaded or newly generated world. Never retain world services across `WorldReadyEvent`.
- Saved-world transitions enter through `WorldLoadRequest`; persistence code translates stable save keys into typed `WorldSettingsOverride<TDto>` values before loading.
- `TryGet<>` for optional services with downstream null-check.
- `Get<>` only when the service **must** exist (throws otherwise).
- Resolve once at init for hot-path consumers. Never `TryGet`/`Get` per frame.

### File size is a symptom

- Real rule: **when you're about to add a new responsibility to a class, split first.**
- ~400 lines is a guardrail. Don't split to hit a number; split to find cohesion. Existing oversized files are flagged in the audit.

### Console commands

- Commands stay on the service that owns the underlying state — `[ConsoleCommand]` / `[CommandPrefix]` attributes live on the service.
- Don't extract a `*Commands` companion class unless the command set genuinely outgrows the service.

### Debug surfaces own their domain

- Each subsystem owns its own debug module: `AtmosphereDebugModule` owns atmosphere globals, `BiomeDebugModule` owns biome metadata, etc.
- `WaterDebugModule` audits water only. `GrassDebugModule` audits grass only.
- `DebugCaptureController` orchestrates; it doesn't enumerate per-domain fields.

---

## Async, background, and compute

- **Awaitable only.** No coroutines (`IEnumerator`). No `async void`. No `Task.Run`.
- Wrap Unity's non-`Awaitable` async surfaces (`AsyncGPUReadback`, `Resources.LoadAsync`, `SceneManager.LoadSceneAsync`) with extensions that return `Awaitable<T>`.
- **Expensive one-shot work** (generation, IO, image encoding, mesh build) runs on a background thread via `Awaitable.BackgroundThreadAsync`.
- **Per-frame hot work** uses Burst jobs or compute shaders. Compute is the preferred tool when the work fits a kernel and the inputs/outputs are buffers — it's often the fastest answer even for complex math.
- Caveats to know, not rules: `ComputeShader.Dispatch` has ~50-100μs launch overhead (don't compute-shader trivial workloads). `AsyncGPUReadback` adds 1-2 frames of latency (don't use when a result is needed this frame).

---

## Per-frame discipline

- Any controller that uploads shader globals every frame uses the **dirty-flag pattern**: static-vs-dynamic split, dirty-marked on `OnPlanetGenerated` and on each console-command setter, only push when dirty.
- Atmosphere and clouds are the precedent. New controllers follow.
- `ShaderGlobalsController` runs in `LateUpdate` so publish phase is consistent across writers.

---

## Shader globals

- `ShaderGlobalIds` is the single source of truth for **every** shader-global string name. It is a `partial` class split one file per domain: `ShaderGlobalIds.Core.cs`, `.Atmosphere.cs`, `.Water.cs`, `.Cloud.cs`, `.Grass.cs`, `.Precipitation.cs`, `.Terrain.cs`, `.Celestial.cs`.
- **A global name is any name passed to `Shader.SetGlobal*` / `Shader.GetGlobal*`.** Every such name lives in `ShaderGlobalIds`; there are **no raw string literals** at a `Shader.PropertyToID(...)` whose ID is used as a global. A new global adds a `public const string X = "_X";` to its domain partial first.
- Each module still caches its own `static readonly int _xId = Shader.PropertyToID(ShaderGlobalIds.X)` locally — the hub owns *names*, not the cached IDs.
- This is **globals only.** Per-`Material` property names and compute-shader property names are shader-*scoped*, not globals; they stay as module-local consts/literals and do **not** belong in `ShaderGlobalIds` (a material property can't collide across shaders the way a global can).

---

## Code style

### Comments

- **Default to no comments.** Name things well; let the code speak.
- Write a comment only when the **WHY is non-obvious**: a hidden constraint, a subtle invariant, a non-obvious workaround for a specific bug.
- **Never** write change-history or course-correction commentary. No `// was X, now Y`, `// added for Z issue`, `// see PR #123`. That belongs in commit messages and rots in code.
- **Never** explain what the code does — well-named identifiers already do that.
- When you touch a file for another reason, prune existing change-history comments you encounter.

### Logger

- New code uses `ILogger` / `LoggerProvider`. Direct `UnityEngine.Debug.Log*` migrates as files are touched for other reasons.
- **`Warning` is reserved for "developer probably wants to fix this."** "Feature disabled, continuing silently" is `Info` (one-time) or a debug channel.

### Dead code

- Experiments are deleted at the same commit that supersedes them, or within one week.
- If genuinely parking an experiment, gate behind `#if PROJECT_X_EXPERIMENT` so it stops shipping. Document what's parked and why.
- Dead fields, unused enum values, `#if false` blocks, and unused DTOs are removed when discovered.

---

## Tests

- No test framework is being added near-term. Don't propose one.
- Editor-time self-tests that run via `RuntimeInitializeOnLoadMethod` are dead fixtures, not tests — delete them.

---

## Don't touch

- **Caustics** (`Assets/Graphics/Shaders/Ocean.shader` and related caustics code). They look correct; every touch breaks them. Audit findings against caustics are flag-only — no code changes.

---

## Audit workflow

- Audits are read-only and produce a findings doc. Bryan reviews findings and marks decisions (fix / defer / wontfix) before any code changes.
- Don't start fixing during an audit phase. Don't fix while findings are still under review.
- Cross-reference baseline audit findings instead of re-listing them.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
