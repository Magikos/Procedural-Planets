# Animal swimming

Status: implementation, EditMode validation, and Planet provider/runtime probes complete. Visual acceptance remains with Bryan.

## Cause and implementation

Ground animals used `PlanetSurfaceGrounding` with `CharacterWaterFloor`.
That placeholder kept their feet above lake and ocean surfaces.
`CreatureAnimationView` then selected walking or running from their movement speed.

Animals now connect to the existing `SurfaceCharacterController` swimming path through `CharacterSwimmingWater`.
Grounding uses the terrain bed. Birds retain their separate flight support.
`SurfaceSwimProfile` supplies species dimensions without changing the player's existing swimming defaults.
The species snapshot supplies swim speed and the fraction of standing height below water.
The default waterline is 70% of body height. Snake uses 15% after visual review.
Signed root depth supports low-body rigs whose root stays above the water surface.
Saved animals over deep water start at buoyancy height when promoted.
The live actor snapshot reports swimming separately from its behavioral objective.

The presentation blends into an optional owned swim clip or shared procedural paddling.
Paddling reuses `LimbPoseSolver` and the existing foot bindings.
The neck lifts while the limbs paddle below water.
Snake presentation retains its lateral slither and disables terrain surface-chain correction while swimming.
Swimming releases terrain foot contacts and suppresses land actions.
Authority checks prevent sleeping, consumption, and melee hits in the swimming state.
Swimming consumes the existing running stamina budget, including stationary paddling.
This pass does not introduce drowning, diving AI, or water-route planning.

## Owned source review

The current Polyperfect ground models have no directly compatible swim clips.
Quirky models have swimming clips on different Generic rigs.
The Quirky rabbit has no articulated leg rig for transferring paddle strokes.
Malbers Wolf Lite has swim, swim idle, entry, and turn clips on another Generic skeleton.
None were imported as vendor behavior components.

The owned FImpossible source root is `D:/Unity/Explore Assets/Assets/FImpossible Creations/Plugins - Animating/`.
Useful references include:

- `Legs Animator/Core/Leg Algorithms/LegsA.Leg.IK.cs`: blends detached feet toward the animation pose.
- `Legs Animator/Core/LegsA.Utils.cs`: separates grounded influence from movement influence.
- `Spine Animator/Utilities/SpineAnimator_FixIKControlledBones.cs`: preserves the animation/procedural pose order.
- `Tail Animator/Code/TailAnimator.Extensions.Waving.cs`: configurable periodic chain motion.
- `Look Animator/Code/LookAnimator.Extensions.BoneWeights.cs`: distributes look rotation through weighted bones.

No explicit swimming implementation was found in these animation sources.
Our shared restore/evaluate/capture/solve pipeline remains the integration point.
Do not install parallel vendor animators or a second state machine.

## Runtime validation checklist

Use a fresh play run after compilation. Existing live drivers retain their old providers through hot reload.
Unity became available for this validation pass.

1. Import and check the console after Unity becomes available.
2. Run `CreatureSwimmingTests`, `WaterPresentationTests`, and `CharacterMotorTests`.
3. Run the full EditMode suite after focused tests pass.
4. Open the animal review scene and enable `CreatureAnimationPrototype.Swim`.
5. Check rabbit, female/male deer, wolf, boar, fox, bear, polar bear, goat, and snake.
6. Use `SwimWaterline`, `SwimSurfaceHeight`, and `Speed` to inspect submersion and strokes.
7. Capture the same deer lake crossing shown in Bryan's 2026-09-08 GIF.
8. Check lake entry, deep crossing, stopping, turning, shore exit, and ocean crossing in a fresh Planet run.
9. Check raised lakes and different planet gravity directions.
10. Confirm birds still fly and land, and existing fish retain their swimming clips.

Pass conditions:

- Deep-water animals show paddling or slither, never a weighted walk/run pose.
- Water hides the belly and most limbs; the muzzle and eyes remain above water.
- Ground IK reports no planted feet while swimming.
- Wading uses the lake bed. Leaving water restores normal foot support.
- Swimming does not produce sleeping, feeding, melee hits, or stamina recovery.
- Repeated transitions do not accumulate bone offsets or change limb lengths.
- Existing player diving and surfacing tests still pass.
- Captures and console output contain no new errors.

## Evidence so far

Core build passed with zero warnings and errors.
Planet build passed with 20 existing warnings, including two Game assembly analyzer-version warnings.
The queued test assembly compiled with two existing analyzer-version warnings and zero errors.
Its first `--no-restore` build reported `NETSDK1004` because its generated assets file was missing.
The normal build restored that file and passed. This did not run the tests.
The final EditMode run passed all 666 tests with no failures or skips.
Job: `6d6eb3533d9b489d90df0c2a31f1e1b4`.
Earlier runs exposed a null-driver endurance fixture and a spring constraint that suppressed small movements. Both defects were corrected.
The spring regression now verifies measurable movement, drag, and preserved segment length.
Review captures cover deer, rabbit, wolf, boar, fox, bear, polar bear, goat, and snake.
Captures are under `local-only/ecosystem-prep/swimming/`.
The prototype hides most limbs underwater and releases ground IK. The fallback idle clip is frozen to prevent underwater grazing poses.
Shared body lean, water drag for secondary chains, and continuous idle-turn foot contacts are implemented.
Animal grass interaction selects at most three nearby grounded animals and preserves the existing eight-slot shader limit.
The exact reported Planet crossing location has not been reproduced.
Fresh Planet closeout tested all nine ground species in two lakes and an ocean, plus actual lake entry/exit for deer, rabbit, and wolf.
Root-depth errors stayed below 0.003 m; all swimming poses released ground contacts.
A combined Eagle probe passed flight, landing, perch, threat-driven takeoff, and visibility refresh.
Grass A/B completed with three windows per condition. Its GPU result was inconclusive because frame-time variation dominated the comparison.
The full measurements, capture limits, and reproducible probe paths are in [the shared animator closeout](2026-09-08-shared-actor-animator.md#planet-and-bird-closeout).
Automated tests do not replace visual review of the final swim appearance.
