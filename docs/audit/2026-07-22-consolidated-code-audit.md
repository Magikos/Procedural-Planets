# Audit Summary

**Findings only — no product code changed.** This audit reviews the current
`scatter-placement` working tree at `d39e50f`, validates the four July audit files that
previously lived in `docs/audit/`, and consolidates every still-actionable item here.
Bryan should mark each finding `fix`, `defer`, or `wontfix` before implementation.

The review covered 262 first-party C# files under `Assets/Scripts`, 39 project-owned
shader/compute/HLSL files, relevant settings and package configuration, repository
operations, the current scatter design, and the prior audit evidence. Existing unrelated
changes in `docs/design/2026-07-20-scatter-placement-system.md` and
`docs/plans/2026-07-20-scatter-placement-sp1.md` were preserved.

There are 5 High, 10 Medium, and 6 Low findings. The highest risks are:

1. Rain and optical-depth compute kernels can address elements outside their allocated
   targets.
2. Surface-edit load/save failure handling can replace durable edits with an incomplete
   ledger.
3. Canceled weather generation can leak four GPU textures before ownership transfers.
4. Generated Graphify artifacts consume about 1.70 GiB in the working tree while the
   current graph is stale and still indexes generated Unity content.

The preferred direction is deliberately small: add bounds guards, make resource ownership
explicit, make persistence atomic, delete unused code, and extract only one proven
collaborator from the surface provider. No new framework is recommended.

## What came back clean

- The only `RuntimeInitializeOnLoadMethod` is the sanctioned
  `LoadingManager.CreateInstance`; there are no `DefaultExecutionOrder` attributes.
- First-party scripts contain no `Task.Run`, `async void`, coroutine, or
  `StartCoroutine` lifecycle paths.
- No raw string-based shader-global writes were found; global names continue to route
  through `ShaderGlobalIds`.
- The earlier grass indirect-count overflow rollback, fallback texture bounds clamps,
  weather ping-pong, cloud/weather formula sharing, rain dirty uploads, and console async
  error logging are present in the current tree.
- Sampled EventBus listeners, console registrations, world service ownership, temporary
  screenshot textures, and owned compute/graphics buffers have symmetric teardown. The
  concrete exceptions are called out below.
- No first-party credential or private-key material was found. Caustics were not modified
  or audited beyond incidental references.
- Large files were not flagged for size alone. `WaterMeshBuilder` remains one cohesive
  compute pipeline and `Planet` remains primarily a composition root.

# Findings

## F01 — Rain update dispatch overruns non-aligned particle buffers

**Category:** Bug  
**Severity:** High  
**Description:** The controller allocates exactly the requested particle count but rounds
the compute dispatch up to 64-thread groups. The kernel has no active-count or buffer-bound
guard before reading and writing `id.x`.  
**Evidence:** `Assets/Scripts/Planet/Precipitation/RainParticleController.cs:34` defaults to
30,000 particles; `:190-202` allocates the exact desired capacity; `:314-315` dispatches
`ceil(count / 64)` groups. `Assets/Resources/RainParticleUpdate.compute:122-125` declares
64 threads and immediately reads `_RainParticles[id.x]`; writes occur at `:132` and `:179`.
The default dispatch launches 30,016 threads against 30,000 elements.  
**Impact:** Sixteen threads perform undefined out-of-bounds buffer access every active
default frame. Other non-multiples of 64 have the same defect.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Pass the active particle count and return immediately when
`id.x >= activeCount`; skip dispatch when the active count is zero. Keep draw count and
allocated capacity semantics unchanged.  
**Refactor Option:** None. A two-line kernel guard and one property upload are sufficient.  
**Behavior note:** Preserving; only invalid worker lanes stop executing.

## F02 — Optical-depth bake dispatch overruns arbitrary inspector sizes

**Category:** Bug  
**Severity:** High  
**Description:** `BakeTextureSize` accepts any integer from 64 through 512, but the bake
rounds dispatch dimensions up to 8-thread groups and the kernel never checks the target
bounds.  
**Evidence:** `Assets/Scripts/Planet/Atmosphere/AtmosphereSettings.cs:62` exposes the full
integer range. `Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs:210,239-240`
uses the raw size and `CeilToInt(size / 8f)`. `Assets/Graphics/Shaders/OpticalDepth.compute:
18-21` starts an 8×8 kernel without a guard and writes `_Result[id.xy]` at `:38`, `:55`,
and `:65`. A size of 257 dispatches 264×264 lanes against a 257×257 texture.  
**Impact:** Valid inspector values can cause undefined UAV writes during atmosphere bake.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Add `if (id.x >= _TextureSize || id.y >= _TextureSize) return;` at
the top of the kernel. Authoring validation may warn about non-aligned sizes, but must not
replace the runtime guard.  
**Refactor Option:** None.  
**Behavior note:** Preserving for in-range pixels.

## F03 — Surface-edit recovery can overwrite durable edits

