# Creature residency — wildlife that stays where you left it

Status: **§11 first slice built and verified, 2026-08-26.** The rest is still design. Written as a cold
handoff: the executing session is assumed to have no prior context, so every seam it needs is named with a
path below. §11 now carries the evidence for each done-condition; §13 carries the answers watching it run
produced.

Parent: [the game architecture](2026-08-20-magikos-game-architecture.md). This is the ambient/creature
layer, sitting on that document's keystone (*world = seed + exception log; promote to GameObject only while
interactive*). It does **not** open ADR-7 creature AI, which stays beyond M6.

---

## 1. The requirement, in Bryan's words

> I do not want the classic car video game where I see a red car, it drives past me and I turn around and
> the red car is gone.

> In Valheim, if I am wandering through the forest and come up on a Troll and run away, if I later come back
> I can find that Troll again, kill it, etc.

> I'd ideally like the idea of seeing a deer and then coming back to that area later and tracking it down —
> like the Troll. But also later when we add in monsters and bosses, etc — it should work the same.

The player-visible outcome: **a creature you have seen is still there when you come back**, and chasing it
away from where it started never makes it vanish.

## 2. Two tiers, permanently separate

The requirement above is about **identity**. Nobody tracks a firefly; everybody tracks the troll.

| | Ambient | Creatures |
| --- | --- | --- |
| fireflies, butterflies, distant flocks, background fish | deer, wolves, trolls, bosses, catchable fish |
| **no identity** — may pop freely | **identity** — persists, findable again |
| GPU only: `ComputeBuffer` + compute advance + `DrawProcedural`, no GameObjects | promoted to a GameObject only while interactive |
| client-side cosmetic, never replicated | server-authoritative |
| template: `Assets/Scripts/Planet/Precipitation/RainParticleController.cs` | this document |

**Do not merge these.** The ambient tier is decoration and must stay free of identity, records and
authority, or it will cost per-firefly storage and network traffic for no player-visible gain.

The ambient tier is genuinely cheap and can be built at any time — `RainParticleController` already solves
the hard parts on a sphere (camera-radius bubble with forward-cone bias, altitude fade, wind coupling,
recycling around a moving camera). It is **not** the first slice, because it exercises none of section 1.

## 3. Decisions already made (Bryan, 2026-08-26)

1. **Creatures drift home.** A chased animal returns to its home range over time rather than staying where
   it was abandoned.
2. **Some deaths are permanent, some regenerate.** Resolved by the expiry model in §6 — one mechanism, the
   difference is a number in species data.
3. **Budget scales with a hard minimum.** Resolved by the rule in §8.
4. **Build authority-shaped now**, single-player-shaped later is a retrofit we are not doing.

## 4. The model

Three separate questions that must not be conflated:

**Birth — owned by the spawner.** A spawn node declares what a territory supports: *"this valley supports 3
deer."* Derived from the world seed, deterministic, identical on every machine, zero storage. Once a
creature exists the spawner has no further say.

**Life support — owned by observation, not by the spawner.** A creature is simulated while it is inside
**any** observer's bubble. This is the single most important line in the document: tying lifetime to
proximity-to-spawner is exactly what breaks the hunt. With the split, chasing a deer two kilometres from its
valley is not a special case — it is in your bubble, so it is simulated, and the spawner is irrelevant.

**Absence — demote, never delete.** When no observer can see it, a creature is written down as a record
(id, species, last position, state, timestamp) and stops being simulated. On re-observation it is
**fast-forwarded**: time is advanced cheaply — it drifted home, healed, aged, possibly died — then promoted
back. Continuous *feeling*, near-zero cost while unobserved.

That is the same ladder the scatter system already uses (mesh → impostor → nothing), applied to simulation:

| tier | when | cost |
| --- | --- | --- |
| **full** | inside an observer's bubble | AI, motor, animation |
| **coarse** | just outside, or over budget | position integrated toward home, no animation |
| **record** | unobserved | nothing until re-observed, then fast-forward |

## 5. Identity: seed plus exception

A wild creature that has never done anything interesting must cost **zero bytes**.

