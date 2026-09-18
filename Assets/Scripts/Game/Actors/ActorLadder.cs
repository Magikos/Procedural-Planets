using System;
using UnityEngine;

public enum ActorLadderPhase { None, MountBottom, MountTop, Climbing, ExitBottom, ExitTop, ApproachStep, SlideStart, Slide, SlideEnd, SprintTop }

/// <summary>Collision authority for an authored ladder performance. Presentation owns limb contact corrections.</summary>
public sealed class ActorLadder
{
    readonly ActorCollision _collision;
    ActorAnimationPerformance _performance;
    ActorTraversalMotion _topExit, _topExitMirrored, _topMount, _bottomMount, _approachStep, _transition, _bottomExit, _upGait, _downGait;
    ActorTraversalMotion _slideStart, _slide, _slideEnd;
    ActorTraversalMotion _bottomExitMirrored;
    ActorTraversalMotion _sprintLeft, _sprintRight, _sprintTop;
    bool _sprinting, _sprintRightLeading, _pendingTopHandover;
    float _transitionPlaybackRate = 1f;
    public float EffectivePlaybackRate => !Active ? 0f : Phase != ActorLadderPhase.Climbing ? _transitionPlaybackRate :
        _sprinting ? Mathf.Abs(_speed) / SprintPhaseRate * SprintPlaybackRate :
        _upGait != null ? Mathf.Abs(_speed) * Duration("Up") : Mathf.Abs(_speed) * Duration("Up") / (_spacing * _rungsPerCycle);
    public bool Sprinting => _sprinting;
    public float SprintPlaybackRate { get; private set; } = 1.5f;
    public float SprintPhaseRate => SprintPlaybackRate / ((_sprintRightLeading ? _sprintRight : _sprintLeft)?.Duration ?? 1f);
    public float SprintCycleHeight => (_sprintRightLeading ? _sprintRight : _sprintLeft)?.Sample(1f).Root.y ?? 0f;
    public bool SprintSpacingFits => SprintMotionFits(_sprintLeft) && SprintMotionFits(_sprintRight);
    bool SprintMotionFits(ActorTraversalMotion motion) => motion != null && Mathf.Abs(
        Mathf.Max(2, Mathf.RoundToInt(motion.Sample(1f).Root.y / _spacing)) * _spacing - motion.Sample(1f).Root.y) <= motion.MaxHeightAdjustment + .0001f;
    string _gaitDirection;
    Vector3 _bottom, _up, _forward, _start, _end;
    public Vector3 SupportFitOffset { get; private set; }
    public float SourceCycleTravel { get; private set; }
    public float GaitPhase => Mathf.Repeat(_cycle, 1f);
    public string ClimbDirection => _gaitDirection ?? "Up";
    public bool SupportFitBlocked { get; private set; }
    public bool PrimarySupportOnly { get; private set; }
    public bool PrimarySupportIsLeft { get; private set; }
    public float ReturnBlendRemaining { get; private set; }
    float _height, _spacing, _elapsed, _duration, _speed, _cycle, _rungsPerCycle, _idleTime;
    public ActorLadderPhase Phase { get; private set; }
    public bool Active => Phase != ActorLadderPhase.None;
    public CharacterPose Pose { get; private set; }
    public string AnimationPhase { get; private set; }
    public float AnimationProgress { get; private set; }
    public string Rejection { get; private set; }
    public float ClimbHeight => Vector3.Dot(Pose.Position - _bottom, _up);
    public float BottomClimbHeight => _bottomExit != null ? -_bottomExit.Sample(1f).Root.y : 0f;
    public float TopClimbHeight => _height - (_topExit?.Sample(1f).Root.y ?? 1.35f);

    public ActorLadder(ActorCollision collision) => _collision = collision ?? throw new ArgumentNullException(nameof(collision));

