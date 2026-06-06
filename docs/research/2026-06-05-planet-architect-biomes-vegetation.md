# Planet Architect v0.1.5 — biome blending + vegetation analysis

**Source:** `D:\Planet_Architect_v0.1.5_Windows\Source\ExportedProject\Assets` (decompiled / IL2CPP export of a shipping game).
**Date examined:** 2026-06-05.
**Purpose:** Map their architecture so we can borrow what's genuinely better than ours.

## TL;DR — what's worth stealing

1. **Climate-space Voronoi for biome assignment** (with **domain warping**) — gives natural meandering borders instead of grid artifacts. Likely the single biggest visual win we could adopt.
2. **Per-biome `biomeOffset` noise seed** — each biome samples placement noise at a different offset, so vegetation patterns look characteristic per biome rather than identical templates.
3. **Layered terrain texturing** — 4 climate biomes + snow + slope + coast as **independent overrides** with smoothstep cutoffs. Each handles one job; combined they cover every natural transition cleanly.
4. **Per-species 2D Gaussian niche** for tree placement — temperature + precipitation each with mean + variance. Mixes species naturally where biomes overlap instead of hard "this biome has these trees" mapping.
5. **Artifact-cleanup pass** on the biome map (5 iterations of speckle removal) — turns voxel-y noise output into clean continents.

The game itself appears to be **Unity-Terrain-based per-face patches**, not a true sphere mesh. Their terrain shader and grass detail engine are mostly standard Unity URP; the smart bits are upstream in the biome / climate / placement model.

## How I figured this out

The game is shipped as IL2CPP — only 8 C# files survived export (first-person controllers + a Readme). The actual game logic lives in compiled DLLs. **Most of what's useful is recoverable from:**

- **ShaderGraph stub files** — the `.shader` bodies are dummy export stubs, but the `Properties` block lists every uniform the graph reads. This tells you exactly what the renderer consumes.
- **MonoBehaviour `.asset` files** in YAML form — these are the ScriptableObject configs. They reveal the *runtime data model* the (now compiled) code consumes.
- **Asset filenames** — naming conventions (`Acacia.asset`, `Birch.asset`, `Continental.asset`, `EarthlikeSettings.asset`) reveal the conceptual taxonomy.

You can reconstruct ~80% of the design without ever touching the DLLs.

## Architecture overview

```
┌─────────────────────────────────────────────────────────────┐
│ EarthlikeSettings (a ScriptableObject "world recipe")       │
│  - sphereRadius, chunkDimensions                            │
│  - plate tectonics params (4096 seed points, 19 plates)     │
│  - heightCurve, mountain noise, ridge params                │
│  - climate noise (temperature, precipitation, lapse rate)   │
│  - biome Voronoi params (2335 points, domain warp, cleanup) │
│  - vegetationTileLevel                                      │
└─────────────────────┬───────────────────────────────────────┘
                      │
            ┌─────────┴─────────┐
            │  Generator runs:  │
            │  1. Plate sim     │
            │  2. Heightmap     │
            │  3. Climate       │
            │  4. Biome Voronoi │
            │  5. Vegetation    │
            └─────────┬─────────┘
                      │
       ┌──────────────┼──────────────┐
       ▼              ▼              ▼
┌────────────┐ ┌──────────────┐ ┌─────────────┐
│ Biome[]    │ │ Vegetation   │ │ Unity       │
│ ScriptObj  │ │ instances    │ │ Terrain     │
│            │ │ (trees,grass)│ │ patches     │
│ - climate  │ │              │ │             │
│   targets  │ │ - placed via │ │ - per-face? │
│ - tex/norm │ │   noise +    │ │ - heightmap │
│ - veg      │ │   species    │ │ - detail    │
│   profile  │ │   niche      │ │   layers    │
└─────┬──────┘ └──────┬───────┘ └──────┬──────┘
      │               │                │
      └───────┬───────┴────────────────┘
              ▼
   ┌──────────────────────────────┐
   │ TerrainTextured shader graph │
   │ Inputs:                      │
   │   _BiomeTexture1..4 (top-K)  │
   │   _SnowTexture (override)    │
   │   _SlopeTexture (override)   │
   │   _CoastTexture (override)   │
   │ Each with cutoff ranges      │
   └──────────────────────────────┘
```

