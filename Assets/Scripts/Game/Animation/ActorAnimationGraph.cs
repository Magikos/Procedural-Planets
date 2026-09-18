using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Shared manual animation graph. Gameplay owns decisions; callers own base weights and optional layers.</summary>
public sealed class ActorAnimationGraph : IDisposable
{
    readonly Unity.Profiling.ProfilerMarker _evaluateMarker = new("Actor.AnimationEvaluate");
    readonly PlayableGraph _graph;
    readonly AnimationPlayableOutput _output;
    readonly List<Layer> _layers = new();
    AnimationLayerMixerPlayable _layerMixer;
    bool _disposed;
    readonly float[] _baseWeights;
    public AnimationMixerPlayable BaseMixer { get; }

    public ActorAnimationGraph(Animator animator, string name, int baseClipCount)
    {
        if (animator == null) throw new ArgumentNullException(nameof(animator));
        if (baseClipCount < 1) throw new ArgumentOutOfRangeException(nameof(baseClipCount));
        animator.applyRootMotion = false;
        animator.fireEvents = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _graph = PlayableGraph.Create(name);
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        BaseMixer = AnimationMixerPlayable.Create(_graph, baseClipCount);
        _baseWeights = new float[baseClipCount];
        _baseWeights[0] = 1f;
        _output = AnimationPlayableOutput.Create(_graph, "Actor pose", animator);
        _output.SetSourcePlayable(BaseMixer);
        _graph.Play();
    }

    public bool IsValid() => !_disposed && _graph.IsValid();

    public AnimationClipPlayable AddBaseClip(int index, AnimationClip clip)
    {
        RequireAlive();
        if (clip == null) throw new ArgumentNullException(nameof(clip));
        if (index < 0 || index >= BaseMixer.GetInputCount()) throw new ArgumentOutOfRangeException(nameof(index));
        if (BaseMixer.GetInput(index).IsValid()) throw new InvalidOperationException("Base clip slot is already occupied.");
        var playable = AnimationClipPlayable.Create(_graph, clip);
        // Preserve humanoid authored foot goals during retargeting; procedural contacts run afterward.
        playable.SetApplyFootIK(true);
        playable.SetApplyPlayableIK(false);
        _graph.Connect(playable, 0, BaseMixer, index);
        return playable;
    }

    /// <summary>Additive clips must have a neutral reference pose; Unity applies their delta from that reference.</summary>
    public Layer AddLayer(AnimationClip clip, AvatarMask mask = null, bool additive = false)
    {
        RequireAlive();
        if (clip == null) throw new ArgumentNullException(nameof(clip));
        if (!_layerMixer.IsValid())
        {
            _layerMixer = AnimationLayerMixerPlayable.Create(_graph, 1);
            _graph.Connect(BaseMixer, 0, _layerMixer, 0);
            _layerMixer.SetInputWeight(0, 1f);
            _output.SetSourcePlayable(_layerMixer);
        }
        int index = _layers.Count + 1;
        _layerMixer.SetInputCount(index + 1);
        var playable = AnimationClipPlayable.Create(_graph, clip);
        // Preserve humanoid authored foot goals during retargeting; procedural contacts run afterward.
        playable.SetApplyFootIK(true);
        playable.SetApplyPlayableIK(false);
        _graph.Connect(playable, 0, _layerMixer, index);
        _layerMixer.SetLayerAdditive((uint)index, additive);
        if (mask != null) _layerMixer.SetLayerMaskFromAvatarMask((uint)index, mask);
        _layerMixer.SetInputWeight(index, 0f);
        var layer = new Layer(this, playable, index);
        _layers.Add(layer);
        return layer;
    }

    public void Advance(float dt)
    {
        RequireAlive();
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        AnimationGraphSampling.AdvanceInputs(BaseMixer, dt);
        foreach (Layer layer in _layers) layer.Advance(dt);
    }

    public void Evaluate()
    {
        RequireAlive();
        using var timing = _evaluateMarker.Auto();
        _graph.Evaluate(0f);
    }

    /// <summary>Crossfade final state weights, including interrupted actions, before sampling or skipping a pose.</summary>
    public void BlendBaseWeights(float dt)
    {
        RequireAlive();
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        float change = 0f;
        for (int i = 0; i < _baseWeights.Length; i++)
            change = Mathf.Max(change, Mathf.Abs(_baseWeights[i] - BaseMixer.GetInputWeight(i)));
        // Bound the largest weight change, and finish exactly. Existing slower fades retain their timing.
        float blend = change > 0f ? Mathf.Min(1f, dt * 8f / change) : 1f;
        float total = 0f;
        for (int i = 0; i < _baseWeights.Length; i++)
        {
            float target = BaseMixer.GetInputWeight(i);
            _baseWeights[i] = Mathf.Lerp(_baseWeights[i], target, blend);
            total += _baseWeights[i];
        }
        for (int i = 0; i < _baseWeights.Length; i++)
            BaseMixer.SetInputWeight(i, total > 0f ? _baseWeights[i] / total : i == 0 ? 1f : 0f);
    }

    public void ResetBlend()
    {
        RequireAlive();
        Array.Clear(_baseWeights, 0, _baseWeights.Length);
        _baseWeights[0] = 1f;
    }
    void RequireAlive() { if (!IsValid()) throw new ObjectDisposedException(nameof(ActorAnimationGraph)); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_graph.IsValid()) _graph.Destroy();
        _layers.Clear();
    }

    public sealed class Layer
    {
        readonly ActorAnimationGraph _owner;
        readonly AnimationClipPlayable _clip;
        readonly int _index;
        internal Layer(ActorAnimationGraph owner, AnimationClipPlayable clip, int index)
        { _owner = owner; _clip = clip; _index = index; }
        public float Weight
        {
            get { _owner.RequireAlive(); return _owner._layerMixer.GetInputWeight(_index); }
            set
            {
                _owner.RequireAlive();
                if (!float.IsFinite(value) || value < 0f || value > 1f) throw new ArgumentOutOfRangeException(nameof(value));
                _owner._layerMixer.SetInputWeight(_index, value);
            }
        }
        public double Time
        {
            get { _owner.RequireAlive(); return _clip.GetTime(); }
            set { _owner.RequireAlive(); if (!double.IsFinite(value) || value < 0d) throw new ArgumentOutOfRangeException(nameof(value)); _clip.SetTime(value); }
        }
        public double Speed
        {
            get { _owner.RequireAlive(); return _clip.GetSpeed(); }
            set { _owner.RequireAlive(); if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); _clip.SetSpeed(value); }
        }
        internal void Advance(float dt) => _clip.SetTime(_clip.GetTime() + _clip.GetSpeed() * dt);
    }
}
