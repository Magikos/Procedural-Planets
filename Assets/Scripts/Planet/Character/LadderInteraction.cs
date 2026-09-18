using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Ladder geometry and bounded rung contacts consumed by the actor's existing motor and pose graph.</summary>
public sealed class LadderInteraction : MonoBehaviour
{
    [Min(1.8f)] public float Height = 3f;
    [Range(.15f, .5f)] public float RungSpacing = .3f;
    [Min(0f)] public float RungOffset = .15f;
    [Min(.4f)] public float Width = .65f;
    [Min(.1f)] public float BodyDistance = .28f;
    [Range(0f, .1f)] public float RungPalmDepth = .08f;
    public float RungsPerCycle => HandRungStep(true) + HandRungStep(false);
    public ActorTraversalMotionAsset TopExitMotion, TopMountMotion, BottomMountMotion;
    public ActorTraversalMotionAsset TopExitMirroredMotion;
    public ActorTraversalMotionAsset BottomExitMirroredMotion;
    public ActorTraversalMotionAsset SlideStartMotion, SlideMotion, SlideEndMotion;
    public ActorTraversalMotionAsset SprintLeftMotion, SprintRightMotion;
    public ActorTraversalMotionAsset SprintTopMotion;
    [Min(.1f)] public float SprintPlaybackRate = 1.5f;
    public ActorTraversalMotionAsset ApproachStepMotion, BottomExitMotion, UpGaitMotion, DownGaitMotion;
    public float MaxRequestedCorrection { get; private set; }
    public Vector3 Bottom => transform.position - transform.forward * BodyDistance + transform.up * .025f;
    public bool SprintTopAvailable => MotionReady(SprintTopMotion, true) &&
        Mathf.Abs(Height - SprintTopMotion.Frames[^1].Root.y) <= SprintTopMotion.MaxHeightAdjustment;
    public Vector3 SprintBottomApproach => SprintTopAvailable ? transform.TransformPoint(
        new Vector3(-SprintTopMotion.ReferenceEdge.x, .025f, -SprintTopMotion.ReferenceEdge.z)) : BottomApproach;
    public Vector3 BottomApproach => MotionReady(BottomMountMotion, true) ?
        Bottom - transform.rotation * (Vector3.ProjectOnPlane(BottomMountMotion.Frames[^1].Root, Vector3.up) +
            (ApproachStepMotion != null && ApproachStepMotion.Frames != null && ApproachStepMotion.Frames.Length >= 2 ?
                ApproachStepMotion.Frames[^1].Root : Vector3.zero)) : Bottom - transform.forward * .25f;
    public Vector3 TopApproach => Bottom + transform.up * Height + transform.forward *
        (MotionReady(TopMountMotion, false) ? -TopMountMotion.Frames[^1].Root.z : .95f);
    public Vector3 TopApproachForward => MotionReady(TopMountMotion, false) ?
        Quaternion.AngleAxis(TopMountMotion.Frames[0].RootYawDegrees, transform.up) * transform.forward : transform.forward;
    static readonly string[] Contacts = { "LeftHand", "RightHand", "LadderLeftFoot", "LadderRightFoot" };
    struct SupportContact
    {
        public Vector3 Previous, Anchor;
        public bool Initialized, Anchored;
        public float Weight, TargetWeight, StableTime;
        public int StableSamples;
    }
    bool _slideReturnPrepared, _slideReturnPreparingContact;
    Vector3 _slideReturnAnchor;
    readonly Dictionary<string, SupportContact> _support = new();
    bool _authoredSupportReady;
    string _supportMode;
    float _primaryPreparationTime;
    bool _preparingPrimarySupport;
    public bool SlidingRailContacts { get; private set; }
    public bool TryGetRailContact(string id, out Vector3 target)
    {
        bool found = SlidingRailContacts && !(id == "RightHand" && _slideReturnPreparingContact) && _support.TryGetValue(id, out var contact);
        target = found ? _support[id].Anchor : default;
        return found;
    }
    // Measured palm separations at the two landing events of the CCP source on this rig.
    public float HalfCycleHandSeparation = .410f, CycleHandSeparation = .349f;
    public int HandRungStep(bool halfCycle)
    {
        float separation = halfCycle ? HalfCycleHandSeparation : CycleHandSeparation;
        if (!float.IsFinite(RungSpacing) || RungSpacing <= 0f || !float.IsFinite(separation) || separation <= 0f)
            throw new ArgumentException("Authored hand landing requires finite positive source separation and rung spacing.");
        return Mathf.Max(1, Mathf.RoundToInt(separation / RungSpacing));
    }
    public Vector3 GaitStartLeftPalm = new(-.2032911f, .9914128f, .3053033f);
    public Vector3 GaitStartRightPalm = new(.2429527f, 1.343099f, .3223074f);
    public Vector3 TopMountSupportOffset
    {
        get
        {
            if (!MotionReady(TopMountMotion, false)) return Vector3.zero;
            Vector3 root = Bottom + transform.up * (Height + TopMountMotion.Frames[^1].Root.y);
            Vector3 left = root + transform.rotation * GaitStartLeftPalm;
            Vector3 right = root + transform.rotation * GaitStartRightPalm;
            return Quaternion.Inverse(transform.rotation) *
                ((NearestHandRungContact(left) - left + NearestHandRungContact(right) - right) * .5f);
        }
    }
    public float SupportWeight(string id) => _support.TryGetValue(id, out var contact) ? contact.Weight : 0f;
    public float ContactPreparationWeight(string id) => _support.TryGetValue(id, out var contact) ? contact.TargetWeight : 0f;
    public bool ContactAnchored(string id) => _support.TryGetValue(id, out var contact) && contact.Anchored;
    public bool TryGetContactAnchor(string id, out Vector3 anchor)
    {
        bool anchored = _support.TryGetValue(id, out var contact) && contact.Anchored;
        anchor = anchored ? contact.Anchor : default;
        return anchored;
    }

