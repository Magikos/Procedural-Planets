# General Code Audit — 2026-07-03

Codebase-wide audit, no feature focus. Scope: all 261 C# files (~38.7k lines) under
`Assets/Scripts`, weighted toward code added or changed since the code-refactor arc closed
(`7048c2c`, 2026-06-15) — the path/scorch edit system, weather/precipitation split, console
scripting, and the working-tree session changes. The grass and cloud systems were
line-audited yesterday ([2026-07-03-grass-cloud-line-audit.md](2026-07-03-grass-cloud-line-audit.md));
those findings are not re-listed.

**Findings only — no code changed.** Severity: `BUG`, `RISK`, `PERF`, `DEAD`, `RULE`
(project-rule violation), `ARCH` (structure), `META` (docs/tooling).

---

## What came back clean (mechanical sweeps)

- **Async discipline**: zero coroutines, zero `async void`, zero `Task.Run` in the entire
  codebase. `Awaitable` everywhere, cancellation tokens threaded properly.
- **Shader globals**: zero raw string literals at `Shader.SetGlobal*` — everything routes
  through `ShaderGlobalIds`.
- **Logging**: direct `Debug.Log*` exists only in `UnityLogger` (the sink) and one line in
  `ConsoleScrollback` — migration complete.
- **No TODO/FIXME/HACK markers, no empty catch blocks, no `#if false` blocks.**
- **No import cycles** (graph report).
- Console commands live on their owning services; dirty-flag upload pattern is followed by
  every shader-global writer I read (WeatherManager, PrecipitationController,
  CloudController, RainParticleController).
- `ConsoleAsyncRunner`'s single-pending-async lifecycle (abandon/cancel/shutdown races,
  CTS ownership handoff) is carefully done and correct as far as I can trace it.

---

## Part 1 — Bugs and risks

### G1. BUG — `WeatherManager.PrecipitationDebugControl` throws where every caller expects null
`WeatherManager.cs:117-120`, `WeatherDiagnostics.cs:92-95,218-221,266-269`

```csharp
// Resolved through ServiceLocator (PrecipitationController self-registers in Awake/OnEnable).
// Returns null if no precipitation system is wired up.
internal IPrecipitationDebugControl PrecipitationDebugControl =>
    ServiceLocator.Get<IPrecipitationDebugControl>();   // Get THROWS when absent
```

The comment promises null; `Get<>` throws. All three call sites in `WeatherDiagnostics`
null-check the result — dead checks guarding a live exception. Any scene with weather
diagnostics enabled but no `PrecipitationController` (the Clouds test scene is the obvious
candidate) throws from the diagnostics tick.

**Fix:** `ServiceLocator.TryGet(out IPrecipitationDebugControl c) ? c : null` — one line,
makes the comment and the callers true.

### G2. BUG (leak) — regenerated weather grid leaks its textures if the manager dies mid-generation
`WeatherManager.cs:372-377`

```csharp
var newGrid = await SphericalWeatherGrid.GenerateComputeAsync(WeatherCompute, _settings, seed, linked.Token);

if (this == null) return;      // destroyed during await → newGrid (4 RenderTextures) never disposed
_grid?.Dispose();
_grid = newGrid;
```

`SphericalWeatherGrid` owns four `RenderTexture`s (weather + dynamics ping-pong pairs).
When the component is destroyed between dispatch and resume, the early return abandons them.

**Fix:** `if (this == null) { newGrid.Dispose(); return; }`

### G3. RISK — `WeatherQueryCache` marks faces cached after applying data to a dead grid
`WeatherQueryCache.cs:57-85`

The readback callback captures `grid`; if the grid is regenerated between request and
callback, the data is applied to the orphaned grid object (harmless) but
`_faceMask |= 1 << face` is set on the *cache*, which `Reset()` just cleared for the new
grid. Diagnostics then report a face as cached whose CPU arrays are actually empty.
Low impact (diagnostics accuracy only). **Fix:** capture the grid at request time and
compare against the current grid in the callback before setting the mask — or route the
mask update through the same `grid` reference check that guards `Apply*FaceReadback`.

