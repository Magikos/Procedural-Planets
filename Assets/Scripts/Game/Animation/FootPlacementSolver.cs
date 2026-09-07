using UnityEngine;

/// <summary>Clip contacts lock stance feet; swing feet keep their authored lift.</summary>
public sealed class FootPlacementSolver
{
    readonly LimbPoseSolver _limb;
    readonly Transform _contact, _frame;
    readonly Vector3 _bendDirection;
    readonly Transform[] _bones;
    readonly Quaternion[] _animatedPose, _offsets;
    readonly AnimationCurve _walkContact, _runContact;
    readonly float _sole, _maxCorrection, _jointLimit;
    bool _planted, _needsSwing;
    Vector3 _anchor, _correction, _normal;
    float _weight, _anchorBlend;
    bool _stepping;
    float _stepTime;
    Vector3 _stepStart, _stepEnd;
    const float StepDuration = 0.25f;
    public float Error { get; private set; }
    public bool Planted => _planted;
    public bool Stepping => _stepping;

    public float ReplantDistance(Vector3 up) => (_planted || _needsSwing) && !_stepping ?
        Vector3.ProjectOnPlane(_contact.position - _anchor, up).magnitude : 0f;

    public bool NeedsStep(Vector3 up) => ReplantDistance(up) > _maxCorrection * 0.4f;

    public FootPlacementSolver(FootDefinition definition, float scale, Transform frame = null)
    {
        _limb = new LimbPoseSolver(definition.Bones);
        _bones = (Transform[])definition.Bones.Clone();
        _animatedPose = new Quaternion[_bones.Length];
        _offsets = new Quaternion[_bones.Length];
        for (int i = 0; i < _offsets.Length; i++) _offsets[i] = Quaternion.identity;
        _contact = definition.Contact != null ? definition.Contact : _limb.Tip;
        if (_contact != _limb.Tip && !_contact.IsChildOf(_limb.Tip))
            throw new System.ArgumentException("Foot contact must belong to the end joint.");
        _frame = frame != null ? frame : _limb.Base;
        _bendDirection = definition.BendDirection;
        _walkContact = new AnimationCurve(definition.WalkContact.keys);
        _runContact = new AnimationCurve(definition.RunContact.keys);
        _sole = Mathf.Max(0f, definition.SoleOffset) * scale;
        _maxCorrection = Mathf.Max(0.01f, definition.MaxCorrection) * scale;
        _jointLimit = Mathf.Clamp(definition.JointLimit, 1f, 90f);
    }

    public void Reset()
    {
        _planted = _needsSwing = false;
        _stepping = false;
        _stepTime = 0f;
        _weight = _anchorBlend = 0f;
        _correction = _normal = Vector3.zero;
        for (int i = 0; i < _offsets.Length; i++) _offsets[i] = Quaternion.identity;
    }

    float Contact(float phase, float moving, float running) => Mathf.Clamp01(
        Mathf.Lerp(1f, Mathf.Lerp(_walkContact.Evaluate(phase), _runContact.Evaluate(phase), running), moving));

    float ReachWeight(float height) => 1f - Mathf.SmoothStep(0f, 1f,
        Mathf.InverseLerp(_maxCorrection * 0.8f, _maxCorrection, Mathf.Abs(height)));

