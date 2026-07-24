---
name: pp-architecture-contract
description: Use when orienting in this codebase, deciding where new code lives, registering a service/initializer/DTO, touching boot, world load, or teardown, asking why ServiceLocator, WorldContext, SettingsProvider, EventBus, or ShaderGlobalIds exist, hitting "not registered"/"already registered"/"frozen"/"dependency cycle" errors, or checking what a change might break. Not for settings-catalog mechanics (see pp-settings-and-flags), rendering theory (see pp-gpu-rendering-reference), or history (see pp-failure-archaeology).
---

# pp-architecture-contract — how the machine is shaped, and what must not break

All facts verified against the working tree on branch `code-refactor`, **2026-07-06**.
Current user instructions, the working tree, `AGENTS.md`/`CLAUDE.md`, and
`.agent-memory/` form the rule stack. This skill explains the *shape* those rules
produce, the WHY behind each load-bearing decision, and the known weak points. If code
or skill text disagrees with the current rule stack, the stale text/code is drift to
correct — not a counter-example.

Jargon used throughout:
- **SO** = `ScriptableObject` (Unity asset used here only as an editor authoring surface).
- **DTO** = immutable snapshot record the runtime actually reads (never the SO).
- **asmdef** = Unity assembly-definition file; enforces compile-time layering.
- **World** = one loaded/generated planet's full service+settings scope; not the same
  thing as a Unity scene.

## Fast path

- New service or boot work: put ordering in the init graph, not `Start()` luck,
  `[DefaultExecutionOrder]`, or `RuntimeInitializeOnLoadMethod`.
- New setting: SO is the editor surface; runtime reads immutable DTO snapshots from the
  settings service.
- Cross-subsystem access: use `ServiceLocator`/`WorldContext` at boundaries; keep
  internal subsystem pipelines explicitly wired.
- Rendering globals: add names in the correct `ShaderGlobalIds` partial; avoid raw
  shader strings in consumers.

## 1. Subsystem map

| Domain | Key classes | Directory | graphify community |
|---|---|---|---|
| Boot & world lifecycle | `LoadingManager`, `InitGraph<T>`, `GameBootstrap`, `SceneBootstrap`, `ServiceLocator`, `WorldContext` | `Assets/Scripts/Core/Services/` | "Loading & Init Pipeline" |
| Settings | `SettingsService`, `SettingsProvider`, `ISettingsService` (`Core/Interfaces/`), per-domain `*Dto` next to each `*Settings` SO | `Assets/Scripts/Core/Services/` + each domain dir | (spread across domain communities) |
| Events | `EventBus<T>`, `EventBusProcessor`, `EventBusRegistry` | `Assets/Scripts/Core/Events/` | "Event Bus" |
| Console | `ConsoleRegistry`, `[ConsoleCommand]`/`[CommandPrefix]`, `ConsoleAsyncRunner` | `Assets/Scripts/Core/Console/` | "Console Input" / "Console UI" / "Console Arg Parsing" |
| Debug capture | `DebugCaptureController`, `DebugCapturePipeline`, `DebugScreenshotFiles` | `Assets/Scripts/Core/Services/` | "Debug Capture System" |
| Planet & terrain | `Planet` (orchestrator MB), `ShapeGenerator`, `TerrainFace` | `Assets/Scripts/Planet/` | "Terrain Face Mesh" |
| Surface chunks & edits | `ChunkedSurfaceProvider` (largest file, 1,764 lines), `SurfaceEditController`, `SurfaceEditStamp` | `Assets/Scripts/Planet/Surface/` | "Terrain Surface Chunks" / "Chunk Visibility LOD" |
| Biomes & climate | `BiomeMapBaker`, `BiomeAtlasService`, `VoronoiBiomeField` | `Assets/Scripts/Planet/Biomes/` | "Biome Climate Pipeline" / "Biome Atlas GPU" |
| Weather | `WeatherManager`, `SphericalWeatherGrid`, `WeatherEvolutionScheduler`, `WeatherQueryCache` | `Assets/Scripts/Planet/` + `Planet/Clouds/` | "Spherical Weather Grid" |
| Clouds | `CloudController`, `CloudRenderFeature`, `CloudDto` | `Assets/Scripts/Planet/Clouds/` | "Cloud Rendering" |
| Atmosphere | `AtmosphereController`, `AtmosphereDto` | `Assets/Scripts/Planet/Atmosphere/` | "Atmosphere Rendering" |
| Precipitation & rain | `PrecipitationController`, `RainParticleController` | `Assets/Scripts/Planet/` + `Planet/Precipitation/` | "Precipitation System" / "Rain Particles GPU" |
| Grass | `PlanetGrassCoordinator` (plain-class orchestrator), `GrassPlacementController`, `GrassNearFieldController` | `Assets/Scripts/Planet/Grass/` + `Planet/PlanetGrassCoordinator.cs` | "Grass Coordination" / "Grass Placement GPU" |
| Water | `WaterMeshBuilder`, `PlanetWaterSurface`, `WaterVolumeRenderFeature` | `Assets/Scripts/Planet/` | "Water Volume Rendering" |
| Shader globals | `ShaderGlobalIds` (9 partials), `ShaderGlobalsController`, `QualityController` | `Assets/Scripts/Core/Services/` | (Core services) |

