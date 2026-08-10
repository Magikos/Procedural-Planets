# Biome border seamlessness — options + plan (2026-08-09)

Status: **RESOLVED — it was a REGRESSION, fixed (commit `9ff9294`).** The drastic biome-edge line was the grass
surface-**overlay**, not the biome albedo. `150a482` had killed it by co-terminating the overlay with the biome
transition; `17707e3` dropped that gate (it starved savanna) and the line came back. Fix: restore the
co-termination gated on grass **density** (savanna-safe) — raise the coverage toe. Verified: the removed overlay
green sits in a ring exactly on the biome border. **The D2/G contrast work below is a SEPARATE, pre-existing,
lower-priority look item (the core biome albedo textures are contrasty) — not what Bryan was seeing; pursue only
if he still wants it after the regression fix.** History retained below.

---

Status (superseded): rev 2 — Codex review verified (all 6 claims CONFIRMED against the tree, parallel-agent
check), plan revised. Bryan wants the biomes to **seamlessly blend together**; walking the surface he still sees
**drastic border colors**.

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

## Empirical result — 2026-08-09 (C REFUTED as a lever; the drama is albedo contrast, not blend width)

Ran the width experiment on the diagnostic (grass) scene at production settings (`TerrainSelectedAlbedo`,
overlay off), before vs after **`KernelRadius 12 → 20`**:

- **Cost:** `mapBake` 1961 ms → 3621 ms (+85% on the diagnostic; the main planet, 4× leaves, would be far worse).
- **Result:** the biome border was **visibly unchanged** — the green→grey transition measured **2 / 7 / 23 px
  identical** before and after; the two renders are indistinguishable.
- **Why:** `BiomeMapBlend` (dominant-weight debug) shows the weights **already blend over a broad band**; the
  kernel was never the bottleneck. The border reads "drastic" because the biome **albedo textures themselves
  are very different** (dark-green grass ≈ (0.17,0.21,0.04) vs bright-grey rock ≈ (0.38,0.35,0.34) vs tan) — a
  **brightness + hue jump** that no weight-blend width can hide.

**Conclusion:** **C (and by extension F/B — anything that widens the weight ramp) is NOT the lever.** Reverted
`KernelRadius` to 12. The seamlessness problem is **biome albedo contrast (Option D)**, split into:
1. the vivid grass **overlay** green painted over the muted production terrain (tune `grass.surface-saturation`
   — knob exists), and
2. the **production `SurfaceAlbedo` texture** brightness/hue contrast between biomes (Codex BB4: retune the
   authored textures, or add a production per-biome tint; capture with `TerrainSelectedAlbedo`).

Both are **contrast/art** levers (a genuine vibrancy-vs-seamlessness tradeoff), not a blend-width code fix.
Recommended next: a production **per-biome tint** (in-engine, live-tunable, defaults to identity) so Bryan
equalizes biome brightness/hue toward each other to taste — or a direct `SurfaceAlbedo` texture retune.

## Round 2 — question for Codex: reconcile vibrant biomes with seamless borders

**Setup (verified this session):** the biome map is a per-face atlas (LOD-independent), the weight blend is
smooth and already wide, and **widening the kernel does nothing visible while costing +85% bake** (measured).
The border reads drastic purely because the production `SurfaceAlbedo` textures are **far apart in brightness
and hue** (dark-green grass vs bright-grey rock vs tan), linearly cross-faded by `CornerTriplanarWeightedPbr`
([PlanetVertexColor.shader:480-519](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L480)). A **linear**
crossfade of two very different textures reads as a muddy, abrupt midline — which is what we see.

**The tension:** Bryan wants **vibrant Synty biomes AND seamless borders**. Reducing contrast (Option D)
sacrifices vibrancy; widening the blend (C/F/B) is refuted. So the interesting question is whether a better
**blend *operator*** reconciles both.

### Candidate approaches (for Codex to rank / correct)

