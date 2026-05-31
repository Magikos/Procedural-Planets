using System.Collections.Generic;

// Provides the shared chunk triangle index list for a given chunk vertex resolution.
//
// Phase A ships a SINGLE template per resolution — edge-fan transitions to coarser-LOD
// neighbors are handled in the mesh job by VERTEX SNAPPING (every other vertex along a
// coarser-LOD edge gets snapped to the midpoint of its two same-edge neighbors). The snapped
// vertex still exists in the index list but lies on the line between its surviving neighbors,
// so no crack forms. This approach produces some degenerate-area triangles, but the GPU cost
// is negligible at our chunk densities.
//
// Future optimization: 16 distinct triangle templates (one per neighbor-LOD mask combo) that
// emit true fan triangulation along coarser-LOD edges, eliminating degenerates. Not needed
// for Phase A.
public static class ChunkTriangleTemplate
{
    // Triangle winding: each cell (x, y) emits two triangles
    //   tri 1: (top-left, bottom-right, bottom-left)   "/" diagonal
    //   tri 2: (top-left, top-right,    bottom-right)
    // matches TerrainFaceMeshJob — CCW when the chunk normal faces away from the planet center.
    static readonly Dictionary<int, int[]> _cache = new();

    public static int[] Get(int resolution)
    {
        if (resolution < 2)
            throw new System.ArgumentOutOfRangeException(nameof(resolution), "Resolution must be >= 2.");

        if (_cache.TryGetValue(resolution, out var cached)) return cached;

        int cellsPerSide = resolution - 1;
        int triangleCount = cellsPerSide * cellsPerSide * 6;
        var triangles = new int[triangleCount];

        for (int y = 0; y < cellsPerSide; y++)
        {
            for (int x = 0; x < cellsPerSide; x++)
            {
                int triIdx = (y * cellsPerSide + x) * 6;
                int topLeft = y * resolution + x;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + resolution;
                int bottomRight = bottomLeft + 1;

                triangles[triIdx]     = topLeft;
                triangles[triIdx + 1] = bottomRight;
                triangles[triIdx + 2] = bottomLeft;
                triangles[triIdx + 3] = topLeft;
                triangles[triIdx + 4] = topRight;
                triangles[triIdx + 5] = bottomRight;
            }
        }

        _cache[resolution] = triangles;
        return triangles;
    }

    public static int TriangleCount(int resolution) => (resolution - 1) * (resolution - 1) * 6;
    public static int VertexCount(int resolution) => resolution * resolution;
}
