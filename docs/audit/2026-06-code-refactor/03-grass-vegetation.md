# Audit — Grass & Vegetation

**Date:** 2026-06-10
**Branch:** code-refactor
**Auditor:** Claude (subagent)
**Scope:** `Assets/Scripts/Planet/Grass/*`, `Assets/Scripts/Core/Services/GrassDebugModule.cs`,
`Assets/Scripts/Core/Interfaces/IGrassDebugStatsProvider.cs`,
`Assets/Scripts/Core/Interfaces/IGrassNearFieldStatsProvider.cs`,
`Assets/Scripts/Core/QualityController.cs` (IGrassQualitySettings co-located there),
`Assets/Graphics/Shaders/Grass.shader`, `Assets/Graphics/Shaders/GrassMidField.shader`,
`Assets/Graphics/Shaders/Includes/Grass*.hlsl`,
`Assets/Scripts/Planet/Biomes/BiomeDefinition.cs`,
`Assets/Scripts/Planet/Biomes/BiomeSurfaceTextureArrays.cs`,
the grass-related grass-blanket fields on `Planet.cs`, plus grass-touching surfaces of
`PlanetChunk.cs` / `ChunkedSurfaceProvider.cs`.
**Status:** Findings only — no code modified.

## Executive summary

The grass stack is the project's most ambitious subsystem and, in many ways, its most
disciplined: a clean compute placement path, immutable per-blade GPU structs, async
readbacks for stats, shared face-space cell-range math, a registered `IGrassRuntimeControl`
seam, residency-based chunk allocation, and three small shared HLSL includes
(`GrassColor`, `GrassDither`, `GrassInteractors`) that all consumers honor. The boot path
is correct: `GrassInteractorRegistry.Initialize` runs in `EarlyInitialize`, controllers are
constructed by `Planet`/`ConfigureGrassController`, no `RuntimeInitializeOnLoadMethod` or
coroutines. The DTO pattern was prototyped (`GrassPlacementDtos`, `GrassInteractorSnapshot`).

The biggest finding is that the **DTO pattern showcase has regressed**: the two
`GrassPlacementDtos` types compile but have **zero call sites** —
`BiomeSurfaceTextureArrays.ResolveGrassParams` reads `def.GrassDensity`, `def.GrassHeight`,
`def.GrassTintBase`, etc. directly from `BiomeDefinition` and packs them into the GPU
buffer, completely bypassing the `GrassBiomeTintConfig.From` composition root. The chunk-grass
controller alone is also pushing 780 lines while owning quality knobs, residency, dispatch,
readback, stats, runtime nesting, and a near-field-stats hand-shake; it would split
cleanly. Several rejected experiments (mid-field controller, smoke-renderer struct fields,
`GrassMidField.shader` + compute) are still resident and pull weight. There are also a
handful of low-severity hot-path / convention drifts noted below.

Slice 5 of the active perf plan already owns the per-frame grass profiling and the
distant-card representation experiment — none of those concerns are re-raised here.

## Findings

