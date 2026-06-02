using System.Collections.Generic;
using UnityEngine;

// Per-vertex chunk-local UV grid for a given resolution. Every chunk at the same
// ChunkResolution uses the exact same UV array (values depend only on vertex row/col, not
// on which face/depth/quadrant the chunk lives at), so we share one cached array per
// resolution and SetUVs(0, ...) it on each chunk mesh. The mesh keeps its own copy after
// SetUVs, so the cached array stays read-only and never sees aliasing.
//
// Output: Vector2[res * res] with (x, y) = (col / (res-1), row / (res-1)) ∈ [0, 1]², used
// by the terrain shader as the sample coordinate into the per-chunk _BiomeMap texture.
public static class ChunkUvTemplate
{
    static readonly Dictionary<int, Vector2[]> _cache = new();

    public static Vector2[] Get(int resolution)
    {
        if (resolution < 2)
            throw new System.ArgumentOutOfRangeException(nameof(resolution), "Resolution must be >= 2.");

        if (_cache.TryGetValue(resolution, out var cached)) return cached;

        var uvs = new Vector2[resolution * resolution];
        float inv = 1f / (resolution - 1);
        for (int y = 0; y < resolution; y++)
        {
            float v = y * inv;
            for (int x = 0; x < resolution; x++)
                uvs[y * resolution + x] = new Vector2(x * inv, v);
        }
        _cache[resolution] = uvs;
        return uvs;
    }
}
