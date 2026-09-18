using System.Collections.Generic;
using UnityEngine;

/// <summary>Explicit order: restore, evaluate clips, capture, spine, look, chains, feet.</summary>
public sealed class ProceduralPoseRig
{
    /// <summary>Refresh physical contact targets from captured animation before the shared solvers run.</summary>
    public event System.Action<float> BeforeCorrections;
    readonly Unity.Profiling.ProfilerMarker _poseMarker = new("Actor.ProceduralPose");
    readonly Transform _frame;
    readonly Transform _body;
    readonly Transform[] _spine, _look, _owned;
    readonly Quaternion[] _baseRotations;
    readonly Vector3[] _basePositions;
    readonly BoneChainSpring[] _chains;
    readonly FootPlacementSolver[] _feet;
    readonly SurfaceChainSolver[] _surfaces;
    readonly float _spineLimit, _yawLimit, _pitchLimit;
    readonly float _leanLimit, _leanWeight;
    readonly float[] _lookWeights;
    readonly Dictionary<string, InteractionLimbSolver> _interactions = new();
    readonly Dictionary<string, Vector3> _authoredInteractionContacts = new();
    readonly Dictionary<string, InteractionPoseTarget> _interactionTargets = new();
    readonly Dictionary<string, InteractionPoseBlend> _interactionBlends = new();
    float _lookInfluence = 1f, _lookBlend = 1f;
    public float LookInfluence
    {
        get => _lookInfluence;
        set { if (!float.IsFinite(value)) throw new System.ArgumentOutOfRangeException(nameof(value)); _lookInfluence = Mathf.Clamp01(value); }
    }
    float _leanRoll, _leanPitch;
    float _turnResponseSeconds = .15f;
    public float TurnResponseSeconds
    {
        get => _turnResponseSeconds;
        set
        {
            if (!float.IsFinite(value) || value < 0f || value > .4f) throw new System.ArgumentOutOfRangeException(nameof(value));
            _turnResponseSeconds = value;
        }
    }
    float _forwardLean, _requestedForwardLean;
    float _facingOffset, _requestedFacingOffset;
    /// <summary>Small supported interaction turns. Larger changes require actor locomotion.</summary>
    public float FacingOffsetDegrees
    {
        get => _requestedFacingOffset;
        set
        {
            if (!float.IsFinite(value) || Mathf.Abs(value) > 15f) throw new System.ArgumentOutOfRangeException(nameof(value));
            _requestedFacingOffset = value;
        }
    }
    public float ForwardLeanDegrees
    {
        get => _requestedForwardLean;
        set
        {
            if (!float.IsFinite(value) || Mathf.Abs(value) > 30f) throw new System.ArgumentOutOfRangeException(nameof(value));
            _requestedForwardLean = value;
        }
    }
    Vector3 _previousVelocity;
    Vector3 _previousPosition, _previousForward;
    float _bend, _yaw, _pitch, _bodyOffset;
    float _stepOffset;
    bool _ready;
    bool _secondaryReady;
    float _secondaryWeight;
    Vector3 _secondaryPosition;

    public bool SpineEnabled { get; set; } = true;
    public bool BodyLeanEnabled { get; set; } = true;
    public bool LookEnabled { get; set; } = true;
    public bool ChainsEnabled { get; set; } = true;
    public bool SurfaceEnabled { get; set; } = true;
    public bool FeetEnabled { get; set; } = true;
    public float MaxFootError { get; private set; }
    public int PlantedFeet { get; private set; }
    public Vector3 LookOrigin => _look.Length > 0 && _look[^1] != null ? _look[^1].position : _frame.position;

