# Audit — Debug, Console & Core Services

**Date:** 2026-06-10
**Branch:** code-refactor (audited from working tree on `phase8-spawning-foundation`)
**Auditor:** Claude (subagent)
**Scope:** `Assets/Scripts/Core/Services/`, `Assets/Scripts/Core/Console/`, `Assets/Scripts/Core/Events/`, `Assets/Scripts/Core/Interfaces/`, `Assets/Scripts/Core/Data/`, `Assets/Scripts/Core/QualityController.cs`, scattered `[ConsoleCommand]`/`[CommandPrefix]` sites.
**Status:** Findings only — no code modified.

## Executive summary

The debug/console/services layer is in materially good shape. The recent `TryGet<>` cherry-pick lines up with how every other resolver in the layer already behaves — there is no `Get<>` misuse to clean up beyond the single `ILogger` boot-point. The console arc has shipped its CONSOLE-6 polish (compiled invoker cache, `CompletionRanker`, etc.) so prior reflection-cost and provider-duplication findings are closed.

The remaining weight is structural and concentrated in three places: a fan-out of cross-domain coupling on `DebugCaptureController` (capture orchestration knows about camera, planet, weather, console, file IO, GUI, registry, screenshot pruning); a soft boot-order contract where `LoadingManager` and `EventBusProcessor` each self-create via `RuntimeInitializeOnLoadMethod` (project rule 3), `EventBusProcessor` not even owned by `GameBootstrap`; and a hand-maintained `EnsureComponent<T>()` list in `GameBootstrap` that quietly accumulates new debug services. Several debug modules and `WaterWakeController` reach across domain boundaries (water debug knows shader IDs for biome/voronoi/terrain/freeze; grass debug knows atmosphere globals).

Most findings are M-effort and already aligned with perf-plan slice 6's splits — cross-references below.

## Findings