    public bool Begin(CharacterPose pose, Vector3 bottom, Vector3 up, Vector3 forward, float height, float rungSpacing,
        bool fromTop, ActorAnimationPerformance performance, float rungsPerCycle = 2f,
        ActorTraversalMotion topExit = null, ActorTraversalMotion topMount = null, ActorTraversalMotion bottomMount = null,
        ActorTraversalMotion approachStep = null, ActorTraversalMotion bottomExit = null,
        ActorTraversalMotion upGait = null, ActorTraversalMotion downGait = null, Vector3 topMountSupportOffset = default,
        ActorTraversalMotion topExitMirrored = null, ActorTraversalMotion slideStart = null,
        ActorTraversalMotion slide = null, ActorTraversalMotion slideEnd = null,
        ActorTraversalMotion sprintUp = null, ActorTraversalMotion sprintUpRight = null,
        ActorTraversalMotion bottomExitMirrored = null, float sprintPlaybackRate = 1.5f,
        ActorTraversalMotion sprintTop = null, bool sprintFromBottom = false, Vector3 sprintApproachOffset = default)
    {
        if (Active) return false;
        if (!pose.IsFinite || !CharacterMath.IsFinite(bottom) || !CharacterMath.IsFinite(up) || !CharacterMath.IsFinite(forward) ||
            Mathf.Abs(up.sqrMagnitude - 1f) > .01f || Mathf.Abs(forward.sqrMagnitude - 1f) > .01f || Mathf.Abs(Vector3.Dot(up, forward)) > .01f ||
            !float.IsFinite(height) || height < 1.8f || !float.IsFinite(rungSpacing) || rungSpacing < .15f || rungSpacing > .5f ||
            !float.IsFinite(rungsPerCycle) || rungsPerCycle <= 0f)
            throw new ArgumentException("Ladder geometry requires finite orthogonal unit axes, height at least 1.8m, and rung spacing 0.15..0.5m.");
        if (performance == null) throw new ArgumentNullException(nameof(performance));
        if (!float.IsFinite(sprintPlaybackRate) || sprintPlaybackRate <= 0f)
            throw new ArgumentOutOfRangeException(nameof(sprintPlaybackRate));
        SprintPlaybackRate = sprintPlaybackRate;
        if (!CharacterMath.IsFinite(topMountSupportOffset) ||
            (topMount == null && topMountSupportOffset.sqrMagnitude > 0f))
            throw new ArgumentException("Top mount support placement requires a finite authored offset.");
        foreach (string name in new[] { "MountBottom", "MountTop", "Up", "Down", "Idle", "ExitBottom", "ExitTop" })
        {
            var phase = performance.GetPhase(name);
            if (phase == null || !float.IsFinite(phase.Clip.length) || phase.Clip.length <= 0f)
                throw new ArgumentException("Ladder performance requires a positive-duration phase: " + name, nameof(performance));
        }
        _bottom = bottom; _up = up; _forward = forward; _height = height; _spacing = rungSpacing;
        _performance = performance; _rungsPerCycle = rungsPerCycle;
        _sprintTop = sprintTop;
        if (!CharacterMath.IsFinite(sprintApproachOffset) || Mathf.Abs(sprintApproachOffset.y) > .001f)
            throw new ArgumentException("Sprint ladder approach requires a finite planar offset.");
        if (sprintFromBottom && (fromTop || sprintTop == null || performance.GetPhase("SprintTop") == null ||
            sprintTop.Sample(1f).Root.y <= 0f || Mathf.Abs(sprintTop.Duration - Duration("SprintTop")) > .001f ||
            Mathf.Abs(height - sprintTop.Sample(1f).Root.y) > sprintTop.MaxHeightAdjustment))
        { Rejection = "This ladder requires a matching authored full-height sprint ascent."; return false; }
        _topExit = topExit; _topMount = topMount; _bottomMount = bottomMount; _approachStep = approachStep;
        _topExitMirrored = topExitMirrored;
        _bottomExitMirrored = bottomExitMirrored;
        if (bottomExitMirrored != null && (performance.GetPhase("ExitBottomMirrored") == null ||
            bottomExitMirrored.Sample(1f).Root.y >= 0f ||
            Mathf.Abs(bottomExitMirrored.Duration - Duration("ExitBottomMirrored")) > .001f))
        { Rejection = "Mirrored bottom exit must match its authored performance phase."; return false; }
        _slideStart = slideStart; _slide = slide; _slideEnd = slideEnd;
        _sprintLeft = sprintUp; _sprintRight = sprintUpRight; _sprinting = false; _pendingTopHandover = false;
        if (sprintUp != null || sprintUpRight != null)
            foreach (var pair in new[] { (Name: "SprintUp", Motion: sprintUp), (Name: "SprintUpRight", Motion: sprintUpRight) })
                if (pair.Motion == null || performance.GetPhase(pair.Name) == null ||
                    pair.Motion.Sample(1f).Root.y <= 0f || Mathf.Abs(pair.Motion.Duration - Duration(pair.Name)) > .001f)
                { Rejection = "Sprint climbing requires both matching authored leading-hand motions."; return false; }
        if (slideStart != null || slide != null || slideEnd != null)
        {
            foreach (var pair in new[] { (Name: "SlideStart", Motion: slideStart), (Name: "Slide", Motion: slide), (Name: "SlideEnd", Motion: slideEnd) })
                if (pair.Motion == null || performance.GetPhase(pair.Name) == null ||
                    Mathf.Abs(pair.Motion.Duration - Duration(pair.Name)) > .001f ||
                    (pair.Name == "SlideEnd" ? pair.Motion.Sample(1f).Root.y <= 0f : pair.Motion.Sample(1f).Root.y >= 0f))
                { Rejection = "Sliding requires matching authored start, descent, and brake motions."; return false; }
        }
        if (topExitMirrored != null && (performance.GetPhase("ExitTopMirrored") == null ||
            topExitMirrored.Sample(1f).Root.y <= 0f ||
            Mathf.Abs(topExitMirrored.Duration - Duration("ExitTopMirrored")) > .001f))
        { Rejection = "Mirrored top exit must match its authored performance phase."; return false; }
        _bottomExit = bottomExit; _upGait = upGait; _downGait = downGait; _gaitDirection = null; SupportFitOffset = Vector3.zero; SourceCycleTravel = 0f;
        if (topMount != null && (Mathf.Abs(topMountSupportOffset.y) > topMount.MaxHeightAdjustment + .0001f ||
            Vector3.ProjectOnPlane(topMountSupportOffset, Vector3.up).magnitude > topMount.MaxPlanarAdjustment + .0001f))
        { Rejection = "Top mount support placement exceeds the authored motion fitting limits."; return false; }
        if ((_upGait == null) != (_downGait == null) ||
            (_upGait != null && (_upGait.Sample(1f).Root.y <= 0f || _downGait.Sample(1f).Root.y >= 0f ||
                Mathf.Abs(_upGait.Duration - Duration("Up")) > .001f || Mathf.Abs(_downGait.Duration - Duration("Down")) > .001f)))
        { Rejection = "Ladder gait motions must match both authored climb directions."; return false; }
        if (_approachStep != null && (performance.GetPhase("ApproachStep") == null ||
            Mathf.Abs(_approachStep.Duration - Duration("ApproachStep")) > .001f))
        { Rejection = "Ladder approach step must match its authored performance duration."; return false; }
        if ((_topExit != null && _topExit.Sample(1f).Root.y <= 0f) ||
            (_topMount != null && _topMount.Sample(1f).Root.y >= 0f) ||
            TopClimbHeight <= BottomClimbHeight || height < Mathf.Max(_topExit?.Sample(1f).Root.y ?? 0f, -(_topMount?.Sample(1f).Root.y ?? 0f)) + .15f)
        { Rejection = "This ladder is too short for the authored top transfers and their clearance."; return false; }
        if ((_topExit != null && Mathf.Abs(_topExit.Duration - Duration("ExitTop")) > .001f) ||
            (_topMount != null && Mathf.Abs(_topMount.Duration - Duration("MountTop")) > .001f) ||
            (_bottomMount != null && Mathf.Abs(_bottomMount.Duration - Duration("MountBottom")) > .001f))
        { Rejection = "Ladder motion must match the authored performance duration."; return false; }
        Vector3 approach = sprintFromBottom ? _bottom + Local(sprintApproachOffset) : fromTop ? _bottom + _up * _height - _forward * (_topMount?.Sample(1f).Root.z ?? -.95f) :
            _bottomMount != null ? _bottom - Local(Vector3.ProjectOnPlane(_bottomMount.Sample(1f).Root, Vector3.up)) : _bottom - _forward * .25f;
        if (!fromTop && !sprintFromBottom && _approachStep != null) approach -= Local(_approachStep.Sample(1f).Root);
        Vector3 approachForward = fromTop && _topMount != null ? Quaternion.AngleAxis(_topMount.Sample(0f).RootYawDegrees, _up) * _forward : _forward;
        if (Vector3.Distance(pose.Position, approach) > .15f || Vector3.Angle(pose.Forward, approachForward) > 12f || Vector3.Angle(pose.Up, _up) > 1f)
        { Rejection = "Move to the ladder approach and face the rungs before mounting."; return false; }
        if (!_collision.TryStance(ActorStance.Standing, pose))
        { Rejection = "The ladder requires standing clearance."; return false; }
        Pose = pose; _cycle = _speed = _idleTime = ReturnBlendRemaining = 0f; PrimarySupportOnly = PrimarySupportIsLeft = false;
        if (sprintFromBottom)
        {
            Vector3 travel = sprintTop.Sample(1f).Root;
            return Start(ActorLadderPhase.SprintTop, _bottom + Local(sprintApproachOffset + travel) + _up * (height - travel.y));
        }
        if (!fromTop && _approachStep != null)
        {
            Vector3 stepEnd = approach + Local(_approachStep.Sample(1f).Root);
            // The ground step keeps the motor's actual standing height, including its skin offset.
            stepEnd += _up * Vector3.Dot(pose.Position - stepEnd, _up);
            return Start(ActorLadderPhase.ApproachStep, stepEnd);
        }
        return Start(fromTop ? ActorLadderPhase.MountTop : ActorLadderPhase.MountBottom,
            fromTop ? _bottom + _up * (_topMount != null ? _height + _topMount.Sample(1f).Root.y : TopClimbHeight) + Local(topMountSupportOffset) :
                _bottom + _up * (_bottomMount?.Sample(1f).Root.y ?? 0f));
    }

