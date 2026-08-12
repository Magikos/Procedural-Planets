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

**DECIDED 2026-08-12 by Bryan: "the player will be down there."** The seabed is real content,
not set dressing. The cheap version — a shallow shelf band decorated for viewing from above — is
ruled out. Consequences: depth bands are a *biome axis* (shelf/reef/slope/deep want different
prototype sets, not one set thinned by depth); underwater lighting, caustics and fog become
required rather than optional; candidates must be benched at swimming eye height, since scale
reads differently from a camera above the surface.

Still open: whether `Cave` (15) — the same unemitted-biome gap, also somewhere the player will
physically be — folds into this pass or stays separate. Ocean scatter does *not* block on
swimming being implemented; the free camera already goes underwater.