### CORE-1 🟠 Out-of-band `RuntimeInitializeOnLoadMethod` self-bootstraps in Core
- **Category:** Architectural drift (rule 3 — boot path discipline)
- **Severity:** 🟠
- **Location:** [LoadingManager.cs:21-28](../../../Assets/Scripts/Core/Services/LoadingManager.cs#L21-L28), [EventBusProcessor.cs:45-52](../../../Assets/Scripts/Core/Events/EventBusProcessor.cs#L45-L52)
- **Effort to fix:** S (decision) / M (implementation)
- **Cross-ref:** project rule 3 explicitly calls this out as the critical hotspot rule.

Two `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` hooks create singleton `GameObject`s before any `GameBootstrap` runs. `LoadingManager` self-creates because it must paint the loading overlay before any other component initializes — there is a documented bootstrapping chicken-and-egg here. `EventBusProcessor` self-creates with no equivalent justification; it could be `EnsureComponent<EventBusProcessor>()`'d from `GameBootstrap` like every other infra service. The `Init()` method on `EventBusProcessor` also doesn't get torn down — there is no symmetric path that drops the singleton on scene reload, but `ClearProcessors()` is called from `GameBootstrap.OnDestroy`.

**Proposed direction:** Promote `EventBusProcessor` ownership to `GameBootstrap.EnsureComponent<EventBusProcessor>()` and delete the static `Init()`. Document `LoadingManager`'s `CreateInstance()` as the *one* sanctioned out-of-band hook with a short comment explaining why (overlay must paint before any `IEarlyInitialize` runs).

### CORE-2 🟠 `DebugCaptureController` is a 1006-line god-class
- **Category:** Cross-coupling
- **Severity:** 🟠
- **Location:** [DebugCaptureController.cs](../../../Assets/Scripts/Core/Services/DebugCaptureController.cs) (and inline `CloudDebugModule` starting at L884)
- **Effort to fix:** L
- **Cross-ref:** Already listed in [perf-plan slice 6](../../design/2026-06-08-performance-maintainability-plan.md). Do not duplicate the split here.

The controller currently owns: registry construction and module wiring (L80-L96), seven event subscriptions (L57-L106), capture-set cycling, screenshot encoding + downsampling + file IO (L339-L405), filesystem pruning (L418-L454), metadata serialization for camera/runtime/weather/precipitation/sun (L472-L597), an OnGUI HUD with prebuilt panel texture (L649-L731), and the console `debug.*` command set (L735-L856). It also reaches into `ICameraTeleportRegistry` (L645) and `IConsoleService` (L804) to coordinate cross-system pose recording and "close console for screenshot" UX. `CloudDebugModule` is implemented in the same file (L884-L1006), which makes the cross-coupling worse than it looks.

**Proposed direction:** Reference perf-plan slice 6 split. Additionally, before splitting, extract `CloudDebugModule` into its own file alongside `GrassDebugModule`/`MemoryDebugModule`/`ConsoleDebugModule`. The eight `Debug*RequestedEvent` listeners are a natural `DebugCommandRouter` boundary distinct from the capture orchestrator.

### CORE-3 🟠 `WaterDebugModule` is 878 lines and crosses water/biome/terrain/freeze
- **Category:** Cross-coupling
- **Severity:** 🟠
- **Location:** [WaterDebugModule.cs](../../../Assets/Scripts/Core/Services/WaterDebugModule.cs)
- **Effort to fix:** L
- **Cross-ref:** [perf-plan slice 6](../../design/2026-06-08-performance-maintainability-plan.md) lists this controller.

Registers ~85 modes (L185-L284), 18 capture sets (L286-L417), water-mesh stats with caching (L702-L838), mesh integrity / edge-manifold analysis (L606-L700), Voronoi/biome metadata, terrain-geography metadata (L144-L175), per-mesh histogram aggregates, and the F6 overlay. Caches 45 `Shader.PropertyToID` constants spanning eight subsystems (wave, glint, foam, freeze, voronoi, terrain coast/slope/snow). The metadata appender for Biome/TerrainGeography lives here instead of on biome/terrain modules.

**Proposed direction:** Split per slice 6 (mode registration vs metadata collection). Separately, push Biome and TerrainGeography metadata into their own modules so this file is genuinely about water. Caustics-related modes are out-of-scope-for-fixes (project rule 6) but should stay grouped together when the split happens.

### CORE-4 🟠 `ShaderGlobalIds` hub is incomplete; every module duplicates `PropertyToID` caches
- **Category:** Cross-coupling / dead code
- **Severity:** 🟠
- **Location:** [ShaderGlobalsController.cs:3-9](../../../Assets/Scripts/Core/Services/ShaderGlobalsController.cs#L3-L9), and every `static readonly int _xxxId = Shader.PropertyToID(...)` site under `Core/Services/` and `Planet/`.
- **Effort to fix:** M
- **Cross-ref:** Echoes audit-2026-05-28 QUAL-03 (string-literal shader globals in `AtmosphereDiagnostics`).

`ShaderGlobalIds` declares exactly four IDs (`_GameTime`, `_OceanDebugMode`, `_WaterFocusMode`, `_OceanFocusMode`). Every debug module then redeclares its own private `static readonly int _xxxId` block — `WaterDebugModule` has 45, `GrassDebugModule` has 8, `CloudDebugModule` has 8, `WaterWakeController` has 5. `_OceanDebugMode` is duplicated in at least 3 files. There is no single source of truth and no contract preventing two modules from picking different names for the same global.

**Proposed direction:** Either (a) expand `ShaderGlobalIds` into a partial class with module-specific files (`ShaderGlobalIds.Water.cs`, `.Grass.cs`, `.Cloud.cs`) and have every module pull from there; or (b) accept the duplication explicitly and add a one-line comment to `ShaderGlobalIds` explaining it's a tiny "shared globals" hub, not the registry. Decision is Bryan's. The status quo is a hidden coupling surface that grows every audit.

### CORE-5 🟡 `GameBootstrap.EnsureComponent<T>` list grows silently
- **Category:** Architectural drift
- **Severity:** 🟡
- **Location:** [GameBootstrap.cs:64-69](../../../Assets/Scripts/Core/Services/GameBootstrap.cs#L64-L69)
- **Effort to fix:** S

Six hand-listed `EnsureComponent<T>()` calls. The 27-commit history shows this list grew unevenly — `ScaleReferenceMarkers` was added recently; `EventBusProcessor` is conspicuously absent (CORE-1). New debug services that need ensuring are easy to forget. There is no symmetric "register interface" call: each `EnsureComponent` target registers itself in `Awake` instead.

**Proposed direction:** Either (a) leave as-is but add a comment naming this as the single source for "always-on infra components"; or (b) discover via interface marker (e.g. `IBootstrapInfraComponent`) and iterate. (b) is over-engineering for six entries today — flag only.

### CORE-6 🟡 `ServiceLocator.Clear()` doesn't dispose, but `Unregister<T>()` doesn't either
- **Category:** Architectural drift
- **Severity:** 🟡
- **Location:** [ServiceLocator.cs:51-69](../../../Assets/Scripts/Core/Services/ServiceLocator.cs#L51-L69)
- **Effort to fix:** S
- **Cross-ref:** Audit-2026-05-28 SUGG-02. Partial: `Clear()` now disposes; `Unregister<T>()` does not.

The 2026-05-28 audit asked for `Clear()` to dispose `IDisposable` services — that has been done (L65-L67). However, the symmetric problem in `Unregister<T>()` remains: `GameBootstrap.OnDestroy` calls `Unregister<IInputMapService>(_inputMapService)` after manually `(.. as IDisposable)?.Dispose()`-ing it (L37). If another caller forgets the dispose dance, the disposable leaks. The asymmetry between `Clear()` (auto-dispose) and `Unregister<T>()` (caller-dispose) is itself a footgun.

**Proposed direction:** Either make both auto-dispose (and remove manual dispose calls in `GameBootstrap.OnDestroy`), or document `Unregister<T>` as caller-owns-dispose. Don't leave them asymmetric.

### CORE-7 🟡 `ShaderGlobalsController.Update()` writes `_GameTime` every frame unconditionally
- **Category:** Per-frame hot path
- **Severity:** 🟡
- **Location:** [ShaderGlobalsController.cs:31-40](../../../Assets/Scripts/Core/Services/ShaderGlobalsController.cs#L31-L40)
- **Effort to fix:** S
- **Cross-ref:** Audit-2026-05-28 NEW-06 partially overlaps.

`_GameTime` genuinely changes per-frame so the write is justified. However, `ShaderGlobalsController` is the *only* per-frame writer with no rate limit; everything else in the layer is event-driven or `LateUpdate`-once. Cost is a few hundred ns/frame today, but Bryan asked to flag hot-path drift. Also, `ShaderGlobalsController` does the work in `Update()` while `WaterWakeController` does its writes in `LateUpdate()` — splitting "compute" and "publish" between frames produces a 1-frame skew that's invisible today but a future debug rabbit-hole.

**Proposed direction:** Move the `_GameTime` write to `LateUpdate` to match `WaterWakeController` and any future shader-globals writers, so the publish phase is consistent. Or, defer to the broader `ShaderGlobalsController` consolidation in CORE-4.

### CORE-8 🟡 `GrassDebugModule.AppendAtmosphereMetadata` reads atmosphere globals from a grass module
- **Category:** Cross-coupling / style
- **Severity:** 🟡
- **Location:** [GrassDebugModule.cs:234-241](../../../Assets/Scripts/Core/Services/GrassDebugModule.cs#L234-L241), and `AppendScaleReferenceMetadata` (L243-L261)
- **Effort to fix:** S

`GrassDebugModule` reads `_AtmosphereRadius`, `_SeaLevelRadius`, `_DensityOriginRadius`, `_ViewSteps`, `_SunSteps`, `_WaterVolumeEnabled`, `_TerrainAerialPerspectiveDistances`, `_OceanDebugMode` to populate an `--- Atmosphere ---` block in the F10 sidecar. This belongs on an atmosphere debug module. Same for the `--- ScaleRef ---` block which reads `IScaleReferenceDebugStatsProvider`. The grass module pulled them in because no atmosphere/scale-ref module existed yet — the original choice is documented in the file but should be unwound during the split work.

**Proposed direction:** Extract `AtmosphereDebugModule` and `ScaleReferenceDebugModule` (the latter as a tiny `IDebugCaptureMetadataProvider` on `ScaleReferenceMarkers` itself or co-located in the same file). Register them through `DebugCaptureController.InitializeRegistry()`.

### CORE-9 🟡 `EventBusProcessor` singleton self-resurrects but no shutdown contract
- **Category:** Architectural drift
- **Severity:** 🟡
- **Location:** [EventBusProcessor.cs](../../../Assets/Scripts/Core/Events/EventBusProcessor.cs)
- **Effort to fix:** S

Self-creates via `RuntimeInitializeOnLoadMethod` (CORE-1) and stays alive forever. `_instance` is set in `Awake` but never cleared in `OnDestroy`, so a domain reload that destroys the GO but keeps statics would leave a dangling reference. `ClearProcessors()` is called from `GameBootstrap.OnDestroy`, but there is no parallel `_instance = null` and no `OnDestroy` on this class at all. Low practical risk under current play patterns; flagging because the singleton lifecycle is implicit.

**Proposed direction:** Add `OnDestroy { if (_instance == this) _instance = null; }`. Fold this into CORE-1's `EnsureComponent` move.

### CORE-10 ⚪ Console Single-target resolves through `FindAnyObjectByType` on every call
- **Category:** Per-frame hot path / style
- **Severity:** ⚪
- **Location:** [CommandExecutor.cs:91-95](../../../Assets/Scripts/Core/Console/Registry/CommandExecutor.cs#L91-L95)
- **Effort to fix:** S

`MonoTargetType.Single` commands re-`FindAnyObjectByType(cmd.DeclaringType, ...)` every invocation. The console isn't a per-frame call site, so this is fine. But the existing `ConsoleRegistry.RegisterInstance<T>` path (`MonoTargetType.Registry`) already exists and is unused for the single-instance Mono case. Many `MonoTargetType.Single` controllers (`DebugCaptureController`, `FreeCameraController`, `QualityController`, `ScaleReferenceMarkers`, `ConsoleController` itself) already register with `ServiceLocator`. Wiring `MonoTargetType.Single` to consult `ServiceLocator` first by `DeclaringType` lookup before falling back to `FindAnyObjectByType` would remove the scan in the common case.

**Proposed direction:** In `ResolveTarget`, try a `ServiceLocator` lookup keyed by `cmd.DeclaringType` (or a registered interface) before the scan. Optional polish.

### CORE-11 ⚪ Console scan triggers reflection allocation across the whole AppDomain at startup
- **Category:** Style / startup cost
- **Severity:** ⚪
- **Location:** [ConsoleRegistry.cs:29-58](../../../Assets/Scripts/Core/Console/Registry/ConsoleRegistry.cs#L29-L58)
- **Effort to fix:** M

`ConsoleRegistry.Scan()` iterates every loaded assembly and calls `GetTypes()` on each, with broad `BindingFlags`. With ~147 `[ConsoleCommand]`/`[CommandPrefix]` annotations across 21 files this is a one-shot cost at console init — only enabled in debug/`--allowDebug` builds (CORE-12 below). Each `GetMethods(flags)` call allocates a `MethodInfo[]`. Total impact is sub-millisecond, but the scan runs against `mscorlib`, `UnityEngine.CoreModule`, etc., which is wasteful. A simple filter (skip assemblies whose name starts with `Unity`, `System`, `Mono`, `Microsoft`) would cut allocations meaningfully and is a one-line predicate.

**Proposed direction:** Add an `IsProjectAssembly(asm)` filter that excludes Unity/system assemblies; document the heuristic.

### CORE-12 🔵 `Debug.isDebugBuild || Application.isEditor` is the only release gate
- **Category:** Style / convention
- **Severity:** 🔵
- **Location:** [DebugConsoleBootstrap.cs:31-43](../../../Assets/Scripts/Core/Console/DebugConsoleBootstrap.cs#L31-L43)
- **Effort to fix:** S

Console is allowed when `Debug.isDebugBuild` is true. The release-build strip plan in the original design doc mentioned a `[DebugOnly]` attribute hook, never implemented. Today, the registry still scans `[ConsoleCommand]` methods in production unless the build was made non-dev — which is fine, but `[ConsoleCommand]` attribute metadata still ships in the assembly. Not urgent; flagging because Bryan asked about CONSOLE-6 follow-ups.

**Proposed direction:** Open question for Bryan — is the `[DebugOnly]` stripping pass worth doing, or has it been deprioritized? See open questions.

### CORE-13 ⚪ `MemoryDebugCounters` is a static publish-shaped god-bag
- **Category:** Cross-coupling
- **Severity:** ⚪
- **Location:** [MemoryDebugModule.cs:10-48](../../../Assets/Scripts/Core/Services/MemoryDebugModule.cs#L10-L48)
- **Effort to fix:** M

Eight static `Report*` methods that external producers (`ChunkedSurfaceProvider`, biome atlas builder, etc.) call to push counters. This is essentially a poor-man's metrics interface — works but couples every memory-counted subsystem to a Core debug type. An `IMemoryReporter` interface registered through ServiceLocator (or an `EventBus` push event) would invert the dependency. Not urgent.

**Proposed direction:** Note for the broader metrics layer when slice 5 (perf-plan) adds CPU/GPU timing counters.

### CORE-14 🔵 `DebugCaptureController.OnGUI` and `DebugInputRelay.Update` mix routing and direct-keyboard reads
- **Category:** Style / convention
- **Severity:** 🔵
- **Location:** [DebugInputRelay.cs:42](../../../Assets/Scripts/Core/Services/DebugInputRelay.cs#L42)
- **Effort to fix:** S
- **Cross-ref:** Audit-2026-05-28 QUAL-07 (F9 still raw-polled in `WeatherManager`).

`DebugInputRelay` routes most hotkeys through `IInputMapService` then raises `DebugCommandRequestedEvent`, but at L42 reads `Keyboard.current?.shiftKey.isPressed` directly to decide between `DropScaleMarkers` and `ClearScaleMarkers`. This is the *one* place in the relay that breaks the abstraction. There is no input map binding for "shift modifier" — adding one would make the chord explicit.

**Proposed direction:** Add a `ShiftHeld` `InputAction` on the gameplay map; the relay reads `_input.ShiftHeld.IsPressed()` instead of `Keyboard.current`. Closes the last raw-polling escape hatch in this layer.

### CORE-15 ⚪ Console history navigation tracks suggestion popup state across two booleans
- **Category:** Style
- **Severity:** ⚪
- **Location:** [ConsoleController.cs:82-90](../../../Assets/Scripts/Core/Console/ConsoleController.cs#L82-L90), see `_suggestionsFrozen`, `_suggestionsSuppressed`, `_activeSuggestionIdx`, `_popupScrollOffset`
- **Effort to fix:** M
- **Cross-ref:** [perf-plan slice 6](../../design/2026-06-08-performance-maintainability-plan.md) — slice 6 will split UI from exec.

There are 4 mutable fields that together describe popup state, mutated from 9 sites across `UpdateNormalMode`, `AcceptSuggestion`, `DismissSuggestions`, `ResetSuggestions`, `HistoryPrevious`, `HistoryNext`, `OnInputMutated`. The slice 6 split will already touch this — call out a `PopupState` struct as part of that work, not separately.

**Proposed direction:** Defer to perf-plan slice 6's `ConsoleController` split (exec vs UI).

## Cross-cutting themes

1. **The debug subsystem is doing more than debugging.** `DebugCaptureController` owns console-close UX, file IO, registry initialization, GUI rendering, and metadata serialization (CORE-2). `WaterDebugModule` owns biome and terrain-geography metadata (CORE-3). `GrassDebugModule` owns atmosphere globals dump (CORE-8). The cleanest unwinding is the slice-6 split *combined with* extracting per-domain debug modules so each subsystem owns its own debug surface.
2. **Shader globals are duplicated across module-local caches with no central authority.** `ShaderGlobalIds` exists but only holds 4 entries while ~70 are scattered across modules (CORE-4). Either commit to a centralized partial-class layout or document that the hub is intentionally tiny. Currently it's the worst-of-both — looks shared, behaves private.

## Open questions for Bryan

1. **CORE-1 / CORE-9:** Should `EventBusProcessor` move under `GameBootstrap.EnsureComponent`, or do you want to keep the static-singleton pattern for resilience against scene reload bugs?
2. **CORE-4:** Centralize `ShaderGlobalIds` (partial classes) or accept module-local caches as intentional encapsulation?
3. **CORE-12:** Is the `[DebugOnly]` release-strip pass from the original design doc still on the roadmap, or has the simpler `Debug.isDebugBuild` gate replaced it?
4. **CORE-8:** Should `Atmosphere`, `ScaleReference`, `Biome`, `TerrainGeography` each own a tiny `IDebugCaptureMetadataProvider` (and thus the cache of their own shader IDs)? My recommendation is yes, but it's a project convention call.

## Out-of-scope for this hotspot

- **Caustics** (project rule 6) — `WaterDebugModule` registers the caustics modes (L245-L250). Flagged only; no fix recommendation.
- **Tests** (project rule 7) — `TestConsoleCommands.cs` is intentional proof-of-life and isn't a test asset. Left untouched.
- **`Planet.cs`, `FreeCameraController.cs` movement splits, `WaterDebugModule`/`ConsoleController` UI-vs-exec splits** — already owned by [perf-plan slice 6](../../design/2026-06-08-performance-maintainability-plan.md). Referenced; not re-recommended.
- **`AtmosphereDiagnostics` string-literal `Shader.SetGlobal*` (audit-2026-05-28 QUAL-03)** — file is outside this hotspot's `Core/` boundary. Flagged in audit-2026-05-28; still open.
- **`WeatherManager.Update` F9 raw-poll (audit-2026-05-28 QUAL-07)** — same: outside `Core/`. CORE-14 covers the *one* remaining raw-poll inside `Core/`.
- **EventBus reflection (audit-2026-05-28 NEW-01 / SUGG-01)** — already resolved in [EventBus.cs:135-150](../../../Assets/Scripts/Core/Events/EventBus.cs#L135-L150) (compiled-expression invoker cache) and [EventBusAutoBinder.cs:70-114](../../../Assets/Scripts/Core/Events/EventBusAutoBinder.cs#L70-L114) (binding cache). Verified during this audit.
- **CONSOLE-6 follow-ups** — `CompletionRanker` shipped at [Console/Intellisense/CompletionRanker.cs](../../../Assets/Scripts/Core/Console/Intellisense/CompletionRanker.cs); slice 6_1/6_2/6_3 polish is in. Console arc is closed for this audit.
