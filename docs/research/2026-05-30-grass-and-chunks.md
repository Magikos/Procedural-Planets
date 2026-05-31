# Research notes — Chunks + Grass + Surface details

**Date:** 2026-05-30
**Branch:** `phase8-spawning-foundation`
**Purpose:** Capture findings from reference material before drafting the design doc. Each section answers specific design questions so the final design can cite back to evidence.

## Project requirements (from Bryan, locked in)

- Per-biome **tunable grass density** (Tropical/Forest lush, Tundra/Desert sparse, etc.).
- Grass is a **GPU compute shader** — modern instanced approach, not CPU instancing.
- Grass **samples terrain colors** so it tints to match underlying biome.
- Grass is **affected by the weather wind system** (existing `_WindDirection` + `_WindSpeed` globals).
- Need to **flatten / pave / clear grass** under paths and structures; **slow regrowth over time** (paved/structured stays permanent in a separate state).
- **Full quadtree LOD chunk system** (Sebastian Lague style — match `local-only/LOD-Planets-in-Unity-master`).
- Biome textures (per-biome surface textures) should land alongside, since grass samples whatever the surface produces.

## Design questions to answer from references

1. **Chunk lifecycle** — how is async subdivision handled? Seam fixing? Memory budget?
2. **Per-chunk grass data** — what does the chunk store? Density map, blade positions, modification mask?
3. **GPU dispatch shape** — one compute per chunk? Per-frame regeneration vs persistent buffers?
4. **Wind animation pattern** — vertex shader sine + per-blade phase? Gerstner-style? Texture-based motion?
5. **Modification system** — per-blade boolean? Density grid? RG mask texture? How is it indexed?
6. **Regrowth model** — timed lerp back to "alive"? Tied to weather/time of day?
7. **LOD on grass itself** — full blades near, impostors mid, none far? How does it cross chunk LOD boundaries?
8. **Biome texture blending** — primary/secondary in shader? Splatmap? Triplanar?

---

## Reference: rendering_countless_blades_waving_grass_unity_guide.md

Adaptation of GPU Gems Ch.7 to spherical planets. **GPU-Gems-era thinking** — favors crossed-card clusters with alpha textures, not individual compute-built blades. Useful as the floor of what we should support; the newer JAHRMANN/GoT references will likely favor full blade geometry via compute.

**Key takeaways for our system:**

- **Geometry:** 3 crossed quads per "cluster" + alpha-tested textures + vertex wind. Cheap, view-angle robust, gives volume illusion. **Compatible with our compute path** — compute can place clusters instead of individual blades for the far-LOD; near-LOD uses real blades.
- **Planet-local up is mandatory:** `float3 up = normalize(positionWS - _PlanetCenterWS);` and **project wind into tangent plane:** `tangentWind = normalize(_WindDirectionWS - up * dot(_WindDirectionWS, up));`. Without this the grass tilts toward world Y on the far side of the planet.
- **Root locking is non-negotiable:** `heightFactor = pow(saturate(localY / bladeHeight), _WindBendPower);` so bottoms stay fixed and tops bend. Common bug failure mode without it: grass "slides" along the wind direction.
- **Wind layering:** gust (large, slow) + medium sway + flutter (small, high-frequency) — additive in vertex shader. Single sine looks robotic.
- **Spatial decorrelation:** `phase = dot(worldPos.xz, float2(12.9898, 78.233)) + instancePhase;` to prevent synchronized motion. Per-instance random phase is the safe default.
- **Suggested per-blade instance data** (~24-32 bytes — small enough for StructuredBuffer at high counts):
  ```csharp
  struct GrassInstance { Vector3 position; Vector3 normal; float rotation; float scale; float colorVariation; float windPhase; int grassType; }
  ```
- **Placement filter** (worth lifting into our spawner):
  ```csharp
  bool CanPlaceGrass(slope, altitude, moisture, biome)
      => biome.SupportsGrass && slope < MaxSlope && moisture > MinMoisture && altitudeInRange;
  ```
- **LOD ladder:** Near = full instanced cards, Mid = fewer/larger clumps, Far = terrain color/normal only, Very far = biome texture only.
- **Shadow LOD policy:** Near = cast + receive, Mid = receive only, Far = none.
- **Interaction is forward-flagged** (player push, explosions, vehicle tracks). Author recommends "compute shader displacement buffers" — i.e. a separate state texture/buffer the compute writes to and the grass shader reads from. That's the modification path.

**Open questions this raised:**

- Crossed cards vs compute-built blades vs Jahrmann's tessellation — pick based on the next two references.
- Is `_GameTime` (already a Unity built-in) sufficient, or do we want a custom time that pauses during loading? (Currently using `_Time.y` in cloud shader.)
- For our planet-tangent wind projection, the project's existing `_WindDirection` global is world-space — projection into tangent has to happen per-vertex in the grass shader. Fine.

## Reference: JAHRMANN-2017-RRTG-draft.pdf

**This is essentially the spec.** Klemens Jahrmann + Michael Wimmer (TU Wien), I3D 2017. "Responsive Real-Time Grass Rendering for General 3D Scenes." Open-source demo: https://github.com/klejah/ResponsiveGrassDemo (OpenGL/C++).

### Why this is THE reference for our system

The paper explicitly tackles **arbitrary 3D models, not just height maps**. That's our cube-sphere planet — we'd be the canonical use case. Each blade is a real geometric object with per-blade physics (gravity, wind, collisions), evaluated entirely on GPU via compute shaders + indirect rendering. No CPU-GPU round-trips per frame. This matches Bryan's stated "grass should be a compute shader" requirement exactly.

### Per-blade data model — 4× float4 (64 bytes)

A blade is a quadratic Bézier curve with three control points:

| Vertex | Role                                                           |
| ------ | -------------------------------------------------------------- |
| `v0`   | Root, fixed in place.                                          |
| `v1`   | Mid control point — derived from `v2` to maintain curve shape. |
| `v2`   | Tip — moved by forces (gravity, wind, collisions).             |

Plus per-blade attributes: **height, width, stiffness coefficient, up-vector, direction angle (alignment on the local plane)**. Packed into 4× float4 total.

At 64 bytes × 100,000 blades = ~6 MB per chunk. Very reasonable.

### Per-frame compute pipeline (this is the architecture)

1. **Physical-model compute** — evaluates forces per blade, updates `v2`, validates state, writes to "force map" texture (each blade owns one texel; force map persists across frames for collision strength η).
2. **Culling compute** — runs 4 tests per blade, atomically increments visible-count, writes surviving blade indices to an index buffer + indirect args buffer.
3. **Indirect render** — `DrawIndirect` reads the count from the args buffer. Tessellation evaluation shader builds the blade geometry from the Bézier control points.

All three phases run on GPU. CPU just dispatches the compute and submits the draw call.

### Force model — directly maps to our requirements

```
δ = (recovery + gravity + wind) * Δt + collision_displacement
```

- **Recovery** = `(v2_initial - v2) * stiffness * max(1 - η, 0.1)` — restoring force toward upright pose; suppressed by collision strength η so a crushed blade stays down longer.
- **Gravity** — environmental (global down OR toward a point) + "front gravity" (bend tip orthogonal to blade width for natural elasticity).
- **Wind** — analytic function returning `w_i(v0)` (direction + strength at the blade's root position), multiplied by alignment factor `θ = directional_alignment * height_ratio` so erect blades catch more wind than bent ones, and parallel-to-wind blades feel less force than perpendicular.

### Force map texture = the persistence + regrowth mechanism

Each blade has one texel in a 2D "force map" (R,G,B = δ translation, A = collision strength η). Each frame:

```
η = max(η_previous - a * Δt, 0)        // fade over time (constant `a` = decay rate)
if collision detected:
    η += squared_collision_displacement // accumulate
```

**This is exactly Bryan's "slow regrowth" mechanism**, already designed. The decay rate `a` directly controls how fast flattened grass returns. For paved/structured areas (permanent), we'd add a **separate state** (a "permanent modification" mask) that overrides `a`, preventing decay back to 0.

### State validation (after each force pass)

Three steps to keep a blade in a valid configuration:

1. **Ground clamp** — `v2 = v2 - up * min(up · (v2 - v0), 0)` keeps the tip above the local plane.
2. **v1 derivation** — `v1 = v0 + h*up * max(1 - lproj/h, 0.05*max(lproj/h, 1))` where `lproj` is the projected length of (v2 - v0) on the ground plane. Ensures the curve always has slight curvature.
3. **Length correction** — Bézier curve length approximation: `L = (2*L0 + (n-1)*L1) / (n+1)` where `L0 = |v0 - v2|`, `L1 = |v0-v1| + |v1-v2|`. Scale control points by `r = h / L` so the curve length matches the blade height. **This was the key improvement over the 2013 predecessor paper.**

### Collision model

Spheres as colliders (complex objects sphere-packed). For each blade, test two points: `v2` and the curve midpoint `m = 0.25*v0 + 0.5*v1 + 0.25*v2`. If a point is inside a sphere, push it to the nearest surface point.

For our use case the player + structures could be approximated as a small set of spheres (capsule = 2-3 spheres). Dispatch a compute pass per frame that pushes the active collider set into a buffer the grass-physics compute reads.

### Culling — 4 tests, all in compute

1. **Orientation** — `0.9 > |dir_camera · dir_blade_width| → cull`. Essential because compute-grass has no thickness; near-parallel blades sub-pixel and alias.
2. **View frustum** — test (v0, m, v2) in NDC with small tolerance.
3. **Distance** — `id mod n < n*(1 - dproj/dmax) → cull`. Critically, this **assumes nearby blades have similar indices** so culling produces evenly distributed survivors, not bare patches. Patch generation in preprocessing handles this (balanced k-means clustering by proximity, then lexicographic sort).
4. **Occlusion** — test (v0, m, v2) against scene depth texture with small bias.

### Measured performance (2017 GTX 780M)

- Nature scene: **397K blades total, 43K rendered after culling, 123 FPS** (8.1 ms frame). 75% culled.
- Helicopter scene (worst case, no occluders): 900K blades, 165K rendered, 56 FPS.

Modern desktop GPUs will trivially exceed this. Bigger numbers (millions of blades per chunk) are realistic.

### Tessellation rendering

Initial quad in (u, v) tess parameters. The tessellation evaluation shader:

1. De Casteljau evaluates the Bézier at `v` → curve point `c` + tangent `t0`.
2. `c0 = c - w*t1`, `c1 = c + w*t1` where `t1` is blade-width direction.
3. Final vertex position = interpolated between c0 and c1 via shape-specific `t(u, v)`:
   - Quad: `t = u`
   - Triangle: `t = u + 0.5v - uv`
   - Quadratic (parabola one side): `t = u - uv²`
   - Triangle-tip (quad bottom, triangle top): `t = 0.5 + (u-0.5)*(1 - max(v-τ, 0)/(1-τ))`
   - Dandelion: heuristic trig function with tessellation-level input
4. Optional 3D V-displacement: `d = w*n*(0.5 - |u-0.5|*(1-v))` — creates a V cross-section so blade has thickness; unfolds width by √2.
5. Width correction at distance — force quad shape when blade < ~3 pixels wide to prevent tipped-shape aliasing.

### Patch generation (preprocessing)

The paper clusters blades into patches via **balanced k-means** by spatial proximity. Each patch has bounding box → first culling step (frustum tested against patch box). Blades within each patch are **lexicographically sorted** so nearby blades have similar indices (required for distance-culling to produce even survivor distribution).

**For us:** chunks ARE the patches. No separate clustering needed. Within each chunk, blades should be sorted by position (Morton order would be ideal; lex is acceptable).

### Open-source reference impl

https://github.com/klejah/ResponsiveGrassDemo — OpenGL 4.5 + C++. Algorithm is engine-portable. Worth fetching the repo or its source files for line-by-line study when implementing our Unity version.

### Direct application to our design

| Bryan's requirement          | JAHRMANN equivalent                                                                                                                        |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| GPU compute shader rendering | Indirect rendering + per-blade compute (paper's core contribution)                                                                         |
| Per-biome tunable density    | Pre-generate blades per chunk with density driven by `BiomeDefinition.GrassDensity`, sample biome map at root position                     |
| Sample terrain colors        | Per-blade `colorVariation` attribute set from sampled biome color at root                                                                  |
| Wind from WeatherManager     | Wind = `w_i(v0)` — feed our `_WindDirection` + `_WindSpeed` globals into the analytic function; project into tangent plane per planet `up` |
| Flatten/cut grass            | Force map η accumulation — exactly the paper's collision-strength mechanism                                                                |
| Slow regrowth                | η decay rate `a` controls how fast flattened grass restores                                                                                |
| Permanent paved/structured   | Separate "permanent modification mask" overlay that pins η at max for those texels                                                         |
| Arbitrary planet surface     | Paper's explicit "general 3D scenes" support — `up` is per-blade, not world-Y                                                              |

