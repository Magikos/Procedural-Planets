using UnityEngine;
using UnityEngine.Rendering;

// Step 4 of the layered grass: sparse "hero" clumps (Synty low-poly grass meshes) scattered on
// the surface to break the uniform blade field, the cousins of future flowers/rocks/trees.
// Camera-centred like the near field: a jittered grid disc rebuilt only when the camera moves a
// cell, biome-approximated by climate moisture (proper GPU biome gating is a later upgrade), each
// clump aligned to the surface normal with a hashed yaw/scale. Rendered with GPU instancing.
public sealed class GrassClumpScatter
{
    const float DiscRadius = 34f;      // metres around the camera
    const float CellSpacing = 3.2f;    // metres between candidate clumps (sparse - hero layer)
    const float RebuildMoveThreshold = 2.0f;
    const float MoistureGate = 0.32f;  // grass grows where moist; skip dry/desert
    const float TargetHeight = 0.6f;   // metres - fit the clump to this regardless of native size
    const int MaxInstances = 1023;     // Graphics.RenderMeshInstanced hard cap per call

    readonly Transform _planetTransform;
    readonly ILogger _logger;
    readonly Mesh _mesh;
    readonly Material _material;
    readonly Matrix4x4[] _matrices = new Matrix4x4[MaxInstances];
    readonly RenderParams _renderParams;

    IPlanetSurfaceSampler _surface;
    IClimateSampler _climate;
    Vector3 _planetCenter;
    Vector3 _lastBuildPos = new Vector3(1e9f, 1e9f, 1e9f);
    float _baseScale = 0.5f;
    float _pivotLiftNative;   // native distance from the mesh pivot down to its base
    int _count;

    // Parked. Reconstructing Synty grass from code (RenderMeshInstanced + a hand-built URP/Lit
    // material) does not converge: mesh scale/pivot need the real mesh inspected, the look needs
    // Synty's own shader, and world-stable placement needs a sphere-stable (cube-face) frame, not a
    // camera-derived tangent. The clean path is to instance Synty's actual PREFAB (correct mesh +
    // material + scale) or set it up hands-on in the editor. See project-grass-layering-arc memory.
    public bool Enabled { get; set; } = false;

    public GrassClumpScatter(Transform planetTransform, Vector3 planetCenter, ILogger logger)
    {
        _planetTransform = planetTransform;
        _planetCenter = planetCenter;
        _logger = logger;

        _mesh = LoadMesh("Grass/SM_Env_Grass_Large_01");
        if (_mesh == null)
        {
            _logger?.Log(LogLevel.Info, "GrassClump", "Synty clump mesh not found under Resources/Grass; clump scatter disabled.");
            return;
        }

        // Fit the clump to a real height regardless of the FBX's native scale (Synty meshes can be
        // authored large / in cm), so we never depend on guessing the mesh size.
        float nativeHeight = _mesh.bounds.size.y;
        _baseScale = TargetHeight / Mathf.Max(nativeHeight, 0.01f);
        // If the pivot sits above the mesh base, lift each instance so its base meets the surface.
        _pivotLiftNative = Mathf.Max(0f, -_mesh.bounds.min.y);

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        _material = new Material(lit) { name = "Runtime Grass Clump", enableInstancing = true };
        var atlas = Resources.Load<Texture2D>("Grass/PolygonNatureBiomes_Meadow_Texture_01");
        if (atlas != null)
            _material.SetTexture("_BaseMap", atlas);
        // Match Synty's grass material: alpha-clip cutout at 0.3, double-sided (their grass is
        // alpha planes; single-sided culling leaves half the blades invisible).
        _material.EnableKeyword("_ALPHATEST_ON");
        _material.SetFloat("_AlphaClip", 1f);
        _material.SetFloat("_Cutoff", 0.3f);
        _material.SetFloat("_Cull", 0f);
        _material.renderQueue = 2450;

        _renderParams = new RenderParams(_material)
        {
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = true,
            // Large bounds around the planet so clumps are never frustum-culled as a batch.
            worldBounds = new Bounds(_planetCenter, Vector3.one * 40000f),
        };
    }

