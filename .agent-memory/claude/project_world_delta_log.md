---
name: project_world_delta_log
description: The M0 save foundation — WorldDeltaLog, EntityId, and the harvest migration. Record layout, the id-space collision that nearly shipped, and what M0 still owes.
metadata:
  type: project
---

**2026-08-21, branch `harvest-vertical-slice`.** Built the M0 non-controller half of the Magikos game
architecture (`docs/design/2026-08-20-magikos-game-architecture.md` §15). Commits `a6b09c0`, `2067ce2`,
`68c03cf`. 146 EditMode tests green (was 117 at the start).

## What shipped

`Assets/Scripts/Core/World/WorldDeltaLog.cs` — append-only log, size-triggered compaction. Header 8 bytes
(magic `0x504C4457`, version). Record = **48-byte fixed part + optional payload + 4-byte FNV-1a checksum**,
so a transform-only record is 52 bytes.

`Assets/Scripts/Core/World/EntityId.cs` — high 16 bits owner tag, low 48 bits counter, counters start at 1 so
`0` is `None`. `EntityIdAllocator.Observe` must be shown every saved id on load or the next mint collides.

`ScatterHarvestStore` is now a **view over the log**, not a store. Public API unchanged, so all five consumers
(`HarvestInteractor`, `LogRenderer`, `StumpRenderer`, `TreeFallSystem`, `ScatterTileCache`) were untouched.

## The design call that changed mid-build

I first built a **fixed 41-byte record** because `docs/design/2026-08-17-save-system.md` costs one that way.
That is wrong for the general case: **`SurfaceEditStamp` has nine fields and does not fit**, and container
contents never will. The architecture doc's own §6.4 struct has a `PayloadLength` for exactly this reason.
Landed on a hybrid — common transform fields stay hoisted out (so an ordinary chop allocates nothing), and
kinds that need more carry an opaque payload they own the encoding of.

## GOTCHA — the id-space collision that nearly shipped

`_byKey` was keyed on the bare `ulong`. **A ScatterId and an EntityId can be the same number** — a ScatterId
packs a cell address that can be small, and EntityId counters start at 1 — so a dropped log would have
overwritten a chopped tree and the tree would come back. Now keyed by `(space, key)`; `TryGet` takes the
`DeltaKind` to pick the space. Kinds describing one thing **share** a space on purpose, so a tree's
`ScatterRemoved`/`ScatterState` collapse to one record, as does an entity's Spawned/Moved/Removed/State.

## Verified on real data, not just tests

Bryan's live world (seed `1691104419`) had a 9074-byte `scatter-harvest-*.json`. On first open:
`[ScatterHarvest] Imported 54 record(s)`, cost **29 ms inside finalize**. Result on disk: **2816 bytes =
8 + 54 × 52 exactly**, all 54 checksums valid, 27 `ScatterState` + 27 `EntitySpawned` — matching 27 chopped
trees and 27 fallen logs. **3.2× smaller than the JSON.** Old file renamed to `.migrated`, not deleted.

**The live log holds its file with `FileShare.Read`, so a second writer gets a sharing violation.** That is
the feature, not a bug — two appenders would interleave and corrupt the save, and this is the "one host owns
the world" guarantee failing loudly. To inspect a live log, read the bytes with `FileShare.ReadWrite`; do not
`Open` a second instance.

## B1 determinism

`TreeFallSystem` picked the topple direction with `Random.onUnitSphere`, reading global RNG that
`TreeStructureGenerator.Random.InitState(seed)` had already stirred. The fall decides where the resting log is
**saved**, so the world diverged on disk, not just on screen. Now `ISeedProvider.GetSeedForEntity(ulong)` —
a **new overload, added because the architecture doc's B17 recommends adopting the orphaned `SeedProvider`
cluster rather than hand-rolling**. Takes `ulong` not `ScatterId` because Core cannot reference Planet.

Two traps found doing it: the tangent basis must be built in **planet-local** space (crossing against world up
folds the planet's rotation in and two processes disagree again), and `ISeedProvider` must be resolved **on
first use, not in the constructor** — `Planet.Awake` runs before `SceneBootstrap.EarlyInitialize` registers it.

## What M0 still owes

- **B6 world identity is NOT closed.** Saves are still keyed `world-{seed}`, so two worlds of the same seed
  collide. `Open(directory, worldKey)` is already the right seam — it takes an opaque key, not a seed — but
  minting a real identity needs a world-creation flow that does not exist.
- **B5 half done.** `SurfaceEditController` (`surface-edits-{seed}.json`, v1) is still on its own JSON and
  still `File.WriteAllText` per mutation. Its stamp needs the payload path.
- **B11 open.** `ScatterPicker.TryPick` returns only `id, protoIndex, pos`, so a stump cannot know the felled
  instance's yaw/scale. Needs the picker to surface the matrix **and** a scale field in the record.
- **B8a undecided** — does undo/redo survive an append-only log? Bryan's call.
- Not mine by request: B2 fixed tick, `IInputProvider`, moving `CharacterMotor`/`SurfaceCharacterController`.

**~20 untracked `*.bak` files sit in `Assets/`** (B10 names `Planet.cs.bak`). Left alone — untracked means
unrecoverable if deleted. Two got swept into a commit by `git add -A` and were untracked again, not deleted.

Related: [[project_gameplay_roadmap]], [[project_tree_generator]], [[reference_unity_mcp]],
[[feedback_quality_over_cheap]].
