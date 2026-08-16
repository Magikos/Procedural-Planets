using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Stage 3 (foliage) of the tree generator (plan 006): place a crossed pair of textured leaf cards at each sprout
// and weld them into one foliage mesh. Rendered with the biome's Synty FoliageLit material, whose leaf-patch
// _BaseMap alpha cuts the leaf silhouette — so cards read as leaves, not flat cardboard. Vertex color carries
// FoliageLit's inputs: B = leaf mask (1 => treat as leaf), G = leaf AO (1 => exposed/bright). `leafScale` sizes
// the clumps (LODs), `skip` drops sprouts for lower LODs. Deterministic per seed.
public static class TreeLeafMesher
{
    static readonly Color LeafVtx = new Color(1f, 1f, 1f, 1f); // G=1 exposed AO, B=1 -> FoliageLit leaf mask on

    public static Mesh Build(TreeSkeleton sk, float leafScale = 1f, int skip = 1)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var cols = new List<Color>();
        var tris = new List<int>();

        skip = Mathf.Max(1, skip);
        if (sk != null)
            for (int i = 0; i < sk.Sprouts.Count; i += skip)
                AddCluster(sk.Sprouts[i], leafScale, verts, uvs, cols, tris);

        var mesh = new Mesh
        {
            name = "Tree foliage (generated)",
            indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AddCluster(TreeSprout s, float leafScale, List<Vector3> verts, List<Vector2> uvs,
        List<Color> cols, List<int> tris)
    {
        // LeafGroup 1 = a big drooping frond (palm); 2 = a spiky needle tuft (open pine); 3 = a weeping leaf strand
        // (willow); 0 = a crossed clump.
        if (s.LeafGroup == 1) { AddFrond(s, leafScale, verts, uvs, cols, tris); return; }
        if (s.LeafGroup == 2) { AddNeedleTuft(s, leafScale, verts, uvs, cols, tris); return; }
        if (s.LeafGroup == 3) { AddWeepingStrand(s, leafScale, verts, uvs, cols, tris); return; }
        if (s.LeafGroup == 4) { AddBlade(s, leafScale, verts, uvs, cols, tris); return; }

        float size = Mathf.Max(0.02f, s.Size * leafScale);
        Vector3 fwd = s.Direction.sqrMagnitude > 1e-6f ? s.Direction.normalized : Vector3.up;
        Vector3 nrm = s.Normal.sqrMagnitude > 1e-6f ? s.Normal.normalized : Vector3.up;
        Quaternion rot = Quaternion.LookRotation(fwd, nrm);

        // Two crossed cards centered on the anchor (a leaf clump readable from any angle; FoliageLit is Cull Off).
        AddQuad(s.Position, rot, size, planeB: false, verts, uvs, cols, tris);
        AddQuad(s.Position, rot, size, planeB: true, verts, uvs, cols, tris);
    }

    // A palm frond: a long tapering ribbon that leaves the crown along the sprout direction and droops under
    // gravity. The palm atlas (Leaf_Palm_01) packs 3 fronds side by side (dead-brown, green, dark-green), so the
    // ribbon maps u to ONE cell (mostly green, a few dead) instead of the whole sheet. v runs base(0)->tip(1).
    // Single strip (FoliageLit is Cull Off, so both faces show).
    static void AddFrond(TreeSprout s, float leafScale, List<Vector3> verts, List<Vector2> uvs,
        List<Color> cols, List<int> tris)
    {
        float len = Mathf.Max(0.2f, s.Size * leafScale);
        Vector3 dir = s.Direction.sqrMagnitude > 1e-6f ? s.Direction.normalized : Vector3.up;
        // A palm frond leaves the crown arching UP and out, then bends over under its own weight and droops at the
        // tip. Launching it flat and sagging from the first segment gives a limp fan instead of that arc.
        dir = (dir + Vector3.up * 0.95f).normalized;

        const float cells = 3f, inset = 0.01f;
        float hv = Hash01(s.Position);
        int cell = hv < 0.12f ? 0 : (hv < 0.6f ? 1 : 2); // 0 = dead-brown, 1 = green, 2 = dark-green
        float uMin = cell / cells + inset, uMax = (cell + 1f) / cells - inset;

        const int segs = 7; // enough samples to read as a curve rather than a bent stick
        float segLen = len / segs;
        Vector3 p = s.Position;
        int prevL = -1, prevR = -1;

        for (int i = 0; i <= segs; i++)
        {
            float tt = i / (float)segs;
            float w = Mathf.Lerp(0.16f, 0.06f, tt * tt) * len; // holds width through the arc, then points at the tip
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-5f) side = Vector3.Cross(dir, Vector3.forward);
            side = side.normalized;

            int li = verts.Count;
            verts.Add(p - side * w); uvs.Add(new Vector2(uMin, tt)); cols.Add(LeafVtx);
            verts.Add(p + side * w); uvs.Add(new Vector2(uMax, tt)); cols.Add(LeafVtx);
            if (i > 0)
            {
                tris.Add(prevL); tris.Add(prevR); tris.Add(li + 1);
                tris.Add(prevL); tris.Add(li + 1); tris.Add(li);
            }
            prevL = li; prevR = li + 1;

            p += dir * segLen;
            dir = (dir + Vector3.down * 0.42f).normalized; // steady bend: up at the base, over the top, down at the tip
        }
    }

