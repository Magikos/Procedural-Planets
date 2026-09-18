using System;
using UnityEngine;

/// <summary>Fixed-rope movement authority. The shared performance player owns the visible pose.</summary>
public sealed class ActorRope
{
    readonly ActorCollision _collision;
    RopeInteraction _rope;
    ActorTraversalMotion[] _motions;
    Vector3 _position, _scale, _start;
    Quaternion _rotation;
    float _elapsed, _height;
    public bool Active { get; private set; }
    public int Phase { get; private set; }
    public string PhaseName => RopeInteraction.PhaseName(Phase);
    public float Progress => _motions == null ? 0 : Mathf.Clamp01(_elapsed / _motions[Phase].Duration);
    public CharacterPose Pose { get; private set; }
    public string Rejection { get; private set; }
    public RopeInteraction Target => _rope;

    public ActorRope(ActorCollision collision) => _collision = collision ?? throw new ArgumentNullException(nameof(collision));

    public bool Begin(RopeInteraction rope, CharacterPose pose)
    {
        if (Active || rope == null || !rope.Valid || !pose.IsFinite || Vector3.Angle(pose.Up, rope.transform.up) > 1f)
            return false;
        if (Vector3.Distance(pose.Position, rope.Entry) > .12f || Vector3.Dot(pose.Forward, rope.transform.forward) < .98f)
        { Rejection = "Approach the rope entrance and face the rope."; return false; }
        _rope = rope; _motions = Array.ConvertAll(rope.Motions, m => m.CreateMotion());
        _position = rope.transform.position; _rotation = rope.transform.rotation; _scale = rope.transform.lossyScale; _height = rope.Height;
        Pose = pose; Active = true;
        if (!Start(0)) { Active = false; return false; }
        return true;
    }

    public CharacterPose Tick(float move, float dt)
    {
        if (!float.IsFinite(move) || !float.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!Active) return Pose;
        if (_rope == null || !_rope.Valid || Vector3.Distance(_position, _rope.transform.position) > .001f ||
            Quaternion.Angle(_rotation, _rope.transform.rotation) > .1f || Vector3.Distance(_scale, _rope.transform.lossyScale) > .001f ||
            Mathf.Abs(_height - _rope.Height) > .001f)
        { Cancel(); Rejection = "Rope support changed or was lost."; return Pose; }
        if (Phase == 3)
        {
            _elapsed = Mathf.Repeat(_elapsed + dt, _motions[3].Duration);
            if (Mathf.Abs(move) > .5f)
            {
                float height = Vector3.Dot(Pose.Position - _rope.Bottom, Pose.Up);
                if (move < 0 && height < .02f) Start(4);
                else
                {
                    int phase = move > 0 ? 1 : 2;
                    float end = height + _motions[phase].Sample(1).Root.y;
                    if (end > _height - 1.9f || end < -.02f) Rejection = "No complete supported climb step remains. Reverse or release.";
                    else Start(phase);
                }
            }
            return Pose;
        }
        float endTime = Mathf.Min(_motions[Phase].Duration, _elapsed + dt);
        while (_elapsed < endTime)
        {
            float nextTime = Mathf.Min(endTime, _elapsed + 1f / 60f);
            var next = Sample(nextTime / _motions[Phase].Duration);
            if (!_collision.ClearSegment(Pose.Position, next.Position, Pose.Up, Pose.Forward))
            { Hold(); Rejection = "Rope movement is blocked."; return Pose; }
            _elapsed = nextTime; Pose = next;
        }
        if (_elapsed >= _motions[Phase].Duration)
        {
            if (Phase == 4) Active = false;
            else Hold();
        }
        return Pose;
    }

    bool Start(int phase)
    {
        Phase = phase; _elapsed = 0; _start = Pose.Position;
        Vector3 previous = _start;
        for (int i = 1; i <= 60; i++)
        {
            var next = Sample(i / 60f);
            if (!_collision.ClearSegment(previous, next.Position, Pose.Up, Pose.Forward) ||
                phase == 4 && i == 60 && !_collision.Ray(next.Position + Pose.Up * .15f, -Pose.Up, .25f, out _))
            { Hold(); Rejection = "Rope movement or ground exit is blocked."; return false; }
            previous = next.Position;
        }
        Rejection = null; return true;
    }

    CharacterPose Sample(float t)
    {
        var d = _motions[Phase].Sample(t).Root;
        return new CharacterPose(_start + Pose.Up * d.y + Pose.Forward * d.z, Pose.Up, Pose.Forward);
    }
    void Hold() { Phase = 3; _elapsed = 0; }
    public void Cancel() => Active = false;
}
