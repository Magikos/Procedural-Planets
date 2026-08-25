---
name: project_m0_foundations
description: M0 of the Magikos architecture — what shipped 2026-08-25 (assembly split, input seam, delta-log v2, B11/B5/B3/B8a), what M0 still owes, and three ledger rows that were wrong.
metadata:
  type: project
---

**2026-08-25, branch `harvest-vertical-slice`.** Ran five parallel lanes against M0 of
`docs/design/2026-08-20-magikos-game-architecture.md`. Commits `c840e36`, `dcd7c73`, `1ebb8b3`,
`a4e073d`, `983a41a`, `0e1f1ae`, `ae0dddb`, `2d37d86`. EditMode **146 → 159 green**.

## What shipped

- **`Magikos.Game` assembly with ZERO references** — not URP, not InputSystem, not even
  `ProceduralPlanets.Core`. M1's "no authoritative path reads a camera/input/local actor" is now a
  compile error, not a promise. Holds `CharacterMotor`, `SurfaceCharacterController`, `CharacterPose`
  (which had to move — it holds `CharacterMath`, called by both), the two provider interfaces, plus new
  `ActorIntent`/`IInputProvider`. Planet-side implementations stayed in `Planet/Character/`.
  `LocalPlayerInput` also stays in Planet: it needs `IInputMapService` from Core, which `Magikos.Game`
  cannot see. Git recorded all five moves as **R100**, i.e. provably pure moves.
- **Delta log schema v1 → v2**: scale appended **last**, so v1 is a strict byte-prefix and every existing
  offset holds. Old files upgrade through the existing `Compact()` atomic-rename path.
- **B11** stump/log now carry the felled tree's yaw and scale (`ScatterPick` off the tile cache matrix).
- **B5** `SurfaceEditController` off its JSON onto the log via the opaque payload; `direction` hoisted into
  the record's `Position` so interest/bucketing can place a stamp without decoding it. 84 B vs ~200 B JSON.
- **B3** lightning seeded per strike from `(world seed, strike index)` via `ISeedProvider`. Per-strike, not
  one stream, so a client that missed strikes still draws this one right.
- **Underwater night level + shaft magnitude** are now `water.set` knobs, defaults reproduce the old
  constants bit-exactly (`x * 1.0f` is the binary32 identity).

## Three ledger rows were WRONG — don't act on them as written

- **B8 ("decide WorldActionManager's fate")** was already struck higher in the same doc as *protected
  infrastructure, do not delete*; the M0 checklist line was never updated. **Decided B8a: undo is a
  local-only affordance that never touches `WorldDeltaLog`** — records carry no prior value, `Remember`
  collapses last-write-wins (and `SpaceOf` shares one space between `ScatterRemoved`/`ScatterState`, so
  chop-then-dig already overwrote the chop), compaction discards depth non-deterministically, and
  `Sequence` is monotonic. M1's real choke point is **`HarvestService.TryHarvest`**, not this.
- **B14 ("one attribute each" on `NoiseFilterData`/`BiomeLookupData`) is INERT.** Burst direct-call needs
  `[BurstCompile]` on **both** class and method; neither `Evaluate` nor `Resolve` has a method-level one.
  From C# they run as managed IL — which is why `NoiseFilterEvaluatorGoldenTests` asserts Burst==managed
  bit-for-bit and passes. The real sites are the **jobs**: `PlanetChunkMeshJob:17`/`:152`,
  `TerrainFace.cs:193`. **Do not pin them blind — `BurstCompiler.Options.EnableFastMath` is True here**,
  so pinning may change terrain and therefore every existing seed. Measure first.
- **B10 said "`Planet.cs.bak` is in the tree".** There were **29**, eight made that day, from `sed -i.bak`.
  `*.bak` is now gitignored (untracked 63 → 13). Three byte-identical ones deleted. **Two must NOT be
  deleted** — `BiomeLookupData.cs.bak` and `WaterVolumePrepass.shader.bak` hold content in no commit.

## Still owed by M0

B2 fixed tick, B6 world identity (`world-{seed}` still collides), B12 `static _host` +
`FindAnyObjectByType`, `HarvestService`'s move (blocked on `ScatterInteraction`/`ScatterLibraryDto`
needing a capability interface), and **background flush** — deliberately not done, because the
`FileStream` is not thread-safe against concurrent appends and it would break sequence ordering.

## Method notes

- **Five agents on disjoint file lanes worked**; the only collision risk was the shared build, and
  `dotnet build ProceduralPlanets.Core.csproj` then `.Planet.csproj` **serially** is a good fast gate.
  Agents cannot run tests — **I can, via MCP `run_tests`** — so the split is: agents write, I verify.
- The `.bak` sweep rediscovered a dead end I had already measured and recorded only in memory. Per
  CLAUDE.md a wontfix belongs **at the code site**; it now is, in `WaterVolumePrepass.shader`.

Related: [[project_world_delta_log]], [[project_water_shore_rendering]], [[project_magikos_architecture]],
[[feedback_quality_over_cheap]], [[reference_unity_mcp]].
