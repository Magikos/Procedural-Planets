using UnityEngine;

/// <summary>
/// Actor-agnostic GROUNDED surface-locomotion driver. It composes the pure <see cref="CharacterMotor"/> with
/// an injected <see cref="IGravityProvider"/> + <see cref="IGroundingProvider"/> and advances a settled pose
/// from plain per-tick intent — no Unity input, camera, grass, console, or scene lifecycle, and no planet
/// center / sampler of its own. A bird's grounded state, a bear, a mount, or the player host all reuse it by
/// injecting different capabilities and a different seed.
///
/// Land movement holds pose on ground failure. An optional water query enables buoyant swimming in deep bodies.
/// </summary>
public sealed class SurfaceCharacterController
{
    readonly IGravityProvider _gravity;
    readonly IGroundingProvider _grounding;
    readonly ISwimmingProvider _water;
    readonly SurfaceSwimProfile _swim;
    readonly float _footOffset;
    readonly ActorCollision _collision;

    float _jumpHeight = 1.6f;
    public float JumpHeight
    {
        get => _jumpHeight;
        set
        {
            if (!float.IsFinite(value) || value <= 0f) throw new System.ArgumentOutOfRangeException(nameof(value));
            _jumpHeight = value;
        }
    }
    const float CoyoteTime = .1f;
    const float JumpBufferTime = .12f;

    CharacterPose _pose;
    Vector3 _lastForward;
    float _verticalVel;
    bool _grounded = true;
    bool _jumpHeld;
    float _coyoteRemaining, _jumpBufferRemaining, _airSpeed;
    Vector3 _airDirection;

    /// <summary>The current settled pose (position, up, facing). Read by the host each frame to place the actor.</summary>
    public CharacterPose Pose => _pose;

    /// <summary>True while standing on the surface; false mid-jump/fall.</summary>
    public bool Grounded => _grounded;
    public bool Jumping { get; private set; }
    public bool Swimming { get; private set; }
    public bool Diving { get; private set; }
    public float WaterDepth { get; private set; }

    public SurfaceCharacterController(
        IGravityProvider gravity, IGroundingProvider grounding, float footOffset, CharacterPose seed,
        ISwimmingProvider water = null, SurfaceSwimProfile? swimming = null, ActorCollision collision = null)
    {
        _gravity = gravity;
        _grounding = grounding;
        _water = water;
        _collision = collision;
        _footOffset = Mathf.Max(0f, footOffset);
        _swim = swimming ?? new SurfaceSwimProfile(.3f, Mathf.Max(_footOffset * 1.4f, .5f), .25f);
        ResetPose(seed);
    }

    /// <summary>
    /// Re-seed the stateful pose/facing to a known-good frame — on spawn, respawn, and planet regeneration —
    /// so stale position/facing never carries into a new provider frame, and the driver never first-ticks
    /// from <see cref="Vector3.zero"/> (the invalid planet-center case).
    /// </summary>
    public void ResetPose(CharacterPose seed, bool grounded = true)
    {
        _pose = seed;
        _verticalVel = 0f;
        _grounded = grounded;
        _jumpHeld = false; _coyoteRemaining = _jumpBufferRemaining = _airSpeed = 0f;
        _airDirection = seed.Forward;
        Jumping = false;
        Swimming = false;
        Diving = false; WaterDepth = 0f;
        _lastForward = CharacterMath.TryProjectOntoTangent(seed.Forward, seed.Up, out Vector3 f)
            ? f
            : CharacterMath.ArbitraryTangent(seed.Up);
    }

