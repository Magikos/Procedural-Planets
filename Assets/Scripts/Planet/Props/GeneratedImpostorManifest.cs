using System.Collections.Generic;
using UnityEngine;

// Disk cache of the impostor atlases for GENERATED props, so the far-field billboards are not re-baked on
// every load. Baked by Tools > ProceduralPlanets > Bake Generated Impostor Atlases.
//
// Why only the atlases: generating the meshes themselves is cheap — MEASURED at 45 ms for every tree variant
// and 150 ms for every rock — so they stay runtime-generated and the edit-a-TreeDef-and-hit-play loop keeps
// working. Baking the atlas is the expensive half, about 105 ms and 6.7 MB per atlas, which was the
// whole of the 7.6 s scatter-renderer phase at load.
//
// Staleness is handled by storing the SHAPE HASH of the meshes each atlas was baked from. A prototype only
// uses a cached atlas when its freshly generated meshes still hash the same, so editing a TreeDef or the
// generator falls back to a live bake for that species: slower, never wrong. Nothing has to be remembered to
// rebake — a stale entry costs load time, and the console says which.
public sealed class GeneratedImpostorManifest : ScriptableObject
{
    public const string ResourcePath = "Settings/GeneratedImpostors";

    [System.Serializable]
    public sealed class Entry
    {
        public string Key;          // ImpostorKey (the prototype display name) the atlas was baked for
        public string ShapeHash;    // hash of the LOD0 meshes at bake time
        public Texture2D Atlas;
        public Texture2D Normal;
        public bool HasSurfaceData;
        public int GridN; // zero keeps the legacy 128-pixel-cell layout
    }

    public Entry[] Entries = System.Array.Empty<Entry>();

    static GeneratedImpostorManifest _loaded;
    static bool _tried;

    // Resources.Load once per domain: the injection asks per prototype, and a miss is as common as a hit
    // (any species whose def changed), so this must not re-hit the asset database each time.
    public static GeneratedImpostorManifest Active
    {
        get
        {
            if (_tried) return _loaded;
            _tried = true;
            _loaded = Resources.Load<GeneratedImpostorManifest>(ResourcePath);
            return _loaded;
        }
    }

    public static void ForgetCache() { _tried = false; _loaded = null; }

    // Attaches the cached card this prototype was baked from, when the manifest still holds one whose
    // appearance hash matches the meshes and tints the prototype now carries. A miss is not an error: the
    // prototype keeps no card and ScatterImpostorFactory bakes it live - slower, never wrong.
    //
    // Both halves of the lookup come from the FINISHED prototype. That is the point: the atlas is per
    // prototype, so nothing has to guess what a canonical variant of a species would have looked like.
    public static ScatterPrototypeDto WithCachedAtlas(ScatterPrototypeDto p)
    {
        if (p == null || !p.HasImpostor || !TryGet(p.ImpostorKey, p.ImpostorSourceHash, out Texture2D atlas, out Texture2D normal, out int gridN, out bool hasSurfaceData))
            return p;
        return p with { BakedImpostorAtlas = atlas, BakedImpostorNormal = normal, BakedImpostorGridN = gridN, BakedImpostorHasSurfaceData = hasSurfaceData };
    }

    // Returns the cached atlas when `hash` still matches what was baked for this key.
    public static bool TryGet(string key, string hash, out Texture2D atlas, out Texture2D normal, out int gridN, out bool hasSurfaceData)
    {
        hasSurfaceData = false;
        gridN = 0;
        atlas = null;
        normal = null;
        if (string.IsNullOrEmpty(key)) return false;
        GeneratedImpostorManifest m = Active;
        if (m?.Entries == null) return false;

        foreach (Entry e in m.Entries)
        {
            if (e == null || e.Key != key || e.Atlas == null) continue;
            if (e.ShapeHash != hash) return false; // stale: the def or the generator moved
            atlas = e.Atlas;
            normal = e.Normal;
            gridN = e.GridN;
            hasSurfaceData = e.HasSurfaceData;
            return true;
        }
        return false;
    }

    // Shape hash PLUS the colours the prop will be drawn in.
    //
    // Colour has to be in here. An impostor card bakes the prop's finished APPEARANCE, not just its outline, so
    // a tint change makes every cached atlas wrong while leaving the geometry identical — and the symptom is
    // nasty to diagnose: the far card keeps the old colour and the prop visibly changes shade as you walk up to
    // it and the mesh takes over. Shape-only hashing shipped exactly that bug.
    public static string AppearanceHash(IReadOnlyList<Mesh> meshes, params Color[] colors)
    {
        string shape = ShapeHash(meshes);
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in shape) { h ^= c; h *= 16777619u; }
            if (colors != null)
            {
                foreach (Color col in colors)
                {
                    // Quantised to 1/255: finer than the eye, coarser than float noise.
                    h ^= (uint)Mathf.RoundToInt(Mathf.Clamp01(col.r) * 255f); h *= 16777619u;
                    h ^= (uint)Mathf.RoundToInt(Mathf.Clamp01(col.g) * 255f); h *= 16777619u;
                    h ^= (uint)Mathf.RoundToInt(Mathf.Clamp01(col.b) * 255f); h *= 16777619u;
                }
            }
            return h.ToString("X8");
        }
    }

    // FNV-1a over topology + quantised bounds of every LOD0 mesh, in order. Cheap enough to run per prototype
    // at boot, and sensitive to exactly what changes a silhouette: vertex/triangle counts and overall extent.
    // Quantised to 1e-3 m so float noise in an unchanged generator cannot invalidate a good atlas.
    public static string ShapeHash(IReadOnlyList<Mesh> meshes)
    {
        unchecked
        {
            uint h = 2166136261u;
            void Mix(int v) { h ^= (uint)v; h *= 16777619u; }
            void MixF(float f) { Mix(Mathf.RoundToInt(f * 1000f)); }

            if (meshes != null)
            {
                for (int i = 0; i < meshes.Count; i++)
                {
                    Mesh mesh = meshes[i];
                    if (mesh == null) { Mix(-1); continue; }
                    Mix(mesh.vertexCount);
                    Mix((int)mesh.GetIndexCount(0));
                    Bounds b = mesh.bounds;
                    MixF(b.center.x); MixF(b.center.y); MixF(b.center.z);
                    MixF(b.size.x); MixF(b.size.y); MixF(b.size.z);
                }
            }
            return h.ToString("X8");
        }
    }
}