    public bool GeometryValid => float.IsFinite(Height) && Height >= 1.8f && float.IsFinite(RungSpacing) && RungSpacing >= .15f && RungSpacing <= .5f &&
        float.IsFinite(Width) && Width >= .4f && float.IsFinite(BodyDistance) && BodyDistance >= .1f &&
        float.IsFinite(RungOffset) && RungOffset >= 0f && RungOffset < RungSpacing &&
        float.IsFinite(HalfCycleHandSeparation) && HalfCycleHandSeparation > 0f &&
        float.IsFinite(CycleHandSeparation) && CycleHandSeparation > 0f &&
        CharacterMath.IsFinite(GaitStartLeftPalm) && CharacterMath.IsFinite(GaitStartRightPalm) &&
        float.IsFinite(SprintPlaybackRate) && SprintPlaybackRate > 0f &&
        float.IsFinite(RungPalmDepth) && RungPalmDepth >= 0f && RungPalmDepth <= .1f &&
        float.IsFinite(RungsPerCycle) && RungsPerCycle >= 1f && Vector3.Distance(transform.lossyScale, Vector3.one) < .001f &&
        MotionReady(TopExitMotion, true) && MotionReady(TopMountMotion, false) && MotionReady(BottomMountMotion, true) &&
        (TopExitMirroredMotion == null || MotionReady(TopExitMirroredMotion, true)) &&
        (BottomExitMirroredMotion == null || MotionReady(BottomExitMirroredMotion, false)) &&
        (BottomExitMotion == null || MotionReady(BottomExitMotion, false)) &&
        ((UpGaitMotion == null && DownGaitMotion == null) || (MotionReady(UpGaitMotion, true) && MotionReady(DownGaitMotion, false))) &&
        (ApproachStepMotion == null || (ApproachStepMotion.Clip != null && ApproachStepMotion.Clip.isHumanMotion &&
            ApproachStepMotion.Frames != null && ApproachStepMotion.Frames.Length >= 2 &&
            CharacterMath.IsFinite(ApproachStepMotion.Frames[^1].Root) && Mathf.Abs(ApproachStepMotion.Frames[^1].Root.y) < .001f));

    static bool MotionReady(ActorTraversalMotionAsset asset, bool ascending) => asset != null && asset.Clip != null &&
        asset.Clip.isHumanMotion && asset.Frames != null && asset.Frames.Length >= 2 &&
        CharacterMath.IsFinite(asset.Frames[^1].Root) && (ascending ? asset.Frames[^1].Root.y > .001f : asset.Frames[^1].Root.y < -.001f);

