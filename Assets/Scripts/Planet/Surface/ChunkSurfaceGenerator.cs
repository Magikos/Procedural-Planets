using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Owns the Burst mesh-job pipeline for initial chunk generation: builds the noise-filter
// snapshot, schedules a mesh+normals job pair per chunk in bounded batches, drains completed
// jobs into chunk CPU data, and disposes in-flight jobs on cancel/teardown. Split out of
// ChunkedSurfaceProvider (restructure step 2); the orchestrator builds the quadtrees + chunk
// list and hands them here. No runtime state survives a generation pass beyond the noise
// filters, which are released on Dispose.
public sealed class ChunkSurfaceGenerator
{
    // Schedule chunks in batches during initial gen to bound transient NativeArray memory.
    // 128 chunks Ã— ~135 KB/chunk â‰ˆ 17 MB transient â€” comfortable on PC.
    const int InitialGenBatchSize = 128;

    readonly ShapeGenerator _shapeGenerator;
    readonly int _chunkResolution;

    // In-flight chunk jobs â€” only populated during a generation pass; empty at runtime.
    readonly List<PendingChunkJob> _pendingJobs = new();
    NativeArray<NoiseFilterData> _filters;
    NativeArray<byte> _diagnosticTerrainCells;

    public ChunkSurfaceGenerator(ShapeGenerator shapeGenerator, int chunkResolution)
    {
        _shapeGenerator = shapeGenerator;
        _chunkResolution = chunkResolution;
    }

    // Builds every chunk's CPU vertex/normal data in place. progressStart/progressSpan map the
    // batch loop onto the caller's overall progress budget.
    public async Awaitable GenerateMeshesAsync(
        IReadOnlyList<PlanetChunk> chunks,
        IProgressHandle progress,
        float progressStart,
        float progressSpan,
        CancellationToken ct)
    {
        if (_filters.IsCreated) _filters.Dispose();
        _filters = _shapeGenerator.BuildNoiseFilterData(Allocator.Persistent);
        if (_diagnosticTerrainCells.IsCreated) _diagnosticTerrainCells.Dispose();
        _diagnosticTerrainCells = _shapeGenerator.BuildDiagnosticTerrainCells(Allocator.Persistent);

        int total = chunks.Count;
        for (int batchStart = 0; batchStart < total; batchStart += InitialGenBatchSize)
        {
            int batchEnd = Mathf.Min(batchStart + InitialGenBatchSize, total);
            int batchSize = batchEnd - batchStart;

            var handles = new NativeArray<JobHandle>(batchSize, Allocator.Temp);
            try
            {
                for (int i = batchStart; i < batchEnd; i++)
                {
                    ScheduleChunkJob(chunks[i]);
                    handles[i - batchStart] = _pendingJobs[_pendingJobs.Count - 1].State.Handle;
                }
                var combined = JobHandle.CombineDependencies(handles);
                handles.Dispose();
                JobHandle.ScheduleBatchedJobs();

                while (!combined.IsCompleted)
                {
                    if (ct.IsCancellationRequested)
                    {
                        combined.Complete();
                        DisposeAllPendingJobs();
                        ct.ThrowIfCancellationRequested();
                    }
                    await Awaitable.NextFrameAsync();
                }
                combined.Complete();
            }
            catch
            {
                if (handles.IsCreated) handles.Dispose();
                DisposeAllPendingJobs();
                throw;
            }

            DrainCompletedJobs();
            float pct = (float)batchEnd / total;
            progress?.Report(progressStart + progressSpan * pct, $"Generated chunks {batchEnd}/{total}");
        }
    }

    public void Dispose()
    {
        DisposeAllPendingJobs();
        if (_filters.IsCreated) _filters.Dispose();
        if (_diagnosticTerrainCells.IsCreated) _diagnosticTerrainCells.Dispose();
    }

