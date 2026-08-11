using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Bakes a scatter prototype's near mesh into one front-view billboard card (RGB = UNLIT albedo,
// A = silhouette) for the far-field impostor tier. Lighting is applied at runtime in the impostor
// shader from the same main light + ambient the mesh uses, so impostors track the day/night sun instead
// of freezing a bake-time light. The card is baked flat (white ambient, no directional) against a pure
// black background, so the silhouette is keyed off luminance. That black background depends on the bake
// camera being a Preview camera (below): the sky/atmosphere/cloud render features all skip Preview, so
// none of them paint sky into the background. Runtime-capable: the LOD strip bakes on load; the planet
// bakes the same way at build.
public static class ScatterImpostorBaker
{
    public struct Card
    {
        public Texture2D Texture;
        public float Width;  // world metres — the billboard quad width
        public float Height; // world metres
        public bool Valid;   // false when the bake produced almost no silhouette (see MinSilhouetteAlpha)
    }

    // Octahedral impostor atlas: the prototype baked from a GridN×GridN grid of hemisphere camera angles
    // (hemi-octahedral layout) into one square texture. At runtime the camera-facing quad picks the cell
    // matching the view direction, so the card reads as the tree from ANY angle — including straight down —
    // instead of a single front-view billboard that foreshortens to a slab from above.
    public struct AtlasCard
    {
        public Texture2D Texture;   // (GridN*AtlasCellPx) square hemi-octahedral atlas, cell (i,j) at (i,j)*cellPx
        public float WorldSize;     // square billboard side in world metres (max of footprint width / height)
        public float CenterOffset;  // billboard centre height above the instance pivot (base) in world metres
        public int GridN;           // frames per axis
        public bool Valid;          // false when the bake keyed almost no silhouette (see MinSilhouetteAlpha)
    }

    const int CardHeightPx = 256;
    const int AtlasCellPx = 128;  // per-angle cell resolution in the octahedral atlas
    const int BakeLayer = 31; // isolate the bake rig from the rest of the scene

    // A bake that keys almost no coverage (thin _ForceLeaf blades like reeds, or a prototype whose front
    // view is nearly empty) yields an invisible card. Callers should skip the impostor tier for these and
    // let the prototype hard-cull at its mesh range instead of drawing nothing.
    const float MinSilhouetteAlpha = 0.2f;

