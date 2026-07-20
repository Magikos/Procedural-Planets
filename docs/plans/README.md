# Visual Systems Audit and Implementation Tracker — 2026-07-11

> **Executor instructions:** Read this document completely before changing code. Bryan
> must mark a finding `FIX` before implementation begins. Work in phase order, preserve
> unrelated dirty-tree changes, run every applicable verification gate, and update only
> the checkboxes supported by evidence. Stop and report when a STOP condition applies;
> do not improvise a wider design.

**Findings only — no code changed.** This document records the audit, candidate fixes,
and an implementation sequence. It does not approve any fix.

## Active tracker

Status: proposed; all findings await Bryan's `FIX / DEFER / WONTFIX` decision.

Current next action: Bryan reviews the findings table, records decisions, and chooses the
first approved phase. Recommended first implementation slice: Phase 1 correctness and
resource ownership, because it is small, high-confidence, and mostly non-visual.

- [ ] Bryan reviewed every finding and recorded a decision.
- [ ] Phase 0 — pin baselines and scope.
- [ ] Phase 1 — correctness and owned-resource cleanup.
- [ ] Phase 2 — renderer lifecycle and player-build hardening.
- [ ] Phase 3 — make diagnostics truthful and reproducible.
- [ ] Phase 4 — retire the god-ray experiment; resume weather-shaped clouds.
- [ ] Phase 5 — remove grass edges and supply a far-field receiver.
- [ ] Phase 6 — correct and validate rain composition.
- [ ] Phase 7 — isolate and repair water artifacts.
- [ ] Phase 8 — make surface edits safe for terrain modification.
- [ ] Phase 9 — take only measured performance wins; delete unused systems.

## Tree and audit scope

- Branch: `code-refactor`.
- Planned at: commit `ec0b1cd`, against the dirty working tree present on 2026-07-11.
- Focus: grass, clouds, god rays, rain, water, terrain/surface edits, renderer lifecycle,
  diagnostics, settings, resource ownership, and adjacent performance/dead code.
- Existing visual plans remain authoritative for their detailed tuning gates:
  [cloud migration](../design/2026-07-04-cloud-visual-migration-plan.md) and
  [grass migration](../design/2026-07-04-grass-visual-migration-plan.md).
- The working tree was already heavily modified before this document. Never reset,
  clean, broadly stash, or overwrite unrelated changes.
- Caustics are flag-only and explicitly out of scope.
- There is no project test framework. Do not introduce or propose one.

Before executing any phase, run:

```powershell
git status --short
git diff --stat ec0b1cd -- <phase in-scope paths>
```

Compare the live symbols with this document. Uncommitted changes are not represented by
the commit SHA; symbol drift is therefore a STOP condition, not permission to guess.

## Findings and decisions

Decision values: `UNREVIEWED | FIX | DEFER | WONTFIX`. All start unreviewed.

