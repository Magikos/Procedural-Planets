#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// Runtime self-test for PlanetChunk + TerrainQuadtree invariants (Phase A step 3). Runs at
// `BeforeSceneLoad` in Editor / Development builds. Failures surface as Debug.LogError so the
// dev sees them immediately on Play without any per-frame overhead.
//
// Covers:
//   - Hash encoding round-trip (HashDetailLevel, ChildHash)
//   - Subdivide → 4 children with correct hash, UV bounds, quadrant
//   - BuildToFixedDepth produces 4^depth leaves
//   - FindLeafContaining returns the deepest chunk for a given UV
//   - Same-face neighbor LOD: equal-depth neighbors are reported correctly
//   - Same-face neighbor LOD: differing-depth neighbors report the coarser side's depth
//   - IsFaceBoundaryEdge correctly identifies face boundaries
//   - Merge restores leaf status without leaving dangling state
public static class TerrainQuadtreeSelfTest
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RunAtStartup()
    {
        if (!TryRun(out string error))
            Debug.LogError($"[TerrainQuadtree] Self-test FAILED: {error}");
    }

    public static bool TryRun(out string error)
    {
        error = null;

        // ---- Hash encoding ---------------------------------------------------------------
        if (PlanetChunk.HashDetailLevel(1u) != 0)
        { error = "HashDetailLevel(1) should be 0"; return false; }
        if (PlanetChunk.ChildHash(1u, 0b11) != 0b111)
        { error = "ChildHash(1, 3) should be 0b111"; return false; }
        if (PlanetChunk.HashDetailLevel(0b111) != 1)
        { error = "HashDetailLevel(0b111) should be 1"; return false; }
        if (PlanetChunk.HashDetailLevel(0b1_00_01_10_11) != 4)
        { error = "HashDetailLevel of a depth-4 hash should be 4"; return false; }

        // ---- Root ------------------------------------------------------------------------
        var tree = new TerrainQuadtree(0);
        if (tree.Root.HashValue != 1u) { error = "Root hash should be 1"; return false; }
        if (tree.Root.DetailLevel != 0) { error = "Root detail level should be 0"; return false; }
        if (tree.Root.UvCenter != new Vector2(0.5f, 0.5f)) { error = "Root UV center wrong"; return false; }
        if (tree.Root.UvHalfExtent != 0.5f) { error = "Root UV half-extent should be 0.5"; return false; }
        if (!tree.Root.IsLeaf) { error = "Root should be a leaf"; return false; }

        // ---- Subdivide -------------------------------------------------------------------
        tree.Subdivide(tree.Root);
        if (tree.Root.IsLeaf) { error = "Root should be subdivided"; return false; }
        if (tree.Root.Children.Length != 4) { error = "Root should have 4 children"; return false; }
        for (byte q = 0; q < 4; q++)
        {
            var c = tree.Root.Children[q];
            if (c.Quadrant != q) { error = $"Child {q} has quadrant {c.Quadrant}"; return false; }
            if (c.DetailLevel != 1) { error = $"Child {q} detail level should be 1"; return false; }
            if (c.HashValue != PlanetChunk.ChildHash(1u, q)) { error = $"Child {q} hash wrong"; return false; }
            if (c.UvHalfExtent != 0.25f) { error = $"Child {q} half-extent should be 0.25"; return false; }
            if (c.Parent != tree.Root) { error = $"Child {q} parent wrong"; return false; }
        }
        // UV centers: NW=(.25,.25), NE=(.75,.25), SW=(.25,.75), SE=(.75,.75)
        if (tree.Root.Children[0b00].UvCenter != new Vector2(0.25f, 0.25f)) { error = "NW UV center"; return false; }
        if (tree.Root.Children[0b01].UvCenter != new Vector2(0.75f, 0.25f)) { error = "NE UV center"; return false; }
        if (tree.Root.Children[0b10].UvCenter != new Vector2(0.25f, 0.75f)) { error = "SW UV center"; return false; }
        if (tree.Root.Children[0b11].UvCenter != new Vector2(0.75f, 0.75f)) { error = "SE UV center"; return false; }

        // ---- BuildToFixedDepth -----------------------------------------------------------
        tree.BuildToFixedDepth(3);
        int expectedLeaves = 4 * 4 * 4;
        if (tree.CountLeaves() != expectedLeaves)
        { error = $"BuildToFixedDepth(3) produced {tree.CountLeaves()} leaves, expected {expectedLeaves}"; return false; }

        // ---- FindLeafContaining ----------------------------------------------------------
        var leaf = tree.FindLeafContaining(new Vector2(0.1f, 0.1f));
        if (leaf == null || leaf.DetailLevel != 3) { error = "FindLeafContaining(0.1,0.1) wrong depth"; return false; }
        if (!leaf.ContainsUv(new Vector2(0.1f, 0.1f))) { error = "Returned leaf doesn't contain query"; return false; }
        if (tree.FindLeafContaining(new Vector2(-0.1f, 0.5f)) != null) { error = "Out-of-range uv should return null"; return false; }
        if (tree.FindLeafContaining(new Vector2(1.5f, 0.5f)) != null) { error = "Out-of-range uv should return null"; return false; }

        // ---- Same-depth neighbor lookup --------------------------------------------------
        // For a uniform depth-3 tree, an interior chunk's neighbors are also at depth 3.
        var interior = tree.FindLeafContaining(new Vector2(0.4f, 0.4f));
        if (interior == null) { error = "Interior chunk not found"; return false; }
        if (TerrainQuadtree.IsFaceBoundaryEdge(interior, CubeEdge.East))
        { error = "Interior chunk should not be on face boundary"; return false; }
        int eastLod = tree.GetSameFaceNeighborLod(interior, CubeEdge.East);
        if (eastLod != 3) { error = $"Interior east neighbor LOD expected 3, got {eastLod}"; return false; }
        int northLod = tree.GetSameFaceNeighborLod(interior, CubeEdge.North);
        if (northLod != 3) { error = $"Interior north neighbor LOD expected 3, got {northLod}"; return false; }

        // ---- Differing-depth neighbor lookup ---------------------------------------------
        // Merge one half of the tree to depth 2, leaving the other half at depth 3. Confirm a
        // depth-3 chunk on the boundary between halves reports its (coarser) neighbor.
        tree.BuildToFixedDepth(0);
        tree.Subdivide(tree.Root); // depth 1
        var nwChild = tree.Root.Children[0b00];
        var neChild = tree.Root.Children[0b01];
        tree.Subdivide(nwChild);          // NW at depth 2
        tree.Subdivide(neChild);          // NE at depth 2
        // Subdivide NE's NW child further so its W edge (interior to face) borders a depth-2 NE chunk.
        var neNw = neChild.Children[0b00]; // depth 2, sits at (0.625, 0.125), half=0.125
        tree.Subdivide(neNw);              // depth 3
        var deepChunk = neNw.Children[0b00]; // depth 3, sits at (0.5625, 0.0625), half=0.0625
        if (deepChunk.DetailLevel != 3) { error = "Deep chunk depth wrong"; return false; }
        if (TerrainQuadtree.IsFaceBoundaryEdge(deepChunk, CubeEdge.West))
        { error = "Deep chunk west edge should be interior"; return false; }
        int westLodCoarse = tree.GetSameFaceNeighborLod(deepChunk, CubeEdge.West);
        if (westLodCoarse != 2)
        { error = $"Deep chunk's west neighbor LOD expected 2 (NW depth-2 sibling), got {westLodCoarse}"; return false; }

        // ---- Boundary edge detection -----------------------------------------------------
        // Root has all 4 edges on the face boundary.
        tree.BuildToFixedDepth(0);
        if (!TerrainQuadtree.IsFaceBoundaryEdge(tree.Root, CubeEdge.East)) { error = "Root East should be boundary"; return false; }
        if (!TerrainQuadtree.IsFaceBoundaryEdge(tree.Root, CubeEdge.North)) { error = "Root North should be boundary"; return false; }
        // Root's E edge: no same-face neighbor exists.
        if (tree.GetSameFaceNeighborLod(tree.Root, CubeEdge.East) != -1)
        { error = "Root East should have no same-face neighbor (returns -1)"; return false; }

        // A subdivided NW child should have its NW corner edges on the face boundary, SE
        // edges interior.
        tree.Subdivide(tree.Root);
        var rootNw = tree.Root.Children[0b00]; // covers (0,0)-(0.5,0.5)
        if (!TerrainQuadtree.IsFaceBoundaryEdge(rootNw, CubeEdge.West)) { error = "NW child's West should be boundary"; return false; }
        if (!TerrainQuadtree.IsFaceBoundaryEdge(rootNw, CubeEdge.North)) { error = "NW child's North should be boundary"; return false; }
        if (TerrainQuadtree.IsFaceBoundaryEdge(rootNw, CubeEdge.East)) { error = "NW child's East should be interior"; return false; }
        if (TerrainQuadtree.IsFaceBoundaryEdge(rootNw, CubeEdge.South)) { error = "NW child's South should be interior"; return false; }

        // ---- Merge -----------------------------------------------------------------------
        tree.BuildToFixedDepth(2);
        if (tree.CountLeaves() != 16) { error = "Pre-merge leaf count wrong"; return false; }
        tree.Merge(tree.Root);
        if (!tree.Root.IsLeaf) { error = "Root should be a leaf after Merge"; return false; }
        if (tree.CountLeaves() != 1) { error = "Post-merge leaf count should be 1"; return false; }
        if (tree.Root.State != ChunkLifecycle.Active) { error = "Post-merge root state wrong"; return false; }

        return true;
    }
}
#endif
