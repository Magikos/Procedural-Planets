# Fixed rope climbing — first slice

Status: ROPE-01 revision 1 implemented; visual approval pending. Main-game adoption is not established.

Bryan approved LEDGE-02 revision 1 and requested continued missing coverage. This slice adds a fixed thick-rope route using owned pole-climb clips. It does not claim rope swinging or top-platform transfer.

## Acceptance and ownership

Mount from the ground, climb, finish a step when movement stops, hold, reverse, descend, and exit. Repeated ascent/descent must return to the same ground baseline. Blocked movement must preserve reverse/release. Releasing or losing the rope must return authority to normal falling without teleporting.

ActorRope owns route state and collision checks. RopeInteraction supplies geometry, five editable motion assets, and bounded hand fitting through ProceduralPoseRig. HumanoidAnimationPrototype connects input and the existing performance player. Existing ActorCollision, ActorTraversalMotion, and transition blending remain shared. No per-animation runtime generator or competing pose writer was added.

W climbs; S descends and exits at the bottom. Release input to finish the current one-second gait and hold. E, Space, or Ctrl releases the rope. Each requested step preflights body clearance. The route stops before a complete step would put the upper body past the rope end. Reversal remains available. Changed/missing support releases route ownership. Ground exit checks support.

## Content and fitting

Source: D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations/Traversal_Pole_Climb_*.fbx. Selected Enter, Up, Down, Idle, and Exit_StepBack, copied with metadata. Original files remain distinct from Rope *.anim production variants under Assets/Art/Interactions/Animations.

Rope Performances.asset binds Mount, Up, Down, Idle, Exit. Mount/exit last 0.9 seconds; each climb cycle lasts 1 second; idle lasts 3.667 seconds. All use 1x timing and 0.15-second phase blends.

Production extraction normalizes the source idle/gait baseline to the mount height. It transfers longitudinal mount/exit travel and vertical climb travel into matching motion assets. Body anticipation remains in the clip; the grounded motor does not move below the floor to reproduce a body dip. Both climb directions use 1.133 metres per complete cycle, avoiding accumulated return-height error. Editable curves retain the residual body motion after extraction.

The fixture has a 5-metre, 16-centimetre-diameter three-strand rope. Actor root sits 0.22 metres from its axis. Hand fitting projects palm contact radially to the rope radius, retaining authored vertical travel and rotation. It fades out as a source hand moves away; corrections beyond 9 cm receive no influence. Mount and exit weights blend. Maximum weighted requested correction in climb-final: 0.06126 metres. This is not a measurement of every finger's surface contact.

No foot IK writer was added. The source leg-wrap pose supplies foot motion. Its pressure and detailed mesh contact remain unmeasured. Thin ropes require a different fit or authored variant.

RopeReview.unity is playable. Press Play, click Visit Rope entrance, press E. Rope Review.prefab contains reusable review geometry. Climbing Rope.asset is a static mesh, not a physics rope.

## Evidence

Bundle: local-only/animation-review/rope-2026-09-15/index.html.

- climb-final: mount, two ascent cycles, hold, two descent cycles, ground exit, rest; 450 frames.
- release-final: mount, climb, hold, release, fall, landing, rest; 330 frames.
- climb-v2: before contact fitting and matched return distance; retained as a defect baseline.
- source: original clips on the verified original Android rig beside raw editable production clips without corrections; 226 frames.

Deterministic captures reset the actor and visit station zero. Simulation uses 1/60-second steps; capture uses 30 fps. Two 640 x 480 panels show wide and close views. Wide camera: (0,4,-5), looking at (-6,3,0), orthographic size 3.9. Close camera follows root offset (3,1.3,-2), looking 0.8 metres above root, size 1.55. Rope bottom is (-6,1,0). Platform top is Y=1. Recipes, phase/root metadata, and correction magnitudes remain in the bundle.

A retains original source timing and original rig. Raw production playback omits the runtime trajectory and transition graph. A full matched production B remains unavailable. The source diagnostic therefore cannot isolate all runtime blend effects.

I inspected sampled full-action frames and consecutive release frames with timing data. Normal-speed playback was not inspected. Finger wrapping, foot pressure, and support-phase drift are not fully measured. Visual approval remains pending.

28 focused EditMode tests passed: ActorRopeTests, ActorBeamTests, ActorPerformancePlaybackTests, ActorThirdPersonCameraTests. Job 3f08fead62aa40ab8cde85d6cae794f4. Checks cover round-trip displacement, stopped position, top limit, blocked ascent, release, lost support, invalid approach, and existing beam/playback/camera regression coverage. dotnet build ProceduralPlanets.Planet.csproj passed; log in the bundle. An initial missing System.Array qualification caused CS0103 during implementation; it was fixed before these checks.

## Remaining coverage

Top-platform mount/dismount, jumping catches, rope swing, flexible-rope physics, thin-rope motion, circling, other rigs, full contact measurements, and main-game integration remain open. Ledge corners/crouched variants, tool equip/stow, fishing, combat, and skill variants retain their existing open status. The broader animation audit remains open.

## Approval — 2026-09-16

Bryan approved ROPE-01 revision 1. Preserve climb-final and release-final as accepted references. The remaining coverage above stays open.
