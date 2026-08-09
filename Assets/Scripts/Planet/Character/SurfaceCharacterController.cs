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

    CharacterPose _pose;
    Vector3 _lastForward;

    /// <summary>The current settled pose (position, up, facing). Read by the host each frame to place the actor.</summary>
    public CharacterPose Pose => _pose;

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
        _lastForward = CharacterMath.TryProjectOntoTangent(seed.Forward, seed.Up, out Vector3 f)
            ? f
            : CharacterMath.ArbitraryTangent(seed.Up);
    }

    /// <summary>
    /// Advance one tick and return the new settled pose. Any capability failure holds the prior pose (never
    /// invents a direction, never lets a NaN escape). Orientation is taken from the FINAL grounded frame
    /// (up = the settled ground normal), not the pre-step up, so the character never leans for a frame.
    /// </summary>
    public CharacterPose Tick(Vector2 move, Vector3 viewForward, float speed, float dt)
    {
        if (!_gravity.TryGetGravity(_pose.Position, out Vector3 accel))
            return _pose;
        Vector3 up = -accel.normalized;

        // The camera basis for the step: the caller's view forward, else the latched facing.
        Vector3 stepForward = CharacterMath.IsFinite(viewForward) && viewForward.sqrMagnitude > 1e-8f
            ? viewForward
            : _lastForward;

        if (!CharacterMotor.TryStep(up, _pose.Position, move, stepForward, speed, dt, out Vector3 next))
            return _pose;

        if (!_grounding.TryGround(next, -up, _footOffset, out GroundResult ground))
            return _pose;

        Vector3 finalUp = ground.Normal.sqrMagnitude > 1e-10f ? ground.Normal.normalized : up;

        // Face the direction actually travelled along the final tangent plane; keep the last facing when idle.
        Vector3 travelled = ground.Position - _pose.Position;
        if (CharacterMath.TryProjectOntoTangent(travelled, finalUp, out Vector3 travelDir))
            _lastForward = travelDir;

        Vector3 forward = CharacterMath.TryProjectOntoTangent(_lastForward, finalUp, out Vector3 f)
            ? f
            : CharacterMath.ArbitraryTangent(finalUp);

        _pose = new CharacterPose(ground.Position, finalUp, forward);
        return _pose;
    }
}
