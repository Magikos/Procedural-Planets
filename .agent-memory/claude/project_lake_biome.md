---
name: project_lake_biome
description: Lake biome feature (2026-08-11) — lakes are their own biome (Lake/LakeShore) with shore reeds; how detection works, what's deferred (lilies-on-water, water tint)
metadata:
  type: project
---

2026-08-11 (branch `scatter-placement`, overnight): built lakes as their own biome, distinct from ocean,
with reeds/cattails on the shore — per Bryan's Synty pond reference (red cattails + lily pads + murky green
water). Design + full writeup: `docs/design/2026-08-11-lake-biome.md`.

**How it works.** Lakes = SMALL water bodies (the ocean is the one huge body). Detection is `LakeMask.cs`
(new): once per world gen, on a background thread, sample ground elevation over a 192²/face grid, flood-fill
the below-water cells PER FACE, tag components smaller than `LakeMaxCells` (1400) as lake **water**, and
dilate a 2-cell **shore** ring onto the surrounding land. `LakeMask.Current.Sample(direction)` returns
None/Water/Shore. Self-limiting: only small components become lakes, so the huge ocean can never mis-tag
(worst case = no lakes detected, never a broken ocean).

**Biome wiring.** `BiomeType` gained `Lake` + `LakeShore` (registry is slot-id-indexed, not enum-indexed, so
growing the enum is cheap; the load-bearing constant was the `+4` special-biome count -> now `+6`). Both
elevation resolvers take a `lakeState` arg (default 0 = unchanged): `BiomeLookupEvaluator.ResolveFromLandBiomes`
(Burst bake) + `BiomeRegistryDto.ResolveElevationOverrides` (managed scatter). `BiomeMapBaker` (terrain) and
`ColorGenerator.ResolveBiome` (scatter) sample LakeMask by direction and pass it. `Lake.asset` reuses Ocean
textures, `LakeShore.asset` reuses Swamp (muddy). Scatter needs NOTHING beyond `Biome = Lake/LakeShore` on a
prototype (membership is enum equality). Verified: ~3749 lake-water + 1296 shore cells -> Lake/LakeShore.

**Scatter.** `ScatterId.SlotBits` raised 6->7 (MaxSlot 127; determinism-safe, Pack id is ephemeral) to free
slots. Lake prototypes (slots 64-67, biome LakeShore): Lake Cattails (dense), Lake Reeds, Lake Rocks, Lake
Wildflowers — reuse existing reed/rock/flower meshes; `MaxAltitude 3m` hugs the water. See
[[project_scatter_biome_buildout]].

**DEFERRED (design in the doc, not done):**
- **Lily pads on water** — needs a lily asset imported (`D:\Unity\Explore Assets\Assets\Synty\
  PolygonNatureBiomes\PNB_Swamp_Marshland\Models\SM_Env_LillyPads_01.fbx`) + a new `OnWater` prototype flag
  that places at the SEA radius (not terrain radius) inside Lake cells (both TryPlace paths, keep parity).
- **Murky-green lake water tint** — the lever is `Ocean.shader` surface color tinted by `BodyFactor`, but
  Ocean.shader is the caustics don't-touch, so it needs Bryan's review / a WaterVolume-side approach.

**Known limitation:** per-face flood-fill can split a bay crossing a cube seam into a sub-threshold piece ->
false lake; isolated ponds are correct. Tunables in `LakeMask.cs`: `Res`, `LakeMaxCells`, `ShoreRings`;
cattail density = `SpacingMeters` on the Lake prototypes. Related: [[project_scatter_dusk_lighting]].
