using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Spawns every ScatterLibrary prototype (all parts + LOD chains) in a labelled grid so the exact runtime
// meshes/materials/LODs can be inspected and fixed in isolation, then applied to the planet. Reads the
// same ScatterLibrary the planet does, so a fix seen here is a fix everywhere. Editor-friendly: rebuild
// via the context menu or on play.
[ExecuteAlways]
public sealed class AssetShowcaseSpawner : MonoBehaviour
{
    public ScatterLibrary Library;
    public float Spacing = 9f;
    public int Columns = 8;
    [Tooltip("Spawn at the prototype's mid ScaleRange so sizes match the planet.")]
    public bool UseMidScale = true;
    [Tooltip("Only the first LOD (no LODGroup) — useful to isolate LOD0 issues.")]
    public bool Lod0Only = false;

    void Start()
    {
        if (Application.isPlaying) Rebuild();
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        if (Library == null) Library = Resources.Load<ScatterLibrary>("Settings/ScatterLibrary");
        if (Library == null) { Debug.LogError("AssetShowcaseSpawner: no ScatterLibrary found"); return; }

        var protos = Library.Prototypes;
        for (int i = 0; i < protos.Length; i++)
        {
            var proto = protos[i];
            if (proto == null) continue;
            int r = i / Columns, c = i % Columns;

            var cell = new GameObject($"[{i}] {proto.DisplayName}");
            cell.transform.SetParent(transform, false);
            cell.transform.localPosition = new Vector3(c * Spacing, 0f, -r * Spacing);
            float s = UseMidScale ? Mathf.Lerp(proto.ScaleRange.x, proto.ScaleRange.y, 0.5f) : 1f;
            cell.transform.localScale = Vector3.one * Mathf.Max(0.01f, s);

            if (proto.Parts != null && proto.Parts.Length > 0)
                foreach (var p in proto.Parts) SpawnPart(p.Name, p.Material, p.LodMeshes, p.CastShadows, p.ReceiveShadows, cell.transform);
            else
                SpawnPart("mesh", proto.Material, proto.LodMeshes, proto.CastShadows, proto.ReceiveShadows, cell.transform);

            AddLabel(cell.transform, proto.DisplayName);
        }
    }

    void SpawnPart(string name, Material mat, Mesh[] lods, bool cast, bool receive, Transform parent)
    {
        if (mat == null || lods == null || lods.Length == 0 || lods[0] == null) return;

        var group = new GameObject(name);
        group.transform.SetParent(parent, false);

        int n = Lod0Only ? 1 : lods.Length;
        var lodList = new List<LOD>();
        for (int i = 0; i < n; i++)
        {
            if (lods[i] == null) continue;
            var go = new GameObject($"{name}_LOD{i}");
            go.transform.SetParent(group.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = lods[i];
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = receive;
            float threshold = 1f - (i + 1f) / (n + 1f); // descending screen-relative sizes
            lodList.Add(new LOD(threshold, new Renderer[] { mr }));
        }
        if (Lod0Only || lodList.Count <= 1) return; // single mesh: no LODGroup

        var lodGroup = group.AddComponent<LODGroup>();
        lodGroup.SetLODs(lodList.ToArray());
        lodGroup.RecalculateBounds();
    }

    void AddLabel(Transform parent, string text)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, -0.4f, 0f);
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.characterSize = 0.12f;
        tm.fontSize = 64;
        tm.anchor = TextAnchor.UpperCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.black;
    }
}
