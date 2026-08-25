using UnityEngine;

/// <summary>
/// Actor-agnostic GROUNDED surface-locomotion driver. It composes the pure <see cref="CharacterMotor"/> with
/// an injected <see cref="IGravityProvider"/> + <see cref="IGroundingProvider"/> and advances a settled pose
/// from plain per-tick intent — no Unity input, camera, grass, console, or scene lifecycle, and no planet
/// center / sampler of its own. A bird's grounded state, a bear, a mount, or the player host all reuse it by
/// injecting different capabilities and a different seed.
///
/// It ALWAYS grounds and holds pose on ground failure, and uses only the gravity direction — so a gravity
/// swap alone does not make it fly. A future airborne behavior is a different driver that reuses
/// <see cref="CharacterMotor"/> + <see cref="IGravityProvider"/>; a behavior state machine selects between them.
/// </summary>
public sealed class SurfaceCharacterController
{
    readonly IGravityProvider _gravity;
    readonly IGroundingProvider _grounding;
    readonly float _footOffset;

    const float JumpHeight = 1.6f;

    CharacterPose _pose;
    Vector3 _lastForward;
    float _verticalVel;
    bool _grounded = true;

    /// <summary>The current settled pose (position, up, facing). Read by the host each frame to place the actor.</summary>
    public CharacterPose Pose => _pose;

    /// <summary>True while standing on the surface; false mid-jump/fall.</summary>
    public bool Grounded => _grounded;

    public SurfaceCharacterController(
        IGravityProvider gravity, IGroundingProvider grounding, float footOffset, CharacterPose seed)
    {
        _gravity = gravity;
        _grounding = grounding;
        _footOffset = Mathf.Max(0f, footOffset);
        ResetPose(seed);
    }

    /// <summary>
    /// Re-seed the stateful pose/facing to a known-good frame — on spawn, respawn, and planet regeneration —
    /// so stale position/facing never carries into a new provider frame, and the driver never first-ticks
    /// from <see cref="Vector3.zero"/> (the invalid planet-center case).
    /// </summary>
    public void ResetPose(CharacterPose seed)
    {
        _pose = seed;
        _verticalVel = 0f;
        _grounded = true;
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
    public CharacterPose Tick(Vector2 move, Vector3 viewForward, float speed, float dt, bool jump = false)
    {
        if (!_gravity.TryGetGravity(_pose.Position, out Vector3 accel))
            return _pose;
        Vector3 up = -accel.normalized;
        float g = accel.magnitude;

        // Facing = the look direction projected onto the tangent plane (fall back to the last facing).
        Vector3 face = CharacterMath.TryProjectOntoTangent(
            CharacterMath.IsFinite(viewForward) && viewForward.sqrMagnitude > 1e-8f ? viewForward : _lastForward,
            up, out Vector3 pf) ? pf : _lastForward;
        _lastForward = face;

        // Horizontal step in the look basis (move.y forward, move.x strafe).
        if (!CharacterMotor.TryStep(up, _pose.Position, move, face, speed, dt, out Vector3 horiz))
            horiz = _pose.Position;

        // Vertical: a jump adds upward speed; gravity integrates it each tick.
        if (_grounded && jump)
            _verticalVel = Mathf.Sqrt(2f * g * JumpHeight);
        _verticalVel -= g * dt;

        Vector3 candidate = horiz + up * (_verticalVel * dt);

        if (!_grounding.TryGround(candidate, -up, _footOffset, out GroundResult ground))
            return _pose;

        Vector3 finalUp = ground.Normal.sqrMagnitude > 1e-10f ? ground.Normal.normalized : up;
        Vector3 forward = CharacterMath.TryProjectOntoTangent(face, finalUp, out Vector3 ff)
            ? ff
            : CharacterMath.ArbitraryTangent(finalUp);

        // Land when at/below the surface and not rising; otherwise stay airborne at the candidate.
        float heightAboveGround = Vector3.Dot(candidate - ground.Position, finalUp);
        if (_verticalVel <= 0f && heightAboveGround <= 0f)
        {
            _pose = new CharacterPose(ground.Position, finalUp, forward);
            _verticalVel = 0f;
            _grounded = true;
        }
        else
        {
            _pose = new CharacterPose(candidate, finalUp, forward);
            _grounded = false;
        }
        return _pose;
    }
}
