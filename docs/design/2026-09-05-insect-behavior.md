# Insect behavior and validation queue

Status: Both wildlife test suites pass. Live population and pollinator transition checks passed; visual acceptance remains incomplete.
Bryan granted Unity control again after the scene conflict. The resumed checks below completed, and the editor returned to Edit Mode.

## Implemented behavior

- Fireflies use up to 24 patches around the observer. Patch centers stay at least eight metres apart.
- Each patch holds at most three particles. Each tick clamps motion to one metre from its center, with terrain clearance.
  Unity particle integration can move particles beyond that radius between ticks; the live sweep measured up to 1.128 metres.
- Retiring patches still reserve their space. Replacement patches cannot overlap their remaining insects.
- The existing local sun ramp drives patch count and fractional emission. The first partial patch can appear at dusk.
- Dawn stops emission progressively. Existing particles finish their five-to-eleven-second lifetimes.
- Butterflies and bees keep individual flight, landing, and rest state. Their mesh wings rotate around a central hinge.
- Butterflies prefer available flowers. Without flowers, they select ground landing points in suitable biomes.
- Bees require flowers. Each insect reserves a different flower while traveling or resting.
- Flower targets come from live scatter transforms. The cache refreshes once per second and retains the nearest 64 targets.
- Removed flower targets trigger a new destination or retirement. No separate flower population is generated.
- Resting pollinators use the existing threat registry to take off. Free-camera position does not create threats.
- `creature.ambience` reports pollinators and nearby flower targets.
- Resident behavior uses elapsed simulation seconds. Wandering birds no longer select ground-grazing pauses while flying.
- Bird perching lasts twelve seconds after descent, independent of frame rate. Landing decisions occur once per five-second window.
- Particle emission derives from the same lifetime range used by the particle system, including ambient birds and flies.
- Existing flies receive outward and upward escape velocity. Escape disables their orbit and adds no particle burst.
- Threat detection screens the segment against terrain heights. A blocked nearer threat cannot hide a farther visible threat.

The visuals remain procedural placeholders. Particle rendering can support full-color textures or meshes.
Individual behavior state is independent of that rendering choice.
Flower landing height currently uses the mesh bounds' top center. Complex flower clusters need authored landing points for petal-level precision.
The route clears terrain, but does not avoid tree canopies or branches.

## Completed checks

- Core C# build passed after package restore.
- Planet C# build passed.
- The EditMode test assembly and its dependencies built with zero errors after the final source changes.
- The subsequent wildlife logic and expanded test assembly also compile. Results are in `docs/agent-conversation/insects-2026-09-05/wildlife-build.txt`.
- The build reports existing analyzer-version and unrelated source warnings. See the build log under `docs/agent-conversation/insects-2026-09-05/build.txt`.
- A Unity console check reported no errors after the initial import. Later changes still need the final import check.
- Graphify update completed. Run it again after any further source changes.
- The initial night baseline recorded 113 particles across three live firefly fields at local sun -0.44.

Baseline artifacts: `docs/agent-conversation/insects-2026-09-05/before-night.png` and `before-night.txt`.
The image is dark and does not establish acceptable appearance. Its numeric population snapshot is usable evidence.
The baseline did not record the seed. A later world must not be presented as an exact matched pixel comparison.

## Queued validations

### Latest Unity results (2026-09-05)

- Asset refresh and script compilation completed. The console reported zero errors.
- `Hidden/SwarmParticles` returned no shader compilation messages.
- Both requested EditMode suites passed: 97 passed, zero failed, zero skipped (0.6501419 seconds).
  MCP job: `28ea22cc2a9d42a98e81856ccd4c89f6`.
- A fresh Planet session generated seed `1691104419`.
- Daylight grassland at `(-1125.00769, 3894.18115, 3057.32837)` reported local sun 0.97 and 64 nearby flowers.
  Camera rotation was `(0.383343726, 0.0542634279, 0.129225045, 0.9129096)`.
  Counts were 12 butterflies, eight bees, zero fireflies, and eight ambient bird particles across two flocks.
- Live insect state contained traveling pollinators, resting bees at their target positions, and a resting butterfly on the ground.
  This is a state snapshot, not proof of complete flight cycles or acceptable animation.
- `docs/agent-conversation/insects-2026-09-05/after-day.png` records the normal daylight view.
  Individual wing detail is too small in that image to judge.
