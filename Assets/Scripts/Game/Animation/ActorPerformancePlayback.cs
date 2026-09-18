using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed class ActorPerformancePlayback
{
    readonly ActorAnimationGraph _graph;
    readonly int _offset;
    readonly ActorAnimationPerformanceLibraryData _library;
    readonly AnimationClipPlayable[] _clips;
    readonly float[] _weights;
    readonly float[] _starts;
    int _performanceOffset;
    int _target = -1;
    int _activePhase = -1;
    bool _newAction;
    float _elapsed;
    float _duration;
    public float Weight { get; private set; }
    public ActorAnimationPerformance ActivePerformance { get; private set; }
    public string PhaseName { get; private set; }
    public float CurrentNormalizedTime { get; private set; }
    public bool RestartPending { get; private set; }

    public static int CountInputs(ActorAnimationPerformanceLibraryData library)
    {
        if (library == null) throw new ArgumentNullException(nameof(library));
        int count = 0;
        for (int i = 0; i < library.Count; i++) count = checked(count + library[i].PhaseCount);
        return checked(count * 2);
    }

    public ActorPerformancePlayback(ActorAnimationGraph graph, int offset, ActorAnimationPerformanceLibraryData library)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        int count = CountInputs(library);
        if (offset < 0 || offset > graph.BaseMixer.GetInputCount() - count) throw new ArgumentOutOfRangeException(nameof(offset));
        for (int i = 0; i < count; i++)
            if (graph.BaseMixer.GetInput(offset + i).IsValid()) throw new ArgumentException("Performance slots must be empty.", nameof(offset));
        _offset = offset;
        _clips = new AnimationClipPlayable[count];
        _weights = new float[count];
        _starts = new float[count];
        int slot = 0;
        for (int i = 0; i < library.Count; i++)
            for (int p = 0; p < library[i].PhaseCount; p++)
            for (int instance = 0; instance < 2; instance++)
            {
                _clips[slot] = graph.AddBaseClip(offset + slot, library[i][p].Clip);
                _clips[slot].SetSpeed(0d);
                slot++;
            }
    }

    public bool Begin(string action, string rig, float proficiency = 0f, string condition = "")
    {
        var selected = _library.Select(action, rig, proficiency, condition);
        ActivePerformance = selected;
        _newAction = true;
        _performanceOffset = 0;
        if (selected == null) return false;
        for (int i = 0; i < _library.Count; i++)
        {
            if (ReferenceEquals(_library[i], selected)) break;
            _performanceOffset += _library[i].PhaseCount * 2;
        }
        return true;
    }

    public void ApplyPhase(string phase, float normalizedProgress, float dt)
    {
        ValidateDelta(dt);
        if (!float.IsFinite(normalizedProgress) || normalizedProgress < 0f) throw new ArgumentOutOfRangeException(nameof(normalizedProgress));
        if (ActivePerformance == null) throw new InvalidOperationException("Begin a supported performance before applying phases.");
        var data = ActivePerformance.GetPhase(phase);
        if (data == null) throw new ArgumentException("The active performance does not contain this phase.", nameof(phase));
        int index = 0;
        while (!ReferenceEquals(ActivePerformance[index], data)) index++;
        int first = _performanceOffset + index * 2;
        if (_newAction || _activePhase != first || RestartPending)
        {
            int available = IsFree(first) ? first : IsFree(first + 1) ? first + 1 : -1;
            _newAction = false;
            _activePhase = first;
            if (available < 0)
            {
                int retained = _target == first || _target == first + 1 ? _target :
                    _weights[first] >= _weights[first + 1] ? first : first + 1;
                if (_target != retained) StartFade(retained, data.BlendSeconds);
                RestartPending = true;
                AdvanceFade(dt);
                return;
            }
            RestartPending = false;
            StartFade(available, data.BlendSeconds);
        }
        PhaseName = phase;
        float progress = data.Loop ? Mathf.Repeat(normalizedProgress, 1f) : Mathf.Clamp01(normalizedProgress);
        CurrentNormalizedTime = Mathf.Lerp(data.StartNormalized, data.EndNormalized, progress);
        _clips[_target].SetTime(CurrentNormalizedTime * data.Clip.length);
        AdvanceFade(dt);
    }

    public void Clear(float dt)
    {
        ValidateDelta(dt);
        ActivePerformance = null;
        PhaseName = null;
        RestartPending = _newAction = false;
        _activePhase = -1;
        if (_target != -1) StartFade(-1, _duration);
        AdvanceFade(dt);
    }

    public void Reset()
    {
        ActivePerformance = null;
        PhaseName = null;
        CurrentNormalizedTime = 0f;
        _target = -1;
        _activePhase = -1;
        RestartPending = _newAction = false;
        _elapsed = _duration = Weight = 0f;
        Array.Clear(_weights, 0, _weights.Length);
        Array.Clear(_starts, 0, _starts.Length);
        WriteWeights();
    }

    void StartFade(int target, float duration)
    {
        Array.Copy(_weights, _starts, _weights.Length);
        _target = target;
        _duration = duration;
        _elapsed = 0f;
    }

    // The graph's final fade can retain a visible contribution after our own weight reaches zero.
    bool IsFree(int index) => _weights[index] == 0f && _graph.BaseMixer.GetInputWeight(_offset + index) == 0f;

    void AdvanceFade(float dt)
    {
        _elapsed = Mathf.Min(_duration, _elapsed + dt);
        float progress = _duration > 0f ? _elapsed / _duration : 1f;
        Weight = 0f;
        for (int i = 0; i < _weights.Length; i++)
        {
            _weights[i] = Mathf.Lerp(_starts[i], i == _target ? 1f : 0f, progress);
            Weight += _weights[i];
        }
        Weight = Mathf.Clamp01(Weight);
        WriteWeights();
    }

    void WriteWeights()
    {
        for (int i = 0; i < _weights.Length; i++) _graph.BaseMixer.SetInputWeight(_offset + i, _weights[i]);
    }

    static void ValidateDelta(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
    }
}