    public ProceduralPoseRig(Transform frame, ProceduralRigDefinition definition)
    {
        _frame = frame;
        _body = definition.Body;
        _leanLimit = Mathf.Clamp(definition.BodyLeanLimit, 0f, 20f);
        _leanWeight = Mathf.Clamp01(definition.BodyLeanWeight);
        _spine = (Transform[])definition.Spine.Clone();
        _look = (Transform[])definition.Look.Clone();
        _lookWeights = NormalizeLookWeights(_look, definition.LookWeights);
        _spineLimit = definition.SpineLimit;
        _yawLimit = definition.LookYawLimit;
        _pitchLimit = definition.LookPitchLimit;
        var owned = new HashSet<Transform>();
        if (_body != null) owned.Add(_body);
        void Add(Transform[] bones) { foreach (Transform bone in bones) if (bone != null) owned.Add(bone); }
        Add(_spine); Add(_look);
        foreach (var interaction in definition.Interactions ?? System.Array.Empty<InteractionLimbDefinition>())
        {
            if (interaction == null || string.IsNullOrWhiteSpace(interaction.Id) || _interactions.ContainsKey(interaction.Id))
                throw new System.ArgumentException("Interaction limbs need unique nonempty identifiers.");
            _interactions.Add(interaction.Id, new InteractionLimbSolver(frame, interaction));
            _interactionBlends.Add(interaction.Id, new InteractionPoseBlend());
            Add(interaction.Bones);
            foreach (var joint in interaction.ContactJoints ?? System.Array.Empty<InteractionContactJoint>()) owned.Add(joint.Bone);
        }
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
        var surfaces = definition.SurfaceChains ?? System.Array.Empty<SurfaceChainDefinition>();
        _surfaces = new SurfaceChainSolver[surfaces.Length];
        for (int i = 0; i < surfaces.Length; i++)
        {
            _surfaces[i] = new SurfaceChainSolver(surfaces[i], definition.transform.lossyScale.x);
            Add(surfaces[i].Bones);
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
        foreach (var entry in _interactions)
            _authoredInteractionContacts[entry.Key] = _frame.InverseTransformPoint(entry.Value.WorldContactPosition);
    }

    public void Reset()
    {
        MaxFootError = 0f; PlantedFeet = 0;
        _ready = false;
        _secondaryReady = false;
        _bend = _yaw = _pitch = _bodyOffset = _stepOffset = 0f;
        _facingOffset = 0;
        _previousVelocity = Vector3.zero;
        _leanRoll = _leanPitch = 0f;
        _lookBlend = _lookInfluence;
        foreach (BoneChainSpring chain in _chains) chain.Reset();
        foreach (SurfaceChainSolver surface in _surfaces) surface.Reset();
        foreach (FootPlacementSolver foot in _feet) foot.Reset();
        foreach (var transition in _interactionBlends.Values) transition.Reset();
    }

    public void Tick(Vector3 up, Vector3 lookDirection, IGroundingProvider ground,
        float phase, float moving, float running, float dt, IReadOnlyList<float> footContacts = null, bool lockFootContacts = false,
        bool preserveAnimatedFootTravel = false, float secondaryWaterWeight = 0f)
    {
        using var timing = _poseMarker.Auto();
        if (footContacts != null)
        {
            if (footContacts.Count != _feet.Length) throw new System.ArgumentException("Contact weights must match the rig feet.", nameof(footContacts));
            foreach (float contact in footContacts)
                if (!float.IsFinite(contact) || contact < 0f || contact > 1f)
                    throw new System.ArgumentOutOfRangeException(nameof(footContacts));
        }
        if (!float.IsFinite(dt) || dt <= 0f || up.sqrMagnitude < 1e-8f) return;
        BeforeCorrections?.Invoke(dt);
        up.Normalize();
        if (!_ready || Vector3.Distance(_previousPosition, _frame.position) > 2f) Reset();
        Vector3 forward = Vector3.ProjectOnPlane(_frame.forward, up).normalized;
        float rise = _ready ? Vector3.Dot(_frame.position - _previousPosition, up) : 0f;
        if (_ready && ground != null && Mathf.Abs(rise) > .04f && Mathf.Abs(rise) < .4f)
            _stepOffset = Mathf.Clamp(_stepOffset - rise, -.4f, .4f);
        _stepOffset = Mathf.Lerp(_stepOffset, 0f, 1f - Mathf.Exp(-12f * dt));
        if (_body != null) _body.position += up * _stepOffset;
        float turn = _ready ? Vector3.SignedAngle(Vector3.ProjectOnPlane(_previousForward, up), forward, up) / dt : 0f;
        Vector3 velocity = _ready ? Vector3.ProjectOnPlane(_frame.position - _previousPosition, up) / dt : Vector3.zero;
        Vector3 acceleration = _ready ? Vector3.ProjectOnPlane(velocity - _previousVelocity, up) / dt : Vector3.zero;
        _previousVelocity = velocity;
        _previousPosition = _frame.position; _previousForward = forward; _ready = true;
        float blend = 1f - Mathf.Exp(-8f * Mathf.Min(dt, 0.1f));
        // Signed local acceleration covers forward, backward, strafe and curved movement without counting turns twice.
        float roll = -Mathf.Atan(Vector3.Dot(acceleration, Vector3.Cross(up, forward)) / 9.81f) * Mathf.Rad2Deg;
        float leanPitch = Mathf.Atan(Vector3.Dot(acceleration, forward) / 9.81f) * Mathf.Rad2Deg;
        _leanRoll = Mathf.Lerp(_leanRoll, BodyLeanEnabled ? Mathf.Clamp(roll * _leanWeight, -_leanLimit, _leanLimit) : 0f, blend);
        _leanPitch = Mathf.Lerp(_leanPitch, BodyLeanEnabled ? Mathf.Clamp(leanPitch * _leanWeight, -_leanLimit, _leanLimit) : 0f, blend);
        if (_body != null)
            _body.rotation = Quaternion.AngleAxis(_leanRoll, forward) *
                Quaternion.AngleAxis(_leanPitch, Vector3.Cross(up, forward)) * _body.rotation;
        _bend = Mathf.Lerp(_bend, SpineEnabled ? Mathf.Clamp(turn * _turnResponseSeconds, -_spineLimit, _spineLimit) : 0f, blend);
        Vector3 tangentLook = Vector3.ProjectOnPlane(lookDirection, up);
        float yaw = tangentLook.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(forward, tangentLook, up) : 0f;
        // Keep the selected side near directly behind; tiny target noise must not reverse the neck.
        if (Mathf.Abs(yaw) > 150f && Mathf.Abs(_yaw) > 1f) yaw = Mathf.Sign(_yaw) * Mathf.Abs(yaw);
        float pitch = Mathf.Atan2(Vector3.Dot(lookDirection, up), tangentLook.magnitude) * Mathf.Rad2Deg;
        _yaw = Mathf.Lerp(_yaw, Mathf.Clamp(yaw, -_yawLimit, _yawLimit), blend);
        _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(pitch, -_pitchLimit, _pitchLimit), blend);
        _forwardLean = Mathf.MoveTowards(_forwardLean, SpineEnabled ? _requestedForwardLean : 0f, dt * 60f);
        _facingOffset = Mathf.MoveTowards(_facingOffset, SpineEnabled ? _requestedFacingOffset : 0, dt * 45);
        float spineYaw = Mathf.Clamp(_bend + _facingOffset, -_spineLimit, _spineLimit);
        Aim(_spine, up, spineYaw, -_forwardLean);
        _lookBlend = Mathf.Lerp(_lookBlend, LookEnabled ? _lookInfluence : 0f, blend);
        Aim(_look, up, (_yaw - spineYaw) * _lookBlend, _pitch * _lookBlend, _lookWeights);
        if (_body != null && FeetEnabled)
        {
            // Lower the body toward supporting feet before solving. A straight leg cannot reach below its full length.
            float offset = 0f;
            for (int i = 0; i < _feet.Length; i++)
                offset = Mathf.Min(offset, _feet[i].BodyOffset(ground, up, phase, moving, running, footContacts?[i]));
            _bodyOffset = Mathf.Lerp(_bodyOffset, offset, blend);
            _body.position += up * _bodyOffset;
        }
        else
        {
            _bodyOffset = Mathf.Lerp(_bodyOffset, 0f, blend);
            if (_body != null) _body.position += up * _bodyOffset;
        }
        TickSecondaryMotion(up, dt, secondaryWaterWeight, ground);
        foreach (SurfaceChainSolver surface in _surfaces)
            surface.Tick(SurfaceEnabled ? ground : null, up, Mathf.Min(dt, .1f));
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
        for (int i = 0; i < _feet.Length; i++)
        {
            FootPlacementSolver foot = _feet[i];
            foot.Tick(FeetEnabled ? ground : null, up, phase, moving, running, Mathf.Min(dt, 0.1f), foot == stepFoot, turn,
                footContacts?[i], FeetEnabled && lockFootContacts, preserveAnimatedFootTravel);
            MaxFootError = Mathf.Max(MaxFootError, foot.Error);
            if (foot.Planted) PlantedFeet++;
        }
        foreach (var transition in _interactionBlends)
        {
            InteractionPoseTarget? requested = _interactionTargets.TryGetValue(transition.Key, out var target) ? target : null;
            var sampled = transition.Value.Tick(requested, dt,
                _frame.TransformPoint(_authoredInteractionContacts[transition.Key]), _frame.rotation);
            if (sampled.HasValue) _interactions[transition.Key].Solve(sampled.Value);
        }
    }

