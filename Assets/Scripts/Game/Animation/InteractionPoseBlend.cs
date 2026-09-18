using System;
using UnityEngine;

/// <summary>Retains an outgoing target while its influence fades and bounds target changes during a reach.</summary>
public sealed class InteractionPoseBlend
{
    InteractionPoseTarget? _current;
    Vector3? _previousAuthoredPosition;
    Quaternion _previousFrameRotation;
    public void Reset() { _current = null; _previousAuthoredPosition = null; }

    /// <summary>Blend corrections while carrying the outgoing offset with the authored pose and actor frame.</summary>
    public InteractionPoseTarget? Tick(InteractionPoseTarget? requested, float dt, Vector3 authoredPosition, Quaternion frameRotation)
    {
        if (!CharacterMath.IsFinite(authoredPosition) || !float.IsFinite(dt) || dt < 0f)
            throw new ArgumentOutOfRangeException(nameof(authoredPosition));
        if (_current.HasValue && _previousAuthoredPosition.HasValue && (!requested.HasValue || requested.Value.FollowAuthoredMotion))
        {
            var current = _current.Value;
            Vector3 offset = Quaternion.Inverse(_previousFrameRotation) * (current.Position - _previousAuthoredPosition.Value);
            _current = new InteractionPoseTarget(authoredPosition + frameRotation * offset, current.Rotation,
                current.Weight, current.UseContact, current.GripRadius, current.BendDirection, current.FollowAuthoredMotion, current.PreserveAuthoredContactJoints);
        }
        _previousAuthoredPosition = authoredPosition;
        _previousFrameRotation = frameRotation;
        return Tick(requested, dt);
    }

    public InteractionPoseTarget? Tick(InteractionPoseTarget? requested, float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!_current.HasValue)
        {
            if (!requested.HasValue) return null;
            var initial = requested.Value;
            _current = new InteractionPoseTarget(initial.Position, initial.Rotation, 0f, initial.UseContact, initial.GripRadius, initial.BendDirection, initial.FollowAuthoredMotion, initial.PreserveAuthoredContactJoints);
        }
        var current = _current.Value;
        bool compatible = requested.HasValue && requested.Value.UseContact == current.UseContact
            && requested.Value.Rotation.HasValue == current.Rotation.HasValue
            && requested.Value.BendDirection.HasValue == current.BendDirection.HasValue
            && requested.Value.PreserveAuthoredContactJoints == current.PreserveAuthoredContactJoints;
        float desiredWeight = compatible ? requested.Value.Weight : 0f;
        // Release a planted hand more slowly while the underlying body pose also recovers.
        float weight = Mathf.MoveTowards(current.Weight, desiredWeight, dt * (desiredWeight < current.Weight ? 3f : 6f));
        Vector3 position = current.Position;
        Quaternion? rotation = current.Rotation;
        float radius = current.GripRadius;
        Vector3? bend = current.BendDirection;
        if (compatible)
        {
            position = Vector3.MoveTowards(position, requested.Value.Position, dt * 3f);
            if (rotation.HasValue) rotation = Quaternion.RotateTowards(rotation.Value, requested.Value.Rotation.Value, dt * 540f);
            radius = Mathf.MoveTowards(radius, requested.Value.GripRadius, dt);
            if (bend.HasValue) bend = Vector3.RotateTowards(bend.Value, requested.Value.BendDirection.Value, dt * Mathf.PI, 0f);
        }
        _current = weight > 0f ? new InteractionPoseTarget(position, rotation, weight, current.UseContact, radius, bend,
            compatible ? requested.Value.FollowAuthoredMotion : current.FollowAuthoredMotion, current.PreserveAuthoredContactJoints) : null;
        return _current;
    }
}
