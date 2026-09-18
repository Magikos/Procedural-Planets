using System;
using UnityEngine;

public enum ActorStance { Standing, Crouching, Crawling }

/// <summary>Per-actor, gravity-relative capsule queries. No GameObject, input device, or update loop.</summary>
public sealed class ActorCollision
{
    public const float Skin = .025f;
    readonly int _mask;
    readonly float _height, _radius, _step;
    public ActorStance Stance { get; private set; }
    public float Height => Stance == ActorStance.Crawling ? _height * .32f : Stance == ActorStance.Crouching ? _height * .65f : _height;
    public float Radius => _radius;
    public float StepHeight => _step;

    public ActorCollision(int mask, float height = 1.7f, float radius = .23f, float stepHeight = .35f)
    {
        if (!float.IsFinite(height) || !float.IsFinite(radius) || !float.IsFinite(stepHeight) ||
            radius <= Skin || height * .32f < radius * 2f || stepHeight < 0f) throw new ArgumentOutOfRangeException(nameof(height));
        _mask = mask; _height = height; _radius = radius; _step = stepHeight;
    }

    void Capsule(Vector3 foot, Vector3 up, Vector3 forward, ActorStance stance, out Vector3 a, out Vector3 b)
    {
        float height = stance == ActorStance.Crawling ? _height * .32f : stance == ActorStance.Crouching ? _height * .65f : _height;
        if (stance == ActorStance.Crawling)
        {
            // Keep the support baseline identical across stances so the floor cannot block standing up.
            Vector3 center = foot + up * (_radius + Skin);
            a = center - forward * (_height * .3f); b = center + forward * (_height * .3f);
        }
        else { a = foot + up * (_radius + Skin); b = foot + up * (height - _radius); }
    }

    public bool Fits(Vector3 foot, Vector3 up, Vector3 forward, ActorStance stance)
    {
        if (!CharacterMath.IsFinite(foot) || !CharacterMath.IsFinite(up) || !CharacterMath.IsFinite(forward) ||
            Mathf.Abs(up.sqrMagnitude - 1f) > .01f || Mathf.Abs(forward.sqrMagnitude - 1f) > .01f)
            throw new ArgumentException("Collision queries require a finite position and unit axes.");
        Capsule(foot, up, forward, stance, out var a, out var b);
        return !Physics.CheckCapsule(a, b, _radius - Skin, _mask, QueryTriggerInteraction.Ignore);
    }

    public bool TryStance(ActorStance stance, CharacterPose pose)
    {
        if (!Enum.IsDefined(typeof(ActorStance), stance)) throw new ArgumentOutOfRangeException(nameof(stance));
        if (stance == Stance) return true;
        if (!Fits(pose.Position, pose.Up, pose.Forward, stance)) return false;
        Stance = stance; return true;
    }

    public bool Ray(Vector3 start, Vector3 direction, float distance, out RaycastHit hit)
        => Physics.Raycast(start, direction, out hit, distance, _mask, QueryTriggerInteraction.Ignore);

    public bool ClearSegment(Vector3 start, Vector3 end, Vector3 up, Vector3 forward)
    {
        Vector3 delta = end - start;
        return Fits(end, up, forward, Stance) && (delta.sqrMagnitude < 1e-10f ||
            !Sweep(start, up, forward, delta, out var hit) || hit.distance >= delta.magnitude);
    }

    public bool FitsCapsule(Vector3 a, Vector3 b, float radius)
    {
        if (!CharacterMath.IsFinite(a) || !CharacterMath.IsFinite(b) || !float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentException("Body capsule geometry must be finite with a positive radius.");
        return !Physics.CheckCapsule(a, b, radius, _mask, QueryTriggerInteraction.Ignore);
    }

    public bool ClearCapsuleSegment(Vector3 startA, Vector3 startB, float startRadius,
        Vector3 endA, Vector3 endB, float endRadius)
        => ClearCapsuleSegment(startA, startB, startRadius, endA, endB, endRadius, 4);

    bool ClearCapsuleSegment(Vector3 startA, Vector3 startB, float startRadius,
        Vector3 endA, Vector3 endB, float endRadius, int subdivisions)
    {
        if (!FitsCapsule(startA, startB, startRadius) || !FitsCapsule(endA, endB, endRadius)) return false;
        Vector3 delta = ((endA - startA) + (endB - startB)) * .5f;
        // Expand by endpoint deformation. This encloses every linearly interpolated capsule during the sweep.
        float radius = Mathf.Max(startRadius, endRadius) +
            Mathf.Max((endA - startA - delta).magnitude, (endB - startB - delta).magnitude);
        if (FitsCapsule(startA, startB, radius) &&
            (delta.sqrMagnitude < 1e-10f || !Physics.CapsuleCast(startA, startB, radius,
                delta.normalized, delta.magnitude, _mask, QueryTriggerInteraction.Ignore))) return true;
        // Resolve conservative deformation overlap with smaller swept envelopes, never unchecked samples.
        if (subdivisions == 0) return false;
        Vector3 midA = (startA + endA) * .5f, midB = (startB + endB) * .5f;
        float midRadius = (startRadius + endRadius) * .5f;
        return ClearCapsuleSegment(startA, startB, startRadius, midA, midB, midRadius, subdivisions - 1) &&
            ClearCapsuleSegment(midA, midB, midRadius, endA, endB, endRadius, subdivisions - 1);
    }

    public bool Support(Vector3 foot, Vector3 up, Vector3 forward, out GroundResult result)
    {
        result = default;
        if (!Sweep(foot + up * .1f, up, forward, -up * (_step + .1f + Skin), out var hit) || Vector3.Dot(hit.normal, up) < .05f) return false;
        result = new GroundResult(foot + up * (.1f - hit.distance + Skin), hit.normal); return true;
    }

    bool Sweep(Vector3 foot, Vector3 up, Vector3 forward, Vector3 delta, out RaycastHit hit)
    {
        Capsule(foot, up, forward, Stance, out var a, out var b);
        return Physics.CapsuleCast(a, b, _radius - Skin, delta.normalized, out hit, delta.magnitude + Skin,
            _mask, QueryTriggerInteraction.Ignore);
    }

    public Vector3 Move(Vector3 start, Vector3 desired, Vector3 up, Vector3 forward, bool grounded)
    {
        Vector3 remaining = desired - start, result = start;
        for (int i = 0; i < 3 && remaining.sqrMagnitude > 1e-10f; i++)
        {
            if (!Sweep(result, up, forward, remaining, out var hit)) return result + remaining;
            if (grounded && Stance != ActorStance.Crawling && Vector3.Dot(hit.normal, up) < .65f &&
                !Sweep(result, up, forward, up * _step, out _) &&
                !Sweep(result + up * _step, up, forward, remaining, out _) &&
                Sweep(result + up * _step + remaining, up, forward, -up * (_step + Skin * 2f), out var landing) &&
                Vector3.Dot(landing.normal, up) >= .05f)
                return result + up * (_step - Mathf.Max(0f, landing.distance - Skin)) + remaining;
            float travel = Mathf.Max(0f, hit.distance - Skin);
            Vector3 moved = remaining.normalized * Mathf.Min(travel, remaining.magnitude);
            result += moved;
            remaining = Vector3.ProjectOnPlane(remaining - moved, hit.normal);
            // Steep faces cannot turn forward input into upward motion.
            if (grounded && Vector3.Dot(hit.normal, up) < .65f)
                remaining -= up * Mathf.Max(0f, Vector3.Dot(remaining, up));
        }
        return result;
    }
}
