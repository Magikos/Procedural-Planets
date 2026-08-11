# Lake biome — design + reference

_Started 2026-08-11 (overnight autonomous build on branch `scatter-placement`). Bryan asked for a full
best-effort pass on all four parts before morning: (1) lake biome identifier, (2) lake-shore scatter,
(3) lily pads on the water, (4) lake water tint. This doc captures the target and the plan; the morning
writeup at the bottom records what actually landed and what needs his eyes._

## The reference (Bryan's Synty pond screenshots)

The target look, from the two Synty reference shots:

- A **pond / lake** — an enclosed inland water body, distinct from the big ocean. Often has a small
  **central island** (with a tree on it).
- **Cattails / reeds** — dense stands of tall **red-brown vertical** cattails ringing the shoreline and
  clustered on the island. This is the dominant lake-edge plant.
- **Lily pads** floating on the water — flat green pads with **pink and yellow** flowers, scattered across
  the surface, denser near the shore.
- **Rocks** ringing the water's edge (grey/tan boulders).
- **Water** reads **dark murky green**, not the ocean's bright blue — shallow, still, a bit reflective.
- Surrounded by lush meadow (grass, bushes, trees, a footpath).

**Our current lake** (before this work): a plain water disc with a bright sandy ring, the ocean's blue
caustic surface, and no lake-specific plants — biomed identically to the ocean (Ocean water + Beach shore).

## How lakes work today (found before building)

- Water is flood-filled into connected **bodies** in `WaterMeshBuilder`; each vertex gets a `BodyFactor`
  (0 = small body = **lake**, 1 = large = **ocean**, via a size threshold `OceanBodyVertexThreshold`).
  Already drives freezing + water rendering. So lake-vs-ocean is known at the water layer.
- **Biomes are elevation-only** (`BiomeRegistryDto.ResolveElevationOverrides`): below `OceanThreshold` →
  `Ocean`, just above → `Beach`, else land/mountain. No water-body awareness, so a lake is biomed exactly
  like the ocean. That's the gap.
- `BiomeType` has no `Lake`. Reeds exist (`FoliageReeds`, `FoliageSwampBeard`); lily pads sourced from the
  external Synty library at `D:\Unity\Explore Assets\Assets`.

## Plan (four parts)

1. **Lake + LakeShore biomes.** Add to `BiomeType`. In the biome baker, flood-fill the below-water cells;
   small connected bodies → `Lake`, their one-cell shore ring → `LakeShore`. Self-contained (mirrors the
   water-body size test). Reuse existing ground textures (LakeShore ← Swamp, Lake ← Underwater/Ocean) to
   avoid authoring new atlas slices. Register in the biome registry; guard every `switch (BiomeType)`.
2. **Lake-shore scatter.** New prototypes on the `LakeShore` biome: red cattails (reuse/tint `FoliageReeds`),
   bushes, wildflowers, rocks. Reuses the whole scatter/gather system (Biome field → biome-map membership).
3. **Lily pads on the water.** Scatter doesn't place on water today; add a "float at the lake surface" path
   so lily prototypes (from the asset library) sit on the `Lake` water plane, denser near the shore.
4. **Lake water tint.** Give `Lake` bodies a murky-green tint distinct from the ocean, driven by `BodyFactor`
   — WITHOUT touching the caustics (Ocean.shader caustics are hard don't-touch); use a tint global / the
   existing body-factor path only.

## Implementation notes (found during prep)

- **Lake detection** = mirror `WaterMeshBuilder.ClassifyWaterBodies`: flood-fill (BFS) the below-water cells,
  component size → `SmoothStep(InverseLerp(threshold*0.25, threshold, count))`; `< 0.65` = small = lake.
  `largeBodyThreshold = max(24, OceanBodyVertexThreshold)`. Do the equivalent on the biome baker's cell grid;
  small body's cells → `Lake`, one-ring dilation of shore land cells → `LakeShore`.
- **Biome elevation classify**: `BiomeRegistryDto.ResolveElevationOverrides` (`:147` Ocean, `:160` Beach). The
  lake tag must override Ocean→Lake and Beach→LakeShore where a cell belongs to a small body.
- **Scatter slots FULL**: `ScatterLibrary.asset` (Assets/Resources/Settings) has 64 prototypes, slots 0-63 all
  used. `ScatterId.SlotBits=6` → MaxSlot 63. Raise **SlotBits 6→7** (MaxSlot 127) — packs into the spare bit
  63 (PlayerShift 62→63, still ≤64 bits); determinism-safe (Pack ID is ephemeral, ScatterHash.Slot unchanged).
- **Cattail template** = clone `Swamp Reeds Prototype.asset` (slot 40): Biome Swamp→**LakeShore**, `FoliageReeds`
  mat (tint red for cattails, or use a library cattail FBX), `HasMaxAltitude=True MaxAltitudeMeters=3` keeps it
  at the water edge, `ConformToSlope 0`, mesh `SM_Env_Reeds_01`. Also `Beach Reed` (slot 61), `IceBog Reeds` (41).
- **Lily-on-water (Stage 3)** needs a NEW placement path: scatter places on the terrain (ShapeGenerator radius),
  but a lily must sit at the **sea radius** (float) inside Lake water cells. Add an "on-water" prototype flag
  that places at the water surface instead of the terrain surface, gated to the `Lake` biome.
- **Lake water tint (Stage 4)**: `Ocean.shader` surface color = `lerp(_ShallowColor, _DeepColor, depthBlend)`
  at `:647-649`; caustics are a SEPARATE WaterVolume feature (`:50`). Ocean.shader is still don't-touch, so
  attempt a minimal murky-green tint by the per-vertex `BodyFactor` (already plumbed as `oceanFactor`) only if
  it can be isolated from caustics; otherwise flag and skip. Lowest priority.

## Morning writeup (filled in as work lands)

_(TBD — see commits + the summary message.)_
