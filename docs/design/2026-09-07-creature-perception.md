# Shared creature perception

## Scope

The animal authority fixture now supplies sight, hearing, and scent observations to the existing brain and memory. The evaluator has no wolf/deer logic or target references. Species authoring can supply the same immutable profile.

This slice validates the ecosystem fixture. Planet wildlife still uses ThreatRegistry for its current terrain-screened threat queries. Its species DTO now carries the shared perception profile for the next host integration. No existing wildlife behavior was removed.

## Contract

- ActorPerceptionProfile owns defaults for day/night sight, viewing angle, hearing/smell range and thresholds, per-sense resolution, memory lifetime, and uncertainty growth.
- CreaturePerceptionSettings snapshots a parent plus explicit overrides. Missing overrides inherit. Cycles, duplicates, unknown settings, and invalid values fail validation.
- ActorStimulus stores a source correlation ID, kind, sense, emission position, strength, and times. Its ID does not imply recognition.
- ActorKnowledge stores an estimated position, uncertainty radius, confidence, identity confidence, observation time, and expiry. Sharing preserves original data and expiry.
- Sight requires viewing angle and an unobstructed line. The host supplies normalized light. Day/night ranges blend continuously.
- Sounds originate from footsteps, attacks, and warnings. They stay at the emission position. Obstacles attenuate sound.
- Living creatures leave bounded scent samples. Bleeding increases scent strength. Samples decay over 45 simulation seconds.
- Position quantization produces a deterministic search area. Smaller resolution values produce smaller areas. Closing on a source improves precision.
- Observations age without following the live target. Direct sight is recorded separately, so shared visual memories do not grant a receiver line of sight.
- Navigation and look targets use estimates. Physical hit validation still uses actual contact. The FSM cannot initiate a strike from an indirect observation.
- Uncertain interests look first, then investigate when appropriate. Investigation times out and has a retry cooldown. Recognized nearby threats retain emergency priority.

## Profiles and scenarios

Profiles: Assets/Resources/Settings/CreaturePerception/{Default,Deer,Wolf}.asset.
Deer overrides wide vision, 5m night sight, 20m hearing, and 6m scent resolution.
Wolf overrides 16m night sight, 22m smell range, and 0.65m scent resolution.
All other values inherit Default and the code defaults.

The existing WolfDeerEncounter scene includes Water awareness, Night vision, Hidden sound, and Scent tracking buttons, plus a live Light slider. Focus an actor for its observation data.

Standalone scenes:
- Assets/Scenes/Tests/CreatureNightVision.unity
- Assets/Scenes/Tests/CreatureHiddenSound.unity
- Assets/Scenes/Tests/CreatureScentTracking.unity

Night vision disables hearing and smell to isolate the light response. Scent tracking disables sight and hearing to compare tracking precision. The camera remains well lit so behavior is readable; the Light slider controls sensed illumination.

## Acceptance checks

- A drinking wolf within awareness makes the deer flee.
- At 7m in darkness, the wolf sees the deer and the deer cannot see the wolf.
- The obstacle blocks sight while an emitted sound can still be heard.
- At equal range, the keen smell profile yields a substantially smaller uncertainty radius.
- Lost sight retains an old estimate, never follows a teleported target, then expires.
- Shared memory cannot renew expiry or grant direct sight.
- Existing home, hunt, and pack behavior tests remain passing.

## Current limits

The fixture supplies light explicitly. Planet sun, local lights, weather, and cave integration remain host work. Smell has no wind transport yet. Hearing uses normalized event strength and simple obstacle attenuation, not acoustic propagation. Memory is transient; multiplayer transport and save serialization are not implemented. The authority owns perception and clients can consume its resulting values.

Scent history is capped at 512 events, and memory at 32 observations per actor. The fixture scans its bounded population. A world host can add a spatial index without changing observation or brain contracts.

## Validation

Core and Planet builds passed. The final incremental Planet build reported 18 existing warnings and zero errors. Unity reported no compilation errors or runtime exceptions.
The full EditMode suite passed 521/521 (job 8f1a4f6bca77419bb75388b5032beaf3).
After the final curiosity priority correction, 34/34 focused perception, home, hunt, pack, and survival tests passed (job def7f1cd5674403bac3c71c0e51dc380). This includes the additional critical-needs regression test.
Graph update completed after the final code change.

Final scenario assertions passed:
- Water approach: deer Flee while wolf Drink.
- Darkness at 7m: wolf direct sight; deer no actor observation.
- Hidden sound: deer hearing observation, no direct sight.
- Scent: deer uncertainty 4.25m versus wolf 0.34m.
- Teleported target: old estimate remained fixed, uncertainty grew, and memory expired.
- Carcass feeding: a wolf starting at 80% hunger reached the carcass and ate; hunger was 12.6% at 20s after subsequent hunger growth.
- A 240s ecosystem run completed before the final curiosity correction: 16 hits, 2 misses, 2 carcasses, and feeding.
- The final 120s ecosystem run completed with 8 hits, no misses, and one carcass. The main wolf's hunger fell from 87.5% at 30s to 24.8% at 90s.

Integration checks found and corrected temporary marker colliders, uncertainty at consumption boundaries, carcass ground destination height, and old scent overriding current sight. Current sight now wins over older visual/scent samples. Trackers prefer newer scent emissions and record searched scent points in the shared evaluator. Curiosity no longer has emergency priority over hunger, thirst, recovery, or sleep.

Evidence directory: local-only/ecosystem-prep/perception.
- scenarios.txt and scenario-probe.cs: final assertions and replayable Unity execute-code body.
- carcass-feeding.txt: consumption check.
- ecosystem-final-240s.txt: pre-curiosity-correction soak.
- ecosystem-priority-120s.txt: final soak.
- night-scene-1.png: readable night test capture. The initial night-scene.png caught the loading overlay and is not visual evidence.
- core-build.log and planet-build.log: build output.

Use the scenario buttons to restart each controlled case. Population balance and the appearance of alert/search behavior remain subject to Bryan's play review.

## Retreat continuity and drawings

An escaping actor retains a recognized pursuer while its observation remains usable. Losing sight or crossing the initial awareness radius no longer resets threat assessment. Hearing, smell, and expiring memory sustain retreat. This does not grant the actor the pursuer's true position.

The scenario presets now retain all senses by default. Set `IsolateTestSense` before restarting a diagnostic scenario to isolate its named sense. The original night probe results above used isolated senses.

The controlled rear-pursuit probe ran for eight seconds without a stop or a Flee interruption. Hearing supplied 118 of 160 observation ticks. After target removal, the deer reached Rest within 16 seconds. Evidence: `local-only/ecosystem-prep/perception/retreat-probe.txt`.

Game-view controls include `Draw senses`, `Sight`, `Hearing`, `Smell`, and `Remembered positions`. Drawings use the focused actor, or the first deer in Overview. No Editor Gizmos switch is required. Green shows sight, cyan hearing, and orange smell. Lines lead to remembered estimates; small rings show uncertainty. Labels include observation age and confidence. The display shows the memory's selected observation, not every simultaneous signal.

Large rings show nominal ranges for strength-one signals. Sight range follows the light setting. Occlusion, signal strength, thresholds, and sleep still affect detection. The overlay draws through geometry for inspection; it does not depict an occlusion-clipped visible region. These controls belong to the animal test host.