    Vector3 TopExit => _bottom + _up * _height + _forward * (_topExit?.Sample(1f).Root.z ?? .95f);

    bool Start(ActorLadderPhase phase, Vector3 end, bool mirrored = false)
    {
        if ((phase == ActorLadderPhase.ExitTop || phase == ActorLadderPhase.ExitBottom || phase == ActorLadderPhase.SprintTop) &&
            (!_collision.Ray(end + _up * .1f, -_up, .25f, out var support) || Vector3.Dot(support.normal, _up) < .8f))
        { Rejection = "The ladder exit has no clear standing support. Reverse direction or release."; return false; }
        _start = Pose.Position; _end = end;
        _transition = phase == ActorLadderPhase.ExitTop ? (mirrored ? _topExitMirrored : _topExit) : phase == ActorLadderPhase.MountTop ? _topMount :
            phase == ActorLadderPhase.MountBottom ? _bottomMount : phase == ActorLadderPhase.ExitBottom ? (mirrored ? _bottomExitMirrored : _bottomExit) :
            phase == ActorLadderPhase.ApproachStep ? _approachStep : phase == ActorLadderPhase.SlideStart ? _slideStart :
            phase == ActorLadderPhase.SlideEnd ? _slideEnd : phase == ActorLadderPhase.SprintTop ? _sprintTop : null;
        string animationPhase = phase + (mirrored ? "Mirrored" : "");
        _duration = Duration(animationPhase);
        // Validate the whole body route before committing. Tick repeats sweeps for late obstacles.
        if (!ClearTransitionRoute(phase))
        { Rejection = "The ladder mount or exit is blocked."; return false; }
        Phase = phase; AnimationPhase = animationPhase; AnimationProgress = _elapsed = 0f;
        _transitionPlaybackRate = 1f;
        Rejection = null;
        return true;
    }

