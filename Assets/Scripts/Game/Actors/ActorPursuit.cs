using System;
using System.Collections.Generic;
using UnityEngine;

public enum ActorPursuitRole { Pursue, FlankLeft, FlankRight, Intercept }

/// <summary>Authority-owned approach slots. The host supplies observed members and validates routes.</summary>
public sealed class ActorPursuit
{
    public readonly struct Member
    {
        public readonly ulong Id;
        public readonly Vector3 Position;
        public readonly float Speed, Reach, Radius;
        public bool Valid => Id != 0 && Finite(Position) && float.IsFinite(Speed) && Speed > 0 &&
            float.IsFinite(Reach) && Reach > 0 && float.IsFinite(Radius) && Radius > 0;
        public Member(ulong id, Vector3 position, float speed, float reach, float radius)
        {
            if (id == 0 || !Finite(position) || !float.IsFinite(speed) || speed <= 0 ||
                !float.IsFinite(reach) || reach <= 0 || !float.IsFinite(radius) || radius <= 0)
                throw new ArgumentOutOfRangeException(nameof(id));
            Id = id; Position = position; Speed = speed; Reach = reach; Radius = radius;
        }
    }
    readonly Dictionary<ulong, int> _slots = new();
    readonly List<ulong> _removed = new();
    ulong _target;
    Vector3 _heading;
    public void Clear() { _slots.Clear(); _target = 0; _heading = Vector3.zero; }
    public static ActorPursuitRole Role(int slot) => slot switch
    { 0 => ActorPursuitRole.Pursue, 1 => ActorPursuitRole.FlankLeft, 2 => ActorPursuitRole.FlankRight, _ => ActorPursuitRole.Intercept };

    public void Update(ulong target, Vector3 position, Vector3 velocity, Vector3 up, IReadOnlyList<Member> members)
    {
        if (target == 0 || !Finite(position) || !Finite(velocity) || !Finite(up) || up.sqrMagnitude < .001f)
            throw new ArgumentOutOfRangeException(nameof(target));
        if (members == null) throw new ArgumentNullException(nameof(members));
        for (int i = 0; i < members.Count; i++)
        {
            if (!members[i].Valid) throw new ArgumentException("Invalid pursuit member.", nameof(members));
            for (int j = 0; j < i; j++)
                if (members[i].Id == members[j].Id) throw new ArgumentException("Duplicate pursuit member.", nameof(members));
        }
        if (_target != target) { Clear(); _target = target; }
        up.Normalize();
        Vector3 motion = Vector3.ProjectOnPlane(velocity, up);
        if (motion.sqrMagnitude > .25f) _heading = motion.normalized;
        if (_heading.sqrMagnitude < .001f && members.Count > 0)
        {
            int first = 0;
            for (int i = 1; i < members.Count; i++) if (members[i].Id < members[first].Id) first = i;
            _heading = Vector3.ProjectOnPlane(position - members[first].Position, up).normalized;
        }
        if (_heading.sqrMagnitude < .001f)
            _heading = Vector3.Cross(up, Mathf.Abs(up.z) < .9f ? Vector3.forward : Vector3.right).normalized;
        _removed.Clear();
        foreach (var entry in _slots)
        {
            bool found = false;
            for (int i = 0; i < members.Count; i++) if (members[i].Id == entry.Key) { found = true; break; }
            if (!found) _removed.Add(entry.Key);
        }
        foreach (var id in _removed) _slots.Remove(id);
        // Keep occupied roles across small cost changes. Only vacancies or a new target cause reassignment.
        for (int slot = 0; slot < members.Count; slot++)
        {
            if (_slots.ContainsValue(slot)) continue;
            int best = -1; float cost = float.MaxValue;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (_slots.ContainsKey(m.Id) && slot != 0) continue;
                float travel = Vector3.Distance(m.Position, Goal(m, slot, position, velocity, up)) / m.Speed;
                if (travel < cost || travel == cost && (best < 0 || m.Id < members[best].Id)) { best = i; cost = travel; }
            }
            if (best >= 0) _slots[members[best].Id] = slot;
        }
    }

    public bool TryGet(ulong id, out int slot) => _slots.TryGetValue(id, out slot);

    public Vector3 Goal(in Member member, int slot, Vector3 target, Vector3 velocity, Vector3 up)
    {
        if (slot == 0) return target;
        Vector3 heading = Vector3.ProjectOnPlane(_heading, up).normalized;
        Vector3 right = Vector3.Cross(up, heading).normalized;
        float distance = Vector3.ProjectOnPlane(target - member.Position, up).magnitude;
        float far = Mathf.Clamp01((distance - member.Reach * 2f) / (member.Reach * 4f));
        float width = Mathf.Lerp(member.Reach * .75f, Mathf.Max(member.Reach * 2f, member.Radius * 4f), far);
        float leadTime = Mathf.Min(2f, distance / member.Speed) * far;
        Vector3 lead = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(velocity, up) * leadTime, Mathf.Min(member.Speed * 2f, distance * .35f));
        Vector3 offset = slot == 1 ? -right : slot == 2 ? right :
            Quaternion.AngleAxis((slot - 3) * 55f, up) * heading;
        return target + lead + offset * width;
    }

    public static bool BlocksStrike(Vector3 attacker, Vector3 target, Vector3 other, Vector3 up, float clearance)
    {
        Vector3 segment = Vector3.ProjectOnPlane(target - attacker, up);
        Vector3 delta = Vector3.ProjectOnPlane(other - attacker, up);
        if (segment.sqrMagnitude < .0001f) return false;
        float t = Vector3.Dot(delta, segment) / segment.sqrMagnitude;
        return t > 0 && t < 1 && (delta - segment * t).sqrMagnitude < clearance * clearance;
    }
    static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
}
