using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public enum BirdVisualKind { Automatic, Eagle, Vulture, Seagull }

/// <summary>Animated bird presentation, independent of authority and landing decisions.</summary>
public sealed class BirdAnimationView : IDisposable
{
    sealed class Art
    {
        public GameObject Prefab;
        public AnimationClip[] Clips;
        public Material Material;
        public Bounds StandingBounds;
        public bool HasBounds;
        public int References;
    }
    static readonly Dictionary<BirdVisualKind, Art> _artCache = new();
    readonly BirdVisualKind _kind;
    readonly Art _art;
    readonly ActorAnimationGraph _graph;
    public ActorAnimationGraph Graph => _graph;
    readonly AnimationMixerPlayable _mixer;
    readonly float _phase;
    readonly ProceduralPoseRig _pose;
    readonly SupportPlane _support = new();
    public ProceduralPoseRig Pose => _pose;
    float _age, _airWeight;
    bool _disposed, _poseSkipped;
    public int PoseEvaluations { get; private set; }
    public Transform Root { get; }
    public BirdVisualKind VisualKind => _kind;

    public BirdAnimationView(Transform parent, ulong identity, float height, bool vulture = false,
        BirdVisualKind visual = BirdVisualKind.Automatic)
    {
        if (!float.IsFinite(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (!Enum.IsDefined(typeof(BirdVisualKind), visual)) throw new ArgumentOutOfRangeException(nameof(visual));
        _kind = visual == BirdVisualKind.Automatic ? (vulture ? BirdVisualKind.Vulture : BirdVisualKind.Eagle) : visual;
        _phase = (identity % 997UL) / 997f;
        Root = new GameObject(_kind + " " + identity).transform;
        Root.SetParent(parent, false);
        try
        {
            _art = Acquire(_kind);
            GameObject model = UnityEngine.Object.Instantiate(_art.Prefab, Root);
            Animator animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = _art.Material;
            _graph = new ActorAnimationGraph(animator, "Bird " + identity, _art.Clips.Length);
            _mixer = _graph.BaseMixer;
            for (int i = 0; i < _art.Clips.Length; i++) _graph.AddBaseClip(i, _art.Clips[i]);
            _mixer.SetInputWeight(0, 1f);
            _graph.Evaluate();
            if (!_art.HasBounds)
            {
                _art.StandingBounds = MeasureStandingBounds(model);
                _art.HasBounds = true;
            }
            Bounds standing = _art.StandingBounds;
            float scale = height / Mathf.Max(0.01f, standing.size.y);
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = Vector3.up * (-height * .5f - standing.min.y * scale);
            for (int i = 0; i < _art.Clips.Length; i++)
                _mixer.GetInput(i).SetTime(_phase * _art.Clips[i].length);
            _graph.Evaluate();
            var definition = model.GetComponent<ProceduralRigDefinition>();
            if (definition != null)
                _pose = new ProceduralPoseRig(Root, definition) { SpineEnabled = false, ChainsEnabled = false };
        }
        catch { Dispose(); throw; }
    }

    static Art Acquire(BirdVisualKind kind)
    {
        if (_artCache.TryGetValue(kind, out Art cached)) { cached.References++; return cached; }
        string root = "Wildlife/Birds/" + kind;
        bool gull = kind == BirdVisualKind.Seagull;
        GameObject prefab = Resources.Load<GameObject>(gull ? root + "/Rig" : root + "Rig")
            ?? Resources.Load<GameObject>(gull ? root + "/Model" : root);
        if (prefab == null) throw new InvalidOperationException("Bird model is missing: " + root);
        AnimationClip[] clips;
        if (gull)
            clips = new[] { Resources.Load<AnimationClip>(root + "/FoldedIdle") ?? Resources.Load<AnimationClip>(root + "/Idle"), Resources.Load<AnimationClip>(root + "/Fly") };
        else
        {
            var imported = Resources.LoadAll<AnimationClip>(root);
            string[] names = kind == BirdVisualKind.Vulture ? new[] { "Vulture_Idle", "Vulture_Fly" }
                : new[] { "Eagle_Idle", "Eagle_Flying", "Eagle_Fly_Idle" };
            clips = new AnimationClip[names.Length];
            for (int i = 0; i < names.Length; i++) clips[i] = Array.Find(imported, clip => clip.name == names[i]);
        }
        foreach (AnimationClip clip in clips)
            if (clip == null) throw new InvalidOperationException("Bird animation is missing: " + root);
        Shader shader = Shader.Find("Planet/PropLit");
        if (shader == null) throw new InvalidOperationException("Bird shader is missing: Planet/PropLit");
        var art = new Art { Prefab = prefab, Clips = clips, References = 1,
            Material = new Material(shader) { name = kind + " shared bird material" } };
        art.Material.SetTexture("_BaseMap", Resources.Load<Texture2D>(gull ? root + "/Color"
            : kind == BirdVisualKind.Vulture ? "Wildlife/Birds/VultureColor" : "Wildlife/Birds/Atlas"));
        art.Material.SetColor("_BaseColor", Color.white);
        _artCache.Add(kind, art);
        return art;
    }

    public void Tick(bool resting, bool fleeing, float dt, Vector3? up = null,
        IGroundingProvider ground = null, CreatureBehaviour behaviour = CreatureBehaviour.Perch,
        Vector3? supportPoint = null, bool evaluatePose = true)
    {
        if (_graph == null || !_graph.IsValid() || _disposed || !float.IsFinite(dt) || dt < 0) return;
        _age += dt;
        _airWeight = Mathf.MoveTowards(_airWeight, resting && !fleeing ? 0f : 1f, dt * 5f);
        float glide = fleeing || _art.Clips.Length < 3 ? 0f : Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01((Mathf.Sin((_age * .5f + _phase * 6.283185f)) - .25f) * 2f));
        _mixer.SetInputWeight(0, 1f - _airWeight);
        _mixer.SetInputWeight(1, _airWeight * (1f - glide));
        if (_art.Clips.Length > 2) _mixer.SetInputWeight(2, _airWeight * glide);
        _graph.Advance(dt);
        _graph.BlendBaseWeights(dt);
        if (!evaluatePose)
        {
            _poseSkipped = true;
            return;
        }
        _pose?.RestoreAnimation();
        if (_poseSkipped) { _pose?.Reset(); _poseSkipped = false; }
        _graph.Evaluate();
        PoseEvaluations++;
        if (_pose == null) return;
        _pose.CaptureAnimation();
        bool grounded = resting && !fleeing && _airWeight <= .01f;
        Vector3 normal = (up ?? Root.up).normalized;
        if (supportPoint.HasValue)
        {
            _support.Point = supportPoint.Value;
            _support.Normal = normal;
            ground = _support;
        }
        Vector3 forward = Vector3.ProjectOnPlane(Root.forward, normal).normalized;
        Vector3 look = forward;
        if (behaviour is CreatureBehaviour.Feed or CreatureBehaviour.Drink)
            look = forward * .45f - normal * (.5f + .4f * Mathf.Sin(_age * 4f + _phase * 6.28f));
        else if (behaviour == CreatureBehaviour.Sleep)
            look = forward * .35f - normal * .65f + Root.right * .2f;
        _pose.LookEnabled = grounded && behaviour is CreatureBehaviour.Feed or CreatureBehaviour.Drink or CreatureBehaviour.Sleep;
        _pose.Tick(normal, look, grounded ? ground : null, 0f, 0f, 0f, dt);
    }

    // Shared by runtime normalization and editor-authored sole contacts.
    public static Bounds MeasureStandingBounds(GameObject model)
    {
        bool found = false;
        var bounds = new Bounds();
        foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            try
            {
                // Imported armatures carry scale compensation. Include it when baking, as animal authoring does.
                skin.BakeMesh(mesh, true);
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 point = model.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            finally { Release(mesh); }
        }
        if (!found || !float.IsFinite(bounds.size.y) || bounds.size.y <= 0f)
            throw new InvalidOperationException(model.name + " has no measurable skinned bird mesh.");
        return bounds;
    }

    sealed class SupportPlane : IGroundingProvider
    {
        public Vector3 Point, Normal;
        public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
        {
            result = new GroundResult(position + Normal * (Vector3.Dot(Point - position, Normal) + offset), Normal);
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _graph?.Dispose();
        if (Root != null) Release(Root.gameObject);
        if (_art != null && --_art.References == 0)
        {
            Release(_art.Material);
            _artCache.Remove(_kind);
        }
    }

    static void Release(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}
