using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Repeatable review recipes using the live production motor and pose graph.</summary>
public static class HumanoidQualityReviewCapture
{
    public static void Capture(string directory, string action)
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPaused)
            throw new InvalidOperationException("Enter and pause the Human interaction review first.");
        if (!new[] { "walk", "strafe", "directions", "jump", "run-jump", "run-jump-through", "run-jump-stop", "run-jump-resume", "run-jump-recovery-resume", "run-jump-late-stop", "chest", "chest-complete", "chest-search", "chest-approach", "chest-cancel", "stance", "stance-interrupt" }.Contains(action))
            throw new ArgumentException("Unknown review action.", nameof(action));
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new IOException("Choose an empty review directory to preserve earlier evidence.");
        Directory.CreateDirectory(directory);
        var review = UnityEngine.Object.FindAnyObjectByType<HumanInteractionReview>();
        if (review == null || review.Actor.View == null) throw new InvalidOperationException("The review actor is unavailable.");
        var actor = review.Actor;
        bool controls = actor.ShowControls;
        GameObject floor = null;
        GameObject directionFloor = null;
        Material sourceRigMaterial = null;
        AuthoredAnimationReference original = null;
        var records = new List<object>();
        var hidden = new List<Renderer>();
        var disabledColliders = new List<Collider>();
        const float dt = 1f / 60f;
        bool chest = action.StartsWith("chest", StringComparison.Ordinal);
        bool stance = action.StartsWith("stance", StringComparison.Ordinal);
        int frames = action == "directions" || action == "chest-search" ? 1080 : action == "chest-cancel" ? 360 : chest ? 720 : stance ? 480 : 240;
        bool jump = action.Contains("jump");
        bool runningJumpAction = action.StartsWith("run-jump", StringComparison.Ordinal);
        var actionSource = chest ? LoadClip("Assets/Art/Interactions/Animations/Loot_TreasureChest_Open_Only.fbx") :
            jump ? LoadClip("Assets/Art/Characters/Animations/Jump Full.fbx") : null;
        var inspectStartSource = chest ? LoadClip("Assets/Art/Interactions/Animations/Loot_TreasureChest_GrabItem.fbx") : null;
        var inspectLoopSource = chest ? LoadClip("Assets/Art/Interactions/Animations/Inspection Idle Source.fbx") : null;
        var inspectEndSource = chest ? LoadClip("Assets/Art/Interactions/Animations/Loot_TreasureChest_GrabItem.fbx") : null;
        bool runningJump = runningJumpAction && actor.Performances != null &&
            actor.Performances.Entries.Any(entry => entry.Action == "Jump" && entry.Condition == "Running");
        bool contextualJump = runningJump && actor.Performances.Entries.Any(entry => entry.Condition == "Running" && entry.Phases.Any(phase => phase.Name == "LandingStop"));
        if (action == "jump" && actor.Performances.Entries.Any(entry => entry.Condition == "Stationary"))
            actionSource = LoadClip(HumanoidStandingHopAuthor.SourcePath);
        if (runningJump) actionSource = LoadClip(contextualJump ? HumanoidRunningJumpAuthor.ForwardSourcePath : HumanoidRunningJumpAuthor.SourcePath);
        double takeoffSeconds = action == "jump" && actionSource.name == "Unreal Take" ? 14d / 30d : runningJump && !contextualJump ? 10d / 30d : .4d;
        const string animationFolder = "Assets/Art/Characters/Animations/";
        var idleSource = !stance ? LoadClip(animationFolder + "HumanoidIdle.fbx") : null;
        var walkSource = action == "walk" ? LoadClip(animationFolder + "Walk Forward.fbx") : null;
        var leftSource = action == "strafe" ? LoadClip(animationFolder + "Walk Left.fbx") : null;
        var rightSource = action == "strafe" ? LoadClip(animationFolder + "Walk Right.fbx") : null;
        var runSource = runningJumpAction ? LoadClip(animationFolder + "Run Forward.fbx") : null;
        var crawlEnterSource = stance ? LoadClip(animationFolder + "Crawl Enter.fbx") : null;
        var crawlExitSource = stance ? LoadClip(animationFolder + "Crawl Exit.fbx") : null;
        string[] directionNames = { "Forward", "ForwardRight", "Right", "BackwardRight", "Backward", "BackwardLeft", "Left", "ForwardLeft" };
        Vector2[] directions = { Vector2.up, new Vector2(1f, 1f).normalized, Vector2.right, new Vector2(1f, -1f).normalized,
            Vector2.down, new Vector2(-1f, -1f).normalized, Vector2.left, new Vector2(-1f, 1f).normalized };
        var directionalSources = action == "directions" ? new[] { "Walk", "Run" }.SelectMany(gait =>
            directionNames.Select(direction => LoadClip(animationFolder + gait + " " + direction + ".fbx"))).ToArray() : Array.Empty<AnimationClip>();
        // SOURCE.md maps these locomotion and prone clips to Kevin Iglesias' HumanF model.
        bool needsFemaleSourceRig = action == "walk" || action == "strafe" || action == "directions" || stance;
        var femaleSourceRig = needsFemaleSourceRig ?
            AssetDatabase.LoadAssetAtPath<GameObject>(animationFolder + "Human Motion Reference.fbx") : null;
        if (needsFemaleSourceRig && femaleSourceRig == null)
            throw new InvalidOperationException("The verified HumanF original reference rig is unavailable.");
        var sources = new Dictionary<string, object>();
        string sourceEvent = null;
        double sourceEventTime = 0d;
        try
        {
            review.ResetRoom();
            actor.ResetActor();
            actor.ShowControls = false;
            actor.HandTarget = actor.LookTarget = null;
            var roomFloor = GameObject.Find("Interaction room floor");
            if (action == "directions" || runningJumpAction)
            {
                foreach (var collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (collider.enabled && !collider.transform.IsChildOf(actor.Actor) && !collider.transform.IsChildOf(actor.transform))
                    { disabledColliders.Add(collider); collider.enabled = false; }
                directionFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                directionFloor.name = "Temporary directions review floor";
                directionFloor.hideFlags = HideFlags.HideAndDontSave;
                directionFloor.transform.position = new Vector3(-2f, -.1f, -.5f);
                directionFloor.transform.localScale = new Vector3(160f, .2f, 160f);
                var roomRenderer = roomFloor != null ? roomFloor.GetComponent<Renderer>() : null;
                if (roomRenderer != null) directionFloor.GetComponent<Renderer>().sharedMaterial = roomRenderer.sharedMaterial;
                roomFloor = directionFloor;
                Physics.SyncTransforms();
            }
            if (chest)
            {
                review.Select(0);
                if (action == "chest-approach")
                {
                    var pose = new CharacterPose(actor.Actor.position - actor.Actor.forward * .3f, Vector3.up, actor.Actor.forward);
                    actor.Motor.ResetPose(pose);
                    actor.Actor.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward));
                }
            }
            else
            {
                var pose = new CharacterPose(new Vector3(-2f, 0f, -.5f), Vector3.up, Vector3.right);
                actor.Motor.ResetPose(pose);
                actor.ThirdPersonCamera.Reset(pose);
                actor.Actor.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward));
            }
            for (int i = 0; i < 90; i++) actor.Step(dt);
            Transform prop = chest ? review.Targets[0].Hinge.root : null;
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                bool text = renderer.GetComponent<TextMesh>() != null;
                bool relevant = renderer.transform.IsChildOf(actor.Actor) ||
                    roomFloor != null && renderer.transform.IsChildOf(roomFloor.transform) ||
                    prop != null && renderer.transform.IsChildOf(prop);
                if (renderer.enabled && (text || !relevant)) { hidden.Add(renderer); renderer.enabled = false; }
            }
            HumanInteractionComparison.Show();
            float initialGroundHeight = actor.Actor.position.y;
            if (roomFloor != null && action != "directions")
            {
                floor = UnityEngine.Object.Instantiate(roomFloor, roomFloor.transform.position - HumanInteractionComparison.Offset, roomFloor.transform.rotation);
                foreach (var collider in floor.GetComponentsInChildren<Collider>()) collider.enabled = false;
            }
            var runtimeAnimator = actor.Actor.GetComponentInChildren<Animator>();
            var referenceAnimator = HumanInteractionComparison.Current.ReferenceRoot.GetComponentInChildren<Animator>();
            Quaternion cameraFacing = actor.Actor.rotation;
            using var render = new AnimationReviewFrames();
            for (int frame = 0; frame < frames; frame++)
            {
                double inputTime = frame / 60d;
                double time = (frame + 1) / 60d;
                Vector2 move = Vector2.zero;
                ActorButtons buttons = ActorButtons.None;
                int directionSegment = action == "directions" && frame >= 60 && frame < 1020 ? (frame - 60) / 60 : -1;
                if (directionSegment >= 0 && (frame - 60) % 60 < 54)
                {
                    move = directions[directionSegment % 8];
                    if (directionSegment >= 8) buttons |= ActorButtons.Sprint;
                }
                if (action == "walk" && frame >= 30 && frame < 150) move = Vector2.up;
                if (action == "strafe" && frame >= 30 && frame < 150) move = frame < 90 ? Vector2.right : Vector2.left;
                int runEnd = action == "run-jump-stop" || action == "run-jump-resume" || action == "run-jump-recovery-resume" ? 60 :
                    action == "run-jump-late-stop" ? 83 : action == "run-jump-through" ? 210 : 102;
                bool resumeRun = action == "run-jump-resume" && frame >= 80 || action == "run-jump-recovery-resume" && frame >= 92;
                if (runningJumpAction && frame >= 15 && (frame < runEnd || resumeRun && frame < 210))
                { move = Vector2.up; buttons = ActorButtons.Sprint; }
                if (jump && frame == 45) buttons |= ActorButtons.Jump;
                if (chest)
                {
                    if (frame == 30 || frame == (action == "chest-search" ? 750 : 300) && action != "chest-cancel") review.Interact();
                    if (action == "chest" && frame == 420) review.Cancel();
                    if (action == "chest-cancel")
                    {
                        if (frame == 90 || frame == 100) review.Cancel();
                        if (frame == 96) review.Interact();
                    }
                }
                if (action == "stance-interrupt")
                {
                    actor.Crouch = frame >= 30 && frame < 34 || frame >= 38 && frame < 80 || frame >= 240 && frame < 270 && !(frame >= 246 && frame < 252);
                    actor.Crawl = frame >= 120 && frame < 126 || frame >= 130 && frame < 240 || frame >= 246 && frame < 252;
                }
                else if (stance)
                {
                    actor.Crouch = frame >= 30 && frame < 90 || frame >= 240 && frame < 330;
                    if (frame == 120) actor.Crawl = true;
                    if (frame == 240) actor.Crawl = false;
                }
                actor.Step(dt, new ActorIntent(move, Vector2.zero, buttons, (uint)frame));
                HumanInteractionComparison.Current.Sample(dt);
                AnimationClip source = null;
                string nextEvent = "unavailable: no matching stance source sequence";
                string sourceStatus = "unavailable";
                if (!stance)
                {
                    sourceStatus = "matching source segment; diagnostic switches do not assess transitions";
                    source = idleSource;
                    nextEvent = frame < (jump ? 45 : 30) ? "idle lead-in" : "idle recovery";
                    if (action == "walk" && move != Vector2.zero) { source = walkSource; nextEvent = "walk start"; }
                    if (action == "strafe" && move != Vector2.zero)
                    { source = move.x > 0f ? rightSource : leftSource; nextEvent = move.x > 0f ? "right strafe start" : "left strafe start"; }
                    if (action == "directions" && directionSegment >= 0 && move != Vector2.zero)
                    {
                        source = directionalSources[directionSegment];
                        nextEvent = (directionSegment < 8 ? "walk " : "run ") + directionNames[directionSegment % 8] + " start";
                    }
                    if (jump)
                    {
                        if (frame < 45 && runningJumpAction && frame >= 15) { source = runSource; nextEvent = "run lead-in"; }
                        if (inputTime >= .75d - takeoffSeconds) { source = actionSource; nextEvent = "authored push-off aligned to jump input"; }
                        sourceStatus = "native source clock; push-off event aligned; fixed ground height; baked body travel retained";
                    }
                    if (chest && review.Session.Active)
                    {
                        string phaseName = review.Session.Phase.Name;
                        source = phaseName == "Lower to inspect" ? inspectStartSource :
                            phaseName == "Look inside" ? inspectLoopSource :
                            phaseName == "Stand from inspection" ? inspectEndSource : actionSource;
                        nextEvent = source == inspectStartSource ? "authored inspection entry" :
                            source == inspectLoopSource ? "authored inspection loop" :
                            source == inspectEndSource ? "authored inspection exit" :
                            review.Session.Phase.ReverseAnimation ? "forward original reference at reversed closing entry" : "authored opening entry";
                        sourceStatus = source == inspectLoopSource ? "A shows the original standing idle at 1x. B/C use its head and torso variation baked into the bent inspection stance; this is a composite, not the same full-body clip." : review.Session.Phase.ReverseAnimation ?
                            "A plays ORIGINAL FORWARD opening at 1x; B/C closing reverses that source. A is not a time-aligned closing reference." :
                            "matching original semantic clip at 1x on retargeted rig; clock resets on source clip change, not subphase changes";
                    }
                }
                else if (action == "stance-interrupt" && frame >= 120 && frame < 322)
                {
                    source = actor.Crawl ? crawlEnterSource : crawlExitSource;
                    nextEvent = actor.Crawl ? "original prone enter at interrupted input" : "original prone exit at interrupted input";
                    sourceStatus = "Original HumanF at 1x; source switches follow input and do not represent production crossfades.";
                }
                else if (frame >= 120 && frame < 240 + Math.Ceiling(crawlExitSource.length * 60d))
                {
                    source = frame < 240 ? crawlEnterSource : crawlExitSource;
                    nextEvent = frame < 240 ? "original prone enter at input frame 120" : "original full prone exit at input frame 240";
                    sourceStatus = frame < 240 ? "original HumanF enter at 1x; end pose held until exit input" :
                        "original HumanF full exit at 1x; production ExitToCrouch selects only normalized 0..0.8";
                }
                if (source != null)
                {
                    bool sourceChanged = original == null || original.Clip != source;
                    if (sourceChanged)
                    {
                        original?.Dispose();
                        bool usesOriginalRig = femaleSourceRig != null &&
                            (source == walkSource || source == leftSource || source == rightSource ||
                             source == crawlEnterSource || source == crawlExitSource || directionalSources.Contains(source));
                        var nativeIdleRig = chest && source == inspectLoopSource ? AssetDatabase.LoadAssetAtPath<GameObject>(HumanCrateMotionAuthor.SourceRigPath) : null;
                        usesOriginalRig |= nativeIdleRig != null;
                        original = new AuthoredAnimationReference(nativeIdleRig != null ? nativeIdleRig : usesOriginalRig ? femaleSourceRig : actor.CharacterPrefab, source, usesOriginalRig);
                        if (usesOriginalRig)
                        {
                            // Preserve imported model units and proportions. Source material import is disabled.
                            if (sourceRigMaterial == null)
                            {
                                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                                if (shader == null) throw new InvalidOperationException("The original rig preview requires the URP Unlit shader.");
                                sourceRigMaterial = new Material(shader) { name = "Original rig neutral review material", hideFlags = HideFlags.HideAndDontSave };
                                sourceRigMaterial.SetColor("_BaseColor", new Color(.65f, .68f, .72f));
                                
                            }
                            var renderers = original.Root.GetComponentsInChildren<SkinnedMeshRenderer>();
                            if (renderers.Length == 0) throw new InvalidOperationException("The original HumanF rig has no visible skinned mesh.");
                            foreach (var renderer in renderers)
                                renderer.sharedMaterials = Enumerable.Repeat(sourceRigMaterial,
                                    Mathf.Max(1, renderer.sharedMesh != null ? renderer.sharedMesh.subMeshCount : renderer.sharedMaterials.Length)).ToArray();
                        }
                        else
                        {
                            // The wrapper carries production scale; the cloned prefab must not multiply it again.
                            original.Root.transform.GetChild(0).localScale = Vector3.one;
                            original.Root.transform.localScale = actor.Actor.lossyScale;
                        }
                        foreach (var skin in original.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
                            { skin.forceMatrixRecalculationPerRender = true; skin.updateWhenOffscreen = true; }
                        string path = original.ClipAssetPath;
                        sources[path] = new { path, source.name, source.length, source.frameRate, source.isLooping,
                            dependencyHash = AssetDatabase.GetAssetDependencyHash(path).ToString(),
                            importSettings = EditorJsonUtility.ToJson(AssetImporter.GetAtPath(path)),
                            rig = original.RigAssetPath, original.UsesOriginalRig, original.Label,
                            rigDependencyHash = AssetDatabase.GetAssetDependencyHash(original.RigAssetPath).ToString(),
                            rigImportSettings = EditorJsonUtility.ToJson(AssetImporter.GetAtPath(original.RigAssetPath)),
                            wrapperScale = V(original.Root.transform.localScale), modelScale = V(original.Root.transform.GetChild(0).localScale),
                            materials = usesOriginalRig ? "Neutral URP Unlit diagnostic material; original imported model scale retained" : "Production prefab materials and scale" };
                    }
                    if (sourceChanged || !chest && nextEvent != sourceEvent)
                    {
                        sourceEvent = nextEvent;
                        sourceEventTime = jump && source == actionSource ? action == "jump" ? .75d : .75d - takeoffSeconds : inputTime;
                    }
                }
                else if (original != null)
                {
                    original.Dispose();
                    original = null;
                }
                Vector3 originalPosition = actor.Actor.position - HumanInteractionComparison.Offset;
                if (jump) originalPosition.y = initialGroundHeight;
                if (original != null)
                {
                    original.Root.transform.SetPositionAndRotation(originalPosition, actor.Actor.rotation);
                    double elapsed = Math.Max(0d, time - sourceEventTime);
                    bool sourceOneShot = jump && source == actionSource || chest && source != idleSource && source != inspectLoopSource;
                    original.Sample(sourceOneShot ? Math.Min(elapsed, source.length - .00001d) : elapsed);
                }
                Vector3[] Points(Animator animator, Vector3 offset) => new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                    HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.Head }.Select(b => animator.GetBoneTransform(b).position - offset).ToArray();
                var b = Points(referenceAnimator, HumanInteractionComparison.Offset);
                var c = Points(runtimeAnimator, Vector3.zero);
                var mixer = actor.View.Graph.BaseMixer;
                var clips = new List<object>();
                for (int i = 0; i < mixer.GetInputCount(); i++)
                    if (mixer.GetInput(i).IsValid() && mixer.GetInputWeight(i) > .001f)
                    {
                        var playable = (AnimationClipPlayable)mixer.GetInput(i);
                        clips.Add(new { slot = i, clip = playable.GetAnimationClip().name, seconds = playable.GetTime(), weight = mixer.GetInputWeight(i) });
                    }
                object ContactMeasurement(string id, Transform contact, InteractionPoseTarget? requested, float declaredWeight)
                {
                    if (contact == null) return null;
                    bool hasAuthored = actor.View.Pose.TryGetAuthoredInteractionContact(id, out var authored);
                    bool hasFinal = actor.View.Pose.TryGetInteractionContact(id, out var final, out bool reachable);
                    return new {
                        id, requiredByPhase = declaredWeight > 0f, declaredWeight, requestedWeight = requested?.Weight ?? 0f,
                        target = V(contact.position), authoredPalm = hasAuthored ? V(authored) : null,
                        finalPalm = hasFinal ? V(final) : null,
                        authoredToContact = hasAuthored ? (float?)Vector3.Distance(authored, contact.position) : null,
                        finalToContact = hasFinal ? (float?)Vector3.Distance(final, contact.position) : null,
                        requestedPosition = requested.HasValue ? V(requested.Value.Position) : null, reachable };
                }
                object ArmLandmarks(Animator animator)
                {
                    if (animator == null || !animator.isHuman) return null;
                    var hand = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                    var elbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position;
                    var shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
                    var thigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position;
                    var knee = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg).position;
                    var ankle = animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
                    var head = animator.GetBoneTransform(HumanBodyBones.Head).position;
                    float legLength = Vector3.Distance(thigh, knee) + Vector3.Distance(knee, ankle);
                    float armLength = Vector3.Distance(shoulder, elbow) + Vector3.Distance(elbow, hand);
                    return new { armLength, legLength, armToLegLength = armLength / Mathf.Max(.0001f, legLength),
                        wristAboveKnee = Vector3.Dot(hand - knee, actor.Actor.up),
                        wristHeightFraction = Vector3.Dot(hand - ankle, actor.Actor.up) /
                            Mathf.Max(.0001f, Vector3.Dot(head - ankle, actor.Actor.up)) };
                }
                var chestTarget = chest ? review.Targets[0] : null;
                var activeContacts = chestTarget?.ContactSets.FirstOrDefault(c => c.Id == review.Session.Phase?.ContactSet);
                var rightContact = activeContacts != null ? activeContacts.Right : chestTarget?.Contact;
                var leftContact = activeContacts != null ? activeContacts.Left : chestTarget?.LeftContact;
                object Sole(string id)
                {
                    if (!actor.View.Pose.TryGetAuthoredInteractionContact(id, out var authored) ||
                        !actor.View.Pose.TryGetInteractionContact(id, out var final, out _)) return null;
                    return new { authored = V(authored), final = V(final), correction = Vector3.Distance(authored, final) };
                }
                records.Add(new { frame, time, inputTime, root = V(actor.Actor.position), grounded = actor.Motor.Grounded,
                    phase = actor.View.Airborne.Phase.ToString(), performanceId = actor.View.PerformanceId, performancePhase = actor.View.PerformancePhase,
                    performanceTime = actor.View.PerformanceTime, stance = actor.Collision.Stance.ToString(),
                    leftSole = Sole("LadderLeftFoot"), rightSole = Sole("LadderRightFoot"),
                    approaching = review.Approaching, status = review.Status, sourceSeconds = original?.ClipTimeSeconds,
                    inputMove = new[] { move.x, move.y }, inputButtons = buttons.ToString(), directionSegment,
                    sourceElapsed = original?.ElapsedSeconds, sourceClip = original?.ClipAssetPath,
                    sourceRig = original?.RigAssetPath, sourceLabel = original?.Label, sourceUsesOriginalRig = original?.UsesOriginalRig,
                    sourceEvent = nextEvent, sourceEventTime, sourceStatus, sourceRoot = V(originalPosition),
                    interactionPhase = review.Session.Phase?.Name, interactionProgress = review.Session.Progress,
                    rightContact = chestTarget != null ? ContactMeasurement("RightHand", rightContact,
                        actor.RightInteractionTarget, review.Session.Phase?.RightHandWeight ?? 0f) : null,
                    leftContact = chestTarget != null ? ContactMeasurement("LeftHand", leftContact,
                        actor.LeftInteractionTarget, review.Session.Phase?.LeftHandWeight ?? 0f) : null,
                    contactAllowance = chestTarget != null ? (float?)chestTarget.MaxContactCorrection : null,
                    sourceArmLandmarks = ArmLandmarks(original?.Animator), uncorrectedArmLandmarks = ArmLandmarks(referenceAnimator),
                    finalArmLandmarks = ArmLandmarks(runtimeAnimator),
                    b = b.Select(V).ToArray(), c = c.Select(V).ToArray(), correction = b.Zip(c, Vector3.Distance).ToArray(), clips });
                if (frame % 2 == 0)
                {
                    var center = actor.Actor.position + Vector3.up * .95f;
                    var originalCenter = originalPosition + Vector3.up * .95f;
                    if (jump && original != null)
                    {
                        var hips = original.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                        originalCenter.x = hips.x; originalCenter.z = hips.z;
                    }
                    render.Capture(Path.Combine(directory, $"frame-{frame / 2:D4}.png"),
                        new[] { originalCenter, center + HumanInteractionComparison.Offset, center }, cameraFacing);
                }
            }
            File.WriteAllText(Path.Combine(directory, "metadata.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                action, scene = actor.gameObject.scene.path, unity = Application.unityVersion, simulationHz = 60, captureHz = 30,
                frames, seconds = frames / 60d, columns = new[] { stance ? "A: original HumanF prone enter/exit / other stance unavailable" : jump ?
                    "A: source timing / RETARGETED / 1x / fixed ground" : chest ?
                    "A: native forward sources / RETARGETED / closing is not matched" : femaleSourceRig != null ?
                    "A: original KI rig during gait / retargeted idle / 1x" : "A: original source segments / RETARGETED / 1x", "B: production clips and clock; no corrections", "C: production runtime" },
                rows = new[] { "rear three-quarter", "side" }, camera = "Root-following orthographic; fixed facing; half height 1.35 m. B/C world positions remain in metrics.",
                rootMotionConvention = jump ? "A: fixed ground height, camera follows baked body XZ; extracted Animator root delta is not applied. B/C: production root." :
                    "A: production root aligned, baked body curves retained; extracted Animator root delta is not applied. B/C: production root.",
                takeoffSeconds,
                directionRecipe = action == "directions" ? "1s idle; eight walk then eight run directions, each .9s move + .1s stop; 1s idle. Source A runs at 1x; short stops exercise interrupted blends, not settled idle." : null,
                supportGeometry = action == "directions" || runningJumpAction ? "Isolated 160m square flat collider, top Y=0; unrelated scene colliders disabled for capture and restored in finally." : "Existing review scene support geometry",
                disabledColliderCount = disabledColliders.Count,
                armLandmarkConvention = "Right upper-arm to elbow to wrist length / thigh to knee to ankle length. Wrist height uses actor up; fingers excluded. Pose-dependent height fractions are not an anatomical approval threshold.",
                stanceRecipe = action == "stance-interrupt" ? "Crouch30..33/retrigger38..79; crawl120..125/retrigger130..239; crouch240..269; crawlretrigger246..251. Three reversals during active blends; full recovery through8s." : stance ? "Crouch input frames30..89; crawl enter120; crawl exit to crouch240; stand330. A has only original full prone enter/exit; other stance intervals unavailable." : null,
                sourceSwitches = "Immediate diagnostic source switches; A does not validate gameplay transitions.",
                chestSourceConvention = chest ? "Search uses the complete original chest GrabItem clip at 1x, resetting only when the source clip changes. The chest-search recipe records more than two complete search cycles before closing. Closing A plays original FORWARD opening; reversed B/C is an adaptation, not a direct source-pose comparison." : null,
                contactConvention = chest ? "Palm markers in world metres against actual prop markers. RequiredByPhase identifies contact intervals. RequestedWeight is pre-blend intent, not measured solver influence. Low correction with a released target does not prove contact." : null,
                timestampConvention = "Inputs at inputTime; poses and measurements after one step at time.",
                sources, provenance = new { capturedUtc = DateTime.UtcNow.ToString("O"),
                    sceneDependencyHash = AssetDatabase.GetAssetDependencyHash(actor.gameObject.scene.path).ToString(),
                    captureSourceHash = AssetDatabase.GetAssetDependencyHash("Assets/Editor/HumanoidQualityReviewCapture.cs").ToString(),
                    runtimeAssembly = AssemblyHash(typeof(HumanoidAnimationPrototype)), editorAssembly = AssemblyHash(typeof(HumanoidQualityReviewCapture)) },
                actor.WalkSpeed, actor.RunSpeed, actor.JumpHeight, scale = V(actor.Actor.lossyScale),
                records
            }, Newtonsoft.Json.Formatting.Indented));
        }
        finally
        {
            original?.Dispose();
            if (sourceRigMaterial != null) UnityEngine.Object.DestroyImmediate(sourceRigMaterial);
            HumanInteractionComparison.Hide();
            if (floor != null) UnityEngine.Object.DestroyImmediate(floor);
            if (directionFloor != null) UnityEngine.Object.DestroyImmediate(directionFloor);
            foreach (var collider in disabledColliders) if (collider != null) collider.enabled = true;
            if (disabledColliders.Count > 0) Physics.SyncTransforms();
            actor.ShowControls = controls;
            foreach (var renderer in hidden) if (renderer != null) renderer.enabled = true;
        }
    }

    static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
    static string AssemblyHash(Type type)
    {
        using var stream = File.OpenRead(type.Assembly.Location);
        using var hash = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
    }
    static AnimationClip LoadClip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
        .First(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
}

