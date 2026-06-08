# 2026-06-06 — Climate-aware grass color (design draft)

**Status:** Draft for review. No code touched.
**Trigger:** Bryan's 2026-06-06 5-step plan, step 4 ("grass tuning with biome-edge variation"); also captured as "new idea #1" from the post-Codex review.
**First demonstration of:** [[feedback-settings-dto-pattern]] — every new consumer gets an immutable DTO, never reads a Settings SO directly.

## What we want

Per-blade grass color should drift with local **moisture**, so within a single biome you see:
- Lush patches reading more saturated green
- Drier patches reading yellower / warmer
- Smooth gradient across biome borders (already there via Voronoi blend + per-biome `GrassTintBase`)

This is the **within-biome** variation. Across-biome variation is already handled by the 4-weight Voronoi blend of `GrassTintBase`.

## What we have today

From [`GrassPlacementController.cs`](../../Assets/Scripts/Planet/Grass/GrassPlacementController.cs):

- Placement compute binds per-chunk `_BiomeIds` + `_BiomeWeights` textures (4-biome top-K).
- It binds `_BiomeGrassParams` — per-biome buffer with grass density/height/etc.
- Compute writes `blade.Color` into the instance buffer; shader reads it as `blade.Color.rgb * tint * brightness`.

So the missing piece is: a per-blade-root moisture signal that modulates the color before it lands in `blade.Color`.

## The data flow question — three options

| Option | What | Cost | Trade-off |
|--------|------|------|-----------|
| **A. Approximate via biome blend** | Each biome gets `GrassTintDry` + `GrassTintLush`; per-biome blend within the 4-weight mix gives apparent moisture variation. Within a single biome the color is uniform. | Cheapest — no new data path | "Within-biome variation" doesn't actually exist; only across-biome. Not what we asked for. |
| **B. Per-chunk climate texture** | Bake a small `RG16` climate texture per chunk at gen time (R=temperature01, G=moisture01). Placement compute samples it at blade root. | ~8 KB per chunk (64×64 RG16); ~16 MB for 2000 active chunks. New bake job alongside BiomeMapBaker. | Real per-blade climate sample. Texture memory + a small new bake job. |
| **C. Inline compute** | Re-derive moisture in the placement compute from latitude + altitude using a bound latitude LUT. Matches C# `MoistureProvider`. | One extra LUT bound to compute (~1 KB). No per-chunk texture. | Duplicates climate logic in HLSL — coupling risk (C# climate model changes → HLSL must follow). |

**My lean: B (per-chunk climate texture).** Reasons:
- Real moisture per blade, not approximation
- Doesn't duplicate climate math in HLSL (the C# climate provider runs once per texel at bake; HLSL just samples)
- Memory cost is modest and bounded
- Sets up well for future consumers (rock placement, flower scatter — also want climate)
- The chunk-bake path already exists (BiomeMapBaker) so adding a second texture per chunk is a known pattern

## DTO design (the pattern we just committed to)

The grass placement controller currently reads from `IGrassQualitySettings` and indirectly from biome SOs via the params buffer. New DTOs needed:

```csharp
// Snapshot per chunk — replaces the implicit "compute reads per-chunk textures" 
// pattern with an explicit "consumer requests its climate inputs" contract.
public readonly struct GrassPlacementClimateBinding
{
    public readonly Texture2D ChunkClimateTexture;  // RG16, R=temp01, G=moisture01
    public readonly int      Resolution;            // matches BiomeMapResolution (64)
}

// Snapshot of per-biome grass tint config (replaces direct reads of BiomeDefinition fields).
public readonly struct GrassBiomeTintConfig
{
    public readonly Color TintBase;       // existing GrassTintBase
    public readonly Color TintDryShift;   // multiplier applied as moisture → 0
    public readonly Color TintLushShift;  // multiplier applied as moisture → 1
}

// Composition root — ONE place that knows BiomeDefinition layout.
static GrassBiomeTintConfig BuildTintConfig(BiomeDefinition src) => new(
    src.GrassTintBase,
    src.GrassTintDryShift,   // NEW field on BiomeDefinition
    src.GrassTintLushShift); // NEW field on BiomeDefinition
```

`BiomeDefinition` gets two new optional fields (`GrassTintDryShift`, `GrassTintLushShift`) defaulting to `Color.white` (no shift) so the change is backward compatible until per-biome shifts are authored.

