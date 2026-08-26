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

**Next up, in order (chosen by capability forced, not by animal):** deer + flee → the §15 FSM trigger, which
is the same work as the `EntityMoved` demotion record and persist-behaviour-as-an-id. Then boar + loot on
death (reuses `HarvestYield`/`InventoryService`). Wolf, troll and bosses are blocked on combat — there is no
health or damage anywhere in the tree. Bosses additionally need a FIXED-LOCATION spawner; today every
territory gets N of every species, so `PerTerritory 1` + never-expire has no way to say "this one place".
Fish and birds need a non-grounded driver.

§13 answers recorded in the doc: lattice stays as the territory unit but the home draw inside a cell should
become a suitability draw (a uniform lattice went empty for 729 m near an unsuitable band); home range ≈ a
quarter of the cell (120 m at 490 m works); fast-forward should NOT kill. Multi-observer budget still open.

Related: [[project_world_delta_log]], [[project_magikos_architecture]].
