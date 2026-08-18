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
| B | `shoreBody` | 0..1 | **packed** | `shore01 * 0.45 + body01 * 0.55`. Two values in one channel — see §4. |
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

## 4. The `shoreBody` packing, and its invariant — D5

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

**Decision: document and guard, do not re-encode yet.** The 0.02% exposure does not justify touching the
volume composite — the most expensive and most fragile pass in the water stack, and the one that would
also drag `Atmosphere.shader` in via §3's invariant. More importantly, the *right* encoding depends on
what **W5** needs channel B to carry once bodies have independent levels and identities; designing it now
means designing it blind.

**Guard:** `WaterMeshBuilder` counts vertices with `0.05 < body01 < 0.95` into
`BuildStats.AmbiguousBodyVertices`; `PlanetWaterSurface` logs a Warning past 0.5% of mesh vertices. That
is the only place the break would be visible, because the symptom is a wrong body *tint*, not an artefact.

**Revisit in W5.** When per-body levels land, either give the catalog body id its own channel (which needs
a fifth value and therefore a second RT or a narrower `forwardDepth`), or keep `body01` strictly binary and
carry the id separately.

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
