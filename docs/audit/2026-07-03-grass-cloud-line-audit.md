# Grass + Cloud Line-Level Re-Audit — 2026-07-03

Full line-by-line validation of both systems as they exist in the current working tree
(branch `code-refactor`, dirty on top of `ec0b1cd`). This tree includes Bryan's post-review
state: the cloud temporal-accumulation experiment is reverted (single-pass march kept, pass
ordering + per-step jitter changes retained), the grass blanket layer is disabled
(`_grassBlanketEnabled = false`) with `PlanetVertexColor.shader` reverted to HEAD, and the
Grass.shader uniform-driven ramps + IGN dither are retained.

**Findings only — no code changed.** Severity: `BUG` (wrong behavior), `RISK` (latent
failure), `PERF`, `DEAD` (dead code, per project rules removed when discovered),
`STYLE/BP` (best practice), `SUGG` (optional improvement).

Files covered: all 14 `Assets/Scripts/Planet/Grass/*.cs`, `PlanetGrassCoordinator.cs`,
grass parts of `QualityController.cs`, `Grass.shader`, `GrassColor/GrassDither/GrassInteractors.hlsl`,
`BiomeGrassPlace.compute`, `GrassNearFieldPlace.compute`; all 8 `Assets/Scripts/Planet/Clouds/*.cs`,
`Cloud.shader`, `CloudShadows.hlsl`, `WeatherSampling.hlsl`, `CloudNoise.compute`,
`WeatherEvolution.compute`, plus the grass/cloud touchpoints in `Planet.cs`.

---

## Part 1 — Bugs and correctness

### A1. BUG — chunk grass overflow inflates the indirect draw count
`BiomeGrassPlace.compute:477-483`

```hlsl
uint slot;
InterlockedAdd(_GrassDrawArgs[1], 1u, slot);
if (slot >= (uint)_MaxBladeInstances)
{
    AddStat(STAT_OVERFLOW_REJECTED_BLADES, 1u);
    return; // buffer full - quit the whole lane
}
```

When the chunk buffer fills, every overflowing thread still leaves its increment in
`_GrassDrawArgs[1]`, which is the instance count consumed directly by
`Graphics.RenderPrimitivesIndirect`. The draw then renders `capacity + overflow` instances;
instances past capacity read out-of-bounds from the blade buffer (returns 0 on DX11 →
degenerate zero-size blades at the world origin, wasted vertex work; undefined on other APIs).

The near-field compute already solved this exact problem —
`GrassNearFieldPlace.compute:571-578` rolls the counter back:

```hlsl
InterlockedAdd(_NearFieldDrawArgs[1], 1u, slot);
if (slot >= (uint)_NearFieldCapacity)
{
    AddStat(NF_STAT_OVERFLOW, 1u);
    // Roll back so the indirect args count stays accurate (capacity-clamped).
    InterlockedAdd(_NearFieldDrawArgs[1], 0xFFFFFFFFu);
    return;
}
```

**Fix:** copy the rollback line into `BiomeGrassPlace.compute` before the `return`.
One line; the concurrent-rollback math converges to exactly `capacity` (N adds, N−cap
rollbacks). Low risk.

### A2. BUG — sky gloom and ground-shadow gloom use different formulas, and the comment claims they match
`Cloud.shader:358-359` vs `CloudShadows.hlsl:80-84`

Cloud.shader (what the sky shows):

```hlsl
float rainRate = weatherRainRate;             // = dynamics.b × storm-gate (CloudPrecipitationSignal)
float gloom = max(cloud.storm, rainRate);
```

CloudShadows.hlsl (what the ground shows):

```hlsl
// Same gloom term as Cloud.shader: rain-heavy cells darken the ground under them ...
float rainRate = saturate(SampleCloudShadowRainRate(direction));   // raw dynamics.b, NO storm gate
float gloom = max(weather.g, smoothstep(0.12, 0.6, rainRate));     // steepened response
```

