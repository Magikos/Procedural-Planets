using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class HumanSkinAuthor
{
    // Reviewed against both source packs; other palettes need verification.
    static readonly Color32 SourceSkin = new Color32(255, 204, 174, 255);

    public static void Apply(GameObject target, GameObject source)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(HumanTrialAuthor.Folder + "/Human.mat");
        if (material == null) throw new InvalidOperationException("The human skin material is required.");
        var texture = new Texture2D(2, 2);
        try
        {
            var parts = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(r => r.name);
            foreach (var skin in target.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name.StartsWith("SOURCE / ")))
            {
                if (!parts.TryGetValue(skin.name.Substring("SOURCE / ".Length), out var original))
                    throw new InvalidOperationException("Missing source part for skin palette: " + skin.name);
                if (original.sharedMaterials.Length != 1 || original.sharedMesh.subMeshCount != 1 || original.sharedMesh.vertexCount != skin.sharedMesh.vertexCount)
                    throw new InvalidOperationException("Skin palette conversion requires matching single-material source topology: " + skin.name);
                var path = AssetDatabase.GetAssetPath(original.sharedMaterial.GetTexture("_BaseMap"));
                if (!File.Exists(path) || !texture.LoadImage(File.ReadAllBytes(path)))
                    throw new InvalidOperationException("Cannot read source palette: " + path);
                var uv = original.sharedMesh.uv;
                var isSkin = uv.Select(u => ((Color32)texture.GetPixel(
                    Mathf.Clamp((int)(u.x * texture.width), 0, texture.width - 1),
                    Mathf.Clamp((int)(u.y * texture.height), 0, texture.height - 1))).Equals(SourceSkin)).ToArray();
                var clothing = new List<int>(); var body = new List<int>();
                var triangles = original.sharedMesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    bool first = isSkin[triangles[i]];
                    if (first != isSkin[triangles[i + 1]] || first != isSkin[triangles[i + 2]])
                        throw new InvalidOperationException("A triangle crosses the skin palette boundary: " + skin.name);
                    var indices = first ? body : clothing;
                    indices.Add(triangles[i]); indices.Add(triangles[i + 1]); indices.Add(triangles[i + 2]);
                }
                // Use the native palette texel, so both skins follow the same colour-space path.
                for (int i = 0; i < uv.Length; i++) if (isSkin[i]) uv[i] = new Vector2(.5f / 32, 10.5f / 32);
                var mesh = skin.sharedMesh;
                mesh.uv = uv; mesh.subMeshCount = body.Count == 0 ? 1 : 2;
                mesh.SetTriangles(clothing, 0);
                if (body.Count > 0) mesh.SetTriangles(body, 1);
                skin.sharedMaterials = body.Count == 0 ? original.sharedMaterials : new[] { original.sharedMaterial, material };
                mesh.UploadMeshData(false); EditorUtility.SetDirty(mesh);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }
}