| ID | Finding | Impact | Effort | Fix risk | Confidence | Decision |
|---|---|---:|---:|---:|---:|---|
| V01 | Active full-resolution screen-space god rays contradict the retired design and stack with atmosphere shafts | High visual/perf | S delete | Low–Med | High | UNREVIEWED |
| V02 | Clouds still use one bottom×top height envelope, producing uniform slab silhouettes | High visual | M | High | High | UNREVIEWED |
| V03 | Near grass thins in compute and fades/dithers again in the shader while both far receivers ship disabled | High visual | M | Med | High | UNREVIEWED |
| V04 | Grass clump strength is authored and transported but does not drive live blade placement | Med visual | M | Med | High | UNREVIEWED |
| C01 | CPU terrain queries use the legacy cube-face inverse instead of the mesh mapping's exact inverse | High correctness | S | Low–Med | High | UNREVIEWED |
| C02 | Rain compute dispatch can write past the particle buffer for counts not divisible by 64 | High correctness | S | Low | High | UNREVIEWED |
| C03 | Surface-edit saves overwrite the only JSON file directly, so interruption can destroy durable stamps | High data loss | S | Low | High | UNREVIEWED |
| C04 | Paths/scorches are painted only into leaf masks and disappear when a parent terrain LOD renders | High visual/correctness | M | Med | High | UNREVIEWED |
| C05 | Edit-stroke “batching” still uploads each stamp and then rebuilds the full path ledger | High hitch risk | M | Med | High | UNREVIEWED |
| C06 | Runtime water and Low-mode terrain meshes are never explicitly destroyed | High lifecycle | S | Low | High | UNREVIEWED |
| C07 | Cloud/atmosphere render textures are released without destroying their Unity objects | Med lifecycle | S | Low | High | UNREVIEWED |
| C08 | Weather-grid cancellation can strand four render textures before ownership transfer | High lifecycle | S | Low | High | UNREVIEWED |
| C09 | Renderer features can retain inactive controllers from the old world during transitions | High correctness | M | Med | High | UNREVIEWED |
| C10 | Cloud globals can continue referencing destroyed old-world weather/noise textures | High correctness | S | Low | High | UNREVIEWED |
| C11 | Cancelling `planet.generate` can leave a partial planet and normal ticking resumes | High correctness | M | Med | High | UNREVIEWED |
| C12 | Water mesh generation ignores cancellation until the entire worker finishes | Med responsiveness | M | Low | High | UNREVIEWED |
| C13 | F10 captures use `CancellationToken.None` and can outlive their controller/world | Med correctness | S–M | Low | High | UNREVIEWED |
| C14 | Hidden runtime shaders found only with `Shader.Find` may be stripped from player builds | High build risk | S–M | Low | Med | UNREVIEWED |
| R01 | Water-volume code expects a `WaterVolumeLip`, but water generation never creates one | High visual | M | Med | High | UNREVIEWED |
| R02 | Water prepass uses the base mesh while the visible ocean surface is vertex-displaced | High visual | M | High | High | UNREVIEWED |
| R03 | Rain renders before clouds, hard-fades at a fixed camera altitude, and lands at sea radius rather than terrain | High visual | M–L | Med–High | High | UNREVIEWED |
| R04 | Precipitation can enqueue a full-screen copy when no full-screen precipitation is visible | Med perf | S | Low | High | UNREVIEWED |
| T01 | Chunk-boundary normals clamp samples to the local chunk, allowing lighting seams | Med visual | M | Med | High | UNREVIEWED |
| T02 | Mixed-LOD edge snapping exists but is disabled, allowing T-junction cracks | Med visual | L | High | Med | UNREVIEWED |
| T03 | Face-UV brush circles distort and clip across cube faces | High future terrain | M | Med | High | UNREVIEWED |
| T04 | Persisted deformation applied at the current replay point would arrive after climate, water, grass, and meshes are derived | High future terrain | L | High | High | UNREVIEWED |
| D01 | Cloud debug mode has two authorities and can report a mode different from the shader | Med diagnosis | S | Low | High | UNREVIEWED |
| D02 | Capture sets/sidecars omit key rain, cloud-lighting, and live tuning state | Med diagnosis | S–M | Low | High | UNREVIEWED |
| D03 | HUD says `P=Precip`, but `P` arms the persistent path-paint tool | Med UX/data risk | S | Low | High | UNREVIEWED |
| P01 | Uniform-biome terrain fragments repeat identical triplanar PBR sampling four times | Med perf | S–M | Med | Med | UNREVIEWED |
| X01 | Wake upload, unused object pool, speculative world-action stack, and editor-coroutines package have no first-party consumer | Low–Med debt/perf | S | Low | High | UNREVIEWED |

## Evidence and smallest suggested fixes

### Visual systems

**V01 — god rays.** `CloudRenderFeature.cs:66-77` enqueues
`GodRayStreakRenderPass`; `CloudRenderFeature.cs:156` promotes the cloud handoff to
RGBA16; `GodRayStreaks.shader:75-148` performs the screen-space march; and
`Atmosphere.shader:152-215` retains the original shaft/halo. The cloud migration tracker
says the naive screen-space-clearness approach was retired.

Smallest fix: delete the second god-ray pass, shader, globals, constants, commands, and
debug mode. Keep the atmosphere halo. Do not build a replacement until a separately
approved experiment has a predicted cost and capture protocol.

**V02 — cloud silhouettes.** `Cloud.shader:189-191` applies the same bottom/top feather
profile everywhere. New storm captures continue to read as a broad ceiling rather than
weather-specific cloud forms.

Smallest fix: execute only Phase 4.1 of the existing cloud migration plan first—analytic
stratus/cumulus/cumulonimbus vertical profiles driven by existing weather channels. Do
not add curl noise or new simulation state until the profile change passes its time-lapse
exit check.

**V03 — grass edge/far read.** `GrassNearFieldPlace.compute:349-354` stochastically thins
roots; `Grass.shader:218-225,317` fades and dithers them again. Both alternative receivers
are disabled in `PlanetGrassCoordinator.cs:18,21`, leaving bare terrain past the near
field.