- **D1. Global biome-albedo contrast/saturation knob** — production `lerp(luma, albedo, vibrance)` (+ optional
  brightness-flatten), live-tunable, default = no change. Fast, but desaturates the whole planet globally.
- **D2. Per-biome tint** — one color/brightness multiply per biome slot (default identity) so only the
  worst-contrast biomes are nudged (e.g. lift grass value, drop rock value), keeping the rest vibrant. More
  authoring; keeps identity better than D1.
- **D3. Retune the authored `SurfaceAlbedo` textures** — fix the contrast at the art source. Full vibrancy,
  no shader knob; pure art.
- **G. Height/noise-based texture blend at the boundary (the operator change).** Replace the *linear* weighted
  crossfade with a **height-blend** (a.k.a. heightlerp / smooth-max over per-texel height × weight) or a
  noise/dither-masked transition, so two high-contrast biome textures **interlock** at the border (rock poking
  through grass, grass fringing into sand) instead of muddy-averaging. This is the standard "advanced terrain
  splatting" technique — it can make very different textures read as a natural transition **without
  desaturating** and **without widening** the weight band. **This is the option Claude under-weighted.**
- **E (shipped, orthogonal).** Grass-overlay `grass.surface-saturation` still trims the in-game vivid-green
  contribution on top of the terrain.

### Questions for Codex (round 2)

1. **Is G (height/noise-blend operator) the right fix** — i.e. the way to keep vibrant biomes AND seamless
   borders — or is contrast reduction (D) unavoidable? Is there a known better operator?
2. Does G fit the existing path cleanly? `CornerTriplanarWeightedPbr` already samples each biome's albedo +
   ARM (which may carry a **height**/AO channel) per corner and sums by weight — can a height-blend reuse that
   ARM/height, or does it need a new per-biome height input? Any triplanar/top-K interaction traps?
3. G still consumes the same top-4 `_BiomeWeights`, so does it perturb **grass** (BB5) the way C/B would, or is
   it terrain-albedo-only (grass reads weights, not the albedo operator)?
4. Perf: a height-blend is per-fragment (shader), not per-bake — is that free relative to the bake, and does it
   avoid the C bake-cost problem entirely?
5. If G is right, where does it slot — inside `CornerTriplanarWeightedPbr` (per-corner) or after the 4-corner
   bilinear? And should the height source be the ARM texture, a dedicated height map, or noise?
6. If G is NOT worth it, which of D1/D2/D3 do you recommend, and is the vibrancy loss simply unavoidable?

## Codex round-2 feedback — 2026-08-09

**Verdict:** **G is worth a bounded shader experiment, but it is not a complete replacement for D.** A
height/noise-shaped blend can replace the muddy near-field crossfade with coherent material patches and retain
the endpoint textures locally. It cannot remove the endpoint brightness/hue jump: outside the blend the original
colors still meet, and once the patches become sub-pixel their filtered average converges toward the same mixed
color. The likely durable answer is therefore **a small D2/D3 mean-luminance correction plus G for transition
character**, not G alone and not global desaturation.

### R2-1. G is the right operator experiment, not a proof that contrast work is avoidable

Height blend is the best first operator to test here. It keeps a continuous atlas ramp while allowing one
material's high areas to remain visible deeper into the other material, which is a much better model for
grass/dirt, rock/soil, and snow/rock than an unstructured RGB average. A stable world-space noise mask is a
reasonable proxy when authored height is unavailable.

Two alternatives are weaker first choices:

- hard stochastic/dither selection preserves endpoint samples close up, but can sparkle or crawl unless the
  mask is stable and filtered, and it still averages at distance;
- perceptual-color-space blending can improve some hue paths, but adds fragment ALU, does nothing for the
  normal/ARM transition, and cannot hide a large lightness difference.

The visual target should be stated as **align average lightness enough that the distant border is quiet while
preserving saturation and within-material contrast**. “Vibrant” does not require grass and rock to have widely
different mean luminance.