- Before the close-up check, an external operation replaced Planet with `Assets/Scenes/Tests/ScatterLodCompare.unity`.
  The probe returned `Runtime error: Service AmbientSwarms not registered.`
  The file named `butterfly-close.png` shows that unrelated comparison scene and is not wildlife evidence.
- Editor control stopped after detecting the scene replacement. No attempt was made to restore over the other task's scene.
- Startup also reported the existing scatter tint warning for `FoliageMeadowCanopy_Autumn` (1.55) and `FoliageMeadowCanopy_Golden` (1.60).

The remaining queue below still requires exclusive editor control. Automated test results do not replace motion, dusk/dawn, or performance checks.

### Resumed runtime results (2026-09-05)

Bryan granted control again. The fresh session used seed `1691104419` and the same grassland pose.

- `pollinator-runtime.json`: 35.067 seconds, 303 samples, 62 landings, 62 takeoffs, 62 changed flower targets, zero duplicate reservations.
- `butterfly-rest-verified.png`: close-up of a resting butterfly. The procedural yellow wing silhouette lacks finished butterfly detail.
- `firefly-cycle.json` and `cycle-0.png` through `cycle-7.png`: eight time samples, each held for fourteen seconds.
  Firefly live patch counts were 0, 1, 9, 24, 9, 1, 0, 0 at local times 0.72, 0.75, 0.78, 0.90, 0.22, 0.25, 0.28, 0.50.
  No patch exceeded three particles. Minimum observed patch-center separation was 8.045986 metres, including retiring patches.
  The complete nighttime snapshot held 56 particles across 24 patches. Dawn left zero particles by local time 0.28.
- `no-flower-cache.txt`: temporarily withholding the live cache for five seconds removed all bees and preserved twelve butterflies.
  The cache reference restored automatically. This tests unavailable flower data, not an actual harvest operation.
- `threat-sampling-cost.json`: 120 batches of 100 visible forty-metre sight queries against the live Planet sampler.
  Mean batch cost was 5.631 ms; p95 was 6.756 ms. This is an isolated synchronous benchmark, not an end-to-end frame-cost measurement.
  Terrain screening is measurable at large populations and must not be described as free.
- Startup reported `Leak Detected : Persistent allocates 2 individual allocations.` without allocation stack traces.
  Its owner is unknown; this run cannot establish a clean leak-free startup.

All paths above are under `docs/agent-conversation/insects-2026-09-05/`.

- `continuous-cycle.json`: an accelerated twenty-four-second day showed gradual population growth and dissipation without time jumps.
  Emission stopped after sunrise. Existing fireflies declined from 56 to zero over approximately eight seconds.
  With this deliberately short day, some fireflies survived into bright daylight; lifetime remains measured in simulation seconds.
  The original 120-second day length restored automatically after the check.
- Disabling ambience immediately cleared all swarms, insects, and cached flower targets. Ambience was then re-enabled.
- The final error-filtered console query returned one grass shader warning:
  `Shader warning in 'GrassNearFieldPlace': use of potentially uninitialized variable (LoadPathWearTexel) at kernel PlaceAndCullNearField at GrassNearFieldPlace.compute(250) (on dx12)`.
  It returned no wildlife exception. Unity returned to Edit Mode in the Planet scene.

Remaining checks: actual harvested-flower invalidation, observer travel over steep/underwater terrain, matched 30/60/120 FPS motion recordings,
resident descent visuals, a real player approaching carcass flies, full-frame terrain-screening cost, and regeneration cleanup.
The automated suites cover several corresponding logic paths, but do not establish these end-to-end results.

### Butterfly art available locally

Bryan asked about multiple designs and the asset catalog. The catalog search found:

- `D:/Unity/Explore Assets/Assets/Synty/PolygonNatureBiomes/PNB_Meadow_Forest/FX/`: Monarch, Blue, Cabbage, and Lunar prefabs,
  four corresponding texture sheets, and `FX_Butterfly_Mesh_01.fbx`. The prefab animates a one-column, eight-row texture sheet.
- `Assets/Polyart/PolyartStudio/SharedResources/`: red/blue butterfly effects and a shared butterfly texture.
- `Assets/Devdog/InventoryPro/Demos/Assets/Models/HideOut/Particles/butterflies/`: three colored butterfly cycle PSDs and a bee PSD.
- `Assets/polyperfect/Low Poly Animated Animals/Meshes/Animals/Bees/SKM_Bee_Animation.fbx`: an animated bee model.

The latter paths are relative to the same external catalog.