## The biome ScriptableObject model

From `Continental.asset` (also Tropical Rainforest, Polar, Desert, Savanna, Steppe, Swamp, Tundra, Temperate Rainforest — **9 biomes total**, Köppen-style):

```yaml
biomeName: Continental
id: 4
rareBiome: false
color: (0.39, 0.56, 0.20, 1)        # debug + low-LOD fallback
texture: <Texture2D>                # diffuse, tile=0.06
normalMap: <Texture2D>
tiling: 0.06
averageTemp: 20.27

# ★ KEY: climate-space targets — a biome is a SET of points in (temp, moisture) space
climateTargets:
  - (20.30, 0.43)
  - (16.62, 0.63)
  - (3.94, 0.48)

# ★ KEY: per-biome noise seed offset — vegetation patterns differ per biome
biomeOffset: (9825, 2240, 9160)

vegetationProfile:
  spawnForests: true
  forestCoverage: 0.566               # what fraction of biome carries forest
  forestDensity: 0.412                # within forest patches, how dense
  patchScale: 0.25                    # noise scale for forest-patch shape
  patchSoftness: 0.2                  # patch-edge feather
  minTreeTemperature: -30             # hard temperature gate
  treeSpecies: [Pine, Birch, Oak]     # species pool for this biome

  spawnGrass: true
  grasslandBiome: false               # flag for "this biome IS grass-dominant"
  grassSpecies: <single species>      # ONE grass species per biome (not a list)
  minGrassTemperature: -5
  grassCoverage: 0.739
  grassDensity: 0.705
  grassPatchScale: 0.057              # finer than forest patches
  grassPatchSoftness: 0.142
```

### What's clever

**Multiple `climateTargets` per biome.** This lets a single biome occupy a *region* in climate space, not just one point. Continental sits at three points — cold-wet, warm-wet, warm-medium. Tropical Rainforest has two clustered points at high temperature + high moisture.

**`biomeOffset`** is a 3D random offset added to noise sample positions for vegetation placement. So when Pine in Continental and Pine in Temperate Rainforest both reference the same Perlin field at the same world location, they get different values. **Result:** the same species placed at boundary points doesn't look like a single continuous patch crossing biomes — each biome has its own characteristic placement texture.

**Forest vs grass are independent layers**, not nested. A point can have grass without forest, forest without grass, both, or neither. Each has its own coverage/density/patch scale/softness/temperature gate.

**Grass is one species per biome** (less detail than trees). Trees are species lists (mixed forests). Reflects what's expensive to render — grass blade density makes per-species variation invisible, trees are sparse enough that species matter visually.

## The tree species model

From `Birch.asset`:

```yaml
baseWeight: 0.3                # multiplier on this species' score
targetTemperature: 17.2        # peak of Gaussian niche
temperatureVariance: 9         # σ of niche
targetPrecipitation: 0.679
precipitationVariance: 0.15
models: [variant1, variant2, variant3]  # rotation between 3 visual variants
```

When placing a tree at a candidate location with local climate (T, P), the score for each species is:

```
score(species) = baseWeight
                * exp(-((T - targetT)/σ_T)²)
                * exp(-((P - targetP)/σ_P)²)
```

Pick the species by weighted random (or argmax). Different species win at different points → mixed forests. Each species gradually falls off at climate edges → smooth species transitions across biomes.

This is **per-species niche modeling**, distinct from the per-biome species list. Both exist:
- Biome filters which species are *eligible* (`treeSpecies` list)
- Species niche then ranks the eligible ones by climate fit

So Tropical Rainforest's 6 species (with intentional GUID duplicates → weight bias) all get ranked by their own Gaussian niches at each placement point.