### R2-2. It fits per corner, but current ARM does not contain height

`SurfaceARM` is explicitly **R=AO, G=roughness, B=metallic** in both the data contract and shader; none of those
channels should be reinterpreted as height. The shader currently samples only `.rgb`, while the checked-in
`*_ARM.png` sources all have alpha fixed at 1.0. Alpha is therefore available for a future authored height pack,
but it contains no usable height today. The array placeholder is also `(AO=1, roughness=.5, metallic=0, A=1)`
and would need a documented neutral-height alpha.

If height is authored into ARM alpha, changing the triplanar ARM sample from RGB to RGBA obtains it from the
**same three texture fetches per biome slot**. A dedicated height array would add three triplanar fetches per
active slot and should not be the first implementation.

The implementation must be two-phase inside `CornerTriplanarWeightedPbr`:

1. sample and cache the active top-4 materials and their height/proxy values;
2. derive support-preserving effective weights, normalize them, then accumulate **albedo, normal, and ARM with
   those same weights**.

The height formula must retain `effectiveWeight[i] = 0` whenever atlas `weight[i] = 0`; otherwise a high height
can leak a biome beyond its authored atlas support. Strength zero must reproduce the current linear weights
exactly. Keep the current stable slot order for ties and an epsilon fallback to the original weights if the
height-weight sum collapses.