Smallest fix: follow the existing grass plan's cheapest option—restore the data-driven
terrain blanket first, slave its handoff to the existing quality distances, then remove
only the redundant near-side darkening/fade proven to cause a ring. Re-enable chunk grass
only if captures show the blanket cannot carry grazing-angle views.

**V04 — uniform grass.** `BiomeDefinition.cs:46` and `GrassPlacementDtos.cs:29-55`
preserve clump strength, but neither live placement compute consumes it.

Smallest fix: after the far-field handoff is stable, use the existing parameter to blend
between independent and clump-coherent blade variation. Keep blade count and instance
stride unchanged unless a capture proves that impossible.

### Correctness, lifecycle, and builds

**C01.** `CoordinateConverter.cs:84-86` identifies the exact inverse, while
`ChunkedSurfaceProvider.cs:293,1576` and `PerFaceSurfaceProvider.cs:116` use the legacy
mapping for terrain queries. Grass altitude gating consumes the result at
`PlanetGrassCoordinator.cs:179`.

Smallest fix: switch only terrain-query callers to `UnitSphereToCubeFaceUvExact`. Leave
intentional legacy biome/diagnostic consumers unchanged.

**C02.** `RainParticleController.cs:314-315` rounds 30,000 particles up to 469 groups,
dispatching 30,016 threads. `RainParticleUpdate.compute:123-125` immediately indexes the
buffer without a count guard.

Smallest fix: upload the active particle count and return when `id.x >= count` before
reading the buffer.

**C03.** `SurfaceEditController.cs:493-505` writes directly to the canonical JSON path.
Saved stamps are explicitly the durable source of truth.

Smallest fix: write a sibling temporary file, flush/close it, then atomically replace or
move it over the canonical file. Preserve the last valid file on any failure.

**C04/C05.** `ChunkedSurfaceProvider.cs:160-177` allocates masks for internal and leaf
nodes, painting calls `ApplyPathWear` per stamp at `:355,694-700`, and
`EndSurfaceStateBatch` at `:775-780` merely clears dictionaries.

Smallest fix: use the existing face atlases as the LOD-independent render source; stop
allocating internal-node edit masks. During a stroke, mutate leaf CPU buffers and collect
dirty leaves, then upload/invalidate grass once per leaf at stroke end. Remove the full
ledger rebuild only after direct-vs-replay checksums match.

**C06/C07.** Owned runtime objects are allocated at `PlanetWaterSurface.cs:134`,
`PerFaceSurfaceProvider.cs:179`, `CloudController.cs:17-18`, and
`AtmosphereController.cs:215`, but their dispose paths omit `Destroy`.

Smallest fix: reuse the established detach + `Release` + `Destroy` ownership pattern in
`ChunkMeshCache` and `SphericalWeatherGrid`; destroy only objects each owner created.

**C08.** `SphericalWeatherGrid.cs:156-200` allocates four textures before a cancellable
await can throw, before a returned grid owns them.

Smallest fix: use one `try/finally` around factory allocation through ownership transfer;
release locals unless transfer completes.

**C09/C10.** `ServiceLocator.cs:379` considers inactive behaviours alive;
`CloudRenderFeature.cs:92-96` and sibling features retain cached controllers on that
basis. `CloudController.OnDisable` does not clear weather texture globals before
`SphericalWeatherGrid.Dispose` destroys them.

Smallest fix: require active/enabled current-world controllers at the shared resolution
boundary, then clear cloud texture globals and set weather resolution to zero during
disable/teardown. Do not add a second service registry.

**C11/C12/C13.** `Planet.cs:190-337` destroys the existing planet before cancellable work
and has no complete partial-state cleanup; `PlanetWaterSurface.cs:168-178` does not pass
the token into the worker; `DebugCapturePipeline.cs:102,111` uses
`CancellationToken.None`.

Smallest fix: add one idempotent partial-generation cleanup leaving a deliberately empty
planet, propagate the existing token between major water-build phases, and link captures
to a controller-lifetime token. Do not build transactional planet staging unless Bryan
requires preserving the old planet after cancellation.

**C14.** Atmosphere, clouds, god rays, water volume, precipitation, rain, and stars use
`Shader.Find`; `ProjectSettings/GraphicsSettings.asset:29-40` does not include them.
Unity notes that shaders reached only this way may be absent from a player build:
<https://docs.unity3d.com/ja/2021.3/ScriptReference/Shader.Find.html>.