    bool ClearTransitionRoute(ActorLadderPhase phase)
    {
        Vector3 previous = _start;
        for (int i = 1; i <= 48; i++)
        {
            Vector3 next = TransitionPosition(i / 48f, phase);
            if (!ClearTransition((i - 1) / 48f, i / 48f, previous, next)) return false;
            previous = next;
        }
        return true;
    }

    float Duration(string name)
    {
        var phase = _performance.GetPhase(name);
        return phase.Clip.length * (phase.EndNormalized - phase.StartNormalized);
    }

    Vector3 TransitionPosition(float progress, ActorLadderPhase phase)
    {
        if (_transition != null)
        {
            var frame = _transition.Sample(progress);
            Vector3 nativeEnd = _start + Local(_transition.Sample(1f).Root);
            // Correct only the approach's small placement tolerance, using the authored phase timing.
            return _start + Local(frame.Root) + (_end - nativeEnd) * Mathf.SmoothStep(0f, 1f, progress);
        }
        float t = Mathf.SmoothStep(0f, 1f, progress);
        if (phase == ActorLadderPhase.ExitTop)
        {
            Vector3 raised = _start + _up * Vector3.Dot(_end - _start, _up);
            return progress < .6f ? Vector3.Lerp(_start, raised, Mathf.SmoothStep(0f, 1f, progress / .6f)) :
                Vector3.Lerp(raised, _end, Mathf.SmoothStep(0f, 1f, (progress - .6f) / .4f));
        }
        if (phase == ActorLadderPhase.MountTop)
        {
            Vector3 outside = _end + _up * Vector3.Dot(_start - _end, _up);
            return progress < .4f ? Vector3.Lerp(_start, outside, Mathf.SmoothStep(0f, 1f, progress / .4f)) :
                Vector3.Lerp(outside, _end, Mathf.SmoothStep(0f, 1f, (progress - .4f) / .6f));
        }
        return Vector3.Lerp(_start, _end, t);
    }

    Vector3 Local(Vector3 value) => Vector3.Cross(_up, _forward) * value.x + _up * value.y + _forward * value.z;

    bool ClearTransition(float before, float after, Vector3 previous, Vector3 next)
    {
        if (_transition == null) return _collision.ClearSegment(previous, next, _up, _forward);
        var a = _transition.Sample(before); var b = _transition.Sample(after);
        return _collision.ClearCapsuleSegment(previous + Local(a.CapsuleA), previous + Local(a.CapsuleB), a.Radius,
            next + Local(b.CapsuleA), next + Local(b.CapsuleB), b.Radius);
    }

