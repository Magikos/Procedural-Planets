using System;
using UnityEngine;

/// <summary>Committed authored evasive steps with swept clearance and supported landing.</summary>
public sealed class ActorDodge
{
    readonly ActorCollision _collision;
    ActorTraversalMotion _motion;
    CharacterPose _start;
    float _elapsed;
    float _locomotionExit;
    public bool Active { get; private set; }
    public int Direction { get; private set; }
    public string PhaseName => Direction switch { 0 => "Left", 1 => "Right", 2 => "Backward", _ => "ForwardRoll" };
    public float Progress => _motion == null ? 0f : Mathf.Clamp01(_elapsed / _motion.Duration);
    public CharacterPose Pose { get; private set; }
    public string Status { get; private set; } = "Ready";

    public ActorDodge(ActorCollision collision) => _collision = collision ?? throw new ArgumentNullException(nameof(collision));

    public bool Begin(ActorTraversalMotion motion, int direction, CharacterPose pose, float locomotionExitNormalized = 1f)
    {
        if (Active) return false;
        if (motion == null || direction < 0 || direction > 3 || !pose.IsFinite ||
            !float.IsFinite(locomotionExitNormalized) || locomotionExitNormalized < 0f || locomotionExitNormalized > 1f ||
            _collision.Stance != ActorStance.Standing) return Reject("Dodge requires a valid standing motion.");
        _motion = motion; _start = Pose = pose; Direction = direction; _elapsed = 0f;
        _locomotionExit = locomotionExitNormalized;
        Vector3 previous = pose.Position;
        int count = Mathf.CeilToInt(motion.Duration * 60f);
        for (int i = 0; i <= count; i++)
        {
            Vector3 next = Sample(i / (float)count);
            if (!Supported(next) || !Clear(previous, next))
                return Reject("Dodge route is blocked or has no support.");
            previous = next;
        }
        Active = true; Status = "Dodging"; return true;
    }

    public CharacterPose Tick(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!Active) return Pose;
        float end = Mathf.Min(_motion.Duration, _elapsed + dt);
        while (_elapsed < end)
        {
            float nextTime = Mathf.Min(end, _elapsed + 1f / 60f);
            Vector3 next = Sample(nextTime / _motion.Duration);
            if (!Supported(next) || !Clear(Pose.Position, next))
            {
                Active = false; Status = "Dodge interrupted by changed clearance or support.";
                return Pose;
            }
            _elapsed = nextTime;
            Pose = new CharacterPose(next, _start.Up, _start.Forward);
        }
        if (_elapsed >= _motion.Duration) { Active = false; Status = "Ready"; }
        return Pose;
    }

    Vector3 Sample(float phase)
    {
        var root = _motion.Sample(phase).Root;
        return _start.Position + Vector3.Cross(_start.Up, _start.Forward) * root.x + _start.Forward * root.z;
    }

    bool Supported(Vector3 position) => _collision.Ray(position + _start.Up * .15f, -_start.Up, .25f, out var hit) &&
        Vector3.Dot(hit.normal, _start.Up) > .95f && Mathf.Abs(Vector3.Dot(hit.point - position, _start.Up)) < .06f;

    bool Clear(Vector3 from, Vector3 to)
    {
        if (!_collision.ClearSegment(from, to, _start.Up, _start.Forward)) return false;
        if (Direction != 3) return true;
        // Keep standing clearance during the roll and reserve room for the extended body at either end.
        Vector3 reach = _start.Forward * (_collision.Height * .4f);
        return _collision.ClearSegment(from + reach, to + reach, _start.Up, _start.Forward) &&
            _collision.ClearSegment(from - reach, to - reach, _start.Up, _start.Forward);
    }

    public bool TryResumeLocomotion()
    {
        if (!Active || Progress < _locomotionExit || !Supported(Pose.Position)) return false;
        Active = false;
        Status = "Locomotion resumed from supported recovery";
        return true;
    }

    // A committed step completes its support transfer. Escape cancels no pose midway through flight.
    public void RequestStop() { if (Active) Status = "Completing committed dodge"; }
    public void Reset() { Active = false; _motion = null; _elapsed = 0f; Status = "Ready"; }
    bool Reject(string reason) { Status = reason; return false; }
}