    // A weeping strand (willow): a thin leafy ribbon that leaves the branch tip going a little outward, then
    // cascades straight down under gravity. UV v runs top->tip so the leaf texture reads along the strand.
    static void AddWeepingStrand(TreeSprout s, float leafScale, List<Vector3> verts, List<Vector2> uvs,
        List<Color> cols, List<int> tris)
    {
        float len = Mathf.Max(0.3f, s.Size * leafScale);
        // Wide enough that neighbouring strands overlap into a curtain — thin ones read as loose blades of grass
        // hanging off bare sticks rather than willow foliage.
        float w = Mathf.Max(0.05f, len * 0.11f);
        Vector3 outward = new Vector3(s.Direction.x, 0f, s.Direction.z);
        outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.right;
        Vector3 d = (outward * 0.5f + Vector3.down).normalized; // start out, then curve down
        Vector3 side = Vector3.Cross(d, Vector3.up);
        side = side.sqrMagnitude > 1e-5f ? side.normalized : outward;

        const int segs = 5;
        float segLen = len / segs;
        Vector3 p = s.Position;
        int prevL = -1, prevR = -1;

        for (int i = 0; i <= segs; i++)
        {
            float tt = i / (float)segs;
            int li = verts.Count;
            verts.Add(p - side * w); uvs.Add(new Vector2(0f, 1f - tt)); cols.Add(LeafVtx);
            verts.Add(p + side * w); uvs.Add(new Vector2(1f, 1f - tt)); cols.Add(LeafVtx);
            if (i > 0)
            {
                tris.Add(prevL); tris.Add(prevR); tris.Add(li + 1);
                tris.Add(prevL); tris.Add(li + 1); tris.Add(li);
            }
            prevL = li; prevR = li + 1;
            p += d * segLen;
            d = (d + Vector3.down * 0.6f).normalized; // sag toward straight down
        }
    }

