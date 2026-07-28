using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Bakes a scatter prototype's near mesh into one front-view billboard card (RGB = UNLIT albedo,
// A = silhouette) for the far-field impostor tier. Lighting is applied at runtime in the impostor
// shader from the same main light + ambient the mesh uses, so impostors track the day/night sun instead
// of freezing a bake-time light. The card is therefore baked flat (white ambient, no directional): the
// background is reliably pure black (a URP camera clears a manual render-to-texture to opaque black and
// ignores backgroundColor), so the silhouette is keyed off luminance. Runtime-capable: the LOD strip
// bakes on load; the planet bakes the same way at build.
public static class ScatterImpostorBaker
{
    public struct Card
    {
        public Texture2D Texture;
        public float Width;  // world metres — the billboard quad width
        public float Height; // world metres
    }

    const int CardHeightPx = 256;
    const int BakeLayer = 31; // isolate the bake rig from the rest of the scene

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
        for (int i = 0; i < ap.Length; i++)
        {
            float lum = ap[i].r * 0.299f + ap[i].g * 0.587f + ap[i].b * 0.114f;
            float t = Mathf.Clamp01((lum - 0.012f) / (0.05f - 0.012f));
            float a = t * t * (3f - 2f * t); // pure-black bg -> 0, geometry -> 1 (real smoothstep)
            outPx[i] = new Color(ap[i].r, ap[i].g, ap[i].b, a);
        }
        card.SetPixels(outPx);
        card.Apply();

        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(albedo);
        Object.DestroyImmediate(root);

        return new Card { Texture = card, Width = w, Height = h };
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
