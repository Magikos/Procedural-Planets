# Water data contract — 2026-08-18

Task **W4** of [2026-08-17-water-architecture-plan.md](2026-08-17-water-architecture-plan.md).

Every value the water system moves between stages, with its range, units, producer and every consumer.
Three carriers exist: the **mesh vertex colour**, the **volume prepass RT**, and the **water interface RT**.
Changing any field means changing every consumer listed here.

---

## 1. Mesh vertex colour (`COLOR`, float4, 0..1)

Produced once per generation by `WaterMeshBuilder.AddVertex`, stored on the `WaterBodies` mesh.

| Channel | Name | Range | Units | Meaning |
|---|---|---|---|---|
| R | `depth01` | 0..1 | normalised | `clamp01(depthMeters / DeepDepth)`. Water column depth below the surface at this vertex. `DeepDepth` is already distance-scaled (`WaterDto.DeepDepth * DistanceScale`), so `depth01` is resolution-independent. |
| G | `shore01` | 0..1 | normalised | `clamp01(shoreDistanceCells * cellWorldSize / ShoreRange)`. 0 exactly at the coastline, 1 once further than `ShoreRange` from any dry cell. Vertices with no dry neighbour anywhere read 1. |
| B | `body01` | 0..1 | class | Body factor. **0 = lake, 1 = ocean.** Intended as a class, not a gradient — see §4. |
| A | `temperature01` | 0..1 | normalised | Water temperature from the climate provider. Feeds the freeze curve only. |

Shoreline clip vertices (the edge-interpolated ones) are written with the fixed
`shorelineEdgeDepth` / `shorelineEdgeShore` constants rather than sampled values — **4.65% of mesh
vertices**, and they have no counterpart in the retained face grid, which matters for W6.

**Consumers:** `Ocean.shader` `vert`/`frag` (all four), `WaterVolumePrepass.shader` `Vert` (all four),
`WaterMeshBuilder` itself (freeze statistics).

---

## 2. Volume prepass RT — `_WaterVolumeData`

`R16G16B16A16_SFloat`, produced by `WaterVolumePrepass.shader`, consumed by `WaterVolume.shader`.

| Channel | Name | Range | Units | Meaning |
|---|---|---|---|---|
| R | `forwardDepth` | 0..far | metres | `max(-positionVS.z, 0)` at the **displaced** surface. Distance from the camera to the water surface along view-forward. Water does not write to the depth buffer (`ZWrite Off`), so this is the only record of where the surface is. |
| G | `depth01` | 0..1 | normalised | Passthrough of vertex colour R. |
| B | `shore + kind` | 0..2047 | **packed** | `round(shore01 * 511) * 4 + kind`. Two values in one channel — see §4. |
| A | `freezeFactor` | 0..1 | normalised | `EvaluateFreezeFactor(temperature01, body01)`. 1 = fully frozen. |

Since 2026-08-18 the prepass **displaces** by `ComputeWaterVertexDisplacement`, identically to
`Ocean.shader`. Before that it rasterised the undisplaced shell, so `forwardDepth` described a surface
the player never saw and every read taken against it misregistered by up to the swell amplitude (D9).

**Consumers in `WaterVolume.shader`:**

| Read | Site | Uses |
|---|---|---|
| `max(.g, .b)` | `WaterCoverageFromData` :92 | Screen coverage mask. `.b` keeps coverage alive at the shoreline where `depth01` falls to 0. |
| `.a` | :676 | `liquidContribution = 1 - freezeFactor`; suppresses caustics under ice. |
| `.b` | :677 | `lake01 = 1 - smoothstep(0.45, 0.55, .b)`. **Already thresholded — the volume treats the body factor as binary.** |
| `lake01` | :807 | Blends the opaque green pond body over the ocean colour. |

---

## 3. Water interface RT — `_WaterInterfaceTexture`

Same packing as §2, dilated. Consumed by `Atmosphere.shader`.

| Read | Site | Uses |
|---|---|---|
| `.r` | `WaterInterfaceFrontMask` :66 | `step(0.0001, forwardDepth) * step(forwardDepth, sceneDepth + 0.01)` — water in front of geometry. |
| `.r` | `CompositeDepthScaled` :104 | Pulls composite depth forward to the water surface so atmosphere integrates to the water, not the seabed. |

**Invariant: `forwardDepth` is non-negative, and 0 means "no water here".** Both sites use `step(0.0001, ·)`
as their validity test. Any scheme that signs or offsets channel R breaks the atmosphere composite.

---

## 4. The channel-B packing — D5 (**re-encoded 2026-08-19**)

Owned by `Includes/WaterVolumeData.hlsl`, which all three shaders now include. It holds the encode, the
decode, and the coverage formula — `WaterVolume` and `Atmosphere` had previously grown *separate copies*
of the same coverage expression against the same channels, with nothing able to report a drift.

```
packed = round(shore01 * 511) * 4 + kind      // max 2047, exact in fp16 (integers exact to 2048)
```

- `shore01` keeps **9 bits** (512 levels).
- `kind` gets **2 bits**: `WATER_KIND_LAKE 0`, `WATER_KIND_OCEAN 1`, 2 and 3 reserved for river and
  waterfall (W13/W14).

