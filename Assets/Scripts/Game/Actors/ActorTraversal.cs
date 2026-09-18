using System;
using UnityEngine;

public enum TraversalKind { None, StepUp, Vault, JumpGrab, Hanging, ClimbUp, DropToHang, HangLeft, HangRight, HangOutwardLeft, HangOutwardRight, HangInwardLeft, HangInwardRight }

/// <summary>Collision-checked traversal trajectory. The actor's existing brain or input owner requests each action.</summary>
public sealed class ActorTraversal
{
    readonly ActorCollision _collision;
    Vector3 _start, _end, _up, _forward;
    float _elapsed, _duration, _apex;
    Collider _support;
    Vector3 _supportPosition;
    Quaternion _supportRotation;
    bool _catchSuppressed;
    Vector3 _releasedPosition;
    Vector3 _dropStartForward;
    Vector3 _motionFit;
    float _motionLanding;
    const float ApproachGrace = .25f;
    float _approachRemaining;
    Collider _approachSupport;
    Vector3 _approachSupportPosition, _approachDirection;
    Quaternion _approachSupportRotation;
    public bool ApproachPending => _approachRemaining > 0f;
    public ActorTraversalMotion VaultMotion { get; set; }
    public ActorTraversalMotion StepUpMotion { get; set; }
    public ActorTraversalMotion JumpGrabMotion { get; set; }
    public ActorTraversalMotion HangLeftMotion { get; set; }
    public ActorTraversalMotion HangRightMotion { get; set; }
    public ActorTraversalMotion[] CornerMotions { get; set; }
    public bool TurningCorner => Kind >= TraversalKind.HangOutwardLeft && Kind <= TraversalKind.HangInwardRight;
    public bool MovingOnLedge => Kind == TraversalKind.HangLeft || Kind == TraversalKind.HangRight || TurningCorner;
    Vector3 _travelEdge, _cornerEndEdge, _cornerPoint, _cornerSide;
    bool _cornerInward;
    Collider _cornerSupport;
    Vector3 _cornerSupportPosition;
    Quaternion _cornerSupportRotation;
    float _cornerGap;

    public ActorTraversalMotion ActiveMotion { get; private set; }
    public TraversalKind Kind { get; private set; }
    public bool Active => Kind != TraversalKind.None;
    public float Progress => _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 0f;
    public Vector3 Edge { get; private set; }
    public CharacterPose Pose { get; private set; }
    public string Rejection { get; private set; }

    public ActorTraversal(ActorCollision collision) => _collision = collision ?? throw new ArgumentNullException(nameof(collision));

