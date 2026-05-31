using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Aggregates all visible leaf chunks of a single cube face into one Mesh — keeps draw call
// count to ~6 (one per face) rather than ~hundreds (one per chunk). Triangles per chunk are
// the shared ChunkTriangleTemplate, offset by the chunk's vertex start in the combined mesh.
//
// Rebuild is triggered by MarkLeavesChanged + RebuildIfDirty (idempotent; called once per
// frame from ChunkedSurfaceProvider.Tick after subdivide/merge work settles). Per-frame cost
// when nothing changed is zero.
public sealed class CombinedFaceMesh
{
    readonly Mesh _mesh;
    readonly int _chunkResolution;
    readonly int[] _chunkTriangleTemplate;
    readonly int _vertsPerChunk;
    readonly int _trisPerChunk;

    readonly List<PlanetChunk> _currentLeaves = new();
    readonly List<Vector3> _vertScratch = new();
    readonly List<Vector3> _normalScratch = new();
    readonly List<int> _triScratch = new();
    bool _dirty = true;

    // Per-chunk vertex offset in the combined mesh — populated during rebuild so consumers
    // (e.g. boundary-normal smoothing) can map a chunk's local vertex index to its mesh index.
    readonly Dictionary<PlanetChunk, int> _chunkVertexOffsets = new();
    public IReadOnlyDictionary<PlanetChunk, int> ChunkVertexOffsets => _chunkVertexOffsets;

    // Snapshot of the CpuVertices references we last actually uploaded — used to early-out
    // when a face is marked dirty but its visible vertex data is unchanged. Reference identity
    // captures content changes precisely: CopyJobOutputToChunk allocates a new array each
    // time a chunk's mesh job completes, so a CpuVertices ref change ↔ new vertex data.
    readonly List<Vector3[]> _lastUploadedVertArrays = new();

    public Mesh Mesh => _mesh;
    public int LastUploadedLeafCount { get; private set; }
    public int LastUploadedVertexCount { get; private set; }
    public int LastUploadedTriangleIndexCount { get; private set; }

    public CombinedFaceMesh(Mesh mesh, int chunkResolution)
    {
        _mesh = mesh;
        _mesh.indexFormat = IndexFormat.UInt32;
        _chunkResolution = chunkResolution;
        _chunkTriangleTemplate = ChunkTriangleTemplate.Get(chunkResolution);
        _vertsPerChunk = ChunkTriangleTemplate.VertexCount(chunkResolution);
        _trisPerChunk = ChunkTriangleTemplate.TriangleCount(chunkResolution);
    }

    public void MarkLeavesChanged(IReadOnlyList<PlanetChunk> visibleLeaves)
    {
        _currentLeaves.Clear();
        for (int i = 0; i < visibleLeaves.Count; i++) _currentLeaves.Add(visibleLeaves[i]);
        _dirty = true;
    }

    public bool RebuildIfDirty()
    {
        if (!_dirty) return false;
        _dirty = false;

        // Early-out: if the leaf set + each leaf's generation matches what we last uploaded,
        // there's nothing new to push to the GPU. This is the dominant case when individual
        // child jobs complete during a parent's subdivision (the parent's still visible until
        // all 4 children are Active, but each child completion dirties the face).
        if (LeafSetMatchesLastUpload()) return false;

        // Count leaves that actually have CPU vertex data ready. A leaf can be in the visible
        // list but mid-generation (CpuVertices == null) — skip those to avoid uploading junk.
        int leavesWithData = 0;
        for (int i = 0; i < _currentLeaves.Count; i++)
            if (_currentLeaves[i].CpuVertices != null) leavesWithData++;

        int totalVerts = leavesWithData * _vertsPerChunk;
        int totalTris = leavesWithData * _trisPerChunk;

        EnsureCapacity(_vertScratch, totalVerts);
        EnsureCapacity(_normalScratch, totalVerts);
        EnsureCapacity(_triScratch, totalTris);
        _vertScratch.Clear();
        _normalScratch.Clear();
        _triScratch.Clear();

        int vertexOffset = 0;
        Bounds bounds = default;
        bool hasBounds = false;
        _chunkVertexOffsets.Clear();
        for (int i = 0; i < _currentLeaves.Count; i++)
        {
            var leaf = _currentLeaves[i];
            if (leaf.CpuVertices == null) continue;

            _chunkVertexOffsets[leaf] = vertexOffset;
            var verts = leaf.CpuVertices;
            var normals = leaf.CpuUnitSpherePoints;
            for (int v = 0; v < verts.Length; v++)
            {
                Vector3 vertex = verts[v];
                _vertScratch.Add(vertex);

                Vector3 normal = normals != null && v < normals.Length ? normals[v] : vertex.normalized;
                _normalScratch.Add(normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up);

                if (!hasBounds)
                {
                    bounds = new Bounds(vertex, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(vertex);
                }
            }

            for (int t = 0; t < _chunkTriangleTemplate.Length; t++)
                _triScratch.Add(_chunkTriangleTemplate[t] + vertexOffset);

            vertexOffset += verts.Length;
        }

        _mesh.Clear();
        _mesh.SetVertices(_vertScratch);
        _mesh.SetNormals(_normalScratch);
        _mesh.SetTriangles(_triScratch, 0, false);
        _mesh.bounds = hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.zero);

        LastUploadedLeafCount = leavesWithData;
        LastUploadedVertexCount = totalVerts;
        LastUploadedTriangleIndexCount = totalTris;

        // Snapshot what we just uploaded so the next dirty pass can detect "no change".
        _lastUploadedVertArrays.Clear();
        for (int i = 0; i < _currentLeaves.Count; i++)
        {
            var leaf = _currentLeaves[i];
            if (leaf.CpuVertices == null) continue;
            _lastUploadedVertArrays.Add(leaf.CpuVertices);
        }

        return true;
    }

    bool LeafSetMatchesLastUpload()
    {
        int withData = 0;
        for (int i = 0; i < _currentLeaves.Count; i++)
            if (_currentLeaves[i].CpuVertices != null) withData++;
        if (withData != _lastUploadedVertArrays.Count) return false;

        int idx = 0;
        for (int i = 0; i < _currentLeaves.Count; i++)
        {
            var leaf = _currentLeaves[i];
            if (leaf.CpuVertices == null) continue;
            if (!System.Object.ReferenceEquals(leaf.CpuVertices, _lastUploadedVertArrays[idx])) return false;
            idx++;
        }
        return true;
    }

    static void EnsureCapacity<T>(List<T> list, int capacity)
    {
        if (list.Capacity < capacity) list.Capacity = capacity;
    }
}
