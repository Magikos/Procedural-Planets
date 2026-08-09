# Biome border seamlessness — options + plan (2026-08-09)

Status: **investigation done, options for review.** For a Codex second opinion before any code. Bryan wants the
biomes to **seamlessly blend together**; walking the surface he still sees **drastic border colors** (vivid-green
grassland meeting tan savanna meeting grey mountain over a short distance).

This doc is self-contained. It states what was verified against the code, what was measured, what is still
uncertain, and four fix options with tradeoffs. **Question for the reviewer at the bottom.**

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