Bryan authorized the four Synty designs. The mesh and four texture sheets now reside in `Assets/Resources/Wildlife/Butterflies/`.
Their asset bytes match the catalog originals. `SOURCE.md` records provenance and importer changes.
The renderer selects a stable design per butterfly and advances the source's eight-row wing-pose atlas during flight.
Resting butterflies hold the first pose. Bees retain their procedural renderer.
The source mesh supplies wing poses and a body; it is not subjected to the procedural wing deformation.

The Planet assembly builds with zero errors and eighteen warnings; see `synty-build.txt` in the scratch directory.
Unity is reserved by another agent. No editor tools were called for this integration.
Queued checks: import settings, all four colors, mesh scale/orientation, alpha edges, folded resting pose, flight animation,
landing contact, and no rendering regression for bees, flies, fireflies, or ambient birds.
The earlier 97 passing tests and runtime samples predate this art integration.

### Synty Unity validation (2026-09-05)

Bryan released Unity for this pass. Asset refresh completed without console errors.
The imported mesh has 96 vertices, and `Hidden/SwarmParticles` reports no compilation messages.
Both wildlife suites passed again: 97 passed, zero failed, zero skipped, in 0.5951748 seconds.
MCP test job: `583f8f30146147d0bbe0cf4f203b3e12`.

- `synty-four-designs.png`: isolated render of Monarch, Blue, Cabbage, and Lunar with the actual imported mesh and runtime shader.
  All four render recognizable colored wings with transparent backgrounds.
- `synty-rest-live.png`: a Monarch resting with folded wings in the live grassland world, seed `1691104419`.
- `butterfly-flight.mp4`: a camera-following recording of the same Monarch resting, taking off, and flying.
  `butterfly-motion/frames.json` stores positions, age, rest state, variant, and capture times.
  The recording contains 53 captures over 7.891 seconds: 33 resting samples and 20 flying samples, with one takeoff.
  The variant stayed at zero. Six distinct flight atlas indices occur in the captured samples.
  Capture overhead lowered sampling frequency; this is not a smooth 24 FPS or a frame-rate-independence benchmark.
- `bee-after-synty.png`: the procedural bee still renders its wing shape and markings after the shared shader change.
  Bee art remains a placeholder.

The image and recording paths are under `docs/agent-conversation/insects-2026-09-05/`.
These checks verify rendering and one live transition. They do not prove branch landing, authored flower contact, or obstacle avoidance.

- `fireflies-after-synty.png`: nighttime after the shared shader update. Diagnostics reported 24 live patches and 58 particles;
  no inspected firefly system exceeded three particles. Daytime pollinators were absent.
- The final console check exposed a capture-tool error:
  `An abnormal situation has occurred: the PlayerLoop internal function has been called recursively. Please contact Customer Support with a sample project so that we can reproduce the problem and troubleshoot it.`
  Its stack begins in `MCPForUnity.Runtime.Helpers.ScreenshotUtility.CaptureCompositedAfterFrame` at
  `Library/PackageCache/com.coplaydev.unity-mcp@a4c2d0a84573/Runtime/Helpers/ScreenshotUtility.cs:196`, through `manage_camera` screenshot handling.
  It also reported missing profiler end samples. Do not describe the full capture session as error-free.
  The existing foliage tint and grass shader warnings remain.
- Unity returned to Edit Mode in the Planet scene. No source changes were needed in this validation pass.

The [shared landing-target proposal](2026-09-05-wildlife-landing-targets.md) describes authored sockets and common reservations.
That architecture is proposed, not implemented; current flower lookup and ground-only resident bird landing remain unchanged.

Run these only after Bryan grants exclusive Unity control. Save the starting editor, camera, and time state first.

1. Refresh assets and wait for compilation. Check shader messages for `Hidden/SwarmParticles` and check the console for new errors.
2. Run the existing EditMode suites `ProceduralPlanets.Tests.CreatureResidencyTests` and `ProceduralPlanets.Tests.CreatureThreatTests`.
   The new regression test is `FireflyPatchesStaySeparatedAndCappedAndStopEmittingAtDawn`.
   It checks separation, repeated emission under the three-particle cap, and complete dissipation after dawn.
   Additional cases cover perch timing at 30/50/120 FPS, paused clocks, descent, flying versus grazing, and equal-time wandering decisions.
   They also cover translated-planet terrain screening, missing terrain, blocked versus visible threats, living fly escape, and bird emission.
