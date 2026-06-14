# ProceduralPlanets Water Depth Contact

[ad-hoc note] Bryan still saw the above-water edge when looking toward another shore and asked if it is drawing order. Diagnosis: partly yes, but specifically a transparent/depth-contact artifact, not just a wrong render queue. Opaque terrain renders first, then transparent water blends over shoreline pixels that still pass depth.

[ad-hoc note] Latest F10 set around `20260521-003259` still shows the line in `FoamParts`, `SurfaceAlpha`, and `VolumeBoundary`, meaning both surface and volume paths should fade at terrain-contact pixels.

[ad-hoc note] `Ocean.shader` now has `ShoreContactVisibility(scenePath, shore01)` and multiplies it into shoreline foam/alpha in both focus and normal paths. It fades shore rendering only when scene-depth path indicates opaque terrain is immediately behind the water surface.

[ad-hoc note] `WaterVolume.shader` now computes `waterVisibleRaw`, then applies a soft above-water shoreline contact fade using `aboveScenePath`, low `shore01Raw`, and `terrainClearance`. This targets volume lines over terrain while avoiding the strict interior mask that caused the sheet/shelf regression.