## The grass species model

From `TundraGrass.asset`:

```yaml
models: [5 variants]
upAxis: 2          # which model-space axis is up (Z here)
minScale: 0.27
maxScale: 0.4
verticalOffset: -0.1
```

No climate niche on grass — that's the parent biome's job. Just visual variants + scale jitter.

## Climate model (from EarthlikeSettings)

```yaml
planetTilt: 20                          # axial tilt → latitudinal climate bands
temperatureNoiseScale: 0.164            # noise added on top of latitude base
temperatureNoiseStrength: 35.376        # ±35°C noise modulation
temperatureNoiseOctaves: 3
minimumPrecipitationLatitude: 15.7      # wet zones outside this latitude band
precipitationNoiseScale: 0.343
precipitationNoiseStrength: 1.179
altitudeTemperatureDropConstant: 75     # lapse rate (°C per "altitude unit")
```

Climate is **latitude-based + noise + altitude-lapse**. Not just noise — there's a latitude gradient and a height-driven temperature drop. That's why high mountains naturally become snowy regardless of biome assignment.

## ★ Biome assignment — the secret sauce

```yaml
biomeVoronoiPointCount: 2335
biomeTemperatureWeight: 4.258
randomizeBiomeVoronoiAmount: 0.771
biomeClearArtifactIterations: 5
biomeDomainWarpStrength: 0.103
biomeDomainWarpScale: 0.0753
biomeDomainWarpOctaves: 4
biomeDomainWarpPersistence: 0.418
biomeDomainWarpLacunarity: 2.73
biomeEdgeWidth: 0.0373
```

Reconstruction of the algorithm:

1. **Seed 2335 Voronoi points** across the sphere (probably Poisson-disk or Fibonacci-sphere distribution, randomized by `randomizeBiomeVoronoiAmount`).
2. For each seed point, compute its (temperature, precipitation) and find the **closest biome's `climateTargets`** under a weighted L2 metric where temperature gets 4.26× the weight of precipitation. Assign that biome to the seed.
3. For any surface point P, **domain-warp P** using 4-octave Perlin (warp strength 0.10, scale 0.075). This shifts the lookup position by a noise vector — straight cell boundaries become wavy, organic borders.
4. Look up the nearest Voronoi seed to the warped P. That seed's biome is P's biome.
5. **Run 5 cleanup iterations** removing speckle (probably "if 7+ of 8 neighbors are biome B and I'm biome A, switch to B" — a graphics erosion/dilation pass).
6. `biomeEdgeWidth: 0.0373` is the **soft blend zone** — within this distance of a boundary, look up multiple nearby seeds and blend their biome textures by inverse-distance weighting (the top-K idea).

### Why this gives natural borders

- **Voronoi** gives random cell sizes and irregular shapes (vs grid quantization).
- **Domain warp** makes cell borders meandering and organic-looking. Without warp, Voronoi cells have visibly straight edges between cell centers; with warp, those edges fractalize.
- **Climate-space distance** (instead of raw position distance) means adjacent biomes are climatically similar — you go through Steppe between Desert and Continental, not from Desert straight to Tropical Rainforest.
- **Temperature weighting > 1** mimics real climate where temperature is a much stronger driver than moisture (deserts and tundras are both "dry" but climatically distant).
- **Artifact cleanup** removes the speckle that pure noise produces — gives clean continents and obvious belts rather than checkerboard mottling.

## Terrain texturing — the layered shader

From `Shader Graphs_TerrainTextured.shader` Properties:

```
_BiomeTexture1..4 + _BiomeNormal1..4 + _BiomeTiling1..4   # top-4 biome blend
_SnowTexture     + _SnowNormal     + _SnowTiling    + _SnowTemp = -7.5      # override
_SlopeTexture    + _SlopeNormal    + _SlopeTiling   + _minSlope, _maxSlope  # override
_CoastTexture    + _CoastNormal    + _CoastTiling   + _beachStartCutoff, _beachEndCutoff  # override
```