    public CharacterPose Tick(float verticalInput, float dt, bool fast = false)
    {
        if (!float.IsFinite(verticalInput) || !float.IsFinite(dt) || dt < 0f)
            throw new ArgumentException("Ladder input and nonnegative delta time must be finite.");
        if (!Active || dt == 0f) return Pose;
        if (Phase == ActorLadderPhase.Climbing && ReturnBlendRemaining > 0f)
        {
            if (verticalInput < -.1f && TryBottomTransfer())
            {
                ReturnBlendRemaining = Phase == ActorLadderPhase.Climbing ? Mathf.Max(0f, ReturnBlendRemaining - dt) : 0f;
                return Pose;
            }
            ReturnBlendRemaining = Mathf.Max(0f, ReturnBlendRemaining - dt);
            return Pose;
        }
        if (Phase == ActorLadderPhase.Climbing && _sprinting) return TickSprint(verticalInput, fast, dt);
        if (Phase == ActorLadderPhase.Climbing && fast && verticalInput > .1f && SprintSpacingFits &&
            CanSprint(Mathf.Abs(GaitPhase - .5f) < .04f ? _sprintRight : _sprintLeft) &&
            (GaitPhase < .04f || GaitPhase > .96f || Mathf.Abs(GaitPhase - .5f) < .04f))
        {
            _sprintRightLeading = Mathf.Abs(GaitPhase - .5f) < .04f;
            _sprinting = true; _cycle = 0f;
            _speed = 0f; PrimarySupportOnly = false;
            AnimationPhase = _sprintRightLeading ? "SprintUpRight" : "SprintUp"; AnimationProgress = 0f;
            BeginSupportTransfer(); return Pose;
        }
        if (Phase == ActorLadderPhase.Slide) return TickSlide(verticalInput, fast, dt);
        if (Phase == ActorLadderPhase.Climbing && fast && verticalInput < -.1f && _slide != null &&
            ClimbHeight > BottomClimbHeight + Mathf.Abs(_slideStart.Sample(1f).Root.y) + .15f &&
            (GaitPhase < .04f || GaitPhase > .96f || Mathf.Abs(GaitPhase - .5f) < .04f))
        {
            Start(ActorLadderPhase.SlideStart, Pose.Position + Local(_slideStart.Sample(1f).Root));
            return Pose;
        }
        if (Phase != ActorLadderPhase.Climbing)
        {
            float remaining = dt;
            while (remaining > 0f && Active && Phase != ActorLadderPhase.Climbing)
            {
                float step = Mathf.Min(remaining, 1f / 60f);
                bool fastTransfer = Phase == ActorLadderPhase.MountBottom || Phase == ActorLadderPhase.MountTop ||
                    Phase == ActorLadderPhase.ExitBottom || Phase == ActorLadderPhase.ExitTop;
                float targetRate = fast && fastTransfer ? SprintPlaybackRate : 1f;
                _transitionPlaybackRate = Mathf.MoveTowards(_transitionPlaybackRate, targetRate, step * 5f);
                float nextElapsed = Mathf.Min(_duration, _elapsed + step * _transitionPlaybackRate);
                Vector3 next = TransitionPosition(nextElapsed / _duration, Phase);
                if (!ClearTransition(_elapsed / _duration, nextElapsed / _duration, Pose.Position, next))
                { Cancel(); Rejection = "The ladder route became blocked; control returned to the motor."; return Pose; }
                _elapsed = nextElapsed; remaining -= step;
                Vector3 facing = _transition != null ? Quaternion.AngleAxis(_transition.Sample(nextElapsed / _duration).RootYawDegrees, _up) * _forward :
                    Vector3.RotateTowards(Pose.Forward, _forward, step * 3f, 0f);
                Pose = new CharacterPose(next, _up, facing);
                AnimationProgress = _elapsed / _duration;
                if (_elapsed >= _duration)
                {
                    if (Phase == ActorLadderPhase.ApproachStep)
                    {
                        if (!Start(ActorLadderPhase.MountBottom, _bottom + _up * (_bottomMount?.Sample(1f).Root.y ?? 0f)))
                            Cancel();
                    }
                    else if (Phase == ActorLadderPhase.SlideStart)
                    {
                        if (fast && verticalInput < -.1f) BeginSlideLoop();
                        else Start(ActorLadderPhase.SlideEnd, Pose.Position + Local(_slideEnd.Sample(1f).Root));
                        return Pose;
                    }
                    else if (Phase == ActorLadderPhase.ExitBottom || Phase == ActorLadderPhase.ExitTop || Phase == ActorLadderPhase.SprintTop) Phase = ActorLadderPhase.None;
                    else
                    {
                        PrimarySupportOnly = Phase == ActorLadderPhase.SlideEnd;
                        PrimarySupportIsLeft = false;
                        Phase = ActorLadderPhase.Climbing; AnimationPhase = PrimarySupportOnly ? "Up" : "Idle";
                        AnimationProgress = _cycle = _speed = 0f;
                        ReturnBlendRemaining = PrimarySupportOnly ? _performance.GetPhase("Up").BlendSeconds : 0f;
                        if (PrimarySupportOnly && verticalInput < -.1f && TryBottomTransfer())
                        {
                            if (Phase != ActorLadderPhase.Climbing) ReturnBlendRemaining = 0f;
                            return Pose;
                        }
                    }
                }
            }
            return Pose;
        }
        if (_upGait != null)
        {
            float remaining = dt;
            float startCycle = _cycle, startHeight = ClimbHeight;
            while (remaining > 0f && Phase == ActorLadderPhase.Climbing)
            {
                float step = Mathf.Min(remaining, 1f / 60f);
                TickAuthoredGait(verticalInput, step, fast, startCycle, startHeight);
                remaining -= step;
            }
            return Pose;
        }
        float input = Mathf.Clamp(verticalInput, -1f, 1f);
        string direction = input >= 0f ? "Up" : "Down";
        float rate = _spacing * _rungsPerCycle / Duration(direction) * (fast ? SprintPlaybackRate : 1f);
        _speed = Mathf.MoveTowards(_speed, input * rate, dt * rate * 5f);
        float nextHeight = Mathf.Clamp(ClimbHeight + _speed * dt, 0f, TopClimbHeight);
        Vector3 desired = _bottom + _up * nextHeight;
        if (!_collision.ClearSegment(Pose.Position, desired, _up, _forward))
        { _speed = 0f; Rejection = "Climbing is blocked. Reverse direction or release the ladder."; }
        else
        {
            float travel = Vector3.Distance(Pose.Position, desired);
            if (travel < .000001f) _speed = 0f;
            Pose = new CharacterPose(desired, _up, _forward);
            _cycle += travel / (_spacing * _rungsPerCycle);
            Rejection = null;
        }
        AnimationPhase = Mathf.Abs(_speed) < .005f ? "Idle" : _speed > 0f ? "Up" : "Down";
        if (AnimationPhase == "Idle") { _idleTime += dt; AnimationProgress = _idleTime / Duration("Idle"); }
        else { _idleTime = 0f; AnimationProgress = _cycle; }
        if (input > .1f && ClimbHeight >= TopClimbHeight - .001f) Start(ActorLadderPhase.ExitTop, TopExit);
        else if (input < -.1f && ClimbHeight <= .001f) Start(ActorLadderPhase.ExitBottom, _bottom - _forward * .25f);
        return Pose;
    }