Two divergences: the shadow path applies `smoothstep(0.12, 0.6, …)` steepening that the sky
path lacks, and the sky path gates rain by storm (`_PrecipitationParams.y/.z`) while the
shadow path uses raw rain rate. Result: a moderately-rainy, low-storm cell darkens the
ground more than the cloud above it — the exact "rain clouds don't look darker" complaint,
now inconsistent between sky and surface. The `// Same gloom term as Cloud.shader` comment
is false and is exactly the change-history-drift the comment rules exist to prevent.

**Fix (pick one formula, apply to both):** if the steepened response is the keeper, mirror
it in Cloud.shader:

```hlsl
// Cloud.shader, inside the density branch:
float gloom = max(cloud.storm, smoothstep(0.12, 0.6, rainRate));
```

or, if the gated-linear form is the keeper, remove the smoothstep from CloudShadows and gate
its rain sample the same way. Either way, delete or correct the comment.

### A3. BUG — `cloud.debug-mode` cannot select mode 9 (WeatherPrecipitationSignal)
`CloudDebugState.cs:3-14`, `Cloud.shader:430-433,447-448`, `CloudDebugModule.cs:25`

The shader implements debug mode 9 and `CloudDebugModule` registers it
(`WeatherPrecipitationSignal = 9`), but the `CloudDebugState.View` enum stops at
`PrecipitationSignal = 8`. The console command `cloud.debug-mode` parses that enum, so mode
9 is only reachable through the generic debug-module path, not the cloud command that owns it.

**Fix:** add `WeatherPrecipitationSignal = 9` to the enum. Also note the naming skew:
enum says `PrecipitationSignal` for 8; the module calls 8 `CloudPrecipitationSignal`.
Aligning names while touching it would prevent the next confusion.

### A4. RISK — `CloudController.Initialize` hard-resolves a service in `Start()`
`CloudController.cs:87-90,136-140`

```csharp
void Start() { Initialize(); }
void Initialize()
{
    if (_weather == null)
        _weather = ServiceLocator.Get<IWeatherConfigurator>();   // throws if absent
}
```

`Get<>` throws when the service is missing. `Start()` runs on scene-object lifecycle, not
the loading-phase graph, so this silently depends on `WeatherManager` having registered
before the CloudController's `Start` — a boot-order coupling of exactly the kind the
init-phase rules exist to remove. If it ever fires the controller is dead for the session
(exception escapes `Start`, `Update` then runs with `_weather == null` forever — which the
`Update` path actually tolerates, so the crash gains nothing over a `TryGet`).

**Fix:** `ServiceLocator.TryGet(out _weather)` in `Initialize()` and let the existing
`_weather != null` checks in `UpdatePerFrameProperties` do their job; re-attempt resolve in
`Update` while null (single field check per frame), or resolve on `WorldReadyEvent`.

### A5. RISK — 1×1 fallback radius texture can be read out of bounds
`GrassNearFieldPlace.compute:196-207` (`SampleSurfaceRadius`), `BiomeGrassPlace.compute:169-181` (`LoadRadius`)

```hlsl
float2 p = saturate(uv) * max(resolution - 1, 1);
int2 p0 = int2(floor(p));                    // NOT clamped
int2 p1 = min(p0 + 1, int2(resolution - 1, resolution - 1));
```

