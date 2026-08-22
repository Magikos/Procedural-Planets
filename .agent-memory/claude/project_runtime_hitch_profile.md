---
name: project-runtime-hitch-profile
description: Travel stutter SOLVED in two rounds (2026-08-16 optimise, 2026-08-19 stage across frames) — it was always Reeval, the tile re-plan; Bryan confirmed smooth. Reeval scales with PROTOTYPE COUNT, so adding species re-opens it.
metadata:
  type: project
---

Measured on Bryan's machine via Unity MCP. Seed `1691104419`, 30 s `ScatterFlyBench` runs at 30 m/s,
warm cache. Fixed in commit `764a0fd`. Design follow-up: `docs/design/2026-08-16-gpu-authored-scatter.md`.

## The cause

`ScatterTileCache.Reeval` re-plans the tile set on the main thread every `ReevalMoveMeters` = **40 m
of travel** — so the spikes are distance-triggered, not time-triggered, and appear at any speed. Its
inner loop ran **per prototype**, recomputing tile geometry that does not depend on the prototype.
At 109 prototypes that was ~1.8M iterations and ~36 MB of list traffic per re-plan.

Second cost, found after fixing the first: `ScatterGpuDraw.DrawProto` re-copied a prototype's ENTIRE
matrix array through an `IReadOnlyList` indexer into a staging array on any change. 18.6M matrices
copied per 30 s of flight.

| | avg per re-plan | uploads per 30 s |
|---|---:|---:|
| before | 170.6 ms | 1591 ms |
| after | **28.6 ms** | **222 ms** |

Frames >100 ms in a 30 s flight: **22 → 1**.

## Cleared suspects — do not re-investigate

- **GC**: 2 collections per 30 s. Incremental GC is on (3 ms slice). Heap is ~2 GB, which is worth
  reducing, but it is not the travel stutter.
- **Chunk mesh page-in**: 0.5 ms per build, 1.32 ms worst, 69 ms per 30 s.
- **Scatter gather job wait**: 347 ms per 30 s, worst 56.9 ms. Commit: 14 ms per 30 s. The existing
  frame-spreading works.

## Two bugs found on the way

- `ReadyMask` was a single `ulong` (64 bits) with 109 prototypes. Prototype 64 read prototype 0's
  bit, so tiles reported ready for variants never gathered — **the tree variants in slots 73-108 were
  largely not rendering**. Fixed with a bitset. Live instances 200k → 894k.
- That bug was also acting as an accidental performance limiter. Fixing it restored the intended
  density, which is why frame cost rose even as the spikes fell.

## Current state and the open question

After the fixes, at ~1.1M instances the profile is 177 frames ≤33 ms, 123 in 33-100 ms, 1 over
100 ms. Spikes are gone; the baseline is now the limit. At rest, CPU main 23.7 ms and GPU 24.7 ms —
**near-balanced, so neither side alone is the ceiling**.

**All of these numbers are Editor numbers.** Before cutting density or starting the GPU-authored
project, measure a standalone build — Editor overhead may account for much of the 33-100 ms band.

## Build vs Editor — measured 2026-08-17, and it changes the conclusion

A non-development Mono player, 1920x1080 windowed, same scene and seed:

| | Editor | **Build** |
|---|---:|---:|
| planet generation | ~40.0 s | **15.0 s** |
| `finalize` | ~7.0 s | **0.06 s** |
| frames >100 ms in a 30 s flight | 1 | **0** |
| frames ≤16.7 ms | — | **287 of 301** |
| worst frame | 152 ms | **43 ms** |

`finalize` collapses because a player uses the **prebaked impostor atlases**; the live bake only ever
runs in the Editor. So the impostor-bake optimisation matters for Editor iteration, not for shipping.

**Caveat on the flight numbers**: the build run landed at 118k-277k live instances, not the 1.1M of
the Editor forest test, because `scatter.goto` is not registered in a player ("unknown command") and
the camera was placed on the surface under wherever it already was. Density is not matched, so this
shows the build is dramatically faster but does NOT prove smoothness at full forest density.

To rerun: a temporary `-autobench` hook in `Planet.cs` (reverted after use) placed the camera via
`TryGetSurfaceRadius`, created a `ScatterFlyBench` at runtime and quit on completion. Use wall-clock
`Awaitable.WaitForSecondsAsync`, never frame counts — a build runs several times the Editor's frame
rate and frame-based waits expire early. Player log:
`%USERPROFILE%\AppData\LocalLow\Magikorp\ProceduralPlanets\Player.log`.

## Round 2 — 2026-08-19, commit `d4fe217`. Bryan confirmed smooth.

A residual "small stutter every second or so" remained after round 1. It was **Reeval again**.
Prototypes had grown 109 → **176** (rock and plant variants joined the tree ones), and Reeval scales
with prototype count, so 28.6 ms had grown back to **30.5 ms, worst 59.3 ms, firing once per second
at 40 m/s** — the cadence Bryan reported, exactly.

**The split showed no hotspot left**, which is what chose the fix:

```
evict 5.0ms   candidates 7.7ms   sortTiles 6.8ms   filter 9.5ms   sortWork 1.5ms
```

Evenly spread → optimising any one stage buys ≤9 ms. The work is inherently ~30 ms, so the fix was to
**stop doing it in one frame**. Reeval is now a five-stage state machine, one stage per `Update`. It
fires once a second, so ~60 idle frames are available and the latency is irrelevant. The new plan
builds into `_workNext` and swaps at Publish so the worker never sees a half-built plan; an epoch
change aborts an in-flight re-plan.

| | before | after |
|---|---:|---:|
| worst single-frame re-plan | 59.3 ms | **14.7 ms** |
| frames >100 ms | 16 | **0** |
| frames 33-100 ms | 189 | 75 |
| frames 16.7-33 ms | 94 | 221 |

**Forward-looking:** staging bounds the per-frame cost, but total re-plan work still grows with
prototype count. Adding many more species raises the per-stage cost again. Next lever if it returns
is chunking *within* a stage (filter and candidates first) — deliberately not built, since one stage
per frame already fits the budget.

## Method notes

`ScatterFlyBench.StartBench(speedMps, seconds)` is callable from MCP `execute_code` via
`FindFirstObjectByType`; it logs a per-sample CSV (`t, dist, speed, frameMs, pending, live, tiles`).
Read the distribution, never just the worst frame — bimodal means a periodic block, a rising ramp
means a runaway. Spike *spacing* converted to metres is what identified the 40 m trigger.

Before trusting any static-counter probe in play mode, confirm exactly one copy of the type is
loaded (see [[reference-unity-mcp]]) — HotReload silently splits statics and reports zero.

Related: [[project-startup-generation-perf]], [[project-scatter-gather-perf]], [[reference-unity-mcp]]