**Category:** Bug  
**Severity:** High  
**Description:** Loading marks the current seed as loaded and clears the in-memory ledger
before IO succeeds. Any read or deserialization failure is caught, leaving an empty ledger
considered authoritative. The next edit then writes empty-plus-one directly over the only
file. Writes are also synchronous, pretty-printed, whole-ledger, and non-atomic.  
**Evidence:** `Assets/Scripts/Planet/Surface/SurfaceEditController.cs:447-464` assigns
`_loadedSeed`, clears `_stamps`, and then calls `File.ReadAllText`; `:487-490` catches the
failure without restoring an unloaded/error state. New edits save immediately at
`:436-444`; `:493-506` serializes the full ledger and calls `File.WriteAllText` on the
destination.  
**Impact:** A transient IO error, corrupt/partial file, or interruption during overwrite
can permanently discard previously durable surface edits. Ledger growth also creates an
unbounded main-thread hitch mechanism.  
**Effort:** M  
**Fix Risk:** MED  
**Confidence:** HIGH  
**Recommendation:** Distinguish `absent`, `loaded`, and `load failed`; only commit
`_loadedSeed` and replace `_stamps` after successful validation. Refuse destructive saves
after load failure. Save an immutable snapshot through a latest-wins Unity `Awaitable`
worker to a temporary file, atomically replace the destination, retain dirty state after
failure, and provide a bounded teardown flush.  
**Refactor Option:** Extract one concrete `SurfaceEditStore` that owns result states,
serialization, atomic replacement, and flush. Do not add a repository interface or job
framework for this single consumer.  
**Behavior note:** Preserving. File recovery behavior improves; stamp ordering and schema
must remain byte-semantically equivalent after a round trip.

## F04 — Canceled weather generation leaks pre-owner textures

**Category:** Bug  
**Severity:** High  
**Description:** Weather generation allocates four render textures before a cancellable
await. The `SphericalWeatherGrid` owner that knows how to dispose them is created only
afterward, so cancellation or setup/dispatch failure has no cleanup path.  
**Evidence:** `Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs:151-161` allocates the
textures; `:195-202` reaches the cancellable next-frame wait; ownership transfers only at
`:207-218`; normal disposal is at `:497-503`. `Assets/Scripts/Planet/WeatherManager.cs:
433-440` cancels prior generation during normal regeneration.  
**Impact:** Rapid regeneration, teardown, or an exception can retain four GPU textures.
At the default 256 resolution the backing is roughly 12 MiB per abandoned generation
(derived estimate, not a profiler measurement).  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Check cancellation before allocation and wrap all local textures in a
`try/finally`, clearing the locals only when ownership successfully transfers.  
**Refactor Option:** None; explicit local ownership is clearer than a new allocation type.  
**Behavior note:** Preserving.

## F05 — Tracked Graphify output is 1.70 GiB, stale, and polluted

**Category:** Maintainability  
**Severity:** High  
**Description:** Generated dated graphs and cache files remain tracked despite current
ignore rules. The active report predates the branch HEAD, and the manifest still includes
Unity-generated directories that `.graphifyignore` is intended to exclude.  
**Evidence:** `git ls-files graphify-out` returns 38 tracked files totaling
1,828,254,851 bytes. It includes six dated `graph.json` snapshots. `.gitignore:109-110`
now ignores cache and dated snapshots, but ignore rules do not untrack existing files.
`graphify-out/GRAPH_REPORT.md:1,13-15` was built from `ec0b1cd2`, not current `d39e50f`.
`graphify-out/manifest.json:1307+` indexes `Library/`; later entries include `Temp/obj/`.
The report claims 488 files and about 10 million words at `:3-5`.  
**Impact:** Clones and checkouts carry generated bulk, graph queries return lower-signal or
stale results, and routine graph changes create noisy diffs.  
**Effort:** S for the current tree  
**Fix Risk:** LOW for untracking generated files; HIGH for any history rewrite  
**Confidence:** HIGH  
**Recommendation:** Untrack dated snapshots and cache, run one clean rebuild honoring
`.graphifyignore`, and retain only the current lightweight artifacts required by the
project workflow. Do not rewrite repository history as part of routine cleanup.  
**Refactor Option:** If historic clone size remains unacceptable, propose a separately
coordinated history rewrite with explicit approval because it changes commit SHAs.  
**Behavior note:** Product behavior is unchanged. A history rewrite would be a
collaboration-affecting operation and is not implicitly approved.

## F06 — Water mesh cancellation does not cancel the heavy work

**Category:** Bug  
**Severity:** Medium  
**Description:** `GenerateAsync` accepts a cancellation token, but its background mesh
calculation and polling loop ignore it until the full result has completed.  
**Evidence:** `Assets/Scripts/Planet/PlanetWaterSurface.cs:98-103` accepts the token;
`:168-178` starts and polls the task with uncancellable `NextFrameAsync`, then checks the
token after completion; `:216-224` does not pass a token to `WaterMeshBuilder.Compute`.
`Assets/Scripts/Planet/WaterMeshBuilder.cs:124-181,540-680` contains the long global and
face-scanning phases without checks.  
**Impact:** Canceling generation or tearing down the world leaves CPU and allocation work
running until the entire build completes. The stale result is not applied, but cancellation
latency violates the method contract.  
**Effort:** M  
**Fix Risk:** MED  
**Confidence:** HIGH  
**Recommendation:** Thread the token through `Compute`, check it between phases and at
bounded face/row/component intervals, and poll with `Awaitable.NextFrameAsync(ct)`. Never
publish partially built `MeshData`.  
**Refactor Option:** Preserve the existing pure compute/apply split; it is already the
right seam.  
**Behavior note:** Preserving. Validate that cancellation leaves the existing mesh intact
and emits no generated event.