    public float BodyOffset(IGroundingProvider ground, Vector3 up, float phase, float moving, float running)
    {
        if (ground == null || !ground.TryGround(_contact.position, -up, _sole, out GroundResult hit) ||
            !CharacterMath.IsFinite(hit.Position)) return 0f;
        float offset = Vector3.Dot(hit.Position - _contact.position, up);
        float stance = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.8f, Contact(phase, moving, running)));
        return Mathf.Min(0f, offset) * stance * ReachWeight(offset);
    }

    public void Tick(IGroundingProvider ground, Vector3 up, float phase, float moving, float running, float dt,
        bool allowStep = false, float turnRate = 0f)
    {
        if (dt <= 0f) return;
        Vector3 animated = _contact.position;
        float contact = Contact(phase, moving, running);
        float blend = 1f - Mathf.Exp(-20f * dt);
        if (_normal.sqrMagnitude < 1e-8f) _normal = up;
        if (ground == null || !ground.TryGround(animated, -up, _sole, out GroundResult hit) ||
            !CharacterMath.IsFinite(hit.Position) || !CharacterMath.IsFinite(hit.Normal))
        {
            _planted = false;
            _stepping = false;
            _needsSwing = false;
            _weight = Mathf.Lerp(_weight, 0f, blend);
            _anchorBlend = Mathf.MoveTowards(_anchorBlend, 0f, dt * 8f);
            _correction = Vector3.Lerp(_correction, Vector3.zero, blend);
            _normal = Vector3.Slerp(_normal, up, blend);
            ApplyCorrection(animated, up, dt);
            return;
        }
        float height = Vector3.Dot(animated - hit.Position, up);
        float reach = ReachWeight(height);
        if (_stepping && moving >= 0.1f)
        {
            // Hand control to the clip through the existing correction fade, without planting a stale target.
            _stepping = false;
            _planted = false;
            _needsSwing = false;
            _anchorBlend = 0f;
        }
        // Idle clips have no swing phase. Replant one foot at a time when rotation moves its support away.
        if (allowStep && !_stepping && reach > 0f && NeedsStep(up))
        {
            _stepping = true;
            _planted = false;
            _stepTime = 0f;
            _stepStart = _contact.position;
            Vector3 predicted = _frame.position + Quaternion.AngleAxis(turnRate * StepDuration, up) *
                (hit.Position - _frame.position);
            _stepEnd = ground.TryGround(predicted, -up, _sole, out GroundResult landing) &&
                CharacterMath.IsFinite(landing.Position) ? landing.Position : hit.Position;
        }
        if (_stepping)
        {
            _stepTime += dt;
            float t = Mathf.Clamp01(_stepTime / StepDuration);
            Vector3 predicted = _frame.position + Quaternion.AngleAxis(turnRate * (StepDuration - _stepTime), up) *
                (hit.Position - _frame.position);
            bool supported = ground.TryGround(predicted, -up, _sole, out GroundResult landing) &&
                CharacterMath.IsFinite(landing.Position) && CharacterMath.IsFinite(landing.Normal);
            // Follow stopped or reversed turns without moving the landing point discontinuously.
            if (supported) _stepEnd = Vector3.MoveTowards(_stepEnd, landing.Position, _maxCorrection * dt / StepDuration);
            Vector3 step = Vector3.Lerp(_stepStart, _stepEnd, Mathf.SmoothStep(0f, 1f, t)) +
                up * (Mathf.Sin(t * Mathf.PI) * _maxCorrection * 0.3f);
            _correction = step - animated;
            _weight = 1f;
            _normal = Vector3.Slerp(_normal, hit.Normal.normalized, blend);
            ApplyCorrection(animated, up, dt);
            if (t >= 1f)
            {
                _stepping = false;
                supported = ground.TryGround(_stepEnd, -up, _sole, out GroundResult support) &&
                    CharacterMath.IsFinite(support.Position) && CharacterMath.IsFinite(support.Normal) &&
                    Vector3.Distance(support.Position, _stepEnd) < _sole + 0.01f;
                _anchor = _stepEnd;
                _anchorBlend = 1f;
                _planted = supported;
                if (!supported) _anchorBlend = 0f;
                _needsSwing = false;
            }
            return;
        }
        if (contact < 0.25f) { _planted = false; _needsSwing = false; }
        if (_planted && (Vector3.Distance(_anchor, hit.Position) > _maxCorrection || reach <= 0f))
        {
            _planted = false;
            // Do not replace a stance anchor in the same frame. Wait for the next actual step.
            _needsSwing = true;
        }
        if (!_planted && !_needsSwing && _anchorBlend <= 0f && contact > 0.65f && reach > 0f)
        {
            _anchor = hit.Position;
            _planted = true;
        }
        _anchorBlend = Mathf.MoveTowards(_anchorBlend, _planted ? 1f : 0f, dt * 8f);
        _weight = Mathf.Lerp(_weight, contact * reach, blend);
        Vector3 target = Vector3.Lerp(hit.Position, _anchor, _anchorBlend);
        Vector3 desired = (target - animated) * _weight;
        // Penetration adds vertical correction only. Crossing zero height must not switch all IK to full weight.
        desired += up * Mathf.Max(0f, -height - Vector3.Dot(desired, up)) * reach;
        _correction = Vector3.Lerp(_correction, desired, blend);
        Vector3 normal = Vector3.RotateTowards(up, hit.Normal.normalized, 35f * Mathf.Deg2Rad, 0f);
        _normal = Vector3.Slerp(_normal, normal, blend);
        ApplyCorrection(animated, up, dt);
    }

    void ApplyCorrection(Vector3 animated, Vector3 up, float dt)
    {
        for (int i = 0; i < _bones.Length; i++) _animatedPose[i] = _bones[i].localRotation;
        Quaternion rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(up, _normal), _weight);
        Vector3 target = animated + _correction;
        // Solve the ankle/wrist; retain the animated offset and joints between it and the hoof contact.
        Vector3 endTarget = target - rotation * (animated - _limb.Tip.position);
        Vector3 pole = _frame.TransformDirection(_bendDirection);
        if (_bones.Length == 3)
        {
            Vector3 animatedBend = Vector3.ProjectOnPlane(_bones[1].position - _bones[0].position,
                _bones[2].position - _bones[0].position);
            // Follow the clip's knee/elbow plane. The authored fallback resolves a fully straight pose only.
            if (animatedBend.sqrMagnitude > 0.000001f) pole = animatedBend;
        }
        if (_correction.sqrMagnitude >= 1e-10f || _weight >= 0.0001f)
            _limb.Solve(endTarget, 1f, _jointLimit, rotation * _limb.Tip.rotation, pole);
        for (int i = 0; i < _bones.Length; i++)
        {
            // Near full extension, a small target displacement can cause a large bend-angle change.
            // Limit the added IK rotation, not the authored animation's movement.
            Quaternion desired = Quaternion.Inverse(_animatedPose[i]) * _bones[i].localRotation;
            Quaternion smooth = Quaternion.Slerp(_offsets[i], desired, 1f - Mathf.Exp(-16f * dt));
            _offsets[i] = Quaternion.RotateTowards(_offsets[i], smooth, 180f * dt);
        }
        for (int i = 0; i < _bones.Length; i++) _bones[i].localRotation = _animatedPose[i] * _offsets[i];
        Error = Vector3.Distance(_contact.position, target);
    }
}
