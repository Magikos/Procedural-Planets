using UnityEngine;

public enum WildlifeActivity { Travel, Visit, Rest, Escape }

/// <summary>Small wildlife uses the same utility selector as resident creatures, with only the goals it needs.</summary>
public sealed class WildlifeBrain
{
    readonly UtilityDecision<WildlifeActivity> _decision = new(0.1f, 0d);
    readonly UtilityCandidate<WildlifeActivity>[] _candidates = new UtilityCandidate<WildlifeActivity>[4];
    double _time;
    float _restRemaining;
    public WildlifeActivity Activity { get; private set; }
    public bool VisitComplete { get; private set; }

    public void Tick(float dt, bool threatened, bool hasTarget, bool arrived, float restSeconds)
    {
        if (!float.IsFinite(dt) || dt < 0f || !float.IsFinite(restSeconds) || restSeconds <= 0f)
            throw new System.ArgumentOutOfRangeException(nameof(dt));
        _time += dt;
        VisitComplete = false;
        if (Activity == WildlifeActivity.Rest)
        {
            _restRemaining -= dt;
            VisitComplete = _restRemaining <= 0f;
        }
        _candidates[0] = new(WildlifeActivity.Travel, 0.1f);
        _candidates[1] = new(WildlifeActivity.Visit, 0.5f, hasTarget && !VisitComplete);
        _candidates[2] = new(WildlifeActivity.Rest, 0.8f, hasTarget && arrived && !VisitComplete);
        _candidates[3] = new(WildlifeActivity.Escape, 1f, threatened, emergency: true);
        _decision.Select(_candidates, _time);
        WildlifeActivity next = _decision.Choice;
        if (next == WildlifeActivity.Rest && Activity != next) _restRemaining = restSeconds;
        Activity = next;
    }
}

/// <summary>A claim held by one animal. A removed or moved target interrupts the visit.</summary>
public sealed class WildlifeLandingReservation
{
    readonly EntityId _owner;
    WildlifeLandingTargets _sites;
    public WildlifeLandingTarget Target { get; private set; }
    public bool Active => _sites != null;

    public WildlifeLandingReservation(EntityId owner)
    {
        if (owner.IsNone) throw new System.ArgumentException("A landing claim needs an animal identity.", nameof(owner));
        _owner = owner;
    }

    public bool TryAcquire(WildlifeLandingTargets sites, Vector3 position, float radius, float clearance,
        WildlifeLandingUse use, ulong exclude = 0, System.Predicate<WildlifeLandingTarget> accepts = null)
    {
        if (Active) return Refresh();
        if (sites == null || !sites.TryFind(position, radius, clearance, use, _owner, out var target,
            candidate => candidate.Id != exclude && (accepts == null || accepts(candidate))) ||
            !sites.TryClaim(target.Id, _owner)) return false;
        _sites = sites;
        Target = target;
        return true;
    }

    public bool Refresh()
    {
        if (_sites != null && _sites.TryGet(Target.Id, out var current) &&
            current.Uses == Target.Uses && current.Clearance >= Target.Clearance &&
            (current.Position - Target.Position).sqrMagnitude < 0.0001f &&
            Vector3.Dot(current.Normal, Target.Normal) > 0.999f && _sites.TryClaim(Target.Id, _owner)) return true;
        Release();
        return false;
    }

    public void Release()
    {
        _sites?.Release(Target.Id, _owner);
        _sites = null;
        Target = default;
    }
}
