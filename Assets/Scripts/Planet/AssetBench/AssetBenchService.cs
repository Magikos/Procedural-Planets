using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Judging state for the asset bench: spawns candidate/reference pairs on the planet surface, frames the
/// camera on one pair at a time, and records a verdict per pair.
///
/// Grounding deliberately uses the same raycast-with-analytic-fallback path the character walks on. The
/// analytic surface can sit below the rendered mesh, and a prop floating above the terrain you are standing
/// on answers the wrong question.
/// </summary>
public enum BenchShaderMode
{
    /// <summary>Render the candidate the way the vendor authored it — their shader, their look.</summary>
    AsAuthored,

    /// <summary>
    /// Re-render the candidate's meshes on the project's own shader, carrying the vendor textures across.
    /// This is the default because it answers the question actually being asked: not "how does this look in
    /// the vendor's demo scene" but "how will this look in our world" — planet-aware lighting, night side
    /// going dark, the same treatment every adopted asset ends up with.
    /// </summary>
    ProjectShader,
}

/// <summary>The spawned objects for one judged entry, kept so the HUD can label them and isolate the pair.</summary>
public sealed class BenchPair
{
    public GameObject Candidate;
    public GameObject Reference;
    public Bounds Bounds;
    public Vector3 Up;

    /// World points just above each object, where the HUD hangs its marker.
    public Vector3 CandidateLabelPoint;
    public Vector3 ReferenceLabelPoint;
}

public sealed class AssetBenchService
{
    // Layout is derived from the batch's largest footprint rather than fixed metres: a fixed gap that suits
    // rocks buries two tree canopies in each other. Multiples of that radius, converted to angular offsets
    // so spacing stays even as it wraps around the curve.
    const float GapPerRadius = 2.8f;
    const float StridePerRadius = 6.5f;
    const float MinFootprintRadius = 1f;
    const float FootOffset = 0f;

    IPlanet _planet;
    IPlanetSurfaceSampler _sampler;
    IPlanetSurfaceRaycaster _raycaster;
    IFreeCameraService _freeCam;

    IGroundingProvider _grounding;
    Vector3 _center;
    float _radius;
    float _seaLevel;
    int _seaLevelHits;
    bool _shaderMissing;

    AssetBenchManifest _manifest;
    readonly List<BenchRow> _rows = new();

    readonly List<BenchPair> _pairs = new();

    Transform _container;
    int _focus = -1;
    string _biome = "";
    bool _isolated;

    public bool IsLoaded => _manifest != null;
    public int Count => _rows.Count;
    public int FocusIndex => _focus;
    public IReadOnlyList<BenchPair> Pairs => _pairs;

    public int JudgedCount
    {
        get
        {
            int n = 0;
            foreach (BenchRow r in _rows)
                if (r.Verdict != BenchVerdict.Unjudged) n++;
            return n;
        }
    }

    /// <summary>Framing multiplier — below 1 moves the camera in. Taste, so it is tunable at runtime.</summary>
    public float Zoom { get; private set; } = 1f;

    /// <summary>Multiplier on the derived layout spacing. Read when a batch is loaded.</summary>
    public float Spacing { get; private set; } = 1f;

    public BenchShaderMode ShaderMode { get; private set; } = BenchShaderMode.ProjectShader;

    /// <summary>Shader the candidates are re-rendered on. Foliage by default; most candidates are props.</summary>
    public string ProjectShaderName { get; private set; } = "Scatter/FoliageLit";

    public string SetShaderMode(BenchShaderMode mode, string shaderName)
    {
        ShaderMode = mode;
        if (!string.IsNullOrWhiteSpace(shaderName))
        {
            if (Shader.Find(shaderName) == null)
                return $"bench: no shader named '{shaderName}'";
            ProjectShaderName = shaderName;
        }

        string reload = IsLoaded ? " — reload the batch to apply" : "";
        return mode == BenchShaderMode.AsAuthored
            ? $"bench: candidates render as authored (vendor shaders){reload}"
            : $"bench: candidates render on {ProjectShaderName}{reload}";
    }

    public string SetSpacing(float value)
    {
        Spacing = Mathf.Clamp(value, 0.25f, 6f);
        string reload = IsLoaded ? " — reload the batch to apply" : "";
        return $"bench: spacing = {Spacing:F2}{reload}";
    }

