using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Draws a stump at each Stump-state harvest record (Inc 1 of the tree cut-set — see plans/005). The stump is
// a PLACEHOLDER (a short generated cylinder in a planet-aware lit material), oriented radial-up and sitting on
// the ground at the felled tree's spot. Per-prototype authored stump meshes replace the placeholder in Inc 2.
// One RenderMesh per stump — fine for POC counts; instance if a felled forest ever needs it.
public sealed class StumpRenderer : System.IDisposable
{
    static readonly Vector3 StumpScale = new Vector3(0.6f, 0.4f, 0.6f); // short + wide

    readonly ScatterHarvestStore _store;
    readonly Transform _planetTransform;
    readonly List<ScatterHarvestStore.HarvestNode> _stumps = new();
    Mesh _mesh;
    Material _material;
    RenderParams _rp;
    bool _ready;

    public StumpRenderer(ScatterHarvestStore store, Transform planetTransform)
    {
        _store = store;
        _planetTransform = planetTransform;

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _mesh = temp.GetComponent<MeshFilter>().sharedMesh; // built-in cylinder mesh; survives the GO destroy
        Collider col = temp.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        if (Application.isPlaying) Object.Destroy(temp); else Object.DestroyImmediate(temp);

        Shader shader = Shader.Find("Planet/PropLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null || _mesh == null) return;
        _material = new Material(shader) { name = "Stump (placeholder)", hideFlags = HideFlags.HideAndDontSave };
        if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", new Color(0.34f, 0.22f, 0.12f));
        _rp = new RenderParams(_material)
        {
            worldBounds = new Bounds(_planetTransform.position, Vector3.one * 100000f),
            shadowCastingMode = ShadowCastingMode.On,
            receiveShadows = true,
        };
        _ready = true;
    }

    public void Render(Camera camera)
    {
        if (!_ready || _store == null || camera == null) return;
        _store.CollectStumps(_stumps);
        if (_stumps.Count == 0) return;

        Vector3 center = _planetTransform.position;
        for (int i = 0; i < _stumps.Count; i++)
        {
            Vector3 basePos = _stumps[i].Position;
            Vector3 up = basePos - center;
            up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, up);
            // Record position is the felled tree's ground point; lift the centred cylinder by its half-height
            // so the stump sits on the ground instead of half-sinking.
            Vector3 pos = basePos + up * StumpScale.y;
            Graphics.RenderMesh(_rp, _mesh, 0, Matrix4x4.TRS(pos, rot, StumpScale));
        }
    }

    public void Dispose()
    {
        if (_material != null)
        {
            if (Application.isPlaying) Object.Destroy(_material);
            else Object.DestroyImmediate(_material);
            _material = null;
        }
        _ready = false;
    }
}