## F07 — Old weather readbacks can mutate a replacement cache

**Category:** Bug  
**Severity:** Medium  
**Description:** `WeatherQueryCache.Reset` clears shared fields but cannot invalidate GPU
readback callbacks captured against the old weather grid. Those callbacks can later update
masks and clear `_pending` for a newer request.  
**Evidence:** `Assets/Scripts/Planet/WeatherQueryCache.cs:25-34` resets state; `:52-57`
captures a grid; callbacks at `:60-94` unconditionally mutate `_pending`, samples, and
masks. `Assets/Scripts/Planet/WeatherManager.cs:448-460` replaces the grid and resets the
same cache while old readbacks may still be outstanding.  
**Impact:** Diagnostics can claim current faces are cached when only an orphan grid was
updated, and a stale callback can allow overlapping new-grid requests.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Increment an epoch on reset, capture it per request, and ignore every
callback whose epoch or grid no longer matches. Only the matching request may clear
`_pending`.  
**Refactor Option:** A single integer epoch is sufficient; no request-object hierarchy is
needed.  
**Behavior note:** Preserving.

## F08 — Disabled weather controllers remain eligible for render passes

**Category:** Bug  
**Severity:** Medium  
**Description:** Weather render features define liveness as “not destroyed,” not “enabled
and ready.” A disabled cloud or atmosphere controller can still schedule fullscreen passes;
precipitation may schedule a no-op pass; disabled rain can continue drawing its retained
buffer. The three features also duplicate most camera, debug, controller, and frustum
gating, which makes this state drift easier.  
**Evidence:** `Assets/Scripts/Core/Services/ServiceLocator.cs:379-385` only checks Unity
null/destroyed status. `CloudRenderFeature.cs:33-60,83-109`,
`AtmosphereRenderFeature.cs:28-56,70-90`, and `PrecipitationRenderFeature.cs:30-52,99-123`
use that condition. `PrecipitationController.cs:131-140` and
`RainParticleController.cs:70-72` omit `isActiveAndEnabled`; rain `OnDisable` at `:128-131`
does not clear readiness or release buffers.  
**Impact:** Disabling a behavior does not reliably disable its rendering and can retain
fullscreen or particle cost with stale state.  
**Effort:** S-M  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Put explicit enabled/readiness state on the runtime interfaces and
require it before scheduling each pass. Include `isActiveAndEnabled` in rain readiness.
Then extract only the common pure camera/global/frustum checks; leave feature-specific
debug modes local.  
**Refactor Option:** A small static gate helper is sufficient. Do not introduce a render
feature base-class hierarchy.  
**Behavior note:** Changes the current behavior of manually disabled weather components;
approval should confirm that “disabled means no render” is intended.

## F09 — Disabled grass-clump code eagerly allocates and leaks materials

**Category:** Bug  
**Severity:** Medium  
**Description:** `GrassClumpScatter` is hard-disabled but still loads resources and creates
a runtime material. It has no disposal path, and near-field recreation overwrites the only
reference with another instance.  
**Evidence:** `Assets/Scripts/Planet/Grass/GrassClumpScatter.cs:38,46-73` disables drawing
but eagerly constructs `Runtime Grass Clump`. `Assets/Scripts/Planet/PlanetGrassCoordinator.cs:
196-205` constructs it. Near-field deactivation and coordinator teardown at `:130-142,
257-280,299-318` do not dispose or clear it. The July scatter design moves Synty clumps to
the new placement system.  
**Impact:** World loads and repeated 500/550 m altitude activation can retain unused native
materials, while dead code competes with the active scatter design.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Delete `GrassClumpScatter` and its coordinator wiring now; recover it
from Git only if the approved scatter implementation needs useful logic.  
**Refactor Option:** If temporarily retained, make construction lazy, implement
`IDisposable`, and pair it with every activation/teardown path. Deletion is preferred.  
**Behavior note:** Preserving because `Enabled` is currently constant false.

## F10 — Regrowth replays every surface mask every five seconds

