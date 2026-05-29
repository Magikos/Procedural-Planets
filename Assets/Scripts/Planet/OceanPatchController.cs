using UnityEngine;

/// <summary>
/// Near-camera water patch with real Gerstner wave geometry. A dense flat grid is created once,
/// then re-positioned under the main camera on the sea-level sphere each frame; the OceanPatch
/// shader projects it onto the sphere and displaces it. Basin clipping is handled by depth testing
/// (dry land occludes the sea-level patch), so this needs no explicit shoreline masking.
///
/// The far ocean (Planet's water mesh + Ocean.shader) still renders everywhere; this patch overlays
/// real wave geometry in the near field. Seamless blending with the far ocean is a later milestone.
/// </summary>
[DisallowMultipleComponent]
public class OceanPatchController : MonoBehaviour
{
    [Header("Patch")]
    [Tooltip("World size (meters) of the patch at the reference planet radius (5000).")]
    public float PatchSize = 1200f;
    [Range(16, 254)] public int Resolution = 192;
    [Tooltip("Above this altitude (meters above sea level) the patch is hidden — far ocean takes over.")]
    public float MaxCameraAltitude = 1400f;

    const float ReferencePlanetRadius = 5000f;

    static readonly int _patchCenterId = Shader.PropertyToID("_OceanPatchCenter");
    static readonly int _patchSeaRadiusId = Shader.PropertyToID("_OceanPatchSeaRadius");
    static readonly int _waveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
    static readonly int _waveLengthId = Shader.PropertyToID("_WaveLength");

    Vector3 _planetCenter;
    float _seaLevelRadius;
    bool _hasPlanet;
    float _scale = 1f;

    MeshRenderer _renderer;
    Material _material;
    Transform _patch;
    static Shader _patchShader;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateInstance()
    {
        if (FindAnyObjectByType<OceanPatchController>() != null) return;
        var go = new GameObject("[OceanPatch]");
        go.AddComponent<OceanPatchController>();
    }

    void Awake()
    {
        if (_patchShader == null) _patchShader = Shader.Find("Planet/OceanPatch");
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable() => EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    void OnDisable() => EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);

    void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetCenter = evt.PlanetCenter;
        _seaLevelRadius = evt.SeaLevelRadius > 0f ? evt.SeaLevelRadius : evt.PlanetRadius * 0.95f;
        _scale = Mathf.Max(_seaLevelRadius / ReferencePlanetRadius, 0.0001f);
        _hasPlanet = _seaLevelRadius > 0f;

        EnsurePatch();
    }

    void EnsurePatch()
    {
        if (_patch == null)
        {
            var go = new GameObject("OceanPatchMesh");
            go.transform.SetParent(transform, false);
            _patch = go.transform;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildGrid(Resolution);

            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            if (_patchShader == null) _patchShader = Shader.Find("Planet/OceanPatch");
            _material = new Material(_patchShader) { name = "OceanPatch" };
            _renderer.sharedMaterial = _material;
        }

        // Wave scale follows planet size. Tuned for visible near-camera chop (not distant swell):
        // ~5 m crests on a ~90 m base wavelength reads clearly from sea level.
        _material.SetFloat(_waveAmplitudeId, 5.0f * _scale);
        _material.SetFloat(_waveLengthId, 90f * _scale);
    }

    void LateUpdate()
    {
        if (!_hasPlanet || _patch == null) return;

        var cam = Camera.main;
        if (cam == null)
        {
            _renderer.enabled = false;
            return;
        }

        Vector3 camPos = cam.transform.position;
        Vector3 toCam = camPos - _planetCenter;
        float camRadius = toCam.magnitude;
        if (camRadius < 0.0001f)
        {
            _renderer.enabled = false;
            return;
        }

        float altitude = camRadius - _seaLevelRadius;
        if (altitude > MaxCameraAltitude * _scale)
        {
            _renderer.enabled = false;
            return;
        }
        _renderer.enabled = true;

        // Tangent frame at the sea-level point beneath the camera. The patch follows the camera;
        // wave phase is world-space in the shader, so the wave pattern stays world-locked (no swimming).
        Vector3 up = toCam / camRadius;
        Vector3 reference = Mathf.Abs(up.y) < 0.92f ? Vector3.up : Vector3.right;
        Vector3 tangentB = Vector3.Cross(up, Vector3.Normalize(Vector3.Cross(reference, up)));

        float patchSize = PatchSize * _scale;
        _patch.position = _planetCenter + up * _seaLevelRadius;
        _patch.rotation = Quaternion.LookRotation(tangentB, up); // object Y → planet up, object XZ → tangent plane
        _patch.localScale = new Vector3(patchSize, 1f, patchSize);

        _material.SetVector(_patchCenterId, _planetCenter);
        _material.SetFloat(_patchSeaRadiusId, _seaLevelRadius);
    }

    // Flat unit grid in the XZ plane, centered at origin, extent [-0.5, 0.5] (scaled by transform).
    static Mesh BuildGrid(int resolution)
    {
        resolution = Mathf.Clamp(resolution, 2, 254);
        int verts = resolution * resolution;
        var vertices = new Vector3[verts];
        var normals = new Vector3[verts];
        var triangles = new int[(resolution - 1) * (resolution - 1) * 6];

        int t = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = x + z * resolution;
                float fx = x / (float)(resolution - 1) - 0.5f;
                float fz = z / (float)(resolution - 1) - 0.5f;
                vertices[i] = new Vector3(fx, 0f, fz);
                normals[i] = Vector3.up;

                if (x < resolution - 1 && z < resolution - 1)
                {
                    triangles[t++] = i;
                    triangles[t++] = i + resolution;
                    triangles[t++] = i + resolution + 1;
                    triangles[t++] = i;
                    triangles[t++] = i + resolution + 1;
                    triangles[t++] = i + 1;
                }
            }
        }

        var mesh = new Mesh { name = "OceanPatchGrid" };
        mesh.indexFormat = verts > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;
        // Large bounds so frustum culling never hides the camera-following patch.
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        return mesh;
    }
}