    public string SetZoom(float value)
    {
        Zoom = Mathf.Clamp(value, 0.2f, 5f);
        Focus(_focus);
        return $"bench: zoom = {Zoom:F2} (lower is closer)";
    }

    /// <summary>True while the reference is hidden so the candidate can be read on its own.</summary>
    public bool IsIsolated => _isolated;

    public BenchPair CurrentPair =>
        IsLoaded && _focus >= 0 && _focus < _pairs.Count ? _pairs[_focus] : null;

    public string StatusLine
    {
        get
        {
            if (!IsLoaded) return "bench: no batch loaded";
            if (_focus < 0 || _focus >= _rows.Count) return $"bench: {_manifest.BatchId} ({_rows.Count} pairs)";
            BenchRow r = _rows[_focus];
            string rework = r.NeedsRework ? " [rework]" : "";
            return $"bench {_focus + 1}/{_rows.Count} · {r.Label} · {r.Verdict}{rework} · {r.Question}";
        }
    }

    public string Load(AssetBenchManifest manifest, out bool ok)
    {
        ok = false;
        if (IsLoaded)
            return $"bench: '{_manifest.BatchId}' already loaded — run bench.report or bench.cancel first";
        if (manifest == null)
            return "bench: manifest is null";
        if (manifest.Entries == null || manifest.Entries.Length == 0)
            return "bench: manifest has no entries";
        if (!ResolvePlanet(out string planetError))
            return planetError;

        _manifest = manifest;
        _shaderMissing = false;
        _container = new GameObject($"AssetBench [{manifest.BatchId}]").transform;

        Vector3 originDir = CameraDirOrDefault();
        int spawned = 0, failed = 0, incompatible = 0;

        float footprint = Mathf.Max(BatchFootprintRadius(manifest), MinFootprintRadius);
        float gapRad = Ang(footprint * GapPerRadius * Spacing);
        float strideRad = Ang(footprint * StridePerRadius * Spacing);
        float forwardRad = Ang(Mathf.Max(14f, footprint * 3f));

        for (int i = 0; i < manifest.Entries.Length; i++)
        {
            AssetBenchEntry e = manifest.Entries[i];
            var row = new BenchRow
            {
                Index = i,
                Label = string.IsNullOrEmpty(e?.Label) ? $"entry {i}" : e.Label,
                Question = e?.Question ?? "",
                Verdict = BenchVerdict.Unjudged,
                Biome = _biome,
                Note = "",
                CandidatePath = PathOf(e?.CandidatePrefab)
            };

            if (e?.CandidatePrefab == null)
            {
                row.Verdict = BenchVerdict.Error;
                row.Note = "candidate prefab missing";
                _rows.Add(row);
                _pairs.Add(new BenchPair { Bounds = new Bounds(SurfacePoint(originDir), Vector3.one * 4f) });
                failed++;
                continue;
            }

            BenchSlot slot = AssetBenchPlacement.SlotDirection(originDir, i, strideRad, gapRad, forwardRad);

            GameObject candidate = TrySpawn(e.CandidatePrefab, slot.CandidateDir, e.CandidateMaterial);
            GameObject reference = SpawnReference(e, slot.ReferenceDir);

            if (candidate == null)
            {
                row.Verdict = BenchVerdict.Error;
                row.Note = "grounding failed";
                failed++;
            }
            else spawned++;

            string badShaders = DescribeIncompatibleShaders(candidate);
            if (badShaders != null)
            {
                row.Note = $"candidate will not render under URP — Built-in shader(s): {badShaders}";
                row.NeedsRework = true;
                incompatible++;
            }

            Vector3 centreDir = (slot.CandidateDir + slot.ReferenceDir).normalized;
            _rows.Add(row);
            _pairs.Add(new BenchPair
            {
                Candidate = candidate,
                Reference = reference,
                Up = centreDir,
                Bounds = MeasurePair(candidate, reference, centreDir),
                CandidateLabelPoint = LabelPoint(candidate, slot.CandidateDir),
                ReferenceLabelPoint = LabelPoint(reference, slot.ReferenceDir),
            });
        }

        Focus(0);
        ok = true;

        string tail = failed > 0 ? $", {failed} failed" : "";

        // Grounding floors at sea level, so an ocean load silently stands everything on water — which looks
        // plausible and judges nothing. Say so rather than let it pass unnoticed.
        string water = _seaLevelHits > 0
            ? $"  ⚠ {_seaLevelHits} object(s) grounded at sea level — you are over ocean. Fly to land and re-run bench.load."
            : "";

        string shaders = incompatible > 0
            ? $"  ⚠ {incompatible} candidate(s) use Built-in-pipeline shaders and will draw magenta — F5 to mark Blocked."
            : "";

        if (_shaderMissing)
            shaders += $"  ⚠ shader '{ProjectShaderName}' not found — candidates left as authored.";

        string mode = ShaderMode == BenchShaderMode.ProjectShader
            ? $"  Rendering candidates on {ProjectShaderName} (bench.shader vendor to compare)."
            : "  Rendering candidates as authored (bench.shader project to compare).";

        return $"bench: loaded '{manifest.BatchId}' — {spawned} pairs{tail}. "
             + $"1-9 jump, Tab next, F1 keep / F2 cut / F3 later / F5 blocked.{mode}{water}{shaders}";
    }