**Layer composition (reconstructed):**

```hlsl
// Pseudocode for fragment
float4 baseAlbedo =
    biomeWeight1 * sample(_BiomeTexture1, uv * _BiomeTiling1) +
    biomeWeight2 * sample(_BiomeTexture2, uv * _BiomeTiling2) +
    biomeWeight3 * sample(_BiomeTexture3, uv * _BiomeTiling3) +
    biomeWeight4 * sample(_BiomeTexture4, uv * _BiomeTiling4);

float coastT  = smoothstep(_beachEndCutoff, _beachStartCutoff, heightAboveSea);
float slopeT  = smoothstep(_minSlope, _maxSlope, surfaceSlope);
float snowT   = smoothstep(_SnowTemp + 1, _SnowTemp - 1, localTemperature);

float4 result = baseAlbedo;
result = lerp(result, sample(_CoastTexture, ...), coastT);
result = lerp(result, sample(_SlopeTexture, ...), slopeT);
result = lerp(result, sample(_SnowTexture,  ...), snowT);
```

Order matters — they likely composite coast → slope → snow so steep snowy cliffs still read as snow rather than rock-then-snow.

**Each override has a START/END cutoff** giving a smoothstep band. So beaches fade in over a small altitude range above sea level; cliffs blend over a slope range; snow blends in as temperature drops past the threshold. Hard cutoffs would look terrible — the smoothsteps are essential.

## Vegetation placement (reconstructed)

The compute shader / placement code isn't visible, but the parameter surface tells you the algorithm. For each vegetation tile at `vegetationTileLevel: 5`:

```
For each candidate placement point P:
    biome = lookupBiome(P)   # from biome map

    # Coverage gate
    coverageNoise = perlin(P + biome.biomeOffset, scale = biome.patchScale)
    coverageNoise = smoothstep(1 - biome.patchSoftness, 1, coverageNoise + biome.forestCoverage - 0.5)
    if (coverageNoise < random()) skip;

    # Density gate
    if (random() > biome.forestDensity) skip;

    # Temperature gate
    if (localTemperature < biome.minTreeTemperature) skip;

    # Species selection (Gaussian niche)
    scores = [species.score(localTemp, localPrecip) for species in biome.treeSpecies]
    species = weightedRandom(scores)

    # Model variant
    model = species.models[random_index]

    # Scale jitter
    scale = random(species.minScale, species.maxScale)

    spawn(model, P, scale, rotation = random)
```

The **`biomeOffset` + `patchScale` + `patchSoftness`** trio is what gives natural-looking patches. Patches are Perlin blobs sized by `patchScale`, edges feathered by `patchSoftness`, and translated per-biome by `biomeOffset` so neighboring biomes don't have aligned vegetation lattices.

## Other interesting systems

### Plate tectonics for mountain placement

```yaml
platePointCount: 4096
minorPlateCount: 8
majorPlateCount: 11
oceanicPlateRatio: 0.318
movementIterations: 10
tectonicEdgeWidth: 0.0384
oceanicPlateWaterAmount: 0.159
tectonicMountainsMagnitude: 0.95
mountainSmoothingWidth: 0.095
boundaryNoiseScale: 100
boundaryNoiseAmplitude: 0.74
```

They simulate **19 tectonic plates** (8 minor + 11 major) over **10 iterations of movement**, then mountain ranges form along **convergent boundaries** with magnitude scaling and noise-modulated edges.

This produces:
- **Real mountain ranges** (linear chains following plate boundaries) instead of "mountain-blob noise field"
- **Oceans concentrated on oceanic plates** rather than below an arbitrary noise threshold
- **Boundary noise** so edges aren't smooth Voronoi-like polygons

This is genuinely sophisticated. Most procedural planets just use ridge noise for mountains and a height threshold for oceans — they get visibly random mountain placement. The plate sim gives geological structure.