The placement controller signature becomes:

```csharp
void RegisterChunk(PlanetChunk chunk, GrassPlacementClimateBinding climate);

// Builds the per-biome params buffer from DTOs, NOT directly from BiomeDefinition reads.
void BuildGrassParamsBuffer(ReadOnlySpan<GrassBiomeTintConfig> tints, ...);
```

## Chunk-bake addition

Where `BiomeMapBaker` currently emits `BiomeIdsTexture` + `BiomeWeightsTexture`, it adds a third texture: `ClimateTexture` (RG16, same 64×64 resolution).

Per texel:
- Sample `IClimateProvider.Evaluate(chunkPoint, elevation)` (already used by the biome bake)
- Pack `Temperature01` into R, `Moisture01` into G

Shared work with the biome bake — we already evaluate climate per texel to do biome assignment. We just write two extra channels.

## Shader integration

In `BiomeGrassPlace.compute` (existing kernel), per blade-root:

```hlsl
float2 chunkUv = blade.LocalUv;
float4 climate = _ChunkClimate.SampleLevel(sampler_ChunkClimate, chunkUv, 0);
float moisture = climate.g;

// Weighted-blend the per-biome dry/lush shifts using the existing 4 biome weights.
float3 dryShift  = w0 * tintDry[id0] + w1 * tintDry[id1] + ... ;
float3 lushShift = w0 * tintLush[id0] + w1 * tintLush[id1] + ... ;
float3 climateShift = lerp(dryShift, lushShift, moisture);

blade.Color.rgb = baseColor * climateShift;
```

Per-blade noise from the existing `tintHash` path stays — adds the high-frequency variation on top of the climate-driven low-frequency drift.

## Where the DTO pattern pays off

If we ever:
- Rename `GrassTintBase` → `BaseColor` on `BiomeDefinition` (1-line change in `BuildTintConfig`, zero callsite touch)
- Add per-biome `GrassMoistureSensitivity` (1-line addition to DTO + builder + 1 shader uniform)
- Swap from per-chunk climate texture to per-vertex climate from mesh UV2 (only `GrassPlacementClimateBinding` builder changes; consumer is unaffected)

…all of these are cheap. Today, that work would touch every direct reader.

## Performance budget

Per blade root:
- 1 extra texture sample (`_ChunkClimate`)
- 4 extra float3 muladds (dry-shift weighted blend)
- 4 extra float3 muladds (lush-shift weighted blend)
- 1 lerp + 1 multiply

Negligible compared to the existing biome-weight blend cost. Memory cost: ~16 MB for 2000 active chunks (the RG16 climate texture set).

## Console commands (for tuning)

Following the climate.* pattern from slice 1b:

```text
grass.dry-shift <biomeName> <r> <g> <b>      # set per-biome dry tint shift
grass.lush-shift <biomeName> <r> <g> <b>     # set per-biome lush tint shift
grass.preview-moisture                       # debug mode: paint blades by sampled moisture
```

## Diagnostics

New debug mode `BiomeGrassClimateShift` paints each blade with its computed `climateShift` color directly (no base color). Lets you visually verify the moisture signal is reaching grass before tuning per-biome values.

## Slice scope

1. Add `GrassTintDryShift` + `GrassTintLushShift` fields to `BiomeDefinition` (default `Color.white`).
2. Add `GrassBiomeTintConfig` DTO + `BuildTintConfig(BiomeDefinition)` builder.
3. Add `GrassPlacementClimateBinding` DTO.
4. Add `ClimateTexture` (RG16) to `PlanetChunk` + bake in `BiomeMapBaker` (write moisture + temperature alongside existing biome map writes).
5. Update placement compute to sample climate texture + apply dry/lush shift weighted blend.
6. Add `grass.dry-shift` / `grass.lush-shift` console commands.
7. Add `BiomeGrassClimateShift` debug mode.

Estimated effort: 1-2 sessions including F10 validation.

## Open question for Bryan

**Confirm option B (per-chunk climate texture) over A (biome-only) or C (inline compute)?** If B, I'll proceed; if A or C, I'll redraft the slice scope.

The other small call: **default values for `GrassTintDryShift` / `GrassTintLushShift` on biomes.** I'd ship them as `Color.white` (no shift), so the immediate visual change is zero until you author per-biome values. That gives you a stable baseline to iterate from. Sound right?