Navigation: `graphify query "<question>"` first (graph at `graphify-out/`, built from
commit `ec0b1cd`; set a timeout — query/update hang in this checkout, audit G19, see
pp-build-and-env Known traps); `graphify-out/GRAPH_REPORT.md` lists 430 communities and the god
nodes — top three by edges: `ChunkedSurfaceProvider` (90), `Planet` (69),
`WeatherManager` (55). Those three files are where most cross-domain wiring meets.

## 2. The two-scope service model (ServiceLocator + WorldContext)

Everything hangs off `Assets/Scripts/Core/Services/ServiceLocator.cs`, which contains
**both** scopes:

- **Application scope** — `ServiceLocator.Register<T>` / `Get<T>` / `TryGet<T>`, a static
  dictionary. Lives for the whole process. Only for services that survive world reloads:
  `ILogger`, `ILoadingManager`, input, quality settings, debug console/capture
  (ownership list locked in `docs/design/2026-06-13-world-lifecycle.md`).
- **World scope** — `WorldContext` (same file, line 48): per-world service dictionary,
  its own `SettingsService`, a lifetime `CancellationToken`, and a reverse-order teardown
  list. Exactly one is active; `ServiceLocator.ActivateWorld` throws if another world is
  already active. Register with `ServiceLocator.RegisterWorld<T>` or, preferably,
  implement `IWorldServiceRegistrar.RegisterWorldServices(IWorldContext)` and let
  `SceneBootstrap` sweep the scene for registrars.

Resolution order that trips people: `ServiceLocator.TryGet<T>` checks the **active world
first**, then falls back to application scope (`ServiceLocator.cs:324-345`). So a world
service and an app service of the same interface would shadow — don't register the same
interface in both scopes. `Get<T>` throws when absent; `TryGet<T>` is for optional
services. **Resolve once at init; never `Get`/`TryGet` per frame** (a known open
violation is listed in §7).

World replacement is transactional (`LoadingManager.RunTransitionAsync`,
`Core/Services/LoadingManager.cs:144`): fade out → pause time → preload scene → cancel
old world's lifetime token → teardown old world (reverse init order, via
`WorldContext.TrackInitializer`) → deactivate old context → activate fresh
`WorldContext(request)` → activate scene → init graph runs → dispose old world → raise
`EventBus<WorldReadyEvent>` → fade in. Any failure after the swap tears down the partial
new world and parks on a fatal loading screen — the game never runs half-initialized.

- `WorldReadyEvent` (`ServiceLocator.cs:250`) is how persistent app-scope tooling
  (e.g. `DebugCaptureController`) refreshes its world references. **Never cache a world
  service across it.**
- Saved-world loads enter through `WorldLoadRequest`
  (`Core/Interfaces/ILoadingManager.cs:6`): scene, optional seed, save identity,
  `SettingsSchemaVersion` (validated against `CurrentSettingsSchemaVersion` before any
  load), and typed `WorldSettingsOverride<TDto>` values applied by
  `WorldContext.ApplySettingsOverrides()` before settings freeze. Stable string keys
  exist only at the save-file boundary; runtime settings are keyed by DTO type.

