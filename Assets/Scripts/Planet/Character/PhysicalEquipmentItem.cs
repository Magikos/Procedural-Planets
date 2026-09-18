using UnityEngine;

/// <summary>Single transform owner for attached, hand-controlled, and dropped gear.</summary>
[RequireComponent(typeof(HeldToolGrip), typeof(Rigidbody))]
public sealed class PhysicalEquipmentItem : MonoBehaviour
{
    public HeldToolGrip Grip;
    public HumanBodyBones StorageBone = HumanBodyBones.Hips;
    public Vector3 StoragePosition;
    public Vector3 StorageEuler;
    public string StorageSlot = "Hip";
    public PhysicalEquipmentState State { get; private set; }
    Transform _storage;
    Rigidbody _body;
    Collider[] _colliders;
    Vector3 _previous, _velocity;
    Quaternion _previousRotation;
    IGravityProvider _gravity;
    Vector3 _angularVelocity;
    Pose _handoffOffset;
    float _handoff = 1f;

    public void Bind(EntityId item, EntityId owner, Animator animator, IGravityProvider gravity)
    {
        if (Grip == null) Grip = GetComponent<HeldToolGrip>();
        _body = GetComponent<Rigidbody>(); _colliders = GetComponentsInChildren<Collider>();
        _gravity = gravity ?? throw new System.ArgumentNullException(nameof(gravity));
        _storage = animator.GetBoneTransform(StorageBone);
        if (_storage == null || Grip.Primary == null) throw new System.InvalidOperationException("Gear requires storage and grip anchors.");
        State = new PhysicalEquipmentState(item, owner, EquipmentLocation.Stowed, StorageSlot);
        SetPhysics(false); _handoff = 1;
        var pose = StoragePose(); transform.SetPositionAndRotation(pose.position, pose.rotation);
        _previous = transform.position; _previousRotation = transform.rotation;
        _velocity = _angularVelocity = Vector3.zero;
    }

    public Pose StoragePose() => new(_storage.TransformPoint(StoragePosition), _storage.rotation * Quaternion.Euler(StorageEuler));

    public bool Contact(EquipmentLocation destination, Pose destinationPose, float maximumDistance = .12f)
    {
        if (State == null || State.Location == EquipmentLocation.World || !WithinContact(destinationPose, maximumDistance)) return false;
        State.Finish();
        if (!State.Begin(destination, destination == EquipmentLocation.Hand ? Grip.PrimaryId : StorageSlot)) return false;
        if (!State.Contact(true)) return false;
        PreserveHandoff(destinationPose);
        return true;
    }

    public bool Pickup(EntityId owner, Pose hand, float maximumDistance = .08f)
    {
        if (State == null || State.Location != EquipmentLocation.World || !WithinContact(hand, maximumDistance) ||
            !State.Acquire(owner, Grip.PrimaryId, true)) return false;
        SetPhysics(false);
        PreserveHandoff(hand);
        _previous = transform.position; _previousRotation = transform.rotation;
        _velocity = _angularVelocity = Vector3.zero;
        return true;
    }

    void PreserveHandoff(Pose destinationPose)
    {
        // Preserve the visible pose at control transfer, then settle only the residual contact error.
        _handoffOffset = new Pose(Quaternion.Inverse(destinationPose.rotation) * (transform.position - destinationPose.position),
            Quaternion.Inverse(destinationPose.rotation) * transform.rotation);
        _handoff = 0;
    }

    bool WithinContact(Pose destination, float maximumDistance)
    {
        if (!float.IsFinite(maximumDistance) || maximumDistance <= 0 || !CharacterMath.IsFinite(destination.position)) return false;
        float distance = Vector3.Distance(Grip.Primary.position, Grip.FittedPoint(Grip.Primary, destination));
        return float.IsFinite(distance) && distance <= maximumDistance;
    }

    public void Present(Pose hand, float dt)
    {
        if (State == null || State.Location == EquipmentLocation.World) return;
        var target = State.Location == EquipmentLocation.Hand ? hand : StoragePose();
        _handoff = Mathf.MoveTowards(_handoff, 1, dt / .16f);
        float remaining = 1 - Mathf.SmoothStep(0, 1, _handoff);
        var position = target.position + target.rotation * (_handoffOffset.position * remaining);
        var rotation = target.rotation * Quaternion.Slerp(Quaternion.identity, _handoffOffset.rotation, remaining);
        transform.SetPositionAndRotation(position, rotation);
        if (dt > 0)
        {
            _velocity = (position - _previous) / dt;
            var delta = rotation * Quaternion.Inverse(_previousRotation);
            delta.ToAngleAxis(out float angle, out var axis);
            if (angle > 180) angle -= 360;
            _angularVelocity = angle != 0 && CharacterMath.IsFinite(axis) ? axis * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
        }
        _previous = position; _previousRotation = rotation;
        if (_handoff >= 1) State.Finish();
    }

    public void Drop()
    {
        if (State == null || State.Location == EquipmentLocation.World) return;
        State.Release(); SetPhysics(true);
        _body.linearVelocity = Vector3.ClampMagnitude(_velocity, 8);
        _body.angularVelocity = Vector3.ClampMagnitude(_angularVelocity, 15);
    }

    void SetPhysics(bool world)
    {
        _body.isKinematic = !world;
        _body.useGravity = false;
        foreach (var collider in _colliders) collider.enabled = world;
    }

    void FixedUpdate()
    {
        if (State?.Location == EquipmentLocation.World && _gravity.TryGetGravity(transform.position, out var acceleration) &&
            CharacterMath.IsFinite(acceleration)) _body.AddForce(acceleration, ForceMode.Acceleration);
    }
}
