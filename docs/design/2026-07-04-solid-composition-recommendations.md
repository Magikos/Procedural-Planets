# SOLID & Composition Recommendations — 2026-07-04

Assessment of where the codebase stands against SOLID + composition-over-inheritance, and
the specific changes that would raise the floor. Grounded in the 2026-07-03 audits
([grass/cloud](../audit/2026-07-03-grass-cloud-line-audit.md),
[general](../audit/2026-07-03-general-code-audit.md)) — every claim has a file behind it.

**Honest baseline first:** this codebase is already better than most Unity projects on
these axes. Composition over inheritance is near-total (no deep hierarchies anywhere —
variation is done with data and interfaces, not subclassing). The settings-DTO pattern,
the world/app service split, the orchestrator rule, and the subsystem decompositions
(WeatherManager → `WeatherEvolutionScheduler` + `WeatherQueryCache` + `WeatherDiagnostics`;
grass → controller/dispatcher/resolver/pool/stats) are textbook composition. Most of what
follows is closing the gap between the rules you already wrote and the code that drifted,
plus three genuinely new standards.

---

## S — Single Responsibility

**Where it holds:** the grass stack is the model citizen — `GrassPlacementController`
(orchestrates), `GrassChunkDispatcher` (GPU dispatch), `GrassChunkResidencyResolver`
(which chunks), `GrassBladeBufferPool` (memory), `GrassPlacementStats` (telemetry). Five
small classes, each answerable in one sentence.

**Where it breaks:**

### S1. `ChunkedSurfaceProvider` (1,764 lines) — four subsystems in one class
Chunk lifecycle/LOD + surface-edit rasterization (~500 lines added by the path system) +
biome atlas access + raycasting + color generation. Already filed as G10; the SRP framing
adds urgency: every new edit type ("future terrain/edit textures" per the roadmap memory)
grows the wrong class. **Extract `SurfaceEditRasterizer`** (the `TryPaint*` / `Rebuild*` /
batch/clear block) — it already touches chunks only through their textures and the atlas
update calls, so the seam is clean.

### S2. MonoBehaviour controllers wear 4-5 hats
`PrecipitationController` is simultaneously: settings authoring surface (inspector fields
→ `PrecipitationDto.From(this)`), shader-global uploader, console-command host, world
service, and event listener. `CloudController` similar minus authoring.
`WeatherManager` shows the fix already applied once: its evolution, readback-cache, and
diagnostics responsibilities live in plain classes it composes; the MB is the Unity
adapter. **Standard to adopt:** *the MB is a shell — lifecycle, inspector, Unity messages;
every responsibility that doesn't need a Unity message lives in a plain class the shell
constructs and ticks.* Applying it here means a `PrecipitationShaderUploader` plain class
(the two `Ensure*/Update*` methods and the 20 property IDs), leaving the MB at ~150 lines.

### S3. Settings authoring belongs to SOs, not scene objects
G11 in the general audit: precipitation authors settings on the MB, rain particles have no
DTO at all. SRP reading: "being the authoring surface" is a responsibility, and the SO is
the class designated for it. Finish the pattern: `PrecipitationSettings` SO +
`RainParticleDto`.

---

## O — Open/Closed

**Where it holds:** the debug-module registry (`DebugCaptureController` orchestrates,
each `*DebugModule` self-registers — new domain = new module, zero core edits) and the
attribute-driven console registry (`[ConsoleCommand]` discovery) are both genuinely
open-for-extension. Keep pointing at them as the house pattern.

**Where it breaks:**

### O1. Surface-edit kinds are stringly-typed switches — the next edit type edits N sites
`SurfaceEditStamp.kind` is `"path"` / `"scorch"` compared inline in
`SurfaceEditController.CountKind`, `ClearSavedScorchStamps`,
`ChunkedSurfaceProvider.RebuildPathWearFromStamps` (`kind != "path"` skip),
`RebuildSurfaceStateFromStamps` (`kind != "scorch"` skip). Adding "terrain-deform" —
which the memory says is coming — means finding every string comparison. **Change:** an
edit-kind handler registration — each kind supplies its rasterize/rebuild/clear behavior;
the controller iterates handlers instead of switching on strings:

```csharp
interface ISurfaceEditKind
{
    string Id { get; }                       // stable save key ("path", "scorch")
    int Rebuild(ChunkedSurfaceProvider p, IReadOnlyList<SurfaceEditStamp> stamps, long now);
    void Clear(ChunkedSurfaceProvider p);
}
```