Smallest fix: serialize focused shader/material references on the renderer features or a
single existing settings asset. Do not add every variant to Always Included by default.

### Rain and water

**R01/R02.** `WaterVolumeRenderFeature.cs:169-175` searches for `WaterVolumeLip`, but
`PlanetWaterSurface` creates only the main water object/mesh. The prepass draws the base
mesh while the visible ocean shader displaces its vertices.

Smallest fix: first capture `Water Artifact`, `VolumeOnly`, and `SurfaceOnly` from the
actual failing view. If the lip is proven necessary, generate it from existing water
boundary data. Make the prepass share the exact existing displacement helper rather than
copying wave math. Caustics remain untouched.

**R03/R04.** `PrecipitationRenderFeature.cs:139` renders before the cloud pass at
`CloudRenderFeature.cs:124`; particles fade at a fixed band in
`RainParticleController.cs:239-253`; the compute lands at `_SeaRadius` in
`RainParticleUpdate.compute:160-169`. `PrecipitationController.cs:131-140` can also mark
the feature enabled from local particles alone, causing an empty full-screen pass.

Smallest fix: put rain composition after clouds, gate each pass by the work it actually
draws, and use existing terrain radius data for local landing. Keep the distant volume as
the high-altitude fallback instead of hard-cutting all rain at one absolute height.

### Terrain modification and performance

**T01/T02.** `PlanetChunkMeshJob.cs:164` clamps normal neighbors within each chunk.
`PlanetChunkMeshJob.cs:10-65` already contains edge snapping, but
`ChunkSurfaceGenerator.cs:111-127` always supplies `EdgeFanMask = 0`.

Smallest fix: derive normals with a one-sample analytic halo. Treat mixed-LOD stitching
as a later visible-defect fix; reuse `EdgeFanMask` rather than creating permanent mesh
variants.

**T03/T04.** Current stamps are Euclidean circles in face UV, and current generation
builds terrain/grass atlases, climate, and water before edit replay in `Planet.cs`.

Smallest fix: measure brush distance by direction-vector chord distance so one stamp
crosses cube faces without copied-face special cases. For future deformation, load
canonical stamps before terrain density evaluation and derive dependent products once.
For live edits, invalidate only affected leaves plus the kernel halo. Do not create a
generic dependency framework.

**P01.** `PlanetVertexColor.shader:490-542` evaluates four corner biome payloads; each
biome slot performs triplanar albedo/normal/ARM sampling.

Smallest fix: add only a same-sole-biome fast path and retain the current general blend.
Land it only after a pinned before/after Terrain frame-timing measurement shows a win.

### Diagnostics and deletion

**D01-D03.** `CloudDebugModule.cs:61-69` writes the shader global while
`CloudDebugState.cs` keeps separate static state; `DebugCapturePipeline.cs:122-194`
restores only the registry mode. Rain's real proof modes and several cloud-lighting modes
are absent from capture sets/sidecars. `DebugOverlayHud.cs:71` advertises `P=Precip`, but
`SurfacePathMousePainter.cs:102-106` binds `P` to persistent path painting.

Smallest fix: make debug-mode application flow through one authority, add one compact
Rain Diagnostics set plus missing sidecar state, and correct the HUD text. Do not add a
second keyboard binding unless requested.

**X01.** `SceneBootstrap.cs:86-87` creates `WaterWakeController`, which scans and uploads
arrays although no ocean shader consumes the wake globals. `ObjectPool.cs` has no caller;
`IWorldAction`/`WorldActionManager` have no real action; and `Packages/manifest.json:4`
contains editor-coroutines with no first-party use.

Smallest fix: delete these systems/package after focused reference searches remain empty.
Reintroduce a concrete implementation when gameplay actually needs it.

## Implementation phases

### Phase 0 — decisions and reproducible baselines

- [ ] Bryan marks every finding `FIX`, `DEFER`, or `WONTFIX` in the table.
- [ ] Record `planet.seed`, `quality.get`, and saved camera teleports for cloud, grass,
      rain, and water views.
- [ ] Archive current `Cloud Diagnostics` and `Grass` F10 PNGs plus sidecars outside the
      six-run prune window.
- [ ] Capture the actual odd-water viewpoint with `Water Artifact`, `VolumeOnly`, and
      `SurfaceOnly`; if no artifact is visible, stop R01/R02 implementation and report.
- [ ] Record current Frame Timing avg/p95 with a filled 120-frame window.

Exit: baselines, seed, tier, poses, and predicted perf directions are written down before
any pixel-changing code.