    public bool TryValidate(ActorAnimationPerformance performance, out string reason)
    {
        if (!MotionReady(TopExitMotion, true) || !MotionReady(TopMountMotion, false) || !MotionReady(BottomMountMotion, true))
        { reason = "Ladder requires valid authored ladder motion assets for both mounts and the top exit, with sampled frames."; return false; }
        if (!GeometryValid)
        { reason = "Ladder geometry requires finite dimensions, valid rung spacing, and unit scale."; return false; }
        if (performance == null)
        { reason = "Ladder requires an authored humanoid ladder performance."; return false; }
        if ((SlideStartMotion != null || SlideMotion != null || SlideEndMotion != null) &&
            (!MotionReady(SlideStartMotion, false) || !MotionReady(SlideMotion, false) || !MotionReady(SlideEndMotion, true)))
        { reason = "Sliding requires valid authored start, descent, and brake motion assets."; return false; }
        if ((SprintLeftMotion != null || SprintRightMotion != null) &&
            (!MotionReady(SprintLeftMotion, true) || !MotionReady(SprintRightMotion, true)))
        { reason = "Sprint climbing requires valid authored motions for both leading hands."; return false; }
        try
        {
            if (SprintTopMotion != null)
            {
                if (!MotionReady(SprintTopMotion, true) || !CharacterMath.IsFinite(SprintTopMotion.ReferenceEdge))
                { reason = "Direct sprint ascent requires a valid authored motion and contact edge."; return false; }
                var phase = performance.GetPhase("SprintTop");
                var motion = SprintTopMotion.CreateMotion();
                if (phase == null || phase.Clip != SprintTopMotion.Clip ||
                    Mathf.Abs(motion.Duration - phase.Clip.length * (phase.EndNormalized - phase.StartNormalized)) > .001f)
                { reason = "Direct sprint ascent must match its authored performance clip and duration."; return false; }
            }
            if (ApproachStepMotion != null)
            {
                var step = ApproachStepMotion.CreateMotion();
                var phase = performance.GetPhase("ApproachStep");
                if (phase == null || phase.Clip != ApproachStepMotion.Clip ||
                    Mathf.Abs(step.Duration - phase.Clip.length * (phase.EndNormalized - phase.StartNormalized)) > .001f)
                { reason = "Ladder approach step must match its authored performance clip and duration."; return false; }
            }
            foreach (var pair in new[] { (Name: "ExitTop", Asset: TopExitMotion), (Name: "MountTop", Asset: TopMountMotion),
                (Name: "MountBottom", Asset: BottomMountMotion), (Name: "ExitBottom", Asset: BottomExitMotion),
                (Name: "Up", Asset: UpGaitMotion), (Name: "Down", Asset: DownGaitMotion),
                (Name: "ExitTopMirrored", Asset: TopExitMirroredMotion), (Name: "SlideStart", Asset: SlideStartMotion),
                (Name: "ExitBottomMirrored", Asset: BottomExitMirroredMotion),
                (Name: "Slide", Asset: SlideMotion), (Name: "SlideEnd", Asset: SlideEndMotion),
                (Name: "SprintUp", Asset: SprintLeftMotion), (Name: "SprintUpRight", Asset: SprintRightMotion) })
            {
                if (pair.Asset == null) continue;
                var motion = pair.Asset.CreateMotion();
                var phase = performance.GetPhase(pair.Name);
                if (phase == null || phase.Clip != pair.Asset.Clip ||
                    Mathf.Abs(motion.Duration - phase.Clip.length * (phase.EndNormalized - phase.StartNormalized)) > .001f)
                { reason = "Ladder " + pair.Name + " motion must match its authored performance clip and duration."; return false; }
                if (Height < Mathf.Abs(motion.Sample(1f).Root.y) + .15f)
                { reason = "This ladder is too short for its authored top transfers and clearance."; return false; }
            }
        }
        catch (ArgumentException error) { reason = "Invalid authored ladder motion: " + error.Message; return false; }
        catch (InvalidOperationException error) { reason = "Invalid authored ladder motion: " + error.Message; return false; }
        reason = null; return true;
    }