    public void SetPlanetCenter(Vector3 center) => _planetCenter = center;

    static Mesh LoadMesh(string path)
    {
        var go = Resources.Load<GameObject>(path);
        if (go == null) return null;
        var mf = go.GetComponentInChildren<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }

    public void Tick(Camera camera)
    {
        if (!Enabled || _mesh == null || _material == null || camera == null)
            return;

        _surface ??= ServiceLocator.TryGet(out IPlanetSurfaceSampler s) ? s : null;
        _climate ??= ServiceLocator.TryGet(out IClimateSampler c) ? c : null;
        if (_surface == null)
            return;

        Vector3 camPos = camera.transform.position;
        if ((camPos - _lastBuildPos).sqrMagnitude > RebuildMoveThreshold * RebuildMoveThreshold)
        {
            Rebuild(camPos);
            _lastBuildPos = camPos;
        }

        if (_count > 0)
            Graphics.RenderMeshInstanced(_renderParams, _mesh, 0, _matrices, _count);
    }

    void Rebuild(Vector3 camPos)
    {
        _count = 0;

        Vector3 up = camPos - _planetCenter;
        if (up.sqrMagnitude < 1e-4f) { up = Vector3.up; }
        up.Normalize();
        Vector3 tangent = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 bitangent = Vector3.Cross(up, tangent);

        // Iterate ABSOLUTE (world-aligned) tangent cells around the camera, not camera-relative
        // offsets: each cell's hash is then stable as the camera moves, so clumps stay planted
        // instead of re-rolling every frame (the jump/pop bug). The tangent frame rotates only
        // slowly across the sphere, so local movement is stable.
        Vector3 rel = camPos - _planetCenter;
        float cpU = Vector3.Dot(rel, tangent);
        float cpV = Vector3.Dot(rel, bitangent);
        int minU = Mathf.FloorToInt((cpU - DiscRadius) / CellSpacing);
        int maxU = Mathf.FloorToInt((cpU + DiscRadius) / CellSpacing);
        int minV = Mathf.FloorToInt((cpV - DiscRadius) / CellSpacing);
        int maxV = Mathf.FloorToInt((cpV + DiscRadius) / CellSpacing);

        for (int cellU = minU; cellU <= maxU && _count < MaxInstances; cellU++)
        {
            for (int cellV = minV; cellV <= maxV && _count < MaxInstances; cellV++)
            {
                float h0 = Hash01(cellU * 73856093 ^ cellV * 19349663);
                if (h0 > 0.62f)   // partial density - not every cell gets a clump
                    continue;
                float h1 = Hash01(cellU * 83492791 ^ cellV * 40503 ^ 0x1234);
                float h2 = Hash01(cellU * 19349663 ^ cellV * 73856093 ^ 0x5678);

                float pu = (cellU + 0.5f + (h1 - 0.5f)) * CellSpacing;
                float pv = (cellV + 0.5f + (h2 - 0.5f)) * CellSpacing;
                float du = pu - cpU, dv = pv - cpV;
                if (du * du + dv * dv > DiscRadius * DiscRadius)
                    continue;

                Vector3 dir = (camPos + tangent * du + bitangent * dv - _planetCenter).normalized;
                if (!_surface.TryGetSurfaceRadius(dir, out float radius) || radius <= 0f)
                    continue;

                float scale = _baseScale * Mathf.Lerp(0.75f, 1.3f, h2);
                Vector3 pos = _planetCenter + dir * (radius + _pivotLiftNative * scale);

                if (_climate != null && _climate.TrySampleClimate(pos, out ClimateSample climate)
                    && climate.Moisture01 < MoistureGate)
                    continue;

                Quaternion align = Quaternion.FromToRotation(Vector3.up, dir);
                Quaternion yaw = Quaternion.AngleAxis(h1 * 360f, dir);
                _matrices[_count++] = Matrix4x4.TRS(pos, yaw * align, new Vector3(scale, scale, scale));
            }
        }
    }

    static float Hash01(int seed)
    {
        uint x = (uint)seed;
        x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
        return (x & 0x00ffffffu) / 16777216f;
    }
}