    /// <summary>
    /// Advance one tick and return the new settled pose. Movement is look-relative: <paramref name="move"/>.y
    /// walks along <paramref name="viewForward"/>, <paramref name="move"/>.x strafes along right — the character
    /// FACES <paramref name="viewForward"/> (not the travel direction), so strafing never turns it. Optional
    /// <paramref name="jump"/> gives an upward hop that gravity integrates and grounding lands. Orientation is
    /// taken from the FINAL frame (no one-frame lean); any capability failure holds the prior pose.
    /// </summary>
    public CharacterPose Tick(Vector2 move, Vector3 viewForward, float speed, float dt, bool jump = false, bool dive = false)
    {
        if (!float.IsFinite(dt) || dt <= 0f || !float.IsFinite(speed) ||
            !float.IsFinite(move.x) || !float.IsFinite(move.y) || !float.IsFinite(move.sqrMagnitude)) return _pose;
        if (!_gravity.TryGetGravity(_pose.Position, out Vector3 accel) || !CharacterMath.IsFinite(accel) ||
            !float.IsFinite(accel.sqrMagnitude) || accel.sqrMagnitude < 1e-12f)
            return _pose;
        Vector3 up = -accel.normalized;
        float g = accel.magnitude;

        // Facing = the look direction projected onto the tangent plane (fall back to the last facing).
        Vector3 face = CharacterMath.TryProjectOntoTangent(
            CharacterMath.IsFinite(viewForward) && viewForward.sqrMagnitude > 1e-8f ? viewForward : _lastForward,
            up, out Vector3 pf) ? pf : CharacterMath.ArbitraryTangent(up);
        _lastForward = face;
        _jumpBufferRemaining = Mathf.Max(0f, _jumpBufferRemaining - dt);
        if (jump && !_jumpHeld) _jumpBufferRemaining = JumpBufferTime;
        _jumpHeld = jump;
        _coyoteRemaining = _grounded ? CoyoteTime : Mathf.Max(0f, _coyoteRemaining - dt);
        bool launchPending = !Jumping && _jumpBufferRemaining > 0f && (_grounded || _coyoteRemaining > 0f);
        float requestedSpeed = speed;
        Vector2 requestedMove = move;
        if (!_grounded && !Swimming)
        {
            Vector3 right = Vector3.Cross(up, face);
            Vector3 desired = right * move.x + face * move.y;
            Vector3 direction = Vector3.ProjectOnPlane(_airDirection, up).normalized;
            if (direction.sqrMagnitude < 1e-8f) direction = face;
            if (desired.sqrMagnitude > 1e-8f) direction = Vector3.RotateTowards(direction, desired.normalized, 2f * dt, 0f);
            _airDirection = direction;
            move = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, face));
            speed = _airSpeed;
        }

        // Horizontal step in the look basis (move.y forward, move.x strafe).
        if (_grounded && !launchPending && _swim.WadingSpeedMultiplier < 1f && _water != null &&
            _water.TryGetDepth(_pose.Position, out float standingDepth, out float standingBodyDepth) &&
            float.IsFinite(standingDepth) && float.IsFinite(standingBodyDepth) &&
            standingBodyDepth > 0f && standingBodyDepth <= _swim.MinimumBodyDepth)
            speed *= Mathf.Lerp(1f, _swim.WadingSpeedMultiplier,
                Mathf.Clamp01(standingDepth / _swim.MinimumBodyDepth));
        if (_grounded || Swimming) CaptureAirMotion(speed, move, up, face);
        if (!CharacterMotor.TryStep(up, _pose.Position, move, face, speed, dt, out Vector3 horiz))
            horiz = _pose.Position;
        if (_collision != null) horiz = _collision.Move(_pose.Position, horiz, up, face, _grounded && !launchPending);

        if (_water != null && _water.TryGetDepth(horiz, out float waterDepth, out float bodyDepth)
            && float.IsFinite(waterDepth) && float.IsFinite(bodyDepth)
            && bodyDepth > _swim.MinimumBodyDepth && waterDepth > -_swim.EntryClearance)
        {
            Swimming = true;
            Jumping = false;
            _grounded = false;
            _verticalVel = 0f;
            _jumpBufferRemaining = _coyoteRemaining = 0f;
            CaptureAirMotion(speed, move, up, face);
            float vertical = dive ? -2.5f : jump ? 2.5f : Diving && _swim.HoldDiveDepth ? 0f
                : Mathf.Clamp((waterDepth - _swim.RootDepth) * 3f, -2f, 2f);
            // Ascent stops at the floating depth. Space must not launch a swimmer above the water.
            float rise = Mathf.Clamp(vertical * dt, waterDepth - Mathf.Max(_swim.RootDepth, bodyDepth - _footOffset),
                Mathf.Max(0f, waterDepth - _swim.RootDepth));
            WaterDepth = waterDepth - rise;
            Diving = WaterDepth > _swim.RootDepth + .15f;
            Vector3 waterPosition = horiz + up * rise;
            if (_collision != null) waterPosition = _collision.Move(horiz, waterPosition, up, face, false);
            WaterDepth = waterDepth - Vector3.Dot(waterPosition - horiz, up);
            _pose = new CharacterPose(waterPosition, up, face);
            return _pose;
        }
        Swimming = false;
        Diving = false; WaterDepth = 0f;

        // Vertical: a jump adds upward speed; gravity integrates it each tick.
        if (launchPending)
        {
            _verticalVel = Mathf.Sqrt(2f * g * JumpHeight);
            Jumping = true;
            _jumpBufferRemaining = _coyoteRemaining = 0f;
        }
        _verticalVel -= g * dt;

        Vector3 candidate = horiz + up * (_verticalVel * dt);
        if (_collision != null)
        {
            Vector3 corrected = _collision.Move(horiz, candidate, up, face, false);
            if (_verticalVel > 0f && Vector3.Dot(candidate - corrected, up) > .001f) _verticalVel = 0f;
            candidate = corrected;
        }

        if (!_grounding.TryGround(candidate, -up, _footOffset, out GroundResult ground))
            return _pose;
        bool hasCapsuleSupport = false;
        if (_collision != null && _verticalVel <= 0f && _collision.Support(candidate, up, face, out var support))
        { ground = support; hasCapsuleSupport = true; }

        Vector3 finalUp = _collision != null ? up : ground.Normal.sqrMagnitude > 1e-10f ? ground.Normal.normalized : up;
        Vector3 forward = CharacterMath.TryProjectOntoTangent(face, finalUp, out Vector3 ff)
            ? ff
            : CharacterMath.ArbitraryTangent(finalUp);

        // Land when at/below the surface and not rising; otherwise stay airborne at the candidate.
        float heightAboveGround = Vector3.Dot(candidate - ground.Position, finalUp);
        float supportTolerance = _collision == null ? 0f : _grounded && !Jumping ?
            (hasCapsuleSupport ? Mathf.Max(.1f, _collision.StepHeight) : .1f) : .002f;
        if (_verticalVel <= 0f && heightAboveGround <= supportTolerance)
        {
            _pose = new CharacterPose(ground.Position, finalUp, forward);
            _verticalVel = 0f;
            _grounded = true;
            Jumping = false;
            if (_jumpBufferRemaining > 0f)
            {
                _verticalVel = Mathf.Sqrt(2f * g * JumpHeight);
                _grounded = false; Jumping = true;
                CaptureAirMotion(requestedSpeed, requestedMove, up, face);
                _jumpBufferRemaining = _coyoteRemaining = 0f;
            }
        }
        else
        {
            _pose = new CharacterPose(candidate, finalUp, forward);
            _grounded = false;
        }
        if (_water != null && _water.TryGetDepth(_pose.Position, out float settledDepth, out float settledBodyDepth) &&
            float.IsFinite(settledDepth) && float.IsFinite(settledBodyDepth) && settledBodyDepth > 0f)
            WaterDepth = Mathf.Max(0f, settledDepth);
        return _pose;
    }

    void CaptureAirMotion(float speed, Vector2 move, Vector3 up, Vector3 forward)
    {
        Vector3 direction = Vector3.Cross(up, forward) * move.x + forward * move.y;
        _airSpeed = speed * Mathf.Min(1f, direction.magnitude);
        _airDirection = direction.sqrMagnitude > 1e-8f ? direction.normalized : forward;
    }
}
