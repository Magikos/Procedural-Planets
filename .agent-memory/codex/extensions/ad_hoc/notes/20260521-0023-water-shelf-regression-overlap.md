# ProceduralPlanets Water Shelf Regression And Overlap

[ad-hoc note] Bryan reported the previous interior volume mask may have fixed the underwater edge, but above water got worse and reads like a sheet/shelf where only the top surface is colored.

[ad-hoc note] Diagnosis: the strict `volumeInteriorMask` removed too much volume contribution near shore for above-water low-angle views. `WaterVolume.shader` now uses `volumeEdgeMask = smoothstep(0.010, 0.060, waterMaskBasis)` plus `volumeBodyMask = lerp(0.65, 1.0, smoothstep(0.10, 0.45, body01Raw))`; `volumeWaterMask = waterMask * volumeEdgeMask * volumeBodyMask`. F10 `VolumeMask` now shows RGB = raw water coverage, effective volume coverage, edge gate.

[ad-hoc note] Bryan asked whether the water mesh should bleed into terrain slightly. Yes, a small under-terrain overlap is reasonable because terrain depth should occlude it while it hides gaps. `WaterMeshBuilder` now pushes clipped shoreline vertices toward the dry endpoint by about `shoreRange * 0.08`, clamped by planet scale.

[ad-hoc note] `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed after this change. Unity needs to reimport `WaterVolume.shader` and regenerate the planet/water mesh for the shoreline overlap to take effect.