    // Each acquisition establishes a new authored support interval. Its fit cannot accumulate without bound.
    public void BeginSupportTransfer() { SupportFitOffset = Vector3.zero; SupportFitBlocked = false; }

    public Vector3 FitSupport(Vector3 requestedDisplacement, float dt)
    {
        if (!CharacterMath.IsFinite(requestedDisplacement) || !float.IsFinite(dt) || dt < 0f)
            throw new ArgumentException("Support fitting requires finite displacement and nonnegative time.");
        if (Phase != ActorLadderPhase.Climbing || dt == 0f) return Vector3.zero;
        Vector3 desired = Vector3.ClampMagnitude(SupportFitOffset + requestedDisplacement, .15f);
        Vector3 next = Vector3.MoveTowards(SupportFitOffset, desired, dt * .75f);
        Vector3 movement = next - SupportFitOffset;
        SupportFitBlocked = !_collision.ClearSegment(Pose.Position, Pose.Position + movement, _up, _forward);
        if (SupportFitBlocked) return Vector3.zero;
        Pose = new CharacterPose(Pose.Position + movement, _up, _forward);
        SupportFitOffset = next;
        return movement;
    }

    CharacterPose TickAuthoredGait(float input, float dt, bool fast, float startCycle, float startHeight)
    {
        input = Mathf.Clamp(input, -1f, 1f);
        if (input < -.1f) _pendingTopHandover = false;
        if (input > .1f && TryTopTransfer()) return Pose;
        if (input < -.1f && TryBottomTransfer()) return Pose;
        float rate = (fast ? SprintPlaybackRate : 1f) / Duration("Up");
        _speed = Mathf.MoveTowards(_speed, input * rate, dt * rate * 5f);
        if (Mathf.Abs(_speed) > .0001f)
        {
            _gaitDirection = _speed > 0f ? "Up" : "Down";
            float nextCycle = _cycle + _speed * dt;
            if (_pendingTopHandover) nextCycle = Mathf.Min(nextCycle, Mathf.Floor(_cycle) + .54f);
            float CycleHeight(float cycle) => Mathf.Floor(cycle) * _upGait.Sample(1f).Root.y + _upGait.Sample(Mathf.Repeat(cycle, 1f)).Root.y;
            float scale = _spacing * _rungsPerCycle / _upGait.Sample(1f).Root.y;
            // The source phase and its body travel must advance together. Exit at the nearest
            // compatible cycle boundary; clamping only travel makes the supporting hand slide.
            // Rebase substeps on this outer tick, avoiding repeated world-position
            // rounding when the actor is far from the scene origin.
            float height = Mathf.Max(Mathf.Min(0f, ClimbHeight), startHeight + (CycleHeight(nextCycle) - CycleHeight(startCycle)) * scale);
            Vector3 desired = _bottom + _up * height;
            // Preserve the source-fitted lateral placement while advancing the motor's vertical trajectory.
            desired += Vector3.ProjectOnPlane(Pose.Position - _bottom, _up);
            if (!_collision.ClearSegment(Pose.Position, desired, _up, _forward))
            { _speed = 0f; Rejection = "Climbing is blocked. Reverse direction or release the ladder."; }
            else
            {
                Pose = new CharacterPose(desired, _up, _forward);
                SourceCycleTravel += Mathf.Abs(nextCycle - _cycle); _cycle = nextCycle; Rejection = null;
                if (PrimarySupportIsLeft ? Mathf.Abs(GaitPhase - .5f) > .04f : GaitPhase > .04f && GaitPhase < .96f)
                    PrimarySupportOnly = false;
                // One reversible phase retains the exact source pose during stop and direction changes.
                AnimationPhase = "Up"; AnimationProgress = GaitPhase;
            }
        }
        if (input > .1f && TryTopTransfer()) return Pose;
        else if (input < -.1f && TryBottomTransfer()) return Pose;
        return Pose;
    }

