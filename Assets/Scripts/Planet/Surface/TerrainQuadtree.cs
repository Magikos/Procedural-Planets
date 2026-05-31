using UnityEngine;

// Per-face quadtree of PlanetChunks (Phase A step 3). Handles tree construction, traversal,
// and same-face neighbor LOD queries. Cross-face neighbor lookup is the caller's job (combine
// CubeFaceTopology.TryMirrorUv with TerrainQuadtree.FindLeafContaining on the neighbor face).
//
// Step 3 deliberately ships without mesh jobs, lifecycle async machinery, or LOD-driven
// subdivision triggers — those land in step 4-5. What's here:
//   - Root construction
//   - Manual Subdivide(chunk) / Merge(chunk) operations
//   - FindLeafContaining(uv) descent
//   - GetSameFaceNeighborLod(chunk, edge) for INTERIOR edges (returns -1 for boundary edges)
//   - BuildToFixedDepth(depth) for testing
public sealed class TerrainQuadtree
{
    public readonly int FaceIndex;
    public readonly PlanetChunk Root;

    public TerrainQuadtree(int faceIndex)
    {
        FaceIndex = faceIndex;
        Root = new PlanetChunk(
            hashValue: 1u,
            detailLevel: 0,
            faceIndex: faceIndex,
            quadrant: 0,
            uvCenter: new Vector2(0.5f, 0.5f),
            uvHalfExtent: 0.5f,
            parent: null);
        Root.State = ChunkLifecycle.Active;
    }

    // Splits a leaf chunk into 4 children. Children inherit Pending state — they have hash,
    // face, uv, and parent pointer but no mesh yet (mesh job belongs to step 4+).
    public void Subdivide(PlanetChunk chunk)
    {
        if (chunk == null) throw new System.ArgumentNullException(nameof(chunk));
        if (chunk.FaceIndex != FaceIndex) throw new System.ArgumentException("Chunk belongs to a different face.", nameof(chunk));
        if (!chunk.IsLeaf) throw new System.InvalidOperationException("Chunk already subdivided.");
        if (chunk.DetailLevel >= PlanetChunk.MaxDetailLevel) throw new System.InvalidOperationException("Max detail level reached.");

        var children = new PlanetChunk[4];
        for (byte q = 0; q < 4; q++)
        {
            children[q] = new PlanetChunk(
                hashValue: PlanetChunk.ChildHash(chunk.HashValue, q),
                detailLevel: chunk.DetailLevel + 1,
                faceIndex: FaceIndex,
                quadrant: q,
                uvCenter: chunk.ChildUvCenter(q),
                uvHalfExtent: chunk.UvHalfExtent * 0.5f,
                parent: chunk);
        }
        chunk.Children = children;
        chunk.Generation++;
        chunk.State = ChunkLifecycle.ActiveWithChildren;
    }

    // Releases all descendants of `chunk` and marks it Active again. Children's meshes (if
    // any) are dropped — they'll be re-generated if the chunk subdivides later.
    public void Merge(PlanetChunk chunk)
    {
        if (chunk == null) throw new System.ArgumentNullException(nameof(chunk));
        if (chunk.IsLeaf) return; // already merged
        ReleaseSubtree(chunk);
        chunk.Children = null;
        chunk.Generation++;
        chunk.State = ChunkLifecycle.Active;
    }

    static void ReleaseSubtree(PlanetChunk chunk)
    {
        if (chunk.Children == null) return;
        foreach (var c in chunk.Children)
        {
            ReleaseSubtree(c);
            // Bumping Generation is critical: any pending mesh job for this chunk will be
            // detected as stale in DrainCompletedJobs (Generation != ExpectedGeneration) and
            // its result discarded — so we don't pay the CPU copy cost for chunks the camera
            // has already moved past.
            c.Generation++;
            c.Parent = null;
            c.Children = null;
            c.State = ChunkLifecycle.Unloading;
        }
    }

    // Descends from root following uv. Returns the deepest chunk that contains the point.
    // Returns null if uv is outside [0,1]² (caller should route to a cross-face lookup).
    public PlanetChunk FindLeafContaining(Vector2 uv)
    {
        if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return null;
        var node = Root;
        while (!node.IsLeaf)
        {
            byte q = node.QuadrantForUv(uv);
            node = node.Children[q];
        }
        return node;
    }

