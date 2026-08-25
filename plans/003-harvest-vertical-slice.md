# Plan 003 — Harvesting vertical slice (first gameplay loop)

**Status:** DONE — committed at `5fa6ce0`, play-verified 2026-08-12. Extended by
[plan 005](005-tree-felling-cutset.md) (felling, stumps, persistent logs) and folded into milestone M0 of
[the game architecture](../docs/design/2026-08-20-magikos-game-architecture.md).
**Written against commit `17a8672`.**

> *Historical — the notes below were written before the play test and are kept as a record. The
> "nothing is play verified" caveat and the three risk points were all resolved on 2026-08-12.*
>
> **Build status (2026-08-12, autonomous):** Stages 0–4 implemented and **build clean** (Core→Planet,
> `dotnet build`). EditMode tests **written but NOT run** (no Unity test runner — MCP disconnected);
> nothing is runtime/play verified. New `.cs` files need a Unity import (focus the editor) for `.meta`
> + csproj regen. **6 common trees authored to `Chop`** (Meadow / Autumn Forest / Golden Forest /
> Golden Meadow / Autumn Meadow / Birch Tree Prototypes) so there is something harvestable — the
> `ScatterLibraryDto` snapshots at boot, so a **stop → reimport → re-enter play (fresh world)** is
> required for those to become harvestable. Interact key = **F**. Play test: spawn (`character.spawn`),
> walk to one of those trees, aim at it, press F → it should vanish + a `[Harvest] Chopped 3x Wood`
> log (+ a HUD toast if the F6 overlay is up) → reload/regen → still gone. **Play-verify risk points:**
> (a) ~~GetSettings in RegisterWorldServices~~ **FIXED** — `RegisterWorldServices` runs BEFORE settings
> are registered (lifecycle fact: NOT frozen-first as assumed), so the eager `GetSettings` threw and
> broke boot; the library now resolves lazily at harvest time via a `Func` guarded by `IsRegistered`.
> (b) reach/perp feel (HarvestReach=10, HarvestPerp=1.5 — tune if picking feels off); (c)
> `RemoveInstanceById` correctness.
**Author:** advisor pass (overnight, 2026-08-12). No code changed to produce this plan.
**Roadmap parent:** [docs/design/2026-08-12-next-roadmap.md](../docs/design/2026-08-12-next-roadmap.md) — this is Track A, the #1 gameplay beachhead.

> Executor note: you have zero prior context. Everything you need is inlined below. Follow
> the repo rules in [CLAUDE.md](../CLAUDE.md): **Awaitable only** (no coroutines/`async void`/
> `Task.Run`), **`ILogger`/`LoggerProvider`** (never `Debug.Log*` in new code), settings SOs
> are editor-only (runtime reads DTOs), resolve services at init not per-frame. **Do not
> commit** — Bryan owns commit timing. Build after each stage: `dotnet build
> ProceduralPlanets.Core.csproj` then `ProceduralPlanets.Planet.csproj` **serially** (parallel
> builds collide on a shared intermediate DLL). Stage 3/4 need in-Unity play verification.

## 1. Goal (what "done" looks like)

In play mode, on a generated planet with the character spawned (`character.spawn` in the
console): the player looks at a nearby tree / bush / rock, presses **Interact** (a keyboard
key), and:
1. the instance **vanishes immediately** (that frame),
2. a one-line HUD toast shows `Picked up <item>`,
3. an in-memory item count increments,
4. **on world reload/regen the instance stays gone** (persisted).

That is the whole slice. It lights up four dead pillars at once (Harvesting, Interaction,
Inventory, HUD) with the smallest possible surface.

## 2. Why this is the right first slice (current state — evidence)

