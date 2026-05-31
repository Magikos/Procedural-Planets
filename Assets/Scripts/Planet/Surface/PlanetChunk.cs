using UnityEngine;

// Quadtree node for a single chunk of a planet face. Identity lives in (FaceIndex, HashValue);
// geometry in UV space (face-local) is (UvCenter, UvHalfExtent). World/sphere data is derived
// lazily — Phase A step 3 only needs identity + tree links; mesh data lands in step 4-5.
//
// Hash bit encoding (step 3 final form):
//   - Face root: HashValue = 1 (leading 1 sentinel preserves leading zeros in children)
//   - Child: HashValue = (parent.HashValue << 2) | quadrant
//   - DetailLevel = (Log2(HashValue) >> 1)
//   - Max depth = 15 (32-bit hash: 1 sentinel + 30 quadrant bits)
//
// Quadrant encoding (NOTE: differs from LOD-Planets CCW convention for clean bit math):
//   bit 0 = u-bit  (1 = East half of parent)
//   bit 1 = v-bit  (1 = South half of parent)
//   NW = 0b00 = 0   NE = 0b01 = 1
//   SW = 0b10 = 2   SE = 0b11 = 3
public sealed class PlanetChunk
{
    public const int MaxDetailLevel = 15;

    public readonly uint HashValue;
    public readonly int DetailLevel;
    public readonly int FaceIndex;
    public readonly byte Quadrant;        // 0..3, NW/NE/SW/SE; 0 for face root

    public readonly Vector2 UvCenter;     // face UV space, [0,1]²
    public readonly float UvHalfExtent;   // half-size in face UV (root = 0.5)

    public PlanetChunk Parent;            // null for face root
    public PlanetChunk[] Children;        // null for leaf, length 4 when subdivided

    public ChunkLifecycle State;
    public uint Generation;               // bumped on every state-affecting transition

    // ---- CPU mesh data (populated when a chunk job completes; step 5+) ---------------------
    public Vector3[] CpuVertices;
    public Vector3[] CpuUnitSpherePoints;
    public float[] CpuElevations;
    public float[] CpuVertexRadii;        // |CpuVertices[i]| — bilinear surface sampler input
    public Bounds CpuLocalBounds;         // Local-space bounds of CpuVertices for LOD/culling diagnostics.
    public Color[] CpuColors;
    public Vector4[] CpuBiomeData;
    // True terrain-aware vertex normals from PlanetChunkNormalsJob (cross-product of neighbor
    // tangents). Preferred over CpuUnitSpherePoints when uploading mesh normals — gives
    // proper lighting on terrain elevation features rather than smooth-sphere shading.
    public Vector3[] CpuNormals;

    // The edge-fan mask used by the most recently scheduled mesh job. Stored so we can
    // detect "mask changed → re-mesh" cases when a neighbor's LOD shifts.
    public byte EdgeFanMaskAtSchedule;

    public PlanetChunk(uint hashValue, int detailLevel, int faceIndex, byte quadrant,
        Vector2 uvCenter, float uvHalfExtent, PlanetChunk parent)
    {
        HashValue = hashValue;
        DetailLevel = detailLevel;
        FaceIndex = faceIndex;
        Quadrant = quadrant;
        UvCenter = uvCenter;
        UvHalfExtent = uvHalfExtent;
        Parent = parent;
        State = ChunkLifecycle.Pending;
    }

    public bool IsLeaf => Children == null;
    public bool ContainsUv(Vector2 uv) =>
        uv.x >= UvCenter.x - UvHalfExtent && uv.x <= UvCenter.x + UvHalfExtent &&
        uv.y >= UvCenter.y - UvHalfExtent && uv.y <= UvCenter.y + UvHalfExtent;

    // Returns NW/NE/SW/SE for a UV that falls inside one of this chunk's four sub-quadrants.
    // Caller is responsible for checking ContainsUv first; this only inspects relative position.
    public byte QuadrantForUv(Vector2 uv)
    {
        byte q = 0;
        if (uv.x > UvCenter.x) q |= 0b01;
        if (uv.y > UvCenter.y) q |= 0b10;
        return q;
    }

    // Returns the UV center of a hypothetical child in the given quadrant.
    public Vector2 ChildUvCenter(byte quadrant)
    {
        float half = UvHalfExtent * 0.5f;
        float dx = (quadrant & 0b01) != 0 ?  half : -half;
        float dy = (quadrant & 0b10) != 0 ?  half : -half;
        return UvCenter + new Vector2(dx, dy);
    }

    // Bilinear sample of the per-vertex radius at the chunk's local UV (both axes in [0,1]
    // within this chunk's UV sub-region). Returns false if CPU data isn't populated yet.
    // Counterpart to TerrainFace.TrySampleSurfaceRadius for the chunked path.
    public bool TrySampleRadius(Vector2 chunkLocalUv, out float radius)
    {
        radius = 0f;
        if (CpuVertexRadii == null || CpuVertexRadii.Length == 0) return false;

        int r = (int)Mathf.Sqrt(CpuVertexRadii.Length);
        if (r * r != CpuVertexRadii.Length) return false; // not a square grid

        float gx = Mathf.Clamp01(chunkLocalUv.x) * (r - 1);
        float gy = Mathf.Clamp01(chunkLocalUv.y) * (r - 1);
        int x0 = Mathf.FloorToInt(gx);
        int y0 = Mathf.FloorToInt(gy);
        int x1 = Mathf.Min(x0 + 1, r - 1);
        int y1 = Mathf.Min(y0 + 1, r - 1);
        float fx = gx - x0;
        float fy = gy - y0;

        float r00 = CpuVertexRadii[x0 + y0 * r];
        float r10 = CpuVertexRadii[x1 + y0 * r];
        float r01 = CpuVertexRadii[x0 + y1 * r];
        float r11 = CpuVertexRadii[x1 + y1 * r];
        radius = Mathf.Lerp(Mathf.Lerp(r00, r10, fx), Mathf.Lerp(r01, r11, fx), fy);
        return radius > 0f;
    }

    // ---- Hash helpers ---------------------------------------------------------------------

    public static uint ChildHash(uint parentHash, byte quadrant) => (parentHash << 2) | quadrant;

    // Detail level of a hash = (position of leading 1 / 2). Hash=1 (root) → 0.
    public static int HashDetailLevel(uint hash)
    {
        if (hash == 0) return -1;
        int bit = 31;
        while ((hash & (1u << bit)) == 0) bit--;
        return bit >> 1;
    }
}

public enum ChunkLifecycle
{
    Pending,             // created, mesh/state not yet built
    Generating,          // mesh job in flight (step 4+)
    Active,              // leaf, mesh available, visible
    ActiveWithChildren,  // subdivided; this chunk's mesh is hidden in favor of children
    Subdividing,         // children Generating; this chunk's mesh still shown
    Merging,             // children being released; this chunk re-shown
    Unloading            // mesh disposed, chunk releasable
}
