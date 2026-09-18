using System;
using UnityEngine;

/// <summary>Authored, supported movement along a fixed straight beam.</summary>
public sealed class ActorBeam
{
    readonly ActorCollision _collision;
    BeamInteraction _beam;
    Vector3 _supportPosition, _supportScale, _start, _forward;
    Quaternion _supportRotation;
    ActorTraversalMotion[] _motions;
    float _elapsed, _from, _to = 1f;
    bool _turnPending;
    int _nextForwardHalf;
    public bool Active { get; private set; }
    public int Phase { get; private set; }
    public string PhaseName => BeamInteraction.PhaseName(Phase);
    public float Progress => _motions == null ? 0f : Mathf.Lerp(_from, _to,
        Mathf.Clamp01(_elapsed / (_motions[Phase].Duration * (_to - _from))));
    public CharacterPose Pose { get; private set; }
    public string Rejection { get; private set; }

    public ActorBeam(ActorCollision collision) => _collision = collision ?? throw new ArgumentNullException(nameof(collision));

    public bool Begin(BeamInteraction beam, CharacterPose pose)
    {
        if (Active || beam == null || !beam.Valid || !pose.IsFinite || Vector3.Angle(pose.Up, beam.transform.up) > 1f)
            return Reject("A valid beam and aligned character pose are required.");
        Vector3 forward = Vector3.Dot(pose.Forward, beam.transform.forward) >= 0f ? beam.transform.forward : -beam.transform.forward;
        if (beam.WallSideWalk && (beam.Wall == null || Vector3.Dot(forward, beam.transform.forward) < .99f))
            return Reject("Approach the marked ledge entrance with a supporting wall.");
        Vector3 entry = beam.Entry(forward);
        if (Vector3.Distance(pose.Position, entry) > .12f || Vector3.Dot(pose.Forward, forward) < .98f)
            return Reject("Approach the beam entrance and face along it.");
        _beam = beam; _motions = Array.ConvertAll(beam.Motions, m => m.CreateMotion());
        _supportPosition = beam.transform.position; _supportRotation = beam.transform.rotation; _supportScale = beam.transform.lossyScale;
        Pose = pose; _forward = forward; _turnPending = false; _nextForwardHalf = 0;
        Active = true;
        if (!Start(0)) { Active = false; return false; }
        return true;
    }

    public CharacterPose Tick(float move, bool turn, float dt)
    {
        if (!float.IsFinite(move) || !float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!Active) return Pose;
        if (_beam == null || !_beam.Valid || Vector3.Distance(_beam.transform.position, _supportPosition) > .001f ||
            Quaternion.Angle(_beam.transform.rotation, _supportRotation) > .1f || Vector3.Distance(_beam.transform.lossyScale, _supportScale) > .001f || !Supported(Pose.Position))
        { Active = false; Rejection = "Beam support changed or was lost."; return Pose; }
        _turnPending |= turn && !_beam.WallSideWalk;
        if (Phase == 5)
        {
            _elapsed = Mathf.Repeat(_elapsed + dt, _motions[5].Duration);
            if (_turnPending) { _turnPending = false; Start(6); }
            else if (Mathf.Abs(move) > .5f)
            {
                bool ahead = move > 0f;
                float endDistance = _motions[ahead ? 2 : 4].Sample(1f).Root.z;
                Vector3 end = Pose.Position + _forward * endDistance;
                bool exits = Mathf.Abs(Vector3.Dot(end - _beam.transform.position, _beam.transform.forward)) > _beam.Length * .5f + .05f;
                Start(exits ? ahead ? 2 : 4 : ahead ? 1 : 3);
            }
            return Pose;
        }
        float duration = _motions[Phase].Duration * (_to - _from);
        float endTime = Mathf.Min(duration, _elapsed + dt);
        while (_elapsed < endTime)
        {
            float nextTime = Mathf.Min(endTime, _elapsed + 1f / 60f);
            float phase = Mathf.Lerp(_from, _to, nextTime / duration);
            var next = Sample(phase);
            if (!Supported(next.Position) || !_collision.ClearSegment(Pose.Position, next.Position, Pose.Up, next.Forward))
            { Hold(); Rejection = "Beam movement is blocked or has no support."; return Pose; }
            _elapsed = nextTime; Pose = next;
        }
        if (_elapsed >= duration)
        {
            if (Phase == 2 || Phase == 4) Active = false;
            else { _forward = Pose.Forward; Hold(); }
        }
        return Pose;
    }

    bool Start(int phase)
    {
        _start = Pose.Position; _forward = Pose.Forward; Phase = phase; _elapsed = 0f;
        bool halfStep = phase == 1 && !_beam.WallSideWalk;
        _from = halfStep ? _nextForwardHalf * .5f : 0f; _to = halfStep ? _from + .5f : 1f;
        Vector3 previous = Pose.Position;
        for (int i = 1; i <= 60; i++)
        {
            var next = Sample(Mathf.Lerp(_from, _to, i / 60f));
            if (!Supported(next.Position) || !_collision.ClearSegment(previous, next.Position, Pose.Up, next.Forward))
            { Hold(); return Reject("Beam movement is blocked or has no support."); }
            previous = next.Position;
        }
        if (phase == 1) _nextForwardHalf = 1 - _nextForwardHalf;
        Rejection = null; return true;
    }

    CharacterPose Sample(float t)
    {
        var motion = _motions[Phase];
        var distance = motion.Sample(t).Root - motion.Sample(_from).Root;
        return new CharacterPose(_start + _forward * distance.z + Vector3.Cross(Pose.Up, _forward) * distance.x, Pose.Up,
            Quaternion.AngleAxis(motion.Sample(t).RootYawDegrees, Pose.Up) * _forward);
    }

    bool Supported(Vector3 position) => (!_beam.WallSideWalk || WallAvailable(position)) && _collision.Ray(position + Pose.Up * .15f, -Pose.Up, .25f, out var hit) &&
        Vector3.Dot(hit.normal, Pose.Up) > .95f && Mathf.Abs(Vector3.Dot(hit.point - position, Pose.Up)) < .06f;
    bool WallAvailable(Vector3 position)
    {
        if (_beam.Wall == null || !_beam.Wall.enabled || !_beam.Wall.gameObject.activeInHierarchy) return false;
        // Entry and exit use the broad platforms beyond the wall ends.
        if (Mathf.Abs(Vector3.Dot(position - _beam.transform.position, _beam.transform.forward)) > _beam.Length * .5f) return true;
        return _beam.Wall.Raycast(new Ray(position + Pose.Up * .8f, -_beam.transform.right), out _, .8f);
    }
    void Hold() { Phase = 5; _elapsed = _from = 0f; _to = 1f; }
    public void Cancel() { Active = false; _turnPending = false; }
    bool Reject(string reason) { Rejection = reason; return false; }
}
