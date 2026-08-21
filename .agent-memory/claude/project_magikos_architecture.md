---
name: project-magikos-architecture
description: The game layer's architecture doc of record (2026-08-20) and the four scoping calls Bryan made that shape it.
metadata:
  type: project
---

The game on top of the planet is **Magikos** — a systemic-magic wizard co-op game. Its architecture
and milestone plan is `docs/design/2026-08-20-magikos-game-architecture.md`, written 2026-08-20 by
merging `docs/Magikos_AI_Project_Context.docx` (Bryan's ChatGPT design consolidation) with the actual
repository and the existing docs of record.

**Why:** the docx was written as if the project were greenfield. It is not. Roughly a third of the
merge work was reconciling docx claims against repo reality — see §2 of the doc, which is the part to
read first.

**How to apply:** treat §2 (reconciliation), §13 (ADRs) and §14 (blocking debt) as the doc's load-bearing
sections. Do not re-derive them.

## Bryan's four scoping calls, 2026-08-20

1. **Multiplayer is a hard constraint that shapes architecture NOW**, not a later addition. 8 players
   max, 2–4 optimal. Every new gameplay system gets authority/replication/stable-ID rules from day one.
2. **PC, plus a console port as a non-blocker.** Mobile and WebGL are dropped. Practical cost: gamepad
   bindings and UI scale discipline from the first UI milestone.
3. **Deliverable = architecture AND milestones**, not one or the other.
4. **One planet is the whole game world.** Portals are intra-planet fast travel.

## The keystone idea

The docx wants "nearly any world object can be imbued" and physicalized items. The repo draws ~1M
props with no C# object behind any of them. The resolution — and the thing the whole design hangs
from — is that the repo already prototyped the answer twice:

> The world is a seed plus an exception log. An object is **derived** until something happens to it,
> then it becomes a **record**, and it becomes a **GameObject only while it must be interactive**,
> demoting back to a record afterwards.

`ScatterHarvestStore` is the exception log. `TreeFallSystem` is the promotion. Generalising both gives
imbuing at scale, affordable 8-player replication (send the seed and the delta log, never the world),
and one record format that serves save file, late-joiner sync and live replication simultaneously.

## All seven open decisions RULED 2026-08-21 (doc §13)

- **Harvest-only rule does NOT cover Unity-registry packages.** NGO in `Packages/` is fine, same as
  Burst/Collections/URP. The rule targets Asset Store vendor C# lifted into `Assets/`.
- **Durability: OUT.** `ItemInstance` gets no condition field. `ToolTier` carries tool progression.
- **MDI: UNDECIDED, revisit at M3.** Bryan: "may or may not make it to the game." M3 builds fixed panels.
  Nothing before M3 depends on it, because the view-model boundary is identical either way.
- **Bindings move to a `.inputactions` asset.** Keep `IInputMapService`'s 42 properties identical so the
  migration touches one file; do NOT start from the untouched Unity default template already on disk.
- **Split `Magikos.Game` assembly in M0.**
- **Tests allowed where they speed development or prevent regression.** No new framework — the existing
  EditMode suite. Update CLAUDE.md's "no test framework" rule to record this.
- **BIGGEST ONE — dedicated server is a future target.** Bryan: "Host saves, clients can save some data
  to help data transfer per world and I want to build for a dedicated server in the future also."

## What the dedicated-server ruling changed

**Authority belongs to the SERVER ROLE, never to "the player who happens to be hosting."** Listen server
= server role + local player in one process; dedicated server = server role alone. So: no authoritative
code inside `PlanetCharacterController` or any player-host MonoBehaviour, and **no authoritative code may
touch a camera, input device, `Shader.SetGlobal*` or `AsyncGPUReadback`.**

## GPU vs headless — MEASURED 2026-08-21, doc §7.1.1/§7.1.2, ADR-14

Bryan asked: keep GPU and convert later, or run a CPU GPU-emulator on the standalone server? **Answer: keep
GPU, port nothing, and there is barely a conversion project waiting.** Six of seven gameplay facts are
ALREADY CPU-authoritative (scatter existence, water, biome, chunk meshes, surface edits, wind/temperature) —
because the CPU needed those answers for grounding/placement/picking years before multiplayer. Of 8
`AsyncGPUReadback` sites, 7 are grass diagnostics.

Rule: **the GPU decides what the world looks like; the CPU decides what is true about it.**

**Why NOT the emulator** (his idea's real merit is one source of truth for the math — dual impls drift and
this repo has been bitten twice, so the instinct is sound): **GPU floating point is not bit-identical across
vendors.** ADR-2 needs every client to derive the same world from the seed, so an NVIDIA and an AMD client
would derive marginally different worlds — differing `ScatterId`, chop refers to a tree the other lacks.
Silent, rare, unreproducible locally. An emulator adds a THIRD FP implementation. Same reason lockstep RTS
never simulates on GPU. **This bites at M1 between two friends' desktops, not at server time.** Software
Vulkan (SwiftShader/llvmpipe) stays a back-pocket option for visual-only-but-headless work only.

**ONE real violation, after measuring:**

**Weather is GPU-only, no CPU path.** `WeatherEvolution.compute` owns init AND evolution; CPU arrays are
filled only by readback. Fallback is a CONSTANT, and before readback lands `SampleWeather` returns
storm=0/rain=0 with no error signal (never consults its own face mask). Only VFX reads it today. Port the
kernel to CPU when a mechanic first reads weather, not before.

## MEASURED 2026-08-21 in the live editor — I was WRONG about grounding

I claimed ground height was LOD-dependent (non-deterministic across clients, unanswerable headless),
inferring it from `ReleaseCpuDataAfterBake` nulling `CpuVertexRadii`. **Measured false. Retracted.**

- **Agreement, 300 directions, r=5108 m:** analytic vs chunk-mesh **mean 7.9 mm**, max 243 mm; raycast vs
  chunked mean 24 mm; analytic vs raycast mean 31 mm. **All three within 28 cm.** `plans/001`'s 3-24 UNIT
  disagreement is gone — D12 (`8fdd1d2`) was its cause. Reproduces D12's own 8 mm figure exactly.
- **LOD drift, 120 directions, camera 8126 m → 120 m altitude: ZERO. 120/120 bit-identical.**
- **Why:** the quadtree is FULLY built to `_maxChunkDepth = 4` on all 6 faces — `_allChunks` = **2046** =
  6×(1+4+16+64+256) — and **1536 keep `CpuVertexRadii`** = exactly the 6×256 max-depth leaves. **LOD selects
  what is DRAWN, it does not prune the sampling tree.** So the sampler always hits the same max-depth leaf.

**So ground height IS deterministic and IS headless-safe. No action needed.** The `AnalyticGroundSampler`
swap is now only an optional simplification worth ≤243 mm (plus dropping the 1536-leaf CPU retention).

Small residual, in the RAYCAST layer not the sampler: `PlanetRaycastGrounding` resolved only **129/300** —
it needs a drawn mesh, so it IS camera-dependent, and falls back to the chunk sampler 57% of the time.
Two clients can differ ≤28 cm on standing height. Tolerable — server is authoritative on position, and
headless the raycast always misses and always falls back, which is correct.

**Method note worth reusing:** `mcp__unity__execute_code` + `codedom` (C# 6: no local functions, use
`System.Func`; fully qualify everything; no top-level `using`). Enter play, wait ~40 s for generation, drive
the camera with `ConsoleController.RunCommand("camera.look-at ...")` — `FreeCameraController` does not snap
it back. Persist pass-1 data to a scratchpad CSV and read it in pass 2 to compare across calls.

**LESSON 1: reading code suggested a desync hazard on the most-touched gameplay query; 15 minutes of measuring
found the opposite. `ReleaseCpuDataAfterBake` reads like it prunes; the shipping config retains every leaf.**

**LESSON 2, worse: it was already settled in writing and I hadn't read the doc.**
`docs/design/2026-08-09-surface-unification.md` is a **decision of record** (rev 2, Codex-reviewed) that
enumerates all **8** surface representations in a table and states in SU1 that
`IPlanetSurfaceSampler.TryGetSurfaceRadius` "is fixed-depth leaf bilinear", `Camera/LOD? = fixed`, because
"only *which* rendered leaves are drawn is camera-selected". Policy = **per-consumer authority + explicit
error budget, NOT one unified ground truth.** ALWAYS check that doc before reasoning about ground height.

What my run actually contributed: it closed that doc's open **follow-up 1** (the land test of mesh-hit vs
silent fallback, specified 2026-08-09 and never run). Numbers added there as a dated addendum —
raycast resolves **129/300 = 43%**, so the analytic fallback is the MAJORITY path away from the camera,
disagreement bounded ≤0.28 m. Caveat owned in the addendum: SU3 asked for a predeclared acceptable fallback
rate and I did not predeclare one, so the rate is descriptive, not pass/fail.

