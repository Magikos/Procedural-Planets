using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

/// <summary>Editor-only production playback reference. Copies timing and blends without procedural pose writers.</summary>
public sealed class HumanInteractionComparison
{
    public static readonly Vector3 Offset = Vector3.right * 20f;
    public static HumanInteractionComparison Current { get; private set; }
    public Transform ReferenceRoot => _dummy != null ? _dummy.transform : null;
    GameObject _root, _floor;
    Material _overlayMaterial;
    bool _overlay;
    int _lastFrame = -1;
    HumanInteractionReview _review;
    GameObject _dummy;
    Transform _sourceProp, _referenceProp, _sourceHinge, _referenceHinge;
    ActorAnimationGraph _graph;
    AnimationClipPlayable[] _clips;

    [MenuItem("Tools/Actors/Human/Show Source Clip Comparison")]
    public static void Show()
    {
        var review = UnityEngine.Object.FindAnyObjectByType<HumanInteractionReview>();
        if (!EditorApplication.isPlaying || review == null || review.Actor.View == null)
            throw new InvalidOperationException("Enter Play mode with the review character initialized first.");
        Hide();
        Current = new HumanInteractionComparison();
        try { Current.Initialize(review); }
        catch { Hide(); throw; }
        EditorApplication.update += Current.Update;
        AssemblyReloadEvents.beforeAssemblyReload += Hide;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/Actors/Human/Show Green Source Overlay")]
    public static void ShowOverlay()
    {
        Show();
        Current._overlay = true;
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) { Hide(); throw new InvalidOperationException("The source overlay requires the URP Unlit shader."); }
        var material = Current._overlayMaterial = new Material(shader) { name = "Source reference green", hideFlags = HideFlags.HideAndDontSave };
        material.SetColor("_BaseColor", new Color(.05f, 1f, .1f, .45f));
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        foreach (var renderer in Current._dummy.GetComponentsInChildren<Renderer>())
        {
            renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        if (Current._referenceProp != null) Current._referenceProp.gameObject.SetActive(false);
        if (Current._floor != null) Current._floor.SetActive(false);
        Current.Sample(1f / 60f);
    }

    [MenuItem("Tools/Actors/Human/Hide Source Clip Comparison")]
    public static void Hide()
    {
        if (Current == null) return;
        EditorApplication.update -= Current.Update;
        AssemblyReloadEvents.beforeAssemblyReload -= Hide;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Current._graph?.Dispose();
        if (Current._overlayMaterial != null) UnityEngine.Object.DestroyImmediate(Current._overlayMaterial);
        if (Current._root != null) UnityEngine.Object.DestroyImmediate(Current._root);
        Current = null;
    }

    void Initialize(HumanInteractionReview review)
    {
        _review = review;
        _root = new GameObject("Source clip comparison (editor only)");
        var transform = _root.transform;
        _dummy = UnityEngine.Object.Instantiate(review.Actor.CharacterPrefab, transform);
        _dummy.name = "B - PRODUCTION PLAYBACK WITHOUT CORRECTIONS";
        _dummy.transform.localScale = review.Actor.Actor.lossyScale;
        // Paused, manually evaluated review cameras must display the sampled bones, not cached skin matrices.
        foreach (var skin in _dummy.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            skin.forceMatrixRecalculationPerRender = true;
            skin.updateWhenOffscreen = true;
        }
        var source = review.Actor.View.Graph.BaseMixer;
        _graph = new ActorAnimationGraph(_dummy.GetComponent<Animator>(), "Production playback reference", source.GetInputCount());
        _clips = new AnimationClipPlayable[source.GetInputCount()];
        for (int i = 0; i < _clips.Length; i++)
        {
            var input = source.GetInput(i);
            if (!input.IsValid()) continue;
            if (input.GetPlayableType() != typeof(AnimationClipPlayable))
                throw new InvalidOperationException("Source comparison requires clip inputs in the actor base mixer.");
            _clips[i] = _graph.AddBaseClip(i, ((AnimationClipPlayable)input).GetAnimationClip());
        }
        if (review.Selected >= 0)
        {
            var target = review.Targets[review.Selected];
            _sourceProp = (target.Hinge != null ? target.Hinge : target.Contact).root;
            _referenceProp = UnityEngine.Object.Instantiate(_sourceProp.gameObject, transform).transform;
            _sourceHinge = target.Hinge;
            if (_sourceHinge != null)
            {
                string path = AnimationUtility.CalculateTransformPath(_sourceHinge, _sourceProp);
                _referenceHinge = path.Length == 0 ? _referenceProp : _referenceProp.Find(path);
            }
        }
        var floor = GameObject.Find("Interaction room floor");
        if (floor != null) _floor = UnityEngine.Object.Instantiate(floor, floor.transform.position + Offset, floor.transform.rotation, transform);
        foreach (var collider in _root.GetComponentsInChildren<Collider>()) collider.enabled = false;
        Sample(1f / 60f);
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) Hide();
    }

    void Update()
    {
        if (EditorApplication.isPaused || Time.frameCount == _lastFrame) return;
        _lastFrame = Time.frameCount;
        Sample(Time.deltaTime);
    }

    public void Sample(float dt)
    {
        if (_graph == null || _review == null || _review.Actor.Actor == null || dt <= 0f) return;
        _dummy.transform.SetPositionAndRotation(_review.Actor.Actor.position + (_overlay ? Vector3.zero : Offset), _review.Actor.Actor.rotation);
        if (_referenceProp != null && _sourceProp != null) _referenceProp.SetPositionAndRotation(_sourceProp.position + Offset, _sourceProp.rotation);
        if (_referenceHinge != null) _referenceHinge.localRotation = _sourceHinge.localRotation;
        var source = _review.Actor.View.Graph.BaseMixer;
        for (int i = 0; i < _clips.Length; i++)
        {
            if (!_clips[i].IsValid()) continue;
            _clips[i].SetTime(source.GetInput(i).GetTime());
            _graph.BaseMixer.SetInputWeight(i, source.GetInputWeight(i));
        }
        _graph.Evaluate();
    }

}

