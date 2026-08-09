# Biome border seamlessness — options + plan (2026-08-09)

Status: **rev 2 — Codex review verified (all 6 claims CONFIRMED against the tree, parallel-agent check), plan
revised.** The corrected decision of record is the **"Verified disposition"** section at the bottom; where the
original body below conflicts with it, the verified section wins. Bryan wants the biomes to **seamlessly blend
together**; walking the surface he still sees **drastic border colors**.

> **Superseded by verification** (see Verified disposition): (1) **Option A is moot** — the biome bake is a
> per-face *atlas* sampled by all LOD nodes, so blend width is LOD-independent; there is no per-render-node
> kernel to adapt. (2) The root cause is **not** an "unpadded hard step / chunk seam" — Pass 1 is padded
> (152²) and the boundary is a continuous-but-abrupt **ecotone**; the real weakness is `Clamp01` collapsing
> out-of-leaf climate to edge values. (3) **Option D as written misses production** — production reads the
> `SurfaceAlbedo` texture array, not the tint LUT (dead code). (4) Option B needs **no new interface** but is
> **spatial Voronoi**, not a climate gradient, and needs a secondary-weight cache.

## Symptom

On-surface, biome boundaries read as **drastic color steps**, not seamless blends. Earlier rounds already
touched this: `150a482` added a grass-overlay greenness gate (killed a thin green-over-tan line), `17707e3`
dropped it (it wrongly made tan-ground savanna barren) and trimmed overlay saturation instead. A live
`grass.surface-saturation` knob and a `smoothstep` overlay toe were added this session — both help the thin
grass-overlay *line* but **not** the underlying biome **color-step** drama, which is a separate system.

## What was verified (code + captures)

Isolation done in the `Assets/Scenes/Tests/Grass.unity` scene (all biomes adjacent), 1000² captures at a fixed
camera, frozen local-noon sun:

| Test | Result | Conclusion |
|---|---|---|
| `debug.mode BiomeMapFlatColor` (pure biome color) | smooth gradients, **no hard line** | the shader blend is smooth |
| `grass.overlay-strength 0` (far blanket off) | terrain smooth; biome **colors still very different** at borders | grass overlay is a *separate*, thinner contributor |
| `grass.surface-saturation 0` | thin overlay line muted; biome color step remains | saturation is the overlay lever, not the biome-step lever |

**Measured** (pure `BiomeMapFlatColor`, green→grey border, camera ~500 m from surface, ~0.466 m/px):
transition width **≈ 10–20 m** across the 20–80% color crossing. Up close this is the band the character walks
through.

### The color blend itself is smooth — not the cause

`PlanetVertexColor.shader` `SampleBiomeTriplanarPbr`
([:522](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L522)) reads the top-4 `_BiomeIds`/`_BiomeWeights`
per texel and accumulates biome albedo **weighted** ([CornerTriplanarWeightedPbr:480](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L480)),
then **bilinearly** interpolates 4 corners. The step is entirely in the **baked weights**, not the shader.

### Root cause: the bake softens hard cells only by a kernel window