For `resolution == 1` (the 1×1 fallback radius texture bound when atlases are missing, with
`_…AtlasResolution` clamped to 1 on the C# side), `p` spans `[0,1]`, so `p0` can be 1 while
the only valid texel is 0. The OOB `Load` returns 0 → `surfaceRadius = 0` → blade placed at
the planet center. Only reachable in the degraded no-atlas state, but that's precisely the
state the fallback exists to keep sane. `SampleSurfaceState` in the same file already clamps
`p0`; the radius samplers just don't.

**Fix:** add the same clamp: `int2 p0 = clamp(int2(floor(p)), int2(0,0), int2(resolution-1, resolution-1));`

### A6. BUG (stats only) — off-face near-field cells counted as "distance rejected"
`GrassNearFieldPlace.compute:464-470`

Cells whose jittered UV lands outside the face square increment
`NF_STAT_DISTANCE_REJECTED`, polluting the distance-reject counter the debug module reports.
Give them their own counter or fold them into `NF_STAT_FACE_AREA_REJECTED` (they are a
face-domain rejection, not a distance one). Matters because these stats are the primary
tool used to debug placement (as the strip-probe work showed).

---

## Part 2 — Performance

### B1. PERF — Cloud.shader samples the dynamics map every march step, even in empty air
`Cloud.shader:338-343`

```hlsl
float3 sampleNormal = normalize(jitteredSamplePos - _CloudPlanetCenter);
float weatherRainRate = CloudPrecipitationSignal(sampleNormal, cloud.storm);  // texture sample
debugWeather = max(debugWeather, cloud.condensation);
...
```

`CloudPrecipitationSignal` → `SampleDynamics` is a `Texture2DArray` sample executed for
every one of up to 96 view steps per pixel, fullscreen, regardless of whether the step hit
any cloud (`density > 0.0001`) and regardless of debug mode. In the release path its result
(`gloom`) is only consumed inside the density branch. The debug accumulators
(`debugWeather*`, ~10 `max()` chains) are cheap ALU, but the texture read is not.

**Fix:** move the `CloudPrecipitationSignal` call inside the `if (cloud.density > 0.0001)`
branch. Debug mode 9 wants the ungated per-step value; keep that under
`if (_CloudDebugMode > 0)` so only debug pays for it:

```hlsl
if (cloud.density > 0.0001)
{
    float rainRate = CloudPrecipitationSignal(sampleNormal, cloud.storm);
    ...
}
if (_CloudDebugMode > 0)   // uniform branch, uniform across the wavefront
{
    float dbgRain = CloudPrecipitationSignal(sampleNormal, cloud.storm);
    debugWeatherRainRate = max(debugWeatherRainRate, dbgRain);
    ...
}
```

Saves one texarray sample × steps × resolution on every clear-sky pixel. Rays through
clear sky are the common case at altitude.

### B2. PERF — per-chunk MPB float set with a compile-time constant, every frame
`GrassChunkRuntime.cs:126-131`

```csharp
public void Render(Material material, Camera camera, int layer)
{
    _props.SetFloat(ChunkFadeId, ChunkPeakCoverage);   // const 0.42f, every chunk, every frame
```

Set it once in `Create()` next to the existing `props.SetBuffer(...)`. Also a findability
problem: the chunk layer's peak coverage constant lives here while its distance-band
constants (`ChunkFadeInStart = 128`, `ChunkPeakDistance = 220`) live in
`GrassPlacementController.cs:9-10`. One home for the chunk-fade tuning trio.

### B3. PERF — AsyncGPUReadback closures allocate per dispatch
`GrassChunkRuntime.cs:95-118`, `GrassNearFieldController.cs:491-506`

Each `RequestReadbacks()` allocates two closure objects + delegates. The chunk path calls
this per chunk on every 25m-camera-move redispatch (`RedispatchAll`) — dozens of small GC
allocations per redispatch wave. Not a frame-time problem; it is steady GC pressure.
If it shows in profiling: cache the delegate per runtime instance
(`Action<AsyncGPUReadbackRequest>` field assigned once in the constructor).

Related best-practice note: the project rule says to wrap non-`Awaitable` async surfaces in
`Awaitable` extensions. For these per-dispatch fire-and-forget readbacks the callback form
is arguably the right tool (no continuation logic, no cancellation need); flagging for a
deliberate exception rather than silent drift.

### B4. PERF (accepted, documented) — global atomic stats in both placement computes
`BiomeGrassPlace.compute:84-87`, `GrassNearFieldPlace.compute:124-127`

Every candidate lane/cell does 1-4 `InterlockedAdd`s on single global addresses (~326k
threads per near-field page dispatch). GPU atomics on one address serialize. Dispatches are
rare (page shifts / 25m moves), so this is acceptable — but it is the first thing to gate
behind a keyword (`#pragma multi_compile _ GRASS_STATS`) if placement dispatch cost ever
shows up in captures, since the stats feed only the debug module.

### B5. PERF/SUGG — near-field controller rebuilds 48 MB of GPU buffers on every altitude-gate crossing
`GrassNearFieldController.cs:242-248`, `PlanetGrassCoordinator.cs:127-139`

Crossing 500m/550m altitude disposes and reconstructs the controller: a 1M-instance
48 MB `GraphicsBuffer`, args/stats buffers, fallback textures, `Shader.Find`, and a full
first dispatch. The 50m hysteresis band prevents rapid thrash, and the altitude fade hides
the pop, so this is fine today. If a gameplay loop ever hovers around that band (a flying
mount circling at ~525m), consider keeping the controller alive with rendering suppressed
instead of full teardown. Flag-only.

---

## Part 3 — Dead code (rules say: remove when discovered)

### C1. DEAD — `GrassPlacementController._lastTickCamera`
`GrassPlacementController.cs:46,116` — written every Tick, never read. Delete both lines.

### C2. DEAD — chunk suppression path can never fire
`GrassNearFieldController.cs:43` sets `SuppressionRadiusFraction = 0f`, so
`SuppressionRadius` is always 0, so `GrassPlacementController.cs:142-174`'s
`suppressionRadiusSq > 0` branch (`suppress`, `OldChunkSuppressedCount`, the
`TransformPoint` per chunk) is unreachable. Either the fraction is a real tuning knob that
deserves a nonzero value, or the mechanism was superseded by per-root thinning (the comment
at `GrassNearFieldController.cs:41-43` says exactly that) and the whole suppress path +
stat + `IGrassNearFieldStatsProvider.SuppressionRadius` plumbing should go.

### C3. DEAD — frustum-cull path in BiomeGrassPlace is permanently off and half-wired
`BiomeGrassPlace.compute:70-71,311-325`, `GrassChunkDispatcher.cs:216-219`,
`GrassPlacementController.cs:303`

C# always sets `_FrustumCullEnabled = 0` (with a good comment explaining why), never uploads
`_CameraFrustumPlanes`, and hardcodes `PlacementFrustumCullEnabled = false` in the debug
stats. If cull were ever switched on it would read garbage planes. Delete `PassesFrustum`,
the planes uniform, the stat, or park it behind an `#if`-style keyword per the dead-code rule.

### C4. DEAD — `SphericalWeatherGrid.EdgeSnappedUv`
`SphericalWeatherGrid.cs:512-517` — no callers since weather init moved to the GPU
(`CSInitWeather` has its own copy at `WeatherEvolution.compute:95-100`, which is the live
one). Delete the C# copy; its explanatory comment already lives in the compute.

### C5. DEAD-ish — redundant null check
`GrassPlacementController.cs:161`: `chunk != null` after `chunk.DetailLevel` was already
dereferenced at line 154. The check can never be false without having thrown earlier.
Remove it (or move it above the deref if Unity-object lifetime is the actual concern —
`PlanetChunk` is a plain class, so it isn't).

---

## Part 4 — Best practice / structure

### D1. BP — Grass placement math is duplicated across the two computes
`BiomeGrassPlace.compute` and `GrassNearFieldPlace.compute` share ~150 lines that must stay
bit-identical: `BiomeGrassParams`/`GrassBladeInstance` structs, `HashUint`/`Hash01`,
`CubeFaceToUnitSphere`, `BlendGrassParams`, `LerpGrassParams`, the corner-blend bilinear,
`SurfaceStateReject`, and `SampleClimateMoisture`. The blend logic already drifted once
historically (the "ids.x only" bug the comments in both files describe). `.compute` files
support `#include`; extract `Assets/Graphics/Shaders/Includes/GrassPlacementCommon.hlsl`
and include it from both. This is the highest-value maintainability change in the grass
system: the next tuning pass on `BlendGrassParams` becomes one edit instead of two.

Smaller C# sibling: `GetUniformWorldScale(Transform)` exists three times
(`FaceSpaceCellRangeBuilder.cs:247`, `GrassPlacementController.cs:353`,
`GrassChunkDispatcher.cs:248`). Two are copies of the first, which is already `public
static`. Delete the copies.

### D2. BP — CloudShadows.hlsl re-declares WeatherSampling's resources instead of including it
`CloudShadows.hlsl:7-10` declares `_CloudWeatherMap`/`_WeatherDynamicsMap` + samplers that
`WeatherSampling.hlsl:8-11` also declares, and `SampleCloudShadowWeather` /
`SampleCloudShadowRainRate` are line-for-line duplicates of `SampleWeather` /
`SampleDynamics(.b)`. Today no shader includes both files, but the first one that does gets
duplicate-resource compile errors, and the two sampling paths can drift (A2 is exactly this
class of bug). Make `CloudShadows.hlsl` `#include "WeatherSampling.hlsl"` and delete its
duplicates — include guards already make this safe, and `_CloudWeatherRotation` is declared
in WeatherSampling.

### D3. BP — coordinator resolves PlanetDto inconsistently
`PlanetGrassCoordinator.cs:197` (`CreateNearFieldGrassController`) calls
`SettingsProvider.GetSettings<PlanetDto>().PlanetRadius` directly while the rest of the
class uses the cached `_planetDto` (kept fresh via `SettingsChangedEvent`). Also, if
`UpdateControllerActivation` runs before `Configure` populates `_planetDto`,
`ComputeWaterRadius(null)` returns −1 and a controller created in that window places grass
underwater until the next redispatch. Use `_planetDto` everywhere, and guard controller
creation on `_planetDto != null` (activation already can't meaningfully run pre-Configure
because `_chunkedProvider` is null — the guard just makes that invariant explicit).

### D4. BP — `GrassPlacementController.Dispose` reaches into the resolver's collection
`GrassPlacementController.cs:217`: `_resolver.Chunks.Clear();` — the controller clears a
collection the resolver owns and repopulates each `Refresh`. Either give
`GrassChunkResidencyResolver` a `Clear()` method (ownership stays inside) or drop the line
entirely: the resolver dies with the controller and nothing reads it afterward.

### D5. BP — static `MaterialPropertyBlock` assigned in an instance constructor
`CloudRenderPass.cs:96,107`: `static MaterialPropertyBlock _propertyBlock;` is re-assigned
every time a `CloudRenderPass` is constructed (every `CloudRenderFeature.Create()`, i.e.
every renderer rebuild/domain reload). Harmless with one feature instance, but it's a
static-vs-instance mismatch waiting to confuse. Make it instance (`readonly`), or
lazily-initialized static.

### D6. BP — interactor release-trail slots starve under a full interactor roster
`GrassInteractorRegistry.cs:170-192`: active interactors and fading release samples share
the same 8 GPU slots, actives first. With 8 live interactors, trails never upload and
recovery pops instantly behind a moving crowd. Fine for today's debug-sphere usage; worth a
one-line comment on `MaxInteractors`, or splitting the cap (e.g. 8 + 8) when the character
controller lands. Also: `Shader.SetGlobalBuffer` (line 197) is re-set every frame though
the buffer object never changes after `EnsureBuffer` — move it inside buffer creation and
after domain-reload re-init (`Initialize` already sets it).

### D7. BP — stale sizing comments
- `FaceSpaceCellRangeBuilder.cs:13-14`: "a 120m disc on a 5293m planet … ~1.3 degrees" —
  draw distance is 200m via quality settings now; the corner-straddle rarity argument
  changes with it. Recompute or drop the specific numbers.
- `GrassNearFieldController.cs:12-13`: "Should drop dispatchesTotal from ~843 to ~5-20" —
  change-history commentary, prune per comment rules.
- `Grass.shader:203-205` ("they were previously tuned for a ~500m draw") — same rule.

### D8. BP — `Bounds` padding magic number
`GrassNearFieldController.cs:512`: `Vector3.one * (_drawDistance * 2f + 256f)` — the 256 is
presumably wind/interactor displacement headroom; name it (`const float BoundsSlackMeters`).

### D9. SUGG — grass writes no depth-prepass/normals passes
`Grass.shader` has a single `UniversalForward` pass (`Queue = Transparent-10`, `ZWrite On`).
Anything that consumes `_CameraDepthTexture`/`_CameraNormalsTexture` from the prepass —
SSAO, depth-based fog on some pipelines, the cloud pass's own depth test — sees terrain,
not grass. Today that's mostly invisible (clouds behind grass blades at the horizon is a
non-case), but it explains why grass never occludes any depth-driven effect. Flag-only:
adding DepthOnly for a million dithered blades is its own cost/benefit decision.

### D10. SUGG — near-field face textures: 36 individual `Texture2D` bindings
`GrassNearFieldPlace.compute:34-69` declares 6 faces × 6 texture kinds with per-face
if-chains (`SampleSurfaceRadius` alone is ~50 lines of face dispatch). If the surface
provider ever produces `Texture2DArray` atlases (it already does for the climate map), the
compute collapses by ~200 lines and the per-face branching disappears. Upstream refactor —
record as the shape of the eventual fix, not near-term work.

---

## Part 5 — Cloud march quality notes (flag-only, tuning territory)

### E1. LightMarch first-sample bias
`Cloud.shader:217-226`: `lightPos` is advanced a full `lightStepSize` before the first
density sample, so occlusion immediately adjacent to the shaded point is never sampled
(partially compensated by `lightStartJitter`). Conventional form samples at the midpoint of
each step (`lightPos += dir * stepSize * 0.5; sample; lightPos += dir * stepSize * 0.5`)
or samples-then-advances. Effect: self-shadowing slightly weaker at cloud tops. Worth a
side-by-side capture before/after if light-direction banding is ever chased again.

### E2. Per-iteration `min()` in LightMarch loop bound
`Cloud.shader:217`: `i < min(_CloudLightSteps, CLOUD_LIGHT_STEPS_MAX)` — hoist to a local
before the loop like the view loop does with `viewSteps`. Compilers usually do this; being
explicit costs nothing.

### E3. CloudNoise sampling position
`CloudNoise.compute:62`: `float3 pos = id / (float)_Resolution;` samples voxel corners.
`(id + 0.5) / _Resolution` samples centers and removes a half-texel phase shift against the
trilinear fetch. Invisible in practice at 128³; note for the next noise regeneration touch.

### E4. Worley border cost
`CloudNoise.compute:40-47`: border cells run a 27×27 wrap search (~729 distance checks).
One-shot generation cost, fine. If detail-noise regeneration ever becomes interactive
(console-tunable resolution), replace with the standard wrapped-offset-per-neighbor form
(27 checks flat).

---

## Part 6 — Cross-checks that came back clean

- **Near-field determinism chain** (stable cell hash → position-seeded blade hash in
  `Grass.shader:BladeSeed`) is sound; the instanceID-exclusion comment matches the code.
- **Range-budget quota math** (`GrassNearFieldController.BuildRangeBudgets`) sums exactly to
  capacity including the last-range remainder; cannot go negative (`Mathf.Max(0, …)`).
- **`FloorDivToMultiple`/`CeilDivToMultiple`** handle negative cell indices correctly.
- **Weather ping-pong** (`SphericalWeatherGrid.Advance`) reads active/writes scratch then
  swaps both texture pairs — no same-texture read/write hazard.
- **Permutation table indexing** in `WGSimplex` stays within the 512-entry doubled tables
  (max index 255 + 255 = 510).
- **`GrassInteractors.hlsl`** clamps `_GrassInteractorCount` defensively and the C# packer
  matches the 32-byte HLSL struct layout.
- **`GetFullScreenTriangle*` + `unity_CameraInvProjection` ray reconstruction** in
  `Cloud.shader:vert` matches the UV-starts-at-top handling.
- **Dirty-flag discipline**: `CloudController` static/per-frame split with change-checked
  uploads is per-rules; `SetAltitudeFade`'s change check is correct.
- **Console commands** live on their owning services (`cloud.*` on CloudController,
  `grass.*` on the coordinator/static commands) — per rules.
- **Caustics untouched** — nothing in scope touches `Ocean.shader`.

---

## Suggested fix order (once approved)

| # | Finding | Effort | Risk |
|---|---------|--------|------|
| 1 | A1 overflow rollback | 1 line | none |
| 2 | A2 gloom unification + comment fix | ~4 lines | visual (re-verify rain-cell look) |
| 3 | A3 enum member | 1 line | none |
| 4 | A5 radius-sampler clamps | 2 lines ×2 files | none |
| 5 | C1/C4/C5 dead deletions | mechanical | none |
| 6 | A4 TryGet in CloudController | ~5 lines | none |
| 7 | B1 hoist dynamics sample | ~10 lines | verify debug modes 8/9 |
| 8 | B2 MPB const set at create | 3 lines | none |
| 9 | D2 CloudShadows includes WeatherSampling | ~20 lines deleted | shader-compile check across includers |
| 10 | D1 shared placement include | ~150 lines moved | needs careful diff of the two copies first |
| 11 | C2/C3 dead paths (suppression, frustum) | Bryan decides keep-vs-delete | none |
| 12 | D3-D8 structure nits | mechanical | none |

Everything in Parts 2-5 below line 7 is discretionary; A1-A5 are the ones I'd call real
defects.

---

## Codex feedback

Reviewed against the current dirty working tree on 2026-07-03. I agree A1, A3, A4, A5,
A6, B1, C1, C4, and C5 are supported by the source. A1/A3/A5 are the safest first fixes.

Corrections and cautions before implementation:

- **A2 is directionally right but stale in its cited code.** Current
  `CloudShadows.hlsl` does not sample `_WeatherDynamicsMap`, does not define
  `SampleCloudShadowRainRate`, and does not contain the quoted "Same gloom term" comment.
  The real current bug is simpler: visible clouds use `max(storm, gatedRain)`, while cloud
  shadows still use storm-only `stormBoost`. Fix by sharing or duplicating the same gated
  rain/gloom term in shadows; do not apply the audit's raw-rain/smoothstep diff literally.
- **B1 must preserve debug mode 9 semantics.** Moving the dynamics sample into the density
  branch is correct for normal rendering and mode 8, but `WeatherPrecipitationSignal` is
  meant to show the weather/dynamics precipitation field across the sampled weather layer,
  not only where rendered density survived. Gate the extra sample specifically on
  `_CloudDebugMode == 9`; do not make all debug modes pay for dynamics sampling.
- **A3 should be fixed before relying on cloud console captures.** The registered debug
  module can reach mode 9, but `cloud.debug-mode` cannot. Add `WeatherPrecipitationSignal =
  9` to `CloudDebugState.View` and align the mode-8 name while touching it.
- **D2 is the right shape for the A2 fix.** Prefer including `WeatherSampling.hlsl` from
  `CloudShadows.hlsl` over adding a second dynamics sampler path. Compile-check shaders
  that include cloud shadows after the include change; the include guard should make this
  safe, but shader duplicate declarations are easy to miss.
- **C2/C3 are delete candidates, but separate them from visual fixes.** The suppression and
  frustum paths are dead in current defaults, yet they are also tied to the still-deferred
  chunk-grass architecture decision. Delete them in a cleanup pass after A1/A3/A5 rather
  than mixing them into a visual rain/grass fix.

I would amend the fix order to: A1, A3, A5, corrected A2/D2, then B1. The rest can stay
cleanup/structure work unless profiling or captures point at it.