    void BeginSlideLoop()
    {
        Phase = ActorLadderPhase.Slide; AnimationPhase = "Slide";
        _transition = _slide; _start = Pose.Position; _end = _start + Local(_slide.Sample(1f).Root);
        _duration = _slide.Duration; _elapsed = AnimationProgress = 0f;
    }

    public Vector3 TransitionEndPosition => _end;

    public bool FitSlideReturn(Vector3 offset)
    {
        if (Phase != ActorLadderPhase.SlideEnd || _elapsed > .00001f || !CharacterMath.IsFinite(offset)) return false;
        Vector3 totalFit = _end + offset - (_start + Local(_slideEnd.Sample(1f).Root));
        if (Mathf.Abs(Vector3.Dot(totalFit, _up)) > _slideEnd.MaxHeightAdjustment ||
            Vector3.ProjectOnPlane(totalFit, _up).magnitude > _slideEnd.MaxPlanarAdjustment) return false;
        Vector3 original = _end; _end += offset;
        if (!ClearTransitionRoute(Phase))
        { _end = original; Rejection = "The slide return placement is blocked."; return false; }
        return true;
    }

    CharacterPose TickSprint(float input, bool fast, float dt)
    {
        var motion = _sprintRightLeading ? _sprintRight : _sprintLeft;
        float rate = SprintPhaseRate;
        _speed = Mathf.MoveTowards(_speed, Mathf.Clamp(input, -1f, 1f) * rate, dt * rate * 5f);
        float next = Mathf.Clamp01(_cycle + _speed * dt);
        Vector3 desired = Pose.Position + Local(motion.Sample(next).Root - motion.Sample(_cycle).Root);
        if (!_collision.ClearSegment(Pose.Position, desired, _up, _forward))
        { _speed = 0f; Rejection = "Sprint climbing is blocked. Reverse direction or release."; return Pose; }
        Pose = new CharacterPose(desired, _up, _forward);
        SourceCycleTravel += Mathf.Abs(next - _cycle); _cycle = next;
        AnimationProgress = next; _gaitDirection = _speed < 0f ? "Down" : "Up"; Rejection = null;
        if (next >= 1f)
        {
            if (fast && input > .1f && CanSprint(_sprintRightLeading ? _sprintLeft : _sprintRight))
            {
                _sprintRightLeading = !_sprintRightLeading; _cycle = 0f;
                AnimationPhase = _sprintRightLeading ? "SprintUpRight" : "SprintUp"; AnimationProgress = 0f;
                BeginSupportTransfer();
            }
            else ReturnFromSprint(false);
        }
        else if (next <= 0f && input < -.1f) ReturnFromSprint(true);
        return Pose;
    }

