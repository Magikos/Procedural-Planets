# Save system design

2026-08-17 — design only, nothing built. Captured from a design conversation so
the reasoning is not lost. Trigger to build: persistent dropped items.

## Why this exists

The world is derived from a seed, so scatter instances cost no storage. Only
*exceptions* are written: a chopped tree, a dug stump, a fallen log. That model
holds until the player can drop things.

A dropped item is not an exception to the seed. It has no cell address, because
its position was chosen rather than derived. It is a genuinely new object, and
it must still be lying where the player left it hours later. That is the case
the current store cannot serve.

## What the current store does, and where it stops

`Assets/Scripts/Planet/Scatter/ScatterHarvestStore.cs` holds two dictionaries
(`HarvestNode` stumps keyed by `ScatterId`, `LogRecord` fallen logs keyed by a
running id) and persists them as JSON under
`Application.persistentDataPath/ProceduralPlanets/scatter-harvest-{seed}.json`.

The blocking property is not size. It is that `Save()` is called from every
mutation — `RecordStump`, `RecordDug`, `RecordLog`, `RemoveLog` — and each call
re-serialises the entire store and does a synchronous `File.WriteAllText` on the
main thread.

Cost per record, as JSON:

| records | file size | cost of one new record |
| --- | --- | --- |
| 1,000 | ~150 KB | trivial |
| 10,000 | ~1.5 MB | full 1.5 MB rewrite, per chop |
| 100,000 | ~15 MB | unusable |

Limits in the order they bite:

1. **Save-on-every-write, main thread.** Felt around 2,000–5,000 records, as a
   hitch on every action rather than as a size problem.
2. **Load-time parse.** 100k records is ~15 MB of JSON through `JsonUtility`.
3. **Memory.** ~60 bytes per dictionary entry. A million entries is ~60 MB. Not
   binding.
4. **Draw**, for explicit objects only. Individual GameObjects get heavy around
   10k regardless of how they are stored — they have no instanced-draw path.

So the practical ceiling today is a few thousand records, and it is a property
of the writer, not a hard limit.

## Three categories of world state

Keeping these separate is what keeps the save small.

| category | examples | storage |
| --- | --- | --- |
| **derived** | every standing tree, rock, bush | nothing — regenerated from the seed |
| **exception** | chopped tree, dug stump | one record keyed by `ScatterId` |
| **explicit** | dropped items, buildings, statues, planted saplings | one record with its own id and transform |
| **transient** | debris, pebbles, VFX | never saved |

Explicit objects need their own store. They cannot borrow `ScatterId`, because
face/level/x/y encode a *derived* cell address that a placed object does not
have. (Bit 63, the old reserved "player-placed" flag, was reclaimed for slot
width for exactly this reason — see the comment in `ScatterId.cs`.) If a unified
handle is ever wanted, reserve a slot *value* meaning "look this up elsewhere"
rather than taking a bit back.

The two stores differ in content but have identical needs: append cheaply,
survive a crash, load fast. One writer serves both.

## Target design: append-only log with compaction

```
current.sav        base snapshot
current.log        appends since that base      live game = base + log
auto-0..N.sav      autosave ring buffer
manual-<name>.sav  manual saves
```

- Every change appends one fixed-size record to `current.log`. Appends are tiny,
  so the common path can stay synchronous without hitching. No debounce timer
  needed for normal play.
- On load, replay the log over the base into memory.
- When the log grows past a threshold, compact: rewrite from in-memory state,
  atomically, and truncate the log.

Fixed-size binary record: id 8 + position 12 + rotation 16 + type 4 + state 1 =
41 bytes, against roughly 160 as JSON. 100k records becomes a ~4 MB file that
loads in milliseconds. Keep the existing `SaveVersion` header so the reader can
reject or migrate older files.

Serialise and write on `Awaitable.BackgroundThreadAsync`, per the project async
rule. No `Task.Run`, no coroutines.

### Why a log rather than debounce-plus-atomic-write

A debounced full write is simpler and takes the ceiling to tens of thousands,
but it always has a window of unsaved work — a few seconds. The log has none.
For dropped inventory that difference is the whole point: a player drops a
stack, the power goes, and the stack is still there. The log is perhaps forty
lines more code.

A separate base file plus delta files was considered and folded into this: one
append log with size-triggered compaction gives the same durability with one
file type instead of two and no reconciliation between them. Compaction *is* the
atomic write, triggered by size rather than by clock.

## Correctness details that decide whether it survives power loss

These are not optional. Each one is a way to lose the whole save rather than one
action.

- **Atomic replace.** Write to a temp file, flush, then rename over the target.
  Rename is atomic on Windows and POSIX. The rename is the commit point. Without
  it, a crash mid-write leaves a truncated save.
- **Compaction ordering.** New file written and renamed *before* the old log is
  truncated. Never a window where neither is complete.
- **Torn final record.** A crash mid-append leaves a partial record. Give each
  record a length prefix or checksum; on load, stop at the first record that
  fails to validate and discard the tail.
- **Flush on exit.** Explicit flush on quit, on world unload, and on focus loss.
  Otherwise "dropped my stuff and alt-F4'd" loses it.
- **Durability versus cost.** An append reaching the OS is not an append reaching
  the disk. True power-loss safety needs an explicit flush, costing
  milliseconds. Reasonable middle: flush on meaningful events (item dropped,
  structure placed) and let cosmetic changes ride.

## Save slots

Compaction already produces a complete self-contained file, so a save slot *is*
a compacted file.

- **Autosave:** compact in-memory state to a temp file, rename to `auto-K.sav`
  with K rotating over N slots. Refresh `current.sav` and truncate `current.log`
  at the same time.
- **Manual save:** identical, named file, no rotation.
- **Load slot:** copy the slot to `current.sav`, then **delete `current.log`**.

The trap: a log is only meaningful against the base it was appended to. A stale
`current.log` beside a newly loaded base replays the previous session's actions
into the wrong world. Deleting the log is part of the load, not a cleanup after
it.

Rotation ordering: write and rename the new autosave fully before removing the
oldest, or a crash mid-rotation costs two saves instead of zero.

Saves are seed + exceptions + explicit objects + player state, so a full world is
kilobytes. Copying slots is free. The gigabytes that would have been the world
itself are never written — that is the payoff of deriving from the seed.

## Out of scope

- **Debris and pebbles.** A separate transient system. Limited interaction,
  never saved.
- **Rock sculpting.** Not supported. Rock to statue is a hard convert, not a
  mesh edit.
- **Inventory stones on the ground.** These are explicit objects and belong in
  the explicit store, not the scatter exception store.

## Build trigger

Not before dropped items are real. The current per-write JSON is adequate for
the volume harvesting produces today, and building it now would be speculative.
The moment items can be dropped, this becomes the first thing to fix — dropped
items accumulate faster than anything else, because every one is player-caused
and none expire on their own.