- **Derived identity.** A creature's identity is `(spawnerId, slot, generation)`, computed from the world
  seed. No allocation, no record, and two processes agree without talking —
  `ISeedProvider.GetSeedForEntity(ulong entityKey)` exists for exactly this and says so in its own summary:
  *"a random-looking value that two processes must agree on."*
  (`Assets/Scripts/Core/Interfaces/ISeedProvider.cs`)
- **Exceptional identity.** The moment a creature becomes notable — killed, wounded, tamed, named, moved far
  from home — it gets a record in the delta log. Only then does it consume storage, and only then does it
  need a minted `EntityId`.

**`generation` is not optional.** When a slot repopulates it must be a *new* creature. Without a generation
counter you kill deer #7, return, and find deer #7 alive — worse than a fresh deer. Increment it on each
expiry; still fully derived, still zero storage.

## 6. Death is an exception with an expiry

This is the unification. **Respawn and population control are the same mechanism**, and species differ only
by a number:

- Kill a creature → an `EntityRemoved` record carrying the species' respawn time.
- Resolving a territory's population = count the slots whose death record is **still live**.
- A death record that has **expired** stops suppressing its slot, so the slot repopulates on its own with
  the next `generation`.

| species | death record expiry |
| --- | --- |
| deer | days |
| troll | about a week |
| **boss** | **never** |

A unique boss at a fixed location is just a spawner with count 1 and no expiry. No separate boss system.

**The log shrinks back toward the seed.** Once a death record expires, world state is once again the seed
default, so the record can be dropped at compaction. This is the opposite of the usual save-file growth
curve and it is worth protecting.

**Copy the expiry mechanics that already exist.** `Assets/Scripts/Planet/Surface/SurfaceEditController.cs`
already does exactly this shape for path wear and scorch: a payload carrying `createdUnixSeconds` +
`regrowSeconds`, a `PruneExpired` pass, and tombstones appended when an entry lapses. Read it before
inventing anything.

## 7. Population: simulate the outcome, not the mechanism

Bryan asked whether to simulate age, birth and death. **Recommendation: no.**

A real lifecycle simulation is a large system with unpleasant emergent failures (populations crash or
explode), it burns CPU continuously while unobserved, and no player can perceive whether a deer is four
years old or six. What is actually wanted is *a world whose populations respond*, and that comes from making
the **slot count** respond — to season, biome, predation pressure — not from simulating N individual
lifespans.

One exception, already handled by §5: a **tamed or named** animal is by definition an exception with a real
record, so it can carry genuine state — age, health, breeding, name. Wild seed-derived animals get the cheap
model; individuals that matter get the real one.

## 8. Budget: fidelity, never existence

**The budget controls how expensively something is simulated. It never controls whether it exists.**

Existence is decided by the record. Overflow demotes a creature down the §4 ladder; it never deletes one.
Without this rule a cap quietly reintroduces the red-car problem through the back door.

The hard minimum is a gameplay guarantee rather than a number: **anything you can see, or that can see you,
is fully simulated regardless of budget.** Above that floor, spend what is left by priority — distance,
then exceptional/named, then species weight, so a boss outranks a rabbit. The cap itself becomes a quality
tier alongside the existing ones.

## 9. What already exists — verified 2026-08-26

The executing session should not rebuild any of this.

**A creature is an actor, and the actor stack is already planet-free.**
- `Assets/Scripts/Game/Actors/CharacterMotor.cs`, `SurfaceCharacterController.cs`, `CharacterPose.cs`
  (which also holds `CharacterMath`), `IGravityProvider.cs`, `IGroundingProvider.cs` all live in the
  **`Magikos.Game`** assembly, whose `.asmdef` declares **zero references** — not URP, not InputSystem, not
  even `ProceduralPlanets.Core`. A planet type cannot be reached from inside it, by construction.
- `IGroundingProvider.TryGround(Vector3 worldPos, Vector3 downDir, float footOffset, out GroundResult)` is a
  **stateless positional query** — no actor identity, no colliders, no NavMesh. Any number of creatures may
  call it. `PlanetSurfaceGrounding` answers it analytically;
  `Assets/Scripts/Planet/AssetBench/AssetBenchService.cs` is already a second consumer, so this is proven
  outside the character.
  **Creature locomotion therefore does not wait on the M4 collision bubble.**
