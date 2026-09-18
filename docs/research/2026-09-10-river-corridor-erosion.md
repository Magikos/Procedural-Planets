# River corridor erosion experiment

Status: offline numerical probe complete; not adopted into planet generation.

## Prediction registered before execution

Bounded droplet erosion can change river banks while preserving the established channel and outlet topology.
The first probe uses the reference's gradient, inertia, erosion brush, and sediment-capacity ideas.
It uses sequential CPU updates, signed terrain heights, explicit sediment accounting, and a corrected downhill speed update.

Expected numerical results:
- Repeated input and seed produce identical output checksums.
- Protected cells and cells outside the corridor change by exactly zero.
- Every height stays within its supplied erosion and deposition limits.
- Removed volume equals deposited volume plus outlet export plus unsettled sediment, within 1e-6 relative error.
- A zero-edit corridor produces zero terrain change and zero transported sediment.
- A descending synthetic catchment produces measurable erosion without changing its protected centreline.

The production experiment must additionally preserve fixed lake outlets, waterfall lips, and graph connectivity.
Its delta must apply once after baseline channel carving through a shared terrain field.
Both terrain backends, scatter, and water queries must agree on final heights.
The offline numerical probe does not establish appearance, spherical seam handling, or backend parity.

## Alternatives and discriminating checks

| Observation | Proposed mechanism | Alternative | Check |
|---|---|---|---|
| Banks appear too uniform | Bounded erosion forms local variation | Water compositing hides existing detail | Compare terrain-only geometry and final water captures separately. |
| Flow looks artificial | Persistent transported foam shows motion | The falling sheet geometry causes the artifact | Keep foam work separate from terrain erosion. |
| Delta appears to improve channels | Erosion redistributes sediment | Repeated carving deepens the whole corridor | Compare baseline-carved input against one signed delta application. |

## Ownership

Rivers task owns routing, corridor experiments, and water presentation.
Procedural Generation owns the base recipe, snow, and terrain materials.
The snow task owns Unity while this offline probe runs.
No shared sampler caller changes are planned for the numerical probe.

## References

- https://github.com/SebLague/Hydraulic-Erosion
- `local-only/Clouds-master/Assets/Scripts/Terrain/Scripts/Erosion.cs`
- `local-only/Fluid-Planet-main/Assets/Scripts/Simulation/Compute/FluidSim.compute`
- `local-only/GDWaterKart-main/water/waves/accumulate_foam.glsl`

The probe is an independent bounded implementation of the reviewed ideas, not a copied GPU kernel.

## Offline results

Command: `dotnet run --project local-only/river-erosion-probe/RiverErosionProbe.csproj` from an appropriate output directory.

The signed-height synthetic case passed repeatability, protected-cell, bounds, mass-balance, zero-edit, and cancellation checks.
It changed 936 cells and preserved 3,289 protected cells exactly.
Removed volume was 3,629.713403 cubic metres; deposited volume was 1,882.513403 cubic metres.
Outlet export was 1,747.107075 cubic metres; unsettled sediment was 0.092925 cubic metres.
The mass residual was -1.50e-11 cubic metres.
Maximum erosion reached the 0.6-metre bound; this case produced no positive net deposition.
A separate flat receiving-pool control produced 0.15 metres of net deposition and a 4.48e-11 cubic-metre mass residual.

The first plotting command failed with `ModuleNotFoundError: No module named 'matplotlib'`.
Numerical results remain in `local-only/river-erosion-probe/synthetic-report.json`.

Verdict: INCONCLUSIVE for production adoption.
Next discriminating probe: export one real ocean-connected river network, using the fixed recipe and existing baseline-carved terrain.
Run the same bounds and accounting checks before considering a shared runtime delta.
The prepared export script writes data only; it does not modify the generated planet.
The small tangent-plane patch uses a planar area approximation. Production spherical metrics remain unverified.

## Real-network result and lifetime probe

Seed 1691104419, network 177: 103 segments in a 217 by 100 grid, spacing 2 metres.
The initial 128-step run changed 1,849 cells and preserved 19,848 protected cells exactly.
It passed repeatability, bounds, and mass accounting. Delta ranged from -0.584265 to +0.15 metres.
Removed/deposited/exported/unsettled volumes were 7578.243/5741.484/782.471/1054.288 cubic metres.
The unsettled amount prevents treating the numerical pass as a complete sediment simulation.

Prediction before the next probe: if lifetime truncation dominates, increasing the limit from 128 to 512 steps should at least halve unsettled sediment.
Termination counters will distinguish lifetime, evaporation, flat terrain, boundary exit, and successful outlet arrival.
All other coefficients and the seed remain fixed. This probe still makes no runtime terrain changes.

The 512-step run confirmed the lifetime prediction: unsettled sediment fell from 1054.288 to 510.070 cubic metres.
Its 12,000 droplets terminated at the ocean outlet (5,949) or through evaporation (6,051).
No droplet reached the extended lifetime limit or left the patch boundary.
Removed/deposited/exported volumes were 7675.794/5894.872/1270.852 cubic metres.
The mass residual was -5.63e-12 cubic metres. Bounds and protected-cell checks still passed.
The patch retained 2535.134 cubic metres of aggregate deposition capacity, but that does not prove local settling capacity at each droplet endpoint.
The remaining unsettled sediment requires a local-capacity diagnostic before treating the probe as a complete sediment simulation.

## Interpretation and handoff

[The numerical terrain plot](../../local-only/river-erosion-probe/planet-result.png) shows a narrow erosion band following the inherited zigzag route.
It does not demonstrate natural channel shape improvement. Bounds, outlet anchors, and mass accounting are proven only for this offline patch.
Longer lifetime explains part of the unsettled sediment; evaporation and local deposition constraints remain separate mechanisms.
The diagram is a numerical plot, not a Unity before/after capture.
Matplotlib was installed into the probe's local `plot-dependencies` directory to produce the plot after the initial missing-module failure.

Verdict: INCONCLUSIVE for production adoption; do not apply this delta to the live planet.
Next geometry probe: refine the entire centreline between fixed graph anchors, then compare path curvature and terrain-only captures against the current rounded grid path.
Next sediment probe: measure remaining deposition capacity at evaporation endpoints before changing the settling algorithm.
No production river code, shared samplers, terrain recipe, snow, grass, animals, scene, camera, or time changed during this experiment.
Unity ownership passed directly to the Grass task for its queued validation.

Run from `local-only/river-erosion-probe`:

```powershell
dotnet run --project RiverErosionProbe.csproj
dotnet run --project RiverErosionProbe.csproj -- planet-patch.json planet-result.json 128
dotnet run --project RiverErosionProbe.csproj -- planet-patch.json planet-long-result.json 512
```

The generated reports and checksums are retained beside the inputs and probe source.
