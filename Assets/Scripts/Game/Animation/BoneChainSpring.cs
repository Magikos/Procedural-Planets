using UnityEngine;

/// <summary>Anchored, length-constrained secondary motion for tails, bone ropes, and cape strips.</summary>
public sealed class BoneChainSpring
{
    readonly Transform[] _bones;
    readonly Vector3[] _positions;
    readonly Vector3[] _velocities;
    readonly Vector3[] _targets;
    readonly float _frequency, _damping, _angle, _cosAngle, _weight, _gravity;
    bool _ready;
    readonly float _clearance;
    readonly GroundResult[] _contacts;
    readonly bool[] _hasContact;
    public float MaxGroundPenetration { get; private set; }

    public BoneChainSpring(SpringChainDefinition definition)
    {
        LimbPoseSolver.ValidateChain(definition.Bones);
        _bones = (Transform[])definition.Bones.Clone();
        _clearance = Mathf.Max(0f, definition.GroundClearance);
        _contacts = new GroundResult[_bones.Length];
        _hasContact = new bool[_bones.Length];
        _positions = new Vector3[_bones.Length];
        _velocities = new Vector3[_bones.Length];
        _targets = new Vector3[_bones.Length];
        _frequency = Mathf.Clamp(definition.Frequency, 0.1f, 12f);
        _damping = Mathf.Clamp01(definition.Damping);
        _angle = Mathf.Clamp(definition.AngleLimit, 0f, 90f) * Mathf.Deg2Rad;
        _cosAngle = Mathf.Cos(_angle);
        _weight = Mathf.Clamp01(definition.Weight);
        _gravity = Mathf.Max(0f, definition.GravityScale);
    }

    public void Reset() => _ready = false;

    public void Tick(Vector3 gravity, float dt, float drag = 0f, IGroundingProvider ground = null, Vector3? up = null,
        float influence = 1f)
    {
        if (!float.IsFinite(drag) || drag < 0f) throw new System.ArgumentOutOfRangeException(nameof(drag));
        if (!float.IsFinite(influence) || influence < 0f || influence > 1f)
            throw new System.ArgumentOutOfRangeException(nameof(influence));
        if (!float.IsFinite(dt) || dt <= 0f || _weight <= 0f) return;
        drag = Mathf.Min(drag, 100f);
        MaxGroundPenetration = 0f;
        Vector3 normalUp = (up ?? -gravity).normalized;
        for (int i = 0; i < _bones.Length; i++)
        {
            _targets[i] = _bones[i].position;
            if (!_ready) { _positions[i] = _targets[i]; _velocities[i] = Vector3.zero; }
            _hasContact[i] = i > 0 && _clearance > 0f && ground != null && normalUp.sqrMagnitude > .5f
                && ground.TryGround(_positions[i], -normalUp, _clearance, out _contacts[i]);
        }
        _ready = true;
        // Substeps bound integration cost and stability after a slow frame. Teleports are reset by the owner.
        float elapsed = Mathf.Min(dt, 0.1f);
        int steps = Mathf.CeilToInt(elapsed * 120f);
        float h = elapsed / steps;
        float omega = 2f * Mathf.PI * _frequency;
        for (int step = 0; step < steps; step++)
        {
            _positions[0] = _targets[0];
            for (int i = 1; i < _bones.Length; i++)
            {
                Vector3 previous = _positions[i];
                _velocities[i] += ((_targets[i] - previous) * (omega * omega) + gravity * _gravity) * h;
                _velocities[i] *= Mathf.Exp(-(2f * _damping * omega + drag) * h);
                Vector3 desired = previous + _velocities[i] * h - _positions[i - 1];
                Vector3 rest = _targets[i] - _targets[i - 1];
                Vector3 restDirection = rest.normalized;
                Vector3 desiredDirection = desired.sqrMagnitude > 1e-10f ? desired.normalized : restDirection;
                // Preserve small motion inside the cone; RotateTowards can discard near-parallel changes.
                Vector3 direction = _angle <= 0f ? restDirection
                    : Vector3.Dot(restDirection, desiredDirection) >= _cosAngle ? desiredDirection
                    : Vector3.RotateTowards(restDirection, desiredDirection, _angle, 0f);
                _positions[i] = _positions[i - 1] + direction * rest.magnitude;
                if (_hasContact[i])
                {
                    Vector3 normal = _contacts[i].Normal.normalized;
                    float penetration = Vector3.Dot(_contacts[i].Position - _positions[i], normal);
                    if (penetration > 0f)
                    {
                        // Intersect the fixed-length sphere with the sampled support plane.
                        float rise = Vector3.Dot(_contacts[i].Position - _positions[i - 1], normal);
                        float length = rest.magnitude;
                        Vector3 tangent = Vector3.ProjectOnPlane(direction, normal).normalized;
                        if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.ProjectOnPlane(restDirection, normal).normalized;
                        if (length > 1e-8f && Mathf.Abs(rise) <= length && tangent.sqrMagnitude > .5f)
                        {
                            Vector3 contactDirection = (normal * rise + tangent * Mathf.Sqrt(Mathf.Max(0f, length * length - rise * rise))) / length;
                            if (Vector3.Dot(contactDirection, restDirection) >= _cosAngle - .00001f)
                                _positions[i] = _positions[i - 1] + contactDirection * length;
                        }
                    }
                }
                _velocities[i] = (_positions[i] - previous) / h;
            }
        }
        for (int i = 0; i < _bones.Length - 1; i++)
        {
            Vector3 current = _bones[i + 1].position - _bones[i].position;
            Vector3 desired = _positions[i + 1] - _positions[i];
            if (current.sqrMagnitude < 1e-10f || desired.sqrMagnitude < 1e-10f) continue;
            Quaternion offset = Quaternion.FromToRotation(current, desired);
            _bones[i].rotation = Quaternion.Slerp(Quaternion.identity, offset, _weight * influence) * _bones[i].rotation;
        }
        for (int i = 1; i < _bones.Length; i++)
            if (_hasContact[i]) MaxGroundPenetration = Mathf.Max(MaxGroundPenetration,
                Vector3.Dot(_contacts[i].Position - _bones[i].position, _contacts[i].Normal.normalized));
    }
}
