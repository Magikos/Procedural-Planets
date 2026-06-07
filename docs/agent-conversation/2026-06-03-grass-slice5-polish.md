# 2026-06-03 — Grass Slice 5: Visual Polish (Wind / Per-Clump / Biome / Counters)

> **Architecture update (2026-06-07):** Mid-field cards were subsequently
> implemented and rejected by F10 visual validation. Current production uses
> near grass -> chunk grass -> far terrain blanket.

**Status:** Slice 5a (wind) shipped. Awaiting Bryan F10 validation before proceeding to 5b.

**Context:** Slice 4 arc concluded (regression fix in [2026-06-03-grass-slice4b-regression.md](2026-06-03-grass-slice4b-regression.md)). Codex proposed slice 5 = visual polish, Bryan approved order: wind → per-clump variation → softer biome transitions → per-layer perf counters.

Distant-hill pain reported as "much better, may need more polish later but good enough for now" — so mid-field impostors (slice 4c) stay deferred. Multi-face dispatch stays disabled until budgeted seam ownership is designed.

## Slice 5a: Wind — shipped 2026-06-03

### What changed

[Grass.shader](../../Assets/Graphics/Shaders/Grass.shader) only. No C# changes.

1. Declared global uniforms: `float3 _WindDirection` + `float _WindSpeed`. Both already maintained by `WeatherManager` and consumed by Cloud / Ocean / Precipitation shaders — grass just reads them.
2. Added `ComputeWindOffset(rootWs, upWs, height, t, seed)` helper in the vertex shader.
3. Wired the wind offset into the spine computation alongside `interactorBend`.

### Wind algorithm

Three phase sources combine for natural-looking sway:

1. **Clump phase** via existing `SmoothPatchNoise(relRoot, 6.0, 0.0)` — whole ~6m patches sway together (large gust waves)
2. **World-position phase** along wind direction — `dot(rootWs, windTangent) * 0.18` — gust visibly travels
3. **Per-blade hash phase** — small individual variation so blades aren't perfectly lockstep

Wind direction is projected onto the local tangent plane so blades sway parallel to the surface (not into/out of the ground). If wind is perpendicular to surface (rare at low latitudes), no bend.

Tip-only via `t * t` scaling — roots stay put. Magnitude clamped to 35% of blade height so violent wind doesn't fold blades past horizontal.

### Validation guidance

1. **Visible sway when standing still**: blades should oscillate gently in the `_WindDirection`. Default `WindDir = (1, 0, 0.3)` in `WeatherManager` so motion should be roughly +X with a slight +Z component.
2. **Gust waves visible across the field**: nearby patches should sway in phase; patches a few meters away should be slightly out of phase. Should look like wave fronts traveling along the wind direction.
3. **No swimming when camera moves**: wind uses `rootWs` as a stable input, not camera-relative anything. Walking around should not shift the phase pattern.
4. **FPS unchanged**: wind adds ~12 ALU ops per blade vertex. ~3M visual blades × 54 verts × 12 ops = ~2B ops/frame. Trivial on modern GPUs. If FPS dropped >5%, something else is going on.
5. **Strong wind cap**: if you can manually crank `_WindSpeed` via the WeatherManager, even at very high values the blades should never fold past their tips (the 35% height clamp).

### Risks / notes

- **First-frame import**: `_Time.y` is a Unity built-in available in all shaders, no setup needed.
- **`_WindDirection` zero vector**: if WeatherManager hasn't initialized yet, the global may be (0,0,0). `SafeNormalize` falls back to `(1,0,0)` so we won't get NaN.
- **Wind under interactor bend (slice 6)**: when characters bend grass, the wind continues to sway the bent grass. That stacks correctly — both are tip displacements added to the spine.
- **Mid-field shader (future slice 4c)** will want the same wind. The helper is currently inline in Grass.shader; extracting to `Includes/GrassWind.hlsl` is trivial if slice 4c ever ships.

### Build status

Both `ProceduralPlanets.Core` and `ProceduralPlanets.Planet` build clean (only pre-existing `CS0414` warning).

### What Bryan should test

1. Take a Grass F10 from a low-altitude grassland view.
2. Watch the scene live for ~10 seconds before capturing — wind motion is the validation, not the still image.
3. Confirm: blades sway in a coherent direction, patches travel waves, no swimming/popping on camera move.
4. F10 numbers should be unchanged from previous baseline — wind is shader-only, no compute or controller changes.

If wind validates, proceed to **slice 5b** (per-clump height/width/color variation, building on the same clump-hash pattern).

## Slice 5b: Per-clump variation — pending

Plan: extend the existing per-blade variation in Grass.shader to use a clump-level seed (multi-meter `SmoothPatchNoise` already in the shader) so adjacent blades get coherent variation rather than per-blade noise. Specifically:

- Per-clump height multiplier (~±20%) so areas have visibly "tall" and "short" patches
- Per-clump width multiplier (~±10%)
- Per-clump tint shift (toward the existing biome tint range, not a new color axis)

Per-blade noise stays for the fine detail; per-clump rides on top.

Scope: ~30 lines in Grass.shader. Same file, no controller/compute changes.

## Slice 5c: Softer biome transitions — pending

Compute-side. The `BlendGrassParams` function already blends top-K biome weights, but at biome boundaries we may still see visible tonal lines. Worth examining whether:

- The issue is the kernel math (e.g., weight power curve too sharp)
- The biome map resolution at chunk boundaries (texel-level aliasing)
- Or the dominant-biome `Tint` being chosen as a discrete biome rather than a continuous blend

Diagnostic step before fix: capture an F10 at a known biome transition (grassland → forest) and inspect the `BiomeMapBlend` debug mode.

Scope: TBD pending diagnosis, likely ~20-40 lines compute.

## Slice 5d: Per-layer perf counters — pending

Add to F10 sidecar:
- Near-field draw cost (vertex count, fragment area estimate)
- Chunk-path draw cost (same)
- Terrain blanket overlay cost estimate (per-pixel ops × terrain pixel coverage)
- Frame budget breakdown — what % of frame time is grass vs. terrain vs. atmosphere vs. clouds

Scope: ~30-50 lines, controller + F10 module. Ships last since it doesn't affect visuals.

## Asking Bryan

After his next F10 of wind:

1. **Wind looks right** → I proceed to slice 5b (per-clump variation)
2. **Wind looks wrong** (too strong, too weak, wrong direction, swimming, etc.) → tell me what's off, I tune the constants
3. **Wind is fine but something else regressed** → pull request F10 + describe what changed
