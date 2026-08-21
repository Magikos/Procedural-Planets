using UnityEngine;

// Code-first rock generator. A RockDef + a seed makes one faceted low-poly boulder; the same seed always makes
// the same boulder, so a rock is derived from its world position exactly like the scatter instance that carries
// it and costs no storage.
//
// Why this exists: a scatter prototype draws ONE mesh per batch, so a library rock is the same rock everywhere,
// rotated. Generating K meshes per prototype gives K real shapes, and per-instance NON-UNIFORM scale multiplies
// those into far more apparent shapes than K — a squashed boulder and a stretched one do not read as the same
// rock, where a uniformly scaled one always does.
public struct RockDef
{
    public string Name;
    public float Size;          // longest axis in metres
    public Vector3 AxisBias;    // relative extent per axis before noise: (1,0.6,1) squat, (0.8,1.7,0.8) upright
    public float Roughness;     // 0 = smooth ellipsoid, 1 = heavily broken
    public int Subdivisions;    // 0 = 20 faces, 1 = 80, 2 = 320
    public float Buried;        // fraction of the height cut off below the pivot, so it sits IN the ground
    public Color Color;
}

public sealed class GeneratedRock
{
    public Mesh[] Lods = System.Array.Empty<Mesh>();
    public Mesh Collider;   // lowest-detail hull, for the streamed MeshCollider bubble (not wired yet)
    public float Height;    // metres above the pivot
    public Mesh Lod0 => Lods.Length > 0 ? Lods[0] : null;
}

public static class RockGenerator
{
    public static GeneratedRock Generate(RockDef def, int seed)
    {
        int sub = Mathf.Clamp(def.Subdivisions, 0, 3);
        var rock = new GeneratedRock();
        var lods = new Mesh[sub > 0 ? 2 : 1];
        lods[0] = BuildShell(def, seed, sub, out float height, true);
        for (int i = 1; i < lods.Length; i++)
        {
            lods[i] = BuildShell(def, seed, sub - i, out _, false);
            // MATCH LOD0's extents. The coarse shell samples the same displacement field at fewer vertices, so
            // it misses the outward bumps and comes out ~17% smaller — which reads as the rock popping smaller
            // the moment you walk far enough away for the LOD to switch. Rescale rather than resample, so the
            // silhouette keeps its size and only its facet count drops.
            MatchExtents(lods[i], lods[0]);
        }
        rock.Lods = lods;
        rock.Height = height;
        // The collider is the coarsest shell, not LOD0: a player stands on a rock, they do not feel its facets,
        // and cooking a 320-face hull per instance is the expensive half of the streamed-collider budget.
        rock.Collider = BuildShell(def, seed, 0, out _, false);
        return rock;
    }

    // Scale `mesh` about its base so its horizontal extents match `reference`. Y is scaled by the same factor
    // so the rock keeps its proportions, and the base stays at y=0 so it does not lift off the ground.
    static void MatchExtents(Mesh mesh, Mesh reference)
    {
        if (mesh == null || reference == null) return;
        // Match the OVERALL extent, not just the horizontal one: a coarse shell loses height as well as width,
        // and matching only x/z left the far LOD ~14% off on the diagonal.
        float refE = reference.bounds.extents.magnitude, curE = mesh.bounds.extents.magnitude;
        if (curE < 1e-4f) return;
        float s = refE / curE;
        if (Mathf.Abs(s - 1f) < 0.01f) return;

        Vector3[] v = mesh.vertices;
        for (int i = 0; i < v.Length; i++) v[i] = new Vector3(v[i].x * s, v[i].y * s, v[i].z * s);
        mesh.SetVertices(v);
        mesh.RecalculateBounds();
    }

    static Mesh BuildShell(RockDef def, int seed, int subdivisions, out float height, bool named)
    {
        Icosahedron(Mathf.Max(0, subdivisions), out Vector3[] verts, out int[] tris);

        Vector3 bias = def.AxisBias == Vector3.zero ? Vector3.one : def.AxisBias;
        float radius = Mathf.Max(0.05f, def.Size) * 0.5f;
        float rough = Mathf.Clamp01(def.Roughness);
        Vector3 off = SeedOffset(seed);

        float maxY = 0f, minY = 0f;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 n = verts[i];
            // Two octaves: the low one breaks the silhouette into lobes, the high one chips the faces. Without
            // the low octave every rock is a sphere with a rash and they all read the same from a distance.
            float lump = Noise(n * 1.35f + off) * 0.62f + Noise(n * 3.1f + off * 1.7f) * 0.24f;
            Vector3 p = n * (radius * (1f + lump * rough));
            p = new Vector3(p.x * bias.x, p.y * bias.y, p.z * bias.z);
            verts[i] = p;
            if (p.y > maxY) maxY = p.y;
            if (p.y < minY) minY = p.y;
        }

