# 2026-06-06 — Biome overhaul slice 1a: BiomeOffset field

**Status:** Shipped and audited. Build verification refreshed after the 2026-06-06 audit.
**Design source:** [`docs/design/2026-06-05-biome-climate-overhaul.md`](../design/2026-06-05-biome-climate-overhaul.md) §3.1 / §4 Step 1a.
**Trigger:** Bryan's 2026-06-06 5-step plan, step 1 ("biome normalization/blend changes").

## What

Added `BiomeOffset: Vector3` to [`BiomeDefinition.cs`](../../Assets/Scripts/Planet/Biomes/BiomeDefinition.cs) under a new "Placement noise" header with tooltip. Populated all 15 biome `.asset` files with unique pseudo-random offsets.

## Files changed

- [`BiomeDefinition.cs`](../../Assets/Scripts/Planet/Biomes/BiomeDefinition.cs) — new field + header + tooltip.
- 15 biome SOs (`Assets/Settings/Planet Settings/Biomes/*.asset`) — `BiomeOffset:` line inserted after `TintPercent:`.

## Initial values shipped

Hand-picked unique Vector3s, deterministic but arbitrary, all in ~1000-10000 range so noise sampling sees meaningful spread:

| Biome     | BiomeOffset                |
| --------- | -------------------------- |
| Ocean     | (1023, 5847, 3291)        |
| Beach     | (7416, 2530, 8169)        |
| Desert    | (9472, 1638, 4805)        |
| Savanna   | (3061, 7924, 1357)        |
| Tropical  | (5829, 4106, 9743)        |
| Scrub     | (2614, 8395, 6182)        |
| Grassland | (8147, 3528, 7619)        |
| Forest    | (4836, 9265, 2473)        |
| Steppe    | (6291, 1748, 5036)        |
| Taiga     | (1502, 6873, 4291)        |
| Swamp     | (7384, 5102, 3964)        |
| Tundra    | (3925, 8160, 1247)        |
| Snow      | (5273, 4581, 6928)        |
| IceBog    | (8056, 3914, 2785)        |
| Mountain  | (4128, 7536, 9051)        |

Re-tunable per biome in the inspector. Re-tuning won't break anything — consumers will just sample noise at a different offset.

## Why this matters (when consumers arrive)

Without per-biome offsets, vegetation/prop placement noise sampled at the same world position with the same scale gives the **same noise value** regardless of biome. Result: a "Forest" patch shape and a "Grassland" patch shape would align at biome boundaries — visually obvious "same template, different colors" tell.

With per-biome offsets, each biome samples a different region of the noise field. Boundaries show **distinct placement textures** on either side. No alignment artifacts.

## No consumers yet

This slice intentionally lands the field with no wiring. Future consumers:

- Phase C grass placement (step 4) — read `biome.BiomeOffset` when sampling density / patch noise.
- Prop/tree placement (step 5) — same pattern.

Pattern at consumption site (pseudocode):

```csharp
float density = SamplePerlin(worldPos * scale + biome.BiomeOffset);
```

## Build verification

```
dotnet build ProceduralPlanets.Core.csproj   → 0 warnings, 0 errors
dotnet build ProceduralPlanets.Planet.csproj → 0 warnings, 0 errors
```

## Validation guidance for next session pickup

1. Open any biome SO (e.g. `Forest.asset`) in Unity inspector. Confirm "Placement noise" section appears with the populated Vector3.
2. Edit a value, save, reopen — should persist.
3. No visual change in-game yet (no consumers). That's correct.

## Audit follow-up and slice 1b foundation

The audit corrected two assumptions in the original handoff:

- `TemperatureProvider` was already latitude plus noise; it was not pure noise.
- Altitude-aware temperature cannot fit the old one-argument provider interface cleanly.

The code now has one canonical `IClimateProvider.Evaluate(pointOnUnitSphere, elevation)` call that returns a normalized `ClimateSample` containing temperature, moisture, and elevation. `ColorGenerator` evaluates that sample once per point. Current biome output is intentionally unchanged.

The revised design keeps temperature/moisture normalized in `[0,1]`, defers physical Celsius conversion until world-scale calibration is defined, and requires climate curves to be baked to LUTs before job/compute generation.

## Repository audit fixes landed with this handoff

- Console UI and programmatic command execution now share one cancellation-source ownership path. A second command cannot replace the token source of a pending command; only `console.cancel` and `console.abandon` bypass the pending guard.
- Planet generation, world actions, and debug captures propagate cancellation and failures to their caller instead of reporting false completion.
- Redo advances the history cursor only after the action succeeds.
- Cancelling `debug.capture` during console fade-out reopens the console correctly.
- Runtime-created console themes are disposed; resource assets remain owned by Unity.
- Console diagnostics use the project `ILogger` path instead of direct `Debug.LogWarning`.
- `_WindDirection` now consistently means the world-space direction features move toward. Cloud, cloud-shadow, and precipitation noise advection use the matching `position - velocity * time` convention.
- All 15 biome YAML assets retain their unique offsets without UTF-8 BOM churn.
- Core and Planet builds pass with zero warnings and zero errors.

## What's next (slice 1b)

Slice 1b is implemented in [`2026-06-06-biome-1b-climate-latitude.md`](2026-06-06-biome-1b-climate-latitude.md). Unity/F10 validation is the remaining gate before Voronoi/domain-warp assignment.