This is the highest-value OCP change because the extension axis is *known* and imminent.
(It also naturally rides along with S1's rasterizer extraction — do them together.)

### O2. Weather render-feature gating is copy-modify, not extend
G12: cloud/precipitation/atmosphere features each re-implement the same
suppress/focus/frustum/controller-alive gate. Today "add a weather pass" = copy 60 lines
and tweak. A shared `WeatherPassGate.ShouldRender(camera)` + one per-feature hook makes
new passes additive.

### O3. Central debug-mode enums require editing the hub to add a mode
`DebugModeConstants.SuppressesWeatherPasses(int)` / `PerformanceWeatherIncludes*(int)`
centralize knowledge about every mode. Low priority (debug modes change slowly), but if it
churns: let a mode declare flags (`suppressesWeather`, `includesClouds`) at registration
in the debug registry instead of the hub pattern-matching mode IDs.

---

## L — Liskov Substitution

Near-zero inheritance means near-zero LSP risk — the codebase's composition habit already
bought this principle. The real, adjacent problem is **interface contract honesty**:

### L1. Contracts lie about failure modes
`WeatherManager.PrecipitationDebugControl` — comment says "returns null", implementation
throws (G1). `CloudController.Initialize` assumes `Get<IWeatherConfigurator>` cannot fail
(A4). Callers substitute "the documented contract" for "the actual behavior" and break.
**Standard:** every interface member that can fail states how (null / throw / default),
and `Get<>` vs `TryGet<>` at the call site must match that statement. This is cheap — it's
a review-checklist line, not a refactor.

### L2. Null-object fallbacks are good — keep them substitutable
`DefaultGrassQualitySettings`, the near-field's 1×1 fallback textures, the neutral climate
map: all correct null-object pattern. The fallback-radius OOB bug (A5) shows the risk —
a fallback that behaves *differently* from the real thing (here: crashing the sampler) is
an LSP violation in spirit. When adding a fallback, test the degraded path once.

---

## I — Interface Segregation

**Where it holds:** `WeatherManager` implementing six narrow interfaces
(`IWeatherProvider` / `IWeatherConfigurator` / `ILateInitialize` / `IProgressReporter` /
`IWorldServiceRegistrar` / `IWorldTeardown`) is ISP done right — each client sees only its
slice. Same for the grass stats-provider interfaces.

**Where it breaks:**

### I1. `IPrecipitationDebugControl` is two interfaces wearing one name
The **render feature** depends on it for `IsRenderingEnabled`, `ShouldRenderLocalParticles`,
`ShouldRenderRainParticles`, particle counts — production render gating. The **debug
module** depends on it for `WeatherParticleProofMode`, `WeatherParticleSettingsSummary`.
The name says debug; the render path says otherwise — that's how it ended up load-bearing
without anyone deciding it should be. **Split:** `IPrecipitationRenderState` (feature-facing)
+ `IPrecipitationDebugControl` (module-facing); the controller implements both.

### I2. `IGrassQualitySettings` is accreting into a grab-bag
Now ~13 members spanning three consumers: chunk-path tuning, near-field distances/altitudes,
overlay altitudes. Each consumer reads 3-5 members. Not urgent, but the next few additions
should split it (`IGrassChunkQuality`, `IGrassNearFieldQuality`) rather than grow it —
same accretion curve `CloudSettings` was on before it was (per project rules) meant to be
split per domain concern.

---

## D — Dependency Injection

This is the axis with the most room. The codebase uses a **service locator**, which is
DI's less-honest cousin: dependencies are real but invisible — not in constructors, not in
any manifest, discoverable only by grepping method bodies. Both live boot-order bugs (A4,
G1) are exactly the failure mode locators enable: nothing forced the dependency to exist
before use.

### D1. Land the dependency-declared init graph — it's already designed
`docs/design/2026-06-10-init-graph.md` + the CLAUDE.md rule ("services declare
`Type[] Dependencies`; a generic dependency-resolved init graph topologically orders
them") **is** this project's DI answer — declared dependencies, ordering derived, missing
deps fail loudly at boot instead of at first use. It's marked in-progress; it is the
single highest-leverage SOLID investment available, because it converts every implicit
`Get<>`-and-pray into a declared, validated edge. The two `[DefaultExecutionOrder]`
stragglers (G5) and both boot-order bugs disappear as a side effect.

### D2. Plain classes take constructor injection — no locator below the composition root
The codebase is one hop from clean here. Compare:

```csharp
// GrassChunkDispatcher — clean: everything through the ctor. ✔
public GrassChunkDispatcher(ChunkedSurfaceProvider surfaceProvider, Transform planetTransform,
    ComputeBuffer grassParamsBuffer, int grassParamCount, int maxBladesPerLane, ...)

// GrassNearFieldController — one line short of clean: ✘
public GrassNearFieldController(Transform planetTransform, ..., ILogger logger)
{
    ...
    var quality = ServiceLocator.Get<IGrassQualitySettings>();   // hidden 8th dependency
```

**Standard:** `ServiceLocator.Get/TryGet` is legal only in composition roots — MB
`Awake`/init-phase methods, render features (Unity constructs them; no ctor seam), console
command bodies, and debug modules. Every plain class receives its dependencies as
constructor parameters. Mechanical to enforce in review ("does a `sealed class` body
mention ServiceLocator? reject"), and it makes each class's dependency list its ctor
signature — which is the entire point of DI.

### D3. Depend on capabilities, not managers
`PlanetGrassCoordinator` takes `IPlanetSurfaceSampler` (a capability), not `Planet` — good
precedent. Counter-case: `CameraFollowGrassInteractor` does `FindAnyObjectByType<Planet>()`
and reaches into `planet.ShapeGenerator.EvaluateElevation` — a concrete two-hop reach that
couples a debug tool to the planet's internals. Debug code, so low stakes, but the
standard for production code: **inject the narrowest interface that answers the question**
(here: the existing `IPlanetSurfaceSampler` already answers it).

### D4. EventBus is for facts, not for pulling state
Current usage is healthy — events announce facts (`PlanetGeneratedEvent`,
`SettingsChangedEvent`) and listeners re-fetch through owned channels. Keep the line:
never put mutable payload objects on the bus that listeners retain (that recreates hidden
shared state with extra steps). Also: delete `EventBusAutoBinder` (G8) — reflection-based
binding makes the dependency graph *less* visible, the opposite of everything above.

---

## Composition

Already the house style; two reinforcements:

### C1. Name the pattern the codebase already invented: coordinator facades
`PlanetGrassCoordinator` (owns grass layer lifecycles, exposes one surface to `Planet`)
and `WeatherManager` (composes scheduler/cache/diagnostics) are the same shape: a facade
that *owns and wires* plain-class collaborators. Write it into CLAUDE.md as the required
shape for any subsystem with 3+ classes, so the next subsystem (surface edits is the
obvious candidate: `SurfaceEditController` + rasterizer + painter are currently wired ad
hoc from `Planet.cs`) gets composed the same way instead of rediscovering it.

### C2. Prefer struct/record snapshots at boundaries — already winning, keep it
`GrassInteractorSnapshot.From(source)` ("ONE place that knows IGrassInteractor's shape"),
`WeatherSample`, `GrassNearFieldStats`, the settings DTOs: boundaries exchange immutable
values, not live references. This is why the codebase has so few aliasing bugs. The one
place it's violated — `WeatherDiagnostics` holding `_owner` (the whole WeatherManager) and
reaching through it — works but is the pattern to avoid: pass the 5 values it reads, or an
interface slice, not the manager.

---

## Priority order

| # | Change | Principle | Size | Payoff |
|---|--------|-----------|------|--------|
| 1 | Land the init dependency graph (designed, unbuilt) | D | L | kills the whole class of boot-order bugs; the project's real DI |
| 2 | Locator-only-at-composition-roots rule + fix the ~6 plain-class violations | D | S | dependency lists become visible; mechanical review rule |
| 3 | `SurfaceEditRasterizer` extraction + edit-kind handlers | S+O | M | unblocks the known next feature (terrain edits) cleanly |
| 4 | Split `IPrecipitationRenderState` out of `IPrecipitationDebugControl` | I | S | stops debug interface being load-bearing |
| 5 | MB-as-shell pass on PrecipitationController (uploader extraction) | S | M | template for every remaining fat MB |
| 6 | Contract-honesty checklist line (null/throw documented per interface member) | L | S | prevents the G1/A4 class of bug at review time |
| 7 | WeatherPassGate extraction | O | S | new weather passes become additive |
| 8 | Settings authoring completion (Precip SO, Rain DTO) | S | M | already filed as G11 |

Items 1-2 are the structural ones — everything else is repetition of patterns the codebase
already does well somewhere. The shortest description of the whole doc: **the project's
best files already follow SOLID; the work is promoting their patterns from precedent to
rule.**