**Category:** Complexity  
**Severity:** Medium  
**Description:** While any stamp is regrowing, the main thread periodically clears and
replays all relevant surface-edit masks, regardless of how few stamps or chunks changed.  
**Evidence:** `Assets/Scripts/Planet/Surface/SurfaceEditController.cs:399-433` performs
full replay every five seconds. `ChunkedSurfaceProvider.cs:494-501,553-580` clears state
and allocates a contribution dictionary; `:703-739` visits chunks and uploads textures.  
**Impact:** High-resolution worlds have a periodic allocation/upload hitch mechanism. The
actual frame cost has not been profiled, so user-visible severity is a hypothesis.  
**Effort:** M for dirty chunks; L for shader-time aging  
**Fix Risk:** MED  
**Confidence:** HIGH on mechanism, MED on measured impact  
**Recommendation:** Instrument replay CPU time, allocations, and texture upload count
first. If it exceeds the agreed budget, update only affected chunks. Consider shader-time
age fading only if it preserves authoritative stamp replay and exact expiry behavior.  
**Refactor Option:** Keep `SurfaceEditStamp` canonical and masks derived. A dirty-chunk set
inside the proposed rasterizer is the smallest likely design.  
**Behavior note:** Potential visual behavior change; require same-seed mask comparisons
before adopting anything beyond dirty-chunk replay.

## F11 — `ChunkedSurfaceProvider` owns a second subsystem

**Category:** Architecture  
**Severity:** Medium  
**Description:** The 1,764-line provider combines chunk generation, LOD/visibility,
residency, ray/radius queries, biome data, mesh orchestration, and a large surface-edit
rasterization/upload workflow. The problem is responsibility coupling, not line count.  
**Evidence:** `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:307-1396` contains
painting, stamp replay, batching, CPU buffers, mask clearing/upload, rasterization, and test
patterns inside the chunk provider. `docs/design/2026-06-12-chunked-surface-provider-restructure.md:
4` records that concrete collaborators previously reduced the same provider substantially.  
**Impact:** Editing changes require navigating and retesting unrelated chunk lifecycle
behavior; resource ownership and regression isolation are difficult.  
**Effort:** L  
**Fix Risk:** MED  
**Confidence:** HIGH  
**Recommendation:** Extract one concrete `SurfaceEditRasterizer` that owns edit buffers,
stroke batching, replay/rasterization, and mask uploads. Keep the provider API stable and
delegate internally. Characterize operation ordering and brush math before moving code.  
**Refactor Option:** Do not add an interface, factory, or generalized editing framework
until a second implementation or real test seam exists.  
**Behavior note:** Preserving; exact mask output and command order are load-bearing.

## F12 — About 811 lines of first-party infrastructure have no product consumer

**Category:** Maintainability  
**Severity:** Medium  
**Description:** Eleven files implement dormant pooling, wake, world-action, mesh-builder,
Poisson-sampling, and automatic event-binding capabilities. Current product code either
never calls them or only constructs/registers an empty subsystem. Future phase documents,
not current behavior, are their primary consumers.  
**Evidence:** Repository-wide reference checks found no consumers for
`Core/Utilities/ObjectPool.cs`, `Core/Utilities/CubeSphereMeshBuilder.cs`,
`PoissonDiscSampling.cs`, or `PoissonDiscSphereSampling.cs`. All auto-binding symbols are
confined to `Core/Events/EventBusAutoBinder.cs:7-204`. `SceneBootstrap.cs:81-87` registers
an action manager with no concrete `IWorldAction` and creates `WaterWakeController`; no
project shader consumes `_WaterWakeCount`, `_WaterWakePositions`, `_WaterWakeDirections`,
or `_WaterWakeParams`, and no serialized emitter reference was found. Future action usage
appears only in `docs/phases/08-phase9-marching-cubes.md`, `10-phase11-resources.md`,
`11-phase12-building.md`, and `13-phase14-multiplayer.md`.  
**Impact:** Dormant types, bootstrap work, reflection machinery, and a second event
subscription convention enlarge the current maintenance surface and imply capabilities the
game does not yet have.  
**Effort:** M in independent deletion batches  
**Fix Risk:** LOW for unreferenced utilities; MED for bootstrap/console surface removal  
**Confidence:** HIGH  
**Recommendation:** Delete the unreferenced utilities and `EventBusAutoBinder` first.
Separately approve removal of the wake and action scaffolding, their bootstrap registration,
console commands, and `.meta` files after a Unity serialized-reference check. Reintroduce a
minimal implementation from Git when a concrete feature needs it.  
**Refactor Option:** None; deletion is the refactor.  
**Behavior note:** Utility/binder deletion is preserving. Removing action console commands
or wake globals changes dormant developer-facing surface and requires explicit approval.

## F13 — Precipitation settings and geometry have multiple owners

