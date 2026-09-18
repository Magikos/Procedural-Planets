using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Independent stage A at the original clip's 1x rate. This diagnostic does not follow production phase.</summary>
public sealed class AuthoredAnimationReference : IDisposable
{
    ActorAnimationGraph _graph;
    AnimationClipPlayable _playable;
    bool _disposed;
    readonly Vector3 _animatorPosition;
    readonly Quaternion _animatorRotation;

    public GameObject Root { get; private set; }
    public Animator Animator { get; private set; }
    public AnimationClip Clip { get; }
    public string ClipAssetPath { get; }
    public string RigAssetPath { get; }
    public bool UsesOriginalRig { get; }
    public double ElapsedSeconds { get; private set; }
    public double ClipTimeSeconds { get; private set; }
    public string Label => UsesOriginalRig ? "A: ORIGINAL CLIP / ORIGINAL RIG / 1x" : "A: ORIGINAL CLIP / RETARGETED RIG / 1x";
    public string RootMotionConvention => "Animator transform root fixed; baked body translation retained; extracted Animator root delta not applied.";

    /// <param name="usesOriginalRig">True only when the supplied rig is verified as the artist's original rig.</param>
    public AuthoredAnimationReference(GameObject rigPrefab, AnimationClip originalClip, bool usesOriginalRig)
    {
        if (rigPrefab == null) throw new ArgumentNullException(nameof(rigPrefab));
        if (originalClip == null) throw new ArgumentNullException(nameof(originalClip));
        if (originalClip.legacy) throw new ArgumentException("The reference requires a non-legacy animation clip.", nameof(originalClip));
        if (!float.IsFinite(originalClip.length) || originalClip.length <= 0f)
            throw new ArgumentException("The reference requires a clip with a positive finite duration.", nameof(originalClip));
        var animators = rigPrefab.GetComponentsInChildren<Animator>(true);
        if (animators.Length != 1)
            throw new ArgumentException("The reference rig must contain exactly one Animator.", nameof(rigPrefab));
        if (originalClip.isHumanMotion && (animators[0].avatar == null || !animators[0].avatar.isValid || !animators[0].avatar.isHuman))
            throw new ArgumentException("A humanoid clip requires a valid humanoid avatar.", nameof(rigPrefab));

        Clip = originalClip;
        ClipAssetPath = AssetDatabase.GetAssetPath(originalClip);
        RigAssetPath = AssetDatabase.GetAssetPath(rigPrefab);
        UsesOriginalRig = usesOriginalRig;
        try
        {
            Root = new GameObject(Label) { hideFlags = HideFlags.HideAndDontSave };
            // Keep the clone inactive until competing writers and physics have been disabled.
            Root.SetActive(false);
            var model = UnityEngine.Object.Instantiate(rigPrefab, Root.transform);
            model.SetActive(true);
            Animator = model.GetComponentInChildren<Animator>(true);
            foreach (var behaviour in model.GetComponentsInChildren<Behaviour>(true))
                behaviour.enabled = behaviour == Animator;
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            Animator.runtimeAnimatorController = null;
            _animatorPosition = Animator.transform.localPosition;
            _animatorRotation = Animator.transform.localRotation;
            Root.SetActive(true);
            _graph = new ActorAnimationGraph(Animator, "Independent authored reference", 1);
            _playable = _graph.AddBaseClip(0, originalClip);
            _playable.SetApplyFootIK(false);
            _playable.SetApplyPlayableIK(false);
            _playable.SetSpeed(0d);
            _graph.BaseMixer.SetInputWeight(0, 1f);
            Sample(0d);
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Absolute diagnostic scrub; intentional immediate sampling is not a gameplay transition.</summary>
    public void Sample(double elapsedSeconds)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AuthoredAnimationReference));
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        ElapsedSeconds = elapsedSeconds;
        ClipTimeSeconds = Clip.isLooping ? elapsedSeconds % Clip.length : Math.Min(elapsedSeconds, Clip.length);
        // Setting twice clears a previous evaluation's time delta when scrubbing backwards.
        _playable.SetTime(ClipTimeSeconds);
        _playable.SetTime(ClipTimeSeconds);
        _graph.Evaluate();
        Animator.transform.SetLocalPositionAndRotation(_animatorPosition, _animatorRotation);
    }

    void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode) Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        _graph?.Dispose();
        if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
        Root = null;
        Animator = null;
    }
}
