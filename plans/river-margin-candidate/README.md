# Aquatic placement candidate

Status: review-only C# files outside Assets. Not imported, compiled, tested, or connected to runtime.
No scene, shader, fish code, or scatter background-planner change is included.

## Candidate behavior

`ScatterWaterPlacement.TryAnchor` preserves signed bed altitude independently from the floating anchor.
It retains the existing 0.15 m visual offset and rejects floating anchors on dry ground.
`PassesHabitat` consumes the existing immutable `WaterSample` values for freshwater identity, depth, and world-space speed.
It requires explicit shelter evidence when requested. Still water alone does not establish shelter.
The numeric values in tests are fixture boundaries, not authoring defaults.

## Exact proposed import scope

1. Add `ScatterWaterPlacement.cs` under `Assets/Scripts/Planet/Scatter/`.
2. Add `ScatterWaterPlacementTests.cs` under `Assets/Tests/EditMode/`.
3. Replace the `placeRadius` and `altitudeMeters` assignments in both `ScatterField.TryGatherCandidate` and `ScatterGatherJob.TryCandidate`:

```csharp
if (!ScatterWaterPlacement.TryAnchor(onWater, localRadius, seaRadiusHere, scale,
        out float placeRadius, out float altitudeMeters))
    return false;
```

Use `Scale` instead of `scale` in the Burst job. Retain the existing altitude checks and pass the resulting bed altitude to TryPlace.
For behavior isolation, initially call this resolver only for opted-in aquatic prototypes; preserve the current land path.
Floating assets must author water clearance zero. Their altitude limits now describe the bed, not the floating mesh.

4. Before activating habitat filtering, agree the immutable query-input handoff with the planner owner.
5. Feed `PassesHabitat` a successful existing water-query result sampled at the candidate. Do not resolve services inside candidate loops.
6. Add an opt-in prototype/DTO/native rule value, following current DTO validation. Unconfigured prototypes retain current behavior.

Steps 4–6 are integration dependencies, not supplied runtime changes. This candidate does not invent a body-kind grid or copy query snapshots per candidate.
The query and anchor must use the same canonical water surface. Account for WaterQueryKernel's surface offset once, not twice.
Prefer the already-sampled ground when connecting the query so eligibility does not introduce a redundant terrain sample.

## Shelter contract

`knownSheltered` must come from reviewed habitat information and be identical for managed and Burst gathers.
Use true for existing lake locations only where shelter has been established. Do not infer it from `IsOcean == false`.
Unknown river pockets remain false, including widened reaches identified only by `Profile.y`.
Reeds may omit shelter requirements while retaining shallow freshwater and speed limits.
Dry-bank reed extension and bank proximity remain separate work; this candidate covers submerged margins.

## Validation queue

No tests ran. NUnit cases cover depth edges, dry points, missing bodies, ocean rejection, speed magnitude,
unknown shelter, invalid inputs, raised basins, scale, grounded anchors, and the old zero-altitude defect.
After coordinated import, run these tests and the existing scatter parity suite.
Extend the parity fixture with active aquatic rules and basin inputs before enabling runtime filtering.
Run actual Burst job coverage; pure helper tests alone do not prove Burst compilation or full gather parity.
Keep the river owner's undecorated baseline available for shading comparison.

The background planner, snapshot lifetimes, asset tuning, and Unity state remain unchanged.
