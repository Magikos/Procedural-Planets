using System;
using UnityEngine;

/// <summary>Authority-side interaction clock. Markers do not depend on a rendered frame or IK success.</summary>
public sealed class ActorInteractionSession
{
    public ActorInteractionPlan Plan { get; private set; }
    public int PhaseIndex { get; private set; }
    public float Elapsed { get; private set; }
    bool _markerSent;
    bool _finishRequested;
    int _generation;
    public bool Active => Plan != null;
    public ActorInteractionPlan.Phase Phase => Active ? Plan[PhaseIndex] : null;
    public float Progress => Active ? Elapsed / Phase.Seconds : 0f;
    public event Action<string> Marker;

    public void Begin(ActorInteractionPlan plan, int phaseIndex = 0)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (phaseIndex < 0 || phaseIndex >= plan.Count) throw new ArgumentOutOfRangeException(nameof(phaseIndex));
        Plan = plan;
        _generation++;
        _finishRequested = false;
        PhaseIndex = phaseIndex; Elapsed = 0f; _markerSent = false;
    }

    public void Cancel() { Plan = null; Elapsed = 0f; _markerSent = false; _finishRequested = false; _generation++; }

    public bool Continue()
    {
        if (!Active || !Phase.WaitForInput) return false;
        if (Phase.RepeatUntilInput) { _finishRequested = true; return true; }
        Next(); return true;
    }

    public void Advance(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        while (Active)
        {
            int generation = _generation;
            bool repeating = Phase.RepeatUntilInput;
            float step = Phase.WaitForInput && !repeating ? dt : Mathf.Min(dt, Mathf.Max(0f, Phase.Seconds - Elapsed));
            Elapsed += step; dt -= step;
            if (!_markerSent && Elapsed >= Phase.Seconds * Phase.MarkerProgress)
            {
                _markerSent = true;
                if (Phase.Marker.Length > 0) Marker?.Invoke(Phase.Marker);
                // A marker consumer may cancel or replace this session.
                if (generation != _generation) return;
            }
            if (Phase.WaitForInput && !repeating || Elapsed < Phase.Seconds) return;
            if (repeating && !_finishRequested)
            {
                Elapsed = 0f; _markerSent = false;
                if (dt <= 0f) return;
                continue;
            }
            Next();
            if (dt <= 0f) return;
        }
    }

    void Next()
    {
        _generation++;
        _finishRequested = false;
        if (++PhaseIndex >= Plan.Count) { Cancel(); return; }
        Elapsed = 0f; _markerSent = false;
    }
}
