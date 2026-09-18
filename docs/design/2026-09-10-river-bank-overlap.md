# River bank overlap

## Finding

Seed 1691104419 showed opposing triangular water wedges beside river segments 1621 and 1620.
Their shared endpoint and widths match. Hiding the standing-water mesh removed the wedges.
The upstream lake cover extends across a lower river. Its nearer surface won the water prepass.

## Change

`RiverField.hlsl` shares segment projection, endpoint clipping, and width calculations between sampling and overlap rejection.
The standing-water prepass rejects higher cover within a lower river's carved bank.
Where the solved field contains standing water, rejection is limited to the river channel.
Waterfall sheets, equal levels, and higher rivers do not reject standing water.
The visible surface also rejects pixels without a prepass owner.
The lookup uses existing river spatial buckets. It does not scan all planet segments.

## Evidence

All 36 focused Unity tests passed, including 10 new GPU cases. There were no failures or skipped tests.
The suites were RiverBankGpuTests, RiverTests, RiverRendererTests, and RiverReachSmoothingTests.
The EditMode project build completed with zero errors and two CS9057 analyzer-version warnings.
The final console check reported existing grass and rain shader warnings, with no water shader error.

The old and new prepasses were compared in the same running scene at a frozen local noon.
The old shader was compiled in memory. The production shader was restored after the comparison.

| Pass | CPU average / P95 | GPU average / P95 |
|---|---|---|
| Old | 25.60 / 34.24 ms | 22.37 / 29.11 ms |
| New | 23.11 / 31.50 ms | 21.54 / 28.21 ms |

These short samples show no observed regression. They do not establish a speed improvement.
Each CPU sample contains 120 frames. Each GPU sample contains 113 frames.
The final capture removes the large opposing wedges. A small isolated shoreline fragment remains outside the river bank.
At that fragment, terrain is above the solved lake level but below its 0.15 m presentation offset.
This remaining lake-cover artifact needs a shoreline coverage solution. Increasing river width would conceal it by changing valid banks.
The rejection uses segment radius before reach smoothing; multi-segment smoothing parity remains an approximation.

Local evidence is under `local-only/river-bank-defect-2026-09-10/`.
The camera rotation observed after the user's responsiveness message was restored, along with the saved running time state.
Unity did not require a restart.
