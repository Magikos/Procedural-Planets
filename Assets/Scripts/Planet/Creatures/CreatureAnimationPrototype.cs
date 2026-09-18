using UnityEngine;

/// <summary>Isolated comparison scene. Controls presentation without a planet or creature authority.</summary>
public sealed class CreatureAnimationPrototype : MonoBehaviour
{
    public CreatureVisualSettings Primary;
    public CreatureVisualSettings Comparison;
    public CreatureVisualSettings[] AdditionalVisuals = System.Array.Empty<CreatureVisualSettings>();
    [Tooltip("Optional world body heights matching AdditionalVisuals. Missing values use each model's authored height.")]
    public float[] AdditionalBodyHeights = System.Array.Empty<float>();
    public bool Walk = true;
    public bool TurnInPlace;
    public bool Eat;
    public bool Rest, Sleep, Drink, Stalk;
    public bool Swim;
    public bool UseSpeciesSwimWaterline = true;
    [Range(.1f, .85f)] public float SwimWaterline = .7f;
    public float SwimSurfaceHeight = 1.5f;
    public bool Spine = true;
    public bool Look = true;
    [Range(0f, 1f)] public float LookInfluence = 1f;
    public bool Chains = true;
    public bool Feet = true;
    [Range(0.1f, 4f)] public float Speed = 1f;
    [Range(-60f, 60f)] public float TurnRate = 25f;
    public Transform LookTarget;
    public ProceduralRigDefinition[] ChainExamples = System.Array.Empty<ProceduralRigDefinition>();
    CreatureAnimationView[] _views;
    float[] _halfHeights;
    float[] _swimWaterlines;
    ProceduralPoseRig[] _examples;
    Vector3[] _exampleOrigins;
    readonly System.Collections.Generic.List<Material> _previewMaterials = new();
    float _time;
    GameObject _swimSurface;
    readonly PrototypeGrounding _ground = new();

    public static float GroundHeight(float x, float z) => 0.18f * Mathf.Sin(x * 1.1f) * Mathf.Cos(z * 0.8f);

    public void Initialize()
    {
        if (_views != null || Primary == null) return;
        var library = Resources.Load<CreatureLibrary>("Settings/CreatureLibrary");
        float Waterline(CreatureVisualSettings visual)
        {
            if (library != null) foreach (var species in library.Species)
                if (species != null && species.Visuals == visual) return species.SwimWaterline;
            return .7f;
        }
        var swimWaterlines = new System.Collections.Generic.List<float> { Waterline(Primary), Waterline(Primary) };
        var views = new System.Collections.Generic.List<CreatureAnimationView>
        {
            new CreatureAnimationView(transform, 0UL, Primary.Snapshot(), 1.84f),
            new CreatureAnimationView(transform, 1UL, Primary.Snapshot(), 1.84f)
        };
        if (Comparison != null)
        {
            views.Add(new CreatureAnimationView(transform, 2UL, Comparison.Snapshot(), Comparison.ModelHeightMeters));
            swimWaterlines.Add(Waterline(Comparison));
        }
        var heights = new System.Collections.Generic.List<float> { .92f, .92f };
        if (Comparison != null) heights.Add(Comparison.ModelHeightMeters * .5f);
        for (int i = 0; i < (AdditionalVisuals?.Length ?? 0); i++)
        {
            var visuals = AdditionalVisuals[i];
            if (visuals == null) continue;
            float height = AdditionalBodyHeights != null && i < AdditionalBodyHeights.Length && AdditionalBodyHeights[i] > 0f
                ? AdditionalBodyHeights[i] : visuals.ModelHeightMeters;
            views.Add(new CreatureAnimationView(transform, (ulong)(3 + i), visuals.Snapshot(), height));
            heights.Add(height * .5f);
            swimWaterlines.Add(Waterline(visuals));
        }
        _views = views.ToArray();
        _swimWaterlines = swimWaterlines.ToArray();
        _halfHeights = heights.ToArray();
        for (int i = 0; i < _views.Length; i++)
        {
            _views[i].Root.position = new Vector3((i - 1) * 3.5f, _halfHeights[i], 0f);
            ApplyPreviewMaterials(_views[i].Root, _previewMaterials);
        }
        _examples = new ProceduralPoseRig[ChainExamples.Length];
        _exampleOrigins = new Vector3[ChainExamples.Length];
        for (int i = 0; i < _examples.Length; i++)
        {
            _examples[i] = new ProceduralPoseRig(ChainExamples[i].transform, ChainExamples[i]);
            _exampleOrigins[i] = ChainExamples[i].transform.position;
        }
    }

