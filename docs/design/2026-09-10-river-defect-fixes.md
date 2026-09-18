# River performance and visual defects

Status: the four reported defects have fixes and validation evidence. Waterfall terrain intrusion remains open.

## Scope and ownership

- Performance owns the overall frame-rate investigation, fish updates, and frame timing counters.
- Rivers owns CPU river sampling, river presentation, lake-mouth blending, and night lighting.
- Procedural Generation prepares whole-reach smoothing and its focused regression checks.
- Agents serialize Unity use. Performance holds Unity during the initial fish validation.

## Baseline

Performance reproduced the reported near-river stall at `RiverChannelFinal`.
Six-frame wall-time samples averaged 1,676 ms per frame. These are provisional samples, not a 120-frame performance result.
Disabling `_RiverActive` did not remove the stall. Performance measured `_fish.Tick` at 1,448.36 ms per call.
The river particle update measured 0.35 ms. Fish simulation repeated expensive water queries across catch-up steps.
The timing counter also rejected frames longer than 1,000 ms. Performance owns that diagnostic fix.

The preserved baseline is `local-only/river-visual-fixes/before/F10-water.00-Off-river-performance-baseline-20260910-072524-910.png` and its sidecar.
The screenshot shows square river ends over receiving water. Its sun was not frozen, so it is not a matched lighting baseline.
The camera position is `(59.48, -1134.49, -4971.95)`, with forward `(-0.0112, -0.0612, 0.9981)`.
The seed remains 1691104419 and the quality level is PC/High.

## Changes under test

The CPU query change compiles the existing `RiverFieldData` sample and carve algorithms with Burst direct calls.
Managed reference methods retain the same algorithm for parity checks. The change does not alter bins or GPU sampling.
The target is at least ten times faster warmed CPU river sampling, without changing wet classification or water height.

The curve candidate smooths entire unbranched reaches before tessellating remaining bends.
It pins headwaters, mouths, junctions, and waterfall endpoints. It retains downhill radii and flow coordinates.
Its tessellation bounds tangent steps and midpoint chord error. The real planet still needs intersection and terrain-support checks.

The lighting candidate shares the lake's daylight, night brightness, and sky-reflection implementation.
It removes river reliance on an unrelated ambient probe and gates sun glitter by local daylight.
The Ocean shader extraction must preserve its existing appearance.

The mouth candidate adds a flared presentation tail into existing receiving water.
Its alpha falls smoothly to zero. A small moving foam term marks the mixing region.
The tail does not carve terrain or add river query segments. The existing lake supplies its water volume.
The shader must reject tail pixels without receiving water at the same height.

## Acceptance checks recorded before import

- CPU query parity: identical wet classification across banks and cube seams; water-radius error at most 4 mm.
- Carving parity: normalized elevation error at most 0.000001 at radius 5,000 m.
- Curves: continuous shared endpoints, deterministic generation, pinned anchors, and no uphill water segments.
- Curves: repeated grid turns decrease substantially; a short right-angle bend has no tangent step above 15.1 degrees.
- Mouths: no rectangular terminal cut, no tail over dry land, and no visible extra water level.
- Night: river and foam darken with the lake; no bright reflection from an unrelated ambient probe.
- Regression: inspect a waterfall lip and receiving pool, banks, caustics, and underwater coverage.
- Performance: Performance reports matched-pose average and p95 after a filled timing window.
- Appearance: provide matched captures for Bryan's review. Numerical checks do not approve the look.

## Evidence

Core and Planet code-health builds pass for the initial Burst wrapper. Planet reports 19 existing warnings and zero errors.
Logs: `local-only/river-visual-fixes/build-core.log` and `build-planet.log`.
Unity import, query parity, fresh generation, and visual captures completed. Results follow below.