Evidence: [`BiomeDefinition.SurfaceARM`](../../Assets/Scripts/Planet/Biomes/BiomeDefinition.cs#L27), array
packing and placeholder at [`BiomeSurfaceTextureArrays`](../../Assets/Scripts/Planet/Biomes/BiomeSurfaceTextureArrays.cs#L51),
and the current RGB sampler at [`PlanetVertexColor.shader`](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L443).

### R2-3. A shader-only G does not perturb grass

G would consume the existing biome IDs/weights without rewriting them. Near- and chunk-grass placement read
those atlas textures independently, so blade counts, biome parameter blending, and scatter remain unchanged.
This differs from B/C/F, which alter the shared atlas itself.

“Terrain-only” should mean **terrain material only**, not albedo only: terrain normal and ARM must follow G's
effective weights or lighting/material boundaries will disagree with color. Grass still uses the original atlas
weights.

Evidence: chunk-grass atlas reads in [`BiomeGrassPlace.compute`](../../Assets/Resources/BiomeGrassPlace.compute#L144)
and near-field reads in [`GrassNearFieldPlace.compute`](../../Assets/Resources/GrassNearFieldPlace.compute#L275).

### R2-4. G avoids bake cost, but it is not free

`mapBake` is unaffected because G runs only in the terrain fragment shader, so it completely avoids C's measured
generation-time regression. The cost moves to every visible terrain pixel on every frame.

The live shader is already heavier than its old “48 taps worst-case” comment implies. With the current secondary
albedo path enabled, each active biome slot can perform 3 primary-albedo + 3 secondary-albedo + 3 normal + 3 ARM
array samples. Four slots at four atlas corners is therefore **up to 192 material-array samples per fragment**,
plus the ID/weight reads; a single-biome interior is about 48 material-array samples. ARM-alpha height adds no
texture samples, but it does add weight math, caching/register pressure, and potentially occupancy cost. A
dedicated height array adds up to 48 more samples in the worst case.

Do not use `mapBake` or the CPU-only `SurfaceVisibility` section to accept this shader change. Predict a GPU
regression, then compare matched captures using whole-frame GPU avg/p95 with valid `n`, at the same seed, pose,
resolution, and quality tier. Include both a broad border view and a terrain-heavy view.

Evidence: primary/secondary albedo sampling at [`PlanetVertexColor.shader`](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L339)
and the four-corner PBR calls at [line 541](../../Assets/Graphics/Shaders/PlanetVertexColor.shader#L541).

### R2-5. Placement and source recommendation

Apply G **inside `CornerTriplanarWeightedPbr`, before the four corner results are bilinearly combined**. After
that bilinear, the per-biome contributors have already been collapsed into one albedo/normal/ARM triple, so
there is no longer enough information to perform material competition. Per-corner shaping preserves the current
point-ID/manual-bilinear design and its continuity.

Recommended source order:

1. **Experiment: reuse the existing per-biome, stable world-space macro noise as the height proxy.** The
   production albedo path already evaluates a slice-offset `ValueNoise3D`; exposing that value to the two-phase
   accumulator can test the operator without a new texture array or authoring every material. Keep the mask
   smooth and world-space—do not use screen-space dither or a hard binary threshold.
2. **If the operator wins visually: author true height into ARM alpha.** This gives texture-correlated
   interlock with no additional texture fetches, but requires repacking and validating every ARM source plus
   the placeholder/import contract.
3. **Dedicated height array only if ARM alpha cannot be used** and profiling leaves room for the added taps.

The macro-noise probe is not proof that authored height will look the same; it is the cheapest decisive test of
whether patch-shaped material competition solves the observed border at all.

### R2-6. D ranking and recommended decision sequence

For iteration, rank **D2 > D3 > D1**. For the final shipped art, prefer **D3** once the desired values are known,
with D2 retained only if runtime/planet-specific variation is genuinely needed.

- **D2** targets only the offending biomes and is the best live-tuning instrument. The existing
  `TintColor`/`TintPercent` authoring data already reaches `BiomeDefinitionDto`, but texture-mode PBR ignores it;
  either make that existing contract explicitly affect production albedo or add a clearly named production
  multiplier—do not repurpose `_BiomeFlatColors`, which stores resolved flat colors rather than identity
  multipliers.
- **D3** has zero runtime cost and is the cleanest final source of truth. Retuning average lightness need not
  sacrifice saturation or texture detail, although it necessarily changes the endpoint palette somewhat.
- **D1** is last: one global operation cannot selectively fix the worst neighbor pairs and needlessly changes
  already-good biomes.

Recommended next experiment:

1. Establish a modest per-biome lightness correction with D2, preserving saturation.
2. Add a default-off, strength-controlled G probe using existing macro noise; capture linear vs G at the same
   border and at near/mid/far distances.
3. Accept G only if it improves the border at all three distances and the whole-frame GPU delta fits a declared
   budget.
4. If accepted, compare the proxy against one or two hand-authored ARM-alpha height pairs before repacking the
   full biome set. If rejected, stop at D2 and bake the chosen values into D3 where practical.

This sequence keeps the operator experiment reversible and answers the art question before committing to a
new height-data pipeline.

## Verified build plan — 2026-08-09 (Codex round-2 CONFIRMED against the tree)

All 4 round-2 claims **CONFIRMED** by 4 parallel adversarial agents (file:line evidence, zero refutations).
The agents surfaced three **critical caveats** Codex's prose glossed — carried into the steps below.

**Verified facts.** ARM = R:AO/G:rough/B:metallic; all 11 `*_ARM.png` have alpha ≡ 255 (no height today) but
the array is already RGBA32, so ARM-alpha height costs **0 extra fetches** (`BiomeDefinition.cs:27`,
`BiomeSurfaceTextureArrays.cs:66`, shader reads `.rgb` at `:450`). `TintColor`/`TintPercent` reach
`BiomeDefinitionDto` but production albedo **ignores** them; the tint LUT `_BiomeFlatColors` is **fully dead**
(never sampled) — don't repurpose it. The per-biome macro `ValueNoise3D` exists in `TriplanarSampleAlbedo:369`,
world-space + smooth + per-slice (`BiomeSliceOffset`), runs at default settings — a free height **proxy**.
`CornerTriplanarWeightedPbr` (`:480-519`) is the only place the top-4 are separated; cost ≈ **192 array
taps/fragment** worst case (12/slot × 4 slots × 4 corners), ~48 single-biome.

### ⚠ Critical caveats (verified, must honor)
1. **Current accumulation is NOT normalized** and hard-drops slots with `weight ≤ wEps=0.004` (`:490`). A naive
   "normalize then accumulate" at **strength 0 would shift colors** → the strength-0 path must reproduce the
   current un-normalized, `wEps`-thresholded sum exactly. **Gate any normalization behind strength > 0.**
2. The macro noise is **local** to `TriplanarSampleAlbedo` and sits **behind a feature gate** (`:364`,
   `_BiomeSecondaryBlend`/`_BiomeMacroVariationStrength`) + a debug early-out. Plumb it out (out-param) **and
   lift the compute above the gate** so the proxy stays valid when those look-dev uniforms are zeroed.
3. **ARM-alpha placeholder = 1.0 = max height.** Unauthored/placeholder slices would dominate a height blend →
   pick a **neutral height default** (e.g. treat missing height as 0.5) for placeholder + un-repacked slices.

### Phase 1 — D2: per-biome lightness correction (the durable base)
- Add a **new named production per-biome multiplier** (e.g. `_BiomeAlbedoTint[]`, default identity/white)
  applied to the sampled `SurfaceAlbedo` in `CornerTriplanarWeightedPbr` — **not** `TintColor` (repurposing it
  is surprising; it currently feeds only dead code) and **not** `_BiomeFlatColors`. Live-tunable via a console
  setter (`terrain.biome-tint <biome> <color>` or a lightness scalar).
- **Target: equalize biome mean *lightness*, preserve saturation** ("vibrant" ≠ different mean luminance). The
  measured offenders: dark-green grass ≈(0.17,0.21,0.04) vs bright-grey rock ≈(0.38,0.35,0.34).
- Default = no change (identity) → zero visual/perf risk until Bryan tunes. Bake the chosen values into the
  authored textures (D3) for the final ship; keep D2 only if per-planet variation is genuinely needed.
- **D-priority D2 > D3 > D1** (Codex): D1 global can't selectively fix the worst pairs.

### Phase 2 — G: height-blend probe (default-off, strength-controlled)
- Insert in the `CornerTriplanarWeightedPbr` **top-4 slot loop, before the `+=`** (never at the 4-corner
  bilinear — too late). Two-phase: (1) read active-slot `(id, weight, heightProxy)`; (2) derive
  `effectiveWeight[i]` (**== 0 iff `weight[i] ≤ wEps`** — preserve support), accumulate albedo+normal+ARM with
  the **same** effective weights (mirrors the current same-weight-for-all-three pattern).
- Height source = the macro `ValueNoise3D` **proxy** (caveat 2). `_BiomeHeightBlendStrength` uniform, **default
  0 = current linear exactly** (caveat 1).
- **Measure GPU, not `mapBake`** (all taps are per-fragment): whole-frame avg/p95, matched seed/pose/res/tier,
  a broad-border view + a terrain-heavy view.
- Terrain-material only — grass reads the atlas weights independently, unaffected (BB5/R2-3). Normal + ARM must
  follow G's effective weights too (or lighting disagrees with color).

### Sequence + gates
1. **Phase 1 (D2)** — modest per-biome lightness correction, saturation preserved. Capture `TerrainSelectedAlbedo`.
2. **Phase 2 (G probe)** — default-off; capture linear vs G at the same border at **near / mid / far**.
3. **Accept G only if** it improves the border at all three distances **and** the whole-frame GPU delta fits a
   declared budget. Update the stale cost comment at shader `:473`.
4. **If G accepted** → author real height into ARM alpha (with caveat-3 neutral default), compare proxy vs 1–2
   hand-authored pairs before repacking all 11. **If rejected** → stop at D2, bake into D3.

All visual → grass-scene + main-planet capture-diff (`TerrainSelectedAlbedo`) and **Bryan's F10 sign-off**.
