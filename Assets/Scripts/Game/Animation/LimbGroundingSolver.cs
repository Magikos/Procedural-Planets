using System;
using UnityEngine;

/// <summary>Supports a low body pose and its limbs without changing bone lengths or actor position.</summary>
public sealed class LimbGroundingSolver
{
    readonly Transform _body;
    readonly Transform _frame;
    readonly Transform[][] _chains;
    readonly LimbPoseSolver[] _solvers;
    readonly FootPlacementSolver[] _contacts;
    readonly Vector3[] _previousLocal;
    readonly float[] _contactWeights;
    bool _sampled;
    readonly Vector3[] _targets, _poles;
    readonly float _clearance, _limit;
    float _lift;

    public LimbGroundingSolver(Transform body, Transform[][] chains, float clearance, float limit, Transform frame = null)
    {
        if (body == null || chains == null || chains.Length == 0 || !float.IsFinite(clearance) ||
            !float.IsFinite(limit) || clearance < 0f || limit <= 0f) throw new ArgumentException("Invalid limb ground support.");
        _body = body; _clearance = clearance; _limit = limit;
        _frame = frame != null ? frame : body.root;
        _chains = new Transform[chains.Length][];
        _solvers = new LimbPoseSolver[chains.Length];
        _contacts = new FootPlacementSolver[chains.Length];
        _previousLocal = new Vector3[chains.Length]; _contactWeights = new float[chains.Length];
        _targets = new Vector3[chains.Length]; _poles = new Vector3[chains.Length];
        for (int i = 0; i < chains.Length; i++)
        {
            if (chains[i] == null || chains[i].Length != 3) throw new ArgumentException("Ground support requires two-bone limbs.");
            _chains[i] = (Transform[])chains[i].Clone();
            _solvers[i] = new LimbPoseSolver(_chains[i]);
            _contacts[i] = new FootPlacementSolver(new FootDefinition { Bones = _chains[i],
                SoleOffset = clearance, MaxCorrection = limit, JointLimit = 90f,
                BendDirection = _frame.InverseTransformDirection(_chains[i][1].position - _chains[i][0].position)
            }, 1f, _frame);
        }
    }

    public void Reset()
    {
        _lift = 0f; _sampled = false;
        foreach (var contact in _contacts) contact.Reset();
    }

    public void Tick(IGroundingProvider ground, Vector3 up, float weight, float dt, Vector3 velocity = default)
    {
        if (ground == null || weight <= 0f || dt <= 0f) { Reset(); return; }
        up.Normalize();
        float rise = 0f;
        for (int i = 0; i < _chains.Length; i++)
        {
            var chain = _chains[i];
            Vector3 local = _frame.InverseTransformPoint(chain[2].position);
            float travel = _sampled ? Vector3.Dot((local - _previousLocal[i]) / dt,
                _frame.InverseTransformDirection(velocity.normalized)) : 0f;
            // Backward motion relative to travel is the supporting stroke. Forward motion releases the contact.
            if (velocity.sqrMagnitude < .0004f) _contactWeights[i] = 1f;
            else if (!_sampled || travel > .02f) _contactWeights[i] = 0f;
            else if (travel < -.02f) _contactWeights[i] = 1f;
            _previousLocal[i] = local;
            float middleLift = Penetration(chain[1].position), tipLift = Penetration(chain[2].position);
            rise = Mathf.Max(rise, Mathf.Max(middleLift, tipLift));
            _targets[i] = chain[2].position + up * tipLift;
            _poles[i] = chain[1].position + up * middleLift;
        }
        // Clear entering ground immediately; release excess body lift gradually.
        _lift = Mathf.Max(rise, Mathf.Lerp(_lift, rise, 1f - Mathf.Exp(-12f * dt)));
        _body.position += up * (_lift * weight);
        for (int i = 0; i < _chains.Length; i++)
            _solvers[i].Solve(_targets[i], weight, 90f, _chains[i][2].rotation, _poles[i] - _chains[i][0].position);
        for (int i = 0; i < _contacts.Length; i++)
            _contacts[i].Tick(ground, up, 0f, velocity.sqrMagnitude > .0004f ? 1f : 0f, 0f, dt,
                contactWeight: _contactWeights[i] * weight, lockStance: true);
        // Contact locking can lower a knee or elbow. Correct after every limb solve without changing tangent anchors or bone lengths.
        float residual = 0f;
        foreach (var chain in _chains)
            for (int i = 1; i < 3; i++) residual = Mathf.Max(residual, Penetration(chain[i].position));
        _body.position += up * (residual * weight);
        _sampled = true;

        float Penetration(Vector3 point) => ground.TryGround(point, -up, _clearance, out var hit) &&
            CharacterMath.IsFinite(hit.Position) ? Mathf.Clamp(Vector3.Dot(hit.Position - point, up), 0f, _limit) : 0f;
    }
}
