---
name: project-creature-residency
description: Creature residency spine and hunting (2026-08-26) - what is built, the traps it hit, and why a player cannot catch an animal on foot.
metadata:
  type: project
---

The residency spine from `docs/design/2026-08-26-creature-residency.md` §11 is **built and play-verified**
(2026-08-26, branch `harvest-vertical-slice`, uncommitted). Code in `Assets/Scripts/Planet/Creatures/`, tests
in `Assets/Tests/EditMode/CreatureResidencyTests.cs`, wired through `Planet`.

**The one keystone:** life support is owned by OBSERVATION, not by the spawner. A creature is simulated while
it is in an observer's bubble; existence is decided only by its slot's death record. Nothing may delete a
creature to save work.

**Key-space trap (already bitten once elsewhere):** a wild creature's derived `(territory, slot, generation)`
address and a host-minted `EntityId` share the delta log's entity space and could be the same number. Split
by a reserved owner tag — `EntityId.DerivedOwner = 0xFFFF`, which `EntityIdAllocator` now refuses to mint
from. The persistence key is the slot (generation zeroed); the generation lives in the record.

**Float-epoch trap:** `died + respawnSeconds - now` in `float` rounds a unix timestamp to the nearest ~128 s,
so a countdown reads as a constant while the clock moves. Subtract in `long` first. The original test used
`diedAt: 1000` and passed straight through the bug — **any expiry test must use a real timestamp.**

Deliberately left out, marked at the code site: the coarse tier (ladder is `full`/`record` only) and the
demotion record is in memory only, so a creature far from home is home again after a save/load.

**Follow-up landed same day — biome siting + `Placeholder Rabbit`.** Species carry a `Biomes` list (EMPTY =
ANY, so adding a species stays one line); siting reads the same `IBiomeProvider` scatter and the terrain bake
use. A slot now draws up to `CreatureTerritory.HomeAttempts` (5) candidates in its own cell and keeps the
first that passes ground AND biome — attempt 0 is byte-identical to the old single draw, so preferred spots
did not move. **Rejections are cached in `_barren` — not an optimisation: without it every unsuitable slot
re-runs the biome field once a second forever.** Measured plan cost 0.05–0.06 ms / 15 territories / 2 species,
so no per-tick creation cap was needed. Verified in play: Forest → 5 deer + 1 rabbit; Grassland → 6 rabbits,
nearest deer past 150 m.

**Threat + flee SHIPPED (`6623c34`), FSM redesigned (`3220878`), persistence closed (`381e353`).**

- **Presence is not threat.** Observers are POSITIONS (what is simulated); threats are ENTITIES with a faction
  (what is feared). That is why the debug freecam frightens nothing — it has no identity, so it cannot enter
  the threat list. **Invariant: never let the observer list grow an identity.**
- Faction relations are a **directed** table; `Wildlife→Wildlife = Neutral` is the single cell that makes deer
  ignore deer/rabbits/birds. The friendly-to-animals spell is a **disguise on the caster** (seen-as Wildlife,
  with an expiry), never a change to species data.
- **FSM keys states by an integer the state owns, not by `Type`** — so the dispatch key IS the persisted
  behaviour id and there is no mapping table to drift. Divergences from the harvest: a state acts on the frame
  it is entered, no block-timeout watchdog (that was the `Time.deltaTime` read), construction throws on
  duplicate ids.
- **One creature record per slot.** A death and a displacement share a key AND a delta space, so a
  displacement for the NEXT occupant would overwrite a lapsed death and reset the generation. **Every record
  carries the generation.** `CreatureRecordPolicy` keeps §5 (calm animal = zero bytes) and §6 (drifted home =
  record dropped) true; a moved-on generation is the one thing that keeps a record alive with nothing to say.


**Hunting SHIPPED (`644215f`), predator debug view SHIPPED (`9639163` + `e50bde6`).**

- A creature is a third harvest target beside a tree and a stump. `HarvestService.TryStrike` grants and
  announces; `CreatureResidencyService.Strike` owns health and routes death through the untouched `Kill`.
  Reused, not written: `ScatterPickMath.NearestAlongRay`, the `grantItem` delegate, `FleeState`.
- **Health is LIVE-only** — a wounded animal that leaves the bubble comes back whole. Deliberate: a wound byte
  on every deer breaks the zero-bytes-for-a-calm-animal promise. `ponytail:` at the site.
- **Being hit alarms an animal for 8 s regardless of the faction table.** Being attacked is an event, not a
  relation, and without it a friendly-spelled player can club a deer that never reacts.
- **TRAP — do not raise `ScatterHarvestedEvent` for anything that is not scatter.** `TreeFallSystem` and
  `ChopFxSystem` subscribe to it and will topple a tree and burst leaves wherever it says. Creatures got their
  own `CreatureStruckEvent`.