### Phase 1 — correctness and owned-resource cleanup

Approved scope: C01, C02, C03, C06, C07, C08.

- [ ] Switch terrain query callers to the exact inverse and run six-face round trips.
- [ ] Guard rain compute buffer access with the uploaded active count.
- [ ] Make surface-edit persistence atomic.
- [ ] Destroy owned meshes and render textures using existing ownership patterns.
- [ ] Protect weather factory allocations through ownership transfer.
- [ ] Build Core then Planet serially; Unity import and fresh play run are clean.

Exit: non-divisible rain counts run without GPU errors; saved edits survive an interrupted
temporary write with the prior canonical file intact; repeated generation does not grow
owned mesh/texture counts.

### Phase 2 — renderer lifecycle and player-build hardening

Approved scope: C09-C14.

- [ ] Render features resolve only active, ready controllers from the current world.
- [ ] Teardown clears destroyed cloud/weather globals.
- [ ] Cancellation leaves an intentionally empty, non-ticking partial planet.
- [ ] Water workers and captures honor existing lifetime tokens.
- [ ] Runtime hidden shaders have explicit serialized inclusion.
- [ ] Produce one Development player build and verify every rendering feature initializes.

Exit: three repeated world transitions and one cancelled regeneration produce no stale
old-world rendering, exceptions, pink/missing shaders, or native-resource growth.

### Phase 3 — truthful diagnostics

Approved scope: D01-D03 plus R04's gating visibility.

- [ ] Route cloud debug changes through one state authority.
- [ ] Add a compact Rain Diagnostics capture set with RainMask, RainDots, and StormDots.
- [ ] Add missing cloud silver-lining/light-shaft state and active live knobs to sidecars.
- [ ] Remove inert water debug modes or implement their distinct shader branch—not both.
- [ ] Correct the `P` HUD text.

Exit: displayed mode, registry mode, sidecar mode, and shader mode agree before and after
F10; a rain-positive capture identifies whether volume, particles, or composition owns an
artifact.

### Phase 4 — clouds and god rays

Approved scope: V01-V02. Detailed tuning remains in the cloud migration plan.

- [ ] Delete the active `GodRayStreaks` experiment and restore the cheaper cloud target.
- [ ] Capture before/after sun halo, horizon, and storm views; Bryan confirms the halo is
      preserved and the broad spokes/haze are gone.
- [ ] Implement only weather-driven vertical profiles from Cloud Phase 4.1.
- [ ] Run the humid → storm → raining time-lapse exit check.

Exit: the second screen-space ray march is absent; atmosphere halo remains; cloud types
are distinguishable in silhouette with HUD off; Bryan signs off on the capture pairs.

### Phase 5 — grass transition and identity

Approved scope: V03-V04. Detailed tuning remains in the grass migration plan.

- [ ] Restore the data-driven far terrain blanket before extending geometry distance.
- [ ] Run `run "Grass Edge Strip Probe"` at the two known worst biome borders.
- [ ] Remove only the redundant fade/darken term demonstrated by the pinned capture.
- [ ] Compare 5 m / 50 m / 150 m and orbit-to-ground sequences.
- [ ] After the handoff is stable, make existing clump strength drive coherent variation.
- [ ] Measure NearGrass avg/p95; keep blade counts unchanged unless separately approved.

Exit: no hard ring, dark band, brightness step, or bare-terrain cutoff; fields read as
tufts rather than uniform fuzz; Bryan signs off.

### Phase 6 — rain

Approved scope: C02, R03-R04.

- [ ] Compose precipitation after clouds.
- [ ] Gate full-screen and local passes independently.
- [ ] Replace sea-radius local landing with existing terrain-radius sampling.
- [ ] Replace the hard altitude disappearance with a handoff to distant precipitation.
- [ ] Capture clear, storm-no-rain, raining-distant, and raining-under-camera cases.

Exit: rain stays under the correct storm, reaches terrain without underground/hovering
streaks, transitions between local and distant representations without a hard cutoff,
and adds no empty full-screen pass.

### Phase 7 — water

Approved scope: R01-R02. Caustics are out of scope.

- [ ] Let Phase 0 isolation choose the owning stage before editing.
- [ ] Add a boundary lip only if the `Water Artifact` evidence proves it is missing.
- [ ] Share existing ocean displacement with the prepass; do not duplicate wave math.
- [ ] Capture surface-only, volume-only, composite, shore, horizon, and underwater pairs.