## 3. Settings: SO authoring → DTO runtime

Full mechanics and the per-DTO catalog live in **pp-settings-and-flags**; here is the
contract only. SOs are editor authoring surfaces. Runtime reads immutable DTO records via
`SettingsProvider.GetSettings<TDto>()` (`Core/Services/SettingsProvider.cs` — a thin
facade over `ServiceLocator.GetWorld().Settings`; **no fallback**: no active world = throw).
Each DTO lives next to its SO with a static `From(SO)` factory (e.g.
`Planet/Clouds/CloudDto.cs:38 From(CloudSettings)`,
`Planet/Atmosphere/AtmosphereDto.cs:29 From(AtmosphereSettings)`).

Lifecycle enforced by `SceneBootstrap.EarlyInitialize` (`Core/Services/SceneBootstrap.cs`):
sweep scene roots for `IWorldSettingsRegistrar` → register every DTO → apply
`WorldLoadRequest` overrides → `ValidateRequired` (services *and* DTOs) → `Freeze()`.
After freeze, registration throws; runtime changes go through
`SettingsProvider.Update<TDto>(current with { … })`, which raises
`EventBus<SettingsChangedEvent>` (`Core/Services/SettingsService.cs:28`). Consumers cache
the DTO and re-fetch on that event, filtered by `evt.DtoType` (see
`AtmosphereController.OnSettingsChanged`, `Planet/Atmosphere/AtmosphereController.cs:114`).
Console-command setters update the DTO, never the SO asset. Materials referenced by SOs
are cloned on first use before any runtime writes.

## 4. Init system: dependency graph, fail-fast

All initialization is driven by `LoadingManager` through two phases:
`IEarlyInitialize` then `ILateInitialize` (`Core/Interfaces/`). Both interfaces declare
`EarlyDependencies`/`LateDependencies` as `IReadOnlyList<Type>` (interface targets, e.g.
`typeof(IPlanet)` — see `WeatherManager.cs:145`). `InitGraph<T>`
(`Core/Services/InitGraph.cs`) topologically orders each phase with Kahn's algorithm;
cycles and missing dependencies are **hard boot failures with a printed diagnostic**, not
warnings. Any initializer exception aborts the whole pass — a partially initialized world
is invalid by decision (2026-06-13).

Status notes, **as of 2026-07-06**:
- The init graph is **landed and live** (`LoadingManager.cs:338,363`). CLAUDE.md still
  labels it "in-progress design — declare deps with a `// Depends on:` comment"; that is
  stale — zero such comments exist and the graph runs every boot.
- `EarlyPriority`/`LatePriority` still exist as a pre-graph tiebreaker
  (`OrderByDescending` before the graph sorts; only `GameBootstrap` 100 and
  `SceneBootstrap` 50 declare non-default values). New code declares dependencies, not
  priorities.
- `DependencyManager.WhenReady<T>()` (mentioned in CLAUDE.md and designed in
  `docs/design/2026-06-10-init-graph.md`) is **not implemented** — grep finds no
  `DependencyManager` type. External observers today use `WorldReadyEvent` or lazy
  resolution (e.g. `PlanetGrassCoordinator.Quality`, `Planet/PlanetGrassCoordinator.cs:54`).
- Banned: `[DefaultExecutionOrder]` (zero uses remain — audit finding G5 fixed in the
  working tree) and `RuntimeInitializeOnLoadMethod` except the single sanctioned one,
  `LoadingManager.CreateInstance` (`LoadingManager.cs:13` — the loading overlay must
  paint before any initializer runs).

## 5. Services over MonoBehaviours; orchestrators

Default to a plain C# class. Use a `MonoBehaviour` only for a Unity message or inspector
surface. One orchestrator drives many services:

- `GameBootstrap` (`Core/Services/GameBootstrap.cs`) — app-scope orchestrator.
  `DontDestroyOnLoad`; registers `IDebugCommandProvider`, `IInputMapService`,
  `IGrassQualitySettings`; ensures the persistent MBs exist (`ShaderGlobalsController`,
  `QualityController`, `DebugInputRelay`, `DebugCaptureController`,
  `SurfacePathMousePainter`); boots the console.
