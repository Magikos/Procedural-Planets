# Grass & Cloud Reference-Project Recommendations — 2026-07-04

What the `local-only/` reference projects do that our implementations don't, filtered to
things worth porting. Sources examined:

- **Clouds**: `Clouds-master` (Sebastian Lague — our cloud shader's ancestor),
  `cloud_rendering_unity_guide.md` (Harris impostor/precomputed-scattering paper digest).
- **Grass**: `UnityURP-InfiniteGrass-main` (compute placement + URP indirect draw),
  `GrassFlow` (ripple sim, paint, empty-chunk skip), `Interactive-Grass-Shader-main`
  (trivial — nothing to take), `rendering_countless_blades_waving_grass_unity_guide.md`
  (GPU Gems ch.7 digest), `gdc_2021_procedural_grass_in_got.pdf` (Ghost of Tsushima).

Ranked by leverage. Each item: what the reference does, what we do, verdict.

---

## Top tier — do these

### R1. CLOUD — blue-noise ray offset (Lague `Clouds.shader:296`)

Lague jitters each ray's start with a **tiled blue-noise texture**:

```hlsl
float randomOffset = BlueNoise.SampleLevel(samplerBlueNoise, squareUV(i.uv*3), 0);
randomOffset *= rayOffsetStrength;
```

We use hash-based white noise (`Hash12`/IGN blend, `Cloud.shader:307-310`). This is
directly aimed at the one complaint still open after the temporal revert: **grain**. White
noise clusters — neighboring pixels can land the same offset, forming visible clumps and
worms. Blue noise pushes neighboring pixels' offsets maximally apart, so the same step
error reads as smooth film grain instead of blotches. It is the standard non-temporal
answer (Lague shipped it; every Nubis/Horizon derivative uses it), and we skipped it when
porting.

**Port**: 64×64 or 128×128 tiled blue-noise texture (generate once via void-and-cluster in
an editor script, or ship a known-good PNG), bind as `_CloudBlueNoise`, replace
`pixelJitter` with a tiled sample:

```hlsl
float pixelJitter = SAMPLE_TEXTURE2D_LOD(_CloudBlueNoise, sampler_CloudBlueNoise,
    pixel / 128.0, 0).r;   // pixel = floor(uv * _ScreenParams.xy), texture set to Repeat
```

Keep the existing per-step decorrelation hash seeded from this. Low effort, targets the
visible problem, zero perf cost.

### R2. CLOUD — skip the detail-noise sample in empty air (Lague `Clouds.shader:199-214`)

Lague computes base shape density first and **only samples the detail texture when
`baseShapeDensity > 0`**:

```hlsl
float baseShapeDensity = shapeFBM + densityOffset * .1;
if (baseShapeDensity > 0) {
    float4 detailNoise = DetailNoiseTex.SampleLevel(...);   // only paid near/inside cloud
    ...
}
return 0;
```

Our `SampleCloud` (`Cloud.shader:154-173`) samples **both** 3D textures whenever
`condensation > 0.001` — including the majority of march samples that land in empty air
inside a cloudy weather cell. Restructure to Lague's order: shape FBM → threshold → only
then detail erosion. Most rays spend most steps in air; this halves 3D-texture bandwidth
for them. (Our `CLOUD_QUALITY_LOW` path already proves the shader works with detail
skipped.) Pairs with the yesterday's-audit B1 finding (hoist the per-step dynamics
sample) — together they make the empty-air step nearly free.

### R3. GRASS — move blade lighting to the vertex stage (InfiniteGrass `GrassBladeShader`)

InfiniteGrass computes **all** lighting — SH ambient, main light + shadow attenuation,
additional lights, fog — in the vertex shader and passes one interpolated color; its
fragment shader is `return half4(IN.color,1)`.

Our `Grass.shader` fragment runs, **per pixel**: planet sun direction math, daylight
curves, backlit pow, night blend, and worst of all `CloudShadowFactor` — which is a 3-step
march each doing a weather-map sample plus a 3D shape-noise sample
(`CloudShadows.hlsl:121-129`). Near-field grass covers most of the screen at ground level;
that's ~6 texture fetches × every grass pixel for a value that's effectively constant
across a 20 cm blade.

