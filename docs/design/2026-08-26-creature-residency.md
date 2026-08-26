# Creature residency — wildlife that stays where you left it

Status: **design, not started.** Written 2026-08-26 as a cold handoff: the executing session is assumed to
have no prior context, so every seam it needs is named with a path below.

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

## 12. Not in scope

Real AI behaviour (fleeing, aggro, pack hunting), combat, taming, breeding, animation blending and rig
import, the ambient tier, the M4 collision bubble, and the network transport. This slice ends at *"a
creature exists, persists, and can be found again."*

## 13. Open questions for Bryan

1. **Territory shape** — do spawners sit on a fixed lattice (one per chunk/tile), or are they placed by
   biome suitability like scatter?
2. **How far is "home"** — is a home range tens of metres or hundreds?
3. **Does fast-forward kill?** A creature unobserved for a month: does it ever die of the passage of time,
   or only from being killed?
4. **Simultaneous observers** — with 8 players, is the budget global or per-player?

None of these block the first slice; all four can be answered from watching it run.

## 14. Gotchas that will bite a cold session

- **Re-entering play generates a NEW world (new seed).** Stop → play breaks any before/after pixel or
  position comparison. Freeze and compare within one session.
- Console setters that mark a dirty flag publish on the controller's **next `Update`** — setting a value and
  rendering in the same call renders the old one.
- A shader reporting zero messages straight after `AssetDatabase.ImportAsset` is **not** proven; variants
  compile on use, and failures appear only in the Unity console.
- Unity auto-refresh is **off** in this project: saving a `.cs` does not recompile. Play mode blocks domain
  reload.
- The dirty worktree is normal and sacred. Never discard, stash over, or clean changes you did not make.
