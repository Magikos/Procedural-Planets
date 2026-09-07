using System.Collections.Generic;
using UnityEngine;

/// <summary>Explicit order: restore, evaluate clips, capture, spine, look, chains, feet.</summary>
public sealed class ProceduralPoseRig
{
    readonly Transform _frame;
    readonly Transform _body;
    readonly Transform[] _spine, _look, _owned;
    readonly Quaternion[] _baseRotations;
    readonly Vector3[] _basePositions;
    readonly BoneChainSpring[] _chains;
    readonly FootPlacementSolver[] _feet;
    readonly float _spineLimit, _yawLimit, _pitchLimit;
    Vector3 _previousPosition, _previousForward;
    float _bend, _yaw, _pitch, _bodyOffset;
    bool _ready;

    public bool SpineEnabled { get; set; } = true;
    public bool LookEnabled { get; set; } = true;
    public bool ChainsEnabled { get; set; } = true;
    public bool FeetEnabled { get; set; } = true;
    public float MaxFootError { get; private set; }
    public int PlantedFeet { get; private set; }
    public Vector3 LookOrigin => _look.Length > 0 && _look[^1] != null ? _look[^1].position : _frame.position;

    public ProceduralPoseRig(Transform frame, ProceduralRigDefinition definition)
    {
        _frame = frame;
        _body = definition.Body;
        _spine = (Transform[])definition.Spine.Clone();
        _look = (Transform[])definition.Look.Clone();
        _spineLimit = definition.SpineLimit;
        _yawLimit = definition.LookYawLimit;
        _pitchLimit = definition.LookPitchLimit;
        var owned = new HashSet<Transform>();
        if (_body != null) owned.Add(_body);
        void Add(Transform[] bones) { foreach (Transform bone in bones) if (bone != null) owned.Add(bone); }
        Add(_spine); Add(_look);
        _chains = new BoneChainSpring[definition.Chains.Length];
        for (int i = 0; i < _chains.Length; i++)
        {
            _chains[i] = new BoneChainSpring(definition.Chains[i]);
            Add(definition.Chains[i].Bones);
        }
        _feet = new FootPlacementSolver[definition.Feet.Length];
        for (int i = 0; i < _feet.Length; i++)
        {
            _feet[i] = new FootPlacementSolver(definition.Feet[i], definition.transform.lossyScale.x, frame);
            Add(definition.Feet[i].Bones);
        }
        _owned = new Transform[owned.Count]; owned.CopyTo(_owned);
        _baseRotations = new Quaternion[_owned.Length];
        _basePositions = new Vector3[_owned.Length];
        CaptureAnimation();
    }

    public void RestoreAnimation()
    {
        for (int i = 0; i < _owned.Length; i++)
            _owned[i].SetLocalPositionAndRotation(_basePositions[i], _baseRotations[i]);
    }

    public void CaptureAnimation()
    {
        for (int i = 0; i < _owned.Length; i++)
        {
            _basePositions[i] = _owned[i].localPosition;
            _baseRotations[i] = _owned[i].localRotation;
        }
    }

    public void Reset()
    {
        _ready = false;
        _bend = _yaw = _pitch = _bodyOffset = 0f;
        foreach (BoneChainSpring chain in _chains) chain.Reset();
        foreach (FootPlacementSolver foot in _feet) foot.Reset();
    }

    public void Tick(Vector3 up, Vector3 lookDirection, IGroundingProvider ground,
        float phase, float moving, float running, float dt)
    {
        if (dt <= 0f) return;
        if (!_ready || Vector3.Distance(_previousPosition, _frame.position) > 2f || dt > 0.25f) Reset();
        Vector3 forward = Vector3.ProjectOnPlane(_frame.forward, up).normalized;
        float turn = _ready ? Vector3.SignedAngle(Vector3.ProjectOnPlane(_previousForward, up), forward, up) / dt : 0f;
        _previousPosition = _frame.position; _previousForward = forward; _ready = true;
        float blend = 1f - Mathf.Exp(-8f * Mathf.Min(dt, 0.1f));
        _bend = Mathf.Lerp(_bend, Mathf.Clamp(turn * 0.15f, -_spineLimit, _spineLimit), blend);
        Vector3 tangentLook = Vector3.ProjectOnPlane(lookDirection, up);
        float yaw = tangentLook.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(forward, tangentLook, up) : 0f;
        float pitch = Mathf.Atan2(Vector3.Dot(lookDirection, up), tangentLook.magnitude) * Mathf.Rad2Deg;
        _yaw = Mathf.Lerp(_yaw, Mathf.Clamp(yaw, -_yawLimit, _yawLimit), blend);
        _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(pitch, -_pitchLimit, _pitchLimit), blend);
        if (SpineEnabled) Aim(_spine, up, _bend, 0f);
        if (LookEnabled) Aim(_look, up, _yaw - (SpineEnabled ? _bend : 0f), _pitch);
        if (_body != null && FeetEnabled)
        {
            // Lower the body toward supporting feet before solving. A straight leg cannot reach below its full length.
            float offset = 0f;
            foreach (FootPlacementSolver foot in _feet)
                offset = Mathf.Min(offset, foot.BodyOffset(ground, up, phase, moving, running));
            _bodyOffset = Mathf.Lerp(_bodyOffset, offset, blend);
            _body.position += up * _bodyOffset;
        }
        else _bodyOffset = 0f;
        foreach (BoneChainSpring chain in _chains)
            if (ChainsEnabled) chain.Tick(-up * 9.81f, dt); else chain.Reset();
        MaxFootError = 0f; PlantedFeet = 0;
        FootPlacementSolver stepFoot = null;
        if (moving < 0.1f && FeetEnabled)
        {
            float distance = 0f;
            foreach (FootPlacementSolver foot in _feet)
            {
                if (foot.Stepping) { stepFoot = null; break; }
                float demand = foot.ReplantDistance(up);
                if (foot.NeedsStep(up) && demand > distance) { distance = demand; stepFoot = foot; }
            }
        }
        foreach (FootPlacementSolver foot in _feet)
        {
            if (!FeetEnabled) { foot.Reset(); continue; }
            foot.Tick(ground, up, phase, moving, running, Mathf.Min(dt, 0.1f), foot == stepFoot, turn);
            MaxFootError = Mathf.Max(MaxFootError, foot.Error);
            if (foot.Planted) PlantedFeet++;
        }
    }

    void Aim(Transform[] bones, Vector3 up, float yaw, float pitch)
    {
        if (bones.Length == 0) return;
        Vector3 right = Vector3.Cross(up, _frame.forward).normalized;
        Quaternion offset = Quaternion.AngleAxis(yaw / bones.Length, up) *
            Quaternion.AngleAxis(-pitch / bones.Length, right);
        foreach (Transform bone in bones) if (bone != null) bone.rotation = offset * bone.rotation;
    }
}
