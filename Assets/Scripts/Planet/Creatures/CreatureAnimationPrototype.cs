using UnityEngine;

/// <summary>Isolated comparison scene. Controls presentation without a planet or creature authority.</summary>
public sealed class CreatureAnimationPrototype : MonoBehaviour
{
    public CreatureVisualSettings Primary;
    public CreatureVisualSettings Comparison;
    public bool Walk = true;
    public bool TurnInPlace;
    public bool Eat;
    public bool Spine = true;
    public bool Look = true;
    public bool Chains = true;
    public bool Feet = true;
    [Range(0.1f, 4f)] public float Speed = 1f;
    [Range(-60f, 60f)] public float TurnRate = 25f;
    public Transform LookTarget;
    public ProceduralRigDefinition[] ChainExamples = System.Array.Empty<ProceduralRigDefinition>();
    CreatureAnimationView[] _views;
    float[] _halfHeights;
    ProceduralPoseRig[] _examples;
    Vector3[] _exampleOrigins;
    readonly System.Collections.Generic.List<Material> _previewMaterials = new();
    float _time;
    readonly PrototypeGrounding _ground = new();

    public static float GroundHeight(float x, float z) => 0.18f * Mathf.Sin(x * 1.1f) * Mathf.Cos(z * 0.8f);

    public void Initialize()
    {
        if (_views != null || Primary == null) return;
        var views = new System.Collections.Generic.List<CreatureAnimationView>
        {
            new CreatureAnimationView(transform, 0UL, Primary.Snapshot(), 1.84f),
            new CreatureAnimationView(transform, 1UL, Primary.Snapshot(), 1.84f)
        };
        if (Comparison != null)
            views.Add(new CreatureAnimationView(transform, 2UL, Comparison.Snapshot(), Comparison.ModelHeightMeters));
        _views = views.ToArray();
        _halfHeights = Comparison != null ? new[] { 0.92f, 0.92f, Comparison.ModelHeightMeters * 0.5f } : new[] { 0.92f, 0.92f };
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
        for (int i = 0; i < _views.Length; i++)
        {
            CreatureAnimationView view = _views[i];
            Transform root = view.Root;
            Vector3 velocity = Vector3.zero;
            if ((Walk || TurnInPlace) && !Eat)
            {
                root.rotation = Quaternion.AngleAxis(TurnRate * dt, Vector3.up) * root.rotation;
                velocity = Walk ? root.forward * Speed : Vector3.zero;
                root.position += velocity * dt;
                Vector3 center = new((i - 1) * 3.5f, 0f, 0f);
                if (Vector3.ProjectOnPlane(root.position - center, Vector3.up).magnitude > 3f)
                {
                    // Restart a bounded demonstration path. The rig detects this teleport and resets contacts.
                    root.SetPositionAndRotation(center + Vector3.up * _halfHeights[i], Quaternion.identity);
                }
            }
            Vector3 position = root.position;
            position.y = GroundHeight(position.x, position.z) + _halfHeights[i];
            root.position = position;
            if (view.Pose != null)
            {
                view.Pose.SpineEnabled = Spine; view.Pose.LookEnabled = Look;
                view.Pose.ChainsEnabled = Chains; view.Pose.FeetEnabled = Feet;
            }
            view.LookTarget = LookTarget != null ? LookTarget.position : null;
            view.Eating = Eat;
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
                $": planted={view.Pose.PlantedFeet}, residual={view.Pose.MaxFootError:F3}m"));
        return result.ToString();
    }

    void OnDisable()
    {
        if (_views != null) foreach (CreatureAnimationView view in _views) view.Dispose();
        if (_examples != null) foreach (ProceduralPoseRig rig in _examples) rig.RestoreAnimation();
        foreach (Material material in _previewMaterials) Destroy(material);
        _previewMaterials.Clear();
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
