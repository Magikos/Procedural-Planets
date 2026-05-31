using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

// Phase A "Low" resolution provider — one Mesh per cube face, fixed vertex count.
// This is the legacy path, lifted from Planet.Initialize + Planet.GenerateMeshAsync with no
// behavior change. Owns the six face GameObjects + TerrainFaces; Planet still owns colors,
// water, shape generator, and event raising (TerrainFaces are exposed for those legacy
// callers; future step swaps that out when the chunked path needs a different shape).
public sealed class PerFaceSurfaceProvider : IPlanetSurfaceProvider
{
    readonly Transform _planetTransform;
    readonly ShapeGenerator _shapeGenerator;
    readonly int _faceResolution;
    readonly Material _faceMaterial;
    readonly Planet.FaceRenderMask _renderMask;

    MeshFilter[] _meshFilters;
    TerrainFace[] _terrainFaces;

    // Phase A only: exposed so Planet's color and water gen can iterate face data without
    // forcing those concerns into IPlanetSurfaceProvider. Replaced when the chunked path
    // needs a different shape.
    public TerrainFace[] TerrainFaces => _terrainFaces;
    public MeshFilter[] MeshFilters => _meshFilters;

    public PerFaceSurfaceProvider(
        Transform planetTransform,
        ShapeGenerator shapeGenerator,
        int faceResolution,
        Material faceMaterial,
        Planet.FaceRenderMask renderMask)
    {
        _planetTransform = planetTransform;
        _shapeGenerator = shapeGenerator;
        _faceResolution = faceResolution;
        _faceMaterial = faceMaterial;
        _renderMask = renderMask;
    }

    public async Awaitable GenerateAsync(IProgressHandle progress, CancellationToken ct)
    {
        EnsureFaces();

        progress?.Report(0f, "Generating terrain...");

        var filters = _shapeGenerator.BuildNoiseFilterData(Allocator.Persistent);
        var faceStates = new TerrainFaceJobState[_terrainFaces.Length];
        var handles = new NativeArray<JobHandle>(_terrainFaces.Length, Allocator.Temp);
        JobHandle combined;
        try
        {
            for (int i = 0; i < _terrainFaces.Length; i++)
            {
                faceStates[i] = _terrainFaces[i].ScheduleMeshDataJob(filters, _shapeGenerator.Settings.PlanetRadius);
                handles[i] = faceStates[i].Handle;
            }
            combined = JobHandle.CombineDependencies(handles);
            handles.Dispose();
            JobHandle.ScheduleBatchedJobs();

            while (!combined.IsCompleted)
            {
                if (ct.IsCancellationRequested)
                {
                    combined.Complete();
                    for (int i = 0; i < faceStates.Length; i++) faceStates[i].Dispose();
                    filters.Dispose();
                    ct.ThrowIfCancellationRequested();
                }
                await Awaitable.NextFrameAsync();
            }
            combined.Complete();
        }
        catch
        {
            if (handles.IsCreated) handles.Dispose();
            for (int i = 0; i < faceStates.Length; i++) faceStates[i].Dispose();
            filters.Dispose();
            throw;
        }

        for (int i = 0; i < _terrainFaces.Length; i++)
            _terrainFaces[i].CompleteMeshDataJob(faceStates[i]);
        filters.Dispose();

        // EvaluateElevation isn't called per vertex anymore (Burst job bypasses it), so feed
        // the per-face elevation samples into ShapeGenerator's min/max envelope manually.
        for (int i = 0; i < _terrainFaces.Length; i++)
        {
            var elevations = _terrainFaces[i].Elevations;
            if (elevations == null) continue;
            for (int v = 0; v < elevations.Length; v++)
                _shapeGenerator.RecordElevationSample(elevations[v]);
        }

        for (int i = 0; i < _terrainFaces.Length; i++)
            _terrainFaces[i].ApplyMeshData();
    }

    public bool TryGetLocalSurfaceRadius(Vector3 localUnitDirection, out float localRadius)
    {
        localRadius = 0f;
        if (_terrainFaces == null) return false;

        var (face, uv) = CoordinateConverter.UnitSphereToCubeFace(localUnitDirection);
        if (face < 0 || face >= _terrainFaces.Length || _terrainFaces[face] == null)
            return false;
        return _terrainFaces[face].TrySampleSurfaceRadius(uv, out localRadius) && localRadius > 0f;
    }

    // Per-face path has no per-frame LOD work — geometry is generated once and stays.
    public async Awaitable GenerateColorsAsync(IBiomeProvider biomeProvider, IProgressHandle progress, CancellationToken ct)
    {
        if (_terrainFaces == null || biomeProvider == null) return;

        await Awaitable.BackgroundThreadAsync();
        ct.ThrowIfCancellationRequested();
        System.Threading.Tasks.Parallel.For(0, _terrainFaces.Length, i => _terrainFaces[i].CalculateColors(biomeProvider));
        ct.ThrowIfCancellationRequested();

        await Awaitable.MainThreadAsync();
        for (int i = 0; i < _terrainFaces.Length; i++)
            _terrainFaces[i].ApplyColors();
    }

    public void Tick(Vector3 observerWorldPosition, Camera observerCamera) { }

    public IReadOnlyList<IFaceMeshSampler> GetFaceMeshSamplers() => _terrainFaces ?? System.Array.Empty<IFaceMeshSampler>();

    public void Dispose()
    {
        // Face GameObjects are children of the planet Transform; Planet.DestroyChildren clears
        // them on regen. Nothing to dispose here yet (jobs are completed in GenerateAsync).
    }

    void EnsureFaces()
    {
        if (_meshFilters != null && _meshFilters.Length == 6 && _terrainFaces != null && _terrainFaces.Length == 6)
        {
            // Already set up; check children still valid.
            bool allValid = true;
            for (int i = 0; i < 6; i++)
            {
                if (_meshFilters[i] == null || _meshFilters[i].sharedMesh == null) { allValid = false; break; }
            }
            if (allValid) return;
        }

        _meshFilters = new MeshFilter[6];
        _terrainFaces = new TerrainFace[6];

        Vector3[] directions = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        for (int i = 0; i < 6; i++)
        {
            GameObject meshObject = new GameObject("mesh");
            meshObject.transform.parent = _planetTransform;

            meshObject.AddComponent<MeshRenderer>().sharedMaterial = _faceMaterial;
            _meshFilters[i] = meshObject.AddComponent<MeshFilter>();
            _meshFilters[i].sharedMesh = new Mesh();

            _terrainFaces[i] = new TerrainFace(_shapeGenerator, _meshFilters[i].sharedMesh, _faceResolution, directions[i]);

            bool renderFace = _renderMask == Planet.FaceRenderMask.All || (int)_renderMask - 1 == i;
            _meshFilters[i].gameObject.SetActive(renderFace);
        }
    }
}
