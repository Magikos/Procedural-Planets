# Distant grass — the "fake grass carpet" at range

Date: 2026-08-12. Branch: `character-controller-mvp`. Author: Claude (overnight, self-serve captures).

## The report

Bryan, standing at the `Lake1` teleport looking across the lake:

> the grass [isn't] drawing far enough… on the far side of the lake there is no grass — even
> though if I walk over there, there is grass. From this side it looks like dirt. We don't have to
> draw individual blades, but we should match the grass color patterns — like a carpet, and it
> shouldn't be a solid color — it should look like little blades even if it's just drawn that way.

## TL;DR

**The "fake grass at distance" mechanism already exists**, is already blade-textured (procedural
fiber/fleck noise, not solid color), and is **turned off by default**. It is the *far grass-surface
overlay* ("blanket") painted onto the terrain by `PlanetVertexColor.shader`, gated by
`PlanetGrassCoordinator._grassBlanketEnabled` (currently `false`).

It was parked for one reason: at grassy↔arid biome borders it painted a **hard green stripe** (the
"biome-edge line"). The proposed fix here softens the coverage gate so the transition reads as a
gradient instead of a band.

**Important caveat for `Lake1` specifically:** this lake sits in an **arid biome**. The far shore is
genuinely low/zero grass-density terrain (see the biome-density table below), so the carpet — even
when on — correctly leaves most of it dry. The carpet's real payoff is in true grassland biomes, not
at this particular lake. "Far shore = dirt" here is *partly correct*, not purely a bug.

## How distant grass works (three layers)

`PlanetGrassCoordinator` owns three grass layers:

| Layer | What it is | State | Range |
|---|---|---|---|
| **Near-field** (`GrassNearFieldController`) | real 3D blades, camera-centered | **on** | ~0–40 m |
| **Chunk** (`GrassPlacementController`) | real 3D blades, chunk-following | off | mid |
| **Blanket / far overlay** | grass **color + fiber painted onto the terrain surface** | **off** | ~24 m → horizon |

So beyond ~40 m there are no blades and no painted carpet → you see **raw terrain albedo** (here,
orange dirt). That is the whole of Bryan's "looks like dirt at distance."

The blanket is a terrain-shader feature, not geometry: `PlanetVertexColor.shader ›
ApplyGrassSurfaceAlbedo()` blends a grass color over the ground where a biome is grassy, broken up by
`ValueNoise3D` fiber/fleck/patch terms (`_GrassFarOverlayFiberStrength = 0.65`) so it reads as a
blade-like carpet rather than a flat green wash — exactly what Bryan described.

Runtime levers (already wired, `grass.*` console):
- `grass.layer Blanket true|false` — the master on/off (default off).
- `grass.overlay-strength <0-1>`, `grass.surface-brightness <0.3-1.5>`, `grass.surface-saturation <0-1>`.
- `grass.debug-layer-colors true` — paints the carpet **red** wherever it covers (great for seeing extent/stripe).

## Why it's off: the biome-edge stripe

`EvaluateGrassOverlay()` gated coverage with a **hard** density cut:

```hlsl
float rawCoverage = saturate(grass.density * slopeKeep * waterKeep);
float envCoverage = smoothstep(0.38, 0.72, rawCoverage);   // narrow -> hard band
```

A narrow `[0.38, 0.72]` window maps a narrow *density* range to a narrow *spatial* band. At a
grassy→arid border (density falling from 0.8 to 0), that band is a thin ribbon that reads as a hard
green line. `grass.debug-layer-colors true` shows it clearly: a thin red stripe along the far
waterline, nothing above it (capture `grass_layercolors.png`).

## Evidence (self-serve captures, frozen noon, `Lake1`)

| Capture | What it shows |
|---|---|
| `grass_baseline_off.png` | Blanket off. Near tufts only; far shore is orange dirt. |
| `grass_blanket_on.png` | Blanket on (old gate). Far shore only marginally greener. |
| `grass_layercolors.png` | Carpet coverage in **red** — a thin band on the far waterline = the stripe. |
| `grassy_on.png` | Panned along the shore — the whole region is arid/orange; almost no true grassland in view. |
| `lake1_preview.png` | Current live-session preview (blanket left **on**). |

Numeric far-shore band (mean RGB): off `(.442,.343,.066)` → on `(.416,.337,.078)`. The carpet
*does* add green, but the region is fundamentally arid so the delta is small.

**Runtime biome grass densities** (read back from `_BiomeGrassParams`, 18 biomes):

- Grassy biomes (ids 5–10, 12, 13, 17): density **0.80–0.95**. Savanna/dry-grass biomes are *also*
  0.80 but with khaki tints (e.g. `(0.55,0.62,0.30)`).
- Desert / rock / water / tundra (ids 0–4, 11, 14–16): density **0.00** — no carpet authored, stays bare.

The `Lake1` far shore did not paint even with the carpet on → it is a **density-0 (arid) biome**, not
grassland. So the carpet correctly leaves it dry.

## Proposed fix — soft proportional gate (in the working tree, NOT committed)

`Assets/Graphics/Shaders/PlanetVertexColor.shader ~L788`:

```hlsl
float rawCoverage = saturate(grass.density * slopeKeep * waterKeep);
float coverageToe = 0.06;   // was 0.38
float coverageFull = 0.85;  // was 0.72
float envCoverage = smoothstep(coverageToe, coverageFull, rawCoverage);
```

Wide gentle ramp → the grassy→arid transition spreads into a broad gradient (no hard band), sparse/dry
biomes read as a faint dry-grass tint instead of bare dirt, grassland interiors still reach full
coverage, and true desert (density ~0) stays bare. High-density biomes are essentially unchanged
(`smoothstep(0.06,0.85,0.8) ≈ 0.96` vs old `1.0`); only borders and sparse biomes shift.

**Status: unverified render.** The editor's compile pipeline was stuck (`isCompiling` never cleared —
HotReload was looping on the uncommitted `AssetBench` scripts that aren't in any asmdef), so the
reimported shader could not be confirmed live, and this arid lake has no grassland in frame to judge
the stripe against. **Verify on a real grassland biome before committing**: `grass.layer Blanket true`,
fly to grassland, toggle, and check the borders with `grass.debug-layer-colors`.

## Recommendation

1. **Enable the soft-gate carpet** (`_grassBlanketEnabled = true` + the shader diff above) — it is the
   intended "fake grass at distance," already blade-textured, and the soft gate addresses the original
   stripe objection. One-line default flip once you've eyeballed a grassland border.
2. **`Lake1` will still read mostly dry** — it's an arid biome. If you want *this* lake lusher at range,
   that's a **content/biome** question, not a carpet question: widen the green `LakeShore` ring, or bias
   lake surroundings toward a wetter biome, so there's actually grass density for the carpet to paint.
3. **Unrelated but worth a cleanup:** the `AssetBench` C# in `Assets/Scripts/Planet/AssetBench/` and
   `Assets/Tests/EditMode/AssetBench*` is uncommitted and not in an asmdef, which sends HotReload into a
   recompile loop (stuck `isCompiling`) and makes shader/script iteration unreliable. Commit it into an
   asmdef or stash it.

## Live session state left for review

Blanket **on** (preview, resets on restart), debug viz off, time frozen at noon, camera at `Lake1`.
Toggle off with `grass.layer Blanket false`; unfreeze with `time.freeze false`.