- `Assets/Scripts/Game/Actors/ActorIntent.cs` — `readonly struct ActorIntent { Vector2 Move; Vector2 Look;
  ActorButtons Buttons; uint Tick; }` — and `IInputProvider.cs`. **An AI brain is just another
  `IInputProvider`.** The seam exists precisely so a server can tick an actor with no input device;
  `Assets/Scripts/Planet/Character/LocalPlayerInput.cs` is the keyboard implementation to model against.

**Persistence is built.**
- `Assets/Scripts/Core/World/WorldDeltaLog.cs` — append-only, `SchemaVersion = 2`, size-triggered
  compaction, background-safe open/replay. `Assets/Scripts/Core/World/WorldDelta.cs` — `FixedBytes = 52`
  (sequence, kind, key, payloadLength, position, rotation, typeIndex, state, scale) + optional payload +
  4-byte FNV-1a checksum.
- `DeltaKind` already has `EntitySpawned = 4`, `EntityMoved = 5`, `EntityRemoved = 6`, `EntityState = 7`,
  and `WorldDeltaLog.SpaceOf` already collapses those four into **one live record per entity** (space 5).
  Creature records should use these rather than adding kinds.
- `Assets/Scripts/Core/World/EntityId.cs` — 16-bit owner tag + 48-bit counter, `HostOwner = 0`,
  `IsAuthoritative => Owner == HostOwner`. **`EntityIdAllocator.Observe` must be shown every saved id on
  load or the next mint collides.**

**Placement inputs.**
- Biome resolution and **signed** altitude: `altitudeMeters = (localRadius - SeaRadiusLocal) * scale`.
  Negative is underwater — a depth band is an ordinary altitude gate, needing no new axis. This is how the
  ocean coral prototypes are placed; do not invent a depth concept.
- `Assets/Graphics/Shaders/Includes/WaterLevelField.hlsl` and `WaterQueryService` are the authority for
  *where water is*, per direction. The global sea sphere is **not** — a lake can sit ~97 m above sea level.
- `Assets/Scripts/Planet/Scatter/ScatterTileCache.cs` holds a working residency bubble, re-planned only
  after `ReevalMoveMeters = 40f` of camera travel. Read it before designing a new bubble; the architecture
  doc intends **one** bubble to eventually serve collision, scatter and replication interest.

**Art.** `docs/research/2026-08-10-external-asset-catalog.md` §C2/§B2 — Polyperfect Low Poly Animated
Animals: 68 species, all rigged (`animationType: 2` Generic, per-animal Avatar), including 12 birds and 13
aquatic species with swim clips. The catalog's warning that *"nothing supplies wildlife AI that works on a
sphere"* is about **their AI controllers**, which need colliders and a NavMesh — it does not apply to using
their meshes and clips with our own motor.

## 10. Constraints

From `CLAUDE.md`, non-negotiable:
- **Awaitable only.** No coroutines, no `async void`, no `Task.Run`. Expensive one-shot work goes to
  `Awaitable.BackgroundThreadAsync`; per-frame hot work is Burst or compute.
- **`ILogger` / `LoggerProvider`**, never `UnityEngine.Debug.Log*`.
- Settings are **SO for authoring, DTO at runtime**, with a static `From(SO)` factory; console commands
  mutate the runtime DTO through the settings service, never the asset.
- Services default to plain classes; one orchestrator MonoBehaviour forwards `Update` and owns disposal in
  reverse init order. No `[DefaultExecutionOrder]`, no new `RuntimeInitializeOnLoadMethod`.
- World-scoped services register via `IWorldServiceRegistrar`; never retain world services across
  `WorldReadyEvent`.
- Tests: Unity Test Framework EditMode only, at `Assets/Tests/EditMode/`. **A test must earn its place.**
  Pure logic qualifies — slot/generation resolution, expiry arithmetic, record round-trips. Feel and
  rendering do not.

From the architecture doc, because decision 3 of §3 says build authority-shaped:
- **No authoritative code path may read a camera, an input device or the GPU.** Creature simulation is
  authority. The observer bubble is defined by *player positions*, which the server has, not by a camera.