### G4. BUG (minor) — rain particle count clamps disagree
`RainParticleController.cs:33` vs `RainParticleController.cs:322`

Inspector range is `[0, 100000]`; the `rain-particles.count` console command clamps to
`50000`. One of them is wrong. Pick one cap (a named const) and use it in both.

---

## Part 2 — Project-rule violations

### G5. RULE — two new `[DefaultExecutionOrder]` uses
- `RainParticleController.cs:28` — `[DefaultExecutionOrder(-50)]`
- `SurfacePathMousePainter.cs:5` — `[DefaultExecutionOrder(-100)]`

The boot rule is explicit: no `[DefaultExecutionOrder]`; ordering belongs to the init-phase
system. `GameBootstrap`/`SceneBootstrap` (-9000/-10000) are the known pre-existing ARCH
items; these two are *new since the rule landed*. Neither documents what it must run before.
If the ordering matters (painter's `BlocksCameraLook` read by the camera controller the same
frame; rain compute dispatch before the render feature), say so in a comment **and** move
the dependency to an explicit mechanism (LateUpdate for the painter's flag consumer; the
rain dispatch is naturally ordered by the render pipeline anyway). If ordering doesn't
matter, delete the attributes.

### G6. RULE — per-frame `ServiceLocator.TryGet` in `SurfacePathMousePainter.Update`
`SurfacePathMousePainter.cs:170-177`

```csharp
bool InputAllowed()
{
    if (ServiceLocator.TryGet(out IConsoleService console) && console.IsOpen) return false;
    if (ServiceLocator.TryGet(out IInputMapService input) && !input.GameplayEnabled) return false;
    return true;
}
```

Called every `Update`. The same file already has the correct cached pattern
(`ResolveRaycaster`/`ResolvePathBrush` with `ServiceLocator.IsAlive` invalidation) —
`InputAllowed` just didn't get it. Also: `Keyboard.current.pKey` (line 103) hardcodes the
toggle key outside `IInputMapService`, so rebinding/gameplay-disable rules don't apply to
it consistently. Debug tool, low stakes — but it's the pattern the rule exists to stop.

### G7. RULE — synchronous file IO on the main thread in `SurfaceEditController`
`SurfaceEditController.cs:464,505`

`Save()` (`File.WriteAllText`, pretty-printed JSON of every stamp) runs synchronously on
every immediate paint/erase console command and on every stroke flush; `EnsureLoaded()`
does `File.ReadAllText` on first touch. The stamp list grows without bound as the world
accumulates edits. Rule: expensive one-shot IO goes through
`Awaitable.BackgroundThreadAsync`. Today the file is small; this is the kind of hitch that
appears six months later with a thousand stamps. At minimum: debounce `Save()` (it already
has `_saveDirty` — the machinery is half-built) and write on an interval/quit instead of
per stamp.

---

## Part 3 — Dead code (rules: remove when discovered)

### G8. DEAD — `EventBusAutoBinder.cs` — the entire file (205 lines)
`Assets/Scripts/Core/Events/EventBusAutoBinder.cs`

`HandleEventBusAttribute`, `IAutoEventBind`, `EventBusExtensions.BindEvents/UnbindEvents`,
and the reflection binder have **zero consumers** — every grep hit for these symbols is
inside the file itself. It was touched since the audit-arc close, so it isn't legacy
leftovers; it's an unused capability. All ~40 event listeners in the codebase use direct
`EventBus<T>.Listen/Unlisten`, which is also the more greppable pattern. Delete the file
(or park behind `#if PROJECT_X_EXPERIMENT` with a note, per the dead-code rule — but given
the manual pattern is well-established, deletion is the honest option).

### G9. DEAD — `SurfaceEditController.ParseShape` / `ParseOperation`
`SurfaceEditController.cs:598-616`

Never called. The inverse parse lives in `ChunkedSurfaceProvider.ParseStampShape` (which
*is* used by the rebuild paths). Delete both methods.

---

## Part 4 — Architecture and structure

### G10. ARCH — `ChunkedSurfaceProvider` is the codebase's largest file and just absorbed another subsystem
`ChunkedSurfaceProvider.cs` — 1,764 lines, ~46 public/major methods