Also from that doc, SU5, relevant if a headless server ever needs ground without the managed quadtree:
build "an immutable, Burst-readable fixed-depth triangle/radius atlas once per terrain generation" — the
grass surface atlas is already that shape. Do NOT traverse the managed quadtree from Burst. Days of work, low-res slow grid.

**Corrections to my earlier claims:** biome map bakes on **CPU** under `Parallel.For` (not compute);
`GpuPlanetTerrain.compute` is a **dead asset**, zero references. GPU-authored-scatter stage 4 deletes the
**streaming machinery** (`Reeval`, LRU, buckets), NOT the placement math — less hostile than I first said.

**Already-built good news:** `ScatterGatherJob` carries `[BurstCompile(FloatMode = FloatMode.Deterministic)]`
— the only float-mode pin in the codebase, on exactly the job deriving gameplay identity. And
`WaterQueryService.cs:10-14` already says "two clients derive the same answer without exchanging anything."
Six other `[BurstCompile]` sites take the default; pin the gameplay-truth ones (noise, biome).

**Client cache (ADR-13):** clients persist received deltas keyed by world + highest sequence; rejoin sends
`lastSequence` and gets only what changed. Guarded by a `worldEpoch` that bumps if the server loads an
older save (otherwise restoring a backup silently desyncs everyone). Cache is NEVER authoritative.
Host migration stays out of scope — the dedicated server is the real answer.

