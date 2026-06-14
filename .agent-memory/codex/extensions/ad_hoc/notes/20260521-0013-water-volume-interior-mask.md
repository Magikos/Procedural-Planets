# ProceduralPlanets Water Volume Interior Mask

[ad-hoc note] After the split surface/volume fix, Bryan still saw an edge bleeding through. Newest targeted F10 captures around `20260521-000447` and `20260521-000458` still correlate the visible line with `VolumeBoundary`/`VolumeOptical`.

[ad-hoc note] The remaining issue is likely the volume pass accepting water mesh data too close to terrain-water intersections, where the water surface is technically between camera and terrain but visually reads as terrain/shore edge bleed.

[ad-hoc note] `WaterVolume.shader` now defines `volumeInteriorMask = smoothstep(0.035, 0.16, depth01Raw) * smoothstep(0.080, 0.32, shore01Raw) * smoothstep(0.20, 0.55, body01Raw)`. `volumeWaterMask = waterMask * volumeInteriorMask`.

[ad-hoc note] The volume pass now uses `volumeWaterMask` for `waterVisible`, water normal selection, and depth/shore/body data blending. F10 `VolumeMask` now shows RGB = raw water coverage, volume interior coverage, volume interior gate.
