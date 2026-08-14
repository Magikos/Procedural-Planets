using UnityEngine;

// Dev preview for the tree generator (plan 006): `tree.gen` grows a tree from a sample TreeDef + seed and spawns
// it (bark + foliage) in front of the camera so shapes can be eyeballed in play. `tree.age` sets the age stage
// (sapling..old). Console-only. Registered by Planet. Visual params (leaf/bark colors, sizes) are first guesses.
[CommandPrefix("tree")]
public sealed class TreePreview : System.IDisposable
{
    readonly Transform _planetTransform;
    float _age = 1f;
    GameObject _last;
    Material _barkMat;
    Material _foliageMat;

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
        GeneratedTree tree = TreeGenerator.Generate(def, s);

        if (_last != null) Object.Destroy(_last);
        Vector3 pos = cam.transform.position + cam.transform.forward * 8f;
        Vector3 up = _planetTransform != null ? (pos - _planetTransform.position) : Vector3.up;
        up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;

        _last = new GameObject($"PreviewTree({s})");
        _last.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, up));
        AddChild("bark", tree.Bark, BarkMat());
        AddChild("foliage", tree.Foliage, FoliageMat());

        return $"tree: '{def.Name}' age {_age:F2} seed {s} — H {tree.Height:F1}m, HP {tree.ChopHp}, wood {tree.WoodYield}; " +
               $"bark {(tree.Bark ? tree.Bark.vertexCount : 0)}v, foliage {(tree.Foliage ? tree.Foliage.vertexCount : 0)}v, " +
               $"stump {(tree.Stump ? tree.Stump.vertexCount : 0)}v, log {(tree.Log ? tree.Log.vertexCount : 0)}v";
    }

    [ConsoleCommand("age", "Set preview tree age 0..1 (sapling..old), then re-run tree.gen.", MonoTargetType.Registry)]
    string AgeCmd(float age)
    {
        _age = Mathf.Clamp01(age);
        return $"tree age = {_age:F2} (0=sapling, 1=old). Run tree.gen to see it.";
    }

    void AddChild(string name, Mesh mesh, Material mat)
    {
        if (mesh == null) return;
        var go = new GameObject(name);
        go.transform.SetParent(_last.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    Material BarkMat()
    {
        if (_barkMat == null) _barkMat = MakeMat("Tree bark", new Color(0.35f, 0.24f, 0.14f));
        return _barkMat;
    }

    Material FoliageMat()
    {
        if (_foliageMat == null) _foliageMat = MakeMat("Tree foliage", new Color(0.24f, 0.44f, 0.16f));
        return _foliageMat;
    }

    static Material MakeMat(string name, Color color)
    {
        Shader sh = Shader.Find("Planet/PropLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh) { name = name, hideFlags = HideFlags.HideAndDontSave };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        return m;
    }

    public void Dispose()
    {
        ConsoleRegistry.UnregisterInstance(typeof(TreePreview));
        if (_last != null) Object.Destroy(_last);
        if (_barkMat != null) Object.Destroy(_barkMat);
        if (_foliageMat != null) Object.Destroy(_foliageMat);
    }
}