Current responsibilities visible in its public surface: chunk generation + LOD ticking,
visibility snapshots, grass residency queries, surface radius sampling, **six brush/paint
entry points, the stamp→wear rasterizer, stroke batching, mask clearing** (roughly lines
307-830 plus helpers — ~500 lines added by the path system), biome-atlas face access,
visible-surface raycasting, color generation, biome rebake, and memory reporting.

The rule is "when you're about to add a new responsibility, split first" — the path system
added one without splitting. The surface-edit block is unusually cohesive and
extraction-ready: it talks to chunks only through `SurfaceStateTexture`/`PathWearTexture`/
`PathWearPixels` and `_biomeAtlas.Update*AtlasRegion`. A `SurfaceEditRasterizer` service
owning `TryPaint*`, `Rebuild*FromStamps`, the batch dictionaries
(`_surfaceStateBatch`, `_strokeContribution`, `_strokeBaseline`), and the clear methods
would drop the provider under ~1,250 lines and give the edit pipeline a home matching its
controller (`SurfaceEditController` already exists as the stamp-ledger half).

### G11. ARCH — three different settings patterns inside one weather subsystem

| Component | Authoring surface | Runtime settings | Console commands mutate |
|---|---|---|---|
| CloudController | `CloudSettings` SO | `CloudDto` | DTO via `SettingsProvider.Update` ✔ |
| PrecipitationController | **the MonoBehaviour's inspector fields** | `PrecipitationDto.From(this)` | DTO ✔ |
| RainParticleController | the MonoBehaviour's inspector fields | **none** | **raw MB fields** |

The canonical pattern (SO authoring → DTO runtime) is only fully implemented by clouds.
`PrecipitationController` uses the MB itself as the SO-equivalent (with an `OnValidate` →
`SettingsProvider.Update` bridge — workable, but a scene object now owns durable settings).
`RainParticleController` has no DTO at all: its console commands write inspector fields
directly, so rain tuning is invisible to the settings service, save/override plumbing
(`WorldSettingsOverride<TDto>`), and `SettingsChangedEvent` consumers. When rain tuning
stabilizes, promote it to a `RainParticleDto` (and decide whether precipitation should get
a real SO). Until then this is recorded drift, not urgent work.

### G12. ARCH — weather render-feature gate boilerplate is triplicated
`CloudRenderFeature.cs:29-61`, `PrecipitationRenderFeature.cs:30-88`, and the atmosphere
feature carry near-identical blocks: Preview/Reflection camera check, `_WaterFocusMode`
global, `_DebugSuppressWeatherPasses` global, ocean-debug-mode suppression,
`IsPlanetInFrustum` (each with its own `static Plane[6]`), and the
cached-controller-`IsAlive` dance. Three copies already drifted once (each has its own
`DebugModeConstants.PerformanceWeatherIncludes*` variant — that part is legitimately
per-feature). Extract the common gate:

```csharp
static class WeatherPassGate
{
    public static bool ShouldRender(Camera camera)   // camera-type + globals + frustum
    ...
}
```

and keep only the per-feature debug-mode check local. Also normalize the `static
MaterialPropertyBlock` initialization: `CloudRenderPass`/`PrecipitationRenderPass` assign a
static from an instance constructor (re-created per renderer rebuild), while
`RainParticlesAfterPostPass` uses `??=`. Make them instance fields — nothing about them
needs to be static.

### G13. ARCH — duplicated cloud-band math and magic fallbacks in the rain path
- `RainParticleController.OnPlanetGenerated` (lines 145-158) re-derives
  `_cloudBottomRadius` with the same formula as
  `PrecipitationController.EnsureStaticPropertiesUploaded` (line 283) — the comment admits
  it ("Same formula PrecipitationController uses"). Duplicated formula = the next tuning
  change breaks one of them.
- The `330f` cloud-base fallback literal appears three times
  (`PrecipitationController.cs:177,281,310`) plus twice in `RainParticleController` —
  it's silently duplicating `CloudSettings.BaseAltitude`'s default. One
  `CloudConstants.DefaultBaseAltitude` (or requiring the DTO) kills all five.

