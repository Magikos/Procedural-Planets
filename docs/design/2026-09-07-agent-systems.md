# Reusable agent systems

Status: Foundation, persistent needs, and stable objective selection implemented and checked.
Current next action: Connect actor-centered observations and source eligibility, then authoritative interaction outcomes.
Baseline: dirty working tree on `harvest-vertical-slice`, commit `1df21b2`. Preserve unrelated changes.

## Contract

Reuse the existing generic FSM, actor input/movement contracts, EntityId, world delta log, and resource authorities.
Rewrites can replace implementations. Removing existing functionality requires Bryan's agreement.
Species profiles describe differences. Shared mechanisms execute behavior across creatures and future NPCs.

Multiplayer is planned, not implemented. The future host/server owns simulation time, decisions, damage, resource availability, and consumption.
Clients request actions; authority validates identity, generation, eligibility, distance, and action timing before mutation.
Presentation consumes accepted state and events. Animation callbacks never authorize damage or grant resources.
Use stable identities and versioned value records. Do not persist Unity objects, FSM instances, or transient decision scores.
Save records support persistent state; future movement replication can use separate transient snapshots without writing every pose to disk.
No networking package, RPC facade, prediction, or transport is introduced in this slice.

## Implementation sequence

1. Actor lifetime: preserve damaged health across demotion and reload; retain old payload readers; add explicit FSM stop/restart cleanup.
2. Needs and decisions: add actor-owned hunger/thirst, shared objective scoring, switching margin, commitment, emergency interruption, and failed-target memory.
3. Observations: adapt ThreatRegistry registration and relationships; add target identities, observation age, and source queries independent of rendering.
4. Navigation: investigate the world obstacle representation, then add route and interaction-position queries above the existing motor.
5. Actions and sources: connect authoritative damage and consumption to existing scatter, corpses, water, and world deltas.
6. World scenario: deer seeks plants/fresh water; wolf evaluates eligible prey; danger and failed access change the objective.

Navigation outcomes must distinguish unknown, temporary blockage, failed progress, and proven unreachable destinations.
A timeout does not prove that a target is unreachable. Packmate exclusion occurs before scoring; predator/prey describe current interactions.
Pack coordination, scent propagation, blood trails, and additional needs follow the first complete scenario.

## First slice details

CreatureRecord receives a versioned health field. Legacy records resolve unspecified health from the species maximum.
Damage writes through the existing world log immediately, before publishing the wounded result. Full-health untouched creatures still need no record.
Promotion restores state rather than healing. Death still creates the existing slot suppression record and separate corpse.
The existing single-player entry point remains. An injected clock supports authority-controlled time and repeatable checks.
FSM Stop exits once and prevents later updates. Restart releases the previous state before entering the next state.
No state IDs, locomotion settings, animation settings, faction defaults, or corpse yields change.

## Preservation and acceptance

- Round-trip current records through WorldDeltaLog; read formats 1 and 2; reject malformed new health payloads.
- Wound, demote, promote, and reload: retain health and identity. Respawn: fresh health and next generation.
- Failed persistence: do not publish or retain an unrecorded wound.
- At-home injured creatures retain records. Unchanged records do not append again at each demotion.
- FSM repeated Stop exits once; Tick after Stop does nothing; restart and invalid restart preserve correct lifetime.
- Run existing FSM, creature residency, hunt, threat, bird, fish, and harvest EditMode tests.
- Build changed assemblies and verify Unity import. Do not claim multiplayer runtime validation without a networking implementation.

## Remaining design work

Multi-observer residency must use the union of player interest regions and simulate each actor once.
The current one-observer host adapter remains a known limit until that migration lands.
Needs pause while unobserved. Revisit offline progression after actual feeding exists. Wounds do not heal automatically.
Consumption must preserve player harvesting, item grants, corpse hide loot, decay, depletion ordering, and save compatibility.
Cancellation must release reservations and reject stale action events when those actions become real consumers.

## Needs and decision slice

ActorNeeds holds bounded hunger/thirst values without Unity or planet dependencies. Default growth reaches one after 1,800/900 simulated seconds.
CreatureSpecies authoring supplies the rates through its immutable DTO. Zero duration disables growth; legacy records start satisfied.
Payload format 4 stores needs while formats 1, 2, and 3 remain readable. No needs-driven health damage occurs in this slice.
Needs checkpoint every 30 simulated seconds, on demotion, clean shutdown, and before the planet reopens its world log.
An abrupt crash can lose growth since the last checkpoint. No write occurs for unchanged saturated values.