Exit: the originally pinned artifact is gone without changing caustics; water surface and
volume boundaries agree during camera movement; Bryan signs off.

### Phase 8 — terrain-edit foundation

Approved scope: C03-C05, T01-T04.

- [ ] Make edit masks independent of mesh LOD through existing face atlases.
- [ ] Upload dirty leaves and invalidate grass once per stroke.
- [ ] Verify direct-stroke and replay masks have identical checksums.
- [ ] Evaluate brush distance in spherical direction space across every cube edge/corner.
- [ ] Load persisted deformation before derived generation products.
- [ ] Add a one-sample analytic halo for normals.
- [ ] Defer mixed-LOD stitching until a pinned crack capture exists, unless Bryan marks
      T02 `FIX` now.

Exit: edits survive LOD changes/reload, cross faces at constant world width, and touch
only affected leaves plus the required halo. A loaded deformation yields matching mesh,
normal, grass, climate, and water state.

### Phase 9 — measured performance and deletion

Approved scope: P01, X01.

- [ ] Delete wake upload, unused pool/action scaffolding, and editor-coroutines only after
      `rg` confirms zero first-party consumers.
- [ ] Measure the same-biome shader fast path before/after; keep it only if avg and p95
      improve without image drift.
- [ ] Do not build frustum compaction, generic edit dependencies, transactional planet
      staging, or new visual systems without a measured need and separate approval.

Exit: deleted systems have no consumers, package import is clean, and any retained shader
optimization has an archived numeric and visual comparison.

## Commands and evidence gates

Run builds serially:

```powershell
dotnet build ProceduralPlanets.Core.csproj --no-restore
dotnet build ProceduralPlanets.Planet.csproj --no-restore
git diff --check
```

Expected: exit 0, zero build warnings/errors introduced, and no whitespace errors. Do not
use `Assembly-CSharp.csproj`; it has known stale third-party references.

For any code change:

```powershell
graphify update .
```

Expected: graph update completes. If it hangs, stop it, record the known tooling failure,
and do not treat the hang as a product regression.

For any visual change:

- [ ] Archive before PNGs and sidecars.
- [ ] Use the same seed, quality tier, and saved camera pose after the change.
- [ ] Fresh Unity import and play-mode run have no new errors/warnings.
- [ ] Archive after PNGs and sidecars.
- [ ] Compare sidecars; explain every difference beyond timestamp/timing jitter.
- [ ] Compare Frame Timing avg and p95 when making a performance claim.
- [ ] Bryan reviews the pixel pairs and explicitly signs off.

## Global done criteria

A phase is `DONE` only when all applicable items below are checked:

- [ ] Every implemented finding was marked `FIX` by Bryan first.
- [ ] No out-of-scope or unrelated dirty-tree files were modified.
- [ ] Core and Planet builds pass serially.
- [ ] Unity import and a fresh play-mode run are clean.
- [ ] Runtime behavior is exercised, not inferred from compilation.
- [ ] Pixel-changing work has pinned before/after captures and Bryan's sign-off.
- [ ] Performance claims include filled-window avg and p95 before/after numbers.
- [ ] Player-only shader inclusion changes are proven in a Development build.
- [ ] `graphify update .` completed after code changes, or its known hang was recorded.
- [ ] This tracker's phase and finding status are updated with evidence timestamps.

## STOP conditions

Stop and report instead of widening the work when:

- a referenced symbol no longer matches the audited state;
- an approved fix requires touching an unapproved finding or unrelated dirty-tree work;
- a visual baseline cannot reproduce the reported artifact;
- the water change approaches caustics code;
- a visual constant appears hand-tuned or signed off and no live capture-tuning session is
  authorized;
- a shader optimization has no measurable avg/p95 benefit;
- two reasonable attempts fail an exit check;
- preserving the old planet after cancellation becomes a requirement—transactional
  staging needs a separate decision.

## What came back clean / deliberately excluded

- No first-party credentials, private keys, `.env` secrets, or prompt-injection content
  were found in the audited scope.
- Release console gating is present; direct logging/coroutine/`Task.Run` rule sweeps found
  no new first-party violation in the audited paths.
- Core and Planet code-health builds passed during the audit; this did not prove shaders
  or visuals.
- Caustics were not audited for modification and remain untouched.
- No water-layer capture in the current folder reproduced the reported odd-water view;
  R01/R02 are source-proven but their visual priority awaits Phase 0 isolation.
- A new test framework, generic terrain dependency framework, and speculative volumetric
  god-ray replacement are not recommended.