Cost: probably a one-time bake at world generation (10 iterations of ~4K points is fast even on CPU).

### Climate from latitude + altitude

`altitudeTemperatureDropConstant: 75` and `minimumPrecipitationLatitude: 15.7` give them realistic Earth-like belts:

- Equator: hot, wet (low latitude × inside precip band)
- Subtropical: hot, dry (just outside precip band)
- Temperate: warm, variable
- Polar: cold, dry

This drives the climate-space biome assignment naturally without needing a separate "biome belt" mask.

### `vegetationTileLevel: 5`

Probably a **quadtree LOD** of vegetation tiles — 5 levels of subdivision. Coarser levels for distant terrain (just billboards or no vegetation), finer levels near camera (full instance density).

## Comparison to our system

Per [`docs/agent-conversation/`](../agent-conversation/) and current code, ours uses:

| Concern | Theirs | Ours |
| ------- | ------ | ---- |
| Geometry | Unity Terrain patches (likely) | Sphere mesh + compute chunks |
| Biome model | 9 Köppen biomes, ScriptableObject pool | Top-K biome blend kernel per chunk |
| Biome assignment | Voronoi (2335 pts) + domain warp in climate space | Direct climate lookup per chunk vertex |
| Biome boundaries | Domain-warped Voronoi + 5 cleanup passes | Top-K weighted blend (kernel-limited) |
| Mountain placement | Plate tectonics sim | Noise (FBM + ridges per layer) |
| Climate inputs | Latitude + altitude lapse + noise | (TBD — Phase B design) |
| Terrain textures | 4 biome slots + snow/slope/coast overrides | (Phase B design pending) |
| Tree placement | Per-biome coverage/density × per-species Gaussian niche | (Not implemented) |
| Grass placement | Per-biome coverage/density, single species/biome | Chunk-based grass instancing |
| Detail rendering | Unity built-in TerrainDetail (BillboardWaving) | Custom compute-instanced blades |

### Known issues theirs solves that we have

1. **[Chunk biome seam](../../memory/project_chunk_biome_seam.md)** — our top-K kernel can't see across chunk boundaries. Their Voronoi-based assignment is **inherently global** (every point looks up its nearest seed regardless of chunks), so seams don't exist by construction. **Adopting Voronoi-in-climate-space would close this issue completely.**

2. **Biome variety at boundaries** — Top-K mixing produces "average of nearby biomes" looks. Their domain-warped boundaries with sharp transitions PLUS the snow/slope/coast overlays mean transitions look like real-world geography (rocky outcrops, beaches, snowlines) instead of color smoothing.

3. **[Normal mapping flat](../../memory/project_normal_mapping_flat.md)** — Their 4 biome normal maps + slope normal + snow normal + coast normal probably explain why their terrain reads as 3D even from far away. They have *seven* normal-mapped layers contributing. Worth checking what compression/format they use (probably DXT5 / BC5 with proper unpacking).

## Recommendations — what to consider stealing

Ordered by ratio of (visual win / implementation effort):

### Tier 1 — high value, modest effort

1. **`biomeOffset` per biome** — 3D noise offset added to vegetation placement noise sample. Adds character per biome. **Cost:** one field on the biome SO + one term in the noise lookup. **Win:** kills "identical placement patterns across biomes."

2. **Per-species Gaussian niche for trees** — `targetTemperature/Variance` + `targetPrecipitation/Variance` + `baseWeight`. Replace "biome → species list" with "biome lists candidates, niche ranks them." **Cost:** restructure the placement scoring. **Win:** smooth species transitions across biomes; mixed forests; species placement responds to local climate, not just biome ID.

3. **Snow/slope/coast as independent texture overrides** with smoothstep cutoffs. **Cost:** four shader uniforms + ~10 lines of HLSL composition. **Win:** terrain looks like it has actual *features* (cliffs, beaches, snowlines) instead of just colored regions. This is probably the single most underrated visual upgrade you can ship.