Burst direct-call support was checked against [Unity's Burst documentation](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-calling-burst-code.html).

## Verified results

The final Unity reload compiled the river and shared water shaders without shader errors.
All 23 RiverTests and RiverReachSmoothingTests passed. All 12 WaterPresentationTests and WaterBodyCombinedSampleTests passed.
The regenerated planet used seed 1691104419. The camera returned to RiverChannelFinal after generation completed.

Performance measured the original stall in fish habitat queries, not river drawing.
Its baseline wall frame averaged about 1676 ms. Its final 240-frame window averaged 25.35 ms, with CPU p95 35.56 ms.
The fish population differed: 52 before and 44 after. This is evidence of stall removal, not an exact isolated speedup.
The warmed 1000-query benchmark took 5.985 ms in Burst versus 101.3135 ms in the managed reference.
Performance owns the fish fixes and subsequent fish jobs work. See local-only/performance-2026-09-10/near-after.json.

Matched channel captures use the same position, rotation, seed, and frozen local noon or midnight.
The new bend is smoother. The mouth now overlaps and fades into the lake instead of ending in a rectangular cut.
The night river darkens with the receiving water. Surface pattern differences remain visible in daylight.
An underwater capture at 0.5 m below the queried surface shows continuous surface coverage and the visible bed.
The query reported body depth 3.3989 m and body ID 1.

Captures are archived in local-only/river-visual-fixes/after:
- F10-water.00-Off-river-after-channel-day-20260910-075031-297.png
- F10-water.00-Off-river-after-channel-night-20260910-075100-386.png
- F10-water.00-Off-river-after-submerged-20260910-075444-433.png
- F10-water.00-Off-river-after-waterfall-day-20260910-075141-490.png

The waterfall check did not pass visual acceptance. Terrain hides part of the sheet and the splash region.
Active nearby emitters contain particles, so absent emission does not explain the result.
A runtime-only particle queue experiment did not resolve it and was not saved to source.
Procedural Generation is investigating terrain support for the pinned waterfall segments.
The earlier performance baseline capture may have had river queries disabled during isolation; do not use it for visual comparison.


## Waterfall support correction queued

Review found a support regression that endpoint-only tests missed.
Smoothing rotated a synthetic receiving run by 24.445 degrees and moved its far endpoint by 5.534 m.
The correction pins the full immediate lip and receiving runs during both fairing and tessellation.
Falling-sheet rows now use the sheet's own tangent, matching the analytic carving axis.
The corrected offline case retains those runs exactly. A new Unity regression checks all three connected legs.
Performance will include the regression during its next shared import. The final waterfall capture and clearance check are recorded below.

The combined post-correction Unity batch passed 117/117 tests, including both river fixtures and the new support regression.
Performance supplied the combined result before releasing Unity for the final waterfall check.

## Measured waterfall geometry defect

The final support-corrected generation retained a terrain intrusion at RiverWaterfallFinal.
The 63-point clearance check sampled 21 longitudinal rows at the center and 80% of each half-width.
Analytic ground clearance was at least 1.0395 m. Visible triangle clearance reached -2.5352 m at a lower edge.
The landing center clearance was -0.5190 m. These measurements identify triangle bridging rather than missing analytic carving.
The fall drops from radius 5076.155 m to 5053.877 m, with half-width 6.1844 m and channel depth 2.4738 m.
Evidence: local-only/river-visual-fixes/waterfall-clearance.json and the support-corrected capture in after/.
Procedural Generation is preparing local terrain refinement. No global channel-depth increase was applied.


## Current boundary

The four reported defects have verified improvements: the near-river stall, sharp bends, abrupt mouths, and night brightness.
Waterfall terrain intrusion remains open. It predates or extends beyond the smoothing regression; exact chronology is not proven.
The support correction prevents an additional alignment error, but does not claim to solve triangle bridging.
Local refinement must preserve chunk seams and generation/runtime cost. Procedural Generation is preparing that bounded design.
The knowledge graph update completed after the support source changes.

## Reviewed terrain follow-up

Procedural Generation supplied local-only/river-visual-fixes/waterfall-local-refinement-proposal.md.
First record the offending visible chunk depth and test its already cached finest mesh.
Only test extra local depth if that mesh still intrudes. Preserve the analytic carving during this proof.
Any production refinement must share stitched indices between rendering and raycasts and balance neighboring chunk detail.
The proposal's node and memory caps remain unmeasured proposals. No refinement code has been imported.