    public void ReleaseContacts(ProceduralPoseRig pose)
    {
        if (pose == null) throw new ArgumentNullException(nameof(pose));
        _support.Clear(); _authoredSupportReady = false; _supportMode = null; SlidingRailContacts = false; MaxRequestedCorrection = 0f;
        _primaryPreparationTime = 0f; _preparingPrimarySupport = false;
        _slideReturnPrepared = _slideReturnPreparingContact = false;
        foreach (string id in Contacts) pose.SetInteractionTarget(id, null);
    }

    public void ApplyContacts(ProceduralPoseRig pose, bool mounted, float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        if (dt == 0f) return;
        MaxRequestedCorrection = 0f;
        if (!mounted) { ReleaseContacts(pose); return; }
        foreach (string id in Contacts) ApplyHeuristicContact(pose, id, dt, Vector3.zero);
    }

    public Vector3 ApplyAuthoredContacts(ProceduralPoseRig pose, ActorLadder ladder, float dt)
    {
        if (pose == null || ladder == null) throw new ArgumentNullException(pose == null ? nameof(pose) : nameof(ladder));
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        if (dt == 0f) return Vector3.zero;
        if (ladder.Phase == ActorLadderPhase.MountTop || ladder.Phase == ActorLadderPhase.MountBottom)
        {
            PrepareMountContacts(pose, ladder);
            return Vector3.zero;
        }
        if (ladder.Phase == ActorLadderPhase.SlideStart || ladder.Phase == ActorLadderPhase.Slide || ladder.Phase == ActorLadderPhase.SlideEnd)
        {
            ApplyRailContacts(pose, ladder);
            if (ladder.Phase == ActorLadderPhase.SlideEnd) PrepareSlideReturn(pose, ladder);
            return Vector3.zero;
        }
        if (ladder.Phase != ActorLadderPhase.Climbing) { ReleaseContacts(pose); return Vector3.zero; }
        Vector3? returnRung = _slideReturnPrepared ? _slideReturnAnchor : null;
        if (SlidingRailContacts) ReleaseContacts(pose);
        if (!pose.TryGetAuthoredInteractionContact("LeftHand", out var left) ||
            !pose.TryGetAuthoredInteractionContact("RightHand", out var right))
            throw new InvalidOperationException("Authored ladder support requires both humanoid palm contacts.");
        float phase = ladder.GaitPhase;
        if (ladder.PrimarySupportOnly)
        {
            if (!_preparingPrimarySupport) _primaryPreparationTime = 0f;
            _primaryPreparationTime += dt;
        }
        else _primaryPreparationTime = 0f;
        _preparingPrimarySupport = ladder.PrimarySupportOnly;
        float leftWeight = phase < .04f ? 1f - Mathf.SmoothStep(0f, 1f, phase / .04f) :
            phase < .46f ? 0f : phase < .5f ? Mathf.SmoothStep(0f, 1f, (phase - .46f) / .04f) : 1f;
        float rightWeight = phase < .5f ? 1f : phase < .54f ? 1f - Mathf.SmoothStep(0f, 1f, (phase - .5f) / .04f) :
            phase < .96f ? 0f : Mathf.SmoothStep(0f, 1f, (phase - .96f) / .04f);
        if (ladder.PrimarySupportOnly)
        {
            leftWeight = ladder.PrimarySupportIsLeft ? 1f : 0f;
            rightWeight = ladder.PrimarySupportIsLeft ? 0f : 1f;
        }
        // The shared interaction blend needs 1/6 second to acquire full influence. Prepare
        // its bounded correction during the authored reach, before the measured support interval.
        bool descending = ladder.ClimbDirection == "Down";
        float normalPreparation = Mathf.Clamp(.2f * ladder.EffectivePlaybackRate /
            (UpGaitMotion != null ? UpGaitMotion.Clip.length : 1.25f), .16f, .4f);
        float leftPreparation = descending ? Mathf.Clamp01((normalPreparation - phase) / normalPreparation) :
            phase < .5f ? Mathf.Clamp01((phase - (.5f - normalPreparation)) / normalPreparation) : 0f;
        float rightPreparation = descending ? phase > .5f ? Mathf.Clamp01((.5f + normalPreparation - phase) / normalPreparation) : 0f :
            Mathf.Clamp01((phase - (1f - normalPreparation)) / normalPreparation);
        if (ladder.PrimarySupportOnly)
        {
            float preparation = Mathf.Clamp01(_primaryPreparationTime / .2f);
            if (ladder.PrimarySupportIsLeft) rightPreparation = preparation;
            else leftPreparation = preparation;
        }
        bool rightLeading = ladder.AnimationPhase == "SprintUpRight";
        if (ladder.Sprinting)
        {
            float leadingWeight = phase < .46f ? 0f : Mathf.SmoothStep(0f, 1f, (phase - .46f) / .04f);
            float trailingWeight = phase < .5f ? 1f : phase < .58f ? 1f - Mathf.SmoothStep(0f, 1f, (phase - .5f) / .08f) :
                Mathf.SmoothStep(0f, 1f, (phase - .96f) / .04f);
            float preparationWindow = Mathf.Min(.5f, .2f * ladder.SprintPhaseRate);
            float leadingPreparation = Mathf.Clamp01((phase - (.5f - preparationWindow)) / preparationWindow);
            float trailingPreparation = Mathf.Clamp01((phase - (1f - preparationWindow)) / preparationWindow);
            if (descending)
            {
                leadingPreparation = 0f;
                trailingPreparation = phase > .5f ? Mathf.Clamp01((.5f + preparationWindow - phase) / preparationWindow) : 0f;
            }
            leftWeight = rightLeading ? trailingWeight : leadingWeight;
            rightWeight = rightLeading ? leadingWeight : trailingWeight;
            leftPreparation = rightLeading ? trailingPreparation : leadingPreparation;
            rightPreparation = rightLeading ? leadingPreparation : trailingPreparation;
        }
        if (!_authoredSupportReady)
        {
            _support["LeftHand"] = new SupportContact { Anchor = NearestHandRungContact(left), Initialized = leftWeight > 0f };
            _support["RightHand"] = new SupportContact { Anchor = returnRung ?? NearestHandRungContact(right),
                Initialized = rightWeight > 0f, Anchored = returnRung.HasValue && rightWeight > 0f };
            _authoredSupportReady = true; ladder.BeginSupportTransfer();
        }
        string mode = ladder.Sprinting ? ladder.AnimationPhase : "Normal";
        if (_supportMode != mode)
        {
            var l = _support["LeftHand"]; var r = _support["RightHand"];
            l.Initialized = leftWeight > 0f; r.Initialized = rightWeight > 0f;
            _support["LeftHand"] = l; _support["RightHand"] = r; _supportMode = mode;
        }
        PlanHand("LeftHand", "RightHand", left, leftWeight, Mathf.Max(leftWeight, leftPreparation));
        PlanHand("RightHand", "LeftHand", right, rightWeight, Mathf.Max(rightWeight, rightPreparation));
        var leftSupport = _support["LeftHand"]; var rightSupport = _support["RightHand"];
        float leftFitWeight = leftSupport.Weight;
        float rightFitWeight = rightSupport.Weight;
        bool sprintPrimaryLeft = rightLeading ? phase < .58f : phase >= .58f;
        if (ladder.Sprinting || ladder.PrimarySupportOnly)
        {
            bool primaryLeft = ladder.Sprinting ? sprintPrimaryLeft : ladder.PrimarySupportIsLeft;
            Vector3 primaryError = primaryLeft ? leftSupport.Anchor - left : rightSupport.Anchor - right;
            Vector3 incomingError = primaryLeft ? rightSupport.Anchor - right : leftSupport.Anchor - left;
            // Prepare body placement only when both hand corrections can fit within
            // their existing six-centimetre limits. Do not pull toward a distant swing.
            if (Vector3.Distance(primaryError, incomingError) <= .12f)
            {
                leftFitWeight = leftSupport.TargetWeight;
                rightFitWeight = rightSupport.TargetWeight;
            }
        }
        Vector3 fit = ((leftSupport.Anchor - left) * leftFitWeight + (rightSupport.Anchor - right) * rightFitWeight) /
            Mathf.Max(.001f, leftFitWeight + rightFitWeight);
        if (ladder.PrimarySupportOnly || ladder.Sprinting)
        {
            bool primaryLeft = ladder.Sprinting ? sprintPrimaryLeft : ladder.PrimarySupportIsLeft;
            Vector3 primaryError = primaryLeft ? leftSupport.Anchor - left : rightSupport.Anchor - right;
            fit = primaryError + Vector3.ClampMagnitude(fit - primaryError, .06f);
        }
        Vector3 displacement = ladder.FitSupport(fit, dt);
        MaxRequestedCorrection = 0f;
        SetAuthoredHand("LeftHand", left + displacement, leftSupport);
        SetAuthoredHand("RightHand", right + displacement, rightSupport);
        ApplyHeuristicContact(pose, "LadderLeftFoot", dt, displacement);
        ApplyHeuristicContact(pose, "LadderRightFoot", dt, displacement);
        return displacement;

        void PlanHand(string id, string other, Vector3 incoming, float weight, float targetWeight)
        {
            var contact = _support[id];
            if (targetWeight > 0f && !contact.Initialized)
            {
                // Preparation can begin before the phase midpoint at faster playback.
                // Select the upcoming hand landing, not the current preparation time.
                bool halfCycle = ladder.PrimarySupportOnly || ladder.Sprinting
                    ? phase > .25f && phase < .75f
                    : (id == "LeftHand") != descending;
                int rungStep = HandRungStep(halfCycle);
                int direction = ladder.ClimbDirection == "Down" ? -1 : 1;
                if (ladder.PrimarySupportOnly) direction = -1;
                if (ladder.Sprinting)
                {
                    bool leading = id == (rightLeading ? "RightHand" : "LeftHand");
                    rungStep = leading == (direction > 0) ? Mathf.Max(2, Mathf.RoundToInt(ladder.SprintCycleHeight / RungSpacing)) : 0;
                }
                Vector3 target = _support[other].Anchor + transform.up * (direction * rungStep * RungSpacing);
                target += transform.right * Vector3.Dot(incoming - target, transform.right);
                contact.Anchor = NearestHandRungContact(target);
                ladder.BeginSupportTransfer();
            }
            // A reaching hand can still travel along the selected rung. Freeze its
            // lateral contact only when the authored support interval starts.
            if (targetWeight > 0f && !contact.Anchored)
            {
                Vector3 target = contact.Anchor + transform.right * Vector3.Dot(incoming - contact.Anchor, transform.right);
                contact.Anchor = NearestHandRungContact(target);
            }
            contact.Initialized = targetWeight > 0f; contact.Anchored = weight > 0f;
            contact.Weight = weight; contact.TargetWeight = targetWeight;
            _support[id] = contact;
        }

        void SetAuthoredHand(string id, Vector3 point, SupportContact contact)
        {
            Vector3 correction = Vector3.ClampMagnitude(contact.Anchor - point, .06f);
            pose.SetInteractionTarget(id, contact.TargetWeight > 0f ?
                new InteractionPoseTarget(point + correction, weight: contact.TargetWeight, useContact: true, preserveAuthoredContactJoints: true) : null);
            MaxRequestedCorrection = Mathf.Max(MaxRequestedCorrection, correction.magnitude * contact.TargetWeight);
        }
    }

