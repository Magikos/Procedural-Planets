# Reusable agent systems

Status: Foundation, persistent needs, stable objectives, and the survival scene are implemented and checked. Bryan accepted the current scene provisionally.
Current next action: Review the [expanded ecosystem fixture](2026-09-07-creature-ecosystem-prep.md), then connect persistent world authority. Preserve harvesting and corpse loot.
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

## Survival scenario slice

The current request adds feeding, drinking, rest, sleep, and a deer escape-speed control to WolfDeerEncounter.
The earlier validation sections describe their respective snapshots; this section supersedes the fixture's earlier lack of consumption.

- ActorResourceSource owns finite quantities and applies nutrition to ActorNeeds. Diet masks distinguish plants, meat, fresh water, and salt water.
- The existing CreatureBrain/FSM adds Feed, Drink, Rest, and Sleep. Existing persisted behavior IDs remain unchanged; new IDs append at 7–10.
- Feed and Drink approach the supplied source. The fixture validates current state, diet, availability, and range before consumption.
- A living deer supplies a hunt target. Its death creates meat once. The animation never grants nutrition or damage.
- Rest eligibility ends when hunger reaches 0.65 or thirst reaches 0.6. Six undisturbed seconds of rest permit sleep.
- Threats interrupt rest, sleep, feeding, and drinking through the same emergency transition.
- Available food scores above hunting at the same hunger. Ongoing feeding can continue below the initial search threshold.
- Bite wind-up tracks a moving target with bounded turning and braking. The hit still requires current range and facing.
- Death presentation removes lateral root travel so the carcass remains over its authority-owned source position.
- Disposed views deactivate immediately before Unity's deferred destruction. Repeated paused resets no longer show duplicate models.

The scene starts with wolf hunger 0.5, thirst 0.15, deer hunger 0.85, and thirst 0.65.
Accelerated needs take 80/160 simulated seconds to grow from zero to one. These are fixture settings, not production species rates.
The deer visits labeled plants and fresh water while the wolf rests, sleeps, wakes, stalks, and hunts.
Default deer escape speed is 40% of 6 m/s. The wolf chase speed is 4 m/s. A wound applies a 0.7 speed multiplier.
The inspector exposes these values. The "Test deer at 100%" button resets the scenario at full escape speed.
"Reset survival scenario" resets needs and stock while retaining movement settings. RestartRequested uses that same reset.

CreatureSurvivalClipAuthor authors ordinary editable clips using the existing rigs and LimbPoseSolver.
Both species receive Rest/Sleep clips. The wolf receives a feeding clip; drinking currently reuses each species' feeding clip.
Playables blend the poses. Rest disables procedural locomotion corrections while the authored folded-leg pose owns the skeleton.
These are prototype poses, not finished animation art. No third-party runtime code or packages were imported.

### Scope limits

The source quantity model and behaviors are reusable. The encounter remains a local authority fixture.
Known source positions stand in for perception. There is no vision occlusion, scent search, navigation graph, or reachability proof here.
The full-speed test can escape a hunt, but it does not establish long-term survival or realistic awareness memory.
Sleep follows a rest timer; fatigue, circadian timing, and sleep recovery are not implemented.
World residency does not yet supply source observations or enable rest. Production corpse inventory and player harvesting remain unchanged.
Before production consumption, connect existing corpse/item authorities so meat cannot be consumed twice or remove hide loot.
Source depletion in this fixture resets with the scene. Persistent creature hunger/thirst from the previous slice remains intact.
No networking runtime, pack behavior, blood trails, or production source persistence is claimed.

### Survival validation

- Final EditMode job `4366f937447e4a2bbce33658dca2b052`: **179/179 passed**, no skipped tests.
- Earlier job `5622dcb0e4584ae399405732e1687769`: 178/178 passed before the bite correction and its regression check.
- Tests cover stock conservation, incompatible diets, salt-water rejection, saturation, invalid quantities, sleep/wake, threat interruption,
  source approach/depletion, carcass preference, and tracking only before the hit marker. Existing selected regressions still pass.
- Test assembly build passed: 0 errors, 22 existing/analyzer warnings. Log: `local-only/animation-preflight/creature-survival-build.txt`.
- Editor assembly build passed after restore: 0 errors, 43 existing/analyzer warnings. Log: `local-only/animation-preflight/creature-survival-editor-build.txt`.
- The first editor build reported `error NETSDK1004: Assets file 'C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets\Temp\obj\ProceduralPlanets.Editor\project.assets.json' not found. Run a NuGet package restore to generate this file.`
  Building with normal restore generated the missing assets file. No dependency was added.
- The authoring tool initially reported missing ProceduralRigDefinition/LimbPoseSolver types. Adding the existing Magikos.Game assembly reference fixed compilation.
- Live default run: rest at 3 s, sleep at 9 s, stalk at 12 s, wound by 24 s, death/feed by 30 s, rest by 39 s, sleep by 45 s.
- Wolf hunger fell from about 0.88 before feeding to below 0.05. Meat fell from 1.50 to about 0.58. Two hits, zero misses.
- Deer plant stock fell from 2.00 to about 0.92. Fresh water fell from 10.00 to about 9.59 before the hunt ended.
- Full-speed run: deer health remained 2/2 through 60 simulated seconds; the wolf missed its bite. This is one controlled scenario.
- The first live run exposed wind-up overshoot: range stayed near 1.35 m while bearing exceeded 95 degrees at impact.
  Bounded wind-up tracking corrected that failure without accepting out-of-range or rear-facing hits.
- Captures: `local-only/animation-preflight/survival-rest-close.png`, `survival-feeding.png` (before carcass alignment),
  `survival-feeding-aligned.png`, and `survival-feeding-side.png` (after alignment).
- Unity console: no `error CS` or `Exception` entries after final compilation and live checks.
- Sleep pose comparison: `survival-sleep.png` and `survival-sleep-lowered.png`. The latter lowers the head toward the forelegs.
- Unity was left playing and paused at 1 simulated second, with two active animal models and the default 40% deer escape speed.
