using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Draws each fallen-log record (plan 005 Inc 4) as a PLACEHOLDER log — a brown elongated cylinder lying at the
// settled rotation. The live tree mesh splays when flat (foliage cards + a branchy upper trunk), so a clean
// placeholder stands in until the tree polish pass generates a proper per-tree fallen-log mesh. The log is
// persistent (from records) and will be harvestable for wood.
public sealed class LogRenderer : System.IDisposable
{
    static readonly Vector3 LogScale = new Vector3(0.35f, 1.4f, 0.35f); // long along the cylinder axis (Y), thin

    readonly ScatterHarvestStore _store;
    readonly Transform _planetTransform;
    readonly List<ScatterHarvestStore.LogRecord> _logs = new();
    Mesh _mesh;
    Material _material;
    RenderParams _rp;
    bool _ready;

    public LogRenderer(ScatterHarvestStore store, Transform planetTransform)
    {
        _store = store;
        _planetTransform = planetTransform;

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _mesh = temp.GetComponent<MeshFilter>().sharedMesh; // built-in; survives the GO destroy
        Collider col = temp.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        if (Application.isPlaying) Object.Destroy(temp); else Object.DestroyImmediate(temp);

        Shader shader = Shader.Find("Planet/PropLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null || _mesh == null) return;
        _material = new Material(shader) { name = "Log placeholder", hideFlags = HideFlags.HideAndDontSave };
        if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", new Color(0.30f, 0.19f, 0.10f));
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
        _store.CollectLogs(_logs);
        for (int i = 0; i < _logs.Count; i++)
        {
            ScatterHarvestStore.LogRecord log = _logs[i];
            float scale = ScatterHarvestStore.StoredScaleOr(log.Scale);
            // The cylinder is centre-pivoted; shift it half a length along its (rotated) axis so the log lies
            // from the felled tree's base outward instead of half-sinking into the stump.
            Vector3 axis = log.Rotation * Vector3.up;
            Vector3 pos = log.Position + axis * (LogScale.y * scale);
            Graphics.RenderMesh(_rp, _mesh, 0, Matrix4x4.TRS(pos, log.Rotation, LogScale * scale));
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