    void PrepareMountContacts(ProceduralPoseRig pose, ActorLadder ladder)
    {
        bool fromTop = ladder.Phase == ActorLadderPhase.MountTop;
        var motion = fromTop ? TopMountMotion : BottomMountMotion;
        if (motion == null || motion.Clip == null || motion.Frames == null || motion.Frames.Length < 2)
        { ReleaseContacts(pose); return; }
        float remaining = (1f - ladder.AnimationProgress) * motion.Clip.length / Mathf.Max(.001f, ladder.EffectivePlaybackRate);
        if (remaining >= .2f) { ReleaseContacts(pose); return; }
        Vector3 endRoot = Bottom + transform.up * (motion.Frames[^1].Root.y + (fromTop ? Height : 0f));
        if (fromTop) endRoot += transform.rotation * TopMountSupportOffset;
        float weight = 1f - remaining / .2f;
        MaxRequestedCorrection = 0f;
        foreach (string id in Contacts)
        {
            if (id != "LeftHand" && id != "RightHand") { pose.SetInteractionTarget(id, null); continue; }
            if (!pose.TryGetAuthoredInteractionContact(id, out Vector3 point))
                throw new InvalidOperationException("Authored ladder mount preparation requires both humanoid palm contacts.");
            if (!_authoredSupportReady)
            {
                Vector3 endPalm = endRoot + transform.rotation * (id == "LeftHand" ? GaitStartLeftPalm : GaitStartRightPalm);
                _support[id] = new SupportContact { Anchor = NearestHandRungContact(endPalm), Initialized = true };
            }
            var contact = _support[id]; contact.TargetWeight = weight; _support[id] = contact;
            Vector3 correction = Vector3.ClampMagnitude(contact.Anchor - point, .06f);
            pose.SetInteractionTarget(id, new InteractionPoseTarget(point + correction, weight: weight,
                useContact: true, preserveAuthoredContactJoints: true));
            MaxRequestedCorrection = Mathf.Max(MaxRequestedCorrection, correction.magnitude * weight);
        }
        _authoredSupportReady = true; _supportMode = "Normal";
    }

