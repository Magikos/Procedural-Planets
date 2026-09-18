using System;
using UnityEngine;

/// <summary>Timed presentation envelope. Contact is an observation, never a gameplay authorization.</summary>
public sealed class ActorReachMotion
{
    readonly float _reachSeconds, _holdSeconds, _releaseSeconds, _timeout;
    float _elapsed, _held;
    bool _releasing;
    Vector3 _target;
    public bool Active { get; private set; }
    public bool Contacted { get; private set; }
    public float Weight { get; private set; }
    public float ContactProgress => Contacted ? Mathf.Clamp01(_held / _holdSeconds) : 0f;
    public InteractionPoseTarget? Target => Active ? new InteractionPoseTarget(_target, null, Weight) : null;

    public ActorReachMotion(float reachSeconds = .35f, float holdSeconds = .8f, float releaseSeconds = .3f, float timeout = 2f)
    {
        foreach (float value in new[] { reachSeconds, holdSeconds, releaseSeconds, timeout })
            if (!float.IsFinite(value) || value <= 0f) throw new ArgumentOutOfRangeException(nameof(reachSeconds));
        _reachSeconds = reachSeconds; _holdSeconds = holdSeconds; _releaseSeconds = releaseSeconds; _timeout = timeout;
    }

    public bool Begin(Vector3 target)
    {
        if (!CharacterMath.IsFinite(target)) throw new ArgumentException("Reach target must be finite.", nameof(target));
        if (Active) return false;
        _target = target; _elapsed = _held = Weight = 0f;
        _releasing = Contacted = false; Active = true; return true;
    }

    public void Cancel() => _releasing = true;
    public void Reset() { Active = Contacted = _releasing = false; _elapsed = _held = Weight = 0f; }

    public void Advance(float dt, Vector3? target, bool allowed = true)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!Active) return;
        if (!allowed || !target.HasValue || !CharacterMath.IsFinite(target.Value)) Cancel();
        if (!_releasing)
        {
            _target = target.Value;
            _elapsed += dt;
            if (Contacted) _held = Mathf.Min(_holdSeconds, _held + dt);
            if (Contacted ? _held >= _holdSeconds : _elapsed >= _timeout) Cancel();
        }
        Weight = Mathf.MoveTowards(Weight, _releasing ? 0f : 1f, dt / (_releasing ? _releaseSeconds : _reachSeconds));
        if (_releasing && Weight <= 0f) Active = false;
    }

    /// <summary>Call after pose evaluation. Returns true only on the first confirmed contact of this motion.</summary>
    public bool ObserveContact(Vector3 hand, float tolerance)
    {
        if (!CharacterMath.IsFinite(hand) || !float.IsFinite(tolerance) || tolerance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (!Active || _releasing || Contacted || Weight < .99f || Vector3.Distance(hand, _target) > tolerance) return false;
        Contacted = true; return true;
    }
}