- **A player CANNOT catch an animal on foot, and that is not a bug.** `PlanetCharacterController` reports
  `ThreatRegistry.LocalPlayer` every tick; a deer notices at 45 m and flees at 2.4x walk. Melee is not the
  hunting verb — `creature.friendly <seconds>` is how the loop is play-tested until a ranged attack exists.
  **Do not "fix" it by lowering `AwarenessMeters`**; that trades a working flee for a working chase.

**TRAP — overlay draw order.** The console, the loading bar and the predator view all draw from
`RenderPipelineManager.endCameraRendering`, and delegate order is SUBSCRIPTION order. The console subscribes
at boot, so anything switched on later drew over it (the predator tint made the console unreadable). Fixed
with `IConsoleService.RaiseToTop()`, called when the predator view hooks. Any new fullscreen overlay must do
the same.


**Carcasses, swarms and birds SHIPPED (2026-08-26, `85d28ae` `e1f0dcc` `c3d5b83`).**

- **ANSWERED, do not re-derive: a point light lights NOTHING in this world.** URP is Forward+
  (`m_RenderingMode: 2` in `Assets/Settings/PC_Renderer.asset`), so the pipeline supports 256 additional
  lights — but `GetAdditionalLight` appears **zero times** in the tree. Terrain, grass, props and foliage all
  shade from `Includes/PlanetSunLighting.hlsl`, 40 lines of analytic sun. `FoliageLit`, `Scatter` and
  `PlanetVertexColor` declare `_ADDITIONAL_LIGHTS` and never read it — a dead keyword. Fireflies are emissive
  billboards (`Hidden/SwarmParticles`); real light spill means adding a light loop to those four shaders.
- **Carcass decay is a derived cache over ONE stored timestamp.** Stage = f(now − diedAt), so a body
  fast-forwards free across a save. Same shape as path wear over stamps.
- **TRAP — a slot key at generation 0 IS the id of the individual that died in it.** Both live in the delta
  log's entity space, so carcasses mint under `EntityId.CorpseOwner` (0xFFFE). `ScatterHarvestStore` now
  ignores entity records that are not host-minted; without that guard a carcass replayed as a phantom fallen
  log and dragged the log allocator's counter up with it.
- **Loot is on the BODY, not the killing blow.** Both go through `CreatureResidencyService.Strike` — the id's
  owner tag picks the path.
- **Never despawn in view** (Bryan, from Valheim): a spent carcass is removed only past
  `CreatureCorpseStore.KeepAliveMeters`. Buildings extend it later; `planned:` at the site.
- **Day/night for ambience is `dot(local up, sun direction)`, NEVER the global clock.** On a sphere the clock
  says nothing about whether it is dark here. Fireflies at noon on the far side is the bug it produces.
- **Swarms scatter off the THREAT registry, not the observer position** — so the freecam cannot startle flies,
  same invariant that keeps it from frightening a deer.
- **Flying needed NO new driver.** `IGroundingProvider.TryGround` already takes an offset above the surface,
  so a flier is `BodyHeightMeters * 0.5 + CruiseAltitudeMeters`. One number on a species. Do not build an
  `IFlyingProvider`. Ceiling: a bird cruises and never lands; perching is a third FSM state.

**OWED: play-verification of the FSM redesign and of the record plumbing.** Blocked on a workflow trap —
**the Unity editor throttles play mode to ~1/20 speed whenever it loses focus** (8 s of play time per 170 s
wall clock), so a planet generation takes about an hour unattended. `Application.runInBackground = true` only
helps while focused.

**Next, by capability forced (not by animal):** loot on death is DONE, so a boar is now a data row rather than
work. A wolf is blocked on the OTHER half of combat — a creature that damages a PLAYER. Health and damage now
exist for creatures only (`Resident.Health`, `HarvestService.TryStrike`); the player has neither. Bosses
additionally need a **fixed-location spawner**; today every territory gets N of every species, so
`PerTerritory 1` + never-expire has no way to say "this one place". Fish and birds need a non-grounded driver.

§13 answers recorded in the doc: lattice stays as the territory unit but the home draw inside a cell should
become a suitability draw (a uniform lattice went empty for 729 m near an unsuitable band); home range ≈ a
quarter of the cell (120 m at 490 m works); fast-forward should NOT kill. Multi-observer budget still open.

Related: [[project_world_delta_log]], [[project_magikos_architecture]].

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Creature residency spine](project_creature_residency.md) — 2026-08-26 §11 slice BUILT+play-verified. Life support owned by OBSERVATION, not the spawner. **Traps: a derived creature key and a minted EntityId share the delta log's entity space (split via `EntityId.DerivedOwner = 0xFFFF`); unix-epoch expiry math in `float` rounds to ~128 s, so an expiry test MUST use a real timestamp.**
