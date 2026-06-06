# Biome / climate / vegetation overhaul — informed by Planet Architect

**Date:** 2026-06-05 (decisions locked 2026-06-06)
**Status:** Approved for implementation.
**Source of ideas:** [docs/research/2026-06-05-planet-architect-biomes-vegetation.md](../research/2026-06-05-planet-architect-biomes-vegetation.md)
**Related:** [docs/design/2026-05-31-biome-textures.md](2026-05-31-biome-textures.md) (Phase B — historical K=2 draft; shipped implementation is K=4 below)

---

## 0. Decisions locked 2026-06-06

After review on 2026-06-06, the following are locked:

### 0.1 Top-K = 4 (already shipped — confirmed K=4, not K=2 as the Phase B draft suggested)

Code verification 2026-06-06: Phase B as shipped uses **K=4**, not K=2. From [`PlanetChunk.cs`](../../Assets/Scripts/Planet/Surface/PlanetChunk.cs): `public const int TopK = 4;`. Three per-chunk textures back this:

```
BiomeBlendedColorTexture (RGBA8)  — flat-shaded fallback color baked from LUT
BiomeIdsTexture          (RGBA8)  — R=id1, G=id2, B=id3, A=id4 (4 biome slot indices)
BiomeWeightsTexture      (RGBA8)  — R=w1, G=w2, B=w3, A=w4 (matching weights, slot-for-slot)
```

Both ID + weights textures are POINT filter (no bilinear) to avoid invalid intermediate biome ids. The shader does a manual 4-corner bilinear blend on the weighted sample.

**Why K=4 is the right place to stay (not down to K=3, not up to K=5):**