    public void TickSecondaryMotion(Vector3 up, float dt, float waterWeight = 0f, IGroundingProvider ground = null)
    {
        if (!float.IsFinite(waterWeight)) throw new System.ArgumentOutOfRangeException(nameof(waterWeight));
        if (!float.IsFinite(dt) || dt <= 0f) return;
        waterWeight = Mathf.Clamp01(waterWeight);
        if (!_secondaryReady || Vector3.Distance(_secondaryPosition, _frame.position) > 2f)
        {
            foreach (BoneChainSpring chain in _chains) chain.Reset();
            _secondaryWeight = ChainsEnabled ? 1f : 0f;
        }
        _secondaryPosition = _frame.position; _secondaryReady = true;
        // Release from the displayed influence, including reversals during release.
        // Hitches retain spring state; the solver already bounds its integration step.
        _secondaryWeight = Mathf.MoveTowards(_secondaryWeight, ChainsEnabled ? 1f : 0f, Mathf.Min(dt, .05f) * 6f);
        foreach (BoneChainSpring chain in _chains)
            if (_secondaryWeight > 0f) chain.Tick(-up.normalized * (9.81f * (1f - .85f * waterWeight)), dt, 12f * waterWeight, ground, up,
                _secondaryWeight);
            else chain.Reset();
    }