3. Enter a fresh Play Mode session. Record the seed, quality, camera pose, and original time settings.
4. Find suitable grassland or forest ground. Freeze time with `time.freeze true`.
   Use `creature.ambience` to confirm the local biome and actual sun value.
5. Inspect fireflies through dusk, night, and dawn using `time.set-local`.
   Start with 0.72, 0.75, 0.78, 0.90, 0.22, 0.25, and 0.28.
   Local latitude changes sun elevation; use the diagnostic sun value to interpret each step.
   Allow at least twelve seconds at each sampled state. Also record a continuous unpaused dusk/dawn transition.
6. Inspect all firefly systems, including retiring systems. Require no more than three particles per patch.
   Require eight metres between patch centers. Confirm drift and threat responses cannot form larger groups.
   Check sloped ground, underwater rejection, observer travel, and repeated dawn/dusk transitions.
7. Set `time.set-local 0.5`. Find visible flower scatter and confirm a nonzero nearby-flower count.
   Follow butterflies and bees through travel, landing, rest, and takeoff.
   Require distinct flower reservations, visible wingbeats, and no spherical group motion.
8. Remove or unload a targeted flower. Within the one-second refresh window, require retargeting or retirement.
   Verify bees remain absent without flowers. Verify butterflies can land on bare ground.
9. Compare 30, 60, and 120 FPS for pollinator travel speed and rest duration.
   Inspect near-camera wing shape, bee markings, terrain clearance, and slope behavior.
10. Check carcass flies and overhead birds for regressions. Confirm disabling ambience and regeneration clear pollinator state.
    Verify resident birds finish descending before their twelve-second rest begins.
    Move a real threat toward a carcass: existing flies must depart without a population burst, then settle after the threat leaves.
    Test a hill between the player and animals. Measure terrain-query cost with a large live population before accepting the screening change.
11. Capture normal-view night and daytime images plus short motion recordings. Bryan judges spacing, timing, and flight feel.
12. Restore the original editor and camera/time state. Record results here before declaring visual completion.

The first test job reported: "Test job failed to initialize (tests did not start within timeout)".
The later job reported: "Cannot start a test run while the Editor is in or entering Play Mode. Stop Play Mode and try again."
Neither result establishes a test pass or a test assertion failure. The shared editor was in concurrent use.

## Review of other wildlife

Bryan authorized the logic fixes after the initial review. Their code is implemented; their Unity validation remains queued.

1. **Implemented: Resident behavior timing uses seconds.**
   `r.Tick` remains an intent sequence number. `CreatureBrain` accumulates the supplied simulation delta separately.
   Wandering and landing decisions use that clock. Perching waits for descent, then rests for twelve seconds.
   Landing decisions are consumed once per time window, including while already perched.

2. **LIMITATION: Resident animals still use capsule bodies.**
   `CreatureView.CreateBody` draws a capsule for deer, rabbits, and resident birds.
   Movement, faction reactions, and bird altitude changes exist. Species animation and articulated wings do not.

3. **Implemented count correction; ambient bird behavior remains limited.**
   Emission now derives from the actual twenty-second mean lifetime, targeting eleven birds per fully active flock.
   Ambient birds still orbit a drifting particle anchor. They do not use the resident bird's landing behavior.

4. **Implemented: Living flies escape.**
   The host updates current particle velocities away from the detected threat with an upward component.
   Orbit and strong noise no longer fight escape. No additional flies are emitted as an escape burst.
   Normal motion settings return when the threat cooldown ends.

5. **Implemented: Bounded terrain screening.**
   `ThreatRegistry` checks surface heights along the sight segment, after range and faction filtering.
   Samples are roughly one metre apart, capped at 128 segments. Missing terrain does not grant sight.
   This uses the existing surface sampler rather than camera-visible triangle raycasts.
   Sub-metre ridges, tree trunks, buildings, and cave geometry still require a world collision query.
   Runtime cost and behavior at terrain streaming boundaries still need validation.

The separation between observer presence and entity threats is consistent across these paths.
Carcass fly eligibility derives from the corpse timestamp and ends at the bones stage.
Carcass fly residency follows the corpse draw radius, rather than spawning flies for every saved corpse.

## Later player interactions

Keep the real player entity as the interaction source. Never treat the free camera as a player.
For a butterfly landing on a still player, measure player motion and add a temporary moving landing target.
Invalidate that target when the player moves, disappears, or changes worlds.
Local contact does not require saving every ambient insect. Captured or persistently named animals would require stable identities.
No player-landing behavior is implemented in this pass.