    void PrepareSlideReturn(ProceduralPoseRig pose, ActorLadder ladder)
    {
        if (!_slideReturnPrepared && ladder.AnimationProgress <= .00001f)
        {
            Vector3 palm = ladder.TransitionEndPosition + transform.rotation * GaitStartRightPalm;
            Vector3 rung = NearestHandRungContact(palm);
            float nativeRise = SlideEndMotion.Frames[^1].Root.y;
            if (nativeRise + Vector3.Dot(rung - palm, transform.up) < 0f)
            {
                Vector3 upper = NearestHandRungContact(rung + transform.up * RungSpacing);
                if (Mathf.Abs(Vector3.Dot(upper - palm, transform.up)) <= SlideEndMotion.MaxHeightAdjustment)
                    rung = upper;
                else return;
            }
            if (ladder.FitSlideReturn(rung - palm))
            { _slideReturnAnchor = rung; _slideReturnPrepared = true; }
        }
        if (!_slideReturnPrepared) return;
        float weight = Mathf.Clamp01(1f - (1f - ladder.AnimationProgress) * SlideEndMotion.Clip.length / .2f);
        if (weight <= 0f || !pose.TryGetAuthoredInteractionContact("RightHand", out var point)) return;
        _slideReturnPreparingContact = true;
        Vector3 correction = Vector3.ClampMagnitude(_slideReturnAnchor - point, .06f);
        _support["RightHand"] = new SupportContact { Anchor = _slideReturnAnchor, TargetWeight = weight };
        pose.SetInteractionTarget("RightHand", new InteractionPoseTarget(point + correction, weight: weight,
            useContact: true, preserveAuthoredContactJoints: true));
        MaxRequestedCorrection = Mathf.Max(MaxRequestedCorrection, correction.magnitude * weight);
    }

