using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Presentation-only pose scheduling. Authority and clip time remain independent of this budget.</summary>
public sealed class ActorPoseCadence
{
    struct State
    {
        public Vector3 Position;
        public int Action;
        public bool Near, Visible;
        public double Next;
    }
    readonly Dictionary<ulong, State> _states = new();
    readonly float _near, _hysteresis, _visibleInterval, _hiddenInterval;
    double _time;
    public int Count => _states.Count;

    public ActorPoseCadence(float nearMeters = 30f, float hysteresisMeters = 5f,
        float visibleHz = 15f, float hiddenHz = 5f)
    {
        if (!float.IsFinite(nearMeters) || nearMeters <= 0f || !float.IsFinite(hysteresisMeters) || hysteresisMeters < 0f ||
            !float.IsFinite(visibleHz) || visibleHz <= 0f || !float.IsFinite(hiddenHz) || hiddenHz <= 0f)
            throw new ArgumentOutOfRangeException(nameof(nearMeters), "Cadence distances and rates must be finite and positive.");
        _near = nearMeters; _hysteresis = hysteresisMeters;
        _visibleInterval = 1f / visibleHz; _hiddenInterval = 1f / hiddenHz;
    }

    public void BeginFrame(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        _time += dt;
    }

    public bool ShouldEvaluate(ulong identity, Vector3 position, float distance, bool visible,
        bool hasObserver, int action, bool critical = false)
    {
        bool known = _states.TryGetValue(identity, out State state);
        bool near = !hasObserver || !float.IsFinite(distance) || distance <= (known && state.Near ? _near + _hysteresis : _near);
        bool force = !known || near || critical || action != state.Action || visible && !state.Visible ||
            (position - state.Position).sqrMagnitude > 25f;
        float interval = visible ? _visibleInterval : _hiddenInterval;
        bool due = force || _time >= state.Next;
        if (due)
        {
            // Align to identity-shifted time slots. Never catch up missed slots after a long frame.
            double phase = Phase(identity) * interval;
            state.Next = (Math.Floor((_time - phase) / interval) + 1d) * interval + phase;
        }
        state.Position = position; state.Action = action; state.Near = near; state.Visible = visible;
        _states[identity] = state;
        return due;
    }

    static double Phase(ulong value)
    {
        value ^= value >> 30; value *= 0xbf58476d1ce4e5b9UL;
        value ^= value >> 27; value *= 0x94d049bb133111ebUL;
        return ((value ^ (value >> 31)) & 0xffffffUL) / 16777216d;
    }
    public void Forget(ulong identity) => _states.Remove(identity);
    public void Clear() { _states.Clear(); _time = 0d; }
}
