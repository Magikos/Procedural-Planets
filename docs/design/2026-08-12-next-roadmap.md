# What's next — prioritized roadmap (2026-08-12)

> **Extended 2026-08-20 by [2026-08-20-magikos-game-architecture.md](2026-08-20-magikos-game-architecture.md),** which is the current architecture and milestone doc of record for the game layer. This roadmap's three-track framing (A harvest / B character feel / C code health) still holds for the near term; the newer doc adds the game systems above it, the 8-player constraint, and the M0–M6 sequence. Where the two disagree, the newer doc wins.

Written against commit `17a8672`. Evidence from three read-only surveys (gameplay
inventory, scatter/render code-health, character/world-feel gap). This is an advisor
doc: it ranks directions and tradeoffs so we pick from evidence, not vibes. No code
changed to produce it.

## Current state (evidence, not vibes)

**The planet substrate is mature.** Terrain gen, biomes, grass (near-field GPU + chunk +
blanket), foliage scatter (GPU-indirect draw, octahedral impostors, pre-baked atlases),
water, clouds, atmosphere, lakes — all landed and looking Synty-correct. Continued
look/content polish is now diminishing-returns, not frontier.

**The game on top is ~0% built.** Gameplay-pillar survey (14 pillars):

| Pillar | Status | Note |
|---|---|---|
| Inventory / items | ABSENT | no item type anywhere |
| Crafting / building | ABSENT | two dead enum values (`IWorldAction` `BuildingPlace/Remove`) |
| Cooking / potions | ABSENT | — |
| Harvesting / gathering | **PARTIAL (data-only)** | `ScatterInteraction{Collect,Chop}` tag + `EntityHarvest` action exist; **no runtime consumer** |
| Magic / spells / VFX | ABSENT | comments only |
| Portals / fast-travel | PARTIAL (debug cam) | `CameraTeleportStore` = saved debug poses, not a player portal |
| Wildlife / NPC AI | ABSENT | motor *designed* for reuse by "bird/bear/mount"; none exist |
| Combat / health / stats | ABSENT | — |
| Mounts / carts | ABSENT | — |
| Save/load player+world | PARTIAL | terrain edit-stamps persist; **no player/entity/placed-object persistence** |
| Interaction (use/pickup) | ABSENT | player raycaster exists but grounding-only; no Interact action |
| UI / HUD | ABSENT (gameplay) | only debug HUD/console |
| Time-of-day gameplay hooks | PARTIAL (lighting only) | `CelestialManager.TimeOfDay`, "no listeners yet" |
| Quests / dialogue | ABSENT | — |

**Character exists but has no presence.** Motor + gravity/grounding injection is genuinely
built and clean (`CharacterMotor`, `IGravityProvider`/`RadialGravityProvider`,
`IGroundingProvider`/`PlanetRaycastGrounding`). But: the actor is a runtime-created
un-animated capsule that *slides* (`PlanetCharacterController.cs:268`), the 3rd-person
camera **hard-snaps every frame** with no damping/collision boom (`:133-148`), grounding
normal is always radial-up (no slope lean), spawn is console-only + aim-dependent, and
there's no fly/swim state. Collider streaming is **designed-only** (doc exists, zero
`MeshCollider`/`BakeMesh`/`Rigidbody` code).

**Scatter/render code is fundamentally healthy.** Async, shader-globals, boot/SO
discipline, execution-order rules all clean; the scatter GPU-draw is a well-factored fresh
design, not a hack-copy. Debt is narrow and concentrated: `GrassNearFieldController.cs`
(694 lines, god-class + copy-paste of the chunk-grass GPU path) and `ScatterField.cs`
(705 lines; 285 of them are six console commands that should be a diagnostics companion).

## The three tracks & tradeoffs

### Track A — Gameplay frontier (harvesting beachhead)
The strategic move: turn the mature substrate into an actual game loop. **Beachhead =
harvesting**, because it's the most de-risked first pillar — the `Collect`/`Chop` tags,
`EntityHarvest` action, the player's surface raycaster, the scatter instances, and Synty
meshes already exist. "Look at a scatter instance → press Use → remove instance + grant
item" is the smallest change that yields a Valheim loop, and it **drags three ABSENT
pillars into existence in one stroke**: a minimal Interaction action (11), a minimal
Inventory (1), and a minimal HUD/hotbar (12).
- **Pro:** highest strategic leverage; first time it *feels like a game*; unblocks the
  most pillars per unit work.
- **Con:** greenfield — needs discipline to ship one vertical slice, not boil the ocean.
- **Effort:** S→M for the slice (removing a GPU-instanced scatter instance at runtime is
  the one non-trivial bit — needs a per-instance "harvested" mask in the tile cache).

### Track B — Character presence & feel
Make the thing that already exists feel good: camera damping + collision boom, movement
accel/decel + orientation slerp, auto-spawn on planet-ready + input toggle, then an
animated rigged model (Kevin Iglesias clips are in the external library, nothing wired).
- **Pro:** cheap, immediate, visible; it's the *lens* the harvest loop is experienced
  through — a hard-snap camera over a sliding capsule makes any demo feel bad.
- **Con:** doesn't add a system; it's polish on locomotion.
- **Effort:** camera damping S/M, movement smoothing S/M, auto-spawn S, animated model M/L.

### Track C — Narrow code-health pass
Bank the two concentrated debts while the arc is fresh in memory: split
`GrassNearFieldController` (extract shared indirect-draw/readback + climate-map/fallback
helper, kills the only real duplication) and split `ScatterField` core-vs-diagnostics;
plus trivial dead-toggle/stale-comment cleanup and `ScatterFlyBench` `Debug.Log`→`ILogger`.
- **Pro:** cheap insurance, best value now (memory is fresh); scatter side needs nothing.
- **Con:** invisible progress; the code is healthy enough to defer without pain.
- **Effort:** Grass split M(risk: medium — live draw path), ScatterField split M(risk low),
  cleanup S. **Not** urgent; **not** blocking anything.

### Deprioritized (evidence-backed)
- **More look/content polish** — substrate is mature; diminishing returns.
- **Collider streaming (the design doc)** — L effort, and *not needed* for harvesting or
  walk-feel. Only pays off once you throw rocks/ragdolls/physics loot. Defer until a pillar
  needs it.
- **Magic/combat/mounts/quests** — later pillars; each L; build after the first loop proves
  the interaction+inventory+HUD spine.

## Recommended sequence

1. **Character feel quick-wins (Track B, cheap subset first)** — camera SmoothDamp +
   collision boom, movement accel/decel + turn slerp, auto-spawn + input toggle. All S/M.
   This is the foundation everything else is *seen through*; do it first so the harvest
   demo reads well. (Defer the animated model — M/L — until after the loop works.)
2. **Harvesting vertical slice (Track A)** — Interact action → harvest a tagged scatter
   instance → per-instance harvested mask in the tile cache → minimal inventory + hotbar
   HUD. First real game loop; unblocks pillars 1/4/11/12.
3. **Narrow code-health pass (Track C)** — opportunistic/parallel; `GrassNearFieldController`
   + `ScatterField` splits while fresh. Doesn't block gameplay; slot it between the above.

Rationale: B is a cheap prerequisite lens for A; A is the strategic unlock; C is cheap
insurance that doesn't compete for the critical path. Polish and collider-streaming wait.

## Open decision
Pick the first track to commit to. Recommendation: **start with the Track-B feel
quick-wins, then the Track-A harvest slice.** If you'd rather see a system land first, go
straight to Track A (accepting the demo feels rougher until B lands).