    /// <summary>Returns the jump input for the motor. A nearby valid vault can consume it while input moves into reach.</summary>
    public bool ResolveJump(CharacterPose pose, Vector3 approachVelocity, bool grounded, bool jump, float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f || !CharacterMath.IsFinite(approachVelocity) || !pose.IsFinite ||
            Mathf.Abs(pose.Up.sqrMagnitude - 1f) > .01f || Mathf.Abs(pose.Forward.sqrMagnitude - 1f) > .01f)
            throw new ArgumentException("Traversal approach requires a finite pose with unit axes, finite velocity, and nonnegative time.");
        if (Active) return false;
        Vector3 velocity = Vector3.ProjectOnPlane(approachVelocity, pose.Up);
        if (ApproachPending)
        {
            if (!grounded || _collision.Stance != ActorStance.Standing ||
                Vector3.Dot(velocity, _approachDirection) <= .1f || Vector3.Dot(pose.Forward, _approachDirection) < .9f ||
                _approachSupport == null || !_approachSupport.enabled || !_approachSupport.gameObject.activeInHierarchy ||
                Vector3.Distance(_approachSupport.transform.position, _approachSupportPosition) > .01f ||
                Quaternion.Angle(_approachSupport.transform.rotation, _approachSupportRotation) > 1f)
            { ClearApproach(); return false; }
            if (_collision.Ray(pose.Position + pose.Up * .45f, pose.Forward, .9f, out var wall) &&
                wall.collider == _approachSupport && TryBegin(pose))
            { ClearApproach(); return false; }
            _approachRemaining = Mathf.Max(0f, _approachRemaining - dt);
            if (ApproachPending) return false;
            ClearApproach();
            return true;
        }
        if (!jump || TryBegin(pose)) return false;
        if (!grounded || Vector3.Dot(velocity, pose.Forward) <= .1f) return true;
        Vector3 ahead = pose.Position + Vector3.ClampMagnitude(velocity * ApproachGrace, .65f);
        if (!_collision.ClearSegment(pose.Position, ahead, pose.Up, pose.Forward)) return true;
        // Probe a future pose without moving authority or replacing the outgoing action's phase.
        var candidate = new ActorTraversal(_collision) { VaultMotion = VaultMotion, StepUpMotion = StepUpMotion };
        if (!candidate.TryBegin(new CharacterPose(ahead, pose.Up, pose.Forward)) || candidate.Kind != TraversalKind.Vault)
            return true;
        _approachRemaining = ApproachGrace;
        _approachSupport = candidate._support;
        _approachSupportPosition = candidate._supportPosition;
        _approachSupportRotation = candidate._supportRotation;
        _approachDirection = pose.Forward;
        return false;
    }

    void ClearApproach() { _approachRemaining = 0f; _approachSupport = null; }

    public bool TryDropToHang(CharacterPose pose)
    {
        if (Active) return false;
        if (!CharacterMath.IsFinite(pose.Position) || !CharacterMath.IsFinite(pose.Up) ||
            !CharacterMath.IsFinite(pose.Forward) || Mathf.Abs(pose.Up.sqrMagnitude - 1f) > .01f ||
            Mathf.Abs(pose.Forward.sqrMagnitude - 1f) > .01f)
            throw new ArgumentException("Ledge descent requires a finite pose and unit axes.");
        Vector3 right = Vector3.Cross(pose.Up, pose.Forward);
        if (TryDropToHang(pose, pose.Forward) || TryDropToHang(pose, -pose.Forward) ||
            TryDropToHang(pose, right) || TryDropToHang(pose, -right)) return true;
        return Reject("Move within 0.55 metres of a clear ledge before descending.");
    }

    public bool TryDropToHang(CharacterPose pose, Vector3 outwardDirection)
    {
        if (!CharacterMath.IsFinite(pose.Position) || !CharacterMath.IsFinite(pose.Up) ||
            !CharacterMath.IsFinite(pose.Forward) || !CharacterMath.IsFinite(outwardDirection) ||
            Mathf.Abs(pose.Up.sqrMagnitude - 1f) > .01f || Mathf.Abs(pose.Forward.sqrMagnitude - 1f) > .01f)
            throw new ArgumentException("Ledge descent requires a finite pose and unit axes.");
        if (Active || _collision.Stance != ActorStance.Standing ||
            !CharacterMath.TryProjectOntoTangent(outwardDirection, pose.Up, out var outward)) return false;
        if (!_collision.Ray(pose.Position + pose.Up * .15f, -pose.Up, .35f, out var support) ||
            Vector3.Dot(support.normal, pose.Up) < .8f) return Reject("Stand on a level ledge before descending.");
        Vector3 outside = support.point + outward * .8f;
        if (_collision.Ray(outside + pose.Up * .1f, -pose.Up, .3f, out _))
            return Reject("Move closer to the edge before descending.");
        if (!_collision.Ray(outside - pose.Up * .2f, -outward, .8f, out var wall) ||
            wall.collider != support.collider || Mathf.Abs(Vector3.Dot(wall.normal, pose.Up)) > .35f ||
            Vector3.Dot(wall.normal, outward) < .7f) return Reject("No clear ledge face below the edge.");
        Vector3 faceOut = Vector3.ProjectOnPlane(wall.normal, pose.Up).normalized;
        Vector3 edge = wall.point + pose.Up * Vector3.Dot(support.point - wall.point, pose.Up);
        // Center the grip opposite the actor, rather than at the oblique search ray's intersection.
        Vector3 alongFace = Vector3.ProjectOnPlane(pose.Position - edge, pose.Up);
        edge += alongFace - faceOut * Vector3.Dot(alongFace, faceOut);
        float edgeDistance = Vector3.ProjectOnPlane(edge - pose.Position, pose.Up).magnitude;
        if (edgeDistance > .55f) return Reject("Move closer to the edge before descending.");
        Vector3 side = Vector3.Cross(pose.Up, faceOut) * .2f;
        for (int sign = -1; sign <= 1; sign += 2)
            if (!_collision.Ray(edge - faceOut * .12f + side * sign + pose.Up * .1f, -pose.Up, .2f, out var hand) ||
                hand.collider != support.collider || Vector3.Dot(hand.normal, pose.Up) < .8f ||
                Mathf.Abs(Vector3.Dot(hand.point - edge, pose.Up)) > .05f)
                return Reject("The ledge cannot support both hands.");
        Vector3 hanging = edge + faceOut * (_collision.Radius + .08f) - pose.Up * (_collision.Height * (1.55f / 1.7f));
        _up = pose.Up; _forward = -faceOut; _dropStartForward = pose.Forward; Edge = edge;
        _support = support.collider; _supportPosition = _support.transform.position; _supportRotation = _support.transform.rotation;
        return Start(pose.Position, hanging, TraversalKind.DropToHang, 1.1f, 0f);
    }

    public void UpdateCatchAvailability(CharacterPose pose, bool grounded)
    {
        if (!CharacterMath.IsFinite(pose.Position)) throw new ArgumentException("Catch position must be finite.", nameof(pose));
        if (grounded || Vector3.Distance(pose.Position, _releasedPosition) > 1f) _catchSuppressed = false;
    }

    public bool TryCatchLedge(CharacterPose pose, Vector3 velocity, Vector3 intendedDirection)
    {
        if (!CharacterMath.IsFinite(pose.Position) || !CharacterMath.IsFinite(pose.Up) ||
            !CharacterMath.IsFinite(pose.Forward) || Mathf.Abs(pose.Up.sqrMagnitude - 1f) > .01f ||
            Mathf.Abs(pose.Forward.sqrMagnitude - 1f) > .01f ||
            !CharacterMath.IsFinite(velocity) || !CharacterMath.IsFinite(intendedDirection))
            throw new ArgumentException("Ledge catch requires finite motion and a unit pose frame.");
        if (Active || _catchSuppressed || _collision.Stance != ActorStance.Standing) return false;
        if (intendedDirection.sqrMagnitude < .0001f) intendedDirection = pose.Forward;
        if (!CharacterMath.TryProjectOntoTangent(intendedDirection, pose.Up, out var direction) ||
            Vector3.Dot(direction, pose.Forward) < .5f) return false;
        float reachHeight = _collision.Height * (1.55f / 1.7f);
        // Thin platforms can have no face at chest height. Probe the bounded hand-reach band.
        RaycastHit wall = default;
        bool foundFace = false;
        for (int sample = 0; sample <= 12; sample++)
        {
            float height = reachHeight - .3f + sample * .05f;
            if (!_collision.Ray(pose.Position + pose.Up * height, direction, _collision.Radius + .35f, out wall) ||
                Mathf.Abs(Vector3.Dot(wall.normal, pose.Up)) > .35f || Vector3.Dot(wall.normal, direction) > -.7f) continue;
            foundFace = true;
            break;
        }
        if (!foundFace) return false;
        Vector3 inward = Vector3.ProjectOnPlane(-wall.normal, pose.Up).normalized;
        Vector3 probe = wall.point + inward * .12f;
        probe += pose.Up * (Vector3.Dot(pose.Position - probe, pose.Up) + reachHeight + .4f);
        if (!_collision.Ray(probe, -pose.Up, .8f, out var top) || top.collider != wall.collider ||
            Vector3.Dot(top.normal, pose.Up) < .8f) return false;
        Vector3 lateral = Vector3.Cross(pose.Up, inward) * .2f;
        for (int side = -1; side <= 1; side += 2)
            if (!_collision.Ray(top.point + lateral * side + pose.Up * .1f, -pose.Up, .2f, out var hand) ||
                hand.collider != top.collider || Vector3.Dot(hand.normal, pose.Up) < .8f ||
                Mathf.Abs(Vector3.Dot(hand.point - top.point, pose.Up)) > .05f) return false;
        Vector3 edge = top.point - inward * .12f;
        Vector3 hanging = edge - inward * (_collision.Radius + .08f) - pose.Up * reachHeight;
        if (Vector3.Distance(hanging, pose.Position) > .4f ||
            !_collision.ClearSegment(pose.Position, hanging, pose.Up, inward)) return false;
        _up = pose.Up; _forward = inward; Edge = edge;
        _support = top.collider; _supportPosition = _support.transform.position; _supportRotation = _support.transform.rotation;
        return Start(pose.Position, hanging, TraversalKind.JumpGrab, .12f, 0f);
    }

    public bool TryBegin(CharacterPose pose)
    {
        if (Active || _collision.Stance != ActorStance.Standing) return Reject("Stand before traversing.");
        _up = pose.Up; _forward = pose.Forward;
        RaycastHit wall = default;
        bool foundWall = false;
        // A suspended platform has no face near the feet. Keep the low probe first for ordinary vaults.
        for (int sample = 0; sample <= 44; sample++)
        {
            if (!_collision.Ray(pose.Position + _up * (.45f + sample * .05f), _forward, .9f, out wall) ||
                Mathf.Abs(Vector3.Dot(wall.normal, _up)) > .35f || Vector3.Dot(wall.normal, _forward) > -.7f) continue;
            foundWall = true;
            break;
        }
        if (!foundWall) return Reject("No reachable wall.");
        Vector3 probe = wall.point + _forward * .12f;
        probe += _up * (Vector3.Dot(pose.Position - probe, _up) + 2.65f);
        if (!_collision.Ray(probe, -_up, 2.65f, out var top) || Vector3.Dot(top.normal, _up) < .8f)
            return Reject("No level ledge within reach.");
        float rise = Vector3.Dot(top.point - pose.Position, _up);
        if (rise < _collision.StepHeight || rise > 2.6f) return Reject("Ledge height is outside traversal range.");
        Edge = top.point - _forward * .12f;
        _support = top.collider; _supportPosition = _support.transform.position; _supportRotation = _support.transform.rotation;
        Vector3 landing = top.point + _forward * (_collision.Radius + .15f) + _up * .04f;
        // The basic step-up motion covers a one-metre rise, including ordinary uneven-ground approaches.
        TraversalKind kind = rise <= 1f ? TraversalKind.StepUp : TraversalKind.ClimbUp;
        // A low, thin obstacle with clear ground beyond it is a vault, not a climb onto its top.
        if (rise <= 1.2f && _collision.Ray(top.point + _forward * 1.2f + _up * .15f, -_up, rise + .4f, out var far) &&
            Vector3.Dot(far.point - pose.Position, _up) < .35f)
        { kind = TraversalKind.Vault; landing = far.point + _up * .04f; }
        if (rise > 1.7f)
        {
            kind = TraversalKind.JumpGrab;
            landing = Edge - _forward * (_collision.Radius + .08f) - _up * 1.55f;
        }
        return Start(pose.Position, landing, kind, kind == TraversalKind.JumpGrab ? .6f : kind == TraversalKind.ClimbUp ? 1.6f : kind == TraversalKind.Vault ? 1.25f : 1f, rise + .06f, groundedJump: kind == TraversalKind.JumpGrab);
    }

    public bool Climb()
    {
        if (Kind != TraversalKind.Hanging) return false;
        Vector3 landing = Edge + _forward * (_collision.Radius + .27f) + _up * .04f;
        return Start(Pose.Position, landing, TraversalKind.ClimbUp, 1.6f, Vector3.Dot(landing - Pose.Position, _up) + .05f);
    }

    /// <summary>One authored hand-over-hand step. Release or reversal takes effect at the next supported pose.</summary>
    public bool MoveAlongLedge(float direction)
    {
        if (!float.IsFinite(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (Kind != TraversalKind.Hanging || Mathf.Abs(direction) < .5f) return false;
        var motion = direction < 0f ? HangLeftMotion : HangRightMotion;
        if (motion == null) return Reject("No authored hanging travel motion is configured.");
        if (motion.Sample(1f).Root.x * direction <= .01f)
            return Reject("Hanging travel motion must move in the requested direction.");
        Vector3 side = Vector3.Cross(_up, _forward) * Mathf.Sign(direction);
        if (CornerMotions != null && _collision.Ray(Edge - _forward * (_collision.Radius + .08f) - _up * .1f, side, .9f, out var inside) &&
            Vector3.Dot(inside.normal, -side) > .95f && TryCorner(direction)) return true;
        Vector3 start = Pose.Position;
        for (int i = 1; i < motion.FrameCount; i++)
        {
            Vector3 offset = TransformMotion(motion.Sample(i / (float)(motion.FrameCount - 1)).Root);
            if (!SupportsHands(Edge + offset) || !_collision.ClearSegment(start, Pose.Position + offset, _up, _forward))
                return TryCorner(direction);
            start = Pose.Position + offset;
        }
        _start = Pose.Position; _travelEdge = Edge; _elapsed = 0f; _duration = motion.Duration;
        ActiveMotion = motion; Kind = direction < 0f ? TraversalKind.HangLeft : TraversalKind.HangRight;
        Rejection = null;
        return true;
    }

    bool SupportsHands(Vector3 edge) => SupportsHands(edge, _forward, _support);

    bool SupportsHands(Vector3 edge, Vector3 forward, Collider support)
    {
        Vector3 right = Vector3.Cross(_up, forward);
        for (int sign = -1; sign <= 1; sign += 2)
            if (!_collision.Ray(edge + right * (.24f * sign) + forward * .12f + _up * .1f, -_up, .2f, out var top) ||
                top.collider != support || Vector3.Dot(top.normal, _up) < .8f ||
                Mathf.Abs(Vector3.Dot(top.point - edge, _up)) > .05f) return false;
        if (!_collision.Ray(edge - _up * .1f - forward * .2f, forward, .3f, out var wall) ||
            wall.collider != support || Vector3.Dot(wall.normal, forward) > -.8f) return false;
        return true;
    }

    bool TryCorner(float direction)
    {
        if (CornerMotions == null || CornerMotions.Length != 4)
            return Reject("The ledge ends or the hanging path is blocked.");
        Vector3 side = Vector3.Cross(_up, _forward) * Mathf.Sign(direction);
        float gap = _collision.Radius + .08f;
        bool inward = _collision.Ray(Edge - _forward * gap - _up * .1f, side, .95f, out var wall);
        if (!inward && !_collision.Ray(Edge + side * .95f + _forward * .12f - _up * .1f, -side, 1.2f, out wall))
            return Reject("No adjacent ledge face is within reach.");
        Vector3 facing = inward ? side : -side;
        if (Vector3.Dot(wall.normal, -facing) < .95f)
            return Reject("The adjacent ledge is not a supported right-angle corner.");
        float distance = Vector3.Dot(wall.point - Edge, side);
        if (distance < .12f || distance > .85f) return Reject("The corner is outside the authored reach range.");
        int index = (inward ? 2 : 0) + (direction > 0f ? 1 : 0);
        var motion = CornerMotions[index];
        if (motion == null) return Reject("No authored motion is configured for this corner.");
        float yaw = motion.Sample(1f).RootYawDegrees;
        if (Vector3.Dot(Quaternion.AngleAxis(yaw, _up) * _forward, facing) < .99f)
            return Reject("The corner motion faces the wrong adjacent wall.");
        Vector3 corner = Edge + side * distance;
        _cornerPoint = corner; _cornerSide = side; _cornerInward = inward;
        Vector3 endEdge = corner + _forward * motion.Sample(1f).Root.z;
        if (!SupportsHands(endEdge, facing, wall.collider)) return Reject("The adjacent ledge has no clear hand support.");
        _start = Pose.Position; _travelEdge = Edge; _cornerGap = gap - motion.ReferenceEdge.z;
        _cornerEndEdge = endEdge; _cornerSupport = wall.collider;
        _cornerSupportPosition = wall.collider.transform.position; _cornerSupportRotation = wall.collider.transform.rotation;
        ActiveMotion = motion;
        Vector3 end = endEdge - facing * gap - _up * 1.55f;
        _motionFit = end - (_start + TransformMotion(motion.Sample(1f).Root) + (_forward - facing) * _cornerGap);
        if (_motionFit.magnitude > motion.MaxPlanarAdjustment)
        { ActiveMotion = null; return Reject("The corner is outside the authored path fit range."); }
        Vector3 previous = _start;
        for (int i = 1; i < motion.FrameCount; i++)
        {
            float t = i / (float)(motion.FrameCount - 1);
            Vector3 next = CornerPosition(t);
            if (!_collision.ClearSegment(previous, next, _up, CornerForward(t)))
            { ActiveMotion = null; return Reject("The corner path has insufficient body clearance."); }
            previous = next;
        }
        _elapsed = 0f; _duration = motion.Duration;
        Kind = (TraversalKind)((int)TraversalKind.HangOutwardLeft + index);
        Rejection = null;
        return true;
    }

    Vector3 CornerForward(float t) => Quaternion.AngleAxis(ActiveMotion.Sample(t).RootYawDegrees, _up) * _forward;
    Vector3 CornerPosition(float t)
    {
        Vector3 position = _start + TransformMotion(ActiveMotion.Sample(t).Root) +
            (_forward - CornerForward(t)) * _cornerGap + _motionFit * Mathf.SmoothStep(0f, 1f, t);
        if (_cornerInward)
        {
            float gap = _collision.Radius + .08f;
            position -= _cornerSide * Mathf.Max(0f, Vector3.Dot(position - _cornerPoint, _cornerSide) + gap);
            position -= _forward * Mathf.Max(0f, Vector3.Dot(position - _cornerPoint, _forward) + gap);
        }
        else
        {
            // Retain capsule clearance when the last full hand step leaves a different distance to the corner.
            float x = Vector3.Dot(position - _cornerPoint, _cornerSide);
            float z = Vector3.Dot(position - _cornerPoint, _forward);
            float gap = _collision.Radius + .08f;
            float safeX = x, safeZ = z;
            if (x < 0f) safeZ = Mathf.Min(z, -gap);
            else if (z > 0f) safeX = Mathf.Max(x, gap);
            else
            {
                Vector2 radial = new Vector2(x, z);
                if (radial.magnitude < gap) { radial = radial.sqrMagnitude > .000001f ? radial.normalized * gap : new Vector2(gap, -gap).normalized * gap; safeX = radial.x; safeZ = radial.y; }
            }
            position += _cornerSide * (safeX - x) + _forward * (safeZ - z);
        }
        return position;
    }

    bool Start(Vector3 start, Vector3 end, TraversalKind kind, float duration, float apex, bool groundedJump = false)
    {
        ActiveMotion = kind == TraversalKind.Vault ? VaultMotion : kind == TraversalKind.StepUp ? StepUpMotion : groundedJump ? JumpGrabMotion : null;
        _motionLanding = 0f;
        _start = start; _end = end; _duration = duration; _apex = apex; _elapsed = 0f;
        if (ActiveMotion != null)
        {
            _motionFit = Edge - start - TransformMotion(ActiveMotion.ReferenceEdge);
            if (Mathf.Abs(Vector3.Dot(_motionFit, _up)) > ActiveMotion.MaxHeightAdjustment ||
                Vector3.ProjectOnPlane(_motionFit, _up).magnitude > ActiveMotion.MaxPlanarAdjustment)
                return Reject("Obstacle is outside the authored traversal fit range.");
            _duration = ActiveMotion.Duration;
            _end = Sample(1f, kind);
            if (kind != TraversalKind.JumpGrab)
            {
            if (!_collision.Ray(_end + _up * .15f, -_up, .3f, out var ground) ||
                Vector3.Dot(ground.normal, _up) < .8f ||
                Mathf.Abs(Vector3.Dot(_end - ground.point, _up)) > .15f)
                return Reject("Authored traversal has no clear ground landing.");
            _motionLanding = Vector3.Dot(ground.point + _up * .04f - _end, _up);
            _end = Sample(1f, kind);
            }
            if (!_collision.Fits(_end, _up, _forward, ActorStance.Standing))
                return Reject("Authored traversal landing has insufficient standing clearance.");
            int intervals = ActiveMotion.FrameCount - 1;
            int segments = intervals * Mathf.Max(1, Mathf.CeilToInt(24f / intervals));
            for (int i = 1; i <= segments; i++)
                if (!ClearMotionSegment((i - 1f) / segments, i / (float)segments))
                    return Reject("Authored traversal body has insufficient clearance.");
            ClearApproach(); Kind = kind; Pose = new CharacterPose(start, _up, _forward); Rejection = null; return true;
        }
        // Validate the full body, including the top platform and overhead clearance, before moving the actor.
        Vector3 previous = start;
        for (int i = 1; i <= 24; i++)
        {
            Vector3 sample = Sample(i / 24f, kind);
            if (!_collision.Fits(sample, _up, _forward, ActorStance.Standing) ||
                !_collision.ClearSegment(previous, sample, _up, _forward))
                return Reject("Traversal path has insufficient clearance.");
            previous = sample;
        }
        ClearApproach(); Kind = kind; Pose = new CharacterPose(start, _up, kind == TraversalKind.DropToHang ? _dropStartForward : _forward); Rejection = null; return true;
    }

    Vector3 Sample(float t, TraversalKind kind)
    {
        if (ActiveMotion != null && (kind == TraversalKind.Vault || kind == TraversalKind.StepUp || kind == TraversalKind.JumpGrab))
        {
            var frame = ActiveMotion.Sample(t);
            return _start + TransformMotion(frame.Root) + Vector3.ProjectOnPlane(_motionFit, _up) * frame.PlanarFitWeight +
                _up * (Vector3.Dot(_motionFit, _up) * (kind != TraversalKind.Vault ? frame.PlanarFitWeight : frame.AnchorWeight) +
                    _motionLanding * Mathf.Max(0f, frame.PlanarFitWeight - frame.AnchorWeight));
        }
        if (kind == TraversalKind.DropToHang)
        {
            Vector3 outside = _end + _up * Vector3.Dot(_start - _end, _up);
            return t < .4f ? Vector3.Lerp(_start, outside, Mathf.SmoothStep(0f, 1f, t / .4f)) :
                Vector3.Lerp(outside, _end, Mathf.SmoothStep(0f, 1f, (t - .4f) / .6f));
        }
        if (kind == TraversalKind.JumpGrab) return Vector3.Lerp(_start, _end, Mathf.SmoothStep(0f, 1f, t));
        Vector3 highStart = _start + _up * _apex;
        Vector3 highEnd = _end + _up * (Vector3.Dot(highStart - _end, _up));
        if (kind == TraversalKind.Vault)
        {
            // Overlap rise, crossing, and landing so the body does not stop at airborne corners.
            // Start and runtime sweeps validate this complete curve against the obstacle.
            float across = SmoothTraversalPhase((t - .12f) / .76f);
            float endHeight = Vector3.Dot(_end - _start, _up);
            float height = t < .35f ? _apex * SmoothTraversalPhase(t / .35f) :
                t > .65f ? Mathf.Lerp(_apex, endHeight, SmoothTraversalPhase((t - .65f) / .35f)) : _apex;
            return _start + Vector3.ProjectOnPlane(_end - _start, _up) * across + _up * height;
        }
        if (t < .35f) return Vector3.Lerp(_start, highStart, Mathf.SmoothStep(0f, 1f, t / .35f));
        if (t < .8f) return Vector3.Lerp(highStart, highEnd, Mathf.SmoothStep(0f, 1f, (t - .35f) / .45f));
        return Vector3.Lerp(highEnd, _end, Mathf.SmoothStep(0f, 1f, (t - .8f) / .2f));
    }

    static float SmoothTraversalPhase(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    Vector3 TransformMotion(Vector3 local) => Vector3.Cross(_up, _forward) * local.x + _up * local.y + _forward * local.z;

    bool ClearMotionSegment(float from, float to)
    {
        var a = ActiveMotion.Sample(from); var b = ActiveMotion.Sample(to);
        Vector3 start = Sample(from, JumpGrabMotion == ActiveMotion ? TraversalKind.JumpGrab : StepUpMotion == ActiveMotion ? TraversalKind.StepUp : TraversalKind.Vault), end = Sample(to, JumpGrabMotion == ActiveMotion ? TraversalKind.JumpGrab : StepUpMotion == ActiveMotion ? TraversalKind.StepUp : TraversalKind.Vault);
        return _collision.ClearCapsuleSegment(start + TransformMotion(a.CapsuleA), start + TransformMotion(a.CapsuleB), a.Radius,
            end + TransformMotion(b.CapsuleA), end + TransformMotion(b.CapsuleB), b.Radius);
    }

    public CharacterPose Tick(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!Active) return Pose;
        if (_support == null || !_support.enabled || !_support.gameObject.activeInHierarchy ||
            Vector3.Distance(_support.transform.position, _supportPosition) > .01f ||
            Quaternion.Angle(_support.transform.rotation, _supportRotation) > 1f)
        { Cancel(); Rejection = "Traversal support changed."; return Pose; }
        if (TurningCorner)
        {
            if (_cornerSupport == null || !_cornerSupport.enabled || !_cornerSupport.gameObject.activeInHierarchy ||
                Vector3.Distance(_cornerSupport.transform.position, _cornerSupportPosition) > .01f ||
                Quaternion.Angle(_cornerSupport.transform.rotation, _cornerSupportRotation) > 1f)
            { Cancel(); Rejection = "Corner support changed."; return Pose; }
            float end = Mathf.Min(_duration, _elapsed + dt);
            while (_elapsed < end)
            {
                float nextTime = Mathf.Min(end, _elapsed + 1f / 60f);
                float t = nextTime / _duration;
                Vector3 next = CornerPosition(t), facing = CornerForward(t);
                if (!_collision.ClearSegment(Pose.Position, next, _up, facing))
                { Cancel(); Rejection = "The corner path became blocked."; return Pose; }
                _elapsed = nextTime; Pose = new CharacterPose(next, _up, facing);
                Edge = next + facing * (_collision.Radius + .08f) + _up * 1.55f;
            }
            if (_elapsed >= _duration)
            {
                _forward = Pose.Forward; Edge = _cornerEndEdge; _support = _cornerSupport;
                _supportPosition = _cornerSupportPosition; _supportRotation = _cornerSupportRotation;
                Kind = TraversalKind.Hanging; ActiveMotion = null;
            }
            return Pose;
        }
        if (Kind == TraversalKind.Hanging) return Pose;
        if (Kind == TraversalKind.HangLeft || Kind == TraversalKind.HangRight)
        {
            float end = Mathf.Min(_duration, _elapsed + dt);
            while (_elapsed < end)
            {
                float nextTime = Mathf.Min(end, _elapsed + 1f / 60f);
                Vector3 offset = TransformMotion(ActiveMotion.Sample(nextTime / _duration).Root);
                if (!SupportsHands(_travelEdge + offset) || !_collision.ClearSegment(Pose.Position, _start + offset, _up, _forward))
                { Kind = TraversalKind.Hanging; ActiveMotion = null; Rejection = "Hanging travel became blocked."; return Pose; }
                _elapsed = nextTime; Edge = _travelEdge + offset;
                Pose = new CharacterPose(_start + offset, _up, _forward);
            }
            if (_elapsed >= _duration) { Kind = TraversalKind.Hanging; ActiveMotion = null; }
            return Pose;
        }
        float endTime = Mathf.Min(_duration, _elapsed + dt);
        // Sweep each short trajectory segment so a late obstacle or long tick cannot tunnel through a wall.
        while (_elapsed < endTime)
        {
            float previousTime = _elapsed;
            float previousProgress = Progress;
            float step = ActiveMotion != null ? Mathf.Min(1f / 60f, _duration / (ActiveMotion.FrameCount - 1)) : 1f / 60f;
            float nextTime = Mathf.Min(endTime, _elapsed + step);
            if (ActiveMotion != null)
            {
                float interval = _duration / (ActiveMotion.FrameCount - 1);
                float boundary = (Mathf.Floor(_elapsed / interval + .00001f) + 1f) * interval;
                if (boundary > _elapsed) nextTime = Mathf.Min(nextTime, boundary);
            }
            _elapsed = nextTime;
            Vector3 next = Sample(Progress, Kind);
            if (ActiveMotion != null ? !ClearMotionSegment(previousProgress, Progress) :
                !_collision.ClearSegment(Pose.Position, next, _up, _forward))
            { _elapsed = previousTime; Cancel(); Rejection = "Traversal path became blocked."; return Pose; }
            Vector3 facing = Kind == TraversalKind.DropToHang ?
                Quaternion.AngleAxis(Vector3.SignedAngle(_dropStartForward, _forward, _up) *
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Progress / .4f)), _up) * _dropStartForward : _forward;
            Pose = new CharacterPose(next, _up, facing);
        }
        if (_elapsed >= _duration) Kind = Kind == TraversalKind.JumpGrab || Kind == TraversalKind.DropToHang ? TraversalKind.Hanging : TraversalKind.None;
        return Pose;
    }

    public void Cancel()
    {
        ClearApproach();
        if (Active) { _catchSuppressed = true; _releasedPosition = Pose.Position; }
        Kind = TraversalKind.None;
    }
    bool Reject(string reason) { Rejection = reason; return false; }
}

