using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

// Generates and owns the visible surface meshes for a planet. Selected per-planet by
// PlanetResolution. Two implementations target Phase A: PerFaceSurfaceProvider (Low,
// existing path) and ChunkedSurfaceProvider (High, quadtree LOD — step 5).
//
// Step 1 contract is intentionally minimal — only the methods Planet needs today to
// delegate generation and surface-radius queries. Tick / GetVisibleMeshes / state hooks
// are added in step 5 when the chunked path needs them.
public interface IPlanetSurfaceProvider : IDisposable
{
    // Schedules and awaits the initial surface generation. Throws OperationCanceledException
    // if `ct` fires. Progress is reported in [0, 1] over the provider's portion of work; the
    // calling Planet remaps it into the larger pipeline.
    Awaitable GenerateAsync(IProgressHandle progress, CancellationToken ct);

    // Bilinear-samples the visible surface radius (planet-local, no scale) in the given
    // direction. Returns false if no surface data is available yet.
    bool TryGetLocalSurfaceRadius(Vector3 localUnitDirection, out float localRadius);

    // Computes and uploads biome vertex color data for this provider's owned meshes.
    Awaitable GenerateColorsAsync(IBiomeProvider biomeProvider, IProgressHandle progress, CancellationToken ct);

    // Per-frame update — chunked provider drives LOD subdivide/merge here; per-face is no-op.
    // Called from Planet.Update once initial GenerateAsync has completed.
    void Tick(Vector3 observerWorldPosition, Camera observerCamera);

    // Returns one face mesh sampler per cube face (6 total), or an empty list if no surface
    // data is available yet. Currently used by WaterMeshBuilder to build the water mesh; both
    // providers return their face roots' per-vertex unit-sphere + elevation grids.
    IReadOnlyList<IFaceMeshSampler> GetFaceMeshSamplers();
}
