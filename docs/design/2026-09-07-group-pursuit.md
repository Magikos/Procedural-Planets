# Group pursuit positioning

## Scope and checks

Extend the existing group hunt and creature FSM. Preserve needs, fear, recovery, independent predators,
full-speed prey, obstacle escape, and authoritative hit validation.

Acceptance checks: unique approach slots remain stable when member order or travel costs change;
a departing pursuer is replaced; target changes clear assignments; invalid members are rejected;
unreachable or excessive approach routes fall back without rejecting a reachable target; allies block
strike lanes; one/two/three-member runtime comparisons record captures, endurance, and navigation stalls.

ActorPursuit receives observed positions, target velocity, ground normal, speed, attack reach, and body radius.
It keeps roles until membership or target changes. The host removes a blocked approach member for four
seconds, during which that member uses direct pursuit. This provides retry hysteresis.
The first slot follows; the next slots approach from opposite sides. Additional slots intercept ahead.
Prediction is limited by travel speed and current separation. Approach routes refresh twice per second.
The native navigation adapter rejects incomplete routes and detours exceeding 1.5 times the target route
plus two metres. Rejected tactical positions do not mark the prey unreachable.

The fixture retains prior-tick velocity and samples every brain before moving any actor. The shared
CreatureSenses carries the approach point and attack-lane availability. Approach affects movement only;
contact uses the actual target position. Committed attacks recheck allied obstruction at contact.
The HUD reports role, accepted approach, and blocked strike lane.

No new FSM, plugin, or networking layer is introduced. Group state remains authority-owned.
This fixture still supplies planar navigation and prototype perception. Production navigation and
replicated group state remain integration work.

## Evidence

Baseline: local-only/ecosystem-prep/pursuit/before.txt and before-20s.png.
One/two/three wolves run against eight deer for 120 seconds, with no independent wolf in this comparison.
The baseline recorded 1/1/2 deer deaths. Mean wolf stamina was .836/.716/.721.
The comparison counts deaths and does not infer ecological balance from one run.

Final validation (2026-09-07):

- 506/506 EditMode tests passed, job 13dec5d7ed014b9ba18a63af1b648f6e.
  Four new pursuit tests cover stable assignments, departing members, prediction and ground planes,
  attack lanes, and invalid members. Existing navigation coverage now includes an isolated island.
- Core and Planet builds passed. Planet reports 20 existing warnings and zero errors.
- Final 120-second comparison: one/two/three wolves killed 1/2/2 deer. Mean wolf stamina was
  .834/.751/.683. Aggregate no-progress actor-seconds were .30/.35/.45, versus .35/.30/.25 before.
  More wolves did not guarantee a faster or cheaper hunt. The sample is too small for balance claims.
- At 20 seconds, the three wolves held Pursue/FlankLeft/FlankRight with both side approaches accepted.
- Default 8/3/1 population, 240 seconds: seven survivors, 34 hits, three misses. Group hunters spent
  129.95 of 278.15 actor-seconds following accepted tactical approaches. Blocked strike lanes occurred
  for 41.25 actor-seconds. Minimum living pack centre separation during hunts was 1.09757 metres
  (the body-disc target is 1.10 metres). Finite food and thirst still affect the outcome.
- A four-member group exercised a reachable Intercept approach within 30 seconds.
- No console errors after the runtime tests. Graphify update completed.
- Unity is paused with the normal 8-deer/3-pack-wolf/1-solo-wolf population restored.

Evidence is under local-only/ecosystem-prep/pursuit/: after-final.txt, after-final-20s.txt,
after-final-20s.png, approaches-close.png, final-240s.txt, four-member-30s.txt, and build logs.
The first post-change run rejected overlong predictions frequently; bounding prediction by separation
resolved the opening-chase rejection. Its intermediate results remain in after.txt for traceability.

Remaining limits: this uses local geometric interception, not a forecast of the prey's full navigation
route. Members retain roles until membership/target changes or approach rejection; no periodic role
auction occurs. The fixture still relies on body discs near combat, so large models need suitable radii.
Bryan's playthrough remains the visual acceptance check.

## Carcass food yield

Bryan requested enough deer meat to fill three wolves easily. CreatureCombatSettings now authors
MeatYield in full hunger-bar units and snapshots it into CreatureCombatDto. The deer asset supplies
4.5 units, replacing the fixture's hard-coded 1.5 units for every animal. Other combat profiles retain
1.5 by default. Existing corpse fractions, food consumption, and decay use the same finite stock.
This is normalized meal capacity, not kilograms or a body-mass metabolism model.

Eight ecosystem tests passed (43e913f0d8d34b50989cec079c8625f6), including the authored deer yield
feeding three empty consumers while hunger advances. Core and Planet builds succeeded.
A live fixture test placed three fully hungry wolves at a fresh deer: each reached 4.8% hunger,
with 1.314 of 4.5 units remaining (29%). The existing feeding goal stops around 5% hunger.
Evidence: local-only/ecosystem-prep/meat-three-wolves.txt. Default population restored and paused.
