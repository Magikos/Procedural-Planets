using UnityEngine;

/// <summary>Water paddling uses the same limb bindings and solver as terrain IK, without planted contacts.</summary>
public sealed class SwimmingPose
{
    readonly Transform _frame, _body;
    readonly Transform[] _look;
    readonly LimbPoseSolver[] _limbs;
    readonly float _height;

    public SwimmingPose(Transform frame, ProceduralRigDefinition rig, float height)
    {
        _frame = frame; _body = rig.Body; _look = (Transform[])rig.Look.Clone(); _height = height;
        _limbs = new LimbPoseSolver[rig.Feet.Length];
        for (int i = 0; i < _limbs.Length; i++) _limbs[i] = new LimbPoseSolver(rig.Feet[i].Bones);
    }

    public void Apply(float time, float weight, float waterline = .7f)
    {
        weight = Mathf.Clamp01(weight);
        if (_body != null) _body.position += _frame.up * (Mathf.Sin(time * Mathf.PI * 4f) * _height * .008f * weight);
        // Raise the neck rather than the entire animal. Legs and belly remain below the waterline.
        if (_look.Length > 0)
        {
            Transform neck = _look[0];
            Vector3 muzzle = _look[^1].position + _frame.forward * (_height * .12f);
            Vector3 offset = muzzle - neck.position;
            float length = offset.magnitude;
            float desiredHeight = Vector3.Dot(_frame.position - neck.position, _frame.up) +
                _height * (Mathf.Clamp(waterline, .1f, .85f) - .5f + .08f);
            if (length > .0001f && Vector3.Dot(offset, _frame.up) < desiredHeight)
            {
                float rise = Mathf.Clamp(desiredHeight, -length * .95f, length * .95f);
                Vector3 horizontal = Vector3.ProjectOnPlane(offset, _frame.up).normalized;
                Vector3 target = horizontal * Mathf.Sqrt(length * length - rise * rise) + _frame.up * rise;
                Quaternion lift = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(offset, target), 50f);
                neck.rotation = Quaternion.Slerp(Quaternion.identity, lift, weight) * neck.rotation;
            }
        }
        foreach (var limb in _limbs)
        {
            Vector3 local = _frame.InverseTransformPoint(limb.Base.position);
            float phase = time * Mathf.PI * 2f * 1.6f + (local.x < 0f ? Mathf.PI : 0f) + (local.z < 0f ? Mathf.PI * .5f : 0f);
            float reach = Vector3.Distance(limb.Base.position, limb.Tip.position);
            Vector3 target = limb.Base.position - _frame.up * (reach * (.6f + .12f * Mathf.Cos(phase)))
                + _frame.forward * (reach * .3f * Mathf.Sin(phase));
            limb.Solve(target, weight, 75f);
        }
    }
}
