using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Reviews the public pickup sequence, carrying turns, and placement without injecting held state.</summary>
public static class CrateQualityReviewCapture
{
    public static void Capture(string directory)
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPaused) throw new InvalidOperationException("Pause the initialized review first.");
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Choose an empty capture directory.");
        Directory.CreateDirectory(directory);
        var review = UnityEngine.Object.FindAnyObjectByType<HumanInteractionReview>();
        var actor = review.Actor;
        var records = new List<object>();
        const float dt = 1f / 60f;
        review.ResetRoom(); actor.ResetActor(); review.Select(7);
        bool controls = actor.ShowControls; actor.ShowControls = false;
        var target = review.Targets[7];
        var clip = target.Use.Phases[0].Animation.Clip;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(HumanCrateMotionAuthor.SourceRigPath);
        using var original = new AuthoredAnimationReference(model, clip, true);
        using var render = new AnimationReviewFrames(360);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = new Color(.65f, .68f, .7f);
        foreach (var renderer in original.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
            renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
        var fixture = new GameObject("Temporary crate review floor");
        Vector3 origin = new Vector3(1000f, 0f, 1000f);
        foreach (float sign in new[] { -1f, 0f, 1f })
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(fixture.transform);
            floor.transform.position = origin + HumanInteractionComparison.Offset * sign - Vector3.up * .5f;
            floor.transform.localScale = new Vector3(16f, 1f, 16f);
            floor.GetComponent<Collider>().enabled = sign == 0f;
        }
        target.PickupRoot.position = origin + Vector3.forward * 1f;
        var pose = new CharacterPose(origin + Vector3.up * .025f, Vector3.up, Vector3.forward);
        actor.Motor.ResetPose(pose); actor.ThirdPersonCamera.Reset(pose);
        actor.Actor.SetPositionAndRotation(pose.Position, Quaternion.identity);
        Physics.SyncTransforms();
        Vector3 sourcePosition = actor.Actor.position;
        bool begun = false; float sourceTime = 0f;
        try
        {
            for (int i = 0; i < 60; i++) actor.Step(dt);
            HumanInteractionComparison.Show();
            for (int frame = 0; frame < 720; frame++)
            {
                if (frame == 30 || frame == 450) review.Interact();
                Vector2 move = frame >= 180 && frame < 300 ? Vector2.up : Vector2.zero;
                Vector2 look = frame >= 220 && frame < 250 ? new Vector2(2f, 0f) : frame >= 270 && frame < 300 ? new Vector2(-2f, 0f) : Vector2.zero;
                actor.Step(dt, new ActorIntent(move, look, ActorButtons.LookHold, (uint)frame));
                if (!begun && review.Session.Active && review.Session.Phase.Name == "Bend and grip")
                { begun = true; sourcePosition = actor.Actor.position; }
                if (begun) sourceTime += dt;
                original.Root.transform.SetPositionAndRotation(sourcePosition - HumanInteractionComparison.Offset, Quaternion.identity);
                original.Sample(sourceTime);
                HumanInteractionComparison.Current.Sample(dt);
                actor.View.Pose.TryGetInteractionContact("RightHand", out var right, out _);
                actor.View.Pose.TryGetInteractionContact("LeftHand", out var left, out _);
                records.Add(new { frame, time = frame / 60f, review.Status, phase = review.Session.Phase?.Name,
                    actor = actor.Actor.position.ToString("F4"), box = target.PickupRoot.position.ToString("F4"),
                    rightError = Vector3.Distance(right, target.Contact.position), leftError = Vector3.Distance(left, target.LeftContact.position) });
                if (frame % 2 == 0)
                {
                    var center = actor.Actor.position + Vector3.up;
                    render.Capture(Path.Combine(directory, $"frame-{frame / 2:D4}.png"),
                        new[] { sourcePosition + Vector3.up - HumanInteractionComparison.Offset, center + HumanInteractionComparison.Offset, center }, Quaternion.identity);
                }
            }
            File.WriteAllText(Path.Combine(directory, "metadata.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                action = "crate-complete", frames = 720, captureHz = 30, simulationHz = 60,
                columns = new[] { original.Label, "B: production graph without correction", "C: production runtime" },
                rows = new[] { "rear", "side" },
                sourceLimits = "A shows original pickup at 1x on its original rig, then holds; later carry and placement belong to B/C. E requests pickup and placement. No injected acquisition.",
                original.ClipAssetPath, original.RigAssetPath, records
            }, Newtonsoft.Json.Formatting.Indented));
        }
        finally { UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(fixture); HumanInteractionComparison.Hide(); actor.ShowControls = controls; review.ResetRoom(); actor.ResetActor(); }
    }
}
