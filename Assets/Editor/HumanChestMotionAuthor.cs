using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Fits the review chest to its owned low-chest motion and preserves native phase timing.</summary>
public static class HumanChestMotionAuthor
{
    const string Clips = "Assets/Art/Interactions/Animations/";
    const string Definitions = "Assets/Art/Interactions/Definitions/";
    const float InspectionPose = .26f;
    const float ContactStart = .57f;
    const float ContactEnd = .64f;
    public static float LastMaximumAuthoredContactError { get; private set; }
    public static string LastContactFitSamples { get; private set; }

    public static string InspectSearchSources()
    {
        var review = UnityEngine.Object.FindAnyObjectByType<HumanInteractionReview>();
        var target = review.Targets.First(t => t.Use != null && t.Use.Action == "Chest");
        var root = target.Hinge.root;
        var output = new System.Text.StringBuilder("clip,normalized,leftPalmInChest,rightPalmInChest\n");
        foreach (string name in new[] { "Loot_Generic_Rummage_Crouching_Loop.fbx", "Loot_TreasureChest_GrabItem.fbx" })
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(Clips + name).OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            using var reference = new AuthoredAnimationReference(review.Actor.CharacterPrefab, clip, false);
            reference.Root.transform.GetChild(0).localScale = Vector3.one;
            reference.Root.transform.localScale = review.Actor.transform.lossyScale;
            reference.Root.transform.SetPositionAndRotation(target.Approach.position - root.up * ActorCollision.Skin, target.Approach.rotation);
            var rig = reference.Root.AddComponent<ProceduralRigDefinition>();
            HumanoidRigBinding.Bind(reference.Animator, rig);
            var left = rig.Interactions.First(hand => hand.Id == "LeftHand");
            var right = rig.Interactions.First(hand => hand.Id == "RightHand");
            for (int i = 0; i <= 40; i++)
            {
                reference.Sample(clip.length * i / 40f);
                Vector3 Palm(InteractionLimbDefinition hand) => root.InverseTransformPoint(hand.Bones[^1].TransformPoint(hand.ContactPosition));
                output.AppendLine(FormattableString.Invariant($"{name},{i / 40f:F3},{Palm(left).ToString("F4")},{Palm(right).ToString("F4")}"));
            }
        }
        return output.ToString();
    }

    public static ActorInteractionDefinition.Phase[] CreatePhases(string action)
    {
        var close = new[] {
            Phase("Reach for lid", "Loot_TreasureChest_Open_Only.fbx", ContactEnd, 1f, reverse: true),
            Phase("Close lid", "Loot_TreasureChest_Open_Only.fbx", ContactStart, ContactEnd, right: 1f, left: 1f, marker: "Close", reverse: true),
            Phase("Stand back", "Loot_TreasureChest_Open_Only.fbx", 0f, ContactStart, reverse: true) };
        if (action == "CloseChest") return close;
        // Keep the bent inspection stance with authored breathing and head movement.
        var look = Phase("Look inside", "Chest Inspection Idle.anim", 0f, 1f, right: 1f, left: 1f, wait: true);
        look.ContactSet = "InspectionRim";
        var inspect = new[] {
            Phase("Lower to inspect", "Loot_TreasureChest_GrabItem.fbx", 0f, InspectionPose),
            look,
            Phase("Stand from inspection", "Loot_TreasureChest_GrabItem.fbx", 0f, InspectionPose, reverse: true) };
        if (action == "Chest") return new[] {
            Phase("Bend and reach", "Loot_TreasureChest_Open_Only.fbx", 0f, ContactStart),
            Phase("Grab and open", "Loot_TreasureChest_Open_Only.fbx", ContactStart, ContactEnd, right: 1f, left: 1f, marker: "Open"),
            Phase("Withdraw from lid", "Loot_TreasureChest_Open_Only.fbx", ContactEnd, 1f)
        }.Concat(inspect).Concat(close).ToArray();
        if (action == "CollectChest") return new[] {
            Phase("Collect from chest", "Loot_TreasureChest_GrabItem.fbx", 0f, .65f, marker: "Collect", markerProgress: .999f),
            Phase("Recover after collection", "Loot_TreasureChest_GrabItem.fbx", .65f, 1f)
        }.Concat(inspect).Concat(close).ToArray();
        throw new ArgumentException("Unknown chest action.", nameof(action));
    }

    public static AnimationClip BakeInspectionIdle()
    {
        var stance = AssetDatabase.LoadAllAssetsAtPath(Clips + "Loot_TreasureChest_GrabItem.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
        var idle = AssetDatabase.LoadAllAssetsAtPath(Clips + "Inspection Idle Source.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
        string path = Clips + "Chest Inspection Idle.anim";
        var result = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (result == null) { result = new AnimationClip(); AssetDatabase.CreateAsset(result, path); }
        result.ClearCurves(); result.frameRate = idle.frameRate;
        var idleBindings = AnimationUtility.GetCurveBindings(idle);
        foreach (var binding in AnimationUtility.GetCurveBindings(stance))
        {
            float baseline = AnimationUtility.GetEditorCurve(stance, binding).Evaluate(stance.length * InspectionPose);
            string muscle = binding.propertyName;
            if (muscle == "Left Hand Down-Up" || muscle == "Right Hand Down-Up") baseline += .35f;
            bool head = muscle.StartsWith("Head") || muscle.StartsWith("Neck");
            bool torso = muscle.StartsWith("Chest") || muscle.StartsWith("UpperChest") || muscle.StartsWith("Spine");
            bool legs = muscle.Contains("Upper Leg") || muscle.Contains("Lower Leg");
            bool bodyPosition = muscle.StartsWith("RootT.");
            float amount = head ? .8f : torso ? .7f : legs ? .12f : bodyPosition ? .3f : 0f;
            var variation = amount > 0f ? idleBindings.FirstOrDefault(b => b.propertyName == muscle && b.type == binding.type) : default;
            var curve = variation.type != null ? AnimationUtility.GetEditorCurve(idle, variation) : null;
            int count = Mathf.CeilToInt(idle.length * result.frameRate);
            var keys = new Keyframe[count + 1];
            for (int i = 0; i <= count; i++)
            {
                float t = idle.length * i / count;
                // The authored break returns to rest. Remove endpoint drift for an editable seamless loop.
                float delta = curve == null ? 0f : curve.Evaluate(t) - Mathf.Lerp(curve.Evaluate(0f), curve.Evaluate(idle.length), (float)i / count);
                keys[i] = new Keyframe(t, baseline + delta * amount);
            }
            AnimationUtility.SetEditorCurve(result, binding, new AnimationCurve(keys));
        }
        var settings = AnimationUtility.GetAnimationClipSettings(stance);
        settings.startTime = 0f; settings.stopTime = idle.length; settings.loopTime = true;
        settings.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(result, settings);
        EditorUtility.SetDirty(result); AssetDatabase.SaveAssetIfDirty(result);
        return result;
    }

    static ActorInteractionDefinition.Phase Phase(string name, string file, float start, float end,
        float right = 0f, float left = 0f, string marker = "", bool wait = false, float markerProgress = 0f, bool reverse = false)
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(Clips + file).OfType<AnimationClip>()
            .First(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        return new ActorInteractionDefinition.Phase {
            Animation = new ActorAnimationPerformanceLibrary.Phase { Name = name, Clip = clip,
                StartNormalized = start, EndNormalized = end, Loop = wait, BlendSeconds = marker == "Open" || marker == "Close" ? .05f : .15f },
            Seconds = clip.length * (end - start), RightHandWeight = right, LeftHandWeight = left,
            Marker = marker, MarkerProgress = markerProgress, WaitForInput = wait, ReverseAnimation = reverse };
    }

    [MenuItem("Tools/Actors/Human/Apply Authored Chest Motion")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring the chest.");
        var review = UnityEngine.Object.FindAnyObjectByType<HumanInteractionReview>();
        if (review == null) throw new InvalidOperationException("Open the Human interaction review.");
        BakeInspectionIdle();
        foreach (string action in new[] { "Chest", "CloseChest", "CollectChest" })
        {
            var definition = AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(Definitions + action + ".asset");
            if (definition == null) throw new InvalidOperationException("Configure interaction definitions first.");
            var phases = CreatePhases(action);
            Undo.RecordObject(definition, "Apply authored chest motion");
            definition.Phases = phases;
            definition.Snapshot();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssetIfDirty(definition);
        }
        FitReviewProp(review);
        EditorSceneManager.MarkSceneDirty(review.gameObject.scene);
        EditorSceneManager.SaveScene(review.gameObject.scene);
    }

    public static void FitReviewProp(HumanInteractionReview review)
    {
        var target = review.Targets.First(t => t.Use != null && t.Use.Action == "Chest");
        var root = target.Hinge.root;
        Undo.RecordObject(root, "Fit chest to authored reach");
        // Keep the prop's original proportions and put contacts on its visible front lid edge.
        root.localScale = Vector3.one;
        var mesh = target.Hinge.GetComponent<MeshFilter>();
        // Keep the editable mesh surface available after runtime upload or static batching.
        target.LidVertices = mesh.sharedMesh.vertices;
        target.LidTriangles = mesh.sharedMesh.triangles;
        var vertices = mesh.sharedMesh.vertices.Select(v => root.InverseTransformPoint(target.Hinge.TransformPoint(v))).ToArray();
        float front = vertices.Min(v => v.z);
        float edgeHeight = vertices.Where(v => v.z < front + .04f).Max(v => v.y);
        var rim = (target.ContactSets ?? Array.Empty<HumanInteractionReview.HandContacts>()).FirstOrDefault(c => c.Id == "InspectionRim");
        if (rim == null)
        {
            rim = new HumanInteractionReview.HandContacts { Id = "InspectionRim" };
            target.ContactSets = (target.ContactSets ?? Array.Empty<HumanInteractionReview.HandContacts>()).Append(rim).ToArray();
        }
        var body = root.GetComponentsInChildren<MeshFilter>()
            .Where(m => !m.transform.IsChildOf(target.Hinge))
            .OrderByDescending(m => m.sharedMesh.bounds.size.sqrMagnitude).First();
        var bodySurface = new HumanInteractionReview.Target {
            Hinge = body.transform, LidVertices = body.sharedMesh.vertices, LidTriangles = body.sharedMesh.triangles };
        Transform RimAnchor(string name, float x)
        {
            var anchor = root.Find(name);
            if (anchor == null) { anchor = new GameObject(name).transform; anchor.SetParent(root, false); }
            var origin = root.TransformPoint(new Vector3(x, edgeHeight + .1f, front + .05f));
            if (!HumanInteractionReview.TryProjectContactSurface(bodySurface, origin, -root.up, out var point, out var normal))
                throw new InvalidOperationException("The inspection palm must project onto the chest body's rim.");
            anchor.SetPositionAndRotation(point + normal * .006f, Quaternion.LookRotation(-normal, root.forward));
            return anchor;
        }
        rim.Right = RimAnchor("Inspection right rim", .16f);
        rim.Left = RimAnchor("Inspection left rim", -.2f);
        rim.MatchRotation = true;
        target.Contact.position = root.TransformPoint(new Vector3(.16f, edgeHeight, front));
        target.LeftContact.position = root.TransformPoint(new Vector3(-.16f, edgeHeight, front));
        target.OpenEuler = new Vector3(-85f, 0f, 0f);
        target.MatchHingeToPhase = true;
        target.MatchHingeToAuthoredHands = true;
        target.OpenHandOffset = Vector3.zero;
        target.UseSourceHandRotation = true;
        target.MatchContactRotation = false;
        target.RightElbowDirection = target.LeftElbowDirection = Vector3.zero;
        Vector3 position = (target.Contact.position + target.LeftContact.position) * .5f - root.forward * .58f;
        position.y = root.position.y;
        var source = AssetDatabase.LoadAllAssetsAtPath(Clips + "Loot_TreasureChest_Open_Only.fbx").OfType<AnimationClip>()
            .First(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        using (var reference = new AuthoredAnimationReference(review.Actor.CharacterPrefab, source, false))
        {
            reference.Root.transform.GetChild(0).localScale = Vector3.one;
            reference.Root.transform.localScale = review.Actor.transform.lossyScale;
            reference.Root.transform.SetPositionAndRotation(position - root.up * ActorCollision.Skin, root.rotation);
            var rig = reference.Root.AddComponent<ProceduralRigDefinition>();
            HumanoidRigBinding.Bind(reference.Animator, rig);
            var rightHand = rig.Interactions.First(hand => hand.Id == "RightHand");
            var leftHand = rig.Interactions.First(hand => hand.Id == "LeftHand");
            Vector3 Palm(InteractionLimbDefinition hand) => hand.Bones[^1].TransformPoint(hand.ContactPosition);
            reference.Sample(source.length * ContactStart);
            var adjustment = Vector3.ProjectOnPlane((target.Contact.position + target.LeftContact.position - Palm(rightHand) - Palm(leftHand)) * .5f, root.up);
            position += adjustment;
            reference.Root.transform.position += adjustment;
            // Fit the natural grip width on the existing visible lid edge; retain the prop dimensions.
            float rightX = root.InverseTransformPoint(Palm(rightHand)).x;
            float leftX = root.InverseTransformPoint(Palm(leftHand)).x;
            float minimumX = vertices.Min(v => v.x), maximumX = vertices.Max(v => v.x);
            target.Contact.position = root.TransformPoint(new Vector3(Mathf.Clamp(rightX, minimumX, maximumX), edgeHeight, front));
            target.LeftContact.position = root.TransformPoint(new Vector3(Mathf.Clamp(leftX, minimumX, maximumX), edgeHeight, front));
            var closed = target.Hinge.localRotation;
            var closedWorld = target.Hinge.rotation;
            var lever = Quaternion.Inverse(closedWorld) * ((target.Contact.position + target.LeftContact.position) * .5f - target.Hinge.position);
            target.AuthoredHingeLever = lever;
            var rightClosed = target.Contact.localPosition;
            var leftClosed = target.LeftContact.localPosition;
            reference.Sample(source.length * ContactEnd);
            var endLever = Quaternion.Inverse(closedWorld) * ((Palm(rightHand) + Palm(leftHand)) * .5f - target.Hinge.position);
            var fitted = HumanInteractionReview.FitAuthoredHinge(closed, new Vector3(-85f, 0f, 0f), lever, endLever);
            target.OpenEuler = new Vector3(-Quaternion.Angle(closed, fitted), 0f, 0f);
            LastMaximumAuthoredContactError = 0f;
            var diagnostics = new System.Text.StringBuilder("normalized,rightError,leftError,rightPalm,leftPalm,rightTarget,leftTarget,hingeAngle\n");
            try
            {
                for (int sample = 0; sample <= 16; sample++)
                {
                    reference.Sample(source.length * Mathf.Lerp(ContactStart, ContactEnd, sample / 16f));
                    var authoredLever = Quaternion.Inverse(closedWorld) * ((Palm(rightHand) + Palm(leftHand)) * .5f - target.Hinge.position);
                    target.Hinge.localRotation = HumanInteractionReview.FitAuthoredHinge(closed, target.OpenEuler, lever, authoredLever);
                    target.Contact.position = HumanInteractionReview.ClosestLidPoint(target, Palm(rightHand));
                    target.LeftContact.position = HumanInteractionReview.ClosestLidPoint(target, Palm(leftHand));
                    LastMaximumAuthoredContactError = Mathf.Max(LastMaximumAuthoredContactError,
                        Vector3.Distance(Palm(rightHand), target.Contact.position), Vector3.Distance(Palm(leftHand), target.LeftContact.position));
                    diagnostics.AppendLine(FormattableString.Invariant($"{Mathf.Lerp(ContactStart, ContactEnd, sample / 16f):F5},{Vector3.Distance(Palm(rightHand), target.Contact.position):F5},{Vector3.Distance(Palm(leftHand), target.LeftContact.position):F5},{Palm(rightHand).ToString("F5")},{Palm(leftHand).ToString("F5")},{target.Contact.position.ToString("F5")},{target.LeftContact.position.ToString("F5")},{Quaternion.Angle(closed, target.Hinge.localRotation):F4}"));
                }
            }
            finally
            {
                target.Hinge.localRotation = closed;
                target.Contact.localPosition = rightClosed;
                target.LeftContact.localPosition = leftClosed;
                LastContactFitSamples = diagnostics.ToString();
            }
        }
        Undo.RecordObject(target.Approach, "Fit chest approach");
        target.Approach.SetPositionAndRotation(position, root.rotation);
        int index = Array.IndexOf(review.Targets, target);
        Undo.RecordObject(review.Actor.Stations[index], "Fit chest station");
        review.Actor.Stations[index].SetPositionAndRotation(position, root.rotation);
        EditorUtility.SetDirty(review);
    }
}