    void ScheduleChunkJob(PlanetChunk chunk)
    {
        int vertexCount = ChunkTriangleTemplate.VertexCount(_chunkResolution);

        var state = new PlanetChunkMeshJobState
        {
            Resolution = _chunkResolution,
            Vertices = new NativeArray<float3>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            UnitSpherePoints = new NativeArray<float3>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            Elevations = new NativeArray<float>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            Normals = new NativeArray<float3>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
        };

        // Pre-cache builds every chunk with EdgeFanMask = 0 (no vertex snapping). At runtime
        // the visibility filter renders chunks at varying depths, so any given chunk's effective
        // neighbor LOD can change between frames â€” pre-baking masks would require 16 mesh
        // variants per chunk. For Phase A we accept small cracks at LOD transitions; fix later.
        Vector3 localUp = GetFaceLocalUp(chunk.FaceIndex);
        GetFaceAxes(chunk.FaceIndex, out Vector3 axisA, out Vector3 axisB);

        var meshJob = new PlanetChunkMeshJob
        {
            Resolution = _chunkResolution,
            FaceLocalUp = new float3(localUp.x, localUp.y, localUp.z),
            FaceAxisA = new float3(axisA.x, axisA.y, axisA.z),
            FaceAxisB = new float3(axisB.x, axisB.y, axisB.z),
            UvOrigin = new float2(chunk.UvCenter.x - chunk.UvHalfExtent, chunk.UvCenter.y - chunk.UvHalfExtent),
            UvExtent = chunk.UvHalfExtent * 2f,
            PlanetRadius = _shapeGenerator.Settings.PlanetRadius,
            EdgeFanMask = 0,
            Filters = _filters,
            DiagnosticTerrainCells = _diagnosticTerrainCells,
            DiagnosticTerrain = _shapeGenerator.DiagnosticTerrainData,
            Vertices = state.Vertices,
            UnitSpherePoints = state.UnitSpherePoints,
            Elevations = state.Elevations,
        };

        JobHandle meshHandle = meshJob.Schedule(vertexCount, 256);

        // Chain the normals pass after the mesh job â€” it reads Vertices written above.
        var normalsJob = new PlanetChunkNormalsJob
        {
            Resolution = _chunkResolution,
            Vertices = state.Vertices,
            Normals = state.Normals,
        };
        state.Handle = normalsJob.Schedule(vertexCount, 256, meshHandle);

        chunk.State = ChunkLifecycle.Generating;
        chunk.EdgeFanMaskAtSchedule = 0;

        _pendingJobs.Add(new PendingChunkJob
        {
            Chunk = chunk,
            State = state,
        });
        JobHandle.ScheduleBatchedJobs();
    }

    void DrainCompletedJobs()
    {
        // Pre-cache: no chunks are released between schedule and completion, so the per-job
        // stale guard from the dynamic-subdivision path is unnecessary. Just complete each
        // job, copy its output, and free its NativeArrays.
        for (int i = _pendingJobs.Count - 1; i >= 0; i--)
        {
            var pending = _pendingJobs[i];
            if (!pending.State.Handle.IsCompleted) continue;

            pending.State.Handle.Complete();
            _pendingJobs.RemoveAt(i);

            CopyJobOutputToChunk(pending.Chunk, pending.State);
            pending.State.Dispose();
            pending.Chunk.State = ChunkLifecycle.Active;

            var elevs = pending.Chunk.CpuElevations;
            if (elevs != null)
                for (int v = 0; v < elevs.Length; v++)
                    _shapeGenerator.RecordElevationSample(elevs[v]);
        }
    }

    void DisposeAllPendingJobs()
    {
        for (int i = 0; i < _pendingJobs.Count; i++)
        {
            _pendingJobs[i].State.Handle.Complete();
            _pendingJobs[i].State.Dispose();
        }
        _pendingJobs.Clear();
    }

    static void CopyJobOutputToChunk(PlanetChunk chunk, PlanetChunkMeshJobState state)
    {
        int vc = state.Vertices.Length;
        chunk.CpuVertices = new Vector3[vc];
        chunk.CpuUnitSpherePoints = new Vector3[vc];
        chunk.CpuElevations = new float[vc];
        chunk.CpuVertexRadii = new float[vc];
        chunk.CpuNormals = new Vector3[vc];

        var vAsV3 = state.Vertices.Reinterpret<Vector3>(sizeof(float) * 3);
        var sAsV3 = state.UnitSpherePoints.Reinterpret<Vector3>(sizeof(float) * 3);
        var nAsV3 = state.Normals.Reinterpret<Vector3>(sizeof(float) * 3);
        NativeArray<Vector3>.Copy(vAsV3, chunk.CpuVertices, vc);
        NativeArray<Vector3>.Copy(sAsV3, chunk.CpuUnitSpherePoints, vc);
        NativeArray<float>.Copy(state.Elevations, chunk.CpuElevations, vc);
        NativeArray<Vector3>.Copy(nAsV3, chunk.CpuNormals, vc);

        if (vc <= 0) return;

        var bounds = new Bounds(chunk.CpuVertices[0], Vector3.zero);
        for (int i = 0; i < vc; i++)
        {
            chunk.CpuVertexRadii[i] = chunk.CpuVertices[i].magnitude;
            bounds.Encapsulate(chunk.CpuVertices[i]);
        }
        chunk.CpuLocalBounds = bounds;
    }

    // ---- Face frame helpers (match CubeFaceToUnitSphere convention) ------------------------

    static Vector3 GetFaceLocalUp(int faceIndex) => faceIndex switch
    {
        0 => Vector3.up, 1 => Vector3.down, 2 => Vector3.left,
        3 => Vector3.right, 4 => Vector3.forward, 5 => Vector3.back,
        _ => Vector3.up,
    };

    static void GetFaceAxes(int faceIndex, out Vector3 axisA, out Vector3 axisB)
    {
        Vector3 up = GetFaceLocalUp(faceIndex);
        axisA = new Vector3(up.y, up.z, up.x);
        axisB = Vector3.Cross(up, axisA);
    }

    struct PendingChunkJob
    {
        public PlanetChunk Chunk;
        public PlanetChunkMeshJobState State;
    }
}