### Open questions this raised

- **Hardware tessellation in Unity URP 17?** Need to verify URP supports tess in custom passes. (Built-in pipeline supported it cleanly; URP is more restricted around custom render features.)
- **Per-chunk grass generation** — paper does it as one-shot preprocessing. For us, each chunk runs its own generation when it loads. Worth measuring.
- **Cross-chunk wind sync** — wind is analytic per-position, so two adjacent chunks see consistent wind without coordination. Good.
- **Sphere-packing of structures/players** — need a simple sphere-collider provider in our project. Player capsule ≈ 2-3 spheres, building footprint = bounding sphere.

## Reference: gdc_2021_procedural_grass_in_got.pdf

**Eric Wohllaib, Sucker Punch — GDC 2021 — "Procedural Grass in Ghost of Tsushima"**

Production-shipped technique on PS4. ~5 sections: compute shader, data pipeline, vertex shader, pixel shader, miscellaneous (velocity / fields / shadows / future). This is JAHRMANN refined into shippable form — same per-frame compute → indirect-draw architecture, but with **dozens** of production fixes for clumping, LOD popping, specular aliasing, shadow cost, and arbitrary-surface support. Treating this as our **second canonical spec** alongside JAHRMANN; the two combined cover almost everything Bryan asked for.

### Architectural choices that change how we should think about our system

#### 1. Blades are NOT stored — they're computed from lane ID per frame

This is the biggest insight in the talk and a major correction to my earlier assumption.

```
compute1: lane_id → tile_grid_position → jitter → (cull) → grass_type + height + clump → blade data
```

Each "lane" in the compute dispatch maps deterministically to one potential blade position. Distance + frustum culling **drop** lanes that don't survive. Surviving lanes write their blade attributes to an `instance_data` buffer and atomically bump `blade_count`. **No persistent per-blade memory.** Blade attributes are functions of `(chunk_id, lane_id, time)` only.

**Implications for our chunk system:**

- Chunk doesn't need a "blade buffer" — it needs a **density/clump field** and a **modification mask** (force map). The compute regenerates blades from these inputs each frame.
- This blows up memory for grass to ~zero per chunk (just a few small textures). We can have **far more active chunks** than I was budgeting.
- Bryan's "slow regrowth" mechanism still lives in the force map (per-position texel, persistent), not per-blade. That actually maps even cleaner — the modification is positional, not blade-based, which is how players think about it ("I flattened _this spot_").

#### 2. Procedural Voronoi clumping kills the "billion identical blades" look

The blue/green clump shots (slides 12–13) are the same patch with clumping isolated as a color. Clump algorithm (slide 16 shows the Voronoi-style scatter): each sample point's nearest-neighbor cluster centers define which clump it belongs to. Each clump stores:

- **Clump facing** (2 floats) — all blades in the clump bend in the same direction
- **Clump color** — base color shared across the clump

Slides 17–21 show the same hillside with clumping added, then clump-color variation, then clump-facing variation. Each step removes a layer of synthetic-looking uniformity. The final shot looks like real grass.

**Why this matters for us:** real grass clumps because individual plants share microclimate (moisture, sunlight, neighbors crowding for sun). Voronoi clumping produces this read for free, **AND** we get a natural place to inject biome color variation: clump base color sampled from biome color map, individual blade adds small noise. So "grass samples terrain colors" — Bryan's requirement — is done at the _clump_ level, not per blade.

#### 3. Vertex-sliding LOD instead of impostor crossfade