    // Returns the detail level of the chunk that touches `chunk` across `edge`, ONLY for edges
    // that are interior to this face. Returns -1 if the edge is on the face boundary (caller
    // must do a cross-face lookup).
    //
    // Works by descending from root with a query point just outside the chunk's edge. For
    // interior edges this point is still inside [0,1]², so the descent finds the neighbor leaf.
    public int GetSameFaceNeighborLod(PlanetChunk chunk, CubeEdge edge)
    {
        if (chunk == null) throw new System.ArgumentNullException(nameof(chunk));
        if (chunk.FaceIndex != FaceIndex) throw new System.ArgumentException("Chunk belongs to a different face.", nameof(chunk));

        // A query point one half-extent-of-the-tightest-possible-chunk outside the edge.
        // Using a fixed small epsilon would work too, but scaling with the chunk size keeps
        // floating-point precision comfortable at deep LODs.
        float eps = chunk.UvHalfExtent * 0.5f;
        if (eps < 1e-5f) eps = 1e-5f;

        Vector2 queryUv = edge switch
        {
            CubeEdge.East  => new Vector2(chunk.UvCenter.x + chunk.UvHalfExtent + eps, chunk.UvCenter.y),
            CubeEdge.West  => new Vector2(chunk.UvCenter.x - chunk.UvHalfExtent - eps, chunk.UvCenter.y),
            CubeEdge.North => new Vector2(chunk.UvCenter.x, chunk.UvCenter.y - chunk.UvHalfExtent - eps),
            CubeEdge.South => new Vector2(chunk.UvCenter.x, chunk.UvCenter.y + chunk.UvHalfExtent + eps),
            _ => throw new System.ArgumentOutOfRangeException(nameof(edge))
        };

        var neighbor = FindLeafContaining(queryUv);
        return neighbor != null ? neighbor.DetailLevel : -1;
    }

    // Returns true if the queried edge of `chunk` sits exactly on this face's boundary, meaning
    // any neighbor lookup must go cross-face. Used by callers to route between same-face and
    // cross-face neighbor queries.
    public static bool IsFaceBoundaryEdge(PlanetChunk chunk, CubeEdge edge)
    {
        const float eps = 1e-5f;
        return edge switch
        {
            CubeEdge.East  => chunk.UvCenter.x + chunk.UvHalfExtent >= 1f - eps,
            CubeEdge.West  => chunk.UvCenter.x - chunk.UvHalfExtent <= eps,
            CubeEdge.North => chunk.UvCenter.y - chunk.UvHalfExtent <= eps,
            CubeEdge.South => chunk.UvCenter.y + chunk.UvHalfExtent >= 1f - eps,
            _ => false
        };
    }

    // ---- Testing helpers ------------------------------------------------------------------

    public void BuildToFixedDepth(int depth)
    {
        if (depth < 0 || depth > PlanetChunk.MaxDetailLevel)
            throw new System.ArgumentOutOfRangeException(nameof(depth));
        if (!Root.IsLeaf) Merge(Root);
        SubdivideRecursive(Root, depth);
    }

    void SubdivideRecursive(PlanetChunk chunk, int targetDepth)
    {
        if (chunk.DetailLevel >= targetDepth) return;
        Subdivide(chunk);
        foreach (var c in chunk.Children)
            SubdivideRecursive(c, targetDepth);
    }

    // Counts all chunks in the tree (internal + leaf).
    public int CountChunks() => CountRecursive(Root);
    static int CountRecursive(PlanetChunk c)
    {
        if (c.IsLeaf) return 1;
        int total = 1;
        foreach (var child in c.Children) total += CountRecursive(child);
        return total;
    }

    public int CountLeaves() => CountLeavesRecursive(Root);
    static int CountLeavesRecursive(PlanetChunk c)
    {
        if (c.IsLeaf) return 1;
        int total = 0;
        foreach (var child in c.Children) total += CountLeavesRecursive(child);
        return total;
    }
}