    public void Focus(int index)
    {
        if (!IsLoaded || _rows.Count == 0) return;
        _focus = Mathf.Clamp(index, 0, _rows.Count - 1);

        if (_freeCam == null) ServiceLocator.TryGet(out _freeCam);
        if (_freeCam == null || _focus >= _pairs.Count) return;

        Bounds b = _pairs[_focus].Bounds;
        _freeCam.FrameCloseUp(b.center, b.extents.magnitude * Zoom);
    }

    /// <summary>Hide the reference so the candidate reads on its own, and back again.</summary>
    public string ToggleIsolate()
    {
        if (!IsLoaded) return "bench: no batch loaded";

        _isolated = !_isolated;
        foreach (BenchPair p in _pairs)
            if (p.Reference != null) p.Reference.SetActive(!_isolated);

        return _isolated ? "bench: reference hidden — candidate only" : "bench: reference shown";
    }

    public void FocusNext() => Focus(_focus + 1 >= _rows.Count ? 0 : _focus + 1);
    public void FocusPrevious() => Focus(_focus - 1 < 0 ? _rows.Count - 1 : _focus - 1);

    /// <summary>Index is 1-based to match what the HUD and status line show.</summary>
    public string FocusOneBased(int oneBased)
    {
        if (!IsLoaded) return "bench: no batch loaded";
        if (oneBased < 1 || oneBased > _rows.Count)
            return $"bench: pick 1-{_rows.Count}\n{PairList()}";

        Focus(oneBased - 1);
        return StatusLine;
    }

    public string PairList()
    {
        if (!IsLoaded) return "bench: no batch loaded";

        var sb = new System.Text.StringBuilder($"bench '{_manifest.BatchId}' — {_rows.Count} pair(s)");
        for (int i = 0; i < _rows.Count; i++)
        {
            BenchRow r = _rows[i];
            string marker = i == _focus ? ">" : " ";
            sb.Append($"\n {marker} {i + 1}. {r.Label} — {r.Verdict}{(r.NeedsRework ? " [rework]" : "")}");
        }
        return sb.ToString();
    }

    public IReadOnlyList<BenchRow> Rows => _rows;

    public string SetVerdict(BenchVerdict verdict)
    {
        if (!TryCurrent(out BenchRow row)) return "bench: nothing focused";
        row.Verdict = verdict;
        string label = row.Label;
        FocusNext();
        return $"bench: {label} → {verdict}. {StatusLine}";
    }

    public string SetNote(string note)
    {
        if (!TryCurrent(out BenchRow row)) return "bench: nothing focused";
        row.Note = note ?? "";
        return $"bench: note on {row.Label}";
    }

    public string SetNeedsRework(bool value)
    {
        if (!TryCurrent(out BenchRow row)) return "bench: nothing focused";
        row.NeedsRework = value;
        return $"bench: {row.Label} needs-rework = {value}";
    }

    public string SetBiome(string biome)
    {
        _biome = biome ?? "";
        foreach (BenchRow r in _rows)
            if (r.Verdict == BenchVerdict.Unjudged) r.Biome = _biome;
        return $"bench: biome tag = '{_biome}' (applies to unjudged rows)";
    }

