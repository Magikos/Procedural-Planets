using System;
using UnityEngine;

public enum ActorAirbornePhase { Grounded, Ascent, Apex, Descent, Falling, Landing }

public sealed class ActorAirborneMotion
{
    const float ApexSpeed = .1f;
    float _recoveryDuration;
    readonly float _minimumDrop;
    bool _airborne;
    float _maximumDownSpeed;

    public ActorAirbornePhase Phase { get; private set; }
    public float PhaseTime { get; private set; }
    public float AirTime { get; private set; }
    public float DropDistance { get; private set; }
    public float LandingSpeed { get; private set; }
    public bool IntentionalJump { get; private set; }
    public float TakeoffSpeed { get; private set; }
    public float AscentProgress { get; private set; }
    public float DescentProgress { get; private set; }
    public bool Active => Phase != ActorAirbornePhase.Grounded;
    public float RecoveryDuration
    {
        get => _recoveryDuration;
        set
        {
            if (!float.IsFinite(value) || value < 0f) throw new ArgumentOutOfRangeException(nameof(value));
            _recoveryDuration = value;
        }
    }

    public ActorAirborneMotion(float recoveryDuration = .2f, float minimumDrop = .25f)
    {
        if (!float.IsFinite(recoveryDuration) || recoveryDuration < 0f)
            throw new ArgumentOutOfRangeException(nameof(recoveryDuration));
        if (!float.IsFinite(minimumDrop) || minimumDrop < 0f)
            throw new ArgumentOutOfRangeException(nameof(minimumDrop));
        _recoveryDuration = recoveryDuration; _minimumDrop = minimumDrop;
    }

    public void Advance(float dt, bool grounded, bool jumping, Vector3 velocity, Vector3 up, bool suspended = false)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!CharacterMath.IsFinite(velocity)) throw new ArgumentOutOfRangeException(nameof(velocity));
        if (!CharacterMath.IsFinite(up) || !float.IsFinite(up.sqrMagnitude) || up.sqrMagnitude < .001f)
            throw new ArgumentOutOfRangeException(nameof(up));
        if (dt == 0f) return;
        if (suspended) { Reset(); return; }
        float vertical = Vector3.Dot(velocity, up.normalized);
        if (!float.IsFinite(vertical)) throw new ArgumentOutOfRangeException(nameof(velocity));
        if (grounded)
        {
            if (_airborne)
            {
                _airborne = false;
                LandingSpeed = _maximumDownSpeed;
                if (_recoveryDuration > 0f && (IntentionalJump || DropDistance >= _minimumDrop))
                    ChangePhase(ActorAirbornePhase.Landing, dt);
                else Reset();
            }
            else if (Phase == ActorAirbornePhase.Landing)
            {
                PhaseTime += dt;
                if (PhaseTime >= _recoveryDuration) Reset();
            }
            return;
        }
        if (!_airborne)
        {
            Reset();
            _airborne = true;
            TakeoffSpeed = Mathf.Max(0f, vertical);
        }
        IntentionalJump |= jumping;
        AirTime += dt;
        float downSpeed = Mathf.Max(0f, -vertical);
        DropDistance += downSpeed * dt;
        _maximumDownSpeed = Mathf.Max(_maximumDownSpeed, downSpeed);
        float referenceSpeed = Mathf.Max(ApexSpeed, TakeoffSpeed);
        AscentProgress = Mathf.Max(AscentProgress, Mathf.Clamp01(1f - Mathf.Max(0f, vertical) / referenceSpeed));
        DescentProgress = Mathf.Max(DescentProgress, Mathf.Clamp01(downSpeed / referenceSpeed));
        ActorAirbornePhase next = !IntentionalJump ? ActorAirbornePhase.Falling :
            vertical > ApexSpeed ? ActorAirbornePhase.Ascent :
            vertical < -ApexSpeed ? ActorAirbornePhase.Descent : ActorAirbornePhase.Apex;
        ChangePhase(next, dt);
    }

    void ChangePhase(ActorAirbornePhase next, float dt)
    {
        PhaseTime = Phase == next ? PhaseTime + dt : 0f;
        Phase = next;
    }

    public void Reset()
    {
        Phase = ActorAirbornePhase.Grounded;
        PhaseTime = AirTime = DropDistance = LandingSpeed = TakeoffSpeed = AscentProgress = DescentProgress = _maximumDownSpeed = 0f;
        IntentionalJump = _airborne = false;
    }
}
