using UnityEngine;

/// <summary>Anchored, length-constrained secondary motion for tails, bone ropes, and cape strips.</summary>
public sealed class BoneChainSpring
{
    readonly Transform[] _bones;
    readonly Vector3[] _positions;
    readonly Vector3[] _velocities;
    readonly Vector3[] _targets;
    readonly float _frequency, _damping, _angle, _weight, _gravity;
    bool _ready;

    public BoneChainSpring(SpringChainDefinition definition)
    {
        LimbPoseSolver.ValidateChain(definition.Bones);
        _bones = (Transform[])definition.Bones.Clone();
        _positions = new Vector3[_bones.Length];
        _velocities = new Vector3[_bones.Length];
        _targets = new Vector3[_bones.Length];
        _frequency = Mathf.Clamp(definition.Frequency, 0.1f, 12f);
        _damping = Mathf.Clamp01(definition.Damping);
        _angle = Mathf.Clamp(definition.AngleLimit, 0f, 90f) * Mathf.Deg2Rad;
        _weight = Mathf.Clamp01(definition.Weight);
        _gravity = Mathf.Max(0f, definition.GravityScale);
    }

    public void Reset() => _ready = false;

    public void Tick(Vector3 gravity, float dt)
    {
        if (dt <= 0f || _weight <= 0f) return;
        for (int i = 0; i < _bones.Length; i++)
        {
            _targets[i] = _bones[i].position;
            if (!_ready) { _positions[i] = _targets[i]; _velocities[i] = Vector3.zero; }
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
                _velocities[i] *= Mathf.Exp(-2f * _damping * omega * h);
                Vector3 desired = previous + _velocities[i] * h - _positions[i - 1];
                Vector3 rest = _targets[i] - _targets[i - 1];
                Vector3 direction = Vector3.RotateTowards(rest.normalized, desired.normalized, _angle, 0f);
                _positions[i] = _positions[i - 1] + direction * rest.magnitude;
                _velocities[i] = (_positions[i] - previous) / h;
            }
        }
        for (int i = 0; i < _bones.Length - 1; i++)
        {
            Vector3 current = _bones[i + 1].position - _bones[i].position;
            Vector3 desired = _positions[i + 1] - _positions[i];
            if (current.sqrMagnitude < 1e-10f || desired.sqrMagnitude < 1e-10f) continue;
            Quaternion offset = Quaternion.FromToRotation(current, desired);
            _bones[i].rotation = Quaternion.Slerp(Quaternion.identity, offset, _weight) * _bones[i].rotation;
        }
    }
}
