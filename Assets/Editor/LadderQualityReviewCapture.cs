using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Complete production ladder recipe: both mounts, stop, reversal, and both dismounts.</summary>
public static class LadderQualityReviewCapture
{
    public static void Capture(string directory, int ladderIndex = 0, string scenario = "roundtrip", int[] detailFrames = null)
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPaused)
            throw new InvalidOperationException("Enter and pause the authored ladder review first.");
        var review = UnityEngine.Object.FindFirstObjectByType<HumanInteractionReview>();
        var actor = review != null ? review.Actor : null;
        if (actor == null || actor.View == null || ladderIndex < 0 || ladderIndex >= actor.Ladders.Length)
            throw new ArgumentException("Choose an authored ladder in the initialized review scene.");
        if (!new[] { "roundtrip", "continuous", "blocked-exit", "blocked-bottom", "interrupt", "target-disable", "mount-retry", "sprint-up", "slide-down", "fast-change", "slide-interrupt", "slide-reverse", "slide-blocked-bottom", "slide-stop", "sprint-reverse" }.Contains(scenario))
            throw new ArgumentException("Choose roundtrip, continuous, blocked-exit, blocked-bottom, interrupt, target-disable, mount-retry, sprint-up, slide-down, fast-change, slide-interrupt, slide-reverse, slide-blocked-bottom, slide-stop, or sprint-reverse.", nameof(scenario));
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new IOException("Choose an empty directory to preserve prior ladder evidence.");
        Directory.CreateDirectory(directory);
        var ladder = actor.Ladders[ladderIndex];
        bool ladderEnabled = ladder.enabled;
        GameObject blocker = null;
        var performance = actor.LadderPerformances.Snapshot().Select("Ladder", "Humanoid");
        var hidden = new List<Renderer>();
        var props = new List<GameObject>();
        var records = new List<object>();
        var events = new List<object>();
        var gripDetails = new List<object>();
        bool sprintRightObserved = false, sprintLeftReturnObserved = false, sprintTopObserved = false;
        bool normalFastReversal = ladder.SprintLeftMotion == null && ladder.SprintRightMotion == null;
        bool normalFastAscentObserved = false, normalFastReverseObserved = false;
        bool mountCancelled = false, slideEvent = false, slideObserved = false, slideBrakeObserved = false, slideStoppedObserved = false;
        AuthoredAnimationReference original = null;
        bool controls = actor.ShowControls;
        const float dt = 1f / 60f;
        int stage = 0, frames = 0;
        float stageTime = 0f, sourceTime = 0f;
        float rejectionTime = 0f;
        string lastRejection = null, failureReason = null;
        int rejectionStage = -1;
        string lastSourcePhase = null;
        (AnimationClip clip, bool reverse, float rate, string provenance) sourceReference = default;
        object[] resetClocks = null;
        var previousContacts = new Dictionary<string, (Vector3 point, Vector3 normal, Vector3 basePoint, Vector3 baseNormal, Vector3? anchor)>();
        ActorButtons previousButtons = ActorButtons.None; float previousInput = 0f;
        try
        {
            review.ResetRoom(); actor.ResetActor(); actor.ShowControls = false;
            // Explicit hidden diagnostic initialization; all subsequent movement uses the production motor.
            var initialApproach = scenario == "sprint-up" && ladder.SprintTopAvailable ? ladder.SprintBottomApproach : ladder.BottomApproach;
            var pose = new CharacterPose(initialApproach - ladder.transform.forward * .5f, ladder.transform.up, ladder.transform.forward);
            actor.Motor.ResetPose(pose); actor.Actor.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward, pose.Up));
            actor.ThirdPersonCamera.Reset(pose);
            resetClocks = ResetBaseClipClocks(actor.View.Graph);
            for (int i = 0; i < 60; i++) actor.Step(dt, default(ActorIntent));
            if (scenario == "blocked-exit")
            {
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "Temporary blocked ladder exit";
                blocker.transform.SetParent(ladder.transform, true);
                blocker.transform.position = ladder.TopApproach + ladder.transform.up * .9f;
                blocker.transform.rotation = ladder.transform.rotation;
                blocker.transform.localScale = new Vector3(.8f, 1.8f, .6f);
                Physics.SyncTransforms();
            }
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (renderer.enabled && !renderer.transform.IsChildOf(actor.Actor) && !renderer.transform.IsChildOf(ladder.transform))
                { hidden.Add(renderer); renderer.enabled = false; }
            HumanInteractionComparison.Show();
            var rig = actor.Actor.GetComponentInChildren<ProceduralRigDefinition>();
            var animator = actor.Actor.GetComponentInChildren<Animator>();
            var baseAnimator = HumanInteractionComparison.Current.ReferenceRoot.GetComponentInChildren<Animator>();
            foreach (float sign in new[] { -1f, 1f })
            {
                var prop = UnityEngine.Object.Instantiate(ladder.gameObject, ladder.transform.position + HumanInteractionComparison.Offset * sign,
                    ladder.transform.rotation);
                foreach (var collider in prop.GetComponentsInChildren<Collider>()) collider.enabled = false;
                foreach (var behaviour in prop.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
                props.Add(prop);
            }
            using var render = new AnimationReviewFrames();
            for (int frame = 0; frame < 1800; frame++)
            {
                stageTime += dt;
                if (actor.Ladder.AnimationPhase == "Slide") slideObserved = true;
                if (scenario == "slide-stop" && slideEvent && actor.Ladder.AnimationPhase == "SlideEnd") slideBrakeObserved = true;
                float input = 0f;
                if ((scenario == "blocked-bottom" && stage == 3 || scenario == "slide-blocked-bottom" && stage == 6) && blocker == null)
                {
                    blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "Temporary blocked bottom exit";
                    blocker.transform.SetParent(ladder.transform, true);
                    var exitFrame = ladder.BottomExitMotion.CreateMotion().Sample(1f);
                    float rearExtent = -exitFrame.Root.z - Mathf.Min(exitFrame.CapsuleA.z, exitFrame.CapsuleB.z) + exitFrame.Radius;
                    float wallFront = rearExtent - .02f;
                    if (wallFront <= actor.Collision.Radius + .02f)
                        throw new InvalidOperationException("The authored bottom exit has no separate rear clearance for this blocker recipe.");
                    blocker.transform.position = ladder.Bottom - ladder.transform.forward * (wallFront + .05f) + ladder.transform.up * .9f;
                    events.Add(new { frame, time = frame / 60d, action = "place-bottom-exit-blocker", wallFront, rearExtent });
                    blocker.transform.rotation = ladder.transform.rotation;
                    blocker.transform.localScale = new Vector3(.8f, 1.8f, .1f);
                    foreach (var prop in props)
                    {
                        var copy = UnityEngine.Object.Instantiate(blocker, prop.transform);
                        copy.transform.localPosition = blocker.transform.localPosition;
                        copy.transform.localRotation = blocker.transform.localRotation;
                        copy.transform.localScale = blocker.transform.localScale;
                        copy.GetComponent<Collider>().enabled = false;
                    }
                    Physics.SyncTransforms();
                }
                if (stage == 0 && frame == 30) { actor.TryUseLadder(ladder, fast: scenario == "sprint-up"); stage = 1; stageTime = 0f; }
                if (scenario == "mount-retry" && !mountCancelled && stage == 1 &&
                    actor.Ladder.Phase == ActorLadderPhase.MountBottom && actor.Ladder.AnimationProgress >= .5f)
                {
                    events.Add(new { frame, time = frame / 60d, action = "cancel-bottom-mount", progress = actor.Ladder.AnimationProgress });
                    actor.CancelLadder(); mountCancelled = true; stage = 14; stageTime = 0f;
                }
                if (actor.Ladder.AnimationPhase == "SprintTop") sprintTopObserved = true;
                if (stage == 1 && sprintTopObserved && !actor.Ladder.Active && !actor.LadderApproaching) { stage = 6; stageTime = 0f; }
                if (stage == 1 && actor.Ladder.AnimationPhase == "SprintTop") input = 1f;
                if (stage == 1 && actor.Ladder.Phase == ActorLadderPhase.Climbing) { stage = scenario == "continuous" || scenario == "sprint-up" || scenario == "sprint-reverse" || scenario.StartsWith("slide-") ? 5 : 2; stageTime = 0f; }
                if (stage == 2) { input = 1f; if (stageTime >= .7f) { stage = 3; stageTime = 0f; } }
                else if (stage == 3 && stageTime >= .7f) { stage = 4; stageTime = 0f; }
                else if (stage == 4)
                {
                    input = -1f;
                    if (scenario == "blocked-bottom")
                    {
                        if (!actor.Ladder.Active)
                        { failureReason = "The bottom-exit blocker did not reject the authored exit route."; break; }
                        if (actor.Ladder.Rejection != null && actor.Ladder.ClimbHeight <= actor.Ladder.BottomClimbHeight + .001f)
                        {
                            events.Add(new { frame, time = frame / 60d, action = "blocked-bottom-exit-rejected", reason = actor.Ladder.Rejection });
                            stage = 12; stageTime = 0f; input = 0f;
                        }
                    }
                    else if ((scenario == "interrupt" || scenario == "target-disable") && stageTime >= .1f)
                    {
                        // Interrupt the new downward blend after stopping partway up the ladder.
                        if (scenario == "target-disable") ladder.enabled = false; else actor.CancelLadder();
                        stage = 11; stageTime = 0f; input = 0f;
                    }
                    else if (stageTime >= .3f) { stage = 5; stageTime = 0f; }
                }
                else if (stage == 5)
                {
                    input = 1f;
                    if (scenario == "sprint-reverse" && normalFastReversal && actor.Ladder.Phase == ActorLadderPhase.Climbing &&
                        actor.Ladder.GaitPhase >= .65f && actor.Ladder.GaitPhase <= .85f &&
                        actor.Ladder.EffectivePlaybackRate >= actor.Ladder.SprintPlaybackRate * .95f)
                    {
                        normalFastAscentObserved = true; stage = 19; stageTime = 0f; input = -1f;
                        events.Add(new { frame, time = frame / 60d, action = "reverse-fast-normal-gait", actor.Ladder.GaitPhase });
                    }
                    else if (scenario == "sprint-reverse" && !normalFastReversal && actor.Ladder.AnimationPhase == "SprintUpRight" && actor.Ladder.AnimationProgress >= .25f)
                    {
                        sprintRightObserved = true; stage = 19; stageTime = 0f; input = -1f;
                        events.Add(new { frame, time = frame / 60d, action = "reverse-right-leading-sprint", progress = actor.Ladder.AnimationProgress });
                    }
                    else if (!actor.Ladder.Active && !actor.LadderApproaching) { stage = 6; stageTime = 0f; input = 0f; }
                    else if (scenario == "blocked-exit" && actor.Ladder.Rejection != null &&
                        actor.Ladder.ClimbHeight >= actor.Ladder.TopClimbHeight - .01f)
                    { stage = 9; stageTime = 0f; input = 0f; }
                }
                else if (stage == 6 && stageTime > .6f && actor.Motor.Grounded)
                {
                    if (!actor.TryUseLadder(ladder, fromTop: true, fast: scenario == "sprint-up")) throw new InvalidOperationException(actor.LadderStatus);
                    stage = 7; stageTime = 0f;
                }
                else if (stage == 7)
                {
                    input = actor.Ladder.Active ? -1f : 0f;
                    if (!slideEvent && (scenario == "slide-interrupt" || scenario == "slide-reverse" || scenario == "slide-stop") &&
                        actor.Ladder.AnimationPhase == "Slide" && actor.Ladder.AnimationProgress >= .15f)
                    {
                        slideEvent = true; input = 0f; stageTime = 0f;
                        if (scenario == "slide-interrupt") { actor.CancelLadder(); stage = 11; }
                        else stage = scenario == "slide-stop" ? 16 : 15;
                        events.Add(new { frame, time = frame / 60d, action = scenario == "slide-interrupt" ? "cancel-active-slide" : scenario == "slide-stop" ? "release-slide-for-stop" : "release-slide-for-reversal" });
                    }
                    else if (scenario == "slide-blocked-bottom" && actor.Ladder.Rejection != null)
                    {
                        if (!slideObserved) { failureReason = "Bottom obstruction rejected before the requested active slide occurred."; break; }
                        slideEvent = true; stage = 12; stageTime = 0f; input = 0f;
                        events.Add(new { frame, time = frame / 60d, action = "blocked-slide-bottom-rejected", reason = actor.Ladder.Rejection });
                    }
                    else if (!actor.Ladder.Active && !actor.LadderApproaching)
                    {
                        if (scenario != "slide-down" && scenario.StartsWith("slide-") && !slideEvent)
                        { failureReason = "The requested slide event did not occur before descent completed."; break; }
                        stage = 8; stageTime = 0f;
                    }
                }
                else if (stage == 15 && actor.Ladder.AnimationPhase == "SlideEnd")
                {
                    input = 1f; stage = 13; stageTime = 0f;
                    events.Add(new { frame, time = frame / 60d, action = "reverse-during-slide-end", progress = actor.Ladder.AnimationProgress });
                }
                else if (stage == 16 && slideBrakeObserved && actor.Ladder.Phase == ActorLadderPhase.Climbing)
                {
                    stage = 17; stageTime = 0f;
                    events.Add(new { frame, time = frame / 60d, action = "slide-brake-returned-to-stopped-climbing" });
                }
                else if (stage == 17)
                {
                    if (actor.Ladder.Phase != ActorLadderPhase.Climbing)
                    { failureReason = "The slide-stop hold did not remain in Climbing."; break; }
                    if (stageTime >= .7f)
                    {
                        slideStoppedObserved = true; stage = 18; stageTime = 0f;
                        events.Add(new { frame, time = frame / 60d, action = "resume-normal-descent-after-slide-stop" });
                    }
                }
                else if (stage == 18)
                {
                    input = actor.Ladder.Active ? -1f : 0f;
                    if (!actor.Ladder.Active && !actor.LadderApproaching) { stage = 8; stageTime = 0f; }
                }
                else if (stage == 19)
                {
                    input = -1f;
                    if (normalFastReversal && actor.Ladder.Phase == ActorLadderPhase.Climbing &&
                        actor.Ladder.ClimbDirection == "Down" && actor.Ladder.EffectivePlaybackRate > 0f)
                    {
                        normalFastReverseObserved = true; stage = 20; stageTime = 0f; input = 0f;
                        events.Add(new { frame, time = frame / 60d, action = "fast-normal-source-clock-reversed", actor.Ladder.GaitPhase });
                    }
                    else if (!normalFastReversal && !actor.Ladder.Sprinting && actor.Ladder.Phase == ActorLadderPhase.Climbing && actor.Ladder.PrimarySupportIsLeft)
                    {
                        sprintLeftReturnObserved = true; stage = 20; stageTime = 0f; input = 0f;
                        events.Add(new { frame, time = frame / 60d, action = "right-sprint-returned-to-normal-left-support", actor.Ladder.GaitPhase });
                    }
                }
                else if (stage == 20 && stageTime >= .5f) { stage = 18; stageTime = 0f; }
                else if (stage == 9 && stageTime > .7f) { stage = 10; stageTime = 0f; }
                else if (stage == 10)
                {
                    input = -1f;
                    if (!actor.Ladder.Active) { stage = 8; stageTime = 0f; input = 0f; }
                }
                else if (stage == 11 && stageTime > 1.2f) { stage = 8; stageTime = 0f; }
                else if (stage == 12 && stageTime > .7f) { stage = 13; stageTime = 0f; }
                else if (stage == 13)
                {
                    input = 1f;
                    if (!actor.Ladder.Active) { stage = 8; stageTime = 0f; input = 0f; }
                }
                else if (stage == 14 && stageTime > .6f && actor.Motor.Grounded)
                {
                    bool accepted = actor.TryUseLadder(ladder);
                    events.Add(new { frame, time = frame / 60d, action = "retry-bottom-mount", accepted });
                    if (!accepted) { failureReason = actor.LadderStatus; break; }
                    stage = 1; stageTime = 0f;
                }
                if (stage == 14 && stageTime > 4f)
                { failureReason = "The cancelled bottom mount did not recover grounded motor control within four seconds."; break; }
                bool sprint = scenario == "sprint-up" || scenario == "sprint-reverse" && stage == 5 && input > 0f || scenario.StartsWith("slide-") && stage == 7 && input < 0f ||
                    scenario == "fast-change" && (stage == 2 && stageTime >= .2f && stageTime < .5f || stage == 4 ||
                        stage == 5 && stageTime < .4f || stage == 7 && (stageTime < .6f || stageTime >= 1.2f));
                ActorButtons buttons = sprint && (input != 0f || scenario == "sprint-up") ? ActorButtons.Sprint : ActorButtons.None;
                if (buttons != previousButtons || input != previousInput)
                    events.Add(new { frame, time = frame / 60d, action = "intent-change", input, buttons = buttons.ToString() });
                previousButtons = buttons; previousInput = input;
                actor.Step(dt, new ActorIntent(new Vector2(0f, input), Vector2.zero, buttons, (uint)frame));
                HumanInteractionComparison.Current.Sample(dt);
                string sourcePhase = actor.Ladder.Active ? actor.Ladder.AnimationPhase : "Idle";
                var source = performance.GetPhase(sourcePhase);
                if (lastSourcePhase != sourcePhase)
                {
                    sourceReference = SourceReference(sourcePhase);
                    var referenceClip = sourceReference.clip;
                    original?.Dispose(); original = new AuthoredAnimationReference(actor.CharacterPrefab, referenceClip, false);
                    original.Root.transform.GetChild(0).localScale = Vector3.one;
                    original.Root.transform.localScale = actor.Actor.lossyScale;
                    foreach (var skin in original.Root.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.forceMatrixRecalculationPerRender = true;
                    sourceTime = 0f; lastSourcePhase = sourcePhase;
                }
                bool canonicalGait = sourcePhase == "Up" && actor.Ladder.Phase == ActorLadderPhase.Climbing;
                bool descendingGait = canonicalGait && actor.Ladder.ClimbDirection == "Down";
                sourceTime += descendingGait ? -dt : dt;
                original.Root.transform.SetPositionAndRotation(actor.Actor.position - HumanInteractionComparison.Offset, actor.Actor.rotation);
                bool reverseSource = canonicalGait ? descendingGait : sourceReference.reverse;
                float sourcePlaybackRate = sourceReference.rate;
                double referenceTime = source.Loop ? Mathf.Repeat(sourceTime * sourcePlaybackRate, original.Clip.length) : Math.Min(sourceTime * sourcePlaybackRate, original.Clip.length);
                original.Sample((sourcePhase == "ApproachStep" ? HumanLadderAuthor.ApproachSourceStart : 0d) +
                    (!canonicalGait && reverseSource ? original.Clip.length - referenceTime : referenceTime));
                if (sourcePhase == "ApproachStep")
                {
                    // Align the independent original segment for viewing without copying the adapted clip or its clock.
                    Vector3 hips = original.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    original.Root.transform.position -= Vector3.ProjectOnPlane(hips - original.Root.transform.position, actor.Actor.up);
                }
                records.Add(new { frame, time = (frame + 1) / 60d, recipeStage = stage, input, buttons = buttons.ToString(), sprintHeld = buttons.HasFlag(ActorButtons.Sprint), ladderPhase = actor.Ladder.Phase.ToString(),
                    actor.Ladder.AnimationPhase, actor.Ladder.AnimationProgress, actor.Ladder.ClimbDirection, actor.Ladder.GaitPhase,
                    actor.Ladder.SourceCycleTravel, actor.Ladder.PrimarySupportOnly, actor.Ladder.PrimarySupportIsLeft, supportFitOffset = V(actor.Ladder.SupportFitOffset), actor.Ladder.SupportFitBlocked,
                    actor.LadderStatus, actor.LadderApproaching,
                    actor.LadderEntrySupportSpeed, locomotionSpeed = actor.View.Speed,
                    cycleIndex = Mathf.FloorToInt(actor.Ladder.AnimationProgress), cyclePhase = Mathf.Repeat(actor.Ladder.AnimationProgress, 1f),
                    handHeightDifference = actor.View.Pose.TryGetInteractionContact("RightHand", out var rightPalm, out _) &&
                        actor.View.Pose.TryGetInteractionContact("LeftHand", out var leftPalm, out _) ?
                        Vector3.Dot(rightPalm - leftPalm, ladder.transform.up) : float.NaN,
                    actor.Ladder.ClimbHeight, actor.Ladder.Rejection, ladder.MaxRequestedCorrection,
                    root = V(actor.Actor.position), sourcePhase, sourceTime, original.ClipTimeSeconds,
                    sourceClip = original.ClipAssetPath, sourceClipName = original.Clip.name, reverseSource, sourcePlaybackRate, productionNominalPlaybackRate = actor.Ladder.EffectivePlaybackRate, actualEffectivePlaybackRate = actor.Ladder.EffectivePlaybackRate,
                    sourcePhaseMayBeIncomplete = actor.Ladder.EffectivePlaybackRate > sourcePlaybackRate,
                    sourceProvenance = sourceReference.provenance,
                    graphInputs = Enumerable.Range(0, actor.View.Graph.BaseMixer.GetInputCount())
                        .Where(index => actor.View.Graph.BaseMixer.GetInputWeight(index) > .0001f)
                        .Select(index => {
                            var mixer = actor.View.Graph.BaseMixer;
                            var playable = (AnimationClipPlayable)mixer.GetInput(index);
                            return new { index, clip = AssetDatabase.GetAssetPath(playable.GetAnimationClip()),
                                time = playable.GetTime(), weight = mixer.GetInputWeight(index),
                                footIK = playable.GetApplyFootIK(), playableIK = playable.GetApplyPlayableIK() };
                        }).ToArray(),
                    contacts = new[] { "LeftHand", "RightHand", "LadderLeftFoot", "LadderRightFoot" }.Select(id => {
                        bool hasAuthored = actor.View.Pose.TryGetAuthoredInteractionContact(id, out var authored);
                        bool hasFinal = actor.View.Pose.TryGetInteractionContact(id, out var final, out var reached);
                        var rung = hasAuthored ? ladder.NearestRungContact(authored) : Vector3.zero;
                        bool hasAnchor = ladder.TryGetContactAnchor(id, out var anchor);
                        float supportWeight = ladder.SupportWeight(id);
                        bool expectedSupport = !ladder.SlidingRailContacts && supportWeight > .001f;
                        bool hasRailContact = ladder.TryGetRailContact(id, out var railTarget);
                        Vector3 railError = hasRailContact && hasFinal ? final - railTarget : Vector3.zero;
                        var binding = rig.Interactions.FirstOrDefault(limb => limb.Id == id);
                        HumanBodyBones boneId = id switch { "LeftHand" => HumanBodyBones.LeftHand, "RightHand" => HumanBodyBones.RightHand,
                            "LadderLeftFoot" => HumanBodyBones.LeftFoot, _ => HumanBodyBones.RightFoot };
                        var baseBone = baseAnimator.GetBoneTransform(boneId);
                        bool hasOrientation = binding != null && binding.Bones.Length > 0 && baseBone != null;
                        Vector3 normal = hasOrientation ? binding.Bones[^1].rotation * binding.ContactRotation * Vector3.forward : Vector3.zero;
                        Vector3 baseNormal = hasOrientation ? baseBone.rotation * binding.ContactRotation * Vector3.forward : Vector3.zero;
                        Vector3 basePoint = hasOrientation ? baseBone.TransformPoint(binding.ContactPosition) - HumanInteractionComparison.Offset : Vector3.zero;
                        bool hasPrevious = previousContacts.TryGetValue(id, out var previous);
                        float? normalStep = hasOrientation && hasPrevious ? Vector3.Angle(previous.normal, normal) : null;
                        float? baseNormalStep = hasOrientation && hasPrevious ? Vector3.Angle(previous.baseNormal, baseNormal) : null;
                        float? contactStep = hasFinal && hasPrevious ? Vector3.Distance(previous.point, final) : null;
                        float? baseContactStep = hasOrientation && hasPrevious ? Vector3.Distance(previous.basePoint, basePoint) : null;
                        float? anchorStep = hasAnchor && hasPrevious && previous.anchor.HasValue ? Vector3.Distance(previous.anchor.Value, anchor) : null;
                        if (hasFinal && hasOrientation) previousContacts[id] = (final, normal, basePoint, baseNormal, hasAnchor ? anchor : null);
                        else previousContacts.Remove(id);
                        return new { id, hasAuthored, hasFinal, reached, anchored = ladder.ContactAnchored(id), authored = hasAuthored ? V(authored) : null,
                            final = hasFinal ? V(final) : null, rung = hasAuthored ? V(rung) : null,
                            nearestRungCenter = hasAuthored ? V(rung) : null,
                            nearestPlannedPalmTarget = hasAuthored && id.EndsWith("Hand") ? V(ladder.NearestHandRungContact(authored)) : null,
                            plannedPalmDepthMeters = id.EndsWith("Hand") ? ladder.RungPalmDepth : 0f,
                            targetBasis = id.EndsWith("Hand") ? "Palm marker target is rung center minus ladder forward times RungPalmDepth; expected error uses persistent anchor. Skin grip remains visually unverified by these markers." : "Foot target uses rung center; expected error uses persistent anchor.",
                            ladder.SlidingRailContacts, hasRailContact, railTarget = hasRailContact ? V(railTarget) : null,
                            railErrorMeters = hasRailContact && hasFinal ? (float?)railError.magnitude : null,
                            railNormalXErrorMeters = hasRailContact && hasFinal ? (float?)Vector3.Dot(railError, ladder.transform.right) : null,
                            railNormalZErrorMeters = hasRailContact && hasFinal ? (float?)Vector3.Dot(railError, ladder.transform.forward) : null,
                            railContactConvention = "Signed world-metre errors on ladder right X and forward Z; vertical rail travel is intentional, not fixed-rung sliding.",
                            hasOrientation, palmNormal = hasOrientation ? V(normal) : null, basePalmNormal = hasOrientation ? V(baseNormal) : null,
                            normalStepDegrees = normalStep, baseNormalStepDegrees = baseNormalStep,
                            normalDifferenceDegrees = hasOrientation ? (float?)Vector3.Angle(baseNormal, normal) : null,
                            palmNormalReversal = normalStep.HasValue && normalStep.Value > 90f,
                            contactStepMeters = contactStep, baseContactStepMeters = baseContactStep, anchorStepMeters = anchorStep,
                            fingerPoints = id.EndsWith("Hand") ? HandDigits(animator, id == "LeftHand", Vector3.zero) : null,
                            baseFingerPoints = id.EndsWith("Hand") ? HandDigits(baseAnimator, id == "LeftHand", HumanInteractionComparison.Offset) : null,
                            expectedSupportKnown = actor.Ladder.Phase == ActorLadderPhase.Climbing, supportWeight, preparationWeight = ladder.ContactPreparationWeight(id),
                            fullSupport = expectedSupport && supportWeight >= .999f, contactBlend = expectedSupport && supportWeight < .999f,
                            expectedSupport, missingExpectedAnchor = expectedSupport && !hasAnchor,
                            expectedSupportConvention = "Runtime planned support weight; independent of successful anchor acquisition. All four contacts remain in the records, including zero-weight contacts.",
                            hasAnchor, anchor = hasAnchor ? V(anchor) : null,
                            anchorAtTopPlatform = hasAnchor && Mathf.Abs(ladder.transform.InverseTransformPoint(anchor).y - ladder.Height) < .001f,
                            anchorRungIndex = hasAnchor ? Mathf.RoundToInt((ladder.transform.InverseTransformPoint(anchor).y - ladder.RungOffset) / ladder.RungSpacing) : -1,
                            anchorError = hasAnchor && hasFinal ? Vector3.Distance(final, anchor) : -1f,
                            nearestRungMetric = "Nearest to current authored contact; not a persistent support target",
                            authoredError = hasAuthored ? Vector3.Distance(authored, rung) : -1f,
                            finalError = hasAuthored && hasFinal ? Vector3.Distance(final, rung) : -1f,
                            correction = hasAuthored && hasFinal ? Vector3.Distance(authored, final) : -1f };
                    }).ToArray() });
                if (frame % 2 == 0)
                {
                    var center = actor.Actor.position + Vector3.up * .95f;
                    render.Capture(Path.Combine(directory, $"frame-{frame / 2:D4}.png"),
                        new[] { center - HumanInteractionComparison.Offset, center + HumanInteractionComparison.Offset, center }, ladder.transform.rotation);
                    if (detailFrames != null && detailFrames.Contains(frame / 2))
                    foreach (string hand in new[] { "LeftHand", "RightHand" })
                    {
                        if (!actor.View.Pose.TryGetInteractionContact(hand, out var palm, out _)) continue;
                        render.Capture(Path.Combine(directory, $"detail-{hand}-{frame / 2:D4}.png"),
                            new[] { palm - HumanInteractionComparison.Offset, palm + HumanInteractionComparison.Offset, palm },
                            ladder.transform.rotation, .18f);
                        render.Capture(Path.Combine(directory, $"detail-front-{hand}-{frame / 2:D4}.png"),
                            new[] { palm - HumanInteractionComparison.Offset, palm + HumanInteractionComparison.Offset, palm },
                            ladder.transform.rotation * Quaternion.Euler(0f, 180f, 0f), .18f);
                        var rungMesh = ladder.GetComponentsInChildren<MeshFilter>().Where(mesh => mesh.name == "Rung")
                            .OrderBy(mesh => Mathf.Abs(Vector3.Dot(mesh.transform.position - palm, ladder.transform.up))).First();
                        var surface = new HumanInteractionReview.Target { Hinge = rungMesh.transform, Contact = rungMesh.transform,
                            LidVertices = rungMesh.sharedMesh.vertices, LidTriangles = rungMesh.sharedMesh.triangles };
                        using var measurement = new DoorSurfaceMeasurement(actor.Actor,
                            animator.GetBoneTransform(hand == "LeftHand" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand));
                        bool measured = measurement.Sample(surface, palm, ladder.transform.forward, out var skin, out var rungSurface, out var normal);
                        gripDetails.Add(new { frame = frame / 2, hand, measured, palm = V(palm),
                            skin = measured ? V(skin) : null, rungSurface = measured ? V(rungSurface) : null,
                            signedSurfaceGap = measured ? (float?)Vector3.Dot(skin - rungSurface, normal) : null,
                            convention = "Rendered skin ray through palm against nearest solid rung. Positive gap, negative penetration. Does not certify every finger triangle." });
                    }
                }
                frames = frame + 1;
                string rejection = actor.Ladder.Rejection;
                if ((scenario == "roundtrip" || scenario == "continuous" || scenario == "mount-retry" || scenario == "sprint-up" || scenario == "slide-down" || scenario == "fast-change" || scenario.StartsWith("slide-") && !slideObserved) && !string.IsNullOrEmpty(rejection))
                {
                    rejectionTime = rejection == lastRejection && stage == rejectionStage ? rejectionTime + dt : 0f;
                    lastRejection = rejection; rejectionStage = stage;
                    if (rejectionTime >= 2f) { failureReason = rejection; break; }
                }
                else { rejectionTime = 0f; lastRejection = null; }
                if (stage == 8 && stageTime > .8f) break;
            }
            if (scenario == "sprint-reverse" && (normalFastReversal ? !normalFastAscentObserved || !normalFastReverseObserved : !sprintRightObserved || !sprintLeftReturnObserved))
                failureReason = normalFastReversal ? "The reversal did not observe fast normal ascent and the downward source clock." :
                    "The sprint reversal did not observe the right-leading take and normal left-support return.";
            if (failureReason == null && scenario == "slide-stop" && (!slideObserved || !slideBrakeObserved || !slideStoppedObserved))
                failureReason = "The slide-stop recipe did not observe active slide, brake, and stopped climbing.";
            if (stage != 8 && failureReason == null)
                failureReason = "Capture timed out before the recipe reached its completed state.";
            File.WriteAllText(Path.Combine(directory, "metadata.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                sprintReversalMode = normalFastReversal ? "normal gait with fast intent" : "optional Wall leading-hand takes",
                normalFastAscentObserved, normalFastReverseObserved,
                legacySprintFlagsConvention = "sprintRightObserved and sprintLeftReturnObserved apply only to the optional Wall-take reversal branch.",
                scenario, initialApproach = V(initialApproach), initialPosition = V(pose.Position), resetClocks, sprintTopObserved, sprintRightObserved, sprintLeftReturnObserved, slideObserved, slideEvent, slideBrakeObserved, slideStoppedObserved, recipe = scenario == "sprint-reverse" ? (normalFastReversal ? "fast normal ascent through left support, reverse the canonical source clock, hold .5 seconds, descend normally" : "sprint ascent until right-leading take .25, reverse to normal left support, hold .5 seconds, descend normally") : scenario == "slide-stop" ? "normal ascent and top remount, active slide, release and brake, stopped hold .7 seconds, normal descent to bottom" : scenario == "slide-interrupt" ? "normal ascent and top mount, slide, cancel active slide, motor recovery" : scenario == "slide-reverse" ? "normal ascent and top mount, slide, release, reverse during SlideEnd, ascend and exit" : scenario == "slide-blocked-bottom" ? "normal ascent, introduce bottom exit blocker, slide to rejection, hold, ascend and exit" : scenario == "sprint-up" ? "Sprint held through approach, fast bottom mount or native SprintTop, ascent and top exit; fast top mount, slide when eligible, and fast bottom exit" : scenario == "slide-down" ? "normal ascent, sprint-held slide descent" : scenario == "fast-change" ? "normal/fast ascent changes, stop, fast descent interruption and direction change, normal/fast descent changes" : scenario == "continuous" ? "continuous up gait, top dismount/mount, continuous down gait, bottom exit" : scenario == "roundtrip" ? "bottom approach/mount, climb, stop, reverse, top exit, top mount, descend, bottom exit" :
                    scenario == "blocked-exit" ? "bottom mount, stop/reverse, attempt blocked top exit, hold, reverse down, bottom exit" :
                    scenario == "blocked-bottom" ? "bottom mount, climb, introduce blocked bottom exit, descend, reject exit, hold, reverse up, top exit" :
                    scenario == "mount-retry" ? "cancel halfway through bottom mount, recover under motor gravity, approach and retry, complete the full ladder roundtrip" :
                    "bottom mount, climb, stop, begin reversal, interrupt during blend, recover under motor gravity",
                runtimeAssembly = typeof(ActorLadder).Module.ModuleVersionId.ToString(), captureAssembly = typeof(LadderQualityReviewCapture).Module.ModuleVersionId.ToString(),
                completed = stage == 8 && failureReason == null, failureReason, events, gripDetails, frames, simulationHz = 60, captureHz = 30, scene = actor.gameObject.scene.path,
                ladderIndex, ladder.Height, ladder.RungSpacing, ladder.RungOffset, ladder.BodyDistance, ladder.RungsPerCycle,
                columns = new[] { "A: original source / RETARGETED / phase-specific native rate", "B: production pose / no limb corrections / shared runtime root", "C: production runtime" },
                sourceConvention = "A runs the original source on the target rig. Source switches follow action phase changes. Faster sprint production can switch before A completes its native 1x take; ladder-skip-source contains the separate full native 31-frame Wall source reference. A climb playback uses the publisher controller intended 1.5x. Production normal gait uses 1x or the configured SprintPlaybackRate under fast intent; actualEffectivePlaybackRate records acceleration, stops, and transfer rate changes. ApproachStep uses the original full WalkForwardStartAndStop FBX from the configured native source start, hips-aligned for silhouette; its world travel is checked separately by the original-motion reconstruction report. SlideStart/Slide/SlideEnd use original Universal Traversal FBXs at native 1x. Optional SprintUp/SprintUpRight use original Universal Traversal Wall Climb Up left/right hand FBXs at native 1x. When those motions are unset, fast ascent retains the original CCP normal gait. Mirrored top transfers use a separately mirrored original CCP clip without production grip curves. Other phases use the original CCP FBX. Down and reverse transfers sample that original backwards; climb uses 1.5x and transfers use 1x. A disables native foot-goal IK for the muscle reference; B and C retain native retarget foot goals. Diagnostic switches do not validate transitions.",
                reset = "Room reset; all existing base clip clocks set to zero twice; hidden actor initialization half a metre outside bottom approach; 60 deterministic settling steps. No runtime graph policy changed.",
                pending = "Rung count per cycle and contact timing require rendered validation; source references do not certify final contacts.",
                libraryHash = AssetDatabase.GetAssetDependencyHash(HumanLadderAuthor.LibraryPath).ToString(), records
            }, Newtonsoft.Json.Formatting.Indented));
        }
        finally
        {
            original?.Dispose(); HumanInteractionComparison.Hide(); actor.ShowControls = controls;
            if (blocker != null) UnityEngine.Object.DestroyImmediate(blocker);
            if (ladder != null) ladder.enabled = ladderEnabled;
            foreach (var prop in props) if (prop != null) UnityEngine.Object.DestroyImmediate(prop);
            foreach (var renderer in hidden) if (renderer != null) renderer.enabled = true;
        }
    }
    static object[] ResetBaseClipClocks(ActorAnimationGraph graph)
    {
        var clocks = new List<object>();
        var mixer = graph.BaseMixer;
        for (int i = 0; i < mixer.GetInputCount(); i++)
        {
            var input = mixer.GetInput(i);
            if (!input.IsValid() || input.GetPlayableType() != typeof(AnimationClipPlayable)) continue;
            var clip = (AnimationClipPlayable)input;
            double previous = clip.GetTime();
            clip.SetTime(0d); clip.SetTime(0d);
            clocks.Add(new { index = i, clip = clip.GetAnimationClip().name, previousTime = previous, resetTime = clip.GetTime() });
        }
        return clocks.ToArray();
    }

    static (AnimationClip clip, bool reverse, float rate, string provenance) SourceReference(string phase)
    {
        if (phase == "ApproachStep")
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Characters/Animations/Ladder/EntryCandidates/Traversal_WalkForwardStartAndStop.FBX")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
            return (clip, false, 1f, "Original WalkForwardStartAndStop FBX, selected source start, retargeted, independent 1x clock.");
        }
        if (phase == "ExitBottomMirrored")
        {
            const string path = "Assets/Art/Characters/Animations/Ladder/CCP Original Bottom Up Mirrored.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) throw new InvalidOperationException("Missing independent mirrored original ladder reference: " + path);
            return (clip, true, 1f, "Original CCP LadderBottomUp with Unity mirror clip setting, sampled backwards at independent native 1x; no production grip curves.");
        }
        if (phase == "ExitTopMirrored" || phase == "MountTopMirrored")
        {
            const string path = "Assets/Art/Characters/Animations/Ladder/CCP Original Top Up Mirrored.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) throw new InvalidOperationException("Missing independent mirrored original ladder reference: " + path);
            return (clip, phase == "MountTopMirrored", 1f, "Original CCP Ladder TopUp with Unity mirror clip setting; independent native 1x clock, no production grip curves.");
        }
        if (phase == "SprintTop")
        {
            const string path = "Assets/Art/Characters/Animations/Ladder/UrgencyCandidates/mantle-high-3m-climbup-run-to-run.fbx";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__"));
            return (clip, false, 1f, "Original Threepeat mantle-high-3m-climbup-run-to-run FBX, full native 1.2-second take at independent 1x; retargeted source reference.");
        }
        if (phase == "SprintUp" || phase == "SprintUpRight")
        {
            string hand = phase == "SprintUp" ? "LeftHand" : "RightHand";
            string path = "Assets/Art/Characters/Animations/Ladder/GaitCandidates/AnimSeq_Traversal_Wall_Climb_Up_" + hand + ".FBX";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__"));
            return (clip, false, 1f, "Original Universal Traversal Wall Climb Up " + hand + " FBX at native 1x, independent one-second source clock; raw Generic counterpart Traversal_Wall_Climb_Up_" + hand + ".fbx, raw root +.6734 metres. No production fixed-origin curves.");
        }
        if (phase == "SlideStart" || phase == "Slide" || phase == "SlideEnd")
        {
            string segment = phase == "SlideStart" ? "Start" : phase == "SlideEnd" ? "End" : "Loop";
            string path = "Assets/Art/Characters/Animations/Ladder/Slide/AnimSeq_Traversal_Ladder_Slide_Down_" + segment + ".FBX";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__"));
            return (clip, false, 1f, "Original Universal Traversal FBX at native 1x with independent source clock; Humanoid retarget reference. Generic raw-root counterpart: Traversal_Ladder_Slide_Down_" + segment + ".fbx in the same folder.");
        }
        string name = phase switch { "MountBottom" or "ExitBottom" => "LadderBottomUp", "MountTop" or "ExitTop" => "Ladder TopUp",
            "Up" or "Down" => "Ladder Up", "Idle" => "Ladder Idle",
            _ => throw new InvalidOperationException("No reviewed original-source mapping for ladder phase: " + phase) };
        return (LadderGaitAuthor.Source(name), phase == "Down" || phase == "MountTop" || phase == "ExitBottom",
            phase == "Up" || phase == "Down" ? 1.5f : 1f,
            "Original CCP publisher FBX on target rig. Native climb 1.5x; transfers 1x. Reverse phases follow publisher convention.");
    }

    static object[] HandDigits(Animator animator, bool left, Vector3 offset)
    {
        HumanBodyBones[] bones = left ? new[] { HumanBodyBones.LeftThumbDistal, HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingDistal, HumanBodyBones.LeftLittleDistal } : new[] { HumanBodyBones.RightThumbDistal, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightRingDistal, HumanBodyBones.RightLittleDistal };
        return bones.Select(id => { var bone = animator.GetBoneTransform(id); return (object)new { bone = id.ToString(), available = bone != null,
            position = bone != null ? V(bone.position - offset) : null }; }).ToArray();
    }
    static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
}





