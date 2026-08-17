# Overnight perf session — 2026-08-16

Autonomous session while Bryan slept. Everything below is measured on his machine via Unity MCP,
seed `1691104419`, `Planet` scene, Editor play mode. Nothing is committed.

## Headline

Planet generation **76.3 s → ~40.3 s** across the whole arc, in two independent wins:

| Stage | Original | After redundant-resolve fix | After impostor fix |
|---|---:|---:|---:|
| colors (Phase B vertex) | 40.8 s (30.1 s) | 15.4 s (4.8 s) | 15.3 s |
| finalize | 17.8 s | 12.9 s | **7.0 s** |
| **total** | **76.3 s** | 45.9 s | **~40.3 s** |

Verification runs after cleanup: 40,675 ms and 40,026 ms, console clean.

## Win 2 (this session): impostor atlas bake

`finalize` was 34% of load and had never been instrumented. Splitting it showed a single call:

```
grass=0ms surfaceEdits=53ms shaderGlobals=0ms harvest=20ms
scatter=1ms scatterRenderer=12783ms generatedEvent=15ms
```

`ScatterRenderer.Configure()` bakes impostor atlases. Probe:

```
prototypes=109   liveBakes=21   sharedHits=36   prebaked=33
bakeMs=12952  avg=617ms per bake
```

Atlas sharing already works; the cost is the bake itself. `ScatterImpostorBaker.BakeAtlas` rendered
each of 64 cells into a cell-sized RT and did `ReadPixels` + `Apply` **per cell per pass** — 128
GPU→CPU sync stalls plus 128 `Texture2D` allocations per prototype.

**Fix**: render every cell into its own viewport rect (`cam.rect`) of one atlas-sized RT, then do
**one** readback per pass. The per-pixel coverage/alpha math is unchanged, just applied once over
the whole atlas instead of per cell.

Result: **617 ms → ~370 ms per bake, 12.9 s → ~7.0 s** for the stage.

## Runtime stutter: my earlier report was wrong

The "44 spikes of ~160 ms, metronomic" profile reported before bed was measured in a
HotReload-patched domain (two copies of the type loaded, patched code deoptimized). In a **clean
domain** the same 60 m/s flight gives:

| | contaminated | clean |
|---|---:|---:|
| ≤33 ms | 255 | 259 |
| 33-100 ms | 2 | 41 |
| >100 ms | **44** | **1** |

Attribution of the remaining cost, measured in the clean domain over a 30 s flight:

- scatter gather job wait: **347 ms total**, avg 5.1 ms, worst 56.9 ms
- scatter commit: **14 ms total**, worst single 0.1 ms
- chunk mesh page-in: **69 ms total**, avg 0.58 ms, worst 1.32 ms

None of these explains a stutter budget worth chasing. The frame-spreading already in
`ScatterTileCache` works. **Do not rebuild the mesh upload path or the gather to chase hitches** —
both were measured innocent.

## BUG FOUND (not fixed — needs your call)

`Assets/Scripts/Planet/Trees/TreeInjection.cs:106`

```csharp
int seed = Mathf.Abs(HashCode.Combine(p.DisplayName ?? "tree", variant)) % 900000 + 1;
```

`System.HashCode` is **randomized per process** — .NET does not guarantee stability across runs.
So every session gives each tree variant a different seed. Measured across two runs of identical
code, same world seed:

- mesh topology XOR (vertex + triangle counts): `7F533F08AA8B6F05` vs `8F34DD08C909351C`
- mesh bounds XOR: `0461DF79C05FE51A` vs `65BB607D98759127`
- baked atlas XOR: differs every run

Trees are therefore **structurally different every session** for the same world. `age` also derives
from this seed, so tree ages shuffle too. `TreeStructureGenerator` itself is correctly seeded
(`Random.InitState`, restored in `finally`) — the defect is only in seed derivation.

Consequences: worlds aren't reproducible; a saved/harvested world may not regenerate the same
trees; and it makes impostor atlases impossible to cache across sessions.

**Fix**: replace `HashCode.Combine` with a stable hash. `ScatterHash` already exists in the repo
for exactly this. One line. Left undone because it changes tree appearance, which is your call.

Fixing it also unlocks the next big win: with stable seeds, the 21 live bakes (~7 s) can be cached
to disk and reused, taking `finalize` toward zero.

## What changed in the working tree (uncommitted)

- `Assets/Scripts/Planet/Scatter/ScatterImpostorBaker.cs` — the single-readback bake.
- `Assets/Scripts/Planet/Planet.cs` — `finalize=` phase timer plus a `Finalize timings` line
  splitting it seven ways. Kept because it is what found this.
- Earlier session (unchanged tonight): `ColorGenerator`, `ChunkedSurfaceProvider`,
  `IBiomeProvider`, `BiomeMapBaker`, `BiomeAtlasService`, `PlanetVertexColor.shader`,
  `DebugModes.hlsl`, `ScatterGatherParityTests`.

All probes reverted. `ScatterField.cs` / `ScatterRenderer.cs` / `ScatterFlyBench.cs` (your
`scatter.backlight` work) untouched. Core, Planet and EditMode assemblies all build with 0 errors.

## Verification limits — read this

Exact pixel parity for the impostor change **could not be proven**, because the bake is
nondeterministic run-to-run *before* any of my changes (see the bug above). Two runs of identical
code produce different atlases. So:

- verified: far-field renders correctly at noon from 260 m — proper conifer silhouettes, correct
  framing, correct distance fade. Evidence: `local-only/perf/2026-08-16-overnight/impostor-farfield.png`
- not verified: byte-for-byte equality with the old bake path

If you want that guarantee, fix the seed bug first — then the hash oracle becomes usable and I can
prove the bake change is neutral.

## Measurement protocol that made this reliable

HotReload silently poisons static-counter probes in play mode. Before trusting any probe:

```csharp
int copies = 0;
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
    if (asm.GetType("YourType") != null) copies++;   // must be 1
```

Edit only while stopped, compile, verify one copy, then play.