UtilityDecision<T> chooses a typed objective key; AdaptiveStateMachine still executes all behaviors.
Selection uses a 0.12 score margin and two-second commitment. Emergency danger interrupts immediately; lost eligibility cancels commitment.
Equal scores retain the current choice. Failure memory holds at most 32 keys and supports expiration and explicit invalidation.
Creature keys combine objective and the existing EntityId. A pending hunt cannot transfer its hit to replacement prey.
Hunger enables hunting at 0.5; health changes its score. Thirst competes with hunting and food search.
Search objectives use the existing wandering movement without idle rest pauses. They do not pretend to find or consume resources.
The encounter fixture explicitly supplies a hungry wolf and a prey identity. It remains a controlled fixture with local damage.
Threat observation memory and route feasibility remain later work; current boolean threat loss still ends escape eligibility immediately.

Acceptance: stable choices across small score changes, emergency cancellation, per-target rejection, satiated hunt rejection,
health-dependent priorities, frame-rate-independent need growth, checkpoint/reload preservation, and no offline growth.

## Evidence

- Planet/Game build: passed, 0 errors; 20 warnings from analyzer/compiler version mismatch and existing Planet code.
- EditMode test assembly build: passed, 0 errors; 2 analyzer/compiler version warnings. Log: `local-only/animation-preflight/agent-foundation-build.txt`.
- Regression job `46d7b287986c49aeb3af385d99380082`: 147 other selected tests passed; five persistence checks passed and three failed during fixture setup.
- First fixture error: `System.NullReferenceException : Object reference not set to an instance of an object`. The fixture omitted the planet transform.
- Second fixture run `c24a6a4d2b6b4887999ecd63a095b717`: five passed, three failed because woodland deer rejected the fixture's grassland.
- Corrected persistence job `c39c9ee8571347b99e367ce5afe69d50`: 8/8 passed. Thus all 155 selected checks passed across the regression and focused rerun.
- Checks cover actual WorldDeltaLog close/reopen, demotion/promotion, fresh-generation health, stale IDs, rejected writes, payload compatibility, and supplied-time planning.
- Unity imported the scripts with no `error CS` entries. Hot Reload initially required a full compilation; the editor performed a domain reload before tests.
- Fresh WolfDeerEncounter play run: `Wolf: Wander | Deer: Dead`, `Health: 0/2 | Hits: 2 | Misses: 0 | Attack: 2`.
- Graphify AST update completed: 763 source files, 10,993 nodes. No multiplayer transport or multi-client runtime test was performed.

Limits: the injected clock supports authority-driven tests; production still uses its default UTC clock. The current scene remains a controlled encounter.
The failing-write test uses a rejecting log adapter; it does not prove disk-failure atomicity inside WorldDeltaLog or recovery from partial I/O.

### Needs and decisions validation

- Build: `dotnet build ProceduralPlanets.Tests.EditMode.csproj --no-restore -v quiet` passed with 0 errors and 20 warnings.
  Output: `local-only/animation-preflight/agent-needs-build.txt`. Warnings remain in the compiler/analyzer combination and existing Planet code.
- Unity initially reported `error CS0246: The type or namespace name 'ActorNeeds' could not be found (are you missing a using directive or an assembly reference?)`.
  A full asset refresh imported the new Game files. Compilation then passed without `error CS` entries.
- First regression job `3df49e3c905c4f1da1bf234fcaf4cd16`: 169/171 passed. Two needs tests used oversized movement steps and demoted their actors.
  Shutdown test: expected `0.019444444444444445d`, observed `0.016666666666666666d`.
  Reload test: expected `0.016666666666666666d`, observed `0.0083333333333333332d`.
  Normal frame stepping corrected the fixture; the production need arithmetic did not change for these failures.
- Final regression job `129b4ac812664d18adab5042dac47354`: 173/173 passed, no skipped tests.
- Fresh encounter completed with 2 hits, 0 misses, and a dead deer. The fixture still demonstrates the accepted hunt sequence.
- Inspector controls added: `WolfHunger` and `WolfThirst`. Status displays the selected objective.
- Live play-mode checks with explicit stepping: satisfied -> Roam; hungry -> Hunt/Stalk; thirst-prioritized -> FindWater; danger -> Escape/Flee.
  All four checks retained 0 hits while checking selection. Unity was left paused at the start of a hungry-wolf encounter.
- No source consumption, navigation reachability, threat-memory delay, or multiplayer transport claim is made by these checks.
