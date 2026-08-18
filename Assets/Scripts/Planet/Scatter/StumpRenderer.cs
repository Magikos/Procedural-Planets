using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Draws a stump at each Stump-state harvest record (tree cut-set — see plans/005). Uses the felled tree's
// authored per-prototype StumpMesh + StumpMaterial (keyed by the record's protoIndex) so a chopped birch
// leaves a birch stump; falls back to a placeholder cylinder for any prototype without an authored stump yet.
// One RenderMesh per stump — fine for POC counts; instance if a felled forest ever needs it.
//
// ponytail: the harvest record stores position + proto only, so a stump draws at the tree mesh's
// native scale and yaw rather than the felled instance's. Ceiling is a visible size/rotation
// mismatch; add scale + yaw to the record when that shows.
public sealed class StumpRenderer : System.IDisposable
{
    static readonly Vector3 PlaceholderScale = new Vector3(0.6f, 0.4f, 0.6f);

    readonly ScatterHarvestStore _store;
    readonly Transform _planetTransform;
    readonly System.Func<ScatterLibraryDto> _libraryFn;
    readonly List<ScatterHarvestStore.HarvestNode> _stumps = new();
    readonly Dictionary<Material, RenderParams> _rpCache = new();

    Mesh _placeholderMesh;
    Material _placeholderMaterial;
    bool _ready;

    public StumpRenderer(ScatterHarvestStore store, Transform planetTransform, System.Func<ScatterLibraryDto> libraryFn)
    {
        _store = store;
        _planetTransform = planetTransform;
        _libraryFn = libraryFn;

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _placeholderMesh = temp.GetComponent<MeshFilter>().sharedMesh; // built-in; survives the GO destroy
        Collider col = temp.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        if (Application.isPlaying) Object.Destroy(temp); else Object.DestroyImmediate(temp);

        Shader shader = Shader.Find("Planet/PropLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null || _placeholderMesh == null) return;
        _placeholderMaterial = new Material(shader) { name = "Stump placeholder", hideFlags = HideFlags.HideAndDontSave };
        if (_placeholderMaterial.HasProperty("_BaseColor"))
            _placeholderMaterial.SetColor("_BaseColor", new Color(0.34f, 0.22f, 0.12f));
        _ready = true;
    }

    RenderParams Rp(Material m)
    {
        if (!_rpCache.TryGetValue(m, out RenderParams rp))
        {
            rp = new RenderParams(m)
            {
                worldBounds = new Bounds(_planetTransform.position, Vector3.one * 100000f),
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
            };
            _rpCache[m] = rp;
        }
        return rp;
    }

    public void Render(Camera camera)
    {
        if (!_ready || _store == null || camera == null) return;
        _store.CollectStumps(_stumps);
        if (_stumps.Count == 0) return;

        ScatterLibraryDto library = _libraryFn?.Invoke();
        Vector3 center = _planetTransform.position;

        for (int i = 0; i < _stumps.Count; i++)
        {
            ScatterHarvestStore.HarvestNode node = _stumps[i];
            Mesh mesh = _placeholderMesh;
            Material mat = _placeholderMaterial;
            bool authored = false;
            if (library?.Prototypes != null && (uint)node.ProtoIndex < (uint)library.Prototypes.Length)
            {
                ScatterPrototypeDto proto = library.Prototypes[node.ProtoIndex];
                if (proto.StumpMesh != null)
                {
                    mesh = proto.StumpMesh;
                    mat = proto.StumpMaterial ?? proto.TrunkMaterial ?? _placeholderMaterial;
                    authored = true;
                }
            }
            if (mat == null) mat = _placeholderMaterial;

            Vector3 up = node.Position - center;
            up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, up);
            // Authored stump pivot is the tree base (mesh origin) -> sits at the record position, native scale.
            // Placeholder cylinder is centre-pivoted -> lift by its half-height and use the placeholder scale.
            Vector3 pos = authored ? node.Position : node.Position + up * PlaceholderScale.y;
            Vector3 scale = authored ? Vector3.one : PlaceholderScale;
            Graphics.RenderMesh(Rp(mat), mesh, 0, Matrix4x4.TRS(pos, rot, scale));
        }
    }

    public void Dispose()
    {
        if (_placeholderMaterial != null)
        {
            if (Application.isPlaying) Object.Destroy(_placeholderMaterial);
            else Object.DestroyImmediate(_placeholderMaterial);
            _placeholderMaterial = null;
        }
        _rpCache.Clear();
        _ready = false;
    }
}
