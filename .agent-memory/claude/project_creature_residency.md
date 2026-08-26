---
name: project-creature-residency
description: Creature residency spine (first slice, 2026-08-26) — what it is, the two traps it hit, and what was deliberately left out.
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

**OWED: play-verification of the FSM redesign and of the record plumbing.** Blocked on a workflow trap —
**the Unity editor throttles play mode to ~1/20 speed whenever it loses focus** (8 s of play time per 170 s
wall clock), so a planet generation takes about an hour unattended. `Application.runInBackground = true` only
helps while focused.

**Next, by capability forced (not by animal):** boar + loot on death (reuses `HarvestYield`/`InventoryService`;
turns `creature.kill` into a gameplay verb). Wolf, troll and bosses are blocked on **combat** — no health or
damage exists anywhere in the tree. Bosses additionally need a **fixed-location spawner**; today every
territory gets N of every species, so `PerTerritory 1` + never-expire has no way to say "this one place".
Fish and birds need a non-grounded driver.

§13 answers recorded in the doc: lattice stays as the territory unit but the home draw inside a cell should
become a suitability draw (a uniform lattice went empty for 729 m near an unsuitable band); home range ≈ a
quarter of the cell (120 m at 490 m works); fast-forward should NOT kill. Multi-observer budget still open.

Related: [[project_world_delta_log]], [[project_magikos_architecture]].
