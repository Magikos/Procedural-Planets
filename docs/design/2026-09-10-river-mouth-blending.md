# River mouth blending revision — 2026-09-10

Bryan reported a visible river/lake transition in an animated capture.
The old fade started after the terminal river segment, which already extended into the receiving water.
An oblique view exposed its rectangular join. A temporary magenta overlap proof identified the tail boundary.
The proof shader was removed before final validation.

## Changes

- The terminal river segment starts the blend and shares its endpoint blend value with the overlap tail.
- River ripple, refraction, and foam contrast diminish downstream. The overlap sides fade broadly.
- The lake/ocean contributes beneath a river near the same surface height. Other water remains depth-occluded.
- Both shaders use WaterReceivingSurfaceBlend for the same smooth radial-height interval, from 0.25 to 1.25 m.
- The tail retains its receiving-water kind mask. No river routing, terrain carving, queries, or water levels changed.
- Existing meshes and draw calls remain. The lake shader adds one conditional interface lookup where nearest-water rejection occurs.

## Validation

Performance released Unity after its scatter tests. River validation used planet seed 1691104419.
Matched overhead view: RiverChannelFinal. Oblique view: RiverMouthBlendOblique. Sun frozen at local noon, then midnight.
Planet code-health build passed with 0 errors and 19 existing warnings.
All 36 RiverTests, RiverReachSmoothingTests, WaterPresentationTests, and WaterBodyCombinedSampleTests passed.
Fresh Unity generation and shader import completed. Shader-error query returned no entries.
The console retained the previously reported Unity AI service UserUnauthorized error; no river runtime exception was observed.

The low-angle join now fades into receiving water. Separated day captures retained the gradual overlap as waves moved.
Night water remains dark. The underwater capture shows continuous surface coverage and the bed below it.
These are visual observations for Bryan's review, not user appearance approval.

## Timing

Same oblique pose, frozen sun, no graph update during measurement. Windows contain 120 CPU samples and 115–116 valid GPU samples.
The comparison temporarily restored only the previous Ocean shader while retaining the new river geometry/shader.
This isolates the added receiving-water rendering approximately; animation and live simulation still vary.

| Window | CPU average / p95 ms | GPU average / p95 ms |
|---|---|---|
| New receiving pass A | 17.01 / 22.88 | 15.47 / 22.05 |
| Previous receiving pass | 16.96 / 20.67 | 15.41 / 19.89 |
| New receiving pass B | 17.90 / 21.90 | 16.09 / 19.11 |

The final shared helper is active. The samples do not establish zero cost or a statistically isolated cost.
No return to the earlier near-river stall was observed.

## Evidence

Source snapshots, timing JSON, capture PNGs, and sidecars are archived in local-only/river-mouth-blend-2026-09-10/.
Capture labels: mouth-blend-before, mouth-blend-final, mouth-oblique-final-a/b, mouth-oblique-final-night, mouth-final-underwater.
The separate waterfall terrain intrusion remains open; this change does not claim to fix it.