**Why shore01 gets the bits.** Coverage runs `shore01` through `smoothstep(0.0005, 0.018)` — a ramp that
lives entirely in the bottom few percent of the range. At 7 bits the shoreline feather would quantise to
about five steps. The kind enum needs no headroom by comparison; W15 calibrates per body *type*, not per
body instance, so the volume never needs a body id.

**Filtering is safe.** The RT is screen-sized and read 1:1 by a fullscreen pass, and `Atmosphere`'s
dilation is a **max-select** (`bestData = candidate`) rather than an average, so no packed value is ever
interpolated between texels.

**Coverage is bit-identical through the change.** `WaterVolumeCoverage` reconstructs the old
`shore01 * 0.45 + (isOcean ? 0.55 : 0)` term rather than switching to the cleaner
`step(0.0001, forwardDepth)` presence test. The ocean term was load-bearing: it is what kept coverage
alive on ocean pixels where both `depth01` and `shore01` fall to zero at the waterline. Moving to a
depth-based presence test changes the shoreline feather on every body at once, so it carries a
`ponytail:` marker and wants its own change with its own visual pass.

### The original defect, for the record

`R16G16B16A16_SFloat` gives four channels; the volume needs five values (`forwardDepth`, `depth01`,
`shore01`, `body01`, `freezeFactor`). `shore01` and `body01` share channel B.

The decode `lake01 = 1 - smoothstep(0.45, 0.55, shoreBody)` is exact **only while `body01` is near 0 or 1**:

| `body01` | `shore01` | `shoreBody` | Decodes as | Correct? |
|---|---|---|---|---|
| 0 (lake) | 0..1 | 0.00 .. 0.45 | lake | yes |
| 1 (ocean) | 0..1 | 0.55 .. 1.00 | ocean | yes |
| 0.37 | 0.0 | 0.20 | lake | defensible |
| 0.37 | 1.0 | 0.65 | **ocean** | **no** |

**Measured on the reference world (481,682 water vertices):**

| `body01` | Count | Share |
|---|---|---|
| exactly 0 (lake) | 2,668 | 0.55% |
| exactly 1 (ocean) | 478,916 | 99.43% |
| intermediate — all in [0.35, 0.40) | 98 | **0.02%** |

So the packing is sound today, and the volume's own `smoothstep(0.45, 0.55)` already declares that it
wants a binary class rather than a gradient.

**Resolved 2026-08-19 by the re-encode above.** The exposure was small, but the encoding failed *silently*
and would have broken outright the day a third body kind existed — which W13/W14 guarantee. Signing
channel R was rejected because §3's `step(0.0001, forwardDepth)` invariant forbids it; a second RT was
rejected because the volume composite measured as the entire GPU cost of the water.

**Guard retained.** `WaterMeshBuilder` still counts vertices with `0.05 < body01 < 0.95` into
`BuildStats.AmbiguousBodyVertices` and `PlanetWaterSurface` warns past 0.5%. It no longer guards a
decoding hazard — the encode now quantises to a kind regardless — but it is the only instrument that
reports the mesh producing a body factor that is neither lake nor ocean, which is a real signal once W5
introduces more body types.

---

## 5. Duplication status

| Value | Copies | State |
|---|---|---|
| Vertex displacement | `Ocean.shader`, `WaterVolumePrepass.shader` | **One** — `Includes/WaterDisplacement.hlsl` (D9 surface half) |
| `EvaluateFreezeFactor` | 2 HLSL + 1 C# | **Two** — one HLSL (source of truth), one CPU mirror in `WaterMeshBuilder`. Unavoidable: the mesh build decides ice coverage on a worker thread before any shader runs. Both sites carry a comment naming the other (D7). |
| Swell gating thresholds | 3 | **One** — `EvaluateSwellGating`. `Ocean.shader` :433 used to restate the numbers with a comment asking that they be kept in sync; it now calls the function (D6). The pair at `Ocean.shader` :357 is a *different*, deliberately narrower gate for the fragment detail layer, not a duplicate. |
| Water surface definition | 3 | **Two** — surface and prepass agree; `WaterVolume.shader` still intersects an analytic `_SeaLevelRadius` sphere (:121-183). Open, owned by W18/W20. |

---

## 6. Shader globals

Published by `PlanetWaterSurface` from `WaterDto` each generation; names in `ShaderGlobalIds.Water.cs`.
They are globals rather than material properties because `Ocean.shader` and `WaterVolumePrepass.shader`
are separate materials that must rasterise the identical surface.

`_SwellAmplitude` · `_SwellWavelength` · `_WaveSpeed` · `_FreezingEnabled` · `_LakeFreezeStart` ·
`_LakeFreezeComplete` · `_OceanFreezeStart` · `_OceanFreezeComplete`

**A material property of the same name shadows the global.** All eight were deleted from both shaders'
`Properties` blocks and from `Ocean.shader`'s `UnityPerMaterial` CBUFFER. Re-adding any one silently
desynchronises the two surfaces, and nothing will report it.

Unset globals read 0, which degrades safely here: `_SwellAmplitude 0` is flat water, `_FreezingEnabled 0`
is liquid. A domain reload before the first generate gives calm water, not an artefact.