Slides 24–27. Two LODs: **High = 15 vertices, Low = 7 vertices**. The transition is done by **sliding vertices together** (the X'd corners in the 4-quad diagram). Vertices in the higher LOD that don't exist in the lower LOD slide along their neighbor's edge until they overlap their target neighbor, then disappear when the merge completes. Slide 27 shows the LODs side-by-side: high has more cross-bars subdividing the strip; low has just the silhouette triangulation.

This is **way cleaner than crossfade.** No popping, no double-rendering during transition, no alpha-sorted overlap. Worth implementing from day one because retrofitting later means changing the mesh-generation logic in the tess/vertex shader.

#### 4. Cubic Bézier curves for blade shape (extends JAHRMANN's quadratic)

GoT uses cubic Bézier (4 control points), JAHRMANN uses quadratic (3 control points). Cubic has:

- Easy position evaluation
- Easy derivative evaluation (→ normal direction)
- **Two shape parameters per blade:** `tilt` controls the endpoint angle, `bend` controls the midpoint offset (slide 29)

So per-blade variation gets richer. Both papers agree the derivative-based normal is mandatory for shading.

**Our choice:** quadratic is simpler and JAHRMANN's reference repo uses it. Start with quadratic, upgrade to cubic later if blade shape variation is too limited.

#### 5. Per-vertex shading details that make blades look "real"

Three orthogonal fixes (slides 33–37):

- **Rounded normals across blade width** (slide 33). Flat normals make a blade look like a flat strip; rounding the normal across the width interpolates it like a tube cross-section. Cheap, dramatic improvement in shading.
- **Glancing-angle width adjustment** (slides 34–35). At near-horizontal view, single-pixel-wide blades alias hard. Widen blade at glancing angles so silhouette stays solid. (Slide 35 shows the field looks denser after the fix.)
- **Clump normal for specular AA** (slides 36–37). Per-blade normals → speckled specular highlight that aliases when wind moves the blade. Use the _clump's_ average normal for specular only. Diffuse stays per-blade. Eliminates the sparkle.

All three are pure shader changes — no extra data, no extra geometry. We should bake them into our shader from the start.

#### 6. Shadows — never per-blade

Slides 45–47. Per-blade shadow casting is unaffordable. GoT uses:

- **Shadow imposters using terrain + depth-dither offset** — fake self-shadowing by reading the terrain shadow and dithering by depth. Slide 45 shows the imposter approach; slide 47 shows the resulting visual: grass has plausible shadow gradient at its base without rendering blade geometry to the shadow map.
- **Screen-space shadows** for the actual grass — uses depth buffer to compute coarse self-shadowing per-screen-pixel rather than per-blade.

Aligns with GPU Gems guide's policy (cast at near-LOD only, never cast at distance) but goes further by saying: even near, don't cast per-blade; fake it from terrain + screen-space.

#### 7. Wind = scrolling Perlin texture, not per-blade analytic

Slide 31 shows the wind debug visualization — a low-res 2D wind field colored by direction/strength, scrolling across the terrain. Each blade samples this texture at its root position to get wind strength + direction.

This is **different from JAHRMANN** (which uses an analytic wind function per blade). The texture approach has big advantages:

- **Spatial coherence is free** — neighbors see neighbor wind, so gusts roll across the field as a wave.
- **Custom wind shapes are easy** — paint a texture for a storm gust, scroll it across.
- **Cheap sampling** — single texture read vs trig math.

**For us:** our `WeatherManager` already does spherical weather grid evaluation. We have a sphere-tiled wind grid available — we feed it into a 2D wind texture per chunk (or globally with planet-local UVs) and grass samples it. Aligns with Bryan's "grass should be affected by the weather wind system."

#### 8. No motion vectors for TAA

Slide 39: "we don't have velocity." GoT explicitly skips writing per-blade motion vectors for TAA. The presenter glosses over why — likely the cost wasn't worth it; small high-frequency motion in TAA forgives the missing vectors. **For us:** URP TAA is opt-in and we're not currently using it; non-issue.

#### 9. Pipeline overlap (data-pipeline section, slide 23)

Multiple instance-data buffers in rotation. While compute1+compute2 fill buffer A for batch N, vertex+pixel are still rendering buffer B from batch N-1. Hides compute latency behind rendering of the previous batch.

**For us:** Unity's command buffer model already does some of this implicitly when we submit dispatches and draws in the same frame. Worth being aware of when we measure — if compute and render are serializing, double-buffering blade data is a known fix.

#### 10. Far LOD = baked texture on terrain (slides 42–43)

Confirms the GPU Gems guide's "very far" tier. When a chunk is far enough that individual blades sub-pixel, the grass is **baked into the terrain texture** — a per-chunk color/normal map blended into the ground shader. Camera approaches → texture fades down as actual blades fade in.

This integrates cleanly with our biome texture work (Phase B). The biome texture system would need a "grass-overlay" channel that's baked from the same density/clump field the compute uses. One source of truth, two output paths (compute + texture).

#### 11. Grass physics = limited collision area (slide 44)

The turquoise wireframe in slide 44 is the **collision mesh for grass** — limited to a small area around the camera/player. Outside that radius, no collision is evaluated.

**For us:** matches JAHRMANN's collision model exactly. Sphere colliders from player/structures only need to be active near the camera. Compute dispatch can early-out for blades outside the active radius.

#### 12. Open future-work problems (slides 48–50)

- **Arbitrary surfaces** — cliffs, complex geo, etc. Slide 48 shows grass struggling at a vertical drop (visible bare strip on cliff face). They flag this as unsolved; for our planet this matters because mountain slopes can be steep. Solution likely involves projecting along the surface normal differently or skipping placement above a slope threshold.
- **Artist-authored vertices** — for special foliage (ferns, slide 49). Not relevant near-term; we can use full meshes for special plants when we get there.
- **Better LOD vertex distribution** — slide 50 shows an 8-vertex vs 4-vertex LOD pair, suggesting they're still iterating on the slide pattern.

### Direct application to our design (additions to the JAHRMANN mapping table)

| Bryan's requirement       | GoT addition                                                                                                          |
| ------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| GPU compute shader        | Lane-ID-driven placement, no persistent per-blade buffer — major memory win                                           |
| Sample terrain colors     | Clump-level base color sampled from biome map, per-blade noise on top — eliminates uniform-color tells                |
| Wind from WeatherManager  | Scrolling 2D wind texture sampled at blade root, fed from our existing weather grid                                   |
| Per-biome tunable density | Density driven by biome map + clump field; both per-chunk inputs                                                      |
| Slow regrowth             | Force map (η) still the mechanism, but it's positional, not per-blade — fits the "lane ID = position" model perfectly |
| Avoid LOD popping         | Vertex sliding (15→7 vertex transition) instead of impostor crossfade                                                 |
| Specular doesn't sparkle  | Clump-normal for specular AA                                                                                          |
| Shadows affordable        | Shadow imposter + screen-space, never per-blade in shadow map                                                         |
| Far LOD                   | Baked grass overlay on terrain texture (integrates with biome texture work)                                           |

### Open questions this raised

- **Voronoi clump field generation** — does Unity have decent Worley/Voronoi noise in a compute-friendly form, or do we roll our own? (We already have noise filters in `Assets/Scripts/Planet/NoiseFilters` — likely yes.)
- **Lane-ID density** — what's our blade density target? GoT runs ~hundreds of thousands per visible region on PS4 hardware. We're on PC (Bryan's `QualityLevel: 0 (PC)`) — millions per chunk is feasible. Need to measure.
- **Wind texture resolution + scroll rate** — needs to match WeatherManager's spherical grid resolution. WeatherManager runs at low Hz; we'd interpolate.
- **URP tessellation support** — same question as JAHRMANN. URP 17 is the latest; need to verify HDRP-only restrictions don't apply.
- **Permanent modification vs decay** — paved/structure modifications need to override the η decay. Probably easiest as a separate "permanent mask" channel in the force map alpha; if `permanent_mask = 1`, skip decay.

## Reference: CWD-Sim_Real-Time_Simulation_on_Grass_Swaying_with.pdf

**Choi & Sung, Keimyung University — Applied Sciences 2024 — "CWD-Sim: Real-Time Simulation on Grass Swaying with Controllable Wind Dynamics"**

Academic paper, OpenGL/GLSL 4.5 reference impl, no public repo cited. Where JAHRMANN/GoT optimize geometry and culling, CWD-Sim optimizes the **wind field itself** — replaces analytic wind / sampled noise textures with a real-time **2D Navier–Stokes fluid simulation**. Demonstrated 7M blades at 29 fps on an RTX 2080 with a 1000×1000 fluid grid. Also worth noting their **quadratic deformation equation** (alternative to Bézier), and their **height-based self-shadow** trick.

### The key idea — fluid-simulated wind, group-of-blades dispatch

Rather than modeling wind per blade or sampling a precomputed texture, they run a **stable-fluids Jos-Stam-style Navier–Stokes solver** every frame on a 2D grid covering the world. The resulting velocity field drives all blades. **All blades in the same grid cell receive the same wind force**, decoupling blade count from wind cost.

```
fluid grid (1000×1000) — Navier–Stokes velocity field per cell
↓ sample at blade root world position
↓ apply identical force to every blade in that cell
↓ visual variation comes from per-blade initial properties (height, rotation, direction)
```

The performance table is striking: at 1M blades wind sim is **5.9 ms (constant)**, grass simulation **0.1 ms**, rendering scales linearly. The fluid sim has fixed cost regardless of blade count.

### Why this matters for our system

**JAHRMANN evaluates wind per blade analytically.** **GoT samples a scrolling Perlin texture.** **CWD-Sim simulates the wind as actual fluid.** Each is a tier up in realism + designer expressiveness, with corresponding cost. Our `WeatherManager` already has a spherical grid that runs slow physics-ish updates — CWD-Sim's pattern argues for **escalating its role from "current weather state" to "active wind velocity field"** and feeding that into grass.

The most interesting consequence: **wind interacts with obstacles**. The paper shows wind bumping around rocks (Figure 1), two wind streams colliding and deflecting (Figures 10–11), arrow-painted tree-structured gusts branching across the terrain (Figure 8). None of this is achievable with a scrolling texture.

For our planet:

- 2D fluid grid in **planet-tangent space** (per face, or per chunk region — see open questions).
- WeatherManager's existing wind direction = boundary/seed input to the fluid sim.
- Storm cells = strong velocity injections at specific positions.
- Lightning/pressure events = local force splats.
- Mountains and structures = solid-cell boundaries the fluid flows around.

### Navier–Stokes solver — what we'd actually implement

Standard Jos Stam stable-fluids 6-step pass on a 2D velocity grid `V[i,j] ∈ R²`:

| Step                     | Equation                                                        | Purpose                                                        |
| ------------------------ | --------------------------------------------------------------- | -------------------------------------------------------------- |
| 1. Curl                  | `C[i,j] = V[i+1,j] - V[i-1,j] + V[i,j+1] - V[i,j-1]`            | Measure rotation per cell                                      |
| 2. Vorticity confinement | `f = (C[i,j+1]-C[i,j-1], C[i+1,j]-C[i-1,j]) · λ; V' = V + f·Δt` | Restore swirls damped by numerical diffusion. λ ≈ 50 in paper. |
| 3. Divergence            | `D[i,j] = (V'[i,j+1]-V'[i,j-1] + V'[i+1,j]-V'[i-1,j])/2`        | How much fluid leaves each cell                                |
| 4. Pressure projection   | `P[i,j] = (P[i,j+1]+P[i,j-1]+P[i+1,j]-P[i-1,j]-D[i,j])/4`       | Iterative Jacobi solve for pressure                            |
| 5. Subtract gradient     | `V'' = V' - (P[i+1,j]-P[i-1,j], P[i,j+1]-P[i,j-1])`             | Enforce incompressibility (∇·V = 0)                            |
| 6. Self-advection        | `α' = α - V''·s·Δt; V''' = V''[α']/(1+λ·Δt)`                    | Transport velocity along itself; small damping                 |

All steps map cleanly to compute shaders — Jacobi iteration in step 4 is the only multi-pass piece. Reference impls cited in their paper: Pavel Dobryakov's WebGL Fluid Sim, Harris's GPU Gems Ch.38, haxiomic's cross-platform fluid experiments.

### Quadratic deformation — drop-in alternative to Bézier

Their key cost-saving claim is the **quadratic equation as a stand-in for cubic Bézier**:

```glsl
// Gravity-only deformation of a vertex at local position P:
P'.x = P.x
P'.y = P.y - k1 · P.y²    // bend downward, more at the tip
P'.z = P.z + k2 · P.y²    // displace along the resting bend direction

// k1 = 0.05, k2 = 0.1 in their experiments
```

Then wind translation `T` is computed from the fluid velocity `V'''` at the blade's cell, weighted by elevation squared:

```glsl
T = F · (V'''.x · P'.y²,
         -|V'''| · P'.y²,    // downward sag scales with horizontal wind magnitude
         -V'''.y · P'.y²)
```

Final vertex position: `P'' = M · ((1-λ)·T + λ·P')` with `λ = 0.2` (small λ = wind dominates, large λ = resting shape dominates).

**Their numbers:** quadratic vs cubic Bézier at 10K vertices = **13 ms vs 75.8 ms (~83% faster)** with visually indistinguishable curves (Figures 4–5 show the green quadratic curve overlaid on the red dotted Bézier — they're identical to the eye).

**For our system:** this is a **third option in the blade-shape spectrum**:

- JAHRMANN: quadratic Bézier (3 control points) — physical (gravity, wind, collisions update v2; v1 derived; state validation).
- GoT: cubic Bézier (4 control points) — `tilt` + `bend` per blade.
- CWD-Sim: scalar quadratic offset — no control points at all, just `y² ` curvature scaled by k1/k2.

The CWD-Sim approach is the cheapest but loses the physical state interpretation (no "tip position" to validate or collide against — the blade is procedurally derived from `y`). **Recommendation:** stick with JAHRMANN quadratic Bézier — we need the v2 endpoint for collisions and the force map. CWD-Sim's deformation is a good fallback if tess perf is bad on URP.

### Height-based self-shadow — almost free, surprisingly good

Slide 6 + Figure 7 show the trick: per-vertex color is darkened proportional to how much the blade has bent (lower deformed y = darker). The reasoning: a flattened blade is more likely to be obscured by neighbor blades.

```glsl
c_final = c_diffuse · clamp(P''.y - |F|·c1 + c2, m_min, m_max)
```

Where `c1, c2` are tuning constants and `m_min, m_max` bound the brightness. Total cost: one multiply per vertex, no shadow map.

The before/after (Figure 7a vs 7b) is dramatic — without shadow the field looks like a flat green sheet; with shadow you see ripples and depth as wind crosses.

**For our system:** we should layer this on top of GoT's shadow imposter + screen-space shadow approach. It's free at the per-vertex stage and gives micro-occlusion between blades that no shadow map could afford.

### Grouping by world position — wind cost decoupled from blade count

```glsl
G = (P.x/w + 0.5, P.z/h + 0.5)   // map blade root to grid cell
```

`w, h` are world-space cell dimensions. They state at 200×200 grid resolution visual quality is still acceptable — so for **modest cell counts the wind sim itself is sub-millisecond.** This grouping is the architectural reason their performance is constant at 5.9 ms regardless of blade count.

**For us:** the spherical version is non-trivial because we don't have flat x,z. Options:

1. **Per-cube-face 2D grid** — six 1024×1024 fluid sims, one per planet face. Boundary handling at face edges is tricky (velocity needs to be remapped across face seams).
2. **Per-chunk local grids** — each top-level chunk runs its own small fluid sim. Cleaner boundaries (chunk is a square in face-local space) but no cross-chunk wind continuity.
3. **One global grid mapped to lat/lon** — distorts at poles but matches WeatherManager's existing spherical grid topology. Use as **wind coupling layer** rather than per-blade source.

Option 3 + per-chunk sampling is probably the right answer for v1, escalating to per-face fluid sims if we want the gust-deflection-around-mountains effect.

### AGC (Arrow-Guided wind flow Control) — designer tool, not gameplay

A scene-editor interface where designers place arrow root + endpoint pairs on the terrain. Each arrow injects a force into the fluid sim; arrows can branch (tree structures) for complex gusts. Cool for offline scene authoring, **not directly applicable for our procedural planet** — we don't have authored terrain to paint wind on.

What we _can_ steal from this idea: **per-biome wind characteristics**. Plains = strong sustained linear gusts; forest = damped + redirected; storm cell = swirling vortex. Inject these as fluid forces based on biome map sampling.

### Limitations & open issues they flag

- **2D only** — can't reproduce 3D vortices, dust devils, etc.
- **No blade–blade collision** — flagged as future work. Compounds with the height-shadow trick (which fakes inter-blade occlusion visually but not physically).
- **Strong wind = flat dark grass** — at high wind the height-shadow darkening dominates and grass goes black. They tried periodic cosine/sine clamping; didn't help. We'd want a max-bend clamp + a max-darkening clamp.
- **No LOD** — flagged as future work. We have it from JAHRMANN/GoT.

### Direct application to our design (additions to the JAHRMANN + GoT mapping table)

| Bryan's requirement            | CWD-Sim addition                                                                                                                            |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Wind from WeatherManager       | Upgrade path: WeatherManager → 2D Navier–Stokes velocity field → grass wind. Reactive to obstacles, weather cells, storm pressure.          |
| GPU compute shader             | All 6 fluid sim steps are compute-shader-friendly; reference impls (Dobryakov, haxiomic) we can port.                                       |
| Slow regrowth / persistent mod | Quadratic vertex shading shadow term is a free micro-shadow for blade-on-blade occlusion that complements the force-map persistence.        |
| Per-biome density / character  | Bias the fluid sim per biome (plains = strong linear, forest = damped, etc.) by injecting biome-aware forces during fluid update.           |
| Performance scaling            | Decoupled wind cost: 1000×1000 grid sim is ~5.9 ms flat, independent of blade count. Grid resolution is the knob to scale, not blade count. |

### Open questions this raised

- **Spherical fluid sim topology** — per-face vs per-chunk vs global lat/lon grid. Decision deferred to design doc; per-chunk is the safe starting point.
- **Coupling with WeatherManager** — what frequency does the fluid sim refresh boundary inputs from weather? Probably every WeatherManager tick (low Hz), interpolated between ticks.
- **Static obstacle injection** — mountains, structures, oceans block flow. Need a solid-cell mask derived from terrain heightmap (or biome map for "is water").
- **Storm + lightning integration** — should storms inject vortices? Lightning = pressure shock? This is gold-plating but the architecture supports it.
- **Cost on Bryan's PC quality target** — at QualityLevel 0 (PC), 1000×1000 fluid is plausible. For lower tiers, fall back to GoT-style scrolling Perlin texture sampled from WeatherManager wind direction.

## Reference: Interactive-Grass-Shader-main project

**Small Unity URP Shader Graph demo — single-collider interactive grass.** Three short C# scripts (Grass.cs, GrassTest.cs, Movement.cs — all under 50 lines each) plus a Shader Graph (`GrassShaderGraph.shadergraph`, ~5100 lines of JSON). Useful as a counter-example for what we should **not** do, and as a quick reference for the simplest possible interactivity pattern.

### What it actually does

- `Grass.cs` listens on `OnTriggerEnter`/`OnTriggerExit` for entities entering a grass area, stashes the most recent entity transform in a list, and **every FixedUpdate writes its world position to material property `_Pos`** via `grassMat.SetVector("_Pos", entities.Last.position)`. When the list empties, `_Pos.y` slowly increments back upward so the deformation visually fades (50 units over ~20K fixed frames).
- `GrassTest.cs` is applied to individual props (rocks, sticks); on trigger they rotate away from the colliding entity by `Quaternion.AngleAxis(180/distance², perpDir)`. Lerps back to identity on exit.
- `Movement.cs` is a 30-line WASD/random-walk controller for the demo entity. Not interesting.

### Shader Graph properties (from grep)

| Property                    | Meaning                                                                               |
| --------------------------- | ------------------------------------------------------------------------------------- |
| `_Pos`                      | Last collider's world position — drives the displacement region                       |
| `WindSpeed`, `WindStrength` | Scalar wind tuning, applied via `Time` × `Gradient Noise` × `Tiling And Offset` chain |
| `RotateAmount`              | Wind sway magnitude                                                                   |
| `AffectDistance`            | Radius around `_Pos` within which displacement happens                                |
| `maintex`                   | Albedo texture                                                                        |

The graph has a "Displacement Sub Graph" node — likely computes `distance(vertexWorldPos, _Pos) < AffectDistance` and offsets vertex position downward/outward inside that radius. Wind is a standard `Gradient Noise` × `Time` × `RotateAmount` pattern fed into vertex displacement.

### Why this is the wrong shape for our planet

- **Single collider only.** Material property `_Pos` is a single Vector3 — only the most recent entity's position is tracked. Multi-player, multi-collider, structure footprints, etc. would all need separate machinery.
- **CPU writes per FixedUpdate.** Doesn't scale to many grass areas — each one needs its own MonoBehaviour + material instance. JAHRMANN's force-map texture is the GPU-side equivalent that scales.
- **No persistent state.** When the entity leaves, `_Pos` linearly drifts back — no per-position memory. Cannot model permanent paving or different per-spot regrowth rates.
- **Shader Graph for vertex displacement of mesh grass.** Not a compute-built blade system. Useful as a "near-LOD with mesh blades" alternative if compute proves too heavy, but it's not where we want to land.

### What's worth borrowing

- **The "linear fade-back when force is gone" pattern.** Trivially extends to JAHRMANN's η decay if we don't want a fully physical fade.
- **Single Shader Graph subgraph that wraps displacement.** Confirms that ShaderGraph in URP can express vertex displacement — useful for our **vegetation other than grass** (small bushes, mesh-based flowers) where compute is overkill and a per-instance Shader Graph displacement is enough.
- **`AffectDistance` as a falloff radius.** Trivial but worth naming — our modification API should expose the same concept (sphere of effect + falloff curve) on both player capsules and structure footprints.

### Conclusion

Reference confirms: for one player + one mesh field, ShaderGraph + material property is fine. For our use case (planet-scale, many actors, persistent modification with biome-driven regrowth), the JAHRMANN/GoT compute-shader architecture is the right path. Move on.

## Reference: LOD-Planets-in-Unity-master/.../Chunk.cs

**The canonical Sebastian-Lague-style chunked quadtree LOD planet.** 490 lines in `Chunk.cs`, ~250 in `TerrainFace.cs`, ~500 in `Planet.cs`. This is the structural blueprint Bryan called out by name ("Sebastian Lague style"). Read end-to-end; documented warts and all so we know what to keep and what to redesign.

### Architecture overview

```
Planet ── 6× TerrainFace ── (root) Chunk ── 4× child Chunk ── 4× child Chunk ── …
                  │
                  └── one Mesh per face (combined from all visible leaf chunks)
```

- Six faces (cube → sphere). Each face owns **one Mesh** that aggregates the vertices/triangles/normals/colors of every visible leaf chunk for that face.
- Each face has a **root Chunk** spanning the whole face; recursively subdivides into 4 children whenever the player's distance to that chunk crosses a per-level threshold.
- Updates every **2 seconds** via a coroutine (`PlanetGenerationLoop`) — not per-frame. Worker `Thread`s do the geometry math; results return to main thread via an `ActionQueue` + `lock`.

### The hash-bit quadtree encoding (the clever bit)

Each chunk stores `uint hashvalue`. Root = 1 (leading 1 bit preserves leading zeros in children). Each level deeper appends 2 bits encoding the quadrant:

| Quadrant | Bits                   |
| -------- | ---------------------- |
| NW       | 00 (`Quadrant.NW = 0`) |
| NE       | 01 (`Quadrant.NE = 1`) |
| SE       | 10 (`Quadrant.SE = 2`) |
| SW       | 11 (`Quadrant.SW = 3`) |

```csharp
children[0] = new Chunk(hashvalue * 4,     ..., Quadrant.NW); // hash << 2 | 0
children[1] = new Chunk(hashvalue * 4 + 1, ..., Quadrant.NE); // hash << 2 | 1
children[2] = new Chunk(hashvalue * 4 + 2, ..., Quadrant.SE); // hash << 2 | 2
children[3] = new Chunk(hashvalue * 4 + 3, ..., Quadrant.SW); // hash << 2 | 3
```

32-bit hash → 16 levels deep max. Bryan's `detailLevelDistances` array has 16 entries. Matches.

### Neighbor LOD via bitmask XOR (the genius bit)

**No explicit neighbor pointers.** When a chunk needs to know its east/west/north/south neighbor's detail level (to decide which edge-fan template to use), it XORs its own hashvalue with a computed mask, then walks down the tree from face root looking for the resulting hash.

`CheckNeighbourLOD(direction, hash)` walks UP the bits two at a time:

- If the next-up quadrant **does NOT change side** when you step in the query direction (e.g. asking for east neighbor of an NE child → that east neighbor is in the parent's east branch, stop), append the "flip across this axis" bitmask and break.
- If the next-up quadrant **does change side**, append the flip mask, shift right, continue up.

Then `terrainFace.parentChunk.GetNeighbourDetailLevel(hashvalue ^ bitmask, detailLevel)` walks down looking for that hash. If found at lower detail level, the neighbor is less-detailed → this edge needs an edge fan.

**This is elegant but has a flaw the file documents in a TODO:** `// REACH BEYOND THIS FACE IF THE CHUNK IS ON THE FACE'S BORDER.` Cross-face neighbor lookup is **not implemented** — so chunks on cube-face borders always assume their cross-face neighbor is same-LOD. Cracks/seams along face borders.

### Quad-template edge fans (seam fix)

```csharp
int quadIndex = neighbours.AsBinarySequence(4);  // 16 possible combos: ESNW each 0 or 1
vertices = Presets.quadTemplateVertices[quadIndex];
triangles = Presets.quadTemplateTriangles[quadIndex];
```

`Presets` contains 16 pre-baked quad meshes — one per neighbor-LOD combination. When a chunk borders a lower-LOD neighbor on (say) its east edge, it picks the template where the east edge is collapsed into "edge fan" triangles that snap to the coarser neighbor's vertex spacing. **Pre-baked, not generated at runtime** — minor RAM cost, zero per-frame cost.

### Border vertices for normal smoothing (the polish bit)

Each chunk computes its 65×65 main vertices AND a ring of border vertices extending one cell beyond the chunk edge. Normals at edge vertices are averaged using triangles that include border vertices, so lighting doesn't crease at chunk seams.

Border vertex indices are negative in the triangle list (`borderTriangles[i] = -(i+1)`) — `SurfaceNormalFromIndices` decodes them via `borderVertices[-indexA - 1]`. Borders never appear in the final mesh, only in normal calculations.

### Per-vertex biome color baking (matches GoT far-LOD)

```csharp
if (detailLevel >= planetScript.vertexColorMinLOD) {
    colors[i] = terrainFace.GetBiomeColor(...);
} else {
    colors[i] = pink;  // covered by texture at far LOD
}
```

At high detail (close), each vertex gets its biome color baked in (saves texture sampling). At low detail (far), they rely on a pre-rendered face texture and the vertex color is a debug-pink that's never seen. **Same pattern GoT uses for grass far-LOD** — bake to texture beyond a threshold.

### What we keep

| Pattern                                                   | Why                                                                                                   |
| --------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| **Hash-bit quadtree encoding**                            | Cheap (1 uint per chunk), enables the neighbor-bitmask trick, no parent/neighbor pointer maintenance. |
| **Neighbor lookup via XOR bitmask**                       | Eliminates per-chunk neighbor pointers and update cascades. Adopt with cross-face support added.      |
| **Pre-baked edge-fan templates**                          | 16 quad templates, indexed by neighbor-LOD bitmask. Zero per-frame cost.                              |
| **Border vertex normal smoothing**                        | Visible quality difference at seams — non-negotiable.                                                 |
| **Per-LOD-level distance thresholds**                     | Simple, tunable, exactly what Bryan asked for.                                                        |
| **One mesh per face**                                     | Fewer draw calls than one mesh per chunk. Combine visible-leaf data into face mesh.                   |
| **Cube-to-sphere normalization + elevation displacement** | Standard, we already do this in `ShapeGenerator`.                                                     |
| **Far-LOD baked face texture**                            | Aligns with GoT grass far-LOD pattern.                                                                |

### What we change / fix

| Issue                                                                    | Our fix                                                                                                                                                                                 |
| ------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Coroutine-based update loop (`PlanetGenerationLoop`)                     | **Awaitable** loop instead. Bryan's standing rule.                                                                                                                                      |
| `Thread` + manual `lock` + `ActionQueue`                                 | Burst Jobs (we already use them) + Awaitable for marshaling. Job system is thread-safe by design.                                                                                       |
| Cross-face neighbor lookup NOT implemented (TODO in source)              | Implement properly. Each face has known adjacency in cube topology; walking across a face edge maps to a specific neighbor face + flipped/rotated axis. Add a `CubeFaceTopology` table. |
| Synchronous `GenerateChildren` recursion (no async chunking)             | Chunk generation is per-chunk Awaitable. Higher-LOD chunks generated in background, swapped in when ready. Need a "pending mesh" state during subdivision.                              |
| Full mesh rebuild every regen (`mesh.Clear()` then re-upload everything) | Incremental update — only rebuild faces whose visible-leaf set changed. Track dirty flag per face.                                                                                      |
| 2-second polling cadence                                                 | Event-driven — recompute when player moves past a threshold. Reduces unnecessary work when stationary.                                                                                  |
| Vertex color baking per-vertex on CPU                                    | Move to Burst job (we already have job infrastructure).                                                                                                                                 |
| `Noise` instantiated inside `GetBiomeColor` per-vertex                   | Reuse our existing noise filter infrastructure.                                                                                                                                         |
| No surface modification or grass hook                                    | Add a `IChunkSurfaceState` interface — chunks own a small modification mask + grass density map alongside vertex/triangle data.                                                         |

### What's missing vs. our requirements (and where we add it)

1. **Per-biome grass density storage** — Chunk needs a `BiomeDensity` field (per-vertex or low-res texture per chunk). Computed once at chunk gen, sampled by grass compute.
2. **Modification mask** — Per-chunk render-texture (e.g. 64×64) for force-map η. Persistent across LOD changes (cached when a chunk is unloaded).
3. **Grass renderer wiring** — When a chunk goes visible, register it with grass compute system; when it unloads, deregister.
4. **Biome texture sampling** — Currently per-vertex CPU color baking; for our setup we want biome map textures the shader samples (matches GoT).
5. **Save/load persistence** — Modifications must survive chunk unload/reload. Per-chunk modification mask cached by hashvalue.

### Open questions this raised

- **Chunk resolution** — Lague uses 65×65 (`Presets.quadRes + 1`). We currently use one mesh per face at higher resolution. Need to settle on chunk vertex count vs LOD depth tradeoff. 65×65 + 16 levels matches Lague.
- **Face mesh memory budget at max LOD** — every 16-level chunk = 65² = ~4K verts; 4^15 chunks per face theoretical max. Practical visible count is small (~hundreds), so face mesh stays manageable, but we should set a hard cap.
- **Modification mask resolution** — 64×64 per chunk, 256 active chunks = 1M texels = ~4MB at RGBA8. Fits, but at higher resolutions or larger active sets, this becomes the main memory cost.
- **Mod mask survival across LOD** — when a chunk subdivides into 4 children, how is the parent's mod mask split? Bilinear sample at child cells.
- **Cross-face seams for grass** — grass at a face edge needs to know neighbor-face wind, density, mod mask. Probably each face has its own grass system and they overlap by half a chunk at the seam.

### One pattern worth calling out: `ActionQueue` is an antipattern we already avoid

The reference uses `List<Action> ActionQueue` with manual `lock(_asyncLock)` to marshal job results to the main thread. **We already have a better solution** — Unity Awaitable + `await Awaitable.MainThreadAsync()`. Don't copy the `ActionQueue` pattern. The reference's threading code is the weakest part of the file.

## Reference: Fluid-Planet GPU instancing pattern

Already skimmed. Uses `StructuredBuffer<T>` + `#pragma instancing_options procedural:setup` + `unity_InstanceID`. Direct pattern for per-blade instance data.

## Reference: Cyanilux GPU Instanced Grass Breakdown (web)

URL: https://www.cyanilux.com/tutorials/gpu-instanced-grass-breakdown/
Recent (Oct 2025), URP-specific, uses newer `Graphics.RenderMeshIndirect`. To fetch if needed.

## Reference: NiloCat 1M-instance example (web)

URL: https://github.com/ColinLeung-NiloCat/UnityURP-MobileDrawMeshInstancedIndirectExample
Mobile-grade, GPU + CPU culling. To fetch if needed.

---

## Synthesis — Proposed Design

> Status: **draft proposal for Bryan's review** before implementation begins. Per the agreed audit workflow, no code lands until Bryan signs off on the decisions below. Cite the reference notes above for evidence; comments + counter-proposals welcome on any decision.

### Phase plan (recap, decisions in bold)

| Phase                                                  | Scope                                                                                                                                                                                 | Verifiable outcome                                                                                                                       |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| **A. Chunk skeleton**                                  | Quadtree LOD chunking on top of existing `TerrainFace`. Hash-bit encoding, cross-face neighbor lookup, edge-fan templates, border-vertex normal smoothing, Awaitable-driven gen loop. | Planet loads with chunked LOD that subdivides smoothly toward the camera, no seams, no cracks across face borders.                       |
| **B. Biome textures + surface state**                  | Per-biome surface texture pipeline. Biome map per chunk (low-res, e.g. 64×64). Far-LOD bake of grass overlay into terrain texture.                                                    | Surface texture changes by biome; one-source-of-truth biome map drives both terrain and grass placement.                                 |
| **C. Grass renderer (placement + render)**             | JAHRMANN/GoT-style compute. Lane-ID-driven, no persistent per-blade buffer. Per-frame compute → indirect render. Vertex-sliding LOD.                                                  | Grass renders at full density near camera, sparser at distance, no popping, no sliding, no synced wind.                                  |
| **D. Wind + interaction**                              | Wind sampled from existing `WeatherManager`-fed wind texture (GoT style) for v1. Player + structure colliders push into a force-map texture (JAHRMANN η).                             | Walking through grass leaves a trail that recovers; storms make grass lean; wind direction matches WeatherManager state.                 |
| **E. Modification API (paving, structures, regrowth)** | Public API for marking grass-flat / paved / cut. Permanent mask channel in force map. Per-chunk modification mask persists across LOD changes.                                        | Build a structure, mark its footprint paved → grass stays flat permanently. Cut a path → grass slowly grows back over configurable time. |

Phases B and C can run in parallel after A. D depends on C. E depends on D.

### Chunk system

**Data model** (per-chunk, expected ~few hundred active per planet):

```csharp
class PlanetChunk
{
    public uint HashValue;                  // 1 = root, child = (parent << 2) | quadrant
    public int DetailLevel;                 // tree depth, 0-15
    public int FaceIndex;                   // 0-5
    public double3 CenterPosition;          // planet-local, double precision
    public double Radius;
    public PlanetChunk[] Children;          // 0 or 4
    public PlanetChunk Parent;              // null for root

    public ChunkMeshData Mesh;              // vertices, triangles, normals, uvs
    public ChunkBiomeData Biomes;           // 64×64 biome map (primary id + blend weight)
    public ChunkSurfaceState SurfaceState;  // force map, modification mask (Phase E)
    public ChunkGrassHandle Grass;          // registration in grass compute system (Phase C)

    public NeighborLodMask EdgeNeighbors;   // 4 bits — ESNW lower-LOD flags
    public ChunkLifecycle State;            // Pending, Active, Subdividing, Unloading
}
```

**Decisions:**

1. **Hash-bit quadtree encoding from LOD-Planets** — keep. Cheap, enables bitmask neighbor lookup, no neighbor-pointer maintenance.
2. **Cross-face neighbor lookup — implement properly.** The reference's `// REACH BEYOND THIS FACE` TODO is the file's biggest gap. Add a `CubeFaceTopology` static table: for each face × edge, what's the neighbor face and how do indices map? Constant-time lookup.
3. **Edge-fan via pre-baked quad templates (16 variants)** — keep. Zero per-frame cost, exact-match vertex snapping at LOD boundaries.
4. **Border vertex normal smoothing** — keep. Non-negotiable for visual quality.
5. **Chunk resolution: 65×65 vertices (matches Lague, matches our existing `Resolution`).** Easy power-of-2-plus-1 lookup; clean math.
6. **Max LOD depth: 15** (16 entries in `detailLevelDistances`). Matches LOD-Planets' 32-bit hash and Bryan's planet size.
7. **One Mesh per face**, NOT one MeshFilter per chunk. Fewer draw calls. Visible-leaf data aggregated into per-face mesh; only dirty faces re-upload.
8. **Awaitable for all async chunk work.** No coroutines, no raw `Thread`, no `ActionQueue`. Compute jobs run on Burst Job system; main-thread marshaling via `await Awaitable.MainThreadAsync()`.
9. **Event-driven regen, not 2-second polling.** Recompute when player crosses a sub-chunk threshold OR when an external trigger marks a chunk dirty (e.g. paving placed).
10. **Per-chunk lifecycle state machine.** Pending → Active → Subdividing (children loading) → Active(with children) → Unloading. Avoids the LOD-Planets pattern of `children = new Chunk[0]` to deactivate (wasteful GC).

**Lifecycle integration with existing code:**

- Replace `TerrainFace.ScheduleMeshDataJob` (which builds one mesh for whole face) with `TerrainFace.UpdateChunks(playerPosition)` that traverses the quadtree and schedules per-chunk Burst jobs.
- `ShapeGenerator.CalculateUnscaledElevation` stays unchanged — chunk vertex generation calls it per vertex.
- `Planet.cs` keeps its job-scheduling pattern (it's good) but the unit of work changes from face → chunk.

### Grass renderer

**Architecture: hybrid JAHRMANN + GoT.** Take JAHRMANN's per-blade Bézier + force map for physics, take GoT's lane-ID-driven placement + clumping + vertex-sliding LOD for production polish. CWD-Sim's fluid wind deferred to a later iteration; v1 uses GoT's scrolling wind texture sourced from WeatherManager.

**Decisions:**

1. **Lane-ID placement, no persistent per-blade buffer (GoT).** Per-frame compute regenerates blades from `(chunkId, laneId, time)` only. Memory per chunk: just biome map + density field + force map. Saves ~6MB/chunk vs persistent blade buffers.
2. **Quadratic Bézier blade shape (JAHRMANN, not cubic).** Simpler, reference impl uses it, force-map mechanism is built around v0/v1/v2. Cubic upgrade (GoT's tilt/bend params) deferred to optional polish phase.
3. **Procedural Voronoi clumping (GoT).** Clump = (center position, facing direction, base color). Sample base color from chunk's biome map at clump center. Per-blade noise on top. Solves Bryan's "grass samples terrain colors" requirement at the right granularity (clump, not blade).
4. **Vertex-sliding LOD (GoT).** Two levels (15 verts → 7 verts). No crossfade, no popping. Implement from day one.
5. **4-test culling (JAHRMANN).** Orientation, frustum, distance-with-id-mod-n, occlusion. All in compute. Atomic increment for visible count, indirect draw consumes it.
6. **Indirect rendering via `Graphics.RenderMeshIndirect`** (modern API). Tessellation evaluation shader builds blade geometry from Bézier control points.
7. **Rounded normals across blade width + glancing-angle widening + clump-normal specular AA (GoT).** Pure shader changes, no extra data. Bake in from day one.
8. **Shadows: shadow imposter on terrain + screen-space self-shadowing + per-vertex height-darken (GoT + CWD-Sim).** Never per-blade shadow map. Per-vertex `c *= clamp(P''.y - bend·k, m_min, m_max)` for free micro-occlusion.
9. **Planet-tangent wind projection (mandatory).** `up = normalize(bladeRoot - planetCenter); tangentWind = wind - up·(wind·up)`. Without this, grass tilts toward world Y on the far side of the planet.
10. **Per-biome grass density.** Biome map already stores primary biome id; each `BiomeDefinition` carries a `GrassDensity` float. Compute reads biome at lane root, scales placement probability.

**Compute pipeline per chunk per frame:**

```
compute_place:    laneId → tile_grid_pos → jitter → biome.density check → cull → write to instance_buf, atomic++ blade_count
compute_animate:  instance_buf → sample wind texture at root → apply force map → quadratic Bézier control points
compute_cull:     orientation + frustum + distance + occlusion → indirect_args.instanceCount++
indirect_render:  tessellation eval shader → blade geometry → fragment shader (lighting, shadow)
```

**Per-blade data flowing from compute to render (transient buffer, regenerated per frame):**

```hlsl
struct BladeInstance {
    float3 v0;          // root (worldspace, on planet surface)
    float3 v2;          // tip (after physics)
    float3 v1;          // mid control point (derived)
    float  height;
    float  width;
    float  rotation;    // direction angle in tangent plane
    uint   clumpId;     // index into clump buffer for color/facing
};
```

**Open question:** does URP 17 cleanly support tessellation in custom shaders? If not, fall back to vertex-shader strip generation (build the blade quad in vertex shader from the Bézier control points).

### Surface modification

**Force map texture per chunk** (JAHRMANN η, extended with permanent-mask channel):

```
ForceMap[chunk] : RGBA8 render texture, 64×64 (matches biome map resolution)
  R: δ.x  (translation x in tangent plane)
  G: δ.y  (translation y in tangent plane)
  B: η    (collision strength, 0-1, decays over time)
  A: permanent_mask (1.0 = paved/structure, η decay disabled at this texel)
```

**Decisions:**

1. **Positional, not per-blade.** Modifications are stored by _position_, sampled by _blades_. Lane-ID architecture makes per-blade modification impossible anyway; positional is also how players think ("I flattened this spot").
2. **64×64 per chunk is the default resolution.** At max LOD a chunk is ~few meters across → 64×64 = ~10cm/texel near the camera. Coarser at distance is fine because grass density also falls off.
3. **Decay rate `a` per biome.** Tropical/Forest grass recovers fast; Desert/Tundra grass recovers slowly. Configurable in `BiomeDefinition`.
4. **Permanent mask = α channel of force map.** Texel with α=1 skips decay. Set by paving/structure placement. Unset by destruction/erosion (future).
5. **Modification mask survives LOD transitions.** When a chunk subdivides, parent's force map is bilinear-sampled into the 4 children. When children re-merge, max-pool back into parent. Cached by hashvalue when chunk unloads.
6. **Collider providers feed into a per-frame compute pass.** Player capsule = 2-3 spheres; structure footprint = bounding sphere(s). Compute reads collider list, writes to force map η + δ at affected texels.

**Decisions deferred:** vehicle tracks (lift from GPU Gems guide later), explosion shockwaves (future), seasonal regrowth modulation (Phase F+).

### Biome texture system

**Per-chunk biome map texture** drives BOTH terrain shading AND grass placement/coloring. One source of truth.

```
BiomeMap[chunk] : RGBA8, 64×64
  R: primaryBiomeId / 255 (normalized lookup index into BiomeDefinition[])
  G: secondaryBiomeId / 255
  B: blendWeight (0 = pure primary, 1 = pure secondary)
  A: reserved for moisture/temperature variation if needed
```

**Decisions:**

1. **Biome assignment in Burst job at chunk gen.** Uses existing `BiomeProvider` patterns (we already have biome sampling — needs verification against current code).
2. **Terrain shader samples biome map + per-biome texture array.** Two biome textures blended by α; same model as our existing biome blending if any.
3. **Grass compute samples biome map for density and clump base color.** Same texture used by both shaders. Single source of truth.
4. **Far-LOD overlay (GoT).** Beyond a distance threshold, compute pre-bakes grass tint into a chunk-level overlay texture that the terrain shader adds. Camera approaches → overlay fades down as actual blades fade in.
5. **Triplanar sampling for sloped/cliff areas.** Not for biome map (which is in chunk-tangent space) but for the per-biome surface textures, to avoid stretching on steep terrain.

### Open questions for Bryan

These need an answer or "trust your judgment, proceed" before I write the design doc proper. Numbered for easy reply.

1. **Compute target: how many grass blades per frame total?** JAHRMANN reports ~400K total / 50K rendered at 123 FPS on 2017 hardware. GoT reports "hundreds of thousands per visible region" on PS4. On your PC at Quality 0, we can target **multi-million total / hundreds of thousands rendered**. Are you OK with that target, or do you want it tuned for lower-spec PCs?

**Feedback:** I eventually want this tunable via a settings qualty screen, not in Unity's inspector.

2. **Tessellation in URP 17.6 — verify or fallback?** I haven't confirmed URP 17 supports hardware tessellation in custom-pass shaders cleanly. Want me to verify before phase C starts (cheap, ~1 day), or proceed with vertex-shader strip generation as a safer default?

**Feedback:** According to Google: The Universal Render Pipeline supports custom HLSL shaders that include Hull and Domain stages, provided the target platform's graphics API (like DirectX 11/12, Vulkan, or Metal) supports hardware tessellation.

3. **Wind v1: GoT scrolling Perlin or CWD-Sim fluid sim?** GoT is simpler and ships fine. CWD-Sim is dramatically more expressive (wind deflects around mountains, storms churn the field). Recommendation: GoT for v1, CWD-Sim as a measured upgrade in a later phase. OK?

**Feedback:** GoT v1 is ok as long as it's built in a way that it can be easily replaced with a more complex system later. Hot-swappable

4. **Permanent mod mask resolution / per-chunk memory budget.** Default plan: 64×64 RGBA8 per chunk × 256 active chunks ≈ 4 MB. Doubles if we go 128×128. Acceptable, or constrain harder?

**Feedback:** Acceptable.

5. **Cross-chunk grass continuity at face seams.** Two options: (a) each face's grass is independent and there's a visible boundary at face edges; (b) faces overlap by half a chunk and we blend density. Option (b) is cleaner but more complex. Preference?

**Feedback:** Option b, I don't want a vislbe chunk line.

6. **Save format for modifications.** Per-chunk force-map alpha channel (permanent mask) needs to persist. Simplest: serialize per-chunk modification mask by hashvalue when the planet saves; rehydrate when chunk loads. This piggybacks on whatever planet-save mechanism we're using (or building). What's the planned save layer?

**Feedback:** The save system has not been designed yet. Since we don't know how the rest of the game will be built yet, let's just implement a simple save/load for now that we can replace / adapt to whatever save solution we build later.

7. **Existing `TerrainFace` / `Planet.cs` integration depth.** The current per-face job system works. Phase A replaces it with per-chunk jobs. Want me to keep the single-face path as a fallback (e.g. for very distant planets in the future), or rip it out cleanly?

**Feedback:** Let's keep it for "low-rez" planets, like you suggested. Maybe when we call into the generation and pass the seed, we can also pass the resolution (low(per-face), high (per-chunk))

8. **Biome map ownership.** The chunk-level biome map is new. Does it belong in the chunk class itself (as I sketched), or as a separate `IBiomeProvider` registered against the chunk? Latter is cleaner if grass and surface modification want to share the map without circular references.

**Feedback:** IBiomeProvider, we want to be able to support any type of biome, including types we haven't though of yet like "Mushroom Land" or something else equally as odd.

9. **Are there features I haven't named that you want in v1?** E.g. seasonal grass color changes, snow accumulation on grass tips, wetness affecting bend stiffness, footprints in dirt vs grass, etc. Easier to scope these now than after the design doc is locked.

**Feedback:** Yes, I want to support all the things you just listed (season changes, snow accumulatoin, wetness, stiffness, footprints, burnt/scortced from fire, etc). All of that needs to be supported. I also want to build a snow system for snow biomes with tracks in the deep snow.

---

## Locked-in Design (post-feedback 2026-05-30)

> The proposal section above is the original draft. This section reflects the design after Bryan's feedback on the 9 open questions and is the **source of truth** going forward. Where it differs from the proposal, this section wins.

### Scope expansion from feedback

Question 9 expanded v1 scope significantly. **All of these are now in scope** (not deferred):

- Seasonal grass color changes
- Snow accumulation on grass tips
- Wetness affecting bend stiffness
- Footprints in dirt vs grass
- Burnt / scorched grass from fire
- **Dedicated snow system for snow biomes** with visible tracks in deep snow

This pushes "one force map per chunk" up to "**stack of surface state textures per chunk**". The renderer architecture survives; the data model grows.

### Revised phase plan

| Phase | Scope | Verifiable outcome |
|---|---|---|
| **A** | Chunk skeleton + Low/High resolution mode switch via `IPlanetSurfaceProvider`. Hash-bit encoding, cross-face neighbor lookup with half-chunk seam overlap, edge-fan templates, border-vertex normal smoothing, Awaitable gen loop. | One generation entry point produces either Low (per-face) or High (per-chunk) planets. No visible chunk lines at face boundaries. |
| **B** | `IBiomeProvider` open biome registry + per-chunk biome map (64×64). Far-LOD overlay bake. | New biomes (e.g. "Mushroom Land") add via data only, no code edits in renderers. |
| **C** | Surface state stack (multiple textures per chunk) + `IWindFieldProvider` abstraction (v1: GoT scrolling Perlin). `IChunkPersistenceProvider` abstraction (v1: PNG-per-chunk in user data dir). | State textures survive LOD transitions, chunk unload/reload, and game save/restart. Swapping wind impl is a one-line change. |
| **D** | Grass renderer (JAHRMANN+GoT compute, lane-ID, vertex-sliding LOD). In-game quality settings drive max blade count. | Quality slider in the settings menu changes grass density live; no Inspector tweaking needed. |
| **E** | Modification + dynamic state API (paving, flatten, wetness, burn, footprints). Reads/writes state stack. Hooked into player movement, `WeatherManager`, fire events. | Walking through grass leaves recovering trail. Storm rain visibly wets grass and droops it. Burned grass dies and reveals scorched dirt. |
| **F** | Snow system for snow biomes — geometry layer + grass interaction + deep-snow tracks. | Tundra biomes show snow that buries grass above a depth threshold; walking leaves footprints that slowly refill. |

B and C run in parallel after A. D depends on B+C. E depends on D. F depends on E.

### Key new abstractions (all introduced for hot-swappability)

| Interface | Purpose | v1 impl | Future impls |
|---|---|---|---|
| `IPlanetSurfaceProvider` | Generate planet surface at requested resolution | `PerFaceSurfaceProvider` (existing path), `ChunkedSurfaceProvider` (new) | LOD-aware proxy that picks impl per planet distance |
| `IBiomeProvider` | Open registry of biome definitions (sample at unit-sphere point → biome blend) | `RegistryBiomeProvider` reading `BiomeDefinition[]` ScriptableObjects | Custom providers per planet type (alien, Mushroom Land, etc.) |
| `IWindFieldProvider` | Sample wind vector at world position | `ScrollingPerlinWindProvider` (GoT-style, fed by WeatherManager direction + strength) | `FluidSimWindProvider` (CWD-Sim 2D Navier–Stokes) |
| `IChunkPersistenceProvider` | Load/save per-chunk modification state by hashvalue | `PngFileChunkPersistence` writing to user data dir | Real save-system-backed provider once save system exists |
| `IGrassQualitySettings` | Read in-game quality settings for max blade count, LOD distances, shadow tier | `QualityMenuGrassSettings` bound to a settings UI | Same — settings menu builds on this |

### Chunk system — locked-in additions

All the **Chunk system** decisions in the proposal above stand, plus:

- **`PlanetResolution` enum at generation API.** `Planet.Generate(seed, resolution: PlanetResolution.High)`. Low = current per-face path stays as-is (good for distant planets in skybox). High = new chunked path. Same surface interface either way.
- **Half-chunk face-seam overlap.** Each cube face's chunk grid extends ~half a chunk beyond its nominal edge into the neighbor face's space; density (vertices, grass, state textures) cross-fades across the overlap zone. Eliminates the visible chunk line at face borders. This is more complex than option (a) but Bryan explicitly chose it.
- **Cross-face neighbor lookup remains required** (the LOD-Planets TODO) — the half-chunk overlap doesn't remove the need; lookup is used to find the neighbor's LOD for edge-fan template selection, and to share state-texture sampling at the overlap.

### Biome system — `IBiomeProvider`

```csharp
public interface IBiomeProvider
{
    BiomeBlend SampleAt(float3 unitSpherePoint, float elevation01);
    int BiomeCount { get; }
    BiomeDefinition GetBiome(int id);
}

public readonly struct BiomeBlend
{
    public readonly int PrimaryId;
    public readonly int SecondaryId;
    public readonly float BlendWeight;   // 0 = pure primary, 1 = pure secondary
}

public class BiomeDefinition : ScriptableObject
{
    public string DisplayName;          // "Tropical", "Tundra", "Mushroom Land"
    public Texture2D SurfaceAlbedo;
    public Texture2D SurfaceNormal;
    public Color GrassTintBase;
    public float GrassDensity;          // 0 = none, 1 = max
    public float GrassRecoveryRate;     // η decay rate per second
    public float SnowAccumulationRate;  // for snow system
    public float WetnessRetentionRate;  // how slowly water dries
    public BiomeSeasonalCurve Seasonal; // color shift, density shift over year
    public bool SupportsTracks;         // soft surfaces (dirt, snow) yes; rock/lava no
}
```

The registry pattern makes "Mushroom Land" a data-only addition. Compute shaders read by id; per-biome textures live in a Texture2DArray indexed by biome id.

### Surface state stack — replaces the single "force map"

```
Per-chunk surface state (4 textures, 64×64 each, total ~64KB per chunk):

ForceMap         RGBA8
  R, G: δ.xy     blade displacement in tangent plane (JAHRMANN)
  B:    η        collision strength, decays per biome.GrassRecoveryRate
  A:    perm     permanent mask (paving / structure footprint)

WeatherState     RGBA8
  R: wetness     0-1, raised by rain, decays per biome.WetnessRetentionRate
  G: snowDepth   0-1 normalized to biome.MaxSnowDepth
  B: burn        0=fresh, 1=ash; spreads via fire events
  A: heat        0-1 for short-lived fire visualization

TrackMap         R16 (single-channel depth)
  R: trackDepth  signed depth of footprint/track displacement
                 (positive = pressed down, used for snow + soft dirt)

SeasonalState    R8
  R: seasonal    0-1 phase for color/density modulation
                 (could be global or per-chunk; per-chunk allows latitude variation)
```

Memory: 4 textures × 64×64 × (4+4+2+1 bytes) = 44 KB per chunk × 256 active = ~11 MB. Comfortable.

**Producers and consumers:**

| State | Written by | Read by |
|---|---|---|
| ForceMap (δ, η) | Collider sweep compute pass (player + structures) | Grass animate compute (displaces v2) |
| ForceMap.perm | Modification API (paving/structure place) | Force-map decay compute (skips perm=1) |
| WeatherState.wetness | Rain compute pass driven by `WeatherManager.RainState` | Grass shader (darker color, more bend), Terrain shader (wet tint) |
| WeatherState.snowDepth | Snow accumulation compute driven by precipitation + temperature | Snow renderer (Phase F), Grass compute (hides blades beyond threshold) |
| WeatherState.burn | Fire event compute (spreads on tick) | Grass compute (no blades on burn=1), Terrain shader (scorch tint) |
| TrackMap | Collider sweep compute (when biome.SupportsTracks) | Snow renderer (deformed snow geometry), Terrain shader (footprint depth shadow) |
| SeasonalState | Global season tick, blended with latitude/biome | Grass compute (color tint), Terrain shader (seasonal albedo shift) |

All writes happen in compute, all reads happen in compute or shader. No CPU per-frame work for state updates.

### Wind — `IWindFieldProvider` (hot-swappable)

```csharp
public interface IWindFieldProvider
{
    // Sample wind in worldspace at the given planet-surface point.
    // Returns velocity vector tangent to the planet surface.
    float3 SampleWindAt(float3 worldPosition, float3 planetUp);

    // Provide a texture binding for shaders that want to sample wind directly
    // (avoids per-blade CPU sampling). Null if provider doesn't expose a texture.
    Texture WindTexture { get; }
    Matrix4x4 WindTextureToWorld { get; }
}
```

**v1 impl: `ScrollingPerlinWindProvider`.** Reads `WeatherManager.WindDirection + WindStrength`, generates a 256×256 2D Perlin scrolling along that direction at that speed, exposes it as `WindTexture`. Grass compute samples the texture at each blade root.

**Future impl: `FluidSimWindProvider`.** Runs the CWD-Sim 6-step Navier–Stokes solver on a 1000×1000 (or per-face 512×512) compute texture, injects WeatherManager direction as boundary input, exposes the velocity field as `WindTexture`. Same consumer-side interface; grass doesn't notice the change.

### Persistence — `IChunkPersistenceProvider` (v1: simple, replaceable)

```csharp
public interface IChunkPersistenceProvider
{
    Awaitable SaveChunkStateAsync(uint chunkHash, ChunkSurfaceState state);
    Awaitable<ChunkSurfaceState> LoadChunkStateAsync(uint chunkHash);
    bool HasState(uint chunkHash);
}
```

**v1 impl: `PngFileChunkPersistence`.**
- One PNG per chunk per state texture, in `%LOCALAPPDATA%/ProceduralPlanets/save/<planetSeed>/<chunkHash>.<state>.png`.
- Trivial to inspect, easy to delete for testing.
- When chunk unloads with `ForceMap.perm` ≠ 0 OR any other state ≠ default, write to disk.
- When chunk loads with file present, read it and upload to chunk's render textures.

When the real save system arrives later, swap in a new provider impl. Grass + state code untouched.

### Grass renderer — locked-in additions to proposal

All the **Grass renderer** decisions above stand, plus:

- **Tessellation confirmed.** Per Bryan's quoted answer: URP supports custom HLSL with Hull/Domain stages on D3D11/12, Vulkan, Metal. Proceed with tessellation. **Sanity check first:** write a hello-world tess shader (one triangle subdivided to 16) before phase D starts, to confirm our URP 17.6 config doesn't have a custom-pass restriction.
- **Max blade count driven by `IGrassQualitySettings`.** Settings menu in-game exposes a slider; default presets (Low / Medium / High / Ultra). Compute dispatch size scales with the setting. No Inspector tweaking required for end-user.
- **Quality settings additionally drive:** LOD distance thresholds, shadow tier (terrain shadow + screen-space at High; just terrain at Medium; none at Low), maximum modification mask resolution.
- **Blade shader reads from full surface state stack** — wetness darkens + droops, snow depth determines whether blade renders at all, burn = no blade + scorch tint hint to terrain, season scrolls color tint, footprint depth offsets blade root downward.

### Snow system (Phase F) — outline

This is new scope; full design deferred to a Phase F design doc. Outline only here:

- Snow biomes are flagged in `BiomeDefinition.SnowAccumulationRate > 0`.
- Snow renderer = geometry layer above terrain. Vertices displaced upward by `WeatherState.snowDepth` interpolated across the chunk.
- Two approaches to consider in Phase F: (a) extra mesh layer per chunk; (b) parallax/POM in terrain shader for shallow snow + dedicated mesh only when depth > threshold.
- Tracks: `TrackMap.R` depresses the snow displacement. Refills as `WeatherState.snowDepth` increases. New snow flattens old tracks (max-pool against accumulation).
- Grass interaction: blades hidden when `snowDepth > bladeHeight * 0.7` (snow-on-grass tip = special particles, not mesh).
- Player movement compute writes tracks; identical pipeline to ForceMap.δ but writes to `TrackMap.R` when `biome.SupportsTracks && snowDepth > minTrackDepth`.

### Resolved open questions (cross-reference for design doc)

| # | Topic | Resolution |
|---|---|---|
| 1 | Compute target / blade count | Driven by in-game quality settings, not Inspector. `IGrassQualitySettings` interface. |
| 2 | URP tessellation | Supported on standard graphics APIs. Proceed with tess; sanity-check shader before phase D. |
| 3 | Wind v1 | GoT scrolling Perlin, behind `IWindFieldProvider` abstraction for future CWD-Sim swap. |
| 4 | Mod mask budget | 64×64 RGBA per chunk OK (now expanded to state-texture stack ~44KB/chunk; ~11MB total at 256 chunks). |
| 5 | Face seam continuity | Option (b): half-chunk overlap with cross-face density blending. |
| 6 | Save format | `IChunkPersistenceProvider` interface; v1 = simple PNG-per-chunk-per-state; future = real save system. |
| 7 | Existing per-face path | Keep it. Generation API takes `PlanetResolution` enum (`Low` = per-face, `High` = per-chunk). |
| 8 | Biome map ownership | `IBiomeProvider` registry-style interface, open to new biomes (Mushroom Land etc) as data. |
| 9 | v1 scope | Expanded: seasonal color, snow accumulation, wetness, stiffness, footprints, burn, dedicated snow system with tracks. |

### New open questions raised by the locked-in scope

1. **Snow renderer approach** — separate mesh layer per chunk vs parallax/POM in terrain shader vs combination. Defer to Phase F design doc, but flag now because terrain shader complexity might be affected (Phase B).
2. **Fire event source** — where do burn events originate? Lightning strikes (we have storm system), player-placed torches, weapon hits? Need an event hook list before Phase E.
3. **Season clock** — is "season" global to the planet, or per-latitude (northern hemisphere winter while southern is summer)? Latter is cooler and easier to express in `SeasonalState.R` per-chunk; just want to confirm intent.
4. **Quality settings menu** — does one exist yet, or am I building it as part of Phase D? If building, what's the UI framework — UI Toolkit, uGUI, IMGUI?
5. **Half-chunk seam overlap math** — needs careful spec for how state-texture sampling crosses face boundaries (cube-face UV mapping is non-trivial near edges). Will be detailed in the Phase A chunk skeleton design doc.

### Next deliverable

**Chunk skeleton design doc** at `docs/design/2026-05-30-chunk-skeleton.md`. Contents:

- `IPlanetSurfaceProvider` interface + `PerFaceSurfaceProvider` (existing) + `ChunkedSurfaceProvider` (new) class outlines
- `PlanetChunk` data class with all fields
- `CubeFaceTopology` static table (face-edge → neighbor face + axis remap)
- Hash-bit encoding spec (bit layout, root convention)
- Neighbor lookup algorithm with cross-face support
- Half-chunk seam overlap UV math
- Lifecycle state machine (Pending → Active → Subdividing → Active+Children → Unloading)
- Awaitable-based generation flow (no coroutines, no Thread+lock)
- Memory budget table
- Integration plan with existing `Planet.cs`, `TerrainFace.cs`, `ShapeGenerator.cs`

Open questions 1-5 above don't block the chunk skeleton doc — they're for Phase B+ design docs. The chunk skeleton can proceed.

---

## Next step

Write `docs/design/2026-05-30-chunk-skeleton.md` per the spec above. Bryan reviews. Implementation begins after sign-off.
