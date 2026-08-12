---
name: project_ocean_scatter
description: Ocean/seabed scatter is unbuilt — Underwater and Cave biomes are declared but never emitted, and scatter has no below-water placement mode
metadata:
  type: project
---

2026-08-12, tracked at [docs/design/2026-08-12-ocean-scatter.md](../../docs/design/2026-08-12-ocean-scatter.md).
Bryan asked for this to be tracked after a coral passed the asset bench with nowhere to go.

**Verified state, not assumption:**

- 69 scatter prototypes cover `BiomeType` 1–14, 17, 18. **`Ocean` (0), `Cave` (15) and
  `Underwater` (16) have zero prototypes.**
- `BiomeType.Underwater` and `BiomeType.Cave` are **declared in the enum and emitted nowhere** —
  `BiomeRegistryDto` only ever returns Ocean↔Beach blends. They are placeholders.
- **Scatter cannot place below the waterline.** The two water paths do not compose into it:
  `MinWaterClearanceMeters` is a *rejection* gate (`ScatterGatherBurst` drops instances with
  `altitudeMeters < MinWaterClearance`), and `OnWater` pins to the sea surface for lily pads
  (`ScatterField`: `placeRadius = SeaRadiusLocal + OnWaterSurfaceOffsetMeters / scale`).
  Nothing places on the terrain *under* the water.

**Why: needs a depth axis, not just a biome.** A coral shelf and an abyssal plain are the same
`Ocean` cell today. Depth is what makes ocean scatter look deliberate rather than sprinkled.

**How to apply:** treat this as a biome feature like [[project_lake_biome]], not an import. The
work is: emit a seabed biome → add a below-water placement mode with CPU/Burst parity → decide
how submerged props take the water volume's fog/absorption and caustics → depth-aware density.
A reef is a colony, so it likely wants [[project_scatter_clumping_direction]].

Open question Bryan has not answered: whether the seabed is real content (swimming/fishing are
in [[project_game_vision]] as later features) or set dressing seen only from above — that choice
changes the scope by a lot.
