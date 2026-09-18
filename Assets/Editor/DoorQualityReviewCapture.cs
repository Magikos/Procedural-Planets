using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Off-axis door approach, authored reach, contact, and recovery at a fixed input cadence.</summary>
public static class DoorQualityReviewCapture
{
    public static void Capture(string directory, float lateralOffset = -.5f, float facingOffset = 65f, string scenario = "complete")
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPaused) throw new InvalidOperationException("Enter and pause the Human review first.");
        if (scenario != "complete" && scenario != "cancel" && scenario != "contact-cancel") throw new ArgumentException("Choose complete, cancel, or contact-cancel.");
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Choose an empty evidence directory.");
        var review = UnityEngine.Object.FindFirstObjectByType<HumanInteractionReview>();
        var actor = review != null ? review.Actor : null;
        int targetIndex = review != null ? Array.FindIndex(review.Targets, t => t.OpenAwayFromActor && t.Use != null) : -1;
        if (actor == null || actor.View == null || targetIndex < 0) throw new InvalidOperationException("An initialized actor and authored door are required.");
        Directory.CreateDirectory(directory);
        var target = review.Targets[targetIndex];
        var records = new List<object>();
        var hidden = new List<Renderer>();
        AuthoredAnimationReference original = null;
        GameObject sourceDoor = null;
        bool controls = actor.ShowControls;
        const float dt = 1f / 60f;
        float sourceTime = 0f;
        bool began = false, cancelled = false;
        Action<string> countMarker = null;
        try
        {
            review.ResetRoom(); actor.ResetActor(); actor.ShowControls = false;
            Vector3 forward = Vector3.ProjectOnPlane(target.Contact.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 start = target.Contact.position - forward * .7f + right * lateralOffset;
            start.y = actor.Actor.position.y;
            var pose = new CharacterPose(start, Vector3.up, Quaternion.AngleAxis(facingOffset, Vector3.up) * forward);
            actor.Motor.ResetPose(pose); actor.ThirdPersonCamera.Reset(pose);
            actor.Actor.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward));
            for (int i = 0; i < 60; i++) actor.Step(dt, default(ActorIntent));
            review.RefreshTarget();
            if (review.Selected != targetIndex) throw new InvalidOperationException("The off-axis start did not select the door.");
            var source = target.Use.Phases[0].Animation.Clip;
            original = new AuthoredAnimationReference(actor.CharacterPrefab, source, false);
            original.Root.transform.GetChild(0).localScale = Vector3.one;
            original.Root.transform.localScale = actor.Actor.lossyScale;
            var doorRoot = target.Hinge.root;
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (renderer.enabled && !renderer.transform.IsChildOf(actor.Actor) && !renderer.transform.IsChildOf(doorRoot) &&
                    !renderer.transform.IsChildOf(original.Root.transform))
                { hidden.Add(renderer); renderer.enabled = false; }
            sourceDoor = UnityEngine.Object.Instantiate(doorRoot.gameObject, doorRoot.position - HumanInteractionComparison.Offset, doorRoot.rotation);
            foreach (var c in sourceDoor.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var b in sourceDoor.GetComponentsInChildren<MonoBehaviour>()) b.enabled = false;
            HumanInteractionComparison.Show();
            using var render = new AnimationReviewFrames(360);
            var wrist = actor.Actor.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
            using var surfaceMeasurement = new DoorSurfaceMeasurement(actor.Actor, wrist);
            Quaternion initialHinge = target.Hinge.rotation;
            int openMarkers = 0;
            countMarker = marker => { if (marker == "Open") openMarkers++; };
            review.Session.Marker += countMarker;
            for (int frame = 0; frame < 600; frame++)
            {
                if (frame == 30) review.Interact();
                began |= review.Session.Active;
                if (scenario == "cancel" && !cancelled && (review.Approaching || review.Session.Active) && frame >= 42)
                { review.Cancel(); cancelled = true; }
                if (scenario == "contact-cancel" && !cancelled && review.Session.Active &&
                    review.Session.Phase.Marker == "Open" && review.Session.Progress >= .3f)
                { review.Cancel(); cancelled = true; }
                if (scenario == "complete" && frame == 420) review.Cancel();
                actor.Step(dt, default(ActorIntent));
                if (began) sourceTime += dt;
                original.Root.transform.SetPositionAndRotation(actor.Actor.position - HumanInteractionComparison.Offset, actor.Actor.rotation);
                original.Sample(sourceTime);
                HumanInteractionComparison.Current.Sample(dt);
                var mixer = actor.View.Graph.BaseMixer;
                var clips = new List<object>();
                for (int i = 0; i < mixer.GetInputCount(); i++)
                    if (mixer.GetInput(i).IsValid() && mixer.GetInputWeight(i) > .001f)
                    {
                        var clip = (AnimationClipPlayable)mixer.GetInput(i);
                        clips.Add(new { name = clip.GetAnimationClip().name, time = clip.GetTime(), weight = mixer.GetInputWeight(i) });
                    }
                bool hasAuthored = actor.View.Pose.TryGetAuthoredInteractionContact("RightHand", out var authored);
                bool hasFinal = actor.View.Pose.TryGetInteractionContact("RightHand", out var contact, out bool reachable);
                Vector3 skin = default, panel = default, normal = default;
                bool hasSurface = hasFinal && surfaceMeasurement.Sample(target, contact, actor.Actor.forward, out skin, out panel, out normal);
                records.Add(new { frame, seconds = (frame + 1) / 60d, review.Approaching, review.Status,
                    phase = actor.View.PerformancePhase, phaseProgress = review.Session.Active ? review.Session.Progress : 0f,
                    contactRequired = review.Session.Active && (review.Session.Phase.Marker == "Open" || review.Session.Phase.Marker == "Close") , root = V(actor.Actor.position), facing = V(actor.Actor.forward),
                    hasAuthored, hasFinal, hasSurface, hasPanel = hasFinal && surfaceMeasurement.HasPanel, handMeshes = surfaceMeasurement.HandMeshCount, hand = hasFinal ? V(contact) : null, target = V(target.Contact.position),
                    correction = hasAuthored && hasFinal ? (float?)Vector3.Distance(authored, contact) : null,
                    skin = hasSurface ? V(skin) : null, panel = hasSurface ? V(panel) : null,
                    skinNormal = hasSurface ? V(surfaceMeasurement.LastSkinNormal) : null, panelNormal = hasSurface ? V(normal) : null,
                    normalAlignment = hasSurface ? (float?)Vector3.Dot(surfaceMeasurement.LastSkinNormal, -normal) : null,
                    signedSkinGap = hasSurface ? (float?)Vector3.Dot(skin - panel, normal) : null,
                    panelTangent = hasSurface ? V(Vector3.ProjectOnPlane(skin - target.Contact.position, normal)) : null,
                    hingeAngle = Quaternion.Angle(initialHinge, target.Hinge.rotation), openMarkers,
                    contactError = hasFinal ? (float?)Vector3.Distance(contact, target.Contact.position) : null, reachable, clips });
                if (frame % 2 == 0)
                {
                    var center = actor.Actor.position + Vector3.up;
                    render.Capture(Path.Combine(directory, $"frame-{frame / 2:D4}.png"),
                        new[] { center - HumanInteractionComparison.Offset, center + HumanInteractionComparison.Offset, center }, Quaternion.LookRotation(forward), 1.65f);
                }
            }
            File.WriteAllText(Path.Combine(directory, "metadata.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                lateralOffset, facingOffset, scenario, began, cancelled, simulationHz = 60, captureHz = 30,
                source = original.ClipAssetPath, runtimeAssembly = typeof(HumanInteractionReview).Module.ModuleVersionId.ToString(),
                columns = new[] { "A: " + Path.GetFileNameWithoutExtension(original.ClipAssetPath) + ", RETARGETED, 1x; static source door", "B: production graph and prop timing without pose corrections", "C: production runtime" },
                limits = "A follows production root trajectory and holds final source pose after its clip. Source prop is static; compare original limb arcs, not prop synchronization. ContactRequired spans the complete palm-push phase through source .44, including the .404 marker; later motion is release. SignedSkinGap measures baked hand triangles against the facing panel; missing surfaces remain null. Default completes opening; cancel interrupts at0.7s; contact-cancel interrupts the grip before its marker.", records
            }, Newtonsoft.Json.Formatting.Indented));
        }
        finally
        {
            if (countMarker != null) review.Session.Marker -= countMarker;
            original?.Dispose(); HumanInteractionComparison.Hide();
            if (sourceDoor != null) UnityEngine.Object.DestroyImmediate(sourceDoor);
            foreach (var renderer in hidden) if (renderer != null) renderer.enabled = true;
            review.ResetRoom(); actor.ResetActor(); actor.ShowControls = controls;
        }
    }
    public static string MeasureSourceContacts(string clipFile = "Activate_Wall_KeyTurn_DoorKnob.FBX")
    {
        var review = UnityEngine.Object.FindFirstObjectByType<HumanInteractionReview>();
        var actor = review.Actor;
        var target = review.Targets.First(t => t.OpenAwayFromActor && t.Use != null);
        var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Interactions/Animations/" + clipFile)
            .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
        using var original = new AuthoredAnimationReference(actor.CharacterPrefab, clip, false);
        original.Root.transform.GetChild(0).localScale = Vector3.one;
        Vector3 actorScale = actor.Actor != null ? actor.Actor.lossyScale : actor.CharacterPrefab.transform.lossyScale;
        original.Root.transform.localScale = actorScale;
        var rig = original.Root.GetComponentInChildren<ProceduralRigDefinition>();
        if (rig == null) rig = original.Root.AddComponent<ProceduralRigDefinition>();
        HumanoidRigBinding.Bind(original.Animator, rig);
        var points = new List<object>();
        for (int i = 0; i <= 50; i++)
        {
            float phase = .02f * i;
            original.Sample(phase * clip.length);
            Vector3 Contact(string id)
            {
                var limb = rig.Interactions.First(x => x.Id == id);
                return original.Root.transform.InverseTransformPoint(limb.Bones[^1].TransformPoint(limb.ContactPosition));
            }
            points.Add(new { phase, right = V(Contact("RightHand")), left = V(Contact("LeftHand")) });
        }
        return Newtonsoft.Json.JsonConvert.SerializeObject(new { actorScale = V(actorScale),
            handleHeight = Vector3.Dot(target.Contact.position - (actor.Actor != null ? actor.Actor.position : actor.transform.position), actor.transform.up), points }, Newtonsoft.Json.Formatting.Indented);
    }

    public static string MeasureSourcePalmSkin()
    {
        var review = UnityEngine.Object.FindFirstObjectByType<HumanInteractionReview>();
        var actor = review.Actor;
        var target = review.Targets.First(t => t.OpenAwayFromActor && t.Use != null);
        using var original = new AuthoredAnimationReference(actor.CharacterPrefab, target.Use.Phases[0].Animation.Clip, false);
        original.Root.transform.GetChild(0).localScale = Vector3.one;
        var rig = original.Root.GetComponentInChildren<ProceduralRigDefinition>() ?? original.Root.AddComponent<ProceduralRigDefinition>();
        HumanoidRigBinding.Bind(original.Animator, rig);
        var hand = rig.Interactions.First(x => x.Id == "RightHand");
        var wrist = hand.Bones[^1];
        var samples = new List<object>();
        foreach (float phase in new[] { .32f, .36f, .40f, .44f })
        {
            original.Sample(phase * target.Use.Phases[0].Animation.Clip.length);
            Vector3 palm = wrist.TransformPoint(hand.ContactPosition);
            using var measurement = new DoorSurfaceMeasurement(original.Root.transform, wrist);
            bool hasSurface = measurement.Sample(target, palm, Vector3.forward, out var skin, out _, out _);
            samples.Add(new { phase, palm = V(original.Root.transform.InverseTransformPoint(palm)), hasSurface, hasPanel = measurement.HasPanel, handMeshes = measurement.HandMeshCount, skin = hasSurface ? V(original.Root.transform.InverseTransformPoint(skin)) : null });
        }
        return Newtonsoft.Json.JsonConvert.SerializeObject(new { samples }, Newtonsoft.Json.Formatting.Indented);
    }
    public static string MeasureDoorSurface()
    {
        var review = UnityEngine.Object.FindFirstObjectByType<HumanInteractionReview>();
        var target = review.Targets.First(t => t.OpenAwayFromActor && t.Use != null);
        var rows = new List<object>();
        foreach (var filter in target.Hinge.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = filter.sharedMesh;
            if (mesh == null) continue;
            if (!mesh.isReadable) { rows.Add(new { mesh = filter.name, readable = false }); continue; }
            var surface = new HumanInteractionReview.Target { Hinge = filter.transform, Contact = target.Contact };
            Vector3 nearest = HumanInteractionReview.ClosestLidPoint(surface, target.Contact.position);
            rows.Add(new { mesh = filter.name, readable = true, nearest = V(nearest),
                anchorGap = Vector3.Distance(nearest, target.Contact.position),
                boundsCenter = V(filter.transform.TransformPoint(mesh.bounds.center)), boundsSize = V(mesh.bounds.size) });
        }
        return Newtonsoft.Json.JsonConvert.SerializeObject(new { anchor = V(target.Contact.position), rows }, Newtonsoft.Json.Formatting.Indented);
    }
    static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
}












