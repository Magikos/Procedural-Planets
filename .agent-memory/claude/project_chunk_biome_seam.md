---
name: chunk-biome-seam
description: "Known polish issue — faint chunk-boundary color seams in the top-K biome blend bake (kernel can't see across chunk boundaries)"
metadata:
  type: project
---

Phase B step 5b ships with faint chunk-boundary color seams on the planet surface. Mitigated 2026-05-31 via edge-replication kernel sampling (every texel gets the full 25 samples; out-of-range cells replicate the nearest valid cell), but not eliminated.

**Root cause:** [BiomeMapBaker.SampleTopKPerTexel](../../Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs) builds a per-chunk high-res biome id grid then runs a 5×5 kernel per output texel. At a chunk's edge, the kernel can only sample inside that chunk's bounds. Neighbor chunk A's east-edge texel kernel looks inward into A; chunk B's west-edge texel kernel looks inward into B. Different interiors → different top-K distributions → faint seam at the shared world-space boundary.

**Why:** The bake uses chunk.CpuBiomeData (bilinear-sampled vertex data) which only covers chunk UV [0,1]. Direct noise evaluation outside that range needs the temperature/moisture/elevation providers, which aren't currently plumbed into the bake path.

**How to apply when picking this up:**
- True fix: extend the high-res biome id grid by KernelRadius cells on each side, populated by direct noise evaluation (TemperatureProvider, MoistureProvider, ShapeGenerator) rather than vertex grid sampling. Adds ~10% overhead to the bake (132²/128² grid + extra noise evals). All four neighbors will then produce identical IDs in their shared border region → seamless.
- Cheap alternative: parent-chunk vertex grid fallback — when sampling outside a leaf's UV bounds, bilinear-sample the parent chunk's CpuBiomeData (which covers a larger region).
- Step 6 (per-biome surface textures with triplanar high-frequency detail) will visually mask the remaining seam significantly. If it becomes invisible after step 6, this fix can be deferred indefinitely.

**Status:** Bryan saw it 2026-05-31 in F10 BiomeMapFlatColor capture, accepted as "pretty good for now" and approved moving to step 6. [[project-current-focus]]