    // A blade / fern frond: a narrow tapering ribbon that leaves the crown steeply and arcs over. Same idea as
    // the palm frond, but UV u spans the WHOLE texture (0..1) instead of one cell of the palm atlas, so it wears
    // the biome's ordinary leaf material — which is what makes it reusable for ferns, reeds and blades generally.
    static void AddBlade(TreeSprout s, float leafScale, List<Vector3> verts, List<Vector2> uvs,
        List<Color> cols, List<int> tris)
    {
        float len = Mathf.Max(0.15f, s.Size * leafScale);
        Vector3 dir = s.Direction.sqrMagnitude > 1e-6f ? s.Direction.normalized : Vector3.up;
        dir = (dir + Vector3.up * 1.5f).normalized; // ferns throw their fronds up before they arch over

        const int segs = 6;
        float segLen = len / segs;
        Vector3 p = s.Position;
        int prevL = -1, prevR = -1;

        for (int i = 0; i <= segs; i++)
        {
            float tt = i / (float)segs;
            float w = Mathf.Lerp(0.10f, 0.02f, tt * tt) * len; // wide near the base, pointed at the tip
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-5f) side = Vector3.Cross(dir, Vector3.forward);
            side = side.normalized;

            int li = verts.Count;
            verts.Add(p - side * w); uvs.Add(new Vector2(0f, tt)); cols.Add(LeafVtx);
            verts.Add(p + side * w); uvs.Add(new Vector2(1f, tt)); cols.Add(LeafVtx);
            if (i > 0)
            {
                tris.Add(prevL); tris.Add(prevR); tris.Add(li + 1);
                tris.Add(prevL); tris.Add(li + 1); tris.Add(li);
            }
            prevL = li; prevR = li + 1;
            p += dir * segLen;
            dir = (dir + Vector3.down * 0.30f).normalized;
        }
    }

    static float Hash01(Vector3 p)
    {
        float h = Mathf.Sin(Vector3.Dot(p, new Vector3(12.9898f, 78.233f, 37.719f))) * 43758.5453f;
        return h - Mathf.Floor(h);
    }

    // FoliageLit reads leaf AO from vertex-color green; B=1 keeps the leaf mask on.
    static Color Leaf(float ao) => new Color(1f, Mathf.Clamp01(ao), 1f, 1f);

    // A needle tuft: a small spiky drooping puff at a branch tip (open pine). Solid geometry, AO in vtx.G.
    static void AddNeedleTuft(TreeSprout s, float leafScale, List<Vector3> verts, List<Vector2> uvs,
        List<Color> cols, List<int> tris)
    {
        float r = Mathf.Max(0.05f, s.Size * leafScale);
        Vector3 c = s.Position;
        Vector3 apex = c + Vector3.up * (r * 0.8f);
        int n = 16; // 8 spikes + 8 notches

        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = TuftPoint(c, i, n, r);
            Vector3 p1 = TuftPoint(c, i + 1, n, r);
            float g0 = (i % 2) == 0 ? 1f : 0.6f;
            float g1 = ((i + 1) % 2) == 0 ? 1f : 0.6f;
            int i0 = verts.Count;
            verts.Add(apex); verts.Add(p0); verts.Add(p1);
            uvs.Add(new Vector2(0.5f, 1f)); uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
            cols.Add(Leaf(0.5f)); cols.Add(Leaf(g0)); cols.Add(Leaf(g1));
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
            tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1); // back face
        }
    }

    static Vector3 TuftPoint(Vector3 c, int i, int n, float r)
    {
        bool spike = (i % 2) == 0;
        float a = i / (float)n * Mathf.PI * 2f;
        float rr = spike ? r : r * 0.45f;
        float yy = spike ? -r * 0.5f : -r * 0.15f; // spikes droop lower than notches
        return c + new Vector3(Mathf.Cos(a) * rr, yy, Mathf.Sin(a) * rr);
    }

    static void AddQuad(Vector3 center, Quaternion rot, float size, bool planeB,
        List<Vector3> verts, List<Vector2> uvs, List<Color> cols, List<int> tris)
    {
        float h = size * 0.5f;
        Vector3 a, b, c, d;
        if (!planeB) { a = new Vector3(-h, 0, -h); b = new Vector3(h, 0, -h); c = new Vector3(h, 0, h); d = new Vector3(-h, 0, h); }
        else { a = new Vector3(0, -h, -h); b = new Vector3(0, h, -h); c = new Vector3(0, h, h); d = new Vector3(0, -h, h); }

        int i0 = verts.Count;
        verts.Add(center + rot * a); uvs.Add(new Vector2(0, 0)); cols.Add(LeafVtx);
        verts.Add(center + rot * b); uvs.Add(new Vector2(1, 0)); cols.Add(LeafVtx);
        verts.Add(center + rot * c); uvs.Add(new Vector2(1, 1)); cols.Add(LeafVtx);
        verts.Add(center + rot * d); uvs.Add(new Vector2(0, 1)); cols.Add(LeafVtx);
        tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
        tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
    }

    // A solid low-poly conifer: layered drooping skirts up the trunk forming a dense fir cone (this pack has no
    // needle texture, so foliage is flat-shaded dark-green geometry via Scatter/VertexColorLit, not cards).
    // Double-sided so it reads solid from any angle. `scale` thins the cone for LODs.
    // baseFrac/radiusFrac shape the cone: where the lowest skirt sits up the trunk, and the cone's half-width, both
    // as fractions of tree height. Fir/spruce = low and wide (the default); a narrow columnar cypress is the same
    // mesh with a small radiusFrac.
    public static Mesh BuildConiferCone(TreeSkeleton sk, int seed, float scale = 1f, int tiers = 18, int spokes = 11,
        float baseFrac = 0.3f, float radiusFrac = 0.26f, float droop = 0.7f)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var cols = new List<Color>();
        var tris = new List<int>();

        if (sk?.Trunk != null)
        {
            float h = Mathf.Max(1f, sk.Height);
            // baseFrac is where the lowest skirt HANGS FROM, not where foliage ends: the skirt droops ~0.7 of that
            // tier's radius below it, and the bottom tier is the widest. So the visible clear trunk is roughly
            // (baseFrac - 0.7 * radiusFrac) * h — set baseFrac below that and the skirts bury themselves in the
            // ground and hide the trunk completely.
            float baseY = h * baseFrac;
            float topY = h * 1.1f; // spire rises above the trunk tip so the trunk stays hidden
            float maxR = h * radiusFrac * scale;
            Vector3 b = sk.Trunk.Base;
            float tierGap = (topY - baseY) / tiers;

            for (int t = 0; t < tiers; t++)
            {
                float ft = t / (float)(tiers - 1);
                float y = Mathf.Lerp(baseY, topY, ft);
                float r = Mathf.Max(0.04f, maxR * (1f - ft * 0.94f));
                float apexY = y + tierGap * 1.5f; // skirt peak on the trunk axis
                // Random roll per tier (deterministic per tree) so the spikes don't line up in a uniform spiral.
                float roll = Hash01(new Vector3(seed * 0.1234f + t * 1.7f, t * 2.31f, seed * 0.077f)) * Mathf.PI * 2f;
                Vector3 apex = b + new Vector3(0f, apexY, 0f);

                // Zigzag rim: alternate spike (far + drooping low) and notch (near + high) so the silhouette reads
                // as pine spikes, not a smooth cone. Fan each pair back to the axis apex so the body stays solid.
                int n = spokes * 2;
                for (int s = 0; s < n; s++)
                {
                    Vector3 pA = RimPoint(b, s, n, roll, r, y, droop);
                    Vector3 pB = RimPoint(b, s + 1, n, roll, r, y, droop);
                    // Bake AO into vtx.G (FoliageLit reads it): interior/apex dark, spike tips bright, notches mid,
                    // + a little per-spike jitter so the cone reads as many leaves catching light, not a solid block.
                    float jitA = 0.85f + 0.15f * Hash01(pA * 3.1f);
                    float jitB = 0.85f + 0.15f * Hash01(pB * 3.1f);
                    Color cApex = Leaf(0.42f);
                    Color cA = Leaf(((s % 2) == 0 ? 1f : 0.6f) * jitA);
                    Color cB = Leaf((((s + 1) % 2) == 0 ? 1f : 0.6f) * jitB);
                    int i0 = verts.Count;
                    verts.Add(apex); verts.Add(pA); verts.Add(pB);
                    uvs.Add(new Vector2(0.5f, 1f)); uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                    cols.Add(cApex); cols.Add(cA); cols.Add(cB);
                    tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                    tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1); // back face
                }
            }
        }

        // A rim vertex: even index = spike (full radius, drooping low), odd = notch (pulled in, high).
        static Vector3 RimPoint(Vector3 b, int s, int n, float roll, float r, float y, float droop)
        {
            bool spike = (s % 2) == 0;
            float a = s / (float)n * Mathf.PI * 2f + roll;
            float rr = spike ? r : r * 0.5f;
            float yy = spike ? y - r * droop : y - r * droop * 0.36f;
            return b + new Vector3(Mathf.Cos(a) * rr, yy, Mathf.Sin(a) * rr);
        }

        var mesh = new Mesh
        {
            name = "Conifer foliage (generated)",
            indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