### GRASS-1 [SO] Grass biome params read straight off the ScriptableObject, bypassing the prototyped DTO
- **Category:** Architectural drift (Settings DTO pattern)
- **Severity:** 🟠
- **Location:** [BiomeSurfaceTextureArrays.cs:150-169](../../../Assets/Scripts/Planet/Biomes/BiomeSurfaceTextureArrays.cs#L150-L169), [GrassPlacementDtos.cs:17-65](../../../Assets/Scripts/Planet/Grass/GrassPlacementDtos.cs#L17-L65)
- **Effort to fix:** S
- **Cross-ref:** Settings DTO pattern feedback (showcase); none in prior audits

`ResolveGrassParams(BiomeDefinition def)` reads every grass field
(`GrassDensity`, `GrassHeight`, `GrassWidth`, `GrassClumpStrength`,
`GrassMaxSlopeDegrees`, `GrassSlopeFadeDegrees`, `GrassMinWaterClearance`,
`GrassBiomeBlendPower`, `GrassTintBase`) directly from the SO and packs them into
`BiomeGrassParamsGpu`. The `GrassBiomeTintConfig` / `GrassPlacementClimateBinding`
DTOs in [GrassPlacementDtos.cs](../../../Assets/Scripts/Planet/Grass/GrassPlacementDtos.cs)
are documented as the canonical composition root, but a grep finds **no callers**
of `GrassBiomeTintConfig.From` or any reference to either type outside the file
that defines them. The pattern Bryan called out as the "first showcase" of the
DTO discipline is therefore not actually shipped on the grass-placement hot path
— the SO is the runtime input.

`GrassTintDryShift` and `GrassTintLushShift` exist on the SO and on the DTO but
are never packed into the GPU struct or sampled anywhere I can find. Either the
DTO is the truth (and `ResolveGrassParams` needs to consume `GrassBiomeTintConfig`
plus pack tint/shifts), or the DTO is dead code that should be deleted before it
gets cargo-culted.

**Proposed direction:** Decide which way to go. If DTO-first: route
`BiomeSurfaceTextureArrays.Build` through a `GrassBiomeTintConfig.From(def)` (and a
sibling `GrassBiomeShapeConfig.From(def)` covering density/height/width/clump/slope/water)
so the GPU pack reads the DTO, never the SO. If the DTO was abandoned: delete
`GrassPlacementDtos.cs` and the `GrassInteractorSnapshot.From` boilerplate to stop
advertising a pattern the codebase doesn't follow.

### GRASS-2 [SPLIT] `GrassPlacementController` is doing too many jobs
- **Category:** Cross-coupling
- **Severity:** 🟠
- **Location:** [GrassPlacementController.cs:1-781](../../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs)
- **Effort to fix:** M
- **Cross-ref:** Perf plan slice 4 (provider split is the model); perf plan slice 5 (grass profiling)

The class is 781 lines and owns: quality-settings resolution, residency selection,
runtime allocation (+50m hysteresis), per-frame Tick + frustum cull, suppression
handshake with near-field, near-field inner-fade negotiation, dispatch parameter
binding (~30 `SetX` calls per chunk), the nested `GrassChunkRuntime` (its own
~130 lines of buffer lifecycle, async readback, render), every readback/stats
field (~25 long counters), and the `IGrassDebugStatsProvider` packing of 60+
fields. It also constructs/destroys its own `Material` and looks up the shader by
name. This is the same ownership-blur shape `ChunkedSurfaceProvider` is being
split for in slice 4.

Two concrete coupling smells beyond size:

1. **Near-field reach-through:** the controller talks directly to
   `IGrassNearFieldStatsProvider` via `ServiceLocator.TryGet` inside both `Tick`
   ([line 275](../../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L275))
   and `ResolveChunkInnerFade` ([line 611](../../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L611)),
   so the chunk path now depends on the near path's `SuppressionRadius`,
   `DrawDistance`, `FullDensityDistance`, and `FadeBand`. That coupling is a
   stats-provider interface, not a real contract; it makes the near and chunk
   layers a knotted pair where changing one requires re-reading the other.
2. **Inline GPU resource class:** `GrassChunkRuntime` is a private nested class but
   is conceptually identical to `GrassNearFieldController`'s buffer triplet
   (blade/args/stats + readbacks). Extracting it (along with the duplicated stats
   constants on the near controller) removes ~250 lines and gives both controllers
   a common test surface.

**Proposed direction:** Split along ownership boundaries:
`GrassChunkResidencyResolver` (residency + allocation reconcile), `GrassChunkDispatcher`
(per-chunk compute setup + render), `GrassChunkRuntime` promoted to its own file
and shared with the near controller, `GrassChunkStatsAggregator` (the readback
accumulators + `IGrassDebugStatsProvider`), and a thin `GrassPlacementController`
orchestrator. Lift the inner-fade negotiation into an explicit
`IGrassLayerHandoff`-style contract instead of "talk to whatever stats provider is
registered."

### GRASS-3 [DEAD] Rejected `GrassMidFieldController` + shader still ship in the build
- **Category:** Style & dead code
- **Severity:** 🟡
- **Location:** [GrassMidFieldController.cs:1-451](../../../Assets/Scripts/Planet/Grass/GrassMidFieldController.cs), [GrassMidField.shader](../../../Assets/Graphics/Shaders/GrassMidField.shader), [Planet.cs:44,210-211,247,519-520,540-548,585-598,638-643,696-707](../../../Assets/Scripts/Planet/Planet.cs#L44)
- **Effort to fix:** S
- **Cross-ref:** [docs/design/2026-06-02-grass-mid-field-layer.md](../../design/2026-06-02-grass-mid-field-layer.md) (superseding decision at top), [docs/design/2026-05-31-grass-renderer.md §"Current LOD architecture — amended 2026-06-07"](../../design/2026-05-31-grass-renderer.md)

Both design docs declare the mid-field card path rejected; `GrassMidFieldController`
disclaims itself in its first comment as "deprecated camera-centered card
experiment ... remove it." `GrassMidField.shader` lives next to `Grass.shader`,
`GrassMidFieldPlace.compute` lives in `Resources/`, and the C# is still wired
through `Planet.Update`, `ConfigureGrassController`, `UpdateGrassControllerActivation`,
`SetGrassEnabled`, `SetGrassLayerEnabled`, `GetGrassRuntimeState`, and the F10
metadata block. `_midFieldGrassEnabled` defaults to `false`, so the controller is
inert at runtime — but every code path that touches grass still pays the
maintenance tax (review the diff, keep the branch logic right, keep the
metadata layout consistent). Both design docs say the right time to delete is "soon."

Two related items live in the same vicinity:

1. **`SmokeRenderer` flag** ([IGrassDebugStatsProvider.cs:5](../../../Assets/Scripts/Core/Interfaces/IGrassDebugStatsProvider.cs#L5), [GrassPlacementController.cs:527](../../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L527)) is hard-coded `false` and emitted to F10 metadata. Smoke-test step from the original design doc; no longer meaningful.
2. **`GrassRenderDiagnostics.GeometryMode`** ([GrassDebugModule.cs:10-80](../../../Assets/Scripts/Core/Services/GrassDebugModule.cs#L10-L80)) ships a Physical/Hybrid/Cluster mode toggle and global `_GrassGeometryMode`/`_GrassClusterStartDistance`/`_GrassClusterEndDistance` plumbed through a console command. It's read from the shader ([Grass.shader:48-50](../../../Assets/Graphics/Shaders/Grass.shader#L48)) but its semantics overlap the now-supported "near → chunk → blanket" stack; worth a hard look on whether it's still desired or another debug-mode legacy.

**Proposed direction:** Delete `GrassMidFieldController.cs`, `GrassMidField.shader`,
`GrassMidFieldPlace.compute`, `IGrassMidFieldStatsProvider`, `GrassMidFieldStats`,
the `GrassRenderLayer.Mid` enum value (or document it as reserved), all `_midFieldGrass*`
plumbing in `Planet.cs`, and the `--- GrassMidField ---` block in
`GrassDebugModule.AppendMidFieldMetadata`. Same pass: drop `SmokeRenderer` from
the stats struct and resolve the cluster-mode question.

### GRASS-4 [HOT] Per-Tick `ServiceLocator.TryGet` in the grass critical path
- **Category:** Per-frame hot path
- **Severity:** 🟡
- **Location:** [GrassPlacementController.cs:275](../../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L275), [GrassPlacementController.cs:615](../../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L615)
- **Effort to fix:** S
- **Cross-ref:** Perf plan slice 5 (grass profiling)

`Tick` calls `ServiceLocator.TryGet<IGrassNearFieldStatsProvider>` every frame to
fetch the suppression radius, and `ResolveChunkInnerFade` does the same a few
lines later. `ServiceLocator` is a dictionary lookup (not free), and the near
controller's lifetime is owned by the same `Planet` instance that owns the chunk
controller — the reference can be passed in once at construction or resolved on
the rare events that actually invalidate it (planet regen, near-field
disposal/recreation).

Same shape, smaller cost: `CameraFollowGrassInteractor.LateUpdate` does
`Camera.allCameras` allocation + linear scan every frame the main camera is null
([CameraFollowGrassInteractor.cs:58-69](../../../Assets/Scripts/Planet/Grass/CameraFollowGrassInteractor.cs#L58-L69))
— this is debug-only, so it's a 🔵, but the throwaway `Camera[]` allocation
inside `LateUpdate` is the wrong pattern to leave lying around as a model.

**Proposed direction:** Inject `IGrassNearFieldStatsProvider` (or, better, the
narrower `GrassNearFieldStats` snapshot via an `IGrassLayerHandoff`) into
`GrassPlacementController` at construction. Either cache the near-field reference
and refresh on near-field lifecycle events, or pass the stats explicitly when
`Planet.Update` ticks the controllers in order. For the camera-follow scan, cache
a static `Camera[1]` scratch buffer or subscribe to
`Camera.onCameraEnabled`/`Disabled` once.

### GRASS-5 [CONV] `LogLevel.Warning` is used for "controller successfully degraded to no-op"
- **Category:** Convention / Style
- **Severity:** 🔵
- **Location:** [GrassPlacementController.cs:164,186,196,200](../../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L164), [GrassNearFieldController.cs:173,184,191,201,207](../../../Assets/Scripts/Planet/Grass/GrassNearFieldController.cs#L173), [GrassMidFieldController.cs:145,156,163,173,179](../../../Assets/Scripts/Planet/Grass/GrassMidFieldController.cs#L145)
- **Effort to fix:** S
- **Cross-ref:** None

Every controller logs `Warning` when the shader isn't found, compute support is
absent, or the biome param buffer hasn't built yet. These are all
"feature-disabled, continue silently" paths in normal builds: a server build with
no graphics, a regen mid-Configure, a developer who removed the Grass shader for
A/B testing. Warning level here means real noise in the console for situations
that aren't actually wrong, which trains the eye to ignore Warning. Reserve
Warning for "something the developer probably wants to fix"; demote these to
`LogLevel.Info` (one-time) or move them behind a `Debug` channel.

**Proposed direction:** Re-tier: `Info` for "feature disabled, here's why";
`Warning` only when the input data was inconsistent (e.g., grass params buffer
present but `count <= 0` despite a registered biome registry).

### GRASS-6 [DEAD] `GrassPlacementClimateBinding` DTO is unreferenced
- **Category:** Style & dead code
- **Severity:** 🔵
- **Location:** [GrassPlacementDtos.cs:48-65](../../../Assets/Scripts/Planet/Grass/GrassPlacementDtos.cs#L48-L65)
- **Effort to fix:** S
- **Cross-ref:** GRASS-1

A grep across the codebase finds no consumers, constructors, or producers. The
matching XML doc-comment says it's "passed to the placement compute as a packed
buffer," but the actual placement path samples face-space biome atlases via
`_BiomeIds_F<n>` / `_BiomeWeights_F<n>` and never references this binding.

**Proposed direction:** Delete with the rest of the GRASS-1 cleanup. Either every
DTO ships or none do.

### GRASS-7 [STYLE] Stats constants and grid math duplicated across the two real grass controllers
- **Category:** Style & dead code (duplication)
- **Severity:** 🔵
- **Location:** [GrassPlacementController.cs:18-34,650](../../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L18-L34), [GrassNearFieldController.cs:23-41](../../../Assets/Scripts/Planet/Grass/GrassNearFieldController.cs#L23-L41), [GrassMidFieldController.cs:11-23](../../../Assets/Scripts/Planet/Grass/GrassMidFieldController.cs#L11-L23)
- **Effort to fix:** S
- **Cross-ref:** GRASS-2

All three controllers redeclare their own `const int StatXyzRejected = N;` block
and their own copies of `BladeStride = sizeof(float) * 12`,
`VerticesPerVisualBlade = 18`, `ClusterCardsPerInstance = 3`, etc. The numbers
are identical between chunk and near-field today, but the inevitable next
"why is overflow off-by-one?" debugging session will reveal a slot mismatch.

**Proposed direction:** Extract a `GrassBladeFormat` static (or a `BladeStrideSpec`
struct) with the stride/vertex constants; move the rejection-reason enum
to a single `GrassStatSlot` enum used by both controllers and the
compute kernels via a shared `.cs.hlsl` include or hand-maintained pair file.

### GRASS-8 [STYLE] Magic literals on grass altitude/transitions live in `Planet.cs`
- **Category:** Style & dead code
- **Severity:** ⚪
- **Location:** [Planet.cs:149-151,210](../../../Assets/Scripts/Planet/Planet.cs#L149-L151), [GrassPlacementController.cs:210](../../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs#L210)
- **Effort to fix:** S
- **Cross-ref:** None

`Planet.cs` carries `NearFieldGrassActivationAltitude = 350f`,
`NearFieldGrassDeactivationAltitude` (with 50m hysteresis),
`MidFieldGrassActivationAltitude`, `CameraRedispatchDistance = 25f`, etc. These
are the per-layer altitude/movement thresholds; they belong on `IGrassQualitySettings`
or on a per-layer settings object so that a future quality preset can tune them
without editing `Planet`.

**Proposed direction:** Lift the altitude thresholds and re-dispatch distance
onto `IGrassQualitySettings` (or a sibling `IGrassLayerActivationSettings`) so
all layer-activation tuning lives in one tier table.

## Cross-cutting themes

1. **The DTO pattern is half-shipped on the very subsystem advertised as its
   showcase** (GRASS-1, GRASS-6). The two grass DTOs compile but the GPU pack
   reads `BiomeDefinition` directly, and the climate DTO has no callers at all.
   This is the most important call-out — getting it right here is what
   establishes the pattern for the other hotspots.

2. **Three grass controllers, one alive, one half-alive, one dead — but the
   sharing surface is informal.** Both real controllers reimplement the same
   buffer triplet, stats constants, blade-format constants, and disposal pattern
   from scratch; the deprecated mid controller pays the same cost but never
   actually runs. The split work in GRASS-2 + the duplicate-extraction in
   GRASS-7 are the same project: pull the common grass-renderer primitives into
   one place before adding more layers (interactors evolution, wind, snow).

## Open questions for Bryan

1. **DTO commitment.** Do you want the grass path to consume `GrassBiomeTintConfig` /
   a new `GrassBiomeShapeConfig` end-to-end (the "first showcase" lives up to its
   billing), or was the DTO experiment for `BiomeDefinition` shelved? If shelved,
   pull `GrassPlacementDtos.cs` out so it doesn't mislead the next contributor.
2. **Mid-field removal timing.** Both design docs say "delete after A/B is done."
   Is the A/B done? If yes, GRASS-3 is a single-pass cleanup. If you want to keep
   it warm a while longer, can we at least excise the dead F10 block and the
   `GrassRenderLayer.Mid` enum value so the runtime surface is honest?
3. **`GrassRenderDiagnostics` geometry mode.** Are Physical/Hybrid/Cluster still
   the way you want to A/B blade representation, or has the "near + chunk +
   blanket" pipeline retired the cluster path?
4. **`GrassTintDryShift`/`GrassTintLushShift`.** Authored on the SO and on the
   DTO, but the GPU pack only carries `Tint` (a single Vector4) — are the moisture
   shifts shipped on another path I'm missing, or are they cargo from the design
   doc that never got plumbed?

## Out-of-scope for this hotspot

- Slice 5 of the active perf plan owns: per-frame grass profiling counters,
  replacing distant 54-vertex tufts with a card representation, and the
  shared/pooled chunk grass buffer experiment. None re-raised here.
- Shader-level audits (cost of vertex-strip generation, `Grass.shader` fragment
  pricing, the GrassMidField shader's billboard math) are out of scope; the
  2026-05-28 shader audit and slice 5 cover that ground.
- Caustics (out of grass scope; no overlap touched).
- Biome registry / Voronoi / `BiomeSettings.cs` audit; this audit only inspects
  the grass-specific fields on `BiomeDefinition`.
- Tests are out of scope (per project stance).