### G14. ARCH (minor) — duplicate property in `PrecipitationController`
`PrecipitationController.cs:138-139` (`IsLocalParticleSystemEnabled`) and `147-148`
(`LocalPrecipitationParticlesEnabled`) are the same expression, one private one public.
Keep the interface one, delete the other.

---

## Part 5 — Performance

### G15. PERF — path regrowth rebuilds every chunk's masks every 5 seconds
`SurfaceEditController.TickRegrowth` → `ReplayStamps(clearFirst: true)` →
`ChunkedSurfaceProvider.ClearPathWearMasks` (`:703-721`) + `ClearSurfaceStateOnly` +
`RebuildPathWearFromStamps` (`:494-551`)

While *any* stamp has `regrowSeconds > 0`, every refresh tick:
- uploads `EmptyPathWearPixels` to **every chunk's** wear texture (`SetPixelData` + `Apply`
  + atlas region blit), with no "already clear" skip,
- clears and re-uploads every surface-state texture the same way,
- allocates a fresh `Dictionary<PlanetChunk, byte[]>` plus per-chunk byte arrays and
  re-rasterizes every active stamp on the CPU.

The `ponytail:` comment in `TickRegrowth` correctly names this a coarse debug-grade
approach. Recording the ceiling and the two-step upgrade path so it's a decision, not a
surprise: (1) cheap: skip clear/upload for chunks whose wear pixels are already all-zero
and reuse a pooled contribution dictionary; (2) real: move regrow fade to the shader
(bake stamp age into the mask's second channel or a per-stamp buffer) so the CPU only
rebuilds when a stamp *expires*, not every 5s while one exists.

### G16. PERF (minor) — `WeatherManager.OnGUI` exists unconditionally
`WeatherManager.cs:390` — `void OnGUI() => _diagnostics.DrawOverlay();`

A defined `OnGUI` makes Unity run the IMGUI event loop for the behaviour every frame
(multiple invocations per frame) even though `DrawOverlay` early-outs when diagnostics are
off. Cheap but nonzero, and it's on the only always-alive weather object. Standard fix:
`useGUILayout = false` plus early-out, or toggle `enabled` on a tiny dedicated overlay
component only while `ShowWeatherDiagnostics` is true.

### G17. PERF (minor) — console command errors never reach the logger
`ConsoleAsyncRunner.ObservePending` (`:246-249`) catches all exceptions and prints
`ex.Message` to the scrollback only — stack traces are lost and nothing lands in the
`ILogger` file sink. One `LoggerProvider.Get().LogException("Console", ex)` alongside the
scrollback line makes async command failures diagnosable after the fact. (Not perf —
observability; filed here to keep the list flat.)

---

## Part 6 — Meta / tooling

### G18. META — CLAUDE.md points at documents that no longer exist
`CLAUDE.md` says "The 2026-06-10 audit lives at `docs/audit/2026-06-code-refactor/`" —
that directory is gone (only the three 2026-07 audit docs exist under `docs/audit/`).
Agents told to "cross-reference baseline audit findings" cannot. Either restore the arc's
findings doc or update CLAUDE.md to state the arc closed with findings resolved and point
at the git history.

### G19. META — the knowledge graph indexes vendored code, drowning project signal
`graphify-out/GRAPH_REPORT.md`: corpus is 24,665 files / 251M words because it ingests
`Library/PackageCache`, `local-only/AssetRipper_export_*`, and demo repos. Consequence:
the "God Nodes" list is `RAIL_API_PINVOKE`, `DllImport`, `IntPtr` — pure vendor noise —
and "Surprising Connections" links Unity package internals to AssetRipper dumps. An
ignore/exclude for `Library/`, `local-only/`, and third-party demo folders would make the
god-node and community sections reflect *this* codebase (where the actual answer —
`ChunkedSurfaceProvider`, `ServiceLocator`, `Planet` — is currently invisible).

---

## Watch list (no action, size/shape awareness)

Largest files after `ChunkedSurfaceProvider` (G10): `WaterMeshBuilder` (849),
`GrassNearFieldController` (687, audited yesterday), `SurfaceEditController` (645 — fine
internally; shrinks further if G9 lands), `BiomeAtlasService` (641), `Planet.cs` (634),
`ScaleReferenceMarkers` (618), `VoronoiBiomeField` (591), `DebugRegistry` (589). None are
over the line on cohesion from what I sampled, but any new responsibility aimed at
`WaterMeshBuilder` or `Planet.cs` should trigger the split-first rule.

---

## Suggested fix order (once approved)

| # | Finding | Effort | Risk |
|---|---------|--------|------|
| 1 | G1 TryGet in PrecipitationDebugControl | 1 line | none |
| 2 | G2 dispose leaked grid | 1 line | none |
| 3 | G4 count clamp unify | 2 lines | none |
| 4 | G8 delete EventBusAutoBinder | −205 lines | verify no reflection-only use (none found) |
| 5 | G9 delete dead parsers | −19 lines | none |
| 6 | G6 cache InputAllowed services | ~10 lines | none |
| 7 | G17 log console exceptions | 2 lines | none |
| 8 | G13 shared cloud-band const/helper | ~15 lines | none |
| 9 | G14 duplicate property | −2 lines | none |
| 10 | G5 execution-order attributes | investigate each, then delete/justify | needs play-test |
| 11 | G7 debounced/background stamp save | ~20 lines | save-loss edge cases — design first |
| 12 | G12 WeatherPassGate extraction | ~60 lines moved | render-order regression check |
| 13 | G16 OnGUI gating | ~5 lines | none |
| 14 | G3 query-cache mask accuracy | ~10 lines | none |
| 15 | G15 regrowth cost | Bryan picks ceiling vs upgrade | visual check |
| 16 | G10 SurfaceEditRasterizer extraction | large, mechanical | needs its own pass |
| 17 | G11 settings unification | design decision first | — |
| 18 | G18/G19 doc + graphify config | config only | none |

G1 and G2 are the only things I'd call live defects; everything else is drift control.

---

## Codex feedback

Reviewed against the current dirty working tree on 2026-07-03, after the weather-grid
export diagnostics were added. I agree with the main ordering: G1/G2 are the live defects,
then the small rule/dead-code fixes, then the larger architecture work.

Amendments before implementation:

- **G1 is broader than diagnostics now.** `WeatherManager.PrecipitationDebugControl` still
  calls `ServiceLocator.Get<IPrecipitationDebugControl>()`, so the new
  `weather.export-grid` path can also throw in scenes without a precipitation controller.
  The one-line `TryGet` property fix should happen before more weather diagnostics work.
- **G2 should dispose on every abandoned new-grid path.** The audited `if (this == null)
  return;` leak is still present. If cancellation or teardown grows more branches later,
  keep the ownership rule simple: once `newGrid` is allocated, either assign it to `_grid`
  or dispose it before leaving the method.
- **G3 remains valid, but the fix must cover both masks.** This tree now tracks dynamics
  readback completion separately, but stale callbacks can still mark `_faceMask` and
  `_dynamicsFaceMask` after `Reset()` if they belong to an old grid. The forced readback in
  `weather.export-grid` avoids this for exports; normal diagnostics can still lie.
- **G7 needs snapshot discipline.** I agree with debouncing/background file IO, but do not
  let a background write read live mutable stamp state. Snapshot/copy on the main thread,
  then write that snapshot off-thread; flush synchronously only on teardown if needed.
- **G10 should wait until G8/G9 are deleted.** The `ChunkedSurfaceProvider` split is real,
  but first remove the dead event binder and dead parsers so the extraction target is not
  padded by stale code.
- **G19 is an operational blocker, not just meta.** `graphify query` and `graphify update`
  both hang in this checkout with no output. Excluding `Library/`, `local-only/`, and
  generated `graphify-out/` history should be treated as fixing project tooling, not polish.

No disagreement with G4-G6, G8-G9, G13, G16, or G17. G11/G12 are valid drift-control items,
but I would not take them before the one-line defects and dead-code deletions.
