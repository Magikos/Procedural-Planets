using UnityEngine;
using UnityEngine.Rendering;

// Review cockpit for the generated-tree program (plan 006): one row per tree biome — the OLD Synty asset on the
// left, then our generated tree across ages (sapling..old). It runs the real TreeInjection.Apply to grab the exact
// in-world materials, so the showcase can't drift from what ships. Self-contained: it also publishes the planet
// sun/wind globals + binds the grass-interactor buffer the FoliageLit trees need, so it works in any scene with
// just this component + a Sun light. Rebuild from the context menu or on play.
[ExecuteAlways]
public sealed class TreeShowcaseSpawner : MonoBehaviour
{
    public ScatterLibrary Library;
    public Light Sun;
    [Range(0f, 1f)] public float NightAmbientIntensity = 0.1f;
    // Trees are real-scale now (a mature broadleaf is ~34 m across), so cells have to be far enough apart that
    // neighbouring crowns never touch — a crowded table hides the silhouette the showcase exists to show.
    public float ColSpacing = 26f;
    public float RowSpacing = 34f;
    public int Seed = 12345;
    public bool BuildGround = true;
    public bool FrameCamera = true;   // snap Camera.main to view the whole table + fix its clip planes

    // Ages, then a DEAD column so the standing-snag variant is reviewed beside the living stages.
    static readonly float[] Ages = { 0.15f, 0.4f, 0.7f, 1f, 1f };
    static readonly string[] AgeNames = { "sapling", "young", "adult", "old", "DEAD" };

    static readonly int SunParamsId = Shader.PropertyToID("_SunParams");
    static readonly int PlanetCenterId = Shader.PropertyToID("_PlanetCenter");
    static readonly int NightAmbientId = Shader.PropertyToID("_NightAmbientIntensity");
    static readonly int WindDirectionId = Shader.PropertyToID("_WindDirection");
    static readonly int WindStrength01Id = Shader.PropertyToID("_WindStrength01");
    static readonly int WindSpeedMpsId = Shader.PropertyToID("_WindSpeedMps");
    ComputeBuffer _dummyInteractors;
    readonly System.Collections.Generic.Dictionary<Material, Material> _noFade = new();
    readonly System.Collections.Generic.List<Material> _spawnedMats = new();
    Material _anyLeaf;

    void OnEnable()
    {
        GrassInteractorFallback.Bind(ref _dummyInteractors);
        Publish();
    }

    void OnDisable()
    {
        _dummyInteractors?.Release();
        _dummyInteractors = null;
    }

    void Update() => Publish();

    void Publish()
    {
        Vector3 sunDir = Sun != null ? -Sun.transform.forward : Vector3.up;
        Shader.SetGlobalVector(SunParamsId, sunDir.normalized);
        Shader.SetGlobalVector(PlanetCenterId, new Vector3(0f, -1_000_000f, 0f)); // flat ground => up is world-up
        Shader.SetGlobalFloat(NightAmbientId, NightAmbientIntensity);
        Shader.SetGlobalVector(WindDirectionId, Vector3.right);
        Shader.SetGlobalFloat(WindStrength01Id, 0f);
        Shader.SetGlobalFloat(WindSpeedMpsId, 0f);
    }

    void Start()
    {
        if (Application.isPlaying) Rebuild();
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        foreach (Material m in _spawnedMats) if (m != null) DestroyImmediate(m);
        _spawnedMats.Clear();
        _noFade.Clear();
        _anyLeaf = null;

        if (Library == null) Library = Resources.Load<ScatterLibrary>("Settings/ScatterLibrary");
        if (Library == null) { Debug.LogError("TreeShowcaseSpawner: no ScatterLibrary found."); return; }

        var raw = ScatterLibraryDto.From(Library);
        bool prevEnabled = TreeInjection.Enabled;
        TreeInjection.Enabled = true;
        var injected = TreeInjection.Apply(raw);
        TreeInjection.Enabled = prevEnabled;

        int row = 0;
        for (int i = 0; i < raw.Prototypes.Length; i++)
        {
            ScatterPrototypeDto p = raw.Prototypes[i];
            if (p == null || p.Interaction != ScatterInteraction.Chop) continue;
            if (!TreeDefLibrary.HasTree(p.Biome, out TreeDefLibrary.TreeSpecies species)) continue;

            float z = -row * RowSpacing;
            SpawnOld(p, new Vector3(0f, 0f, z), $"{p.Biome} OLD\n{p.DisplayName}");
            SpawnCapsule(new Vector3(4f, 0f, z), row == 0); // 2 m character beside the Synty original too

            // Materials from the injected prototype (same index) = exactly what the planet uses. A "* Dead Tree"
            // prototype injects a bark part ONLY, so its foliage material has to be borrowed — this row still
            // shows the living age stages, which need one.
            Material bark = null, foliage = null;
            ScatterPrototypeDto inj = injected.Prototypes[i];
            if (!ReferenceEquals(inj, p) && inj?.Parts != null && inj.Parts.Length >= 1)
            {
                bark = inj.Parts[0].Material;
                foliage = inj.Parts.Length >= 2 ? inj.Parts[1].Material : AnyLeafMaterial(injected);
            }

            for (int a = 0; a < Ages.Length; a++)
            {
                bool dead = a == Ages.Length - 1;
                TreeDef def = dead
                    ? TreeDefLibrary.DeadSpecies(species, Ages[a])
                    : TreeDefLibrary.Species(species, Ages[a]);
                GeneratedTree t = TreeGenerator.Generate(def, Seed + i * 7 + a);
                var cell = new GameObject($"{species} {AgeNames[a]}");
                cell.transform.SetParent(transform, false);
                cell.transform.localPosition = new Vector3((a + 1) * ColSpacing, 0f, z);
                AddMesh(cell.transform, "bark", t.Bark, bark);
                AddMesh(cell.transform, "foliage", t.Foliage, foliage);
                AddLabel(cell.transform, $"{species}\n{AgeNames[a]}");
                SpawnCapsule(new Vector3((a + 1) * ColSpacing + 4f, 0f, z), false); // a human beside EVERY tree
            }
            row++;
        }

        SetupStage(row);
    }

