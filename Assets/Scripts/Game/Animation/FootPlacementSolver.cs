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
    Vector3 _lastContact;
    bool _hasLastContact;
    Vector3 _previousFrame;
    bool _hasFrame;
    bool _lockStance;
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
        _hasLastContact = false;
        _hasFrame = false;
        _stepTime = 0f;
        _weight = _anchorBlend = 0f;
        _correction = _normal = Vector3.zero;
        for (int i = 0; i < _offsets.Length; i++) _offsets[i] = Quaternion.identity;
    }

    float Contact(float phase, float moving, float running) => Mathf.Clamp01(
        Mathf.Lerp(1f, Mathf.Lerp(_walkContact.Evaluate(phase), _runContact.Evaluate(phase), running), moving));

    float ReachWeight(float height) => 1f - Mathf.SmoothStep(0f, 1f,
        Mathf.InverseLerp(_maxCorrection * 0.8f, _maxCorrection, Mathf.Abs(height)));

    public float BodyOffset(IGroundingProvider ground, Vector3 up, float phase, float moving, float running, float? contactWeight = null)
    {
        if (ground == null || !ground.TryGround(_contact.position, -up, _sole, out GroundResult hit) ||
            !CharacterMath.IsFinite(hit.Position)) return 0f;
        Vector3 support = _lockStance && _planted ? _anchor : hit.Position;
        float offset = Vector3.Dot(support - _contact.position, up);
        float stance = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.8f, contactWeight ?? Contact(phase, moving, running)));
        return Mathf.Min(0f, offset) * stance * ReachWeight(offset);
    }

    public void Tick(IGroundingProvider ground, Vector3 up, float phase, float moving, float running, float dt,
        bool allowStep = false, float turnRate = 0f, float? contactWeight = null, bool lockStance = false,
        bool preserveAnimatedTravel = false)
    {
        if (dt <= 0f) return;
        _lockStance = lockStance;
        Vector3 animated = _contact.position;
        Vector3 travel = _hasFrame ? Vector3.ProjectOnPlane(_frame.position - _previousFrame, up) / dt : Vector3.zero;
        _previousFrame = _frame.position; _hasFrame = true;
        float contact = contactWeight ?? Contact(phase, moving, running);
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
        Vector3 groundPoint = hit.Position;
        // Lift the swing foot toward the next tread before its ankle reaches the riser.
        if (contact < .65f && travel.sqrMagnitude > .01f &&
            ground.TryGround(animated + Vector3.ClampMagnitude(travel * .12f, _maxCorrection), -up, _sole, out var ahead) &&
            CharacterMath.IsFinite(ahead.Position) && CharacterMath.IsFinite(ahead.Normal))
        {
            float rise = Vector3.Dot(ahead.Position - hit.Position, up);
            if (rise > .02f && rise <= _maxCorrection)
                hit = new GroundResult(hit.Position + up * (rise + .025f * (1f - contact)), ahead.Normal);
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
            // Clip evaluation restores the authored pose before this solve. Start from the displayed foot instead.
            _stepStart = _hasLastContact ? _lastContact : _contact.position;
            Vector3 predicted = _frame.position + Quaternion.AngleAxis(turnRate * StepDuration, up) *
                (hit.Position - _frame.position);
            _stepEnd = ground.TryGround(predicted, -up, _sole, out GroundResult landing) &&
                CharacterMath.IsFinite(landing.Position) ? landing.Position : hit.Position;
        }
        if (_stepping)
        {
            _stepTime += dt;
            float t = Mathf.Clamp01(_stepTime / StepDuration);
            Vector3 predicted = _frame.position + Quaternion.AngleAxis(turnRate * Mathf.Max(0f, StepDuration - _stepTime), up) *
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
        // Authored travel does not use a world-space anchor. Keep that dormant target current
        // so stopping cannot pull a foot back to an earlier gait contact. Keep displayed offsets
        // and contact state intact; idle replanting and explicit stance locks retain their behavior.
        if (preserveAnimatedTravel && moving >= .1f && !lockStance) _anchor = hit.Position;
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
        // Ordinary authored locomotion owns horizontal travel. Terrain IK may adjust height,
        // but must not hold that trajectory behind the motor or turn a strafe into a crossover.
        if (preserveAnimatedTravel && moving >= .1f && !lockStance)
            desired = Vector3.Project(desired, up);
        // Penetration adds vertical correction only. Crossing zero height must not switch all IK to full weight.
        desired += up * Mathf.Max(0f, -height - Vector3.Dot(desired, up)) * reach;
        _correction = Vector3.Lerp(_correction, desired, blend);
        Vector3 normal = Vector3.RotateTowards(up, hit.Normal.normalized, 35f * Mathf.Deg2Rad, 0f);
        _normal = Vector3.Slerp(_normal, normal, blend);
        ApplyCorrection(animated, up, dt, _lockStance ? groundPoint : null);
    }

    void ApplyCorrection(Vector3 animated, Vector3 up, float dt, Vector3? groundPoint = null)
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
            // A nearly straight knee has an unstable bend plane. Blend toward the rig's pole before that singularity.
            float legLength = Vector3.Distance(_bones[0].position, _bones[1].position) +
                Vector3.Distance(_bones[1].position, _bones[2].position);
            float confidence = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.01f, .08f, animatedBend.magnitude / legLength));
            if (animatedBend.sqrMagnitude > 0.000001f) pole = Vector3.Slerp(pole.normalized, animatedBend.normalized, confidence);
        }
        // Contact weight alone is not a correction. An identity solve can still redirect
        // a nearly straight authored knee toward the rig's fallback pole.
        if (_correction.sqrMagnitude >= 1e-10f || Quaternion.Angle(rotation, Quaternion.identity) >= .001f)
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
        if (_lockStance && _planted && _anchorBlend >= .999f && _weight >= .99f)
        {
            // Once contact has settled, rotation damping must not move a planted endpoint with the actor.
            Vector3 contactOffset = _contact.position - _limb.Tip.position;
            _limb.Solve(_anchor - contactOffset, 1f, _jointLimit, _limb.Tip.rotation, pole);
            for (int i = 0; i < _bones.Length; i++)
                _offsets[i] = Quaternion.Inverse(_animatedPose[i]) * _bones[i].localRotation;
        }
        if (groundPoint.HasValue)
        {
            float penetration = Vector3.Dot(groundPoint.Value - _contact.position, up);
            if (penetration > .001f && penetration <= _maxCorrection)
            {
                // Rotation damping must not leave a supporting foot inside a tread.
                _limb.Solve(_limb.Tip.position + up * penetration, 1f, _jointLimit, _limb.Tip.rotation, pole);
                for (int i = 0; i < _bones.Length; i++)
                    _offsets[i] = Quaternion.Inverse(_animatedPose[i]) * _bones[i].localRotation;
            }
        }
        Error = Vector3.Distance(_contact.position, target);
        _lastContact = _contact.position;
        _hasLastContact = true;
    }
}
