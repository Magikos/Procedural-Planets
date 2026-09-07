# Wolf–deer encounter — 2026-09-07

## Active tracker

Bryan accepted the deer and wolf overall and deferred minor sliding. Bryan authorized a controlled stalk, chase, and attack encounter. The encounter runs in `Assets/Scenes/Tests/WolfDeerEncounter.unity`. Visual acceptance of this encounter remains open.

## Implementation

The shared `CreatureBrain` now contains Stalk, Chase, Attack, and Recover states. These extend the existing `AdaptiveStateMachine<CreatureContext>` adapted from Bryan's earlier state machine. Existing saved behavior IDs remain unchanged; new IDs append after Perch. Existing world hosts do not supply prey senses, so this change does not enable population-wide hunting.

The fixture starts a grazing deer six metres ahead of the wolf. At four metres, the deer looks toward the threat for 0.6 seconds, then uses the existing Flee state. The wolf changes from stalking to chasing when the deer becomes alert. An attack commits its heading and performs a short forward approach before the hit marker. Recovery either resumes pursuit or returns to wander. An 18-second hunt limit applies a five-second cooldown.

Attack duration and hit time come from the visual settings snapshot. The brain owns the action clock and emits at most one hit request per attack. The presenter samples the attack clip from that clock. A frame that crosses the hit time and clip end still produces one request. Target loss and a threat to the wolf cancel the action before damage. The host checks current range and facing at the marker.

The fixture uses two health points and one damage per valid bite. This is local encounter test state. Production damage remains owned by `CreatureResidencyService.Strike`, including live identity checks, alarm, death, and carcass persistence. Production integration must bind prey identity and call that method; no second production health service was added.

Attack and death clips use the existing Playables presenter. Attack blending fades walking foot correction rather than forcing attack poses through walking contacts. Death holds its final pose and skips procedural corrections. Eating and ordinary movement remain available. Preview-material conversion is shared by both prototype hosts.

## Controls

- Open `WolfDeerEncounter` and enter Play mode.
- Select `Wolf Deer Encounter` and enable `Restart Requested` to replay it.
- Disable `Target Available` to test target loss.
- Enable `Interrupt Wolf` to test an incoming threat during an attack.
- The state display reports both behaviors, test health, hits, misses, and attack sequence.

The camera follows the pair. The ground uses the existing prototype height function with a 0.5-metre mesh grid.

## Evidence and limits

All 20 selected EditMode cases pass: 15 pose cases and five hunt cases. Hunt coverage checks state progression, once-only hit timing across a large tick, target-loss cancellation, threat cancellation, range/facing, and give-up behavior. The Planet build passes. A fresh controlled 40-second run recorded two hits, zero misses, deer death, recovery, and return to wander. Target loss and incoming threat each produced zero hits after interruption.

Evidence lives under `local-only/animation-preflight/`: `encounter-build.txt`, `encounter-run.json`, `encounter-cancellation.json`, `hunt-live/`, and `wolf-deer-encounter.mp4`. The live recording completed two hits and deer death. The build reports 18 existing warnings and zero errors; Unity reported no current console errors. The scene is paused at the start for review. An initial test compilation failed because `CreatureHunt` was internal; the helper visibility was corrected before the final test run.

The attack marker at normalized time 0.12 is provisional and needs visual bite-contact review. The fixture does not implement obstacles, scent, occlusion, pack tactics, network authority, population prey selection, or sound. The alert pose uses look tracking; stalking uses slowed walk playback. These are not dedicated authored alert or stalking clips. Terrain foot sliding remains deferred.

## Next integration

After encounter acceptance, bind hunt targets to live creature identities in residency, validate target lifetime each tick, and route successful hits through `Strike`. Add attack sounds and other markers only after the bite timing is accepted. Keep the isolated fixture for regression review.
