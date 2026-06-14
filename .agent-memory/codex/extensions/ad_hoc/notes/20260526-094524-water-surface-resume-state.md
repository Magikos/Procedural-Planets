# ProceduralPlanets Water Surface Resume State

Date: 2026-05-26 09:45 local
Repo: `C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets`

## Why This Note Exists

Bryan is nearly out of Codex access and asked to preserve current context before losing the thread. Resume from this note before touching code.

## Current Working Tree

Known dirty files at the time of this note:

- `Assets/Graphics/Shaders/Ocean.shader`
- `Assets/Scripts/Core/Services/WaterDebugModule.cs`

`git diff --stat -- Assets/Graphics/Shaders/Ocean.shader Assets/Scripts/Core/Services/WaterDebugModule.cs` showed:

- `Ocean.shader`: large active surface-water changes, about 397 changed lines
- `WaterDebugModule.cs`: capture-set changes, about 8 changed lines

Do not revert these blindly. They are part of the active water surface iteration.

## Validation Status

Latest code validation run:

```text
dotnet build ProceduralPlanets.Planet.csproj
Build succeeded. 0 warnings, 0 errors.
```

Unity still needs to reimport/compile shaders and Bryan needs to run visual F10 validation after the latest shader changes.

## Current Water Direction

The project is in the layer-by-layer water rebuild phase. The water volume/depth/caustics work reached a good state. The active layer is now ocean surface polish:

- surface wave pattern
- glint
- foam
- later wakes/foam/wake integration

Bryan's current feedback before this note:

- The waves are still too uniform.
- He wants the wave/surface pattern to feel more like the caustic effect.
- An odd hard-edged cutout is visible in F10 and appears to affect glint.

## Latest F10 Diagnosis

Latest inspected F10 group was around:

- `local-only\debug-screenshots\F10-water.00-Off-20260526-085345-997.png`
- `F10-water.05-Glint-20260526-085348-678.png`
- `F10-water.08-MotionMask-20260526-085349-807.png`
- `F10-water.10-WaveSlope-20260526-085350-492.png`
- `F10-water.11-WaterData-20260526-085347-929.png`
- `F10-water.54-SurfaceFxContrib-20260526-085353-404.png`
- `F10-water.57-SurfaceFxProof-20260526-085354-156.png`

Important observation: the hard-edged cutout appears in `WaterData`, not just in `Glint`. That means the cutout is coming from mesh-provided water metadata, specifically vertex color channels used as water data:

- R: depth01
- G: shore01
- B: body01

Glint was amplifying that discontinuity, but glint is not the root source.

## Latest Shader Changes

`Ocean.shader` now has a caustic-style surface pattern branch:

- Added `Hash22`
- Added `SurfaceVoronoi`
- Added `SurfaceCellPatternUv`
- Added `SurfaceCellPattern`
- `ComputeSurfaceWaves` now blends this cellular pattern into wave slope/ripple proof instead of relying mainly on long uniform bands.

This should make `SurfaceFxProof` look more caustic/cellular and less like repeated S-shaped stripes.

`Ocean.shader` also now detects abrupt water-data edges:

- `float3 waterData = float3(depth01, shore01, body01);`
- `float dataEdge = saturate(length(fwidth(waterData)) * 16.0);`
- `float dataContinuity = lerp(1.0, 0.22, smoothstep(0.16, 0.86, dataEdge));`
- glint is multiplied by `dataContinuity`

This is intentionally diagnostic and defensive: it should reduce glint amplification across bad/abrupt water-data transitions while proving whether the cutout matches water-data discontinuity.

`SurfaceFxContrib` was changed so:

- R = wave slope
- G = detected water-data edge
- B = glint

Next F10 should check whether the odd cutout lines up with green in `SurfaceFxContrib`.

## Prior Patch Just Before This

Foam was nearly gone because post-generation camera/distance visibility suppressed it too hard. That was relaxed.

Local storm/weather sampling was also decoupled from wave geometry because stretched/uniform patches lined up with `MotionMask` storm/weather patches. `storm01` remains available for diagnostics, but should not reshape the wave field strongly.

## Next Visual Checks

Ask Bryan to run F10 after Unity shader reimport. Check:

1. `SurfaceFxProof`
   - Should now show a more cellular, caustic-like animated pattern.
   - If it still looks like long uniform stripes, inspect `SurfaceCellPattern` scale/time and whether `waveProof` is still dominated by the old crossedProof bands.

2. `SurfaceFxContrib`
   - Green now means `dataEdge`.
   - If the odd cutout is green, the root problem is water mesh vertex-color metadata or interpolation, not glint.

3. `WaterData`
   - If the cutout is present here, inspect `WaterMeshBuilder` data generation and interpolation:
     - `AddVertex` color packing
     - `CreateIntersection`
     - bodyFactor classification
     - shore/depth edge values
     - clipped triangle interpolation across ocean/shore boundaries

4. `Glint`
   - Should have less hard clipping at the cutout after `dataContinuity` dampening.
   - If the cutout remains strong in glint but not green in `SurfaceFxContrib`, then the issue may be lighting/fresnel/normal discontinuity instead of water-data discontinuity.

5. `Foam` and `FoamParts`
   - Should no longer be black after the previous visibility/threshold fix.

## Likely Root Cause Of Cutout

The hard shape is likely not a surface shader pattern issue. It appears to be an abrupt mesh-water-data discontinuity. The most likely source is in `WaterMeshBuilder`:

- vertex colors encode water data
- clipped shoreline/intersection vertices get synthetic depth/shore/body values
- interpolation across triangles can create hard regions if generated vertices or body factors differ abruptly

Do not tune glint endlessly if the cutout remains visible in `WaterData` or `SurfaceFxContrib` green. Follow Bryan's hard-isolation rule: prove root source first, then fix it.

## Useful Files

- `Assets/Graphics/Shaders/Ocean.shader`
- `Assets/Graphics/Shaders/WaterVolume.shader`
- `Assets/Graphics/Shaders/Includes/DebugModes.hlsl`
- `Assets/Scripts/Core/Services/DebugModeConstants.cs`
- `Assets/Scripts/Core/Services/WaterDebugModule.cs`
- `Assets/Scripts/Planet/WaterMeshBuilder.cs`
- `Assets/Scripts/Planet/Planet.cs`

## Workflow Reminder

Bryan's current workflow preference is evidence-led:

- use F10 capture sets
- use hard diagnostic lines
- use forced colors/extreme/binary tests before tuning
- if a final visual does not change, stop small value tweaks and isolate ownership

For this specific issue, the next hard line is: does the cutout match `SurfaceFxContrib` green and/or `WaterData`? If yes, move into `WaterMeshBuilder` metadata generation. If no, inspect normal/glint/fresnel continuity.
