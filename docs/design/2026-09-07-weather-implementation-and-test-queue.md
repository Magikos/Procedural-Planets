# Weather implementation and Unity test queue

Bryan authorized implementation after the weather audit. Unity belongs to another active task. Do not import these changes until the editor is released.

## Required behavior

- Global weather evolves without drawing unseen parts of the planet.
- Distant precipitation uses curtains. The rain zone beneath a cloud uses nearby 3D particles.
- Existing drops keep world-space positions. Camera motion cannot translate or rotate their trajectories.
- Drops terminate at the first receiving surface: ground, player, water, and future buildings.
- Clouds, shadows, rain, snow, surface moisture, and water respond to shared weather and wind.
- Arid climate gradually reduces moisture and cloud support. Passing storms are not deleted at biome boundaries.
- Cloud lightning flashes drive delayed thunder. Visible bolts and ground strikes remain a later phase.

## Staged work

Candidate root: `tools/weather-staging/2026-09-07/candidate`. Baselines preserve the original dirty files byte-for-byte. No candidate has been applied to live `Assets`.

| Work | Current result |
|---|---|
| Shared particle ceiling and curtain top | Implemented in candidate; rain refreshes settings and shares its fade ceiling |
| Rain/snow phase | Rain uses the complement of the existing snow phase |
| World-space local rain | Retains independent integration; seeds a local column, fades before distance recycling, handles degenerate spawn/billboard directions |
| Hidden local updates | Suspends for disabled local precipitation, invalid altitude, no camera, zero count, or underwater; reseeds on re-entry |
| Arid moisture coupling | Evolution reads existing climate moisture; cloud support is limited by humidity using the existing condensation moisture cost |
| Thunder | Four bounded world-space voices consume the lightning event, with distance delay and planet occlusion |
| Audio assets | Three existing UniStorm thunder clips staged; no procedural fallback needed |

The climate change is a candidate, not a validated weather model. It keeps existing rates. Check climate extremes and storm lifetimes before acceptance.

### Still outstanding

- Swept collision against actual terrain, the player, lakes, and future rooftops. The candidate still uses the sea sphere.
- Rain splashes, water impact rings, wet-ground accumulation/drying, and snow accumulation/melting.
- Ground-reaching snow. The existing snow lower slab remains unchanged.
- Continuous cloud-noise transport when wind direction changes.
- Distant-to-near visual tuning and cold curtain appearance.
- Shared multiplayer lightning timing. Existing locally timed scheduling remains explicitly provisional.
- Debug/frustum suppression of rain computation beyond the newly shared controller gates.

These items are not complete because the candidate compiles. Continue implementation in coherent slices, then queue each slice's visual evidence.

## Audio provenance

Source catalog: `docs/research/2026-08-10-external-asset-catalog.md` and the owned Asset Store purchase list (UniStorm, product 2714).

Copied only `Thunder 4.wav`, `Thunder 5.wav`, and `Thunder 6.wav` from:

`D:/Unity/Explore Assets/Assets/UniStorm Weather System/Sounds/Thunder`

Candidate resource names are `Weather/Thunder/Thunder-4`, `Thunder-5`, and `Thunder-6`. Durations are 6.97, 6.36, and 6.07 seconds. Original sample bytes are preserved. New metadata GUIDs avoid collisions; mono import supports positional playback. No UniStorm runtime code was copied. Audio quality and imported volume remain unauditioned.

## Offline checks completed

- Disposable Core, Planet, and EditMode test projects compile serially with zero errors. The test build includes `RainParticleRegressionTests.cs`.
- The first generated-project build failed because newer animal sources were absent. The disposable build projects now reconstruct source membership from the current assembly definitions. Live generated projects were not changed.
- Remaining build warnings are Unity analyzer/compiler version warnings and existing `ScatterHarvestStore` field warnings. Full logs are under the staging `build` directory.
- Windows D3DCompiler compiled the actual candidate `RainUpdate`, `CSEvolveWeather`, and `CSInitWeather` kernels as `cs_5_0`.
- D3DCompiler reports `warning X3568: 'kernel' : unknown pragma ignored` for Unity's kernel directives. This does not establish Unity backend compilation.
- Baseline/existence checks passed for all 18 candidate files before delivery.
- Graphify update completed: 11,055 nodes and 15,868 edges. The HTML export was skipped because the graph exceeded its 5,000-node visualization limit. Staging is excluded from graph extraction.

Reproduce from the repository root:

```powershell
python tools/weather-staging/2026-09-07/stage.py check
python tools/weather-staging/2026-09-07/stage.py review
python tools/weather-staging/2026-09-07/stage.py build
python tools/weather-staging/2026-09-07/validate_compute.py
```

## Unity queue — pending editor release

The app follow-up `weather-unity-test-queue` is active. It checks every ten minutes, remains quiet while ownership is unchanged, and resumes only after release is established.

Known active owner at preparation time: Codex task `Animals & Animation`, ID `01a0744a-1020-70f2-b8d5-e1f546b68c07`. Other agents may also use the editor. Task idleness alone does not prove release. Check the owner's latest completion/release note and current coordination before touching Unity.

1. Confirm the editor is released. Preserve its current scene and the other task's work. Load the Unity orchestration skill before using MCP.
2. Run `stage.py check`. On a conflict, merge the candidate against current files. Never overwrite concurrent source changes.
3. Capture the live baseline before applying the candidate. Pin world seed, observer pose, quality, time, and weather state.
4. Run `python tools/weather-staging/2026-09-07/stage.py apply`. This writes only the explicit candidate files after baseline checks.
5. Copy the queued `RainParticleRegressionTests.cs` into `Assets/Tests/EditMode/` if that path remains absent. Let Unity create its metadata.
6. Import changed sources and assets. Run only `ProceduralPlanets.Tests.RainParticleRegressionTests` first. Archive test results.
7. Run the visual and performance cases below. Restore previous debug/time/weather controls when finished.
8. Record outcomes here. Repair failures before claiming success. Run Graphify on applied source changes. Keep final appearance pending Bryan's review.

### Regression cases

| Case | Required evidence |
|---|---|
| Camera translation and rotation | GPU tests prove identical surviving-drop positions and velocities under different camera poses |
| Camera follows falling drops | Ten successive steps preserve trajectory independence |
| Re-entry from orbit | Seeded particles remain local even when cloud base is farther away than the local radius; no NaNs |
| Walk into rain | Curtain remains legible in distance; local drops emerge without dragging or visible recycling |
| Wind | Surviving drops fall radially and drift with wind; camera facing has no effect |
| Settings and ceiling | Change cloud altitude and particle ceiling live; spawning and fade agree without resetting visible drops |
| Disable/re-enable | No frozen rendering while disabled; no local dispatch while suspended; immediate reseeding on entry |
| Cold transition | Warm: rain. Cold: snow without full liquid rain. Mixed transition preserves complementary phase weights |
| Arid transition | Track humidity, condensation, and rain over time in wet and dry climate cells; no instant biome-edge deletion |
| Thunder | One event produces one delayed sound; greater distance delays arrival; no far-side sound through planet |
| Thunder lifecycle | Disable and regenerate cancel queued voices; no leaked source GameObjects; at most four voices |
| Performance | Compare average and p95 over 120 frames for dry, local rain, distant storm, snow, orbit, and underwater views |

Use the existing `Cloud Diagnostics` captures and weather export tools. Runtime commands such as `weather.force` overwrite the stationary source map; restore with `weather.regenerate` before assessing natural climate evolution.

Do not mark collision, wet surfaces, snowfall contact, or full weather fidelity passed from this first candidate. Their implementation remains outstanding.