    // Ground plane + soft ambient + a framed, un-clipped camera so the table is reviewable without hand-setup.
    void SetupStage(int rows)
    {
        float xSpan = Ages.Length * ColSpacing;        // Synty original at 0 .. the DEAD column at Ages.Length*col
        float zSpan = Mathf.Max(1, rows - 1) * RowSpacing;
        Vector3 centerLocal = new Vector3(xSpan * 0.5f, 5f, -zSpan * 0.5f);

        if (BuildGround)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "ShowcaseGround";
            ground.transform.SetParent(transform, false);
            ground.transform.localPosition = new Vector3(centerLocal.x, 0f, centerLocal.z);
            ground.transform.localScale = new Vector3((xSpan + 24f) / 10f, 1f, (zSpan + 24f) / 10f);
            var mr = ground.GetComponent<MeshRenderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = "ShowcaseGround" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.42f, 0.45f, 0.4f));
            mr.sharedMaterial = m;
        }

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);

        if (FrameCamera && Camera.main != null)
        {
            var cam = Camera.main;
            Vector3 camLocal = new Vector3(xSpan * 0.5f, zSpan * 0.4f + 14f, zSpan * 0.55f + 16f);
            cam.transform.position = transform.TransformPoint(camLocal);
            cam.transform.LookAt(transform.TransformPoint(centerLocal + Vector3.up * 3f));
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, zSpan * 3f + 500f);
        }
    }

    void SpawnOld(ScatterPrototypeDto p, Vector3 pos, string label)
    {
        var cell = new GameObject(label.Replace('\n', ' '));
        cell.transform.SetParent(transform, false);
        cell.transform.localPosition = pos;
        if (p.Parts != null)
            foreach (ScatterPartDto part in p.Parts)
                if (part != null && part.CanRender)
                    AddMesh(cell.transform, "part", part.LodMeshes[0], part.Material);
        AddLabel(cell.transform, label);
    }

    // Unity's capsule primitive is exactly 2 m tall — a stand-in for the ~2 m player, so tree sizes are legible.
    void SpawnCapsule(Vector3 pos, bool label)
    {
        var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cap.name = "Character 2m";
        cap.transform.SetParent(transform, false);
        cap.transform.localPosition = new Vector3(pos.x, 1f, pos.z);
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = "Character" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.25f, 0.5f, 0.9f));
        cap.GetComponent<MeshRenderer>().sharedMaterial = m;
        if (label) AddLabel(cap.transform, "2m\ncharacter");
    }

    void AddMesh(Transform parent, string name, Mesh mesh, Material mat)
    {
        if (mesh == null || mat == null) return;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = NoFade(mat);
    }

    // Any generated-tree foliage material in the library, for rows whose own prototype injects bark only (dead
    // trees). Cached per Rebuild — the exact species tint matters less than the row rendering at all.
    Material AnyLeafMaterial(ScatterLibraryDto injected)
    {
        if (_anyLeaf != null) return _anyLeaf;
        foreach (ScatterPrototypeDto p in injected.Prototypes)
            if (p != null && p.Interaction == ScatterInteraction.Chop && p.Parts != null && p.Parts.Length >= 2)
            {
                _anyLeaf = p.Parts[1].Material;
                if (_anyLeaf != null) return _anyLeaf;
            }
        return null;
    }

    // Scatter materials dither out between _FadeStart and _FadeEnd (120..150 m by default). This table is wider
    // than that, so viewing the whole thing would fade the far trees to nothing. Render through a COPY with the
    // fade pushed past the horizon — copying matters because several of these are shared project assets.
    Material NoFade(Material src)
    {
        if (src == null || !src.HasProperty("_FadeEnd")) return src;
        if (_noFade.TryGetValue(src, out Material cached) && cached != null) return cached;
        var copy = new Material(src) { name = src.name + " (showcase)" };
        copy.SetFloat("_FadeStart", 8000f);
        copy.SetFloat("_FadeEnd", 10000f);
        _noFade[src] = copy;
        _spawnedMats.Add(copy);
        return copy;
    }

    void AddLabel(Transform parent, string text)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.characterSize = 0.2f;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.UpperCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.black;
    }
}
