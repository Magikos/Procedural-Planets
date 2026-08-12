using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Editor QA tool: lay out every scatter prototype's mesh LODs in a labelled grid so bad assets/LODs (wrong
// normals -> black, missing material, broken decimation) are easy to spot side by side. One row per
// prototype; one column per LOD level (LOD0 nearest ... last LOD farthest); every part of the prototype is
// co-located per cell (so a tree shows trunk + foliage together). A TextMesh labels each row and each column
// header. View in PLAY mode so foliage/prop shaders light correctly (they read the runtime planet sun; in
// edit mode they render dark). Re-runnable; "Clear" removes the gallery.
public static class ScatterLodGalleryTool
{
    const string RootName = "ScatterLodGallery";
    const float ColSpacing = 16f;   // metres between LOD columns
    const float RowSpacing = 14f;   // metres between prototype rows
    const float LabelX = -10f;

    [MenuItem("Tools/ProceduralPlanets/Build Scatter LOD Gallery")]
    public static void Build()
    {
        Clear();
        var root = new GameObject(RootName);

        string[] guids = AssetDatabase.FindAssets("t:ScatterPrototype");
        var protos = new List<ScatterPrototype>();
        foreach (string g in guids)
        {
            var p = AssetDatabase.LoadAssetAtPath<ScatterPrototype>(AssetDatabase.GUIDToAssetPath(g));
            if (p != null) protos.Add(p);
        }
        protos.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        int maxLods = 0;
        foreach (var p in protos) maxLods = Mathf.Max(maxLods, MaxLodCount(p));
        for (int c = 0; c < maxLods; c++)
            MakeLabel(root.transform, $"LOD{c}", new Vector3(c * ColSpacing, 6f, -RowSpacing), 3f);

        int row = 0, built = 0;
        foreach (var p in protos)
        {
            int lods = MaxLodCount(p);
            if (lods == 0) continue; // placement-only prototype, nothing to show
            float z = row * RowSpacing;
            MakeLabel(root.transform, p.name, new Vector3(LabelX, 2f, z), 2f);

            for (int c = 0; c < lods; c++)
            {
                var cell = new GameObject($"{p.name} LOD{c}");
                cell.transform.SetParent(root.transform, false);
                cell.transform.position = new Vector3(c * ColSpacing, 0f, z);
                foreach (ScatterPart part in Parts(p))
                {
                    if (part.Material == null || part.LodMeshes == null || part.LodMeshes.Length == 0) continue;
                    Mesh mesh = part.LodMeshes[Mathf.Min(c, part.LodMeshes.Length - 1)];
                    if (mesh == null) continue;
                    var go = new GameObject(part.Name ?? "part");
                    go.transform.SetParent(cell.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = part.Material;
                }
            }
            row++; built++;
        }

        Selection.activeGameObject = root;
        Debug.Log($"[LOD Gallery] built {built} prototypes x up to {maxLods} LOD columns under '{RootName}'. " +
                  "Enter Play mode to light them correctly. Impostors are not shown (billboards); use the far-distance in-game view for those.");
    }

    [MenuItem("Tools/ProceduralPlanets/Clear Scatter LOD Gallery")]
    public static void Clear()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null) Object.DestroyImmediate(existing);
    }

    static IEnumerable<ScatterPart> Parts(ScatterPrototype p)
    {
        if (p.Parts != null && p.Parts.Length > 0)
        {
            foreach (var part in p.Parts) if (part != null) yield return part;
        }
        else if (p.Material != null && p.LodMeshes != null && p.LodMeshes.Length > 0)
        {
            yield return new ScatterPart { Name = "part", Material = p.Material, LodMeshes = p.LodMeshes, LodEndDistances = p.LodEndDistances };
        }
    }

    static int MaxLodCount(ScatterPrototype p)
    {
        int n = 0;
        foreach (var part in Parts(p))
            if (part.LodMeshes != null) n = Mathf.Max(n, part.LodMeshes.Length);
        return n;
    }

    static void MakeLabel(Transform parent, string text, Vector3 pos, float size)
    {
        var go = new GameObject($"label:{text}");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.characterSize = size;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.MiddleLeft;
        tm.color = Color.white;
    }
}
