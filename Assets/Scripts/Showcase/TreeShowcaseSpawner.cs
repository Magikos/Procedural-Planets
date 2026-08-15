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
    public float ColSpacing = 9f;
    public float RowSpacing = 11f;
    public int Seed = 12345;
    public bool BuildGround = true;
    public bool FrameCamera = true;   // snap Camera.main to view the whole table + fix its clip planes

    static readonly float[] Ages = { 0.15f, 0.4f, 0.7f, 1f };
    static readonly string[] AgeNames = { "sapling", "young", "adult", "old" };

    static readonly int SunParamsId = Shader.PropertyToID("_SunParams");
    static readonly int PlanetCenterId = Shader.PropertyToID("_PlanetCenter");
    static readonly int NightAmbientId = Shader.PropertyToID("_NightAmbientIntensity");
    static readonly int WindDirectionId = Shader.PropertyToID("_WindDirection");
    static readonly int WindStrength01Id = Shader.PropertyToID("_WindStrength01");
    static readonly int WindSpeedMpsId = Shader.PropertyToID("_WindSpeedMps");
    static readonly int InteractorsId = Shader.PropertyToID("_GrassInteractors");
    static readonly int InteractorCountId = Shader.PropertyToID("_GrassInteractorCount");
    ComputeBuffer _dummyInteractors;

    void OnEnable()
    {
        // FoliageLit declares the global _GrassInteractors StructuredBuffer; an unbound SRV silently drops every
        // FoliageLit draw. Bind a 1-element dummy (count 0 => never read) so the trees render off the planet.
        _dummyInteractors ??= new ComputeBuffer(1, sizeof(float) * 8, ComputeBufferType.Structured);
        Shader.SetGlobalBuffer(InteractorsId, _dummyInteractors);
        Shader.SetGlobalInt(InteractorCountId, 0);
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
            SpawnCapsule(new Vector3(-ColSpacing * 0.9f, 0f, z), row == 0); // 2 m character for scale
            SpawnOld(p, new Vector3(0f, 0f, z), $"{p.Biome} OLD\n{p.DisplayName}");

            // Materials from the injected prototype (same index) = exactly what the planet uses.
            Material bark = null, foliage = null;
            ScatterPrototypeDto inj = injected.Prototypes[i];
            if (!ReferenceEquals(inj, p) && inj?.Parts != null && inj.Parts.Length >= 2)
            {
                bark = inj.Parts[0].Material;
                foliage = inj.Parts[1].Material;
            }

            for (int a = 0; a < Ages.Length; a++)
            {
                TreeDef def = TreeDefLibrary.Species(species, Ages[a]);
                GeneratedTree t = TreeGenerator.Generate(def, Seed + i * 7 + a);
                var cell = new GameObject($"{species} {AgeNames[a]}");
                cell.transform.SetParent(transform, false);
                cell.transform.localPosition = new Vector3((a + 1) * ColSpacing, 0f, z);
                AddMesh(cell.transform, "bark", t.Bark, bark);
                AddMesh(cell.transform, "foliage", t.Foliage, foliage);
                AddLabel(cell.transform, $"{species}\n{AgeNames[a]}");
            }
            row++;
        }

        SetupStage(row);
    }

    // Ground plane + soft ambient + a framed, un-clipped camera so the table is reviewable without hand-setup.
    void SetupStage(int rows)
    {
        float xSpan = 4f * ColSpacing;                 // old(0) .. old age (4*col)
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
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
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