    public string BuildReport(string isoTimestamp) =>
        AssetBenchReport.BuildMarkdown(_manifest != null ? _manifest.BatchId : "batch", isoTimestamp, _rows);

    public string BatchId => _manifest != null ? _manifest.BatchId : "batch";

    public void Unload()
    {
        if (_container != null) UnityEngine.Object.Destroy(_container.gameObject);
        _container = null;
        _manifest = null;
        _rows.Clear();
        _pairs.Clear();
        _focus = -1;
        _isolated = false;
    }

    // --- internals ---

    bool TryCurrent(out BenchRow row)
    {
        row = null;
        if (!IsLoaded || _focus < 0 || _focus >= _rows.Count) return false;
        row = _rows[_focus];
        return true;
    }

    float Ang(float meters) => _radius > 1f ? meters / _radius : 0.001f;

    GameObject SpawnReference(AssetBenchEntry entry, Vector3 dir)
    {
        if (entry.ReferencePrototype != null)
            return TrySpawnPrototype(entry.ReferencePrototype, dir);

        return entry.ReferencePrefab != null ? TrySpawn(entry.ReferencePrefab, dir, null) : null;
    }

    GameObject TrySpawn(GameObject prefab, Vector3 dir, Material materialOverride)
    {
        if (!TryGroundAt(dir, out Vector3 groundPoint))
            return null;

        GameObject go = UnityEngine.Object.Instantiate(prefab, groundPoint, Quaternion.identity, _container);

        // An explicit material on the entry is a deliberate authoring choice and outranks the mode.
        if (materialOverride != null) ApplyMaterial(go, materialOverride);
        else if (ShaderMode == BenchShaderMode.ProjectShader && !ApplyProjectShader(go)) _shaderMissing = true;

        Orient(go, groundPoint, dir);
        return go;
    }

    /// Rebuilds the prop the way the scatter renderer does — LOD0 mesh and authored material per part — so
    /// the reference is what the planet actually plants rather than an FBX with its vendor placeholder.
    GameObject TrySpawnPrototype(ScatterPrototype prototype, Vector3 dir)
    {
        if (!TryGroundAt(dir, out Vector3 groundPoint))
            return null;

        var root = new GameObject(prototype.DisplayName);
        root.transform.SetParent(_container, false);
        root.transform.position = groundPoint;

        if (prototype.Parts != null && prototype.Parts.Length > 0)
        {
            foreach (ScatterPart part in prototype.Parts)
                AddPart(root.transform, part.Name, part.Material, Lod0(part.LodMeshes),
                    part.CastShadows, part.ReceiveShadows);
        }
        else
        {
            AddPart(root.transform, "Part", prototype.Material, Lod0(prototype.LodMeshes),
                prototype.CastShadows, prototype.ReceiveShadows);
        }

        Orient(root, groundPoint, dir);
        return root;
    }

    static Mesh Lod0(Mesh[] meshes) => meshes != null && meshes.Length > 0 ? meshes[0] : null;

    /// Widest thing in the batch, so one spacing suits every pair and the row stays readable end to end.
    /// Measured off the assets — layout has to be decided before anything is instantiated.
    static float BatchFootprintRadius(AssetBenchManifest manifest)
    {
        float radius = 0f;
        foreach (AssetBenchEntry e in manifest.Entries)
        {
            if (e == null) continue;
            radius = Mathf.Max(radius, FootprintRadius(e.CandidatePrefab));
            radius = Mathf.Max(radius, e.ReferencePrototype != null
                ? FootprintRadius(e.ReferencePrototype)
                : FootprintRadius(e.ReferencePrefab));
        }
        return radius;
    }