- `Planet` (MB) constructs and forwards `Configure`/`Tick`/`Dispose` to
  `PlanetGrassCoordinator` — a **plain class** that itself owns
  `GrassPlacementController` and `GrassNearFieldController` and carries the `grass.*`
  console commands via `[CommandPrefix("grass")]` + `ConsoleRegistry.RegisterInstance(this)`
  (`Planet/PlanetGrassCoordinator.cs`). This is the reference example of the pattern:
  no GameObject, constructor-injected deps, ticked by its owner.
- Console commands stay on the service that owns the state (no `*Commands` companion
  class unless the set genuinely outgrows the service). `SurfacePathDebugCommands.cs`
  (`path.*`/`scorch.*`) is an accepted wrapper over the shared `SurfaceEditController`.
- Cross-subsystem boundaries use `ServiceLocator`/`EventBus`; **internal** pipeline wiring
  uses interfaces + orchestrator-injected references. Don't route a subsystem's internals
  through the locator.

## 6. Cross-cutting mechanisms

**EventBus** (`Core/Events/EventBus.cs`): `EventBus<TEvent>.Raise/Listen/Unlisten` where
`TEvent : struct, IGameEvent`. Weak-reference subscribers (dead Unity objects pruned
automatically), compiled-expression invokers (no per-event reflection/boxing), optional
filters, `ListenOnce`, and deferred delivery drained by `EventBusProcessor`. Subscriber
exceptions are caught and logged — a bad listener can't break the raiser. Use direct
`Listen`/`Unlisten`; `EventBusAutoBinder.cs` is dead code slated for deletion (audit G8,
still present as of 2026-07-06).

