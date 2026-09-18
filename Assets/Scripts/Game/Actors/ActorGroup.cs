using System;
using System.Collections.Generic;

/// <summary>Group zero denotes independent actors, not an alliance between all unaffiliated actors.</summary>
public static class ActorGroup
{
    /// <summary>A bounded group action. Individual actors may leave for danger or urgent needs.</summary>
    public sealed class Hunt
    {
        public ActorPursuit Pursuit { get; } = new();
        public ulong Leader { get; private set; }
        public ulong Target { get; private set; }
        public double Until { get; private set; }
        public bool Active(double now) => Target != 0 && now < Until;
        public void Commit(ulong leader, ulong target, double now, double duration)
        {
            if (leader == 0 || target == 0 || !double.IsFinite(now) || now < 0 ||
                !double.IsFinite(duration) || duration <= 0 || !double.IsFinite(now + duration))
                throw new ArgumentOutOfRangeException(nameof(duration));
            if (Active(now)) return;
            if (Target != target) Pursuit.Clear();
            Leader = leader; Target = target; Until = now + duration;
        }
        public void Clear() { Leader = Target = 0; Until = 0; Pursuit.Clear(); }
    }
    public readonly struct Candidate
    {
        public readonly ulong MemberId;
        public readonly uint Group;
        public readonly double MemberDistance, TargetDistance;
        public readonly bool Eligible;
        public Candidate(ulong memberId, uint group, double memberDistance, double targetDistance, bool eligible)
        {
            if (!double.IsFinite(memberDistance) || memberDistance < 0 || !double.IsFinite(targetDistance) || targetDistance < 0)
                throw new ArgumentOutOfRangeException(nameof(memberDistance));
            MemberId = memberId; Group = group; MemberDistance = memberDistance; TargetDistance = targetDistance; Eligible = eligible;
        }
    }
    // The authority supplies observed candidates. No global pack knowledge or species-specific logic lives here.
    public static int SelectLeader(uint group, IReadOnlyList<Candidate> candidates, double communicationRadius, double targetRadius)
    {
        if (candidates == null) throw new ArgumentNullException(nameof(candidates));
        if (!double.IsFinite(communicationRadius) || communicationRadius <= 0 || !double.IsFinite(targetRadius) || targetRadius <= 0)
            throw new ArgumentOutOfRangeException(nameof(communicationRadius));
        int selected = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!candidate.Eligible || !Allied(group, candidate.Group) || candidate.MemberDistance > communicationRadius || candidate.TargetDistance > targetRadius) continue;
            if (selected < 0 || candidate.MemberId < candidates[selected].MemberId) selected = i;
        }
        return selected;
    }
    public static bool Allied(uint first, uint second) => first != 0 && first == second;

    public static double Support(uint observerGroup, uint otherGroup, double strength, double distance, double radius, bool willing)
    {
        if (!double.IsFinite(strength) || strength < 0 || !double.IsFinite(distance) || distance < 0 ||
            !double.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(strength));
        return willing && Allied(observerGroup, otherGroup) ? strength * Math.Clamp(1d - distance / radius, 0d, 1d) : 0d;
    }
}