    public static Card Bake(IReadOnlyList<Mesh> meshes, IReadOnlyList<Material> materials)
    {
        Bounds b = meshes[0].bounds;
        for (int i = 1; i < meshes.Count; i++) b.Encapsulate(meshes[i].bounds);
        float w = Mathf.Max(b.size.x, b.size.z);
        float h = Mathf.Max(b.size.y, 1e-3f);
        Vector3 ctr = b.center;
        int px = Mathf.Max(8, Mathf.RoundToInt(CardHeightPx * w / h));

        var root = new GameObject("ImpostorBakeRig") { hideFlags = HideFlags.HideAndDontSave };
        for (int i = 0; i < meshes.Count; i++)
        {
            var g = new GameObject("m") { layer = BakeLayer };
            g.transform.SetParent(root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = meshes[i];
            g.AddComponent<MeshRenderer>().sharedMaterial = materials[i];
        }
        var camGO = new GameObject("c");
        camGO.transform.SetParent(root.transform, false);
        Camera cam = camGO.AddComponent<Camera>();
        // Preview camera type: every sky/atmosphere/cloud/star/scatter render feature skips Preview and
        // Reflection cameras, so none of them paint the sky into the bake's background. Without this the
        // atmosphere feature fills the background with sky blue (opaque), the luminance key reads that as
        // geometry, and the whole card bakes as an opaque blue box instead of a clean tree silhouette.
        cam.cameraType = CameraType.Preview;
        cam.orthographic = true;
        cam.orthographicSize = h * 0.52f;
        cam.aspect = (float)px / CardHeightPx;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.cullingMask = 1 << BakeLayer;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = Mathf.Max(w, h) * 8f;
        camGO.transform.position = ctr + new Vector3(0f, 0f, -Mathf.Max(w, h) * 2f);
        camGO.transform.LookAt(ctr);

        // Flat white ambient + no directional light -> the mesh renders as ~unlit albedo (runtime
        // lights it). Against the pure-black background this keeps the luminance key clean.
        AmbientMode savedMode = RenderSettings.ambientMode;
        Color savedAmbient = RenderSettings.ambientLight;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.white;

        var rt = new RenderTexture(px, CardHeightPx, 16, RenderTextureFormat.ARGB32);
        Texture2D albedo = RenderTo(cam, rt, Color.black);

        RenderSettings.ambientMode = savedMode;
        RenderSettings.ambientLight = savedAmbient;

        var card = new Texture2D(px, CardHeightPx, TextureFormat.ARGB32, false);
        Color[] ap = albedo.GetPixels();
        var outPx = new Color[ap.Length];
        float maxAlpha = 0f;
        for (int i = 0; i < ap.Length; i++)
        {
            float lum = ap[i].r * 0.299f + ap[i].g * 0.587f + ap[i].b * 0.114f;
            float t = Mathf.Clamp01((lum - 0.012f) / (0.05f - 0.012f));
            float a = t * t * (3f - 2f * t); // pure-black bg -> 0, geometry -> 1 (real smoothstep)
            if (a > maxAlpha) maxAlpha = a;
            outPx[i] = new Color(ap[i].r, ap[i].g, ap[i].b, a);
        }
        card.SetPixels(outPx);
        card.Apply();

        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(albedo);
        Object.DestroyImmediate(root);

        return new Card { Texture = card, Width = w, Height = h, Valid = maxAlpha >= MinSilhouetteAlpha };
    }

    // Bakes the prototype from a GridN×GridN hemisphere of angles into one octahedral atlas. Same unlit-
    // albedo + luminance-key silhouette as Bake, but square cells (framing the tree's max extent so it fits
    // from any angle) and one render per cell. The billboard is centred on the tree centre (CenterOffset)
    // and made camera-facing at runtime, so the atlas cell for the view direction always shows a real angle.
    public static AtlasCard BakeAtlas(IReadOnlyList<Mesh> meshes, IReadOnlyList<Material> materials, int gridN)
    {
        Bounds b = meshes[0].bounds;
        for (int i = 1; i < meshes.Count; i++) b.Encapsulate(meshes[i].bounds);
        float w = Mathf.Max(b.size.x, b.size.z);
        float h = Mathf.Max(b.size.y, 1e-3f);
        float s = Mathf.Max(w, h);   // square framing fits the tree from top-down (w wide) and side (h tall)
        Vector3 ctr = b.center;

        var root = new GameObject("ImpostorAtlasBakeRig") { hideFlags = HideFlags.HideAndDontSave };
        for (int i = 0; i < meshes.Count; i++)
        {
            var g = new GameObject("m") { layer = BakeLayer };
            g.transform.SetParent(root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = meshes[i];
            g.AddComponent<MeshRenderer>().sharedMaterial = materials[i];
        }
        var camGO = new GameObject("c");
        camGO.transform.SetParent(root.transform, false);
        Camera cam = camGO.AddComponent<Camera>();
        cam.cameraType = CameraType.Preview;   // black background (see Bake) — the luminance key depends on it
        cam.orthographic = true;
        cam.orthographicSize = s * 0.52f;
        cam.aspect = 1f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 1 << BakeLayer;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = s * 8f;

        AmbientMode savedMode = RenderSettings.ambientMode;
        Color savedAmbient = RenderSettings.ambientLight;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.white;

        int atlasPx = gridN * AtlasCellPx;
        var atlas = new Texture2D(atlasPx, atlasPx, TextureFormat.ARGB32, false);
        var cellRt = new RenderTexture(AtlasCellPx, AtlasCellPx, 16, RenderTextureFormat.ARGB32);
        float dist = s * 2f;
        float maxAlpha = 0f;
        for (int j = 0; j < gridN; j++)
        for (int i = 0; i < gridN; i++)
        {
            Vector3 dir = HemiOctDecode(new Vector2((i + 0.5f) / gridN, (j + 0.5f) / gridN)); // tree -> camera
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.forward, dir); // top-down pole
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right).normalized;
            camGO.transform.SetPositionAndRotation(ctr + dir * dist, Quaternion.LookRotation(-dir, up));

            cam.targetTexture = cellRt;
            cam.Render();
            RenderTexture.active = cellRt;
            var cell = new Texture2D(AtlasCellPx, AtlasCellPx, TextureFormat.ARGB32, false);
            cell.ReadPixels(new Rect(0, 0, AtlasCellPx, AtlasCellPx), 0, 0);
            cell.Apply();
            RenderTexture.active = null;

            Color[] cp = cell.GetPixels();
            for (int k = 0; k < cp.Length; k++)
            {
                float lum = cp[k].r * 0.299f + cp[k].g * 0.587f + cp[k].b * 0.114f;
                float t = Mathf.Clamp01((lum - 0.012f) / (0.05f - 0.012f));
                float a = t * t * (3f - 2f * t);
                if (a > maxAlpha) maxAlpha = a;
                cp[k] = new Color(cp[k].r, cp[k].g, cp[k].b, a);
            }
            atlas.SetPixels(i * AtlasCellPx, j * AtlasCellPx, AtlasCellPx, AtlasCellPx, cp);
            Object.DestroyImmediate(cell);
        }
        atlas.Apply();

        RenderSettings.ambientMode = savedMode;
        RenderSettings.ambientLight = savedAmbient;
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(cellRt);
        Object.DestroyImmediate(root);

        return new AtlasCard { Texture = atlas, WorldSize = s, CenterOffset = ctr.y, GridN = gridN, Valid = maxAlpha >= MinSilhouetteAlpha };
    }

    // Hemi-octahedral decode: square uv in [0,1]^2 -> unit direction on the upper hemisphere (y = up).
    // Cell centre uv -> the camera direction that cell was baked from; the runtime encode is its inverse.
    // uv=(0.5,0.5) -> straight up (top-down view); the four corners -> the horizon cardinal directions.
    static Vector3 HemiOctDecode(Vector2 f)
    {
        f = f * 2f - Vector2.one;
        Vector2 p = new Vector2(f.x + f.y, f.x - f.y) * 0.5f;
        float y = 1f - Mathf.Abs(p.x) - Mathf.Abs(p.y);
        return new Vector3(p.x, y, p.y).normalized;
    }

    static Texture2D RenderTo(Camera cam, RenderTexture rt, Color bg)
    {
        cam.backgroundColor = bg;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var t = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
        t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        t.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        return t;
    }
}
