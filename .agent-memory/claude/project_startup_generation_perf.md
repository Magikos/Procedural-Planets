---
name: project-startup-generation-perf
description: Startup perf arc — planet gen 76.3s → ~40.3s in two measured wins (redundant per-vertex resolve, then impostor bake readbacks); Codex plans 002/003 rejected on evidence
metadata:
  type: project
---

Branch `harvest-vertical-slice`, **UNCOMMITTED**. All numbers measured on Bryan's machine via
Unity MCP, seed `1691104419` (`SceneBootstrap.WorldSeed = 12345` → deterministic fresh starts).
Full write-up: `docs/design/2026-08-16-overnight-perf-session.md`.

## Arc

| Stage | before | after both wins |
|---|---:|---:|
| colors (Phase B vertex) | 40.8 s (30.1 s) | 15.3 s (4.8 s) |
| finalize | 17.8 s | **7.0 s** |
| **total** | **76.3 s** | **~40.3 s** |

**Win 1 — delete redundant work, not Burst.** Face-atlas mode only needs climate per vertex; the old
path also ran a domain-warped assignment-field lookup + `LakeMask` + DTO resolve for each of
19,250,814 vertices, feeding only debug mode 73. `ColorGenerator.GetClimateData` replaced it;
`CalculateChunkBiomeData` and `IBiomeProvider.GetBiomeData` deleted as dead. **Byte-identical
atlases PROVEN** (old vs new both `ids=57C8EB2B29A2961C`) by reverting only the changed files while
keeping the checksum instrumentation — reuse that trick.

**Win 2 — impostor bake.** `finalize` was 34% of load and uninstrumented; splitting it showed
`ScatterRenderer.Configure()` = 12.8 s of 12.9 s. `ScatterImpostorBaker.BakeAtlas` did `ReadPixels`
per cell per pass = **128 GPU sync stalls per prototype**. Now renders each cell into a viewport
rect (`cam.rect`) of one atlas-sized RT with **one readback per pass**: 617 → ~370 ms per bake.
Sharing was already fine (109 prototypes → 21 live bakes, 36 shared, 33 prebaked).

## Both Codex plans rejected on evidence

- **002 (Burst climate, L effort)** — unnecessary. `vertex` is 4.8 s, under its own 8 s gate.
  Burst would still help (`climateEvalCpu`=122 s vs `climateAllocCpu`=72 ms, so it IS math-bound,
  ~3-5× available ≈ 3.5 s) but it is now the 4th-biggest item.
- **003 (rolling histogram, M effort)** — cannot reach its gate. `topKCpu` share 0.41;
  `BuildHighResIdGrid` is ~59% of mapBake and the plan doesn't touch it.

## Next targets (measured, in order)

1. `colors` 15.3 s — `mapBake` ~8.3 s of it, dominated by `BuildHighResIdGrid`'s ~35.5M
   assignment-field+lake samples (NOT the smoothing 003 targeted).
2. `finalize` 7.0 s — all impostor bakes. Cacheable to disk **once the seed bug below is fixed**.
3. terrain 6.9 s, water 7.0 s — untouched.

## Blocking bug for further impostor work

`TreeInjection.cs:106` uses `HashCode.Combine` for variant seeds — randomized per process, so trees
are structurally different every session for the same world seed (proven: topology + bounds XOR
differ across two runs of identical code). Fix with `ScatterHash`. See [[project-tree-generator]].

Related: [[project-runtime-hitch-profile]], [[reference-unity-mcp]], [[feedback-audit-workflow]]