    public void SetInteractionTarget(string id, InteractionPoseTarget? target)
    {
        if (id == null || !_interactions.ContainsKey(id)) throw new System.ArgumentException("Unknown interaction limb.", nameof(id));
        if (target.HasValue) _interactionTargets[id] = target.Value;
        else _interactionTargets.Remove(id);
    }

    public bool TryGetInteractionContact(string id, out Vector3 position, out bool reachable)
    {
        if (id != null && _interactions.TryGetValue(id, out var limb))
        {
            position = limb.WorldInteractionPosition;
            reachable = _interactionTargets.TryGetValue(id, out var requested) && limb.Reachable
                && Vector3.Distance(position, requested.Position) <= .02f;
            return true;
        }
        position = default; reachable = false; return false;
    }

    public bool TryGetInteractionContactPose(string id, out Pose pose)
    {
        if (id != null && _interactions.TryGetValue(id, out var limb))
        {
            pose = new Pose(limb.WorldInteractionPosition, limb.WorldContactRotation);
            return true;
        }
        pose = default; return false;
    }

    /// <summary>Last evaluated contact before correction, carried by the actor's current frame.</summary>
    public bool TryGetAuthoredInteractionContact(string id, out Vector3 position)
    {
        if (id != null && _authoredInteractionContacts.TryGetValue(id, out var local))
        {
            position = _frame.TransformPoint(local);
            return true;
        }
        position = default; return false;
    }

    static float[] NormalizeLookWeights(Transform[] bones, float[] authored)
    {
        var weights = new float[bones.Length];
        float total = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            float weight = authored != null && i < authored.Length ? authored[i] : 1f;
            if (!float.IsFinite(weight) || weight < 0f) throw new System.ArgumentException("Gaze weights must be finite and nonnegative.");
            total += weights[i] = bones[i] != null ? weight : 0f;
        }
        if (total > 0f) for (int i = 0; i < weights.Length; i++) weights[i] /= total;
        return weights;
    }

    void Aim(Transform[] bones, Vector3 up, float yaw, float pitch, float[] weights = null)
    {
        if (bones.Length == 0) return;
        Vector3 right = Vector3.Cross(up, _frame.forward).normalized;
        for (int i = 0; i < bones.Length; i++)
        {
            float weight = weights == null ? 1f / bones.Length : weights[i];
            Quaternion offset = Quaternion.AngleAxis(yaw * weight, up) * Quaternion.AngleAxis(-pitch * weight, right);
            if (bones[i] != null) bones[i].rotation = offset * bones[i].rotation;
        }
    }
}
