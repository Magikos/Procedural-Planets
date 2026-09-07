using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Animated bird presentation, independent of authority and landing decisions.</summary>
public sealed class BirdAnimationView : IDisposable
{
    readonly PlayableGraph _graph;
    readonly AnimationMixerPlayable _mixer;
    readonly Material _material;
    readonly float _phase;
    float _age, _airWeight;
    public Transform Root { get; }

    public BirdAnimationView(Transform parent, ulong identity, float height)
    {
        var prefab = Resources.Load<GameObject>("Wildlife/Birds/Eagle");
        var clips = Resources.LoadAll<AnimationClip>("Wildlife/Birds/Eagle");
        AnimationClip Clip(string name) => clips.First(c => c.name == name);
        if (prefab == null) throw new InvalidOperationException("Bird model is missing.");
        Root = new GameObject("Bird " + identity).transform;
        Root.SetParent(parent, false);
        _phase = (identity % 997UL) / 997f;
        try
        {
            GameObject model = UnityEngine.Object.Instantiate(prefab, Root);
            Animator animator = model.GetComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _material = new Material(Shader.Find("Planet/PropLit"));
            _material.SetTexture("_BaseMap", Resources.Load<Texture2D>("Wildlife/Birds/Atlas"));
            _material.SetColor("_BaseColor", Color.white);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial = _material;
            _graph = PlayableGraph.Create("Bird " + identity);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "Bird pose", animator);
            _mixer = AnimationMixerPlayable.Create(_graph, 3);
            output.SetSourcePlayable(_mixer);
            string[] names = { "Eagle_Idle", "Eagle_Flying", "Eagle_Fly_Idle" };
            for (int i = 0; i < names.Length; i++)
            {
                AnimationClip clip = Clip(names[i]);
                var playable = AnimationClipPlayable.Create(_graph, clip);
                playable.SetTime(_phase * clip.length);
                _graph.Connect(playable, 0, _mixer, i);
            }
            _mixer.SetInputWeight(0, 1f);
            _graph.Play();
            _graph.Evaluate(0f);
            // Measure the standing pose, not import bounds that include the extended wings.
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh);
                Bounds bounds = mesh.bounds;
                float scale = height / Mathf.Max(0.01f, bounds.size.y);
                model.transform.localScale = Vector3.one * scale;
                model.transform.localPosition = Vector3.up * (-height * 0.5f - bounds.min.y * scale);
            }
            finally { Release(mesh); }
        }
        catch { Dispose(); throw; }
    }

    public void Tick(bool resting, bool fleeing, float dt)
    {
        if (!_graph.IsValid()) return;
        dt = Mathf.Max(0f, dt);
        _age += dt;
        _airWeight = Mathf.MoveTowards(_airWeight, resting ? 0f : 1f, dt * 5f);
        float glide = fleeing ? 0f : Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01((Mathf.Sin((_age * 0.5f + _phase * 6.283185f)) - 0.25f) * 2f));
        _mixer.SetInputWeight(0, 1f - _airWeight);
        _mixer.SetInputWeight(1, _airWeight * (1f - glide));
        _mixer.SetInputWeight(2, _airWeight * glide);
        _graph.Evaluate(dt);
    }

    public void Dispose()
    {
        if (_graph.IsValid()) _graph.Destroy();
        Release(_material);
        if (Root != null) Release(Root.gameObject);
    }

    static void Release(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}
