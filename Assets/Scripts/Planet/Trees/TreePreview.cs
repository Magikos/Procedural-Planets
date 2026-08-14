using UnityEngine;

// Dev preview for the tree generator (plan 006 T1): `tree.gen` grows a tree from a sample TreeDef + seed, meshes
// its trunk/branches, and spawns it in front of the camera so shapes can be eyeballed in play. `tree.age` sets
// the age stage (sapling..old). Console-only, no leaves/cut-set yet. Registered by Planet.
[CommandPrefix("tree")]
public sealed class TreePreview : System.IDisposable
{
    readonly Transform _planetTransform;
    float _age = 1f;
    GameObject _last;
    Material _mat;

    public TreePreview(Transform planetTransform)
    {
        _planetTransform = planetTransform;
        ConsoleRegistry.RegisterInstance(this);
    }

    [ConsoleCommand("gen", "Generate a preview tree in front of the camera. Optional seed.", MonoTargetType.Registry)]
    string GenCmd(int? seed = null)
    {
        var cam = Camera.main;
        if (cam == null) return "tree: no main camera";

        TreeDef def = TreeDefLibrary.SampleBroadleaf(_age);
        int s = seed ?? Random.Range(1, 999999);
        TreeSkeleton sk = TreeStructureGenerator.Generate(def, s);
        Mesh mesh = TreeTubeMesher.Build(sk);

        if (_last != null) Object.Destroy(_last);
        Vector3 pos = cam.transform.position + cam.transform.forward * 8f;
        Vector3 up = _planetTransform != null ? (pos - _planetTransform.position) : Vector3.up;
        up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;

        _last = new GameObject($"PreviewTree({s})");
        _last.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, up));
        _last.AddComponent<MeshFilter>().sharedMesh = mesh;
        _last.AddComponent<MeshRenderer>().sharedMaterial = Mat();

        return $"tree: '{def.Name}' age {_age:F2} seed {s} — {sk.Branches.Count} branches, " +
               $"{sk.Sprouts.Count} leaf anchors, {mesh.vertexCount} verts, height {sk.Height:F1} m";
    }

    [ConsoleCommand("age", "Set preview tree age 0..1 (sapling..old), then re-run tree.gen.", MonoTargetType.Registry)]
    string AgeCmd(float age)
    {
        _age = Mathf.Clamp01(age);
        return $"tree age = {_age:F2} (0=sapling, 1=old). Run tree.gen to see it.";
    }

    Material Mat()
    {
        if (_mat == null)
        {
            Shader sh = Shader.Find("Planet/PropLit") ?? Shader.Find("Universal Render Pipeline/Lit");
            _mat = new Material(sh) { name = "Tree preview bark", hideFlags = HideFlags.HideAndDontSave };
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", new Color(0.35f, 0.24f, 0.14f));
        }
        return _mat;
    }

    public void Dispose()
    {
        ConsoleRegistry.UnregisterInstance(typeof(TreePreview));
        if (_last != null) Object.Destroy(_last);
        if (_mat != null) Object.Destroy(_mat);
    }
}