**Category:** Architecture  
**Severity:** Medium  
**Description:** Distant precipitation, fog, dust/snow particles, debug controls, and close
rain use inconsistent authoring/runtime paths. Rain inspector state is outside the DTO/save
flow, while cloud-band geometry and fallbacks are duplicated.  
**Evidence:** `Assets/Scripts/Planet/PrecipitationController.cs` is a 501-line authoring and
runtime controller with roughly 50 inspector fields. `PrecipitationDto.cs:3-97` mirrors 47
positional fields. `Precipitation/RainParticleController.cs:30-69` owns separate public
inspector state without a corresponding DTO. The top-radius formula and constants `330`,
`25`, and `45` repeat at `PrecipitationController.cs:182,282-284,311` and
`RainParticleController.cs:143-159`. `PrecipitationController.cs:139-153` exposes the same
local-particle expression twice.  
**Impact:** Save/override behavior is inconsistent and cloud-band formula drift can place
systems at different altitudes. Serialized migration becomes harder as the monolithic DTO
grows.  
**Effort:** L  
**Fix Risk:** MED  
**Confidence:** HIGH  
**Recommendation:** First extract one pure precipitation-band calculation and delete the
duplicate property. Then decide explicitly whether close-rain tuning is saved/runtime
configuration; if yes, migrate existing serialized values into cohesive concrete DTO
slices.  
**Refactor Option:** Compose concrete volume/fog, dust-snow, and close-rain settings only
where consumers actually differ. Avoid factories and per-domain interfaces without a
second implementation.  
**Behavior note:** Formula sharing is preserving. DTO/schema changes require serialized
migration approval and equivalence checks.

## F14 — Loading-transition cancellation has no defined commit boundary

**Category:** Architecture  
**Severity:** Medium  
**Description:** The public transition accepts a cancellation token, but after additive
loading begins most waits do not observe it. A caller can cancel and still have the old
world torn down and replaced. Simply adding cancellation to every await is unsafe because
Unity can be left with a staged scene whose activation is disabled.  
**Evidence:** `Assets/Scripts/Core/Interfaces/ILoadingManager.cs:87-91` exposes the token.
`Assets/Scripts/Core/Services/LoadingManager.cs:161-168` checks it before loading, then
polls without it at `:174-175`, `:187-189`, `:204-205`, and `:211-212`. Initialization at
`:202` uses only the new world's lifetime token. There are currently no external callers
that exercise cancellation.  
**Impact:** The API promises cancellation semantics it does not currently deliver; future
callers can unintentionally complete a destructive world swap after cancellation.  
**Effort:** M-L  
**Fix Risk:** HIGH  
**Confidence:** HIGH on semantics, MED on present impact  
**Recommendation:** Define an explicit transition commit point. Before commit, cancellation
must finish/activate and unload the staged scene while retaining the old world. After
commit, link cancellation only where abort cleanup is proven. If transition cancellation is
not intended, remove or narrow the token contract instead.  
**Refactor Option:** A small staged-transition state object is justified because cleanup
obligations change at the commit boundary.  
**Behavior note:** Behavior is currently ambiguous and must be approved before refactoring.

## F15 — Authoritative project guidance is stale

**Category:** Maintainability  
**Severity:** Medium  
**Description:** Rules and onboarding material point to deleted audits, a past branch,
obsolete initialization status, a nonexistent API, and an outdated skill count.  
**Evidence:** `CLAUDE.md:7` points to deleted `docs/audit/2026-06-code-refactor/`;
`:11` says 16 skills; `:47` names nonexistent `DependencyManager.WhenReady<T>()`.
`.agent-skills/README.md:15` and `.agent-memory/MEMORY.md:19` hardcode the old
`code-refactor` branch. `README.md` contains only its title.  
**Impact:** Agents and contributors can follow invalid workflows or look for APIs and
sources of truth that do not exist.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** In a separately approved rule/documentation change, point to this
consolidated audit, describe the live initialization contract, remove the phantom API and
volatile active-branch claims, update the skill count, and give the root README a minimal
purpose/open/run/navigation section.  
**Refactor Option:** Keep checkout-specific state in dated memory entries, not durable
rules.  
**Behavior note:** Product behavior is unchanged. `CLAUDE.md` edits require explicit
approval under project change control.

## F16 — Two frame loops still resolve world services repeatedly

**Category:** Architecture  
**Severity:** Low  
**Description:** The path painter resolves two services from `Update`, and the precipitation
render feature resolves rain rendering every camera render even though its adjacent
controller path already caches liveness.  
**Evidence:** `Assets/Scripts/Core/Services/SurfacePathMousePainter.cs:90-94,169-175`
performs two `ServiceLocator.TryGet` calls per update.
`Assets/Scripts/Planet/PrecipitationRenderFeature.cs:81-87` resolves
`IRainParticleRenderer` per render; `:99-113` demonstrates the existing cache pattern.  
**Impact:** Small repeated global lookup cost and hidden lifecycle coupling in steady-state
paths. The cost is not measured.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Cache the dependencies and refresh/invalidate them across world
replacement using the existing `ServiceLocator.IsAlive` pattern.  
**Refactor Option:** None.  
**Behavior note:** Preserving.

## F17 — Disabled grass placement branches remain half-wired

