---
name: reference-planet-architect
description: External reference project at D:\Planet_Architect_v0.1.5_Windows — IL2CPP-exported planet game with notable biome/climate/vegetation architecture; analyzed and compared 2026-06-05
metadata:
  node_type: memory
  type: reference
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
---

**Source path:** `D:\Planet_Architect_v0.1.5_Windows\Source\ExportedProject\Assets\`

**Analysis paper:** [`docs/research/2026-06-05-planet-architect-biomes-vegetation.md`](../../docs/research/2026-06-05-planet-architect-biomes-vegetation.md) — full breakdown of their biome, climate, vegetation, and terrain texturing architecture with recommendations on what to consider stealing.

**Key insights captured in the paper:**
- 9-biome Köppen pool with multiple `climateTargets: Vector2[]` per biome (temperature × moisture)
- **Voronoi assignment in climate space + domain warp + cleanup pass** — would close [[project-chunk-biome-seam]] entirely
- Per-biome `biomeOffset: Vector3` noise seed for vegetation placement (kills cross-biome lattice alignment)
- Layered terrain shader: 4 biome slots + snow/slope/coast overrides with smoothstep cutoffs
- Per-species 2D Gaussian climate niche (`targetTemperature/Variance`, `targetPrecipitation/Variance`)
- Plate tectonics simulation (19 plates) for mountain ranges

**How to read this project quickly:** Game is IL2CPP — only 8 trivial C# files survived export. **The meaningful info lives in:**
- `Assets/MonoBehaviour/*.asset` (ScriptableObject YAML configs — biomes, species, world recipe)
- `Assets/Shader/Shader Graphs_*.shader` (Properties blocks only — bodies are dummy exporter stubs but uniforms reveal what the graph consumes)
- Asset filenames (`Continental.asset`, `Birch.asset`, `EarthlikeSettings.asset` etc.) reveal the taxonomy

You can reconstruct ~80% of the design without touching DLLs.

**Use when:** considering biome system changes, Phase B biome textures design, the chunk seam issue, vegetation placement design, or any time Bryan asks "how would I do X for a planet game."
