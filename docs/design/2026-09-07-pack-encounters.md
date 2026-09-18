# Pack encounters and hunt tuning — 2026-09-07

## Scope

Default ecosystem: eight deer, three wolves in group 1, and one independent wolf in group 0.
Population fields apply on restart. Single-pair mode remains available.

Shared mechanics:

- ActorGroup defines alliance and distance-weighted support. Group zero does not ally independent actors.
- ActorGroup.SelectLeader selects among authority-supplied candidates, without species or scene dependencies.
  Target offers are snapshotted before sharing, preventing iteration-order relay through a pack.
- ActorDisposition computes confidence and effective courage from own strength, allied support, and opposition.
  Baseline courage remains unchanged. Outnumbering lowers effective courage; nearby willing allies increase it.
- Dead, sleeping, retreating, severely wounded, and stamina-recovering members do not supply support.
  Health, stamina, and distance scale remaining contributions.
- The existing CreatureBrain FSM has a generic Threaten state (ID 13; existing IDs remain unchanged).
  CanThreaten and ThreatProvoked are host-supplied capabilities/senses, not wolf checks in the state.
- Warnings face the threat without attack requests. Provocation or intrusion inside two metres permits defense.
  After six seconds of an unresolved warning, the actor yields for eight seconds instead of starving in a standoff.
- Existing utility margins and commitment time remain in use. Rival detection retains a known threat to twelve metres,
  versus five metres for discovery, so retreat does not stop immediately at the discovery threshold.
- Stalking no longer consumes the 18-second pursuit budget. Attack recovery does not reset that budget.
  Stalk movement increased from 0.6 to 1.2; deer maximum run speed remains 6 versus wolf 4.

Fixture wiring supplies eligible prey, group assignments, willingness, and nearby observations.
Independent predators are space competitors, not automatic prey. Group mates are excluded from rival detection.
Group members share a target and followers use lateral approach offsets before closing for contact.
Each member can still leave to eat, drink, rest, or retreat. This is not a global pack controller.

## Presentation and controls

Threaten reuses the authored stalk pose held at its start, plus look-at behavior.
The warning audio is a prototype bark, not a claimed growl animation or finished animal performance.
Only this sound was copied; no vendor code or plugin was installed:

Source: `D:/Unity/Explore Assets/Assets/Malbers Animations/Animal Controller/Wolf Lite/Audio/Wolf Bark.wav`
Destination: `Assets/AssetPacks/PolyperfectAnimals/Wolf/WarningBark.wav`

The scene references the sound through the prototype's WolfWarning field.
Warnings use spatial audio, at most once per four seconds. Manual time-scale-zero simulations suppress playback.

Pack confrontation places the independent wolf near the pack. Nearby pack support can be toggled live.
Set PackWolfCount to 1 and restart for the equal-rival comparison, or 2/3 for the outnumbered comparison.
Actor buttons wrap into rows; controls and status scroll separately. Focusing an actor places its details first.

## Evidence

471/471 EditMode tests passed: job `b17665d96c9f44de802e1424d21bff10`.
Six new tests cover affiliation, unavailable support, equal versus outnumbered rivals,
loss of support, warning without attack, leader selection, and long stalking before pursuit.
The existing long-chase test moved its expected completion from 22 to 23 ticks because pursuit starts after stalking.

Runtime comparisons:

- 1v1 at three seconds: both wolves Threaten, full health, 50% confidence, 65% courage, no hits.
- 1v3: independent wolf immediately Flee, approximately 29% confidence and 56% courage; fear rises during retreat.
- Final 240-second ecosystem: 23 hits, six misses, nine survivors. Two deer and one pack wolf died.
  Last attackers: deer 8 / solo wolf 40; deer 3 / pack wolf 21; pack wolf 20 / deer 1.
  Carcass meat was consumed. Both pack and solo hunting produced kills; defending deer remained consequential.
- No runtime exceptions were found. Both Core and Planet compiled without errors.

Evidence logs: `local-only/ecosystem-prep/pack-1v1.txt`, `pack-retreat-trace.txt`,
`pack-retreat-final.txt`, `pack-final-240s.txt`; captures under `local-only/ecosystem-prep/captures/`.

These are accelerated fixture results, not proof of ecological balance. Food remains finite and depletes.
Production perception, streamed planet navigation, group persistence/network replication, and species-specific
warning clips remain future integrations. Shared decisions take values and remain authority-owned.

Final presentation checks: equal rivals yielded by eight seconds without damage; the three-member pack
made the independent wolf flee at 0.3 seconds with 28% confidence and 56% effective courage.
Warning audio playback reported `isPlaying=True`. The final focused-actor HUD capture is
`captures/pack-hud-final.png`. Unity is paused with the default 8/3/1 population restored.

## Obstacle escape and hunt commitment follow-up

Escape now retains a reachable destination through turns. Path corner advancement requires a clear
navigation segment. Constrained movement slides along boundaries. Failed escape routes retry alternative
complete paths without advancing the stall timer once per candidate.

ActorGroup.Hunt holds a bounded 20-second leader/target commitment. Nearby willing members can join
at 35% hunger; a leader starts at 65%. Joined members retain the target after separating. Urgent thirst,
sleep, recovery, fear, and unreachable targets still take priority. The HUD reports leader and team hunt.
These rules remain generic; the fixture supplies affiliation and perception values.

Validation: 473/473 EditMode tests passed (job 9ddf4eff314849a68fbb7b7bc0a4591f).
Core and Planet builds succeeded. A controlled refuge-edge escape moved the deer 8.48 metres in eight
seconds with full health. A 240-second ecosystem run recorded 77.55 seconds with at least two committed
hunters on the same target, with a maximum of three. It ended with eight survivors, 30 hits, two misses,
and consumed carcass meat. These observations verify participation, not flanking tactics or ecological balance.
Logs: local-only/ecosystem-prep/edge-escape-runtime.txt and edge-pack-240s.txt.
The first runtime probe ran outside Play mode and returned "Sequence contains no elements"; entering
Play mode and rebuilding the controlled fixture resolved the probe failure.
The default 8-deer, 3-pack-wolf, 1-solo-wolf fixture is restored and paused.
