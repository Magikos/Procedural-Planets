---
name: project-current-focus
description: "As of 2026-06-06: Phase B (biome textures K=4) shipped. Active work: biome model overhaul (BiomeOffset → climate model lat+alt+noise → Voronoi assignment) per Bryan's 5-step plan. Then texture/look work using Synty assets."
metadata:
  node_type: memory
  type: project
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
---

**Phase B IS shipped on branch `phase4-biomes`** (confirmed by code 2026-06-06):
- K=4 biome blending (NOT K=2 as the Phase B draft suggested — see [[biome-climate-overhaul]] §0.1).
- Three per-chunk textures: `BiomeBlendedColorTexture`, `BiomeIdsTexture` (R/G/B/A = id1..id4), `BiomeWeightsTexture` (R/G/B/A = w1..w4). Both ID + weights are POINT filter to avoid invalid intermediate IDs; shader does manual 4-corner bilinear blend.
- Per-biome `Texture2DArray` triplanar sampling for albedo + normal + ARM is live.
- `_SurfaceStateMask` per-chunk binding + Phase E stub in `IPlanetSurfaceProvider`.

**Active arc — biome model overhaul + texture/look work (Bryan's 5-step plan, 2026-06-06):**

1. **Biome model overhaul** — finishing biome normalization/blend. Design at [`docs/design/2026-06-05-biome-climate-overhaul.md`](../../docs/design/2026-06-05-biome-climate-overhaul.md). Three slices:
   - **1a: SHIPPED 2026-06-06** ✓ — `BiomeOffset: Vector3` added to `BiomeDefinition` under "Placement noise" header; all 15 biome SOs populated with unique pseudo-random values in ~1000-10000 range. Build clean. No consumers yet — grass placement (step 4) and props (step 5) will read it. Slice log: `docs/agent-conversation/2026-06-06-biome-1a-biome-offset.md`.
   - **1b: NEXT** — Climate model overhaul. `TemperatureProvider`/`MoistureProvider` switch from pure noise to `latitudeBase(|y|) +/- altitudeLapse(elevation) + noise`. New fields on `BiomeSettings`: `AltitudeTemperatureDropConstant`, two latitude `AnimationCurve`s (temp and moisture — moisture curve emulates Hadley/Westerlies bands: wet equator, dry subtropical, wet temperate, dry polar). New console commands `climate.temp-noise-scale`, `climate.altitude-lapse`, `climate.show-bands` (debug viz). **Open decision when picking up: ship inline (net additive — old behavior preserved behind init defaults) OR write short design doc first per audit workflow?** Bryan was about to choose when the session ran out.
   - 1c: Voronoi + domain warp + 5-iter cleanup biome assignment. Behind feature flag, A/B vs current direct lookup. The 5-iter cleanup is THE fix for thin-stripe biomes (Bryan's specific concern 2026-06-06).
2. **Grass on/off console toggle** — small enabler so Bryan can judge biome look without grass overlay.
3. **Texture/look work** — multi-variant Synty texture blend per biome (using POLYGON Meadow Forest / Tropical Jungle / Swamp Marshland packs at `D:\UnityExtractedPackages\Sorted`); snow/slope/coast overrides; stylization pass; URP volume tuning. Reference look: Genshin Impact, Zelda BotW, Valheim.
4. **Grass tuning** — size/color/placement variation, especially fade near biome edges. Implies per-grass-species Gaussian climate niche (analogous to tree species).
   - **4a-1 SHIPPED 2026-06-06** ✓ Foundation: `GrassTintDryShift` + `GrassTintLushShift` fields on `BiomeDefinition` (Color.white defaults, all 15 biome SOs populated); `GrassBiomeTintConfig` + `GrassPlacementClimateBinding` DTOs in new `GrassPlacementDtos.cs`. **First demonstration of [[feedback-settings-dto-pattern]] in code.** Slice log: `docs/agent-conversation/2026-06-06-grass-climate-color-foundation.md`. Design doc: `docs/design/2026-06-06-grass-climate-color.md`.
   - **4a-2 NEXT** — Per-chunk `ChunkClimateTexture` bake plumbing. Adds RG16 (temp01, moisture01) per chunk via `BiomeMapBaker` extension; uses existing per-vertex `CpuBiomeData.x/.y`. ~16 MB for 2000 active chunks. No consumer yet — foundation only, same shape as 4a-1.
   - **4a-3 PLANNED** — Placement compute consumption: build `GrassBiomeTintConfig[]` at init via `From(BiomeDefinition)` factory; bind climate texture; sample + apply dry/lush weighted blend; per-blade `blade.Color` write. Plus `grass.dry-shift` / `grass.lush-shift` console commands + `BiomeGrassClimateShift` debug mode. **First visible result.**
   - **4b SHIPPED 2026-06-07** ✓ **Grass interactors** — `IGrassInteractor` interface + `GrassInteractorRegistry` (static, max 8 active, ComputeBuffer-backed) + `GrassInteractorBootstrap` (lazy-spawn MonoBehaviour for per-frame upload) + `DebugGrassInteractor` MonoBehaviour + `CameraFollowGrassInteractor` (sea-level surface snap) + `grass.interactor-*` console commands. Stub `SampleGrassInteractorBend` in `GrassInteractors.hlsl` replaced with real implementation (tangent-plane projection, smoothstep falloff). **Second use of [[feedback-settings-dto-pattern]]** via `GrassInteractorSnapshot`. Design doc: `docs/design/2026-06-07-grass-interactors.md`. Slice log: `docs/agent-conversation/2026-06-07-grass-interactors.md`. Validate in Unity with `grass.interactor-spawn`. **Spherical gravity / character controller / 3rd-person camera explicitly deferred to a separate big arc — interactor system is character-agnostic.**
5. **Props** — rocks, bushes, trees. Tree species get full Gaussian niche from biome-climate-overhaul §3.3.

**Known perf characteristics (do not "optimize" without context):**
- **Voronoi global-field build is ~5 seconds at default `VoronoiSeedCount = 2048`.** That's measured one-time cost during planet generation, not steady-state. F10 sidecar reports it as `buildMs`. Generation is intentionally allowed to be expensive — parallelize for load speed if/when needed, but the spec is "do correct work once at gen, fast lookups at runtime."
- **`MoistureNoiseStrength` has zero effect while `MoistureLatitudeInfluence = 0`** — the legacy path takes `lerp(legacy, band, 0) = legacy` and discards the band-derived noise contribution. Tooltip updated 2026-06-06 to call this out; the parameter remains live for the latitude-band moisture path.

**Locked decisions (do not re-litigate):**
- **K=4** is correct and already shipped — do not change.
- **5-iter Voronoi seed cleanup** is required (not optional) — it's the explicit fix for the thin-stripe biome problem.
- **Generation is one-time, not per-frame** — expensive but correct algorithms are fine; parallelize via Burst/compute/Awaitable for load time but don't constrain to a frame budget.
- **K=3 for trees, with single grass species per biome → upgraded:** grass DOES get Gaussian niche (closes design doc open question #6).

**Known issues still queued (do not block this arc):**
- [[normal-mapping-flat]] — terrain reads flat under lighting; data pipeline confirmed working, lighting compression likely the cause. May be addressed by texture work in step 3.
- [[chunk-biome-seam]] — faint chunk-boundary seams in current kernel-based bake. **Closed by construction once 1c (Voronoi) ships** — Voronoi assignment is global, no kernel boundaries.

**Reference materials for texture work:**
- [[reference-planet-architect]] — biome/climate/vegetation reference
- Synty extracted packs at `D:\UnityExtractedPackages\Sorted` — Meadow Forest, Tropical Jungle, Swamp Marshland, Prototype