    void ReturnFromSprint(bool returnedToEntry)
    {
        _sprinting = false; _speed = 0f; PrimarySupportOnly = true;
        PrimarySupportIsLeft = returnedToEntry && _sprintRightLeading;
        _cycle = PrimarySupportIsLeft ? .5f : 0f;
        ReturnBlendRemaining = _performance.GetPhase("Up").BlendSeconds;
        AnimationPhase = "Up"; AnimationProgress = _cycle; BeginSupportTransfer();
    }

    bool CanSprint(ActorTraversalMotion motion) => motion != null &&
        ClimbHeight + motion.Sample(1f).Root.y < TopClimbHeight + (_topExit?.MaxHeightAdjustment ?? 0f);

    CharacterPose TickSlide(float input, bool fast, float dt)
    {
        float brakeHeight = BottomClimbHeight - _slideEnd.Sample(1f).Root.y + .025f;
        if (!fast || input >= -.1f || ClimbHeight <= brakeHeight + .001f)
        {
            if (!Start(ActorLadderPhase.SlideEnd, Pose.Position + Local(_slideEnd.Sample(1f).Root)))
            { Cancel(); Rejection = "The slide brake is blocked; control returned to the motor."; }
            return Pose;
        }
        float remaining = dt;
        while (remaining > 0f)
        {
            float step = Mathf.Min(remaining, 1f / 60f);
            float progress = Mathf.Min(1f, (_elapsed + step) / _duration);
            Vector3 next = TransitionPosition(progress, Phase);
            if (Vector3.Dot(next - _bottom, _up) < brakeHeight)
            {
                float low = _elapsed / _duration, high = progress;
                for (int i = 0; i < 16; i++)
                {
                    float middle = (low + high) * .5f;
                    if (Vector3.Dot(TransitionPosition(middle, Phase) - _bottom, _up) > brakeHeight) low = middle;
                    else high = middle;
                }
                progress = low; next = TransitionPosition(progress, Phase);
            }
            if (!ClearTransition(_elapsed / _duration, progress, Pose.Position, next))
            { Cancel(); Rejection = "The slide route became blocked; control returned to the motor."; return Pose; }
            Pose = new CharacterPose(next, _up, _forward);
            _elapsed = progress * _duration; AnimationProgress = progress;
            remaining -= step;
            if (ClimbHeight <= brakeHeight + .001f) return Pose;
            if (progress >= 1f) BeginSlideLoop();
        }
        return Pose;
    }

    bool TryTopTransfer()
    {
        bool mirrored = _pendingTopHandover || Mathf.Abs(GaitPhase - .5f) < .04f;
        if (!mirrored && GaitPhase >= .04f && GaitPhase <= .96f) return false;
        var motion = mirrored ? _topExitMirrored : _topExit;
        if (mirrored && motion == null) return false;
        float nativeHeight = _height - (motion?.Sample(1f).Root.y ?? 1.35f);
        if (Mathf.Abs(ClimbHeight - nativeHeight) > (motion?.MaxHeightAdjustment ?? .001f)) return _pendingTopHandover;
        Vector3 end = _bottom + _up * _height + _forward * (motion?.Sample(1f).Root.z ?? .95f);
        if (Start(ActorLadderPhase.ExitTop, end, mirrored)) { _pendingTopHandover = false; return true; }
        // The first eligible pose can still be below the platform's safe route.
        // Finish the authored handover before holding a genuinely blocked exit.
        if (mirrored && GaitPhase >= .5f && GaitPhase < .54f && ClimbHeight < nativeHeight)
        { _pendingTopHandover = true; return false; }
        return true;
    }

    bool TryBottomTransfer()
    {
        bool mirrored = Mathf.Abs(GaitPhase - .5f) < .04f;
        if (!mirrored && GaitPhase >= .04f && GaitPhase <= .96f) return false;
        var motion = mirrored ? _bottomExitMirrored : _bottomExit;
        if (mirrored && motion == null) return false;
        float nativeHeight = -(motion?.Sample(1f).Root.y ?? 0f);
        if (Mathf.Abs(ClimbHeight - nativeHeight) > (motion?.MaxHeightAdjustment ?? .001f)) return false;
        Vector3 end = _bottom + Local(motion != null ? Vector3.ProjectOnPlane(motion.Sample(1f).Root, Vector3.up) : -Vector3.forward * .25f);
        Start(ActorLadderPhase.ExitBottom, end, mirrored);
        return true;
    }
    public void Cancel() { Phase = ActorLadderPhase.None; _speed = 0f; _pendingTopHandover = false; }
}