**Category:** Complexity  
**Severity:** Low  
**Description:** Suppression and placement-compute frustum culling are permanently disabled
by constants, but their C# and shader plumbing remains. A nearby quality comment also
claims the overlay range derives from the near range even though the coordinator still
uses distinct hard-coded values.  
**Evidence:** `Assets/Scripts/Planet/Grass/GrassNearFieldController.cs:44` fixes
`SuppressionRadiusFraction` at zero; `GrassPlacementController.cs:140-170` retains the
unreachable path. `GrassChunkDispatcher.cs:219` always sets `_FrustumCullEnabled = 0`;
`Assets/Resources/BiomeGrassPlace.compute:56-57,156-169` retains its unused uniforms and
function. `QualityController.cs:38-42` describes one source, while
`PlanetGrassCoordinator.cs:47-48,218-219` applies 24/120 m overlay values independent of
the near 144/200 m values.  
**Impact:** Dead branches and misleading configuration obscure which layer owns coverage
and transition behavior.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Delete suppression statistics/plumbing and the placement-compute
frustum path in separate changes; runtime draw frustum culling must remain. Expose the
overlay range as distinctly named quality values or correct the single-source comment—do
not force the values to match without visual approval.  
**Refactor Option:** Restore the deleted branch from Git if an approved design later needs
it.  
**Behavior note:** Dead-path deletion is preserving. Changing overlay distances is visual
tuning and is not recommended by this audit.

## F18 — Full grass rosters drop release trails

**Category:** Bug  
**Severity:** Low  
**Description:** Active interactors and fading release samples share eight GPU slots, and
active entries are packed first. With eight live interactors, no release trail can upload.  
**Evidence:** `Assets/Scripts/Planet/Grass/GrassInteractorRegistry.cs:170-192` fills the
fixed-capacity buffer with actives before release samples.  
**Impact:** Recovery can pop immediately behind a moving crowd. Current usage is primarily
debug spheres, so the trigger is not representative of shipped gameplay yet.  
**Effort:** S-M  
**Fix Risk:** LOW  
**Confidence:** HIGH on the edge case, MED on current relevance  
**Recommendation:** Do not expand capacity speculatively. When the character/crowd
consumer lands, reserve a measured number of trail slots or split active and release caps,
then capture recovery with a full roster.  
**Refactor Option:** Two fixed slices in the existing buffer are enough; no dynamic
collection architecture is needed.  
**Behavior note:** Any slot rebalance changes crowd interaction visuals and needs capture
approval.

## F19 — `Planet.TryGetSettings` suppresses every exception

**Category:** Maintainability  
**Severity:** Low  
**Description:** A broad empty catch converts any settings failure into “settings absent.”  
**Evidence:** `Assets/Scripts/Planet/Planet.cs:601-617`, especially the empty
`catch (System.Exception)` at `:611-613`.  
**Impact:** Disposed, misregistered, or otherwise broken settings services are hidden,
making lifecycle faults harder to diagnose.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Remove the catch after the existing registration check, or catch only
the known absence/lifecycle exception and log unexpected failures once.  
**Refactor Option:** None.  
**Behavior note:** Preserving for healthy state; failures become visible instead of silent.

## F20 — Seven direct Unity packages are removal candidates

**Category:** Maintainability  
**Severity:** Low  
**Description:** Seven packages are direct dependencies but have no first-party source,
asset, scene, prefab, or test-assembly use found by the audit. Serialized or editor-only
reachability still requires Unity verification, so this is a removal trial, not a claim of
proven dead code.  
**Evidence:** `Packages/manifest.json:3-14` directly lists
`com.unity.ai.navigation`, `com.unity.editorcoroutines`,
`com.unity.multiplayer.center`, `com.unity.nuget.newtonsoft-json`,
`com.unity.sharp-zip-lib`, `com.unity.test-framework`, and `com.unity.timeline`.
Repository searches found no first-party use of their APIs or asset types.  
**Impact:** Unneeded direct packages enlarge resolution/import surface, upgrade constraints,
and dependency exposure.  
**Effort:** S  
**Fix Risk:** LOW-MED  
**Confidence:** MED  
**Recommendation:** Remove one package at a time or in a small batch, let Unity resolve
and import, then verify compile, scenes, inspectors, and editor tooling. Restore immediately
if Unity reveals a serialized/editor dependency. Add no replacement package.  
**Refactor Option:** None.  
**Behavior note:** Expected preserving, but Unity import verification is mandatory before
acceptance.

## F21 — Near-grass allocation and readback churn remain unmeasured

**Category:** Complexity  
**Severity:** Low  
**Description:** Altitude hysteresis disposes and recreates the near-field controller and
its large instance buffer, while each active statistics request constructs two capturing
GPU-readback callbacks. Both mechanisms are real, but neither has a profiler-backed
user-visible cost in this branch.  
**Evidence:** `Assets/Scripts/Core/QualityController.cs:45-47` sets the 500/550 m activation
gates. `Assets/Scripts/Planet/PlanetGrassCoordinator.cs:130-171` creates/disposes the
controller across them. `GrassNearFieldController.cs:45,249` allocates 1.5 million
48-byte blades (about 72 MiB before the smaller supporting buffers).
`GrassNearFieldController.cs:494-513` creates two readback closures per stats request;
`GrassChunkRuntime.cs:91-118` has the same pattern, although chunk grass is disabled.  
**Impact:** Altitude crossings may hitch and readback waves may create small managed
allocations. Keeping the controller resident would instead retain about 72 MiB, so an
unmeasured “fix” can be worse than the current tradeoff.  
**Effort:** S to measure; S-M to change  
**Fix Risk:** LOW for cached delegates, MED for residency policy  
**Confidence:** HIGH on mechanism, LOW on user-visible impact  
**Recommendation:** Capture allocation, main-thread, render-thread, and GC data while
crossing both gates and while forcing repeated readbacks. Cache named delegates only if
they appear in the allocation trace. Change residency only if the crossing hitch exceeds
an agreed budget and the memory trade is accepted.  
**Refactor Option:** None. Cached instance callbacks or one changed lifetime threshold are
enough if measurement fails.  
**Behavior note:** Delegate caching is preserving. Controller residency changes memory
behavior and requires approval.