## §0 gameplay contract — PROPOSED, Bryan has NOT ruled on it

Written 2026-08-21 as §0 of the doc (placed before §1 so the existing 17 sections didn't renumber).
Fantasy: *"a wizard homesteading a wild planet — you don't learn magic from a teacher, you take it apart."*
Valheim skeleton, but **experimentation replaces the combat-and-boss spine.**

10-min loop = harvest (≈1 node in 7 pays an orb) under **three non-combat pressures: mana clock, bag grid
shape, overflow temptation.** 1-hour = go out with an intention, try ONE new imbue, come back, run a ritual,
bank one upgrade. Session goal = **one capability you didn't have when you sat down.** 40-hour arc =
casting → composing, peaking at an **earned overpowered moment** found by understanding, not questing.
MP value = sub-linear ritual contribution curve (why 2-4 optimal, 8 ceiling) + overflow buffs nearby allies
+ Magic Knowledge and crafting are separate axes so specialising is real + the world persists on the host.

**THE OPEN DESIGN QUESTION, still Bryan's:** where does threat come from in the first 10 minutes? The
contract offers only RESOURCE tension, no DANGER tension. Three exits — (1) overload/catastrophic imbue IS
the danger (cheapest, most on-theme), (2) bring creatures forward from post-M6 (costs combat+AI+health),
(3) weather/night as hazard (cheap, and it's the trigger that forces the CPU weather port). I recommended
**1 now, 3 next, 2 when the AI spine exists.** Not blocking M0-M2.

**6 FALSIFIABLE CLAIMS** (the point of the contract — a playtest can prove them wrong): C1 harvest holds
attention because it pays a magic dividend; C2 imbuing is what players tell friends about; C3 two players
FEEL the ritual speedup; C4 the bag grid creates real decisions (fails if nobody ever rotates an item);
C5 mana scarcity changes behaviour (fails if players never hit empty); C6 overflow tempts.
**C1/C4/C5 are testable at M2 with no magic built** — instrument the harvest loop.

## READ THESE BEFORE ANY GAME-ARCHITECTURE WORK — I missed all of them first time

**`docs/PROJECT_PLAN.md`** is the MASTER PLAN INDEX (2026-04-25) and `.agent-skills/pp-docs-and-memory` says
Bryan owns it. It indexes **17 chapters in `docs/phases/`** (00-architecture, 00-code-architecture,
00-cross-cutting, then phases 1-15). I wrote a 1100-line game architecture doc without reading ANY of them.
A sweep found: 3 factual errors, 6 silent reversals of founding decisions, 2 whole omissions, 17 things I
independently re-derived that were already written in April.

**`docs/design/2026-08-09-surface-unification.md`** — decision of record, 8 surface representations, see above.

**The three factual errors, as cautionary examples:**
1. Called `WorldActionManager` "completely unused, decide whether to delete" — it has **4 live console
   commands** (`action.undo/redo/history/clear`) AND an explicit `planned:` marker citing docs/phases.
   **CLAUDE.md says a `planned:` marker exists to permanently stop an audit re-litigating a settled call.
   I re-litigated a marked site. Always grep for the marker before calling anything dead.**
2. Proposed `SeedProvider.GetSeedForEntity(scatterId)` — real signature is `(ChunkCoord, int)`. Didn't typecheck.
3. Called `SeedProvider` "already built and correct" — `GetSeedForChunk`/`GetSeedForEntity`/`ChunkCoord`/
   2 `CoordinateConverter` methods have **ZERO callers**.

**The trap that already bit once:** `CLAUDE.md`'s dead-code rule lists `docs/design/`, `plans/`,
`advisor-plans/`, `.agent-memory/` as protected intent sources — **`docs/phases/` is NOT in that list.**
So `ObjectPool`/`IObjectPool<T>`/`PoissonDiscSampling`/`EventBusAutoBinder` were DELETED at `f63ec14`
because "usage appears only in docs/phases/". Still exposed: `MoonPhaseChangedEvent`,
`DayNightChangedEvent`, `CelestialManager.MoonFullness`, `BiomeType.Cave`. **Recommend adding
`docs/phases/` to that CLAUDE.md list.**

**Two things the phases docs have that nothing else does:**
- **Where mana orbs come from**: weighted LOOT TABLES on harvest. "Tree → Wood ×3 (100%), Mana Orb ×1 (15%)"
  (`00-architecture.md:91`), rolled with a deterministic `(chunk seed + entity index)` "so multiplayer
  clients agree on drops" (`:90`) — they specified my §7.3 rule 2 four months before I re-derived it.
  Without this, magic is disconnected from the survival loop. `PROJECT_PLAN.md:4`: "future magic system
  with mana orbs as world drops."
- **Marching cubes / caves / digging was a FOUNDING decision** (`00-architecture.md:5`). I omitted it in
  1100 lines. It matters because **deformable terrain would invalidate the ground-height determinism proof** —
  dug ground is no longer a pure function of the seed. Now §6.5, with `DeltaKind.TerrainDeform` reserved.

Also specified there and nowhere else: Valheim structural integrity (support propagates from foundations,
wood<stone<metal), respawn timers (needs a time field in the delta record — an M0 schema decision),
base defense, boats raft→karve→longship, map/waypoints/compass, spell discovery in caves/ruins, stamina.

Related: [[project-game-vision]], [[project-gameplay-roadmap]], [[project-all-generated-props]],
[[reference-collision-strategy]], [[reference-state-machine-project]], [[project-startup-generation-perf]]