**Port**: compute `cloudShadow`, `daylight`, `surfaceDirect`, `sunDir` (everything that
varies per-blade, not per-pixel) in `GrassVertex` and interpolate. Keep in the fragment
only what genuinely needs per-pixel rates: the dither clip, cluster-card clip pattern, and
(optionally) the `abs(dot(normalWS, sunDir))` wrap term using the interpolated normal.
Biggest available grass GPU win, no visual downside at blade scale — a blade is thinner
than the lighting gradient it samples.

---

## Second tier — worth doing, needs a decision or more effort

### R4. GRASS — GPU frustum compaction for the near field (InfiniteGrass compute, lines 89-98)

InfiniteGrass appends a position only after a clip-space frustum test (with 1.1×/1.5×
slack for sway):

```hlsl
float4 absPosCS = abs(mul(_VPMatrix, float4(positionWS, 1.0)));
if (absPosCS.z <= absPosCS.w && absPosCS.y <= absPosCS.w * 1.5 && absPosCS.x <= absPosCS.w * 1.1 ...)
    _GrassPositions.Append(positionWS);
```

It can do this because it **rebuilds the whole buffer every frame**. Our near field
deliberately persists its buffer across page shifts, so culling at placement time would
leave holes when the camera rotates (the chunk path documents exactly this,
`GrassChunkDispatcher.cs:216-219`) — and indeed our near-field kernel has a
`FrustumRejected` stat that nothing increments. Net effect today: all ~1M placed instances
(full 360° disc) go through the vertex stage every frame; behind-the-camera blades are
~40-60% pure waste.

**Recommended shape** (keeps our persistence, takes their win): leave placement as-is; add
a tiny per-frame **compaction kernel** — read the persistent blade buffer, frustum-test
each instance with slack, append survivors to a second buffer driving the indirect draw.
1M-thread trivial kernel ≈ 0.1-0.2 ms; cuts vertex work roughly in half whenever the
camera isn't spinning. The alternative (per-frame full rebuild, InfiniteGrass-style) also
kills the page-shift machinery and its stats plumbing but re-rolls placement cost every
frame — the compaction pass is the cheaper retrofit. Measure vertex-stage cost first
(F10 + `FrameTimingCounters`) so the win is provable.

### R5. GRASS — clump identity from a spatial hash (Ghost of Tsushima GDC 2021)

GoT's signature look: blades belong to **clumps** (Voronoi cells); each clump owns shared
parameters — facing direction, base height, tilt — so blades inside a clump agree and
clumps differ. We have per-biome `ClumpStrength` and smooth patch noise
(`SmoothPatchNoise`), which modulates *amplitude* but gives every blade an independent
identity — our fields read as uniform fuzz rather than tufts.

**Port sketch** (placement computes, both):

```hlsl
uint clumpId = HashUint(cellIndex.x / CLUMP_CELLS * 73856093u
             ^ cellIndex.y / CLUMP_CELLS * 19349663u ^ faceSeed);
float clumpHeight = lerp(0.8, 1.25, Hash01(clumpId ^ 0x11111111u));
float2 clumpLean  = (float2(Hash01(clumpId ^ 0x22222222u), Hash01(clumpId ^ 0x33333333u)) - 0.5)
                  * biome.Shape.w;   // ClumpStrength drives lean coherence
height *= lerp(1.0, clumpHeight, biome.Shape.w);
```

plus pass `clumpLean` per blade (fits in the existing `Color.a` or a repacked field) and
add it to the blade's lean vector in `Grass.shader`. Bounded effort, big step toward the
"desired overall look" reference, and it uses the `ClumpStrength` biome parameter that
currently does very little.

### R6. GRASS — trail/bend render-texture to replace the 8-slot interactor cap (InfiniteGrass `GrassSteppedTrailShader`, GoT equivalent)