# Refactoring Plan

This is a proposal, not authorization. Each slice should be independently approved and
landed; preserve the current dirty worktree throughout.

1. **Make GPU bounds deterministic (F01, F02).** Add explicit kernel guards, keep draw and
   bake dimensions unchanged, build Core before Planet, then exercise default and
   deliberately non-aligned counts/sizes in Unity. Confirm no graphics validation errors
   and pixel/particle output is unchanged for valid lanes.
2. **Repair durable state and ownership (F03, F04, F07).** Add explicit surface-store
   result states and atomic replacement; establish pre-await texture cleanup; add a weather
   readback epoch. Validate corrupt/truncated/missing ledgers, forced save failure,
   cancel-before-next-frame regeneration, and rapid grid replacement.
3. **Honor bounded cancellation (F06, F14).** First add cooperative cancellation to the
   already-pure water computation. Treat loading transitions as a separate design decision:
   specify the commit point and cleanup state machine before editing awaits.
4. **Delete before abstracting (F09, F12, F17).** Remove `GrassClumpScatter`, unreferenced
   utilities, `EventBusAutoBinder`, and disabled grass branches in small batches. Check
   scenes/prefabs/meta references and Unity compilation after each. Approve wake/action
   developer-surface removal separately.
5. **Stop repository-generated bulk (F05).** Untrack ignored Graphify archives/cache and
   rebuild a clean current graph. Do not rewrite history. Re-run a project-only query and
   confirm `Library`, `Temp`, vendor, and `graphify-out` do not enter the manifest.
6. **Separate the surface-edit collaborator (F10, F11).** Characterize same-seed stamp
   replay first; move buffer/batch/raster/upload ownership into one concrete
   `SurfaceEditRasterizer`; only then consider dirty-chunk regrowth if timings justify it.
7. **Unify weather contracts narrowly (F08, F13, F16).** Add explicit render readiness,
   share a pure common gate and precipitation-band calculation, cache world references,
   and remove the duplicate property. Defer DTO slicing until a serialized migration is
   designed.
8. **Clean rules and dependencies (F15, F19, F20).** Make the exception visible, update
   authoritative docs with approval, and trial package removals through Unity one at a
   time.
9. **Measure, then decide (F10, F18, F21).** Record surface replay
   CPU/allocation/upload metrics, full-roster grass recovery captures, and near-grass
   altitude/readback profiles. Do not implement shader-time fading, buffer expansion,
   callback caching, or buffer residency changes without a failing counter or capture.

Suggested collaborators are limited to `SurfaceEditStore`, `SurfaceEditRasterizer`, and a
small staged loading-transition state object. Everything else should be a local guard,
helper, cache, or deletion.

# Prior Audit Reconciliation

The statuses below replace the four deleted audit files. `OPEN` and `PARTIAL` items are
represented in the Findings section. `REJECTED` means the observation does not justify a
current change; `SUPERSEDED` means later behavior/design replaced its premise.

## Former `2026-07-01-cloud-rain-audit.md`

| Prior item | Status | Current evidence / destination |
|---|---|---|
| W1 cloud jitter | RESOLVED | Current march jitter/quality path differs from the reported defect; no fresh visual failure evidence. |
| W2 rain gloom/silver lining | RESOLVED | Shared weather gloom helpers now drive cloud and shadow consumers. |
| W3 far-rain readability | RESOLVED | Separate close-rain system and current precipitation visual controls implement the accepted direction. |
| W4 rain-volume fog | RESOLVED | Fog/haze controls are present in precipitation settings and shader plumbing. |
| W5 duplicate close-rain systems | RESOLVED | WeatherParticles retains dust/snow; rain uses `RainParticleController`. |
| W6a production debug sample | RESOLVED | Dynamics sampling is gated/load-bearing in the current cloud path. |
| W6b unconditional rain uploads | RESOLVED | `RainParticleController.cs:256-284` change-checks uploads. |
| W6c event-order radii source | RESOLVED | Current initialization derives radii from settings rather than another listener's global state. |
| W6d execution order | RESOLVED | No `DefaultExecutionOrder` remains. |
| W6e dead precipitation lanes | RESOLVED | Former duplicate rain/profile lanes were removed. |
| W6f WeatherParticles sampling cost | REJECTED | Rain pass was removed; remaining dust/snow work has no measured bottleneck. |
| Directional rain ideas | SUPERSEDED | Optional product/tuning ideas are not audit defects. |

## Former `2026-07-01-grass-lod-audit.md`

