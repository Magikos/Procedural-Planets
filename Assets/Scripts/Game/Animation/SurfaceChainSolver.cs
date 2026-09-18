using System;
using UnityEngine;

/// <summary>Projects an authored articulated pose onto a surface without changing its tangent motion.</summary>
public sealed class SurfaceChainSolver
{
    readonly Transform[] _bones;
    readonly Vector3[] _animated, _targets;
    readonly float[] _offsets;
    readonly int[] _children;
    readonly Quaternion[] _rotations;
    readonly float _clearance, _limit, _response;
    bool _ready;

    public SurfaceChainSolver(SurfaceChainDefinition definition, float scale)
    {
        if (definition == null || definition.Bones == null || definition.Bones.Length < 2)
            throw new ArgumentException("Surface support requires at least two ordered bones.");
        _bones = (Transform[])definition.Bones.Clone();
        for (int i = 0; i < _bones.Length; i++)
        {
            if (_bones[i] == null) throw new ArgumentException("Surface support contains a missing bone.");
            for (int j = i + 1; j < _bones.Length; j++)
                if (_bones[i] == _bones[j] || (_bones[j] != null && _bones[i].IsChildOf(_bones[j])))
                    throw new ArgumentException("Surface support bones must be unique and ordered parent before child.");
        }
        _children = new int[_bones.Length];
        for (int i = 0; i < _bones.Length; i++)
        {
            _children[i] = -1;
            for (int j = i + 1; j < _bones.Length; j++)
                if (_bones[j].parent == _bones[i]) { _children[i] = j; break; }
        }
        _animated = new Vector3[_bones.Length]; _targets = new Vector3[_bones.Length];
        _offsets = new float[_bones.Length];
        _rotations = new Quaternion[_bones.Length];
        _clearance = Mathf.Max(0f, definition.Clearance) * Mathf.Abs(scale);
        _limit = Mathf.Max(0f, definition.MaxCorrection) * Mathf.Abs(scale);
        _response = Mathf.Max(.1f, definition.Response);
    }

    public void Reset() => _ready = false;

    public void Tick(IGroundingProvider ground, Vector3 up, float dt)
    {
        if (dt <= 0f || up.sqrMagnitude < .5f) return;
        up.Normalize();
        float blend = _ready ? 1f - Mathf.Exp(-_response * dt) : 1f;
        for (int i = 0; i < _bones.Length; i++)
        {
            _animated[i] = _bones[i].position;
            _rotations[i] = _bones[i].rotation;
            float offset = ground != null && ground.TryGround(_animated[i], -up, _clearance, out GroundResult hit)
                ? Mathf.Clamp(Vector3.Dot(hit.Position - _animated[i], up), -_limit, _limit) : 0f;
            _offsets[i] = Mathf.Lerp(_ready ? _offsets[i] : 0f, offset, blend);
            _targets[i] = _animated[i] + up * _offsets[i];
        }
        for (int i = 0; i < _bones.Length; i++)
        {
            // Orient each segment toward its supported child, then restore all target positions in hierarchy order.
            _bones[i].rotation = _rotations[i];
            int child = _children[i];
            if (child >= 0)
            {
                Vector3 before = _animated[child] - _animated[i], after = _targets[child] - _targets[i];
                if (before.sqrMagnitude > 1e-8f && after.sqrMagnitude > 1e-8f)
                    _bones[i].rotation = Quaternion.FromToRotation(before, after) * _bones[i].rotation;
            }
            _bones[i].position = _targets[i];
        }
        _ready = true;
    }
}