The substrate already carries the harvesting scaffolding; **nothing reads it yet**:
- `ScatterInteraction { None=0, Collect=1, Chop=2 }` enum exists
  ([Assets/Scripts/Planet/Scatter/ScatterInteraction.cs:1](../Assets/Scripts/Planet/Scatter/ScatterInteraction.cs#L1)),
  authored per-prototype (`ScatterPrototype.cs:41-42`), plumbed through the DTO
  (`ScatterDtos.cs:33,43`), validated (`ScatterDtos.cs:189`) — but a whole-repo grep finds
  **no runtime reader**. This slice adds the first one.
- `ScatterId` packs face/level/x/y/slot into a `ulong` and its own header comment calls it
  "the persistence key SP5 writes chop/collect overrides against"
  ([ScatterId.cs:1-6](../Assets/Scripts/Planet/Scatter/ScatterId.cs#L1)). Round-trip is already
  golden-tested.
- `IWorldAction.EntityHarvest` + `WorldActionManager` exist but are **in-memory only, no disk,
  no concrete impl** (`Core/Services/WorldActionManager.cs`). Optional for this slice — skip it.
- The player + camera ray already exist (`PlanetCharacterController.cs`).

## 3. Architecture decisions (made — do not re-litigate)

| Concern | Decision | Evidence |
|---|---|---|
| Make an instance disappear | Filter harvested ids at **`ScatterTileCache.Commit`** — the sole choke point both placement paths (serial + Burst) funnel through, each `ScatterInstance` still carrying its `Id`. Buckets are the *only* source the GPU indirect draw reads, so filtering there auto-removes from the draw with **no shader/GPU change**. | `ScatterTileCache.cs:422` (Commit), loop `:434-438`; draw source `ScatterRenderer.Render:100-116` → `ScatterGpuDraw.DrawProto` |
| Persist removals | Seed-keyed JSON set of harvested `ScatterId`s, **mirroring `SurfaceEditController`**. Because the whole scatter cache is rebuilt every world load/regen, the Commit-time filter makes removal persist with **no replay pass**. | `Surface/SurfaceEditController.cs` (SaveVersion `:9`, FilePath `:601`, Save `:493`, EnsureLoaded `:447`); lifecycle `Planet.cs:353-369` |
| Pick the looked-at instance | New nearest-instance-along-camera-ray query over live bucket positions (no colliders exist). Requires buckets to **retain the instance Id** (currently dropped). | raycaster hits terrain only `Planet.cs:515-550`; buckets drop id `ScatterTileCache.cs:437` |
| Item grant + inventory | First reader of `ScatterInteraction`. Minimal `InventoryService` = `Dictionary<string,int>` in app scope. Item id = prototype `DisplayName` for the slice. | — |
| HUD | Copy `DebugOverlayHud`'s existing timed-toast pattern (`NotifyPrecipitationToggle:15-20`, drawn while `Time.unscaledTime <= until`). Cheapest one-line toast. | `Core/Services/DebugOverlayHud.cs:15-20,54-60` |

## 3b. POC #1 scope + iteration seams (toward the Valheim chop loop)

**POC #1 = the harvest SPINE on the existing capsule** (console `character.spawn` is fine): walk →
look at a tree → press Interact → it vanishes → +1 item → persists across reload. Ugly but
functional. Deferred to later POCs: animated model + camera feel (plan 004), inventory/hotbar UI,
Collect-vs-Chop distinction, icons, drops, regrow, a tools inventory, auto-spawn/toggle.

**Design constraint (Bryan, 2026-08-12): the code must iterate cheaply toward the full loop** —
animations, sounds, per-hit damage (axe quality → N hits before a tree falls), an animated topple,
and the tree breaking into logs (Valheim-style tree → logs → wood). So the POC builds the right
SEAMS now and implements each trivially; the future features are ADDITIVE at those seams, not
rewrites. **Build the seams, NOT the features** — mark every trivial stub with a `ponytail:` comment
naming the upgrade path.

Seams to establish (all thin — a method, two small structs, two events; not a framework):
1. **One verb choke point:** `HarvestService.TryHarvest(ulong id, int protoIndex, in ToolTier tool)
   → HarvestResult`. Every harvest routes through it. POC body: apply `tool.Damage` to the node; if
   depleted → persist harvested (Stage 2) + raise `ScatterHarvestedEvent` + remove the instance this
   frame (Stage 4) + grant yield to inventory. `// ponytail: node HP defaulted so one hit fells it;
   add a per-ScatterId hit accumulator here for multi-hit chopping.`
2. **Node takes damage, not boolean removal:** the node has an HP / `remainingHits` concept even
   though POC defaults it so `tool.Damage` one-shots it. Multi-hit chopping = add an HP source + a
   transient/persistent per-`ScatterId` hit accumulator BEHIND `TryHarvest` — callers unchanged.
3. **Tool seam:** `readonly struct ToolTier { string Name; int Damage; }`. POC passes one default
   (`BasicAxe`, `Damage` ≥ any node HP so it one-shots). Axe quality = more tiers + a real
   equipped-tool lookup later.
4. **Yield seam:** `readonly struct HarvestYield { string ItemId; int Count; }`, derived from the
   prototype + its `ScatterInteraction` (Chop → Wood×N, Collect → the plant×1). POC: ONE hardcoded
   mapping. Data-drive it later (a per-prototype yield table). Yield → inventory now; the seam also
   permits "spawn a harvestable log ENTITY" later by swapping what consumes the yield.
   `// ponytail: yield → inventory item today; swap the consumer to spawn-drop-entity for logs.`
5. **Feedback events (the anim/sound/vfx hook):** raise `HarvestHitEvent` (damaged, not felled) and
   `ScatterHarvestedEvent { ulong Id; int ProtoIndex; Vector3 WorldPos; HarvestYield Yield; }` on
   `EventBus` (cross-subsystem = EventBus, per the architecture rule). **The POC's own HUD line
   subscribes to `ScatterHarvestedEvent`** — so the event has a real consumer now (not dead code),
   and it's the exact template future chop-SFX / hit-particles / fall-animation copy, with zero
   change to harvest logic.
6. **Removal is one call → a fall animation can delay it:** instance removal is a single method
   (`ScatterTileCache.RemoveInstance`, Stage 4); POC removes instantly, but a future topple plays on
   `ScatterHarvestedEvent` and removal can move behind it. Keep removal in one place.

Net: multi-hit + axe tiers + data-driven yields + log entities + anim/sound/topple are all additive
at seams 1–6. POC builds NONE of them — one default tool, one-shot fell, one yield-to-inventory, no
FX beyond the single HUD line.

## 4. Stage 0 — PREREQUISITE: fix `ScatterGatherBurst` slot-bit mismatch

**Why first:** the default on-planet placement path is Burst (`ScatterTileCache._burstReady`
is true whenever the ground sampler is `IBurstElevationSource`, `ScatterTileCache.cs:149-159`).
`ScatterGatherBurst.PackUnchecked` packs with **`SlotBits = 6`**
([ScatterGatherBurst.cs:179](../Assets/Scripts/Planet/Scatter/ScatterGatherBurst.cs#L179)),
while canonical `ScatterId` uses **`SlotBits = 7`** (`ScatterId.cs:9`). Slots **64–127**
(the lake-shore props — cattails/reeds/rocks/wildflowers live at slots 64–67 per the lake
biome work) overflow 6 bits and pack an **aliased/wrong id**. Harvest is the first feature
that depends on a stable per-instance id, so this must be corrected or harvesting lake props
saves the wrong key and either fails to remove or removes the wrong instance.

**Do:** align `ScatterGatherBurst`'s slot packing (and any matching mask/shift constants in
that file) to `ScatterId`'s canonical `SlotBits=7 / SlotShift=56 / player bit=63` layout.
Prefer having the Burst packer call the same shared constants `ScatterId` uses rather than
re-declaring them, so they can't drift again.

**Verify:**
- `dotnet build` Core→Planet clean.
- In play: `scatter.verify` still returns `PASS` / `PASS_WITH_KNOWN_CORNER_GAP` (it proves id
  uniqueness + round-trip; `ScatterField.cs:442`).
- Add/confirm an EditMode test: an id packed for `slot=65` round-trips through
  `ScatterId.Unpack` back to `slot=65` (extend the existing ScatterId golden test).

**Escape hatch:** if aligning the bits changes ids that something *else* already persisted
against (search for any on-disk use of the 6-bit form — there should be none), STOP and report
before proceeding.

## 5. Stage 1 — buckets retain the instance Id

`ScatterDrawBuckets` keeps parallel `List<Matrix4x4>` + `List<Vector3>` per prototype, keyed
by owner tile, swap-removed on eviction (`ScatterDrawBuckets.cs:12-113`). It **drops
`inst.Id`** at commit.

**Do:** add a parallel `List<ulong> _ids` per prototype, populated in the same
`Add`/commit path, swap-removed in **lockstep** with matrices/positions on eviction. Expose
`IReadOnlyList<ulong> Ids(int proto)` mirroring `Positions(int proto)` (`ScatterTileCache.cs:100`).
Populate it from `inst.Id` where `Commit` currently stores the matrix (`ScatterTileCache.cs:437`).

**Verify:** build clean; `scatter.count` / `scatter.density` unchanged (ids unused so far); no
visual change.

## 6. Stage 2 — harvested set + persistence + Commit filter

**Do:**
1. New `ScatterHarvestStore` (plain class, `Assets/Scripts/Planet/Scatter/`), a direct
   structural copy of `SurfaceEditController`'s persistence:
   - `const int SaveVersion = 1;`
   - in-memory `HashSet<ulong> _harvested;`
   - `void Configure(int seed)` → sets `_seed`, lazy-loads.
   - `bool Contains(ulong id)`, `void Add(ulong id)` (adds + `Save()`).
   - JSON at `Application.persistentDataPath/ProceduralPlanets/scatter-harvest-{seed}.json`
     via a `[Serializable]` wrapper `{ int version; int planetSeed; string[] ids; }`.
     **`JsonUtility` cannot serialize `ulong`** — store each id as `id.ToString()` and parse
     with `ulong.TryParse` on load. Reject on `version != SaveVersion || planetSeed != seed`
     (same guard as `SurfaceEditController.EnsureLoaded:447`).
   - Use `LoggerProvider`, not `Debug.Log`.
2. `ScatterTileCache` holds a `ScatterHarvestStore` reference (constructor-injected or set in
   `Configure`). In the `Commit` loop (`:434-438`) add: `if (_harvest != null &&
   _harvest.Contains(inst.Id)) continue;` **before** the bucket store.
3. Wire `harvestStore.Configure(seed)` in the `Planet.cs` generation-complete block, next to
   `_scatter.Configure(...)` (`Planet.cs:369`) — after `_surfaceEdits.ReplayStamps` (`:354`).
   Construct the store where `_surfaceEdits` is constructed (`Planet.cs:100`).

**Verify:** hand-add a known live id to the json file, regen the world (or reload) → that
instance is absent (`scatter.count` drops by one for its prototype). Build clean.

## 7. Stage 3 — ray → nearest-instance picker

No instance colliders exist; picking is a math query over live bucket data.

**Do:** new `ScatterPicker` (plain class). Method
`bool TryPick(Ray camRay, float reachMeters, out ulong id, out int protoIndex, out Vector3 pos)`:
- iterate prototypes whose DTO `Interaction != ScatterInteraction.None`
  (`ScatterLibraryDto.Prototypes[i].Interaction`);
- for each, scan `cache.Positions(i)` + `cache.Ids(i)` (Stage 1);
- keep the candidate that is within `reachMeters` of the ray origin **and** whose offset makes
  the smallest angle to `camRay.direction` under a threshold (e.g. within a ~1.5 m
  perpendicular distance to the ray, or an angular cone); return the nearest such by distance.
- Return false if none qualify.

Keep it O(visible instances) — fine for a reach of a few metres; the buckets near the camera
are small. If it ever needs speeding up, note it but don't pre-optimize.

**Verify (play):** aim at a tree within reach → log prints the picked id + prototype name; aim
at sky/empty → no pick. (Temporary log is fine; remove before Stage 4 completion.)

## 8. Stage 4 — Interact input + harvest + grant + HUD (wire the loop)

**Do:**
1. **Input:** in `IInputMapService` (`Core/Interfaces/IInputMapService.cs:16-39`) declare
   `InputAction Interact { get; }`; in `InputMapService` add the property + one ctor line
   `Interact = AddButton("Interact", "<Keyboard>/f");` (`:106-131` block — **`e` is taken**
   by VerticalMove `:78`, use `f`).
2. **Inventory:** new `InventoryService` (plain class), `Dictionary<string,int> _items`,
   `void Add(string item, int n=1)`, `int Count(string item)`. Register app-scope via
   `ServiceLocator.Register` (persists across worlds). No disk persistence this slice.
   `HarvestService` (§3b seam 1) is what calls `Add` with the resolved `HarvestYield`.
3. **HUD:** add a `NotifyPickup(string item, int count)` to `DebugOverlayHud` copying
   `NotifyPrecipitationToggle` (`:15-20`) — flash message + `Time.unscaledTime + 2f` until-time,
   drawn in `Draw` like `:54-60`. **Wire it by subscribing to `ScatterHarvestedEvent`** (§3b seam
   5), not by a direct call — so the event has its first real consumer and future FX subscribe the
   same way. (Visible only while the debug overlay is up; acceptable for the POC. Upgrade path:
   `SDFTextRenderer` + `LoadingProgressBarOverlay` for an always-on line — out of scope.)
4. **Immediate removal:** add `void RemoveInstance(int protoIndex, ulong id)` to
   `ScatterTileCache` that swap-removes that id from the bucket (Stage 1 ids make this O(n) find
   + O(1) remove) and marks the proto draw dirty (same dirty flag `ScatterGpuDraw.DrawProto`
   consumes, `ScatterTileCache` already tracks draw-dirty per proto — reuse it). This makes the
   instance vanish **this frame** without a full re-gather.
5. **Glue in `PlanetCharacterController.Update`:** on `_input.Interact.WasPressedThisFrame()`
   (next to the `_input.Jump.WasPressedThisFrame()` read, `:126`) →
   `picker.TryPick(new Ray(camPos, camForward), reach≈4f, out id, out protoIndex, out _)` → if hit:
   `harvest.TryHarvest(id, protoIndex, defaultTool)`. **`HarvestService` owns** persist (Stage 2)
   + remove-this-frame (`ScatterTileCache.RemoveInstance`) + resolve+grant yield + raise the events
   (§3b seams 1/4/5/6); the controller only picks + calls the verb, staying a thin player host.
   Resolve `picker` + `harvest` once at init (not per frame) via `ServiceLocator.TryGet` /
   constructor. `defaultTool` is a hardcoded `ToolTier` for the POC.

**Verify (play):** spawn character, walk to a tree, press F → it vanishes instantly, toast
shows, `scatter.count` for that prototype drops by one; reload/regen → still gone; press F on
empty air → nothing. Build clean Core→Planet.

## 9. Files

**New**
- `Assets/Scripts/Planet/Scatter/ScatterHarvestStore.cs` — persistent harvested-id set.
- `Assets/Scripts/Planet/Scatter/ScatterPicker.cs` — ray→nearest-instance query.
- `Assets/Scripts/Planet/Scatter/HarvestService.cs` — the one verb choke point (§3b seam 1):
  `TryHarvest(id, proto, tool)` → damage node → persist + remove + yield + events.
- `Assets/Scripts/.../HarvestTypes.cs` — `ToolTier`, `HarvestYield`, and the `HarvestHitEvent` /
  `ScatterHarvestedEvent` EventBus events (§3b seams 3/4/5). Small; one file.
- `Assets/Scripts/.../InventoryService.cs` (place beside other Core services) — item counts.
- EditMode tests (see §11).

**Modify**
- `Assets/Scripts/Planet/Scatter/ScatterGatherBurst.cs` — Stage 0 slot-bit fix.
- `Assets/Scripts/Planet/Scatter/ScatterDrawBuckets.cs` — retain `_ids` (Stage 1).
- `Assets/Scripts/Planet/Scatter/ScatterTileCache.cs` — `Ids(proto)`, harvest ref + Commit
  filter, `RemoveInstance` (Stages 1/2/4).
- `Assets/Scripts/Planet/Planet.cs` — construct + `Configure(seed)` the harvest store (Stage 2).
- `Assets/Scripts/Core/Interfaces/IInputMapService.cs` + `Core/Services/InputMapService.cs` —
  `Interact` action (Stage 4).
- `Assets/Scripts/Core/Services/DebugOverlayHud.cs` — `NotifyPickup` toast (Stage 4).
- `Assets/Scripts/Planet/Character/PlanetCharacterController.cs` — the glue (Stage 4).

## 10. Scope boundaries

**In:** one Interact key; look-at pick within reach; instance vanishes + persists; one item
count; a one-line HUD toast fired on `ScatterHarvestedEvent`; and the §3b seams implemented
trivially (one-shot `TryHarvest`, one default `ToolTier`, one hardcoded `HarvestYield`, both events).

**Explicitly OUT — do NOT build, but leave the §3b seam each hangs off:** multi-hit chopping /
per-node HP accumulator, axe-quality tiers / equipped-tool lookup, data-driven yield tables, log
entities (tree → logs → wood), fall/topple animation, chop SFX + hit particles, crafting/building,
inventory UI/hotbar, `IWorldAction`/undo, Collect-vs-Chop yield difference, respawn/regrow
(`regrowSeconds` is a future hook), inventory save/load, drops/physics. If a step tempts you toward
any of these, STOP — they are additive later at seams 1–6, not part of the POC.

## 11. Test plan (EditMode, reuse the existing ScatterId golden-test pattern)

- **ScatterId slot-64+ round-trip** (Stage 0 guard): pack `slot=65` (and 127), unpack, assert
  equality. Prevents the SlotBits regression from returning.
- **ScatterHarvestStore round-trip**: `Add` three ids → `Save` → new instance `Configure(seed)`
  `Load` → `Contains` all three; wrong-seed load returns empty; a `ulong` above `int.MaxValue`
  survives the string serialize/parse.
- **ScatterPicker geometry**: a synthetic set of positions — a ray straight at one returns it; a
  ray perpendicular returns none; two along the ray return the nearer. Pure math, no Unity scene.
- **HarvestService one-shot**: `TryHarvest(id, proto, BasicAxe)` with a default-HP node → result is
  Felled, `HarvestYield.Count > 0`, the id is marked in a fake store, and `ScatterHarvestedEvent`
  fires exactly once (fake store + inventory). Proves the seam wiring so multi-hit can later slot in
  behind `TryHarvest` without breaking the one-shot path.

No new test framework — UTF is already in the manifest; follow the existing EditMode tests
(`ScatterId`/DTO-validation/placement-math) for structure.

## 12. Escape hatches

- If Stage 0's bit fix ripples into anything that reads the 6-bit id form, STOP and report.
- If the Commit filter doesn't remove the instance (it should — buckets are the sole draw
  source), do **not** start editing shaders/`ScatterGpuDraw`; re-check that `_burstReady`
  path's `ScatterInstance.Id` matches the saved id (Stage 0). Report if still stuck.
- If picking feels bad (grabs the wrong node), tune reach/threshold via a temporary console
  knob and leave the number for Bryan to lock — do not hand-bake a guessed constant.

## 13. Maintenance note

The harvested set is a **derived cache keyed by ScatterId**, exactly like path-wear/scorch are
derived from `SurfaceEditStamp`s (see `.agent-memory/MEMORY.md` "Surface edits"). Keep it that
way: `ScatterId` is the single source of truth for instance identity, so any future change to
the bit layout must bump both packers *and* migrate saved harvest files. The Commit-time filter
is the one place removal is enforced; if a second draw source is ever added, it must consult the
same store. Regrow, Collect-vs-Chop yields, and tool gating all hang off the same
`ScatterInteraction` reader this slice introduces.