InfiniteGrass stamps interactors into a camera-following world-space RT that decays over
time; blades sample it for bend/flatten. Ours packs at most **8** live interactors + fading
release samples into one buffer (`GrassInteractorRegistry`), and the general-audit already
flagged trail starvation when the roster fills (finding D6). A single R8/RG8 RT
(~512² covering the near field, stamped by interactors, faded per frame, sampled in the
blade vertex shader) removes the cap entirely and gives persistent crush trails for free —
it composes with the path-wear system rather than replacing it (wear = permanent, trail RT
= seconds-scale recovery).

**Defer until** the character controller / creatures exist; the debug sphere doesn't need
it. When it lands, drop the release-sample machinery from `GrassInteractorRegistry` —
the RT subsumes it.

---

## Third tier — cheap polish / parked ideas

### R7. CLOUD — cubed edge-erosion weight (Lague `Clouds.shader:207-210`)
Lague erodes with `(1-shapeFBM)³` so detail noise eats edges but leaves cores solid:
`float detailErodeWeight = oneMinusShape * oneMinusShape * oneMinusShape;`
Ours uses linear `(1.0 - density)` (`Cloud.shader:172`). One-line experiment; tends to read
as puffier cumulus. Try it during the next cloud-look pass, keep whichever capture wins.

### R8. GRASS — height-masked specular sheen (InfiniteGrass `ApplySingleDirectLight`)
`directSpecular *= positionY * 0.12` — a faint tip-only specular pop. We have zero
specular on grass. Two lines in the (post-R3, vertex-stage) lighting path; makes dewy/lush
biomes read glossier. Taste call.

### R9. GRASS — ripple impulses (GrassFlow `AddRipple`/`UpdateRipples` kernels)
Expanding ring forces that propagate through the field (explosions, landings, magic AOEs).
Natural extension of the interactor system once R6's RT exists (stamp rings into the same
texture). Park until gameplay needs it.

### R10. CLOUD — precomputed cloud ambient into night lighting (Harris guide §10-12)
The impostor paper's durable idea for us isn't impostors (wrong tool for a ray-marched
spherical shell) — it's that **cloud scattering is an ambient light source**: overcast
nights should be brighter (city-glow-less bounce) and clear nights darker. We already have
`_NightAmbientIntensity`; modulating it by average local cloud coverage (one weather-map
sample on the CPU per frame) is a cheap mood win. Park for the night-lighting pass.

---

## Explicitly not recommending

- **Impostor clouds** (Harris paper): built for discrete cumulus billboards; our
  full-shell ray march + weather sim is a different architecture and already cheaper than
  impostor re-render heuristics at our step counts.
- **InfiniteGrass's per-frame buffer realloc** (`argsBuffer = new ComputeBuffer(...)` in
  `LateUpdate`): an anti-pattern we already avoid — nothing to learn.
- **GPU Gems cross-quad textured grass** (Pelzer guide): our procedural cluster cards
  already implement the same distant-LOD idea without texture authoring.
- **GrassFlow's `EmptyChunkDetect`**: our per-chunk instance-count readback already gives
  the same signal; a zero-count indirect draw is near-free.
- **Wind texture scrolling** (InfiniteGrass/GoT): our analytic travelling-wave wind with
  gust envelope is equivalent quality and planet-surface-aware (tangent-plane projected);
  a scrolling 2D texture doesn't wrap a sphere without the cube-face plumbing we'd have to
  build.

---

## Suggested order

| # | Item | System | Effort | Why now |
|---|------|--------|--------|---------|
| 1 | R1 blue-noise ray offset | Cloud | S | directly attacks the remaining grain complaint |
| 2 | R2 detail-noise early-out | Cloud | S | free march speedup, enables higher step counts |
| 3 | R3 vertex-stage blade lighting | Grass | M | biggest grass GPU win, no visual cost |
| 4 | R7 cubed erosion | Cloud | S | one-line look experiment while in the file |
| 5 | R5 clump identity | Grass | M | visual identity, activates dormant ClumpStrength |
| 6 | R4 frustum compaction | Grass | M | measure first, then ~halve vertex work |
| 7 | R8 tip specular | Grass | S | polish, rides on R3 |
| 8 | R6 trail RT | Grass | L | when characters land |
| 9 | R9 ripples | Grass | L | when gameplay needs it |
| 10 | R10 cloud ambient at night | Cloud | S | night-lighting pass |