`BiomeMapBaker` ([BiomeMapBaker.cs](../../Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs)):
- **Pass 1** ([:78](../../Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs#L78)) fills a high-res id grid where each
  cell stores **ONE hard `primary` biome id** ([:122](../../Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs#L122))
  from `assignmentField.EvaluatePrimaryId`. **No soft membership at the source** — the boundary between two
  biomes is a hard categorical step at cell resolution.
- **Pass 2** ([:129](../../Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs#L129)) makes each output texel's weights
  the **area fraction** of each biome in a `KernelRadius=12` window (25×25 = 625 samples) over those hard cells,
  takes top-K, normalizes.

Constants: `MapResolution=64`, `HighResolution=128` (2×), `KernelRadius=12`
([:27-29](../../Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs#L27)), `TopK=4`. So **the entire blend zone equals
the kernel window** — there is no other softening. The blend width is a **fixed fraction of the baked chunk's
world size** (kernel is measured in texels, texels are chunk/64).

### Open question — is the blend width LOD-dependent? (needs confirmation)

`ChunkedSurfaceProvider` builds all 6 quadtrees with `BuildToFixedDepth(_maxChunkDepth)`
([:153](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L153)) — a **fixed** depth (~depth 2 per
the memory note at [:35](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L35)). If the biome map is
baked once per fixed-depth leaf, the blend width is **uniform in world space** (~10–20 m everywhere), and "worse
up close" is just "10–20 m is a lot when you're standing in it." If instead the render selects coarser LOD nodes
that each carry their own biome bake, the blend width **scales with the rendered node size** → narrower on the
near (finer) nodes → drastic up close, soft far. **This must be confirmed** (where the biome map is baked vs.
which node the camera renders) because it decides whether the fix must be LOD-adaptive (Option A) or a simple
uniform widen (Option C) suffices.

Two aggravators stack regardless: the biome LUT colors are **highly contrasty** (vivid green / tan / grey), and
the grass overlay paints green on top.

## Options

### A. LOD-adaptive kernel — fixed world blend width
Scale `KernelRadius` (in texels) by the baked node's world size so the blend zone is a **constant world
distance** (e.g. 30 m) at every LOD. Correct fix **iff** the blend is LOD-dependent (open question above).
- **Pro:** seamless at all distances by construction; the true root fix if LOD-dependent.
- **Con:** near/fine nodes get a larger texel kernel → bake cost ↑ (kernel samples grow as radius²); needs the
  node's world size threaded into the baker. Moot if the bake is already uniform-depth (then A ≡ C).

### B. Soft membership at the source — Pass 1 stores weighted biomes
Replace the hard `primary` per high-res cell with a **soft climate-based blend** (top-2 biomes + weights from
climate-space proximity), so boundaries are smooth *before* the kernel. The kernel then just denoises.
- **Pro:** inherently smooth, LOD-independent, removes the hard-cell stepping at the root; blend width follows
  the climate gradient (physically meaningful). Best-looking result.
- **Con:** biggest change — touches `IBiomeAssignmentField.EvaluatePrimaryId` / `BiomeLookupEvaluator` and both
  bake passes; more per-cell work; must keep the top-K packing + weight normalization intact.

### C. Widen the kernel — `KernelRadius` 12 → ~20
One constant. Wider blend everywhere.
- **Pro:** trivial, immediate, reversible.
- **Con:** bake cost 625 → ~1681 samples/texel (~2.7×); still a fraction of chunk, so if LOD-dependent it stays
  narrower up close (doesn't fix the root); ceiling on how wide before biomes smear into mush.

### D. Reduce biome LUT color contrast — desaturate the palette toward each other
Art change: bring the biome ground colors closer in hue/value so even a 10–20 m blend isn't a drastic step.
- **Pro:** cheapest visible relief; no bake/perf change; directly attacks the "drastic *colors*" half.
- **Con:** Bryan's palette/art call; reduces biome color identity; doesn't widen the blend (a narrow blend of
  *similar* colors is just less noticeable).

### (E. Already shipped, orthogonal) grass-overlay levers
`grass.surface-saturation` knob + `smoothstep` coverage toe (this session) address the thin grass-overlay line
on top — keep, but they do **not** fix the biome color step. Overlay saturation still worth a live tune.

## Recommendation (for the reviewer to challenge)

1. **First confirm the LOD question** — where the biome map is baked vs. which node renders. Cheap, decides A vs C.
2. If LOD-dependent → **A** (LOD-adaptive kernel) is the correct structural fix.
3. If uniform-depth → the blend is simply too narrow; **B** (soft membership) is the better long-term fix than
   just **C** (widen), because C smears hard cells while B fixes the stepping at the source.
4. **D** (palette) as an independent, immediate lever regardless — the colors are genuinely very contrasty.
5. Keep the shipped overlay levers (E).

Leaning: **B (soft membership) + D (palette softening)** as the durable combination, with the LOD confirmation
gating whether A is also needed. All are visual → capture-diff in the grass scene + Bryan's F10 sign-off; none
should land before the LOD question is answered.

## Questions for Codex

1. Is the biome bake per-render-LOD-node or once per fixed-depth leaf? (Decides A vs C — see the open question.)
2. Soft membership (B): best place to introduce top-2 climate weights — in `IBiomeAssignmentField` /
   `BiomeLookupEvaluator.ResolveFromLandBiomes`, or as a new Pass-1 output the kernel consumes? Any determinism
   or top-K-packing traps?
3. Is a **fixed world blend width** (~20–40 m) the right target, or should the blend track the climate gradient
   (variable width, wide where climates change slowly, narrow at sharp fronts)?
4. Perf budget: bake runs per chunk at generation — is a ~2.7× (C) or larger (A near-nodes) bake-cost hit
   acceptable, or must the fix be cost-neutral (favoring B/D)?
5. Anything wrong in the taxonomy above, or a better fifth option?

## Provenance

Verified 2026-08-09 against branch `character-controller-mvp`. Captures in the session scratchpad
(`biome_iso_*.png`, `biome_diff_toe.png`). Shader blend: `PlanetVertexColor.shader` `SampleBiomeTriplanarPbr`.
Bake: `BiomeMapBaker.cs` Pass 1/2. Fixed-depth quadtree: `ChunkedSurfaceProvider.cs:153`.

## Codex review feedback — 2026-08-09

**Verdict:** The isolation of the grass overlay from the underlying terrain blend is sound, but the current **B + D** recommendation is not ready for approval. The LOD question is already answered by the live atlas path, the bake description omits its existing padding, B bypasses an existing soft-membership API while adding a large hidden cost, and D targets a LUT that the production PBR terrain does not use for its albedo.

### BB1. ARCH (blocking) — Close the LOD question; Option A is not the active path

In normal face-atlas mode, only fixed-max-depth leaves are baked, their maps are stitched into one atlas per face, and every rendered node maps its chunk UV into that same face atlas. Camera-selected terrain LOD therefore does **not** change the biome blend width. The per-render-node bake exists only as the fallback when `CanBuildFaceAtlases` fails.

The configured scenes fit the atlas path comfortably: `GrassDiagnosticPlanet` uses depth 3, producing 505×505 face atlases; the main `Planet` uses depth 4, producing 1009×1009 atlases. Remove Option A from the active recommendation and document the fallback separately.

Fixed depth still does not mean uniform world-space width. The band varies with planet radius, max depth, and cube-face metric distortion. At the same normalized face location, the main planet's kernel spans about 1.67× as many metres as the Grass diagnostic planet's. A true fixed-metre target would require a local face metric, not render-LOD adaptation.

Evidence: [`BiomeAtlasService.CanBuildFaceAtlases`](../../Assets/Scripts/Planet/Surface/BiomeAtlasService.cs#L55), leaf-only stitching at [`BuildFacePixelsAsync`](../../Assets/Scripts/Planet/Surface/BiomeAtlasService.cs#L255), atlas binding in [`ChunkMeshCache`](../../Assets/Scripts/Planet/Surface/ChunkMeshCache.cs#L270), and bake target selection in [`ShouldBakeChunkMap`](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L1731).

### BB2. CORRECTNESS (blocking) — Separate a true chunk seam from a smooth but abrupt ecotone

Pass 1 does not build an unpadded 128×128 grid. It builds a 152×152 grid (`128 + 2×12`), evaluates directions outside the leaf, and gives adjacent leaves overlapping biome-assignment neighborhoods. The remaining padding weakness is narrower: temperature, moisture, and elevation are bilinearly sampled with `Clamp01`, so their out-of-leaf padding collapses to the current leaf's edge values.

The document should distinguish three symptoms:

- a genuine discontinuity at a chunk or cube-face boundary;
- a mathematically continuous but perceptually abrupt biome ecotone;
- the independent grass-overlay edge.

The reported `BiomeMapFlatColor` result—smooth gradient and no hard line—classifies the current problem as the second item. Do not call that a chunk seam or use the older unpadded-bake diagnosis as its root cause.

Evidence: padded-grid construction in [`BiomeMapBaker`](../../Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs#L30), out-of-leaf sampling at [line 76](../../Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs#L76), and climate/elevation clamping at [line 235](../../Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs#L235).

### BB3. ARCH/PERF — Reframe Option B around the existing soft-membership contract

`IBiomeAssignmentField.Evaluate` already returns primary ID, secondary ID, and secondary weight. Both `DiagnosticGridBiomeField` and `VoronoiBiomeField` implement it; the baker deliberately calls `EvaluatePrimaryId` and discards that information. B therefore does not require a new assignment interface or a new climate-weight resolver. If pursued, Pass 1 should consume the existing contract and Pass 2 should accumulate quantized source weights.

There is a substantial hidden cost: `VoronoiBiomeField.EvaluatePrimaryId` samples its prebuilt 512×512 primary atlas, while `Evaluate` performs the full domain-warp and KD-tree nearest/different-biome queries. Calling `Evaluate` for every padded sample of every leaf is not an acceptable default. A production B path would first extend the global Voronoi lookup with secondary ID plus byte weight, then let leaf bakes sample that deterministic cache.

Preserve integer accumulation and the current stable ID-order tie break. Record how much source weight falls outside top K before renormalization; soft membership increases the chance of more than four contributors at junctions. Also rename the option: production Voronoi membership follows spatial seed distance, not a climate gradient, and its current ratio can soften much of a cell. It is not proven to be the best-looking result.

Evidence: [`IBiomeAssignmentField`](../../Assets/Scripts/Planet/Biomes/BiomeAssignmentField.cs#L23) and [`VoronoiBiomeField.Evaluate`](../../Assets/Scripts/Planet/Biomes/VoronoiBiomeField.cs#L119).

### BB4. CORRECTNESS (blocking for Option D) — Tune production albedo, not the flat-color LUT

`BiomeMapFlatColor` displays the pre-blended LUT colors. With `_BIOME_COLOR_MODE_TEXTURE` enabled, production terrain instead samples the `SurfaceAlbedo` texture array in `SampleBiomeTriplanarPbr`; changing biome LUT/tint colors does not retune those production slices.

Rewrite D as one of:

- retune the actual authored `SurfaceAlbedo` textures; or
- introduce an explicit production per-biome tint only if asset retuning cannot provide the required control.

Use `TerrainSelectedAlbedo` and the normal production view for the decision capture. `BiomeMapFlatColor` remains useful for proving weight continuity, but it cannot validate the proposed production palette change.

Evidence: albedo-array construction in [`BiomeSurfaceTextureArrays`](../../Assets/Scripts/Planet/Biomes/BiomeSurfaceTextureArrays.cs#L52) and the production shader branch in [`PlanetVertexColor.shader`](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L1045).

### BB5. BEHAVIOR — A/B/C also change generated grass

The face-atlas biome IDs and weights are shared with both near- and chunk-grass placement. Grass density is accumulated from those weights using each biome's blend power. Widening the kernel or changing source membership therefore changes emitted grass and parameter blending; it is not terrain-only pixel tuning. Scatter remains on its separate biome-evaluation path.

Acceptance must include settled grass counters plus visual checks for biome-specific grass leaking too far across the widened band. A before/after count difference is expected, but it must be bounded and explained.

Evidence: [`BlendGrassParams`](../../Assets/Graphics/Shaders/Includes/GrassPlacementParamBlend.hlsl#L26).

### BB6. PERF/EVIDENCE — Measure the real planet before selecting a width or accepting 2.7× work

The current evidence names scratchpad images but does not provide archived paths, seed, exact camera pose, quality tier, face location, or sidecars. The Grass diagnostic recipe also differs materially from production: radius 1500/depth 3 versus radius 5000/depth 4. Its measured 10–20 m transition must not become the main-planet target without a main-planet measurement.

The current nested scan performs approximately 983 million kernel-sample visits on the diagnostic planet and 3.93 billion on the main planet. Radius 20 raises those to approximately 2.64 billion and 10.58 billion. That does not prove the change is too slow, but it makes an evidence-free approval inappropriate. Capture the existing `Biome color timings` `mapBake` and total generation times on both recipes, declare the budget, then measure the candidate.

If a wider kernel proves visually correct but misses the budget, replace the radius-squared scan with a summed-area or sliding-window biome histogram. Do not build that optimization before the visual experiment proves that width is the right lever.

### Recommended disposition

1. Close the LOD question as fixed-depth face-atlas sampling and remove A from the active path.
2. Archive production-material captures on the main planet, with seed, pose, tier, face location, and sidecars.
3. Test D against the actual production albedo path.
4. If contrast correction is insufficient, try C as a reversible visual experiment and measure `mapBake`.
5. Optimize the existing box filter only if C is visually accepted and misses the generation budget.
6. Defer B unless the desired art direction explicitly calls for broad, variable-width ecotones and justifies the extra global soft-membership data.

### What came back clean

- The shader's manual four-corner blend is continuous; there is no need to replace the PBR sampling architecture.
- Disabling the grass overlay is the correct isolation test and proves that overlay tuning alone cannot solve the underlying color contrast.
- The existing top-K packing and integer normalization provide a deterministic base that can be retained by either a wider hard-source filter or a future quantized soft-source filter.

## Verified disposition — 2026-08-09 (decision of record)

Every Codex claim BB1–BB6 was **independently verified against the tree** (6 parallel agents, one per claim,
adversarial "default-skeptical" verdict with file:line evidence). **All six returned CONFIRMED** — precise
citations, only clarifying nuances, zero refutations. Claude's original **B+D** lean was wrong. This section is
the corrected decision of record.

### Corrected root cause

- **Blend width is LOD-INDEPENDENT (BB1 ✓).** In normal mode the biome map is baked **once per fixed-depth
  leaf** and stitched into **one atlas per cube face** (`BiomeAtlasService.BuildFacePixelsAsync` skips
  `!IsLeaf`; `ShouldBakeChunkMap = !usesFaceBiomeAtlases || chunk.IsLeaf`, `ChunkedSurfaceProvider.cs:1731`).
  Every rendered LOD node samples a UV sub-rect of that same atlas (`ChunkMeshCache.cs:270`, no LOD check).
  The per-render-node bake is only the `CanBuildFaceAtlases`-fails **fallback**. → **Option A is removed.**
- **Not a chunk seam — a continuous ecotone (BB2 ✓).** Pass 1 builds a **padded 152²** grid
  (`128 + 2×KernelRadius`) and samples the direction field **beyond** the leaf, so the categorical assignment is
  continuous across chunk boundaries. `BiomeMapFlatColor` (smooth, no hard line) is a **mathematically
  continuous but perceptually abrupt** biome ecotone. The remaining weakness is `Clamp01` on temperature/
  moisture/elevation (`BiomeMapBaker.cs:235,252`) collapsing the out-of-leaf padding to leaf-edge values.
- **Width does NOT transfer between planets (BB1/BB6 ✓).** The kernel is a fixed high-res-texel window, so its
  world width scales `~ radius / 2^depth`: **187.5 (diag r1500/d3) vs 312.5 (main r5000/d4) → ~1.67×**. The
  **10–20 m measured on `GrassDiagnosticPlanet` must NOT be reused as the main-planet target.**

### Revised options (verified)

- **A. LOD-adaptive kernel — REMOVED.** Moot in atlas mode (degenerates to C).
- **B. Soft membership — DEFER.** No new interface needed (`IBiomeAssignmentField.Evaluate` already returns
  primary+secondary+weight; `ResolveFromLandBiomes` already accepts them, fed degenerately today — BB3 ✓). BUT
  it is **spatial Voronoi seed-distance**, not a climate gradient (rename; unproven as best-looking), and a
  viable path must first build a **secondary-id+weight Voronoi cache** (mirroring the 512² primary atlas) —
  per-sample `Evaluate` (domain-warp + 2 KD-tree queries × ~23k padded samples/leaf) is too costly as default.
  Defer unless the art direction explicitly wants broad, variable-width ecotones.
- **C. Widen `KernelRadius` 12→~20 — reversible experiment, gated.** Real **2.69×** bake-kernel cost
  (625→1681 samples/texel; ~3.93B→10.58B sample-visits on the main planet — BB6 ✓). The `mapBake` timer
  already exists (`ChunkedSurfaceProvider.cs:1526` "Biome color timings"); measure it on **both** planets +
  declare a budget before accepting. Softens everywhere but stays a fixed fraction of leaf (width still varies
  by planet).
- **D. Reduce biome color contrast — REWRITTEN.** Production reads the authored **`SurfaceAlbedo` texture
  array** (`_BIOME_COLOR_MODE_TEXTURE`, `PlanetVertexColor.shader:1045`); the tint LUT is **dead code** (BB4 ✓).
  So D must be **(a) retune the authored `SurfaceAlbedo` textures**, or **(b) add an explicit production
  per-biome tint multiply** (none exists today). Capture decisions with **`TerrainSelectedAlbedo`**, not
  `BiomeMapFlatColor`.
- **F. (new, from BB2) Widen the ecotone at the source.** Instead of a bigger kernel, feed Pass 1's out-of-leaf
  climate/elevation from **neighbor chunks or a global field** rather than `Clamp01`-to-edge. Widens the
  transition where climates actually change, no kernel-cost blow-up. Needs design; flag as a candidate.
- **Coupling (BB5 ✓):** the biome atlas weights feed **near- + chunk-grass placement** (`BlendGrassParams`,
  density `pow(weight, blendPower)`). **B, C, and F change emitted grass**, not just terrain pixels — acceptance
  must include settled grass counters (`STAT_EMITTED_BLADES` / `NF_STAT_EMITTED`) with a bounded, explained
  delta + a leak check. Scatter is insulated **only while the change stays bake-side** (Pass 1/2); do not route
  B through `EvaluateBiome`.

### Recommended sequence (Claude + Codex agree)

1. **Measure on the main planet first (no code):** production captures (`TerrainSelectedAlbedo`, plus the normal
   view) with seed / camera pose / quality tier / face location + sidecars, and the `mapBake` + total-gen
   timings on both `GrassDiagnosticPlanet` and `Planet`. The 10–20 m number is diagnostic-only.
2. **Try D-corrected** (retune `SurfaceAlbedo` textures or add a production per-biome tint) — cheapest, attacks
   the "drastic *colors*" half directly; no bake/perf/grass impact.
3. **If contrast correction is insufficient → C** as a reversible KernelRadius experiment, with the `mapBake`
   budget + grass counters as acceptance gates.
4. **Consider F** (source-climate padding) if a wider *ecotone* is wanted without the kernel-cost hit.
5. **Optimize the box filter** (summed-area / sliding-window histogram) only if C is visually accepted and
   misses the generation budget.
6. **Defer B** unless the art direction explicitly calls for broad, variable-width ecotones.
7. Keep the shipped grass-overlay levers (`grass.surface-saturation`, `smoothstep` toe) — orthogonal, they
   don't touch the biome color step.

All of the above are visual → grass-scene + main-planet capture-diff and **Bryan's F10 sign-off** before landing.
