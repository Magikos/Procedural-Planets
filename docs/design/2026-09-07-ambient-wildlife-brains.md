# Ambient bird and pollinator brains

Status: Unity compilation and 124 wildlife tests passed on 2026-09-07. Live flower visits and bird approaches were observed. Broader visual checks remain below.

## Ownership

`AmbientWildlifeSimulation` owns individual bird and pollinator state. It accepts elapsed time, observer position, and world queries.
It has no scene objects, animation graphs, Unity clock, or graphics calls. The placement callback supplies habitat-approved positions.
`AmbientSwarms` supplies live flower snapshots and draws the simulation's read-only `AmbientWildlifePose` values.
The current local world still schedules both stages. This change does not implement network replication.

`WildlifeBrain` uses the same `UtilityDecision<T>` implementation as `CreatureBrain`.
Small wildlife selects travel, visit, rest, or escape. It does not allocate the resident hunting and combat states.
Threats use the existing faction registry and interrupt visits both on the ground and during flight.
The debug observer only controls population range; it is not a threat.

## Shared landing

`WildlifeLandingReservation` uses the existing `WildlifeLandingTargets` registry and exclusive claims.
Ambient birds share the resident birds' authored-site registry. Bees and butterflies can use sites with their respective use flags.
Scatter flowers publish into a separate instance of the same registry, preserving the separate scatter and authored identity domains.
Moving, removing, flooding, or invalidating a target interrupts its visit. Escape, retirement, and clear release claims.
The existing `BirdLandingGround` validates dry terrain and slopes for ground landings.

Ambient animal IDs use the reserved owner `0xFFFC`. They are transient and do not enter creature save records.
The allocator does not reset when the population clears within one simulation instance.

## Preserved and added behavior

- Birds retain solo, seven-member, and twenty-four-member groups with loose or V arrangements.
- Birds can leave formation, land, rest for twelve seconds, and return to formation.
- A nearby resting member keeps its flock active until it can depart.
- Bees visit flowers or authored bee sites. Butterflies also use valid ground targets.
- Both pollinators retain distinct flight curves, wingbeats, opacity ramps, and butterfly art variants.
- Fireflies and carcass flies retain their particle behavior.

Pollinators attempt one arrival per kind every quarter second. This replaces the previous frame-rate-dependent arrival rate.
Movement uses steps no larger than 1/30 second. A long frame processes at most one second of catch-up.
No nectar stock, hive behavior, player landing, fish migration, or new consumption authority is added here.
These need their own world resources or interactions. Flower visits do not grant nutrition through rendering.

## Validation queue

Offline Core and Planet builds passed with zero errors. Core reported two analyzer-version warnings; Planet reported eighteen existing warnings.
Logs: `local-only/wildlife-brain-core-build.txt` and `local-only/wildlife-brain-planet-build.txt`.
No Unity test, import command, play-mode transition, or capture was requested by this pass.

When Bryan grants exclusive Unity access:

1. Import and compile the changed scripts. Compile the new test fixture.
2. Run `AmbientWildlifeBrainTests` (eight cases), `BirdPerchTests`, `CreatureThreatTests`, and `CreatureResidencyTests`.
3. Also run the pending `CreatureCarrionTests` from the vulture pass.
4. Confirm actual bird landings, twelve-second rests, takeoffs, and formation return with no terrain penetration.
5. Confirm bee and butterfly flower visits, all butterfly art variants, and escape while airborne and resting.
6. Remove or move claimed sites. Harvest flowers. Flood a ground site. Confirm immediate release after the next source update.
7. Check faction disguise, sunrise/sunset populations, observer departure, disable/enable, regeneration, and graph cleanup.
8. Capture the same wildlife views used in the previous bird and pollinator checks. Compare firefly spacing and dusk/dawn behavior.

Acceptance requires all tests to pass and runtime evidence for landing, escape, claims, and cleanup.
Visual correctness remains unverified until captures are reviewed.

## Validation results — 2026-09-07

Bryan granted exclusive Unity access. The first run failed one older rendering test:

```text
Expected: in range (2,48)
But was: 0
```

The Edit Mode test supplied no elapsed time. `AmbientSwarms.Tick` now accepts an optional elapsed-time override for individual wildlife.
The test supplies 1/30 second explicitly. Runtime callers retain the Unity delta-time default.

The rerun passed 124/124 tests, zero failures and zero skips, in 1.7976005 seconds.
Job: `64f43144d4ac4a0ba1d5425723d5f111`.
This run included AmbientWildlifeBrainTests, CreatureCarrionTests, BirdPerchTests, CreatureThreatTests, and CreatureResidencyTests.

A fresh Play Mode run showed 64 nearby flower targets, bees and butterflies in Visit and Rest, and two bird groups totaling 31 birds.
The live snapshot included birds approaching landings. Headless Unity tests verified contact, rest, and departure.
`docs/agent-conversation/wildlife-2026-09-07/butterfly-rest-live.png` shows an actual resting butterfly in the production world.
The initial `pollinator-live.png` capture occurred after dusk and is not a butterfly validation image.
The accelerated day cycle was frozen at local noon for the close-up, within Play Mode only.

The console returned this shader warning during the run:

```text
Shader warning in 'GrassNearFieldPlace': use of potentially uninitialized variable (LoadPathWearTexel) at kernel PlaceAndCullNearField at GrassNearFieldPlace.compute(250) (on dx12)
```

No wildlife exception was observed. This pass did not modify the grass shader.
Full visual coverage of every butterfly variant, flock return, and field interactions remains unverified.
The automated coverage passed; this record does not claim every queued manual scenario was captured.
Temporary preview objects and graphs were released, and Play Mode was stopped.