- **The 4 channels of RGBA8 are a natural fit.** No packing math, no leftover channels. Going to K=3 would waste a channel; going to K=5 would require a second texture per slot.
- **Solves the "thin-band biome" problem.** When the climate gradient crosses A → B → C where B is a thin stripe, K=2 (the draft's intent) would let B dominate visually in the middle. K=4 blends 4 biomes everywhere, so the thin B is **visible but diluted** — looks like a natural transitional band, not a jarring intermediate biome.
- **3-way and 4-way junction points** blend correctly. Even rare 4-biome corners (where 4 biomes meet at a point) work natively.
- **No refactor cost** — already shipped.

**Cost (already paid):** 4 albedo + 4 normal samples per fragment via triplanar = real but acceptable on modern hardware.

### 0.2 Seed cleanup (5 iterations of "majority-of-8-neighbors") is THE fix for thin-stripe biomes

The cleanup pass described in §3.4.5 is now load-bearing for transition quality — not just speckle cleanup. Concretely: after each Voronoi seed is assigned its biome (by nearest `climateTargets` match), iterate 5 times: any seed whose 8 nearest neighbors include 6+ of biome X gets reassigned to X. **Result:** isolated B-pockets between A and C regions are reabsorbed into whichever majority wins. The climate path still crosses B's territory, but no seeds remain *assigned* to B in the thin band — the transition becomes a direct A → C blend.

K=4 + 5-iteration cleanup address both transition junctions and isolated thin-stripe seeds.

### 0.3 Generation is one-time, not per-frame

All the expensive machinery (Voronoi seed scatter, climate evaluation per seed, 5-iteration cleanup, per-chunk biome map bake, top-K nearest-seed lookups) runs **once at planet generation**, not per frame. There is no per-frame `EvaluateBiome` cost in the steady state — the shader samples the baked per-chunk RGBA8 map, which is a single texture fetch.

This means **we should not constrain the design to fit a frame budget.** Expensive but correct algorithms are fine; we just need to parallelize them (Burst jobs, compute shaders, async/Awaitable continuations) so the player isn't waiting 30 seconds at "Generating planet…". A 5-second generation that produces beautiful biome boundaries is better than a 1-second generation that ships visible artifacts.

### 0.4 Weight filtering constraint

Do not box-filter biome IDs or weight channels independently. Neighboring texels may use different ID-to-channel assignments, so filtering weights without their paired IDs is invalid. The shipped shader's manual four-corner weighted blend is the safe interpolation path.

---

## 1. Purpose

Adopt four insights from the Planet Architect analysis into our biome / climate / vegetation pipeline. The Phase B doc (biome textures) handles *how biome ID + textures reach the GPU per chunk*. This doc handles *what produces those biome IDs in the first place, and how the terrain looks once they're there*.

Concretely, this doc covers four adoptions, in increasing order of structural impact:

1. **`BiomeOffset: Vector3` per biome** — kills cross-biome lattice alignment in vegetation noise sampling. Trivial.
2. **Snow / slope / coast layered terrain overrides** — smoothstep-cutoff layers in the terrain shader. Biggest visual win; integrates with Phase B's shader.
3. **Per-species 2D Gaussian climate niche for trees** — restructures tree placement scoring. Affects the (not-yet-shipped) tree system; design-now-implement-later.
4. **Voronoi + domain-warp biome assignment in climate space** — replaces our current direct climate→biome lookup. Closes the [chunk-biome-seam](../../) issue by construction, gives organic-meandering boundaries.

Plate tectonics (mountain ranges via plate sim) is **out of scope** — own research pass later.

---

## 2. Relationship to Phase B

Phase B = **plumbing**. Phase B builds:
- Per-chunk `Texture2D` biome map (R = primary biome id, G = secondary biome id, B = blend weight)
- Per-planet `Texture2DArray` of biome textures (albedo + normal + ARM per slice)
- Per-chunk `SurfaceStateTexture` (RGBA8) reserved for Phase E (paving / scorching / snow accumulation)
- Bake pipeline (Burst job) and terrain shader path (triplanar sampling)

This overhaul = **content**. It changes:
- How **`IBiomeProvider.EvaluateBiome`** produces its result (#4)
- What the terrain shader does with the result (#2 — adds three override layers on top of biome blend)
- What the biome SO carries (#1, #3 — new fields)

**Net effect on Phase B:** mostly additive. Phase B's bake pipeline keeps working — `BiomeMapBakeJob` still calls `IBiomeProvider.EvaluateBiome` and writes (primary, secondary, blend) into the texture. The provider's internals change. The texture format does not.

**Phase B's terrain shader gains layers (#2).** This is the only place Phase B and this doc materially overlap. We have two options:

- **Option A — fold into Phase B**: extend Phase B's shader spec to include the three overrides upfront. Slightly delays Phase B but ships them together.
- **Option B — Phase B.1 follow-up**: Phase B ships as written, then a small Phase B.1 slice adds the overrides. Faster to first useful chunk.

**Recommendation: Option B.** Phase B is already substantial; getting it to first-light without dragging in override design is cheaper. The overrides require new artist input (slope texture, snow texture, coast texture, normal maps) which is itself a non-zero cost. Ship Phase B with biome textures + flat composition, then add overrides as Phase B.1 with the new art.

---

## 3. The four adoptions

### 3.1 — `BiomeOffset: Vector3` per biome

**What it is:** A 3D random offset added to noise sample positions when computing vegetation placement (and later, when computing per-biome detail textures). Each biome carries its own `BiomeOffset`. Same noise function, different offsets per biome → vegetation patterns look characteristic per biome.

**Why it matters:** Without this, every biome samples vegetation placement noise at the same world position → boundaries between adjacent biomes show **identical patch alignment** even when the vegetation density differs. The eye reads "same template, different colors." With per-biome offset, each biome has its own characteristic patch positions, so a forest in Continental looks geographically distinct from a forest in Temperate Rainforest even where both have ~50% coverage.

**Change:**

```csharp
// BiomeDefinition.cs
[Tooltip("Per-biome noise sample offset. Decorrelates vegetation patterns across biome boundaries.")]
public Vector3 BiomeOffset = Vector3.zero;
```

**Where it's consumed:** Anywhere vegetation placement or detail noise is sampled per chunk / per biome. Phase C grass placement code will read it:

```csharp
// Placement noise lookup, was:
float density = SamplePerlin(worldPos * scale);
// becomes:
float density = SamplePerlin(worldPos * scale + biome.BiomeOffset);
```

**Initial values:** Hand-assigned in the inspector per biome (e.g. `(9825, 2240, 9160)` for Continental, `(8402, 1585, 2593)` for Tropical) — matching Planet Architect's convention. Could also derive from biome name hash, but explicit values let the artist tune for specific feels.

**Risk:** None — additive field, defaults to zero (no change from current behavior). Safe to ship before downstream consumers exist.

**Sequence position:** Independent. Can ship any time.

---

### 3.2 — Snow / slope / coast layered terrain overrides

**What it is:** Three independent texture layers that override the biome blend in the terrain shader:

| Layer | Trigger | Smoothstep range |
| ----- | ------- | ---------------- |
| Coast | low height above sea | `_beachStartCutoff`, `_beachEndCutoff` |
| Slope | high surface slope | `_minSlope`, `_maxSlope` |
| Snow | low local temperature | `_SnowTemp ± fadeWidth` |

Each layer has its own albedo + normal + tiling. Composited on top of the biome blend in order: coast → slope → snow (so a steep snowy cliff reads as snow, not as rock-then-snow).

**Why it matters:** Real-world geography has overlaid features that don't respect biome borders. Beaches form anywhere land meets water. Cliffs expose rock regardless of climate. Snow caps mountains regardless of biome. Without these layers, our terrain looks like *colored regions* — recognizably algorithmic. With them, it looks like *geography*.

This is the single most underrated visual upgrade we can ship. Planet Architect's defaults: `_beachStartCutoff: 0.01`, `_beachEndCutoff: 0.015`, `_minSlope: 0.6`, `_maxSlope: 0.7`, `_SnowTemp: -7.5`.

**Change — shader uniforms (added to Phase B's terrain shader, post-Phase B as B.1):**

```hlsl
// Coast
[NoScaleOffset] _CoastAlbedo     ("Coast albedo", 2D)     = "white" {}
[NoScaleOffset] _CoastNormal     ("Coast normal", 2D)     = "bump" {}
[NoScaleOffset] _CoastARM        ("Coast ARM", 2D)        = "white" {}
                _CoastTiling     ("Coast tiling", Float)  = 0.06
                _BeachStartCutoff("Beach start (height above sea)", Float) = 0.01
                _BeachEndCutoff  ("Beach end (height above sea)",   Float) = 0.015

// Slope
[NoScaleOffset] _SlopeAlbedo     ("Slope albedo", 2D)     = "white" {}
[NoScaleOffset] _SlopeNormal     ("Slope normal", 2D)     = "bump" {}
[NoScaleOffset] _SlopeARM        ("Slope ARM", 2D)        = "white" {}
                _SlopeTiling     ("Slope tiling", Float)  = 0.025
                _MinSlope        ("Slope min (no override)",  Float) = 0.6
                _MaxSlope        ("Slope max (full override)", Float) = 0.7

// Snow
[NoScaleOffset] _SnowAlbedo      ("Snow albedo", 2D)      = "white" {}
[NoScaleOffset] _SnowNormal      ("Snow normal", 2D)      = "bump" {}
[NoScaleOffset] _SnowARM         ("Snow ARM", 2D)         = "white" {}
                _SnowTiling      ("Snow tiling", Float)   = 0.08
                _SnowTemp        ("Snow temperature (°C)", Float) = -7.5
                _SnowFadeWidth   ("Snow fade width (°C)",  Float) = 2.0
```

**Change — fragment composition (after Phase B's biome blend):**

```hlsl
// Phase B output:
float4 albedo  = biomeBlendAlbedo;
float3 normalT = biomeBlendNormal;
float3 arm     = biomeBlendARM;

// Inputs we need:
// - heightAboveSea  = elevation - waterLevel  (already known per-vertex; pass via interpolant)
// - surfaceSlope    = 1 - dot(worldNormal, radialNormal)  (compute in vertex or fragment)
// - localTemperature = (sample from biome map? or per-vertex interpolant?) — see §3.2.1

// Coast
float coastT = 1.0 - smoothstep(_BeachStartCutoff, _BeachEndCutoff, heightAboveSea);
// (coast wins below _BeachStartCutoff, fades out by _BeachEndCutoff)

// Slope
float slopeT = smoothstep(_MinSlope, _MaxSlope, surfaceSlope);

// Snow
float snowT = 1.0 - smoothstep(_SnowTemp, _SnowTemp + _SnowFadeWidth, localTemperature);

// Triplanar sample each override
float4 coastA   = TriplanarSample(_CoastAlbedo, worldPos, normal, _CoastTiling);
float3 coastN   = TriplanarSampleNormal(_CoastNormal, worldPos, normal, _CoastTiling);
float3 coastARM = TriplanarSample(_CoastARM, worldPos, normal, _CoastTiling).rgb;
// ... same for slope and snow

// Composite — coast first, then slope, then snow (snow wins on steep snowy peaks)
albedo  = lerp(albedo,  coastA,   coastT);
normalT = lerp(normalT, coastN,   coastT);
arm     = lerp(arm,     coastARM, coastT);

albedo  = lerp(albedo,  slopeA,   slopeT);
normalT = lerp(normalT, slopeN,   slopeT);
arm     = lerp(arm,     slopeARM, slopeT);

albedo  = lerp(albedo,  snowA,    snowT);
normalT = lerp(normalT, snowN,    snowT);
arm     = lerp(arm,     snowARM,  snowT);
```

#### 3.2.1 Where local temperature comes from in the shader

Three options:

- **Per-vertex interpolant** — `ColorGenerator` already computes per-vertex temperature; pass it through the vertex shader to the fragment as a `float` interpolant. **Cheap, simple, no extra texture.**
- **Sample from biome map G channel** — Phase B currently uses G for secondary biome id. Would need to add a fourth channel or a second texture.
- **Compute in shader from latitude + altitude** — requires `_PlanetCenter`, `_PlanetTilt`, `_BaseTemperatureCurve`. More complex but the most flexible (and consistent with #4 below).

**Recommendation: per-vertex interpolant** for B.1 — cheapest, plumbing path already exists.

**Risk:** Three new texture pairs need to be authored or sourced. Without art, the layers default to "white" textures which would visibly wash out terrain. So this slice is **gated on having coast / slope / snow art assets**.

**Sequence position:** Phase B.1, after Phase B ships the biome blend.

---

### 3.3 — Per-species 2D Gaussian climate niche for trees

**What it is:** Each tree species carries a Gaussian climate niche over (temperature, precipitation):

```csharp
[CreateAssetMenu(menuName = "ProceduralPlanets/Vegetation/Tree Species")]
public sealed class TreeSpecies : ScriptableObject
{
    [Header("Climate niche")]
    public float BaseWeight = 1.0f;           // overall preference multiplier
    public float TargetTemperature = 15f;     // peak of Gaussian
    public float TemperatureVariance = 8f;    // σ
    public float TargetPrecipitation = 0.5f;
    public float PrecipitationVariance = 0.2f;

    [Header("Visuals")]
    public GameObject[] Models;               // 2-3 variants, picked randomly
    public float MinScale = 0.8f;
    public float MaxScale = 1.2f;
    public float VerticalOffset = 0f;
}
```

**Placement scoring** (pseudocode):

```csharp
// For each candidate tree placement at world point P with climate (T, P):
float ScoreSpecies(TreeSpecies s, float t, float p)
{
    float tDelta = (t - s.TargetTemperature) / s.TemperatureVariance;
    float pDelta = (p - s.TargetPrecipitation) / s.PrecipitationVariance;
    return s.BaseWeight * exp(-(tDelta*tDelta + pDelta*pDelta));
}

// Pick species by weighted random among biome's eligible species list:
TreeSpecies pick = WeightedPick(biome.TreeSpecies, s => ScoreSpecies(s, t, p));
GameObject model = pick.Models[Random.Range(0, pick.Models.Length)];
```

**Why it matters:** The "biome → species list" model gives **hard species transitions at biome boundaries**: cross from Continental to Temperate Rainforest and the trees abruptly switch from Pine to Oak. The Gaussian niche gives **smooth species transitions**: species that thrive in both climates blend gradually; species at the climate edge fade out before their biome border. Result: forests look continuous across biomes, with species composition drifting smoothly.

Also enables **mixed forests** within a single biome. Tropical Rainforest in Planet Architect lists 6 species; their relative scores at each placement point determine which species dominates locally. Hot-wet-low areas get one mix, cooler-higher elevation areas get another, all inside the same biome.

**Change — biome SO addition:**

```csharp
[Header("Vegetation")]
public TreeSpecies[] TreeSpecies;      // eligibility list — Gaussian niche then ranks them
public float MinTreeTemperature = -30f; // hard gate (e.g. Polar = no trees regardless of niche)
```

**Risk:** Tree system isn't shipped yet. This is **design now, implement when trees ship**. No code changes for adoption today; the design doc captures the model so the eventual tree implementation uses it.

**Sequence position:** Design locked now (as part of this doc). Implementation deferred to the tree-system slice.

---

### 3.4 — Voronoi + domain-warp biome assignment in climate space

**What it is:** Replace the current direct climate→biome lookup with a **Voronoi-in-climate-space** assignment that adds **domain warping** for organic borders. The current `IBiomeProvider.EvaluateBiome(point, elevation)` is replaced internally; its signature and `BiomeResult` shape stay the same so Phase B's bake pipeline keeps working.

**Algorithm:**

1. **Bake-time (planet generation):**
   - Scatter ~2000 Voronoi seed points on the unit sphere using Fibonacci spiral (deterministic, well-distributed) or Poisson disk (more random).
   - For each seed, compute its (Temperature, Precipitation) from the climate model (§3.4.1).
   - Find the nearest **biome `ClimateTargets`** under a weighted L2 metric (temperature weight ≈ 4.26, matching Planet Architect's `biomeTemperatureWeight`).
   - Assign that biome to the seed. Store as `(seedPosition, biomeId)` array.
   - Build a spatial accelerator (KD-tree on sphere, or face-grid hash) for fast nearest-seed lookup.
   - Run **5 iterations of speckle-cleanup**: for each seed, if 6+ of 8 nearest neighbors share a different biome, switch to majority. Removes single-cell artifacts.

2. **Runtime (per `EvaluateBiome` call):**
   - Domain-warp the lookup position: `warpedPoint = point + WarpField(point, scale, octaves) * warpStrength`.
   - Find the nearest Voronoi seed to `warpedPoint`. That seed's biome = `PrimaryBiome`.
   - Find the second-nearest seed for `SecondaryBiome` and `BlendWeight = 1 - dist1/(dist1+dist2)` (inverse-distance blend).

#### 3.4.1 Climate model (new)

Extend the current climate signals into **latitude + altitude + noise**. Temperature already uses latitude plus noise; slice 1b adds altitude lapse and artist-authored latitude curves. Moisture currently uses noise only and gains latitude bands.

```csharp
float Temperature01(Vector3 pointOnUnitSphere, float elevation, float waterLevel)
{
    float latitude = Mathf.Abs(pointOnUnitSphere.y);                   // 0 at equator, 1 at poles
    float latBase = TemperatureLatitudeCurve(latitude);                // normalized 0..1
    float landHeight = Mathf.Max(0f, elevation - waterLevel);
    float altDrop = landHeight * AltitudeTemperatureDrop;
    float noise = SampleCenteredNoise(pointOnUnitSphere, TemperatureNoise) * TemperatureNoiseStrength;
    return Mathf.Clamp01(latBase - altDrop + noise);
}

float Moisture01(Vector3 pointOnUnitSphere)
{
    float latitude = Mathf.Abs(pointOnUnitSphere.y);
    float latBase = MoistureLatitudeCurve(latitude);     // wet/dry atmospheric bands
    float noise = SampleCenteredNoise(pointOnUnitSphere, MoistureNoise) * MoistureNoiseStrength;
    return Mathf.Clamp01(latBase + noise);
}
```

Notes:
- `TemperatureLatitudeCurve` and `MoistureLatitudeCurve` are `AnimationCurve` fields on `BiomeSettings`.
- The canonical runtime contract is normalized temperature/moisture in `[0,1]`; physical Celsius values are deferred until planet scale and lapse calibration are explicit.
- `AltitudeTemperatureDrop` must be calibrated against this project's normalized elevation domain. Do not copy Planet Architect's constant directly.
- `IClimateProvider.Evaluate(pointOnUnitSphere, elevation)` returns one `ClimateSample`, so temperature, moisture, and elevation stay coherent through biome lookup and future placement systems.
- Curves should be baked to small LUTs before moving climate generation into Burst or compute work; do not evaluate `AnimationCurve` from worker jobs.

#### 3.4.2 Voronoi seed distribution

**Fibonacci spiral** is recommended:

```csharp
Vector3[] FibonacciSpherePoints(int n)
{
    var points = new Vector3[n];
    float phi = Mathf.PI * (3f - Mathf.Sqrt(5f)); // golden angle
    for (int i = 0; i < n; i++)
    {
        float y = 1f - (i / (float)(n - 1)) * 2f;     // -1 to 1
        float radius = Mathf.Sqrt(1f - y * y);
        float theta = phi * i;
        points[i] = new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius);
    }
    return points;
}
```

Deterministic, no clumps, no empty patches. Add a small per-point random jitter (Planet Architect's `randomizeBiomeVoronoiAmount: 0.77`) seeded by `ISeedProvider.GetSeedForSystem("BiomeVoronoi")` so each world has different seed locations.

#### 3.4.3 Domain warp

```csharp
Vector3 DomainWarp(Vector3 point)
{
    Vector3 warp = new Vector3(
        SamplePerlin(point * WarpScale + new Vector3(0, 0, 0), WarpOctaves),
        SamplePerlin(point * WarpScale + new Vector3(100, 0, 0), WarpOctaves),
        SamplePerlin(point * WarpScale + new Vector3(200, 0, 0), WarpOctaves));
    return point + warp * WarpStrength;
}
```

Planet Architect's defaults: `biomeDomainWarpStrength: 0.103`, `biomeDomainWarpScale: 0.075`, `biomeDomainWarpOctaves: 4`, `biomeDomainWarpPersistence: 0.418`, `biomeDomainWarpLacunarity: 2.73`. Good starting point.

#### 3.4.4 Nearest-seed lookup

For 2000 seeds queried at every chunk vertex (~16K vertices per chunk × N chunks), brute force is too slow. Options:

- **Spherical KD-tree** — moderate complexity, fast queries. Good fit.
- **Face-grid hash** — bin seeds by cube-face cell, lookup checks the local cell plus 8 neighbors. Simpler than KD-tree, fine for our seed count.
- **GPU-resident texture lookup** — bake the entire biome assignment to a high-res cubemap once at world gen, then `EvaluateBiome` becomes a texture lookup. Fastest at runtime, but the cubemap resolution caps the spatial detail.

**Recommendation: face-grid hash** for the initial implementation. Simple, fast enough, easy to validate. Move to GPU cubemap if profiling demands it.

#### 3.4.5 Speckle cleanup — also the thin-stripe fix (see §0.2)

5 iterations of "majority of N nearest neighbors":

```csharp
for (int iter = 0; iter < 5; iter++)
{
    for (int i = 0; i < seeds.Length; i++)
    {
        var neighbors = NearestN(seeds[i], 8);
        int myBiome = seeds[i].BiomeId;
        var counts = new Dictionary<int, int>();
        foreach (var n in neighbors)
            counts[n.BiomeId] = counts.GetValueOrDefault(n.BiomeId) + 1;
        int maxBiome = counts.OrderByDescending(kv => kv.Value).First().Key;
        if (counts[maxBiome] >= 6) seeds[i].BiomeId = maxBiome;
    }
}
```

Threshold of 6/8 means we only flip when there's strong majority — avoids oscillation.

**Why this matters (whole approach):**
- **Closes the chunk-biome-seam issue by construction.** The Voronoi assignment is global; every point looks up the same seed array regardless of which chunk it's in. No kernel boundary. No seam.
- **Organic boundaries.** Domain warp gives meandering, fractal-edged biome transitions that look like real geography. Pure Voronoi has visibly straight cell edges; warped Voronoi looks like coastlines.
- **Climate-coherent transitions.** Biomes adjacent in (T, P) space are placed adjacent geographically. You go through Steppe between Desert and Continental, not from Desert straight to Tropical Rainforest. Looks natural.
- **Latitude + altitude climate** gives Earth-like belts and mountain snow caps without manual tuning per planet.

**Risk:** This is the largest change in this doc. Replaces the core biome assignment logic. Phase B is unaffected at the *interface* level (still consumes `IBiomeProvider.EvaluateBiome → BiomeResult`) but Phase B's bake job will see *different output* than today, which may produce visually different chunks from existing seed values. Worth gating with a feature flag for direct visual comparison during development.

**Sequence position:** After Phase B ships and stabilizes. Best to land Phase B with current biome assignment first, then swap the provider internals.

---

## 4. Recommended sequencing

**Updated 2026-06-06:** Phase B is **already shipped** (K=4, three per-chunk textures, Texture2DArray triplanar sampling all live). So the sequencing starts at #1, not at Phase B.

```
Step 1a — #1 (BiomeOffset) — ~30 min, free field add. No consumers yet, but landed for grass placement.
   │
Step 1b — Climate model overhaul (§3.4.1)
   ├─ IClimateProvider/ClimateSample contract (landed 2026-06-06)
   ├─ TemperatureProvider: altitude lapse + authored latitude curve (implemented 2026-06-06)
   ├─ MoistureProvider: latitude bands + noise with legacy compatibility blend (implemented 2026-06-06)
   ├─ Curves baked to immutable LUTs before worker generation (implemented 2026-06-06)
   ├─ Console tuning commands and presets (implemented 2026-06-06)
   ├─ StrongBands + Earthlike F10 climate signal validation passed 2026-06-06
   └─ Foundation for #4 (Voronoi assignment uses these inputs)
   │
Step 1c — #4 (Voronoi assignment + domain warp + 5-iter cleanup)
   ├─ Fibonacci spiral seed scatter (~2000 seeds), deterministic + jittered (implemented 2026-06-06)
   ├─ Per-seed (T, P) lookup → nearest registry-grid climate target (implemented 2026-06-06)
   ├─ 5-iteration majority-of-neighbors cleanup (§0.2 — thin-stripe fix) (implemented 2026-06-06)
   ├─ Tangent-projected spherical domain warp + exact KD-tree lookup (implemented 2026-06-06)
   ├─ Fast 512x512x6 map-bake atlas with matching inverse face coordinates and edge-snapped seams (implemented 2026-06-06)
   ├─ Land behind a feature flag for visual A/B vs current IBiomeProvider (implemented 2026-06-06)
   ├─ Orbit + surface Unity/F10 validation passed 2026-06-06
   └─ Voronoi promoted to default 2026-06-06; direct lookup retained temporarily for regression A/B
   │
Step 1d — Climate-aware frozen water
   ├─ Sample trusted slice 1b temperature across connected water components
   ├─ Freeze inland lakes coherently; retain local polar control for large oceans
   ├─ Store static freeze factor in water vertex-color alpha
   ├─ Suppress liquid motion/foam/wakes and blend to an authored ice response
   └─ Add water-temperature/freeze diagnostics and F10 validation
   │
Step 3 (texture/look work — from Bryan's 2026-06-06 plan)
   ├─ Multi-variant Synty texture blend per biome
   ├─ #2 (snow/slope/coast overrides) — needs new art assets
   ├─ Stylization pass (color tint, posterization)
   └─ URP volume profile tuning
   │
Step 5 (props — trees, rocks, bushes)
   └─ #3 (per-species Gaussian niche for trees) — needs climate model from 1b
```

Step 2 (grass on/off toggle) and Step 4 (grass tuning) from Bryan's 5-step plan slot in between Step 1 (model) and Step 3 (textures), and between Step 3 and Step 5, respectively.

---

## 5. Open questions for review

1. **Snow / slope / coast art assets.** §3.2 lives or dies on having decent art. Are we OK with this gating Phase B.1, or should we use placeholder textures (Substance / public domain rock-and-snow) to ship the shader path and swap art later?

2. **Domain warp tuning across planet scales.** Planet Architect uses `warpScale: 0.075` for what's probably a similar planet scale to ours (10 km radius). Our planet is `1f` unit sphere internally, so we need to verify the warp parameters give the right *world-scale* effect. May need experimentation rather than copying defaults verbatim.

3. **Feature flag vs hard cutover for #4.** Voronoi assignment will produce *different biome IDs at the same seed* than the current direct lookup. This means saved worlds (if any persistence exists) would visually change after the migration. For dev that's fine, for end-users it's not. Worth a thought: do we ever cap the existing biome behavior so worlds are stable across upgrades?

4. **Climate noise vs domain warp — same noise field or different?** Planet Architect uses separate noise fields for climate-derivation and biome-boundary domain warp. Should be confirmed.

5. **`AltitudeTemperatureDropConstant` calibration.** Planet Architect's 75 is for their world scale. Our world scale and elevation range differ. Will need to recalibrate so high mountains genuinely become cold (snow caps) rather than only being a few degrees cooler.

6. **Phase C grass system interaction.** Once Phase C ships, grass density per chunk will read from the biome map. The Gaussian niche idea (#3) applies to trees but not directly to grass (Planet Architect uses single-species-per-biome for grass). Should grass have an analogous niche, or stay simple?

---

## 6. What's NOT in this doc

- **Plate tectonics simulation for mountain placement.** Worth its own research pass. Deferred.
- **Vegetation LOD tiles** (Planet Architect's `vegetationTileLevel: 5`). Phase C+ concern; not biome-model territory.
- **Layered detail texturing** (macro variation noise to break up triplanar repeat). Lives in Phase B polish.
- **`BiomeRegistryEditor` UX updates** for new fields. Track separately; cosmetic.
- **Snow accumulation as a runtime mechanic** (Phase E). The static `snowT` mask from #3.2 is purely visual; runtime snowdrifts go in the `SurfaceStateTexture` (Phase B's reserved channel).

---

## 7. Recommended next steps after approval

1. Capture a baseline biome F10 before changing climate output.
2. Implement slice 1b settings, normalized altitude lapse, moisture latitude bands, and LUT baking behind current defaults.
3. Add climate diagnostic modes for temperature, moisture, and altitude contribution, then validate each signal independently.
4. Implement slice 1c Voronoi assignment behind a feature flag and compare against the baseline.
5. Spec the coast / slope / snow overlay art bill before Phase B.1.
6. Carry the Gaussian climate niche into the future tree-system design.

---

## 8. Pointers

- Research source: [`docs/research/2026-06-05-planet-architect-biomes-vegetation.md`](../research/2026-06-05-planet-architect-biomes-vegetation.md)
- External project: `D:\Planet_Architect_v0.1.5_Windows\Source\ExportedProject\Assets\` (see [`reference_planet_architect.md`](../../) memory entry)
- Current biome system: [`Assets/Scripts/Planet/Biomes/`](../../Assets/Scripts/Planet/Biomes/), [`Assets/Scripts/Planet/ColorGenerator.cs`](../../Assets/Scripts/Planet/ColorGenerator.cs), [`Assets/Scripts/Core/Data/BiomeTypes.cs`](../../Assets/Scripts/Core/Data/BiomeTypes.cs)
- Phase B substrate: [`docs/design/2026-05-31-biome-textures.md`](2026-05-31-biome-textures.md)
- Chunk-seam motivation for #4: [`memory/project_chunk_biome_seam.md`](../../)
- Frozen-water follow-up: [`2026-06-06-climate-frozen-water.md`](2026-06-06-climate-frozen-water.md)