    void ApplyRailContacts(ProceduralPoseRig pose, ActorLadder ladder)
    {
        if (!SlidingRailContacts) ReleaseContacts(pose);
        SlidingRailContacts = true; MaxRequestedCorrection = 0f;
        float weight = ladder.Phase == ActorLadderPhase.SlideStart ? ladder.AnimationProgress :
            ladder.Phase == ActorLadderPhase.SlideEnd ? 1f - ladder.AnimationProgress : 1f;
        foreach (string id in Contacts)
        {
            if (id != "LeftHand" && id != "RightHand") { pose.SetInteractionTarget(id, null); continue; }
            if (!pose.TryGetAuthoredInteractionContact(id, out Vector3 point)) continue;
            Vector3 local = transform.InverseTransformPoint(point);
            Vector3 rail = transform.TransformPoint(new Vector3((id == "LeftHand" ? -1f : 1f) * Width * .5f, local.y, 0f));
            Vector3 correction = Vector3.ClampMagnitude(rail - point, .06f);
            _support[id] = new SupportContact { Anchor = rail, TargetWeight = weight };
            pose.SetInteractionTarget(id, new InteractionPoseTarget(point + correction, weight: weight, useContact: true,
                followAuthoredMotion: true, preserveAuthoredContactJoints: true));
            MaxRequestedCorrection = Mathf.Max(MaxRequestedCorrection, correction.magnitude * weight);
        }
    }

