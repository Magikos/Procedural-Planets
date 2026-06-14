# ProceduralPlanets Stop Matte Tuning Pivot

[ad-hoc note] Bryan's F10 set around `20260521-183707` still showed the low-horizon shoreline contour in `Off` and `VolumeOnly` after adding `horizonContactMatte` and `SeaSourceMatte` mode 39.

[ad-hoc note] `SeaSourceMatte` lit a broad magenta region over the water/shore contour, which means the shader can classify a candidate source/edge region, but production output still leaves the visible line. This is not a good signal for continuing small opacity, luma, transmittance, or matte-threshold tweaks.

[ad-hoc note] Pivot recommendation: stop stacking production matte tweaks in `WaterVolume.shader` for this artifact. Keep the debug modes as evidence, but move the next fix attempt to water-volume coverage/edge geometry: screen-space horizon occluder with explicit feather, analytic sea-sphere coverage independent of the raster water mesh edge, or mesh/prepass shoreline overlap.

[ad-hoc note] If continuing from here, first consider backing out or isolating the last production matte changes while retaining F10 modes 35-39. The next diagnostic should prove whether a deliberately feathered water coverage band over the terrain contact edge removes the line without causing the earlier above-water sheet/shelf regression.