    void OnEnable() => Initialize();
    public static void ApplyPreviewMaterials(Transform root, System.Collections.Generic.List<Material> owned)
    {
        // Isolated scenes have no planet shader globals. Keep production material assets unchanged.
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetTexture("_BaseMap", renderer.sharedMaterial.GetTexture("_BaseMap"));
            material.SetFloat("_Smoothness", 0f);
            renderer.sharedMaterial = material;
            owned.Add(material);
        }
    }
    void Update() => Step(Time.deltaTime);

    public void Step(float dt)
    {
        Initialize();
        if (_views == null) return;
        _time += Mathf.Max(0f, dt);
        if (Swim && _swimSurface == null)
        {
            _swimSurface = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _swimSurface.name = "Swim review waterline";
            _swimSurface.transform.SetParent(transform, false);
            _swimSurface.transform.localScale = new Vector3(8f, 1f, 8f);
            Destroy(_swimSurface.GetComponent<Collider>());
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(.06f, .25f, .32f));
            _swimSurface.GetComponent<Renderer>().sharedMaterial = material;
            _previewMaterials.Add(material);
        }
        if (_swimSurface != null)
        {
            _swimSurface.SetActive(Swim);
            _swimSurface.transform.position = new Vector3(transform.position.x, SwimSurfaceHeight, transform.position.z);
        }
        for (int i = 0; i < _views.Length; i++)
        {
            CreatureAnimationView view = _views[i];
            Transform root = view.Root;
            Vector3 velocity = Vector3.zero;
            if ((Walk || TurnInPlace || Stalk) && (Swim || !Eat && !Drink && !Rest && !Sleep))
            {
                root.rotation = Quaternion.AngleAxis(TurnRate * dt, Vector3.up) * root.rotation;
                velocity = Walk || Stalk ? root.forward * Speed : Vector3.zero;
                root.position += velocity * dt;
                Vector3 center = new((i - 1) * 3.5f, 0f, 0f);
                if (Vector3.ProjectOnPlane(root.position - center, Vector3.up).magnitude > 3f)
                {
                    // Restart a bounded demonstration path. The rig detects this teleport and resets contacts.
                    root.SetPositionAndRotation(center + Vector3.up * _halfHeights[i], Quaternion.identity);
                }
            }
            Vector3 position = root.position;
            float waterline = UseSpeciesSwimWaterline ? _swimWaterlines[i] : SwimWaterline;
            position.y = Swim ? SwimSurfaceHeight - _halfHeights[i] * (waterline * 2f - 1f)
                : GroundHeight(position.x, position.z) + _halfHeights[i];
            root.position = position;
            if (view.Pose != null)
            {
                view.Pose.SpineEnabled = Spine; view.Pose.LookEnabled = Look;
                view.Pose.LookInfluence = LookInfluence;
                view.Pose.ChainsEnabled = Chains; view.Pose.FeetEnabled = Feet;
            }
            view.LookTarget = LookTarget != null ? LookTarget.position : null;
            view.Eating = Eat;
            view.Swimming = Swim;
            view.SwimWaterline = waterline;
            view.Drinking = Drink && !Eat;
            view.Resting = Rest && !Sleep && !Eat && !Drink;
            view.Sleeping = Sleep && !Eat && !Drink;
            view.Stalking = Stalk && !Eat && !Drink && !Rest && !Sleep;
            view.Tick(velocity, Vector3.up, dt, _ground);
        }
        for (int i = 0; i < _examples.Length; i++)
        {
            ProceduralRigDefinition definition = ChainExamples[i];
            definition.transform.position = _exampleOrigins[i] + Vector3.right * (Mathf.Sin(_time * 2f) * 0.3f);
            ProceduralPoseRig rig = _examples[i];
            rig.RestoreAnimation(); rig.CaptureAnimation(); rig.ChainsEnabled = Chains;
            rig.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, dt);
            var lines = definition.GetComponentsInChildren<LineRenderer>();
            for (int j = 0; j < lines.Length && j < definition.Chains.Length; j++)
            {
                Transform[] bones = definition.Chains[j].Bones;
                for (int k = 0; k < bones.Length; k++) lines[j].SetPosition(k, bones[k].position);
            }
        }
    }

    public string Diagnostics()
    {
        if (_views == null) return "Not initialized";
        var result = new System.Text.StringBuilder();
        foreach (CreatureAnimationView view in _views)
            result.AppendLine(view.Root.name + (view.Pose == null ? ": comparison clips only" :
                $": swim={view.SwimWeight:F2}, planted={view.Pose.PlantedFeet}, residual={view.Pose.MaxFootError:F3}m"));
        return result.ToString();
    }

    void OnDisable()
    {
        if (_views != null) foreach (CreatureAnimationView view in _views) view.Dispose();
        if (_examples != null) foreach (ProceduralPoseRig rig in _examples) rig.RestoreAnimation();
        foreach (Material material in _previewMaterials) Destroy(material);
        _previewMaterials.Clear();
        if (_swimSurface != null) Destroy(_swimSurface);
        _swimSurface = null;
        _views = null; _examples = null;
    }

    public sealed class PrototypeGrounding : IGroundingProvider
    {
        public bool TryGround(Vector3 p, Vector3 down, float offset, out GroundResult result)
        {
            float dx = 0.198f * Mathf.Cos(p.x * 1.1f) * Mathf.Cos(p.z * 0.8f);
            float dz = -0.144f * Mathf.Sin(p.x * 1.1f) * Mathf.Sin(p.z * 0.8f);
            result = new GroundResult(new Vector3(p.x, GroundHeight(p.x, p.z) + offset, p.z),
                new Vector3(-dx, 1f, -dz).normalized);
            return true;
        }
    }
}