## 11. First slice

**The residency spine, with one placeholder animal.** Not fireflies — fireflies test none of §1.

Definition of done:
1. A spawner declares N of one species in a territory, derived from the seed. Two runs of the same seed
   produce the same creatures in the same places.
2. The creature walks the sphere using `CharacterMotor` driven by a trivial wander `IInputProvider`. No
   colliders.
3. Walk away until it demotes; walk back; it is **still there**, fast-forwarded and drifted toward home.
4. Kill it (a console command is fine — no combat system yet). It stays dead across a save/load.
5. Set its respawn expiry short, wait it out, and the slot repopulates with a **new** generation id.
6. Set expiry to never and it stays dead permanently — the boss case, same code path.
7. EditMode tests for slot/generation resolution and expiry arithmetic. The rest verified in play.

### Built, 2026-08-26

`Assets/Scripts/Planet/Creatures/` — `CreatureKey` (derived identity), `CreatureTerritory` (birth + drift
home), `CreatureDeathRecord` + `CreatureDeathCodec` (expiry, delta-log encoding), `CreatureResidencyService`
(the ladder, `creature.*` commands), `CreatureWanderInput` (the brain, an `IInputProvider`), `CreatureView`
(capsules), `CreatureLibrary` SO + `CreatureLibraryDto`. Wired through `Planet`, which resolves the observer
POSITION and hands it over — the service itself never sees a camera. Tests in
`Assets/Tests/EditMode/CreatureResidencyTests.cs`.

Evidence, all at seed 1691104419 on face 5 territory (11,9):

| condition | evidence |
| --- | --- |
| 1 derived, deterministic | the bubble-local id set is byte-identical across two independent runs (domain reload + full regeneration): 14 ids, `diff` clean |
| 2 walks the sphere | capsule grounded and wandering in the forest; `fromHome` advances every tick; no colliders |
| 3 away and back | 3 km away → live 3→0 with all 91 residents kept; back after ~75 s → the SAME three ids, `fromHome` 21→2, 23→2, 30→14 |
| 4 stays dead across save/load | `C5/4:11,9#0g0` killed, then stop → recompile → domain reload → regenerate: still `suppressed` from the on-disk log |
| 5 expiry repopulates a NEW generation | two independent lapses — a 300 s record produced live `#0g1`, a 25 s record produced `#2g1` |
| 6 never-expire stays dead | `creature.respawn 0` then kill → `suppressed=forever`, still forever after a reload |
| 7 tests | 182/182 EditMode green (23 new) |

**Follow-up landed 2026-08-26 — biome siting + a second species.** `CreatureSpecies` gained a `Biomes` list
(empty = any, so a new species stays a one-line addition) and placement now gates on biome as well as the
signed altitude band, answering §13 question 1 — details there. A second placeholder, `Placeholder Rabbit`,
exists specifically to prove the gate: it lives in Grassland/Scrub/Steppe/Savanna, biomes the deer refuses,
so crossing a biome line visibly swaps which animal is around you. Measured at one Forest spot: 5 deer live,
1 rabbit. At one Grassland spot: 6 rabbits live and the nearest deer pushed out past 150 m. Both remain
capsules — 1.7 m brown and 0.45 m pale — per Bryan's standing call that placeholder art is fine while
mechanics are built. 189/189 EditMode tests green.

Two defects were found by running it and fixed: forcing a re-plan also cleared the position the console
measured distances from (`d=Infinity`, and `creature.kill` finding nothing), and `SecondsUntilRespawn` did
unix-epoch arithmetic in `float`, which rounds the epoch to the nearest ~128 s so the countdown read as a
constant. Suppression itself was always long arithmetic and correct. The second now has a test at a real
timestamp — the original test used `diedAt: 1000`, which is small enough to hide it.

**Deliberately not built, and why** (both marked at the code site): the coarse tier of §4 — the ladder is
`full` and `record` only, because no §11 condition distinguishes coarse from record and the budget of §8 is
not in this slice; and the demotion record is in MEMORY only, so a creature left far from home is back home
after a save/load. That is a maximal fast-forward rather than a lost creature, and it keeps §5's promise that
an animal which has done nothing notable costs zero bytes. The upgrade is one `EntityMoved` record on
demotion past a displacement threshold — which wants question 2 below answered first.

