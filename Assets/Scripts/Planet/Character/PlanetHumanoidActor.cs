using System;
using UnityEngine;

public sealed class PlanetHumanoidActor : HumanoidActorController
{
    IGravityProvider _gravity;
    IGroundingProvider _ground;
    ISwimmingProvider _water;

    public void Configure(CharacterPose seed, IGravityProvider gravity, IGroundingProvider ground, ISwimmingProvider water)
    {
        if (gameObject.activeInHierarchy || Motor != null)
            throw new InvalidOperationException("Configure the planet actor before activation.");
        if (!seed.IsFinite) throw new ArgumentException("Spawn pose must be finite.", nameof(seed));
        _gravity = gravity ?? throw new ArgumentNullException(nameof(gravity));
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _water = water;
        transform.SetPositionAndRotation(seed.Position, Quaternion.LookRotation(seed.Forward, seed.Up));
        ShowControls = FollowActor = WalkLoop = Running = Reach = false;
        Crouch = Crawl = Dive = SwimUp = RequestJump = false;
        InteractionCrouch = InteractionMovementLocked = false;
        InteractionPosition = InteractionForward = InteractionApproachPosition = InteractionApproachForward = null;
        RightInteractionTarget = LeftInteractionTarget = null;
    }

    protected override void OnEnable()
    {
        if (_gravity == null || _ground == null)
            throw new InvalidOperationException("Planet actor environment is not configured.");
        base.OnEnable();
        if (Motor != null) ResetActor();
    }

    public override bool TryGetGravity(Vector3 position, out Vector3 acceleration)
    {
        acceleration = default;
        return _gravity != null && _gravity.TryGetGravity(position, out acceleration);
    }

    public override bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
    {
        result = default;
        if (!CharacterMath.IsFinite(position) || !CharacterMath.IsFinite(down) || down.sqrMagnitude < .001f)
            return false;
        Vector3 up = -down.normalized;
        bool grounded = _ground != null && _ground.TryGround(position, down, offset, out result);
        // Nearby props can stand above the terrain provider's surface.
        if (Physics.Raycast(position + up * .4f, -up, out var hit, .9f, 1 << 0, QueryTriggerInteraction.Ignore))
        {
            Vector3 point = hit.point + up * offset;
            if (!grounded || Vector3.Dot(point - result.Position, up) > 0f)
            { result = new GroundResult(point, hit.normal); return true; }
        }
        return grounded;
    }

    public override bool TryGetDepth(Vector3 position, out float signedDepth, out float bodyDepth)
    {
        signedDepth = bodyDepth = 0f;
        return _water != null && _water.TryGetDepth(position, out signedDepth, out bodyDepth);
    }
}