        // Sink the rock so `Buried` of its height is below the pivot, then flatten everything under the pivot
        // onto y=0. Terrain is not level, so a rock resting exactly on its lowest point floats on one side; a
        // cut base plus a little burial reads as a rock set INTO the ground from every angle.
        float full = maxY - minY;
        float lift = -minY - full * Mathf.Clamp01(def.Buried);
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 p = verts[i];
            p.y += lift;
            if (p.y < 0f) p.y = 0f;
            verts[i] = p;
        }
        height = maxY + lift;

        return FlatShade(verts, tris, def.Color, named ? $"Rock {def.Name} {seed}" : null);
    }

    // Splits every triangle into its own three vertices so each face gets one hard normal. That faceting IS the
    // low-poly look — a smooth-shaded version of the same geometry reads as a lumpy potato.
    static Mesh FlatShade(Vector3[] verts, int[] tris, Color tint, string name)
    {
        int faces = tris.Length / 3;
        var v = new Vector3[faces * 3];
        var n = new Vector3[faces * 3];
        var c = new Color32[faces * 3];
        var uv = new Vector2[faces * 3];
        var t = new int[faces * 3];

        for (int f = 0; f < faces; f++)
        {
            Vector3 a = verts[tris[f * 3]], b = verts[tris[f * 3 + 1]], d = verts[tris[f * 3 + 2]];
            Vector3 nrm = Vector3.Cross(b - a, d - a).normalized;
            // Per-face brightness jitter baked into vertex colour: it separates neighbouring facets even where
            // the light hits them at the same angle, which is what stops a boulder reading as one flat blob.
            float shade = 0.86f + 0.14f * Frac(Vector3.Dot(nrm, new Vector3(12.9898f, 78.233f, 37.719f)) * 43758.5453f);
            var col = (Color32)(tint * shade);
            int i0 = f * 3;
            v[i0] = a; v[i0 + 1] = b; v[i0 + 2] = d;
            n[i0] = n[i0 + 1] = n[i0 + 2] = nrm;
            c[i0] = c[i0 + 1] = c[i0 + 2] = col;
            // Each face samples a DIFFERENT cell of the stone texture. Mapping every face to the same 0..1 quad
            // makes one blotch pattern repeat identically across the whole rock, which reads as a tiled decal
            // rather than stone. Cell picked from the face index, so it stays deterministic.
            const int cells = 4;
            int cx = (f * 7) % cells, cy = ((f * 5) / cells) % cells;
            float cw = 1f / cells;
            Vector2 o = new Vector2(cx * cw, cy * cw);
            uv[i0] = o; uv[i0 + 1] = o + new Vector2(cw, 0f); uv[i0 + 2] = o + new Vector2(cw * 0.5f, cw);
            t[i0] = i0; t[i0 + 1] = i0 + 1; t[i0 + 2] = i0 + 2;
        }

        var mesh = new Mesh { name = name ?? "RockShell" };
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetColors(c);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static void Icosahedron(int subdivisions, out Vector3[] verts, out int[] tris)
    {
        const float t = 1.618034f; // golden ratio
        var v = new System.Collections.Generic.List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        var f = new System.Collections.Generic.List<int>
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };
        for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;

        for (int s = 0; s < subdivisions; s++)
        {
            var next = new System.Collections.Generic.List<int>(f.Count * 4);
            var mid = new System.Collections.Generic.Dictionary<long, int>();
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Midpoint(v, mid, a, b), bc = Midpoint(v, mid, b, c), ca = Midpoint(v, mid, c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            f = next;
        }

        verts = v.ToArray();
        tris = f.ToArray();
    }

    static int Midpoint(System.Collections.Generic.List<Vector3> v,
        System.Collections.Generic.Dictionary<long, int> cache, int a, int b)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (cache.TryGetValue(key, out int hit)) return hit;
        v.Add(((v[a] + v[b]) * 0.5f).normalized);
        cache[key] = v.Count - 1;
        return v.Count - 1;
    }

    // Value noise on a stable integer hash, NOT Mathf.PerlinNoise: the rock has to be identical across runs and
    // platforms because it is derived from the world seed, and Unity does not guarantee that for Perlin.
    static float Noise(Vector3 p)
    {
        Vector3 i = new(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z));
        Vector3 f = p - i;
        Vector3 u = new(Smooth(f.x), Smooth(f.y), Smooth(f.z));
        float n000 = Hash(i), n100 = Hash(i + Vector3.right), n010 = Hash(i + Vector3.up), n110 = Hash(i + new Vector3(1, 1, 0));
        float n001 = Hash(i + Vector3.forward), n101 = Hash(i + new Vector3(1, 0, 1)), n011 = Hash(i + new Vector3(0, 1, 1)), n111 = Hash(i + Vector3.one);
        float x00 = Mathf.Lerp(n000, n100, u.x), x10 = Mathf.Lerp(n010, n110, u.x);
        float x01 = Mathf.Lerp(n001, n101, u.x), x11 = Mathf.Lerp(n011, n111, u.x);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, u.y), Mathf.Lerp(x01, x11, u.y), u.z) * 2f - 1f;
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    static float Hash(Vector3 p)
    {
        unchecked
        {
            uint h = 216636261u;
            h = (h ^ (uint)Mathf.RoundToInt(p.x)) * 16777619u;
            h = (h ^ (uint)Mathf.RoundToInt(p.y)) * 16777619u;
            h = (h ^ (uint)Mathf.RoundToInt(p.z)) * 16777619u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    static float Frac(float x) => x - Mathf.Floor(x);

    static Vector3 SeedOffset(int seed)
    {
        unchecked
        {
            uint h = (uint)seed * 2654435761u;
            return new Vector3((h & 0xFF) * 0.37f, ((h >> 8) & 0xFF) * 0.53f, ((h >> 16) & 0xFF) * 0.71f);
        }
    }
}
