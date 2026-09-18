using System;
using UnityEngine;

public sealed class InteractionLimbSolver
{
    readonly Transform _frame;
    readonly LimbPoseSolver _limb;
    readonly Transform[] _bones;
    readonly Vector3 _contactPosition, _bendDirection;
    readonly Quaternion _contactRotation;
    readonly float _jointLimit, _cone, _extension, _wristLimit, _forearmTwist;
    readonly (Transform bone, Quaternion rotation, float limit)[] _joints;
    readonly SphereGripSolver _grip;
    readonly float _contactThickness;
    Vector3 _gripOffset;
    public Vector3 WorldContactPosition => _limb.Tip.TransformPoint(_contactPosition);
    public Quaternion WorldContactRotation => _limb.Tip.rotation * _contactRotation;
    public Vector3 WorldInteractionPosition => WorldContactPosition + _limb.Tip.rotation * _contactRotation * _gripOffset;
    public float Error { get; private set; } = float.PositiveInfinity;
    public float RotationError { get; private set; } = float.PositiveInfinity;
    public bool Reachable => Error <= .02f && RotationError <= 15f && GripPenetration <= .001f;
    public float GripPenetration { get; private set; }

    public InteractionLimbSolver(Transform frame, InteractionLimbDefinition definition)
    {
        if (frame == null || definition == null) throw new ArgumentNullException(nameof(definition));
        _frame = frame; _limb = new LimbPoseSolver(definition.Bones);
        _bones = (Transform[])definition.Bones.Clone();
        if (!CharacterMath.IsFinite(definition.ContactPosition) || !CharacterMath.IsFinite(definition.BendDirection) ||
            !ValidAngle(definition.JointLimit) || !ValidAngle(definition.ReachConeDegrees) ||
            !ValidAngle(definition.WristLimitDegrees) || !ValidAngle(definition.ForearmTwistLimitDegrees) || !float.IsFinite(definition.MaximumExtension) ||
            definition.MaximumExtension <= 0f || definition.MaximumExtension > 1f)
            throw new ArgumentException("Interaction contact limits must be finite and within their valid ranges.");
        _contactPosition = definition.ContactPosition; _contactRotation = Rotation(definition.ContactRotation);
        _contactThickness = definition.ContactThickness;
        _bendDirection = definition.BendDirection; _jointLimit = Mathf.Clamp(definition.JointLimit, 1f, 180f);
        _cone = definition.ReachConeDegrees; _extension = definition.MaximumExtension; _wristLimit = definition.WristLimitDegrees;
        _forearmTwist = definition.ForearmTwistLimitDegrees;
        var authored = definition.ContactJoints ?? Array.Empty<InteractionContactJoint>();
        _joints = new (Transform, Quaternion, float)[authored.Length];
        for (int i = 0; i < authored.Length; i++)
        {
            var joint = authored[i];
            if (joint == null || joint.Bone == null || joint.Bone == _limb.Tip || !joint.Bone.IsChildOf(_limb.Tip) ||
                !ValidAngle(joint.MaxCorrectionDegrees)) throw new ArgumentException("Contact joints must be valid descendants of the limb tip.");
            for (int j = 0; j < i; j++) if (_joints[j].bone == joint.Bone) throw new ArgumentException("Contact joints must be unique.");
            _joints[i] = (joint.Bone, Rotation(joint.LocalRotation), joint.MaxCorrectionDegrees);
        }
        _grip = new SphereGripSolver(_limb.Tip, authored, _contactRotation, _contactThickness);
    }