## 12. Not in scope

Real AI behaviour (fleeing, aggro, pack hunting), combat, taming, breeding, animation blending and rig
import, the ambient tier, the M4 collision bubble, and the network transport. This slice ends at *"a
creature exists, persists, and can be found again."*

## 13. Open questions for Bryan

None of these blocked the first slice. Three are now answered from watching it run; the fourth is not,
and saying so is more useful than a guess.

1. **Territory shape** — **both, at different levels. ANSWERED AND BUILT, 2026-08-26.** The lattice stays as
   the territory unit (level 4: 16x16 cells per cube face, 1,536 territories, ~490 m across) because the cell
   address is what makes the id derivable and the save key stable — a suitability field cannot do that job.
   But a uniform lattice with ONE home draw per slot goes empty wherever that single point fails the gate:
   standing on a shelf just under sea level, the nearest creature was **729 m away** with every local slot
   rejected, which a player reads as "this world has no animals" rather than "this patch is unsuitable".

   Built: `CreatureTerritory.HomeCandidate(seeds, slot, attempt)` draws up to `HomeAttempts` (5) points
   inside the same cell, and the service keeps the first whose ground AND biome the species accepts.
   Attempt 0 is unchanged from the single-draw version, so a slot's preferred spot did not move — the retry
   only changes where it settles for. Siting now reads the same `IBiomeProvider` the terrain bake and scatter
   placement use, so a creature cannot disagree with the ground about which biome it stands in.

   Two things fell out of building it:
   - A rejection is permanent (suitability is seed-derived), so rejected slots are cached in `_barren`.
     Without it every unsuitable slot in the bubble re-runs the biome field once a second, forever.
   - Measured plan cost with the cache: **0.05–0.06 ms for 15 territories** across two species. No spreading
     over frames is needed, and the "cap new residents per tick" idea was dropped as unnecessary.

2. **How far is "home"** — **roughly a quarter of the territory: 100–150 m.** At 120 m against a 490 m cell,
   observed `fromHome` ranged 2–65 m in normal wandering and the homing pull reeled creatures back
   reliably. A territory's three animals stayed distinguishable and did not merge into the neighbouring
   territory's group, which is what starts to happen as the range approaches half the cell.

3. **Does fast-forward kill?** — **Recommend no, and the slice supports it.** Drifting home at 0.8 m/s
   already supplies the whole "the world moved on while you were away" feeling: a 75 s absence visibly
   relocated every creature toward home. Adding a death-by-time term buys no player-visible outcome and
   reintroduces exactly the population crash/explosion failure §7 rejects. Deaths stay exceptional.

4. **Simultaneous observers** — **unanswered; nothing in the slice exercised it.** `Tick` takes one observer
   position today. The multi-observer shape is a union of bubbles with a creature's tier decided by its
   NEAREST observer, which follows from §4 without a new idea — but whether the budget is global or
   per-player depends on measurements that need more than one player to make, so it stays open.

## 14. Gotchas that will bite a cold session

- ~~**Re-entering play generates a NEW world (new seed).**~~ **Measured false for this scene, 2026-08-26.**
  `SceneBootstrap.WorldSeed` is a serialized field fixed at `12345` (`SceneBootstrap.cs:55`), used unless a
  `WorldLoadRequest` overrides it (`:79`), so `Planet.Seed` came back as `1691104419` across three separate
  play sessions with a domain reload between them. That stability is what made the §11 save/load conditions
  testable at all — a creature death written in one session was still suppressing its slot in the next.
  The original warning still applies to anything that DOES randomise the seed, and to before/after *pixel*
  comparisons, where a full regeneration changes far more than the seed.
- Console setters that mark a dirty flag publish on the controller's **next `Update`** — setting a value and
  rendering in the same call renders the old one.
- A shader reporting zero messages straight after `AssetDatabase.ImportAsset` is **not** proven; variants
  compile on use, and failures appear only in the Unity console.
