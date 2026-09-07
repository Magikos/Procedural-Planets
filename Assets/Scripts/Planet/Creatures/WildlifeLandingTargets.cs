using System;
using System.Collections.Generic;
using UnityEngine;

[Flags]
public enum WildlifeLandingUse { Bird = 1, Butterfly = 2, Bee = 4 }

/// <summary>A contact point supplied by the world owner, with no scene object in authority code.</summary>
public readonly struct WildlifeLandingTarget
{
    public readonly ulong Id;
    public readonly Vector3 Position, Normal;
    public readonly float Clearance;
    public readonly WildlifeLandingUse Uses;

    public WildlifeLandingTarget(ulong id, Vector3 position, Vector3 normal, float clearance, WildlifeLandingUse uses)
    {
        Id = id;
        Position = position;
        Normal = normal.normalized;
        Clearance = clearance;
        Uses = uses;
    }

    public bool IsValid => Id != 0 && CharacterMath.IsFinite(Position) && CharacterMath.IsFinite(Normal) &&
        Normal.sqrMagnitude > 0.99f && !float.IsNaN(Clearance) && !float.IsInfinity(Clearance) && Clearance > 0f;
}

/// <summary>World-owned snapshots and exclusive claims. Disappearing sites lose their claims.</summary>
public sealed class WildlifeLandingTargets
{
    readonly Dictionary<ulong, WildlifeLandingTarget> _targets = new();
    readonly Dictionary<ulong, EntityId> _claims = new();
    readonly List<ulong> _removed = new();

    public void Publish(IReadOnlyList<WildlifeLandingTarget> targets)
    {
        _targets.Clear();
        if (targets != null)
            for (int i = 0; i < targets.Count; i++)
                if (targets[i].IsValid) _targets[targets[i].Id] = targets[i];
        _removed.Clear();
        foreach (var claim in _claims)
            if (!_targets.ContainsKey(claim.Key)) _removed.Add(claim.Key);
        foreach (ulong id in _removed) _claims.Remove(id);
    }

    public bool TryGet(ulong id, out WildlifeLandingTarget target) => _targets.TryGetValue(id, out target);

    public bool TryFind(Vector3 position, float radius, float clearance, WildlifeLandingUse use,
        EntityId owner, out WildlifeLandingTarget target, Predicate<WildlifeLandingTarget> accepts = null)
    {
        target = default;
        if (!CharacterMath.IsFinite(position) || !(radius > 0f) || float.IsInfinity(radius) ||
            !(clearance > 0f) || float.IsInfinity(clearance) || owner.IsNone) return false;
        float best = radius * radius;
        foreach (var pair in _targets)
        {
            WildlifeLandingTarget candidate = pair.Value;
            if ((candidate.Uses & use) == 0 || candidate.Clearance < clearance ||
                (_claims.TryGetValue(pair.Key, out EntityId claimant) && claimant != owner) ||
                (accepts != null && !accepts(candidate))) continue;
            float distance = (candidate.Position - position).sqrMagnitude;
            if (distance > best || (distance == best && target.Id != 0 && candidate.Id > target.Id)) continue;
            target = candidate;
            best = distance;
        }
        return target.Id != 0;
    }

    public bool TryClaim(ulong id, EntityId owner)
    {
        if (owner.IsNone || !_targets.ContainsKey(id) ||
            (_claims.TryGetValue(id, out EntityId claimant) && claimant != owner)) return false;
        _claims[id] = owner;
        return true;
    }

    public void Release(ulong id, EntityId owner)
    {
        if (_claims.TryGetValue(id, out EntityId claimant) && claimant == owner) _claims.Remove(id);
    }

    public void Clear() { _targets.Clear(); _claims.Clear(); }
}