| Prior item | Status | Current evidence / destination |
|---|---|---|
| G1 double fade/banding | SUPERSEDED | Current accepted grass uses stochastic thinning plus visual shrink/dim/dither; project memory records the visual result as accepted. |
| G2 albedo/lighting mismatch | REJECTED | The attempted visual change was reverted after testing. |
| G3 overlay remap | REJECTED | The attempted visual change was reverted after testing. |
| G4 independent overlay window | SUPERSEDED | The July layering design intentionally keeps the far blanket as a base layer; misleading ownership wording remains in F17. |
| G5 scattered transition constants | PARTIAL | Most near-field values are centralized; distinct hard-coded overlay values/comment drift remain in F17. |
| G6 promote/delete chunk layer | REJECTED | The current project decision keeps chunk grass disabled; no new evidence justifies reopening the entire layer. |
| G7 altitude pop/allocation | PARTIAL | Visual fade is resolved; the ~72 MiB near buffer still recreates across the 500/550 m gate. F21. |
| G8 corner/suppression notes | PARTIAL | Corner risk is accepted/surfaced by scatter work; dead suppression moves to F17. |
| ARCH-1 execution order | RESOLVED | No attributes remain. |
| PERF-1 frame service/settings lookup | RESOLVED | Coordinator dependencies are cached. |
| PERF-2 per-frame material constants | RESOLVED | Writes are no longer unconditional. |
| DEBT-1 provider cohesion | OPEN | F11. |
| DEBT-2 empty catch | OPEN | F19. |
| DOC-1 deleted audit link | OPEN | F15. |

## Former `2026-07-03-general-code-audit.md`

| Prior item | Status | Current evidence / destination |
|---|---|---|
| G1 precipitation debug lookup | RESOLVED | Nullable consumers use `TryGet`. |
| G2 post-generation weather leak | PARTIAL | Returned-grid disposal is fixed; pre-owner cancellation leak remains in F04. |
| G3 stale weather callback | OPEN | F07. |
| G4 rain count cap | RESOLVED | One `MaxParticleCount` governs the range and setter. |
| G5 execution-order attributes | RESOLVED | Zero remain. |
| G6 per-frame service lookup | OPEN | F16. |
| G7 synchronous surface IO | OPEN | Expanded to durable-loss risk in F03. |
| G8 EventBusAutoBinder | OPEN | Included in dormant deletion F12. |
| G9 obsolete parsers | RESOLVED | Parsers are gone. |
| G10 provider size/cohesion | OPEN | F11. |
| G11 weather settings patterns | OPEN | F13. |
| G12 render gate duplication | OPEN | Combined with readiness defect in F08. |
| G13 duplicated band math | OPEN | F13. |
| G14 duplicate precipitation property | OPEN | F13. |
| G15 full regrowth replay | OPEN | F10. |
| G16 unconditional `OnGUI` | REJECTED | The mechanism is real but no meaningful cost or failure is evidenced; do not optimize it. |
| G17 console async errors | RESOLVED | Runner logs exceptions. |
| G18 stale authoritative docs | OPEN | F15. |
| G19 graph pollution | PARTIAL | Ignore/query behavior improved; tracked bulk and polluted stale output remain in F05. |

## Former `2026-07-03-grass-cloud-line-audit.md`

| Prior item | Status | Current evidence / destination |
|---|---|---|
| A1-A6 correctness set | RESOLVED | Count rollback, shared gloom, debug range, initialization, fallback clamps, and face-area stats are present. |
| B1 cloud dynamics sample | RESOLVED | Sample is gated/load-bearing. |
| B2 per-frame chunk constant | RESOLVED | Constant upload moved out of the frame loop. |
| B3 readback closures | OPEN | F21; measurement is the first action. |
| B4 global placement atomics | REJECTED | Deliberately accepted diagnostics cost; no contrary counter. |
| B5 near buffer recreation | PARTIAL | Visual pop fixed; allocation watch retained in F21. |
| C1 dead camera field | RESOLVED | Removed. |
| C2 dead suppression | OPEN | F17. |
| C3 dead placement frustum | OPEN | F17. |
| C4-C5 dead weather/null paths | RESOLVED | Removed. |
| D1-D5 shared math/ownership cleanup | RESOLVED | Shared includes and current object ownership match the intended fixes. |
| D6 interactor trail starvation | OPEN | F18. |
| D7 stale sizing comments | REJECTED | Historical sizing commentary is cosmetic; the materially misleading live ownership claim is independently covered by F17. |
| D8 bounds magic | RESOLVED | Current code names/owns the padding. |
| D9 grass depth pass | REJECTED | It is an optional rendering tradeoff, not a demonstrated defect. |
| D10 texture arrays | REJECTED | Upstream optimization lacks a measured need. |
| E1-E4 cloud tuning notes | REJECTED | Flag-only visual/perf ideas lack current failing captures or counters. |

# Questions for the User

None are needed to complete the audit. Before implementation, Bryan should mark findings
`fix`, `defer`, or `wontfix`; F08, F12, F14, F15, and any serialized settings migration in
F13 require an explicit behavior or rule decision.