    static float FootprintRadius(GameObject prefab)
    {
        if (prefab == null) return 0f;

        float radius = 0f;
        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Vector3 scale = filter.transform.lossyScale;
            Vector3 extents = filter.sharedMesh.bounds.extents;
            radius = Mathf.Max(radius, Mathf.Abs(extents.x * scale.x), Mathf.Abs(extents.z * scale.z));
        }
        return radius;
    }

    static float FootprintRadius(ScatterPrototype prototype)
    {
        if (prototype == null) return 0f;

        float radius = 0f;
        if (prototype.Parts != null)
            foreach (ScatterPart part in prototype.Parts)
                radius = Mathf.Max(radius, MeshFootprintRadius(Lod0(part.LodMeshes)));

        return radius > 0f ? radius : MeshFootprintRadius(Lod0(prototype.LodMeshes));
    }

    /// Horizontal only — a tall trunk does not need the neighbouring prop pushed away.
    static float MeshFootprintRadius(Mesh mesh)
    {
        if (mesh == null) return 0f;
        Vector3 extents = mesh.bounds.extents;
        return Mathf.Max(Mathf.Abs(extents.x), Mathf.Abs(extents.z));
    }

    static void AddPart(Transform parent, string name, Material material, Mesh mesh,
        bool castShadows, bool receiveShadows)
    {
        if (mesh == null) return;

        var go = new GameObject(string.IsNullOrEmpty(name) ? "Part" : name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = castShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = receiveShadows;
    }

    bool TryGroundAt(Vector3 dir, out Vector3 groundPoint)
    {
        Vector3 probe = _center + dir * (_radius + 200f);
        if (!_grounding.TryGround(probe, -dir, FootOffset, out GroundResult ground))
        {
            groundPoint = default;
            return false;
        }

        if (_seaLevel > 0f && Vector3.Distance(ground.Position, _center) <= _seaLevel + 0.5f)
            _seaLevelHits++;

        groundPoint = ground.Position;
        return true;
    }

    void Orient(GameObject go, Vector3 groundPoint, Vector3 dir)
    {
        AssetBenchPlacement.BuildTangentBasis(dir, out _, out Vector3 forward);
        go.transform.rotation = Quaternion.LookRotation(forward, dir);
        SnapToGround(go, groundPoint, dir);
    }

    /// Assigns the shared material as-is — the bench never writes to it, so no clone is needed.
    static void ApplyMaterial(GameObject go, Material material)
    {
        if (material == null) return;

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            var slots = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < slots.Length; i++) slots[i] = material;
            r.sharedMaterials = slots;
        }
    }

    /// Rebuilds every material slot on the project's shader, carrying the vendor albedo across. Per-slot
    /// rather than one material for the whole prop: a tree is bark plus leaves, and collapsing them to a
    /// single material puts bark on the canopy.
    bool ApplyProjectShader(GameObject go)
    {
        Shader shader = Shader.Find(ProjectShaderName);
        if (shader == null) return false;

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            Material[] source = r.sharedMaterials;
            var slots = new Material[source.Length];

            for (int i = 0; i < source.Length; i++)
            {
                var converted = new Material(shader) { name = (source[i] != null ? source[i].name : "slot" + i) + " (bench)" };
                CopyAlbedo(source[i], converted);
                slots[i] = converted;
            }

            r.sharedMaterials = slots;
        }

        return true;
    }

    static readonly string[] AlbedoProperties = { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo", "_Diffuse" };

    static void CopyAlbedo(Material source, Material destination)
    {
        if (source == null) return;

        foreach (string property in AlbedoProperties)
        {
            if (!source.HasProperty(property)) continue;
            Texture texture = source.GetTexture(property);
            if (texture == null) continue;

            foreach (string target in AlbedoProperties)
                if (destination.HasProperty(target)) { destination.SetTexture(target, texture); break; }

            break;
        }

        if (source.HasProperty("_BaseColor") && destination.HasProperty("_BaseColor"))
            destination.SetColor("_BaseColor", source.GetColor("_BaseColor"));
        else if (source.HasProperty("_Color") && destination.HasProperty("_BaseColor"))
            destination.SetColor("_BaseColor", source.GetColor("_Color"));
    }

    /// Vendor prefabs do not agree on where the pivot sits — base, centre, or an arbitrary rig root — so
    /// placing the pivot on the ground leaves props floating or half-buried. Drop each one until its lowest
    /// rendered point touches the ground instead.
    static void SnapToGround(GameObject go, Vector3 groundPoint, Vector3 up)
    {
        bool any = false;
        Bounds bounds = default;
        Encapsulate(go, ref bounds, ref any);
        if (!any) return;

        // Support distance of the world AABB along the surface normal.
        Vector3 ext = bounds.extents;
        float halfHeight = Mathf.Abs(ext.x * up.x) + Mathf.Abs(ext.y * up.y) + Mathf.Abs(ext.z * up.z);
        float bottomAboveGround = Vector3.Dot(bounds.center - groundPoint, up) - halfHeight;

        go.transform.position -= up * bottomAboveGround;
    }

    /// A Built-in-pipeline shader still compiles and reports isSupported under URP — it just draws magenta.
    /// Catching it here turns "why is the fox pink" into a note before the judging starts.
    static string DescribeIncompatibleShaders(GameObject go)
    {
        if (go == null) return null;

        var offenders = new List<string>();
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                if (DeclaresUniversalPipeline(m.shader)) continue;
                if (!offenders.Contains(m.shader.name)) offenders.Add(m.shader.name);
            }
        }

        return offenders.Count == 0 ? null : string.Join(", ", offenders);
    }

    static bool DeclaresUniversalPipeline(Shader shader)
    {
        var tag = new UnityEngine.Rendering.ShaderTagId("RenderPipeline");
        for (int i = 0; i < shader.subshaderCount; i++)
        {
            string value = shader.FindSubshaderTagValue(i, tag).name;
            if (!string.IsNullOrEmpty(value) && value.Contains("Universal")) return true;
        }
        return false;
    }

    /// Renderer bounds rather than the spawn points: a tree and a pebble need very different standoffs,
    /// and the pair must fit in frame together.
    Bounds MeasurePair(GameObject candidate, GameObject reference, Vector3 centreDir)
    {
        bool any = false;
        Bounds bounds = default;

        Encapsulate(candidate, ref bounds, ref any);
        Encapsulate(reference, ref bounds, ref any);

        return any ? bounds : new Bounds(SurfacePoint(centreDir), Vector3.one * 4f);
    }

    Vector3 LabelPoint(GameObject go, Vector3 dir)
    {
        if (go == null) return SurfacePoint(dir);

        bool any = false;
        Bounds b = default;
        Encapsulate(go, ref b, ref any);
        if (!any) return go.transform.position + dir * 2f;

        Vector3 ext = b.extents;
        float halfHeight = Mathf.Abs(ext.x * dir.x) + Mathf.Abs(ext.y * dir.y) + Mathf.Abs(ext.z * dir.z);
        return b.center + dir * (halfHeight + 1.2f);
    }

    static void Encapsulate(GameObject go, ref Bounds bounds, ref bool any)
    {
        if (go == null) return;

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
    }

    Vector3 SurfacePoint(Vector3 dir)
    {
        Vector3 probe = _center + dir * (_radius + 200f);
        return _grounding.TryGround(probe, -dir, FootOffset, out GroundResult g)
            ? g.Position
            : _center + dir * _radius;
    }

    Vector3 CameraDirOrDefault()
    {
        Camera cam = Camera.main;
        if (cam == null) return Vector3.up;
        Vector3 d = cam.transform.position - _center;
        return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.up;
    }

    bool ResolvePlanet(out string error)
    {
        error = null;
        if (_planet == null) ServiceLocator.TryGet(out _planet);
        if (_sampler == null) ServiceLocator.TryGet(out _sampler);
        if (_raycaster == null) ServiceLocator.TryGet(out _raycaster);
        if (_freeCam == null) ServiceLocator.TryGet(out _freeCam);

        if (_planet == null || _sampler == null)
            return Fail(out error, "bench: no planet yet — generate a world first");
        if (_planet.LastGeneratedRadius <= 0f)
            return Fail(out error, "bench: planet has no generated radius yet");

        _center = _planet.Transform != null ? _planet.Transform.position : Vector3.zero;
        _radius = _planet.LastGeneratedRadius;
        _seaLevel = _planet.LastSeaLevelRadius;
        _seaLevelHits = 0;

        var analytic = new PlanetSurfaceGrounding(_sampler, _center, _seaLevel);
        _grounding = _raycaster != null
            ? new PlanetRaycastGrounding(_raycaster, _center, _seaLevel, analytic)
            : analytic;

        return true;
    }

    static bool Fail(out string error, string message)
    {
        error = message;
        return false;
    }

    static string PathOf(GameObject prefab)
    {
#if UNITY_EDITOR
        return prefab != null ? UnityEditor.AssetDatabase.GetAssetPath(prefab) : "";
#else
        return prefab != null ? prefab.name : "";
#endif
    }
}