### Tier 2 — high value, larger effort

4. **Voronoi + domain warp for biome assignment** (in climate space). Replaces our chunk-local top-K kernel. **Cost:** significant — needs a global biome map (texture or compute buffer), per-chunk sampling instead of per-chunk computation. **Win:** kills the chunk-seam issue entirely + biome borders look geographically real.

5. **Climate model = latitude + altitude + noise** rather than direct noise. **Cost:** moderate; needs a "compute (T, P) for any world point" function used by both biome assignment and species niche scoring. **Win:** makes the climate-space machinery from #4 actually meaningful; gives Earth-like belts and mountain snow.

6. **Artifact cleanup pass on the biome map** — 5 iterations of "majority-of-neighbors" smoothing. **Cost:** one extra compute pass at world bake. **Win:** removes the speckle that pure noise outputs always produce.

### Tier 3 — interesting but probably out of scope

7. **Plate tectonics simulation for mountains.** This is genuinely cool but it's a major addition. Worth a separate research paper on Songer & Yang 2013 or similar academic refs if we ever pursue it. For now our FBM + ridges is reasonable.

8. **Layered LOD of vegetation tiles** (`vegetationTileLevel: 5`). Worth designing in from the start; we'll need it eventually as the grass chunks scale.

## Things we already do better

To be fair to our system:

- **Sphere mesh from scratch** vs their cube-face Terrain — we own the geometry pipeline cleanly. They've shoehorned Unity Terrain onto a sphere, which has known seam/UV issues.
- **Compute-shader-instanced grass** — far more flexible than Unity's TerrainDetail BillboardWaving system. Their grass is the standard Unity terrain detail with shader wind; ours can do per-blade physics, interactor bend (slice 6 hook), etc.
- **Per-chunk colocated work** — our chunk pattern means LOD streaming is natural. Their tile system seems baked-once-at-world-gen.
- **Awaitable-based async pipeline + cancellation token plumbing** — robust scaffolding. They appear to have a synchronous baking pass.

The biome / climate / placement model is where they beat us. The runtime rendering / streaming / engineering hygiene is where we beat them.

## Where the model lives in their files (for future re-reference)

- `Assets/MonoBehaviour/EarthlikeSettings.asset` — global world recipe
- `Assets/MonoBehaviour/{Continental,Desert,Polar,Savanna,Steppe,Swamp,Temperate Rainf.,Tropical Rainf.,Tundra}.asset` — 9 biome SOs
- `Assets/MonoBehaviour/{Acacia,Birch,Cactus,DeadTree,Joshua,Oak,Palm,Pine,Willow,Bamboo,Banana}.asset` — tree species SOs (climate niche + variant models)
- `Assets/MonoBehaviour/{TundraGrass,TempRFGrass,TropicalRfForest,LightGreenGrass,YellowGrass,YellowGreenGrass}.asset` — grass species SOs + a few "forest preset" lists
- `Assets/Shader/Shader Graphs_TerrainTextured.shader` — Properties block reveals the 4+3 layered shader
- `Assets/Shader/Shader Graphs_{BiomeColor,GrassCoverage,ForestCoverage,HeightMap,Slope}.shader` — debug viewers (visualize biome ID / grass mask / forest mask / heightmap / slope). Worth noting they kept these in the shipped build — useful pattern.

## Suggested follow-ups

- Compare snow/slope/coast cutoff values against what we'd want — their defaults (`_beachStartCutoff: 0.01`, `_minSlope: 0.6`, `_SnowTemp: -7.5`) are good starting points.
- If Phase B biome textures design isn't locked yet, fold the four overrides into the design upfront — they're cheaper to add early than retrofit.
- Read Whittaker's biome diagram (the temperature × precipitation 2D plot) — that's the conceptual model both biome SO `climateTargets` and per-species niches descend from.
- If we ever look at iSphere / TerraGen / Outerra / Songer-Yang papers — those are the academic predecessors of this kind of climate-driven biome system.