- Unity auto-refresh is **off** in this project: saving a `.cs` does not recompile. Play mode blocks domain
  reload.
- The dirty worktree is normal and sacred. Never discard, stash over, or clean changes you did not make.

---

## 15. Behaviour: the state machine decision (2026-08-26)

Not part of the §11 slice. Recorded now because the constraint in "The load-bearing part" below is free to
honour today and expensive to retrofit, and because someone will otherwise build behaviour on live objects.

**Decision: harvest `AdaptiveStateMachine<TContext>` from Bryan's State Machine project as the creature
behaviour backbone, when the second behaviour arrives — not before.**

Source: `C:\Users\Bryan\Source\Repos\Magikorp\State Machine\Assets\Scripts\StateMachine\`. Verified present
and re-read 2026-08-26: `AdaptiveStateMachine.cs`, `Interfaces/IState.cs`, `CompositeState.cs`,
`StateTransition.cs`. The wider project is a WIP skeleton whose motor is empty and entirely collider-based —
**harvest the FSM, not the character code.**

### Why this FSM, and why not something else

Three of its features are why it beats a hand-rolled switch for animals specifically:

- `ResolveTo(from, ctx) => Type` — the target is chosen by context at transition time, so "flee exits back to
  whatever I was doing" does not require every state to know every other state.
- `EvaluateExit(ctx) => Type?` — a state ends *itself* ("done eating", "lost the scent") instead of a
  transition table polling for a condition only that state can see.
- `CompositeState` — `Alive{Idle, Wander, Flee}` / `Dead` is the natural creature shape, and a pack's
  `Hunting` composite nests inside it without flattening the whole table.

Not a behaviour tree, not utility AI. BTs earn their keep on deep reactive composition; utility AI on many
competing drives. Deer, wolves, trolls and a boss need neither, and this FSM is already owned and understood.

### The load-bearing part: behaviour persists as an id, not as a live object

`AdaptiveStateMachine` keeps `_currentState` **inside the machine** (`AdaptiveStateMachine.cs:19`). A machine
is a live object, and §4 demotion destroys live objects. Build behaviour on the machine alone and a wolf that
was hunting you is idle when you come back — the §1 red-car problem, one layer up from where this document
solved it.

So the rule, decided now:

> A creature's behaviour state is persisted as a small id and re-entered on promotion. The FSM instance is a
> promotion-time artefact, never the source of truth.

This costs nothing to honour. `WorldDelta.State` is already a byte, unused by the death record, and §4 already
lists "state" in what a demotion writes down. It lands on the **same** work as the `EntityMoved` demotion
record already marked `ponytail:` in `CreatureResidencyService.Demote` — build the two together, put the
behaviour id in `State`, and the FSM drops in behind `IInputProvider` with no rework: a
`CreatureBrain : IInputProvider` owning a machine whose states write an `ActorIntent`. The seam the §11 slice
already built takes it unchanged.

### Trigger

The second behaviour with a real transition. Flee-from-player is the natural one: the moment behaviour is
wander/flee/return, hand-rolled branching starts losing. Until then `CreatureWanderInput` is ~60 lines, and an
FSM wrapped around a single state with zero transitions is the speculative infrastructure this project bans.

### Three things the port must fix — not optional

1. **`Time.deltaTime` inside the machine** (`AdaptiveStateMachine.cs:120`, the block-timeout watchdog).
   Authority code cannot read static Time: a fast-forward has no frames, and a dedicated server's
   `Time.deltaTime` does not mean what the caller assumes. `dt` comes from the injected context.
2. **`Logwin` and `Debug.LogError`** throughout → `ILogger`. Logwin is a plugin this project does not have.
3. **The states are not stateless.** `.agent-memory` claimed "context injected per-update, states hold no
   data" and that is **wrong**: `FallingState.Enter` writes `AirControlFactor` onto the *state instance*
   (`Airborne/FallingState.cs`). Sharing one state set across creatures would cross-contaminate them. Take one
   machine and one state set per **LIVE** creature — records need no machine, so the count is bounded by the
   bubble rather than by the lattice — or make "no instance fields on a state" a porting rule and enforce it.

Skipped from that project, unchanged from the earlier harvest: its EventBus, Singleton, Logwin,
PlayerInputProvider, all physics motor/sensor code, and the empty `ICharacter*Context` interfaces.

---

## 16. Threat: what a creature is actually afraid of (2026-08-26)

The flee behaviour of §15 needs an answer to "flee from *what*". Getting that wrong makes a deer bolt from a
debug camera, and makes "deer do not run from other deer" a special case written per species.

### Presence is not threat

`CreatureResidencyService.Tick` takes a `Vector3`, not an entity, because authority code must not know a
camera exists (§10). The consequence is the fix:

| list | contents | decides |
| --- | --- | --- |
| **observers** | positions | what is **simulated** |
| **threats** | entity id + position + faction | what is **feared** |

The free camera is in the first and can never be in the second, because a threat query needs an identity and
a debug camera has none. So flying around debugging leaves the wildlife grazing, and that is the existing
split doing its job rather than a special case for the camera.

**The invariant to protect: the observer list must never grow an identity.** The moment it does, presence and
threat collapse back together and the camera becomes a predator again.

### Relations are directed

A symmetric "are we friends" flag cannot express the pair we need: a wolf *hunts* a deer, a deer is *afraid
of* a wolf. Same pair, different relation each way. So the table is directed - how the ROW feels about the
COLUMN:

| | → Wildlife | → Predator | → Player |
| --- | --- | --- | --- |
| **Wildlife** | Neutral | Afraid | Afraid |
| **Predator** | Hostile | Neutral | Hostile |
| **Player** | Neutral | Neutral | Neutral |

Bryan's requirement that deer ignore deer, rabbits and birds is one cell - `Wildlife → Wildlife = Neutral` -
rather than a rule written per species. Adding a species means picking a faction, not editing a list of who
fears whom.

**Decided (Bryan, 2026-08-26): the player is a threat to wildlife by default**, armed or not. Deer bolt on
sight. It is the simplest rule and it is what makes the friendly spell feel like it did something.

### The spell decides the shape

A friendly-to-animals spell is the requirement that rules out the obvious implementation. If "deer fear
players" were baked into the deer's species data, the spell would have to mutate species data - globally, for
every player, permanently. Wrong three times over. So the lookup is:

> `Relation(me, them)` = `base(myFaction, theirFaction)`, **overridden by an active effect on THEM**

The spell puts a temporary effect on the *caster*: to animals, they count as Wildlife. One caster, one
duration, species data untouched. Two properties are not optional:

- **Authority-side.** A client cannot declare itself friendly to the server's wolves.
- **It is a RECORD with an expiry**, not a live flag - `startedUnixSeconds + durationSeconds`, held in memory
  today. Bryan's instinct is that active spells should survive a save; that is not designed yet, but shaping
  the effect as a record now means persisting it later is one `Append` against the existing
  `DeltaKind.EntityState` rather than a rewrite. This is §15's lesson a second time: **do not build state
  that exists only as a live object.**

That expiry is the **third** use of `createdUnix + durationSeconds` in this codebase, after path wear
(`SurfaceEditController`) and creature death (`CreatureDeathRecord`). Rule of three is reached; a shared
helper is now worth considering, but after the third use is working, not before.

### Loyalty is a second axis, and must not merge with faction

- **Faction** - species-level, shared, **zero bytes per wild creature**. Answers "is that a threat".
- **Loyalty** - per-INDIVIDUAL, a scalar, for taming. §7 already establishes that a tamed animal is an
  exception carrying a real record, which is the natural home for it.

Merging them would put a per-individual byte on every deer on the planet and kill §5's promise that a
creature which has done nothing interesting costs nothing. Faction stays free; loyalty is what an animal
costs once it matters.

Free consequence of the same model: when one deer flees, alerting nearby creatures of the SAME faction gives
herd panic with no herd system. Not built, but the model does not fight it.

### What flee needs beyond relations

- **Awareness radius** per species - a deer notices further than a rabbit.
- **Detection is distance-only.** Line of sight would need raycasts against terrain that has no colliders; an
  analytic horizon test is possible but is real work. Ceiling named rather than paid.
- **Flee heading** away from the threat along the surface - the great-circle math already exists.
- **An exit condition** - threat gone or far enough. This is what `EvaluateExit` is for.
- **Return home** - the homing pull already in the wander brain.
- **A persisted behaviour id** (§15). Without it a deer that was fleeing when you left is calm when you
  return, which is the red-car problem one layer up from where §1 solved it.

Cost is not a concern: live creatures are bubble-bounded (tens) and threats are single digits. Even
creature-versus-creature is a few hundred checks a tick. Ceiling named, not optimised.

### Testing it without a player

If the free camera is not a threat - and it must not be - flee needs another way to provoke. `character.spawn`
gives a real player and is the honest path. `creature.threat <seconds>` plants a temporary threat source at
the camera for quick checks, and `creature.friendly <seconds>` exercises the spell override before any spell
system exists.

### Behaviour, as built (2026-08-26)

The machine landed as `Assets/Scripts/Game/Ai/AdaptiveStateMachine.cs` in `Magikos.Game`, which is where it
belongs: zero assembly references, so it can drive a player as readily as an animal. Four things diverge from
the harvested original, and each is a defect that was found rather than a preference:

1. **States are keyed by a small integer they own, not by `Type`.** Type keys made "which state is running"
   and "what gets written down" two different values joined by a hand-written mapping. With an integer key
   the id IS the key, so the persist-as-an-id rule above is structural rather than a convention. Restoring is
   `Start(ref ctx, savedId)`.
2. **A state acts on the frame it is entered.** The original returned after switching, spending that tick on
   the transition - visible as an animal freezing for a beat before it runs.
3. **No block-timeout watchdog**, which is where the `Time.deltaTime` read lived. Authority code cannot read
   a static clock; a fast-forward has no frames. Nothing blocks yet, so nothing is lost.
4. **Construction refuses** duplicate ids, transitions from unknown states, and transitions missing a
   condition or target. Each of those used to produce an actor that simply never entered a state.

Not built: `CompositeState`. Nothing needs nesting yet and the integer keying does not foreclose it - a
composite is a state that owns a sub-machine and forwards to it.

### Persistence, closed 2026-08-26

Both `ponytail:` markers on demotion are discharged. §13 question 2 answered the home range, which was the
stated blocker: a displacement is worth a byte once a creature is beyond half of it, or is doing anything
other than wandering.

**One record per slot, not two.** A death and a displacement would share a key AND a delta space, so the later
write replaces the earlier one. That is correct in one direction - a dead creature is not also out wandering -
but a trap in the other: a displacement written for the slot's NEXT occupant would overwrite a lapsed death
and take the generation counter with it, and a repopulated slot silently falling back to generation 0 is
exactly what §5 forbids. Every record therefore carries the generation whatever else it says, and
`CreatureRecord` covers both cases behind one codec.

**The log still shrinks back toward the seed.** `CreatureRecordPolicy` is that rule, kept pure because it is
what decides whether the save grows:

| situation | action |
| --- | --- |
| calm, near home, never killed | nothing written (§5) |
| beyond half its home range, or not wandering | written |
| drifted home again | record DROPPED (§6) |
| generation has moved on | record kept even with nothing else to say |

That last row is the one that is easy to get wrong. The generation counter lives nowhere else, so forgetting
the record would put the slot back to generation 0 and resurrect an animal the player already killed.

Payload format 2 adds a state byte and the behaviour. Format 1 was death-only and is still read, because it is
already in Bryan's save - reading it as anything but a death would resurrect killed creatures.

`creature.deaths` and `creature.clear-deaths` became `creature.records` and `creature.clear-records`, since
deaths are no longer the only thing recorded.

**Verification gap, stated plainly.** The decision rule, both record round-trips, generation preservation
across the collapse, and the format-1 migration are covered by tests (230/230 green). The PLUMBING either side
of it - writing on demote, seeding a resident from a saved record - is not, and could not be play-tested
unattended: the editor throttles to roughly a twentieth speed whenever it loses focus, putting one planet
generation near an hour. Worth one pass when someone is at the machine: displace a creature, let it demote,
`creature.records` should show it; then stop, play, and it should still be there doing the same thing.
