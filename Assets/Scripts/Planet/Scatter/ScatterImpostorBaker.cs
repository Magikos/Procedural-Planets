using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Bakes a scatter prototype's near mesh into one front-view billboard card (RGB = lit colour,
// A = silhouette) for the far-field impostor tier. A URP camera clears a manual render-to-texture to
// opaque black and ignores backgroundColor, so the silhouette is keyed off luminance: the background
// is reliably pure black, and a modest ambient floor keeps even shadowed geometry well above the key
// threshold. Runtime-capable: the LOD strip bakes on load; the planet bakes the same way at build.
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

    public static Card Bake(IReadOnlyList<Mesh> meshes, IReadOnlyList<Material> materials, Vector3 lightEuler)
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
        var lightGO = new GameObject("l");
        lightGO.transform.SetParent(root.transform, false);
        Light light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        lightGO.transform.rotation = Quaternion.Euler(lightEuler);

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

        AmbientMode savedMode = RenderSettings.ambientMode;
        Color savedAmbient = RenderSettings.ambientLight;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.32f, 0.33f, 0.35f);

        var rt = new RenderTexture(px, CardHeightPx, 16, RenderTextureFormat.ARGB32);
        Texture2D lit = RenderTo(cam, rt, Color.black);

        RenderSettings.ambientMode = savedMode;
        RenderSettings.ambientLight = savedAmbient;

        var card = new Texture2D(px, CardHeightPx, TextureFormat.ARGB32, false);
        Color[] lp = lit.GetPixels();
        var outPx = new Color[lp.Length];
        for (int i = 0; i < lp.Length; i++)
        {
            float lum = lp[i].r * 0.299f + lp[i].g * 0.587f + lp[i].b * 0.114f;
            float t = Mathf.Clamp01((lum - 0.012f) / (0.05f - 0.012f));
            float a = t * t * (3f - 2f * t); // pure-black bg -> 0, lit geometry -> 1 (real smoothstep)
            outPx[i] = new Color(lp[i].r, lp[i].g, lp[i].b, a);
        }
        card.SetPixels(outPx);
        card.Apply();

        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(lit);
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