    public float Solve(InteractionPoseTarget target)
    {
        GripPenetration = 0f;
        _gripOffset = Vector3.zero;
        if (!target.UseContact)
        {
            _limb.Solve(target.Position, target.Weight, _jointLimit, target.Rotation);
            RotationError = 0f;
            return Error = Vector3.Distance(_limb.Tip.position, target.Position);
        }
        Quaternion sampledLocal = _limb.Tip.localRotation;
        Quaternion wristRotation = target.Rotation.HasValue ? target.Rotation.Value * Quaternion.Inverse(_contactRotation) : _limb.Tip.rotation;
        Vector3 contactTarget = target.Position;
        if (target.GripRadius > 0f)
        {
            _gripOffset = new Vector3(0f, target.GripRadius + .5f * _contactPosition.magnitude * _frame.lossyScale.x,
                target.GripRadius + _contactThickness * _frame.lossyScale.x);
            contactTarget -= target.Rotation.Value * _gripOffset;
        }
        Vector3 wristTarget = contactTarget - wristRotation * Vector3.Scale(_contactPosition, _limb.Tip.lossyScale);
        Vector3 delta = wristTarget - _limb.Base.position;
        float length = 0f;
        for (int i = 1; i < _bones.Length; i++) length += Vector3.Distance(_bones[i - 1].position, _bones[i].position);
        if (delta.sqrMagnitude > 1e-10f)
        {
            // Moving contacts correct an authored reach, including swings beside or behind the actor.
            Vector3 reachAxis = target.FollowAuthoredMotion ? (_limb.Tip.position - _limb.Base.position).normalized : _frame.forward;
            Vector3 direction = Vector3.RotateTowards(reachAxis, delta.normalized, _cone * Mathf.Deg2Rad, 0f);
            wristTarget = _limb.Base.position + direction * Mathf.Min(delta.magnitude, length * _extension);
        }
        Vector3? bend = target.BendDirection ?? (!target.FollowAuthoredMotion && _bendDirection.sqrMagnitude > 1e-10f ? _frame.TransformDirection(_bendDirection) : null);
        _limb.Solve(wristTarget, target.Weight, _jointLimit, wristRotation, bend);
        if (target.Rotation.HasValue && _bones.Length == 3 && _forearmTwist > 0f)
        {
            // Pronation belongs to the forearm. Keep the wrist bend separate from rotation around the arm's length.
            _limb.Tip.localRotation = sampledLocal;
            Vector3 axis = (_limb.Tip.position - _bones[1].position).normalized;
            Quaternion deltaRotation = wristRotation * Quaternion.Inverse(_limb.Tip.rotation);
            float twist = 2f * Mathf.Atan2(Vector3.Dot(new Vector3(deltaRotation.x, deltaRotation.y, deltaRotation.z), axis),
                deltaRotation.w) * Mathf.Rad2Deg;
            twist = Mathf.DeltaAngle(0f, twist);
            _bones[1].rotation = Quaternion.AngleAxis(Mathf.Clamp(twist, -_forearmTwist, _forearmTwist) * target.Weight, axis) * _bones[1].rotation;
            _limb.Tip.rotation = Quaternion.Slerp(_limb.Tip.rotation, wristRotation, target.Weight);
        }
        _limb.Tip.localRotation = Quaternion.RotateTowards(sampledLocal, _limb.Tip.localRotation, _wristLimit * target.Weight);
        if (!target.PreserveAuthoredContactJoints)
            foreach (var joint in _joints)
                joint.bone.localRotation = Quaternion.RotateTowards(joint.bone.localRotation, joint.rotation, joint.limit * target.Weight);
        RotationError = target.Rotation.HasValue
            ? Quaternion.Angle(_limb.Tip.rotation * _contactRotation, target.Rotation.Value) : 0f;
        Error = Vector3.Distance(WorldInteractionPosition, target.Position);
        if (target.GripRadius > 0f)
        {
            float closure = target.Weight * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.02f, .12f, Error)));
            _grip.Solve(target.Position, target.GripRadius, closure);
            GripPenetration = _grip.MaxPenetration;
        }
        return Error;
    }

    static bool ValidAngle(float value) => float.IsFinite(value) && value >= 0f && value <= 180f;
    static Quaternion Rotation(Quaternion value)
    {
        float magnitude = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
        if (!float.IsFinite(magnitude) || magnitude < 1e-8f) throw new ArgumentException("Contact rotation must be finite and nonzero.");
        return value.normalized;
    }
}
