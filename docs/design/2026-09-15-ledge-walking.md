# Narrow ledge walking — first slice

Status: Implemented in LedgeWalkReview.unity. LEDGE-02 revision 1 awaits visual approval.

Bryan approved BEAM-01 revision 1 and requested continued missing coverage. This slice reuses ActorBeam, BeamInteraction, collision checks, and the shared performance player. It adds lateral authored root travel and wall validation instead of another movement controller or pose writer.

## Acceptance and scope

The actor must enter a supported narrow ledge, travel sideways, finish a step when released, idle, reverse, and exit without teleporting. Root travel must follow the authored clip at 1x. A blocked path must remain reversible. Missing floor or wall support must release the route. Beam behavior must retain its tests and accepted assets.

This first slice supports a fixed, straight, level ledge with one marked entrance. The actor can cross or return to that entrance. Opposite-end entry, corners, crouched travel, changing-size or moving surfaces, different body proportions, and main-game adoption remain open.

## Content and control

Eight original FBX files were copied with metadata from D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations/Traversal_LedgeWalk_*.fbx. These cover left/right entry, loop, exit, and idle. The right entry and idle remain imported candidates; production uses the left entry/idle and both travel/exit directions.

Editable production assets are Assets/Art/Interactions/Animations/Ledge Walk *.anim. Matching motion assets store planar displacement and return-exit yaw. Ledge Walk Performances.asset binds the existing seven route phases. The unused turn slot shares idle behavior; wall-side mode ignores turn requests. No runtime generator overwrites these clips.

Left entry lasts 1.667 seconds and travels about (-0.265, 0, 0.864) metres in route space. The loop lasts 0.933 seconds and travels 0.343 metres. Left exit lasts 0.733 seconds and travels (0.265, 0, 0.357). Reverse clips rotate the original right clips 180 degrees. Reverse exit travels (0.282, 0, -0.523). Idle lasts 4 seconds. Clips retain authored timing and limb curves. Existing foot correction remains active; no new hand or foot pose writer was added.

Return exit transfers its final facing to the actor root. Its clip removes the same rotation, preserving the combined authored pose. This prevents the locomotion blend from turning back toward the original direction afterward.

Press Play and click Visit Narrow ledge entrance. E starts; W travels; S reverses; released input finishes the current clip and idles. A/D does not turn into the wall. E, Space, or Ctrl releases route control. Existing performance playback blends phase and release changes.

Ledge Walk Review.prefab contains reusable fixture geometry. The walkway is 0.65 metres wide and 4 metres long, with top Y=1. The wall face is X=-6.4. Route center is X=-6. Both ends have broad platforms. Wall presence is tested during supported travel; entry/exit beyond wall ends use those platforms.

## Evidence

Bundle: local-only/animation-review/ledge-walk-2026-09-15/index.html.

- forward-v1: complete entry, crossing, exit, and rest; 600 frames.
- reverse-final: entry, travel, stop, reverse, return exit, and rest; 440 frames.
- reverse-v1: retained before the return-facing correction.
- source: original clips on the verified original rig beside raw production clips without corrections; 273 frames.

Runtime captures use deterministic 1/60-second simulation steps and 30 fps output. Two 640 x 480 panels give wide and close views. Wide camera: (0,4,-5), target (-6,1.5,0), orthographic size 3.9. Close camera follows root offset (3,1.3,-2), targets root + (0,0.8,0), size 1.55. ResetActor and VisitStation(0) precede each capture. Recipes and per-frame phase/root metadata are retained in the bundle.

I inspected sampled full-action frames and entry/exit sequences with timing data. Normal-speed playback was not inspected. Foot sliding has not been quantified. Runtime cancellation/support-loss renders remain unreviewed. These limits remain separate from passing functional tests.

A uses the original clip on the verified original Android rig at 1x. The raw production diagnostic omits the motor trajectory and transition graph. Reverse production clips have a different coordinate convention. A full matched production B remains unavailable; the diagnostic does not isolate every runtime blend effect.

22 focused EditMode tests passed: ActorBeamTests, ActorPerformancePlaybackTests, ActorThirdPersonCameraTests. Job e0130c521a73437489cc411be22c7fa4. New tests cover forward/reverse ledge exits and final facing, ignored turn input, and lost wall support. Existing tests cover beam entrances, travel, stops, reversal, blocking, support loss, cancellation, and invalid poses. Focused whitespace checks passed.

Visual approval is pending. The broader animation audit remains open. Rope climbing and fishing remain missing, along with the existing equipment, combat, and skill-variant gaps.

Code-health build: dotnet build ProceduralPlanets.Planet.csproj passed after restore. The first --no-restore attempt failed with NETSDK1004 because project.assets.json was missing. Logs: local-only/animation-review/ledge-walk-2026-09-15/build.log and build-restored.log.

## User approval
Bryan approved both current sequences on 2026-09-15. Remaining coverage stays open.