    void ApplyHeuristicContact(ProceduralPoseRig pose, string id, float dt, Vector3 displacement)
    {
        InteractionPoseTarget? target = null;
        if (pose.TryGetAuthoredInteractionContact(id, out Vector3 point))
        {
            point += displacement;
            _support.TryGetValue(id, out var support);
            float speed = support.Initialized ? Vector3.Distance(point, support.Previous) / dt : float.PositiveInfinity;
            if (speed > .3f) support.Anchored = false;
            if (!support.Anchored)
            {
                Vector3 nearest = NearestRungContact(point);
                bool stable = speed < .25f && Vector3.Distance(point, nearest) <= .06f;
                if (!stable) { support.StableTime = 0f; support.StableSamples = 0; }
                else
                {
                    if (support.StableSamples == 0 || Vector3.Distance(point, support.Anchor) > .06f ||
                        Mathf.Abs(Vector3.Dot(nearest - support.Anchor, transform.up)) > .01f)
                    { support.Anchor = nearest; support.StableTime = 0f; support.StableSamples = 0; }
                    support.StableTime += dt; support.StableSamples++;
                    // A slow swing apex is not a plant. Require sustained nearby
                    // contact, and multiple observations even across a frame hitch.
                    support.Anchored = support.StableTime >= .1f && support.StableSamples >= 3;
                }
            }
            float distance = Vector3.Distance(point, support.Anchor);
            if (support.Anchored && distance >= .09f) support.Anchored = false;
            support.Weight = support.Anchored ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.06f, .09f, distance)) : 0f;
            if (support.Anchored)
            {
                Vector3 correction = Vector3.ClampMagnitude(support.Anchor - point, .06f);
                target = new InteractionPoseTarget(point + correction, weight: support.Weight, useContact: true, preserveAuthoredContactJoints: true);
                MaxRequestedCorrection = Mathf.Max(MaxRequestedCorrection, correction.magnitude * support.Weight);
            }
            support.Previous = point; support.Initialized = true; _support[id] = support;
        }
        pose.SetInteractionTarget(id, target);
    }

    // The palm marker lies inside the hand. Seat its authored cup in front of the rung,
    // rather than placing that interior marker at the solid rung centre.
    public Vector3 NearestHandRungContact(Vector3 point) => NearestRungContact(point) - transform.forward * RungPalmDepth;

    public Vector3 NearestRungContact(Vector3 point)
    {
        Vector3 local = transform.InverseTransformPoint(point);
        float lastRung = RungOffset + Mathf.Floor((Height - RungOffset + .0001f) / RungSpacing) * RungSpacing;
        float rungHeight = Mathf.Clamp(Mathf.Round((local.y - RungOffset) / RungSpacing) * RungSpacing + RungOffset, RungOffset, lastRung);
        // The authored top transfer grips the platform's front edge, above the last regular rung.
        if (Mathf.Abs(local.y - Height) < Mathf.Abs(local.y - rungHeight)) rungHeight = Height;
        Vector3 rung = new(Mathf.Clamp(local.x, -Width * .5f + .04f, Width * .5f - .04f), rungHeight, 0f);
        return transform.TransformPoint(rung);
    }

    public static void BindFootContacts(ProceduralRigDefinition rig)
    {
        var interactions = new List<InteractionLimbDefinition>(rig.Interactions);
        for (int i = 0; i < 2; i++)
        {
            if (interactions.Exists(limb => limb.Id == Contacts[i + 2])) continue;
            var foot = rig.Feet[i];
            var end = foot.Bones[^1];
            interactions.Add(new InteractionLimbDefinition {
                Id = Contacts[i + 2], Bones = foot.Bones, JointLimit = 45f, MaximumExtension = .97f,
                BendDirection = foot.BendDirection, WristLimitDegrees = 15f,
                ContactPosition = end.InverseTransformVector(-rig.transform.up * foot.SoleOffset * rig.transform.lossyScale.x) });
        }
        rig.Interactions = interactions.ToArray();
    }
}