**ShaderGlobalIds** (`Core/Services/ShaderGlobalIds.*.cs`): the single source of truth
for every shader-**global** name (anything passed to `Shader.SetGlobal*`/`GetGlobal*`).
Nine domain partials exist: `Core`, `Atmosphere`, `Biome`, `Celestial`, `Cloud`, `Grass`,
`Precipitation`, `Terrain`, `Water` (CLAUDE.md's list omits `Biome` — the file is real).
The hub owns *names*; each module caches its own
`static readonly int _xId = Shader.PropertyToID(ShaderGlobalIds.X)`. Per-`Material` and
compute-shader property names are shader-scoped and stay module-local (see
`PlanetGrassCoordinator.cs:28-37` mixing both correctly). The 2026-07-03 audit sweep
found zero raw string literals at `Shader.SetGlobal*` — keep it that way.

**Dirty-flag uploads**: any controller pushing shader globals per frame splits
static-vs-dynamic and only uploads when dirty. Canonical example:
`AtmosphereController._staticPropertiesDirty` (`Planet/Atmosphere/AtmosphereController.cs:21`)
— set on `OnPlanetGenerated` and `OnSettingsChanged`, consumed by
`EnsureStaticPropertiesUploaded` which early-outs when clean (`:141`).
`ShaderGlobalsController` (`Core/Services/ShaderGlobalsController.cs`) publishes
frame-varying globals in `LateUpdate` so all writers share one publish phase; it also
writes `_GameTime` wrapped to a 3600 s period (float-precision guard) and resets
transient debug globals on enable.

**Weather grid as single source of truth**: `WeatherManager`
(`Planet/WeatherManager.cs`) owns one `SphericalWeatherGrid`
(`Planet/Clouds/SphericalWeatherGrid.cs`) and exposes it as `IWeatherProvider`
(`SampleWeather(worldPosition)` → coverage/precipitation/storm). Clouds, rain,
lightning, and grass weather response all read the same grid — sky and ground visuals
can never disagree about where a storm is. Channels, evolution, and the coupling
contract are **pp-weather-sim-reference**'s territory; do not invent a second weather
field.

**SurfaceEditStamp as source of truth**: saved `SurfaceEditStamp` records
(`Planet/Surface/SurfaceEditController.cs:617`, ledger owned by `SurfaceEditController`)
are canonical. Path-wear and scorch textures are **derived caches**, rebuildable via
`ChunkedSurfaceProvider.RebuildPathWearFromStamps` (`:494`) and
`RebuildSurfaceStateFromStamps` (`:553`). Never treat the baked masks as authoritative;
new surface-edit features add a stamp type, not a new persistence path.

## 7. Invariants

| Invariant | Why | What breaks if violated | Where enforced |
|---|---|---|---|
| Runtime never reads a settings SO outside boot | DTO snapshots decouple consumers from god-SOs; console/save/override plumbing only sees DTOs | Console setters silently stop working; saves can't override the value; editor asset gets mutated at runtime | Convention + audit greps (design doc slice 6); violations are findings |
| One active `WorldContext`; world services never cached across `WorldReadyEvent` | World replacement is transactional | Stale references to torn-down services; leaked textures/buffers from the dead world | `ServiceLocator.ActivateWorld` throws (`ServiceLocator.cs:281`); teardown cancels lifetime token |
| Duplicate service/DTO registration throws; settings freeze after `SceneBootstrap` | Fail at boot, not mid-game; "who owns this?" always has one answer | Shadowed registrations, non-deterministic resolution | `WorldContext.Register` (`:75-82`), `SettingsService.Register`/`Freeze` |
| Required services + DTOs validated before world is ready | A world is fully initialized or unavailable | Null-ref roulette hours into a session | `SceneBootstrap.EarlyInitialize` → `ValidateRequired` lists (`SceneBootstrap.cs:16-52`) |
| Init ordering = declared `Type[]` dependencies, no execution-order attributes | Machine-checkable "why does A run before B"; cycles fail loudly | Renumber-the-priorities churn; silent misordering after scene edits | `InitGraph<T>` hard-fails on cycle/missing dep (`InitGraph.cs:42-53,79`) |
| `Awaitable` only — no coroutines, `async void`, `Task.Run` | Cancellation threads through world lifetime tokens; coroutines can't | Uncancellable work outlives its world; silent exception swallowing | Convention; 2026-07-03 audit sweep verified zero violations |
| Every shader-global name lives in `ShaderGlobalIds` | Globals collide across shaders; one grep finds every writer | Two systems fight over an undiscoverable string | Convention; audit sweep clean as of 2026-07-03 |
| Per-frame shader-global writers use dirty flags; publish in `LateUpdate` | Uploads are not free; consistent publish phase | Redundant GPU uploads; mid-frame global flips | Precedent (`AtmosphereController`, `CloudController`); `ShaderGlobalsController` |
| Stamps are canonical; masks are caches | Rebuildable state survives format changes and bugs | Unrecoverable divergence between saved edits and rendered wear | `Rebuild*FromStamps` paths exist and are the only rebuild story |
| Caustics untouched (`Assets/Graphics/Shaders/Ocean.shader` + related) | Every touch has broken them (see pp-failure-archaeology) | Visual regression Bryan has to catch | CLAUDE.md "Don't touch"; findings against caustics are flag-only |
| Audits are findings-only until Bryan marks fix/defer/wontfix | Bryan is the change authority | Unreviewed changes to a codebase with hand-tuned visuals | Workflow rule — see pp-change-control |

## 8. Decision log (load-bearing decisions + WHY)

| Decision | WHY | Source |
|---|---|---|
| Two service scopes instead of one global locator | Saved-world loading requires replacing *everything* world-owned atomically; a flat locator can't express "this dies with the world" | `docs/design/2026-06-13-world-lifecycle.md` (locked decisions) |
| Fail-fast init; no partial worlds | Debugging a half-initialized dependency chain cost more than a loud boot failure ever will | same doc: "A world is either fully initialized or unavailable" |
| DTO-type-keyed settings, string keys only at the save boundary | Type keys are compile-checked and refactor-safe; schema versions absorb save-format drift | same doc + `2026-06-10-settings-service.md` |
| SO→DTO split with `From(SO)` factories | Ends god-SO cross-coupling; rename an SO field and only the factory changes; DTOs compose exactly what one consumer needs | `2026-06-10-settings-service.md` (closed audit findings PLANET-1/WEATHER-1/GRASS-1) |
| Kahn's-algorithm init graph over priority numbers | Priorities were magic constants with no machine-checkable meaning; inserting a service required reading its neighbors | `2026-06-10-init-graph.md` ("Why priority numbers are out") |
| Interface dep targets (`typeof(IPlanet)`), not concrete types | Prevents coupling init order to implementations | same doc (locked decisions) |
| One orchestrator MB forwarding to plain-class services | Unity messages are the only thing MBs are for; plain classes are constructor-injected, disposable in deterministic reverse order | CLAUDE.md "Services over MonoBehaviours" |
| `EventBus` with weak refs + compiled invokers | Cross-subsystem notification without lifetime coupling; destroyed listeners self-prune; no reflection cost per event | `Core/Events/EventBus.cs:134` comment (the one sanctioned WHY comment style) |
| `ShaderGlobalIds` partial-class hub, names only | A global name is a process-wide singleton; the hub makes collisions visible at review time without centralizing the cached-ID hot path | CLAUDE.md "Shader globals" |
| Stamps as source of truth, masks as caches | Wear/scorch textures are lossy bakes; only a replayable ledger supports regrow-over-time, format migration, and rebuild-after-bug | `.agent-memory` surface-edit record; `Rebuild*FromStamps` implementation |
| Sanctioned single `RuntimeInitializeOnLoadMethod` | The loading overlay must exist before the first initializer can run — a bootstrap chicken-and-egg with exactly one egg allowed | CLAUDE.md boot rules + `LoadingManager.cs:13` |
| No test framework near-term | Bryan's explicit stance; validation = in-game evidence (see pp-validation-and-evidence) | CLAUDE.md "Tests" |

## 9. Known weak points — stated plainly (as of 2026-07-06)

1. **Chunk biome seam** (open, accepted): faint chunk-boundary color seams in the top-K
   biome blend. `BiomeMapBaker.SampleTopKPerTexel`'s 5×5 kernel can't see across chunk
   bounds; edge-replication mitigated but didn't eliminate it. True fix: extend the biome
   id grid by kernel radius via direct noise evaluation. Bryan accepted it 2026-05-31.
2. **Normal-mapping flat** (open, parked): the triplanar normal/ARM pipeline is wired
   end-to-end (arrays load 16/16, debug modes prove perturbation) yet terrain reads flat.
   Leading suspect: lighting-range compression in the `dayLight` lerp of
   `PlanetVertexColor.shader` (current endpoints and history: pp-failure-archaeology
   entry 10 — the tree has already widened them once). Details there.
3. **Grass far-field is an undecided design** (blocking): beyond 200 m there is nothing.
   Three options (extend near field / re-enable chunk layer as mid band / blanket-only)
   await Bryan's call — `docs/design/2026-07-04-grass-visual-migration-plan.md` Phase 3.
   No far-field code before that decision.
4. **Two grass layers are disabled**: `PlanetGrassCoordinator.cs:18,21` —
   `_chunkGrassEnabled = false`, `_grassBlanketEnabled = false` (blanket off until it
   shares blend ownership with the biome material path; current values: see
   pp-settings-and-flags). Near field is the only live layer.
5. **CLAUDE.md drift**: it references `docs/audit/2026-06-code-refactor/` (removed from
   tracking; recover via `git show 7048c2c:docs/audit/...`), describes the init graph as
   in-progress (it landed), and names `DependencyManager.WhenReady<T>()` (never built).
   Follow the *rules*; treat these three references as stale.
6. **Open audit findings that touch architecture** (from
   `docs/audit/2026-07-22-consolidated-code-audit.md`; former G1 TryGet, G2 grid-leak
   disposal, and
   G5 execution-order attributes are already **fixed in the working tree** — verified
   2026-07-06):
   - G6 RULE (open): `SurfacePathMousePainter.InputAllowed` does per-frame
     `ServiceLocator.TryGet` (`Core/Services/SurfacePathMousePainter.cs:169-176`).
   - G7 RULE (open): `SurfaceEditController` does synchronous `File.ReadAllText`/
     `WriteAllText` on the main thread (`:464,505`); grows with the stamp ledger.
   - G8 DEAD (open): `EventBusAutoBinder.cs` (204 lines, zero consumers).
   - G10 ARCH (open): `ChunkedSurfaceProvider` at 1,764 lines absorbed the surface-edit
     rasterizer; extraction-ready but waits on approval and on G8/G9 deletions.
   - G11 ARCH (recorded drift): three settings patterns in one weather subsystem — clouds
     fully SO→DTO, `PrecipitationController` uses its own inspector fields as the
     authoring surface, `RainParticleController` has no DTO at all (its console commands
     write MB fields directly, invisible to save/override plumbing).
   - G15 PERF (ceiling known): path regrowth rebuilds every chunk's masks every 5 s while
     any regrowing stamp exists.
   All are Bryan-gated — read pp-change-control before fixing anything here.
7. **`SceneBootstrap` validation profiles**: `WorldServiceValidationProfile.Full` vs
   `TerrainGrassBiome` (`SceneBootstrap.cs:6`) — test scenes validate a reduced service
   list, so "boots in the test scene" does not prove the full scene's registration set.

## 10. Where NOT to apply the patterns

- **Editor-only authoring affordances are legitimate.** `OnValidate`, `[ContextMenu]`,
  custom inspectors, tooltips, and inspector-tunable fields on SOs/MBs stay — do not
  strip them to satisfy "default to plain class" or "SOs are editor-only". The rule bans
  *runtime reads* of SOs, not authoring ergonomics.
- **Don't move material/compute property names into `ShaderGlobalIds`.** Only globals
  belong there; a material property can't collide across shaders.
- **Don't extract `*Commands` classes** from services whose command set is small;
  commands live with the state they mutate.
- **Don't split files to hit a line number.** ~400 lines is a symptom check; split when
  adding a *responsibility* (the watch list in the 2026-07-03 audit names the files near
  the line: `WaterMeshBuilder` 849, `Planet.cs` 634, etc.).
- **Don't route a subsystem's internal pipeline through ServiceLocator/EventBus** — those
  are cross-subsystem boundaries; internals use constructor-injected interfaces.
- **Don't touch caustics.** Ever. Flag findings only.

## When NOT to use this

- Adding/tuning a specific setting, quality tier, or runtime toggle → **pp-settings-and-flags**.
- How clouds/grass/water/atmosphere actually render (theory + shader structure) → **pp-gpu-rendering-reference**.
- Weather-grid channels, evolution math, coupling contract details → **pp-weather-sim-reference**.
- Why a past approach failed / what was reverted → **pp-failure-archaeology**.
- Whether you're allowed to make a change at all → **pp-change-control**.
- Building/running/measuring → **pp-build-and-env**, **pp-run-and-operate**, **pp-diagnostics-and-tooling**.

## Provenance and maintenance

Written 2026-07-06 against the dirty working tree of branch `code-refactor` (dirty is
normal here). Re-verify volatile claims before relying on them (git-bash):

```bash
# Two-scope locator, WorldContext, WorldReadyEvent all in one file
grep -n "class WorldContext\|struct WorldReadyEvent\|static class ServiceLocator" Assets/Scripts/Core/Services/ServiceLocator.cs
# Init graph is live (not "in-progress")
grep -n "new InitGraph" Assets/Scripts/Core/Services/LoadingManager.cs
# WhenReady still absent / priorities still present
grep -rn "DependencyManager\|WhenReady" Assets/Scripts --include=*.cs
grep -rn "EarlyPriority =>\|LatePriority =>" Assets/Scripts --include=*.cs
# No DefaultExecutionOrder (G5 stays fixed)
grep -rn "DefaultExecutionOrder" Assets/Scripts --include=*.cs
# G1 stays fixed (TryGet, not Get)
grep -n "IPrecipitationDebugControl control" Assets/Scripts/Planet/WeatherManager.cs
# ShaderGlobalIds partial list (currently 9 incl. Biome)
ls Assets/Scripts/Core/Services/ShaderGlobalIds.*.cs
# Disabled grass layers
grep -n "_chunkGrassEnabled = \|_grassBlanketEnabled = " Assets/Scripts/Planet/PlanetGrassCoordinator.cs
# Open dead code / file-size findings
wc -l Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs Assets/Scripts/Core/Events/EventBusAutoBinder.cs
# Graph freshness
git rev-parse --short HEAD   # compare against "Built from commit" in graphify-out/GRAPH_REPORT.md
```

Update triggers: the grass far-field decision landing (§9.3), any G-series fix approval
(§9.6), `DependencyManager`/`WhenReady` being built (§4), a CLAUDE.md refresh (§9.5), or
a new `ShaderGlobalIds` partial.
