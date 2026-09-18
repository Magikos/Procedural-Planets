using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Isolated motor-driven traversal comparisons using the regression fixture dimensions.</summary>
public static class TraversalQualityReviewCapture
{
    public static void Capture(string directory, string action = "vault", string scenario = "complete")
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPaused)
            throw new InvalidOperationException("Enter and pause the initialized Human review scene first.");
        if (!new[] { "vault", "step-up", "climb", "thin-ledge" }.Contains(action) ||
            !new[] { "complete", "cancel", "support-loss", "drop-input", "legacy-climb", "legacy-drop" }.Contains(scenario))
            throw new ArgumentException("Choose vault/step-up/climb and complete/cancel/support-loss.");
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new IOException("Choose an empty directory to preserve prior evidence.");
        var review = UnityEngine.Object.FindFirstObjectByType<HumanInteractionReview>();
        var actor = review != null ? review.Actor : null;
        if (actor == null || actor.View == null) throw new InvalidOperationException("The Human actor is not initialized.");
        bool thin = action == "thin-ledge";
        bool climb = action == "climb" || thin;
        Directory.CreateDirectory(directory);
        var host = new GameObject("Temporary traversal review fixture");
        var origin = new Vector3(1000f, 0f, 1000f);
        bool controls = actor.ShowControls;
        var records = new List<object>();
        AuthoredAnimationReference original = null;
        Material sourceMaterial = null;
        const float dt = 1f / 60f;
        bool started = false, interrupted = false, sourceStarted = false;
        float sourceTime = 0f, hangingTime = 0f, recoveredTime = 0f;
        int frames = 0;
        try
        {
            review.ResetRoom(); actor.ResetActor(); actor.ShowControls = false;
            actor.HandTarget = actor.LookTarget = null;
            GameObject Box(string name, Vector3 center, Vector3 size, Vector3 offset, bool collision)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = name; box.transform.SetParent(host.transform);
                box.transform.position = origin + center + offset; box.transform.localScale = size;
                box.GetComponent<Collider>().enabled = collision;
                return box;
            }
            Vector3 obstacleCenter = thin ? new Vector3(0f, 2.45f, 1.6f) : action == "vault" ? new Vector3(0f, .5f, .85f) :
                new Vector3(0f, action == "step-up" ? .375f : 1.1f, 1.6f);
            Vector3 obstacleSize = thin ? new Vector3(3f, .2f, 2f) : action == "vault" ? new Vector3(2f, 1f, .4f) :
                new Vector3(3f, action == "step-up" ? .75f : 2.2f, 2f);
            Collider support = null;
            foreach (float sign in new[] { 0f, -1f, 1f })
            {
                var offset = HumanInteractionComparison.Offset * sign;
                Box("Review floor", new Vector3(0f, -.5f, 0f), new Vector3(12f, 1f, 12f), offset, sign == 0f);
                var obstacle = Box("Review traversal support", obstacleCenter, obstacleSize, offset, sign == 0f);
                if (sign == 0f) support = obstacle.GetComponent<Collider>();
            }
            Physics.SyncTransforms();
            var pose = new CharacterPose(origin + Vector3.up * (action == "vault" ? .09f : .025f) -
                Vector3.forward * (action == "vault" ? .45f : 0f), Vector3.up, Vector3.forward);
            actor.Motor.ResetPose(pose); actor.ThirdPersonCamera.Reset(pose);
            actor.Actor.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward, pose.Up));
            for (int i = 0; i < 60; i++) actor.Step(dt);
            string path = "Assets/Art/Characters/Animations/";
            string clipName = thin && actor.JumpGrabMotion != null ? "ReviewCandidates/Ledge Jump Full.fbx" : action == "vault" ? "Basic Fence Vault.fbx" : climb ? "Basic Pull Up.fbx" : "Step Up.fbx";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path + clipName).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var rig = thin && actor.JumpGrabMotion != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path + clipName) : action == "step-up" ? actor.CharacterPrefab : AssetDatabase.LoadAssetAtPath<GameObject>(path + "Traversal Reference.fbx");
            original = new AuthoredAnimationReference(rig, clip, action != "step-up");
            if (action != "step-up")
            {
                sourceMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                sourceMaterial.color = new Color(.65f, .68f, .7f);
                foreach (var renderer in original.Root.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => sourceMaterial).ToArray();
            }
            HumanInteractionComparison.Show();
            using var render = new AnimationReviewFrames(360);
            for (int frame = 0; frame < 480; frame++)
            {
                var buttons = frame == 30 ? ActorButtons.Jump : ActorButtons.None;
                float forward = frame >= 30 && !started && !thin ? 1f : 0f;
                if (actor.Traversal.Kind == TraversalKind.Hanging)
                {
                    hangingTime += dt;
                    if (hangingTime >= .4f && !sourceStarted)
                    {
                        if (scenario == "legacy-drop") buttons |= ActorButtons.Crouch;
                        else if (scenario == "legacy-climb" || !thin) buttons |= ActorButtons.Jump;
                        else forward = scenario == "drop-input" ? -1f : 1f;
                    }
                }
                bool targetPhase = !climb || actor.Traversal.Kind == TraversalKind.ClimbUp;
                if ((scenario == "cancel" || scenario == "support-loss") && started && targetPhase && !interrupted && actor.Traversal.Progress >= .35f)
                {
                    if (scenario == "cancel") actor.Traversal.Cancel(); else { support.enabled = false; Physics.SyncTransforms(); }
                    interrupted = true;
                }
                actor.Step(dt, new ActorIntent(new Vector2(0f, forward), Vector2.zero, buttons, (uint)frame));
                started |= actor.Traversal.Active;
                sourceStarted |= climb ? actor.Traversal.Kind == TraversalKind.ClimbUp : actor.Traversal.Active;
                if (sourceStarted) sourceTime += dt;
                original.Root.transform.SetPositionAndRotation((thin ? pose.Position : actor.Actor.position) - HumanInteractionComparison.Offset, thin ? Quaternion.identity : actor.Actor.rotation);
                original.Sample(thin ? Mathf.Max(0f, (frame - 29) * dt) : sourceTime);
                HumanInteractionComparison.Current.Sample(dt);
                var mixer = actor.View.Graph.BaseMixer;
                var clips = new List<object>();
                for (int i = 0; i < mixer.GetInputCount(); i++)
                    if (mixer.GetInput(i).IsValid() && mixer.GetInputWeight(i) > .001f)
                    {
                        var playable = (AnimationClipPlayable)mixer.GetInput(i);
                        clips.Add(new { slot = i, clip = playable.GetAnimationClip().name, time = playable.GetTime(), weight = mixer.GetInputWeight(i) });
                    }
                records.Add(new { frame, time = (frame + 1) / 60d, forward, buttons = buttons.ToString(),
                    root = V(actor.Actor.position), grounded = actor.Motor.Grounded, kind = actor.Traversal.Kind.ToString(),
                    actor.Traversal.Progress, actor.Traversal.Rejection, sourceTime, original.ClipTimeSeconds, clips });
                if (frame % 2 == 0)
                {
                    var center = actor.Actor.position + Vector3.up;
                    render.Capture(Path.Combine(directory, $"frame-{frame / 2:D4}.png"),
                        new[] { (thin ? pose.Position + Vector3.up : center) - HumanInteractionComparison.Offset, center + HumanInteractionComparison.Offset, center }, Quaternion.identity, 2f);
                }
                frames = frame + 1;
                if (started && !actor.Traversal.Active) recoveredTime += dt;
                if (recoveredTime >= 1.2f) break;
            }
            File.WriteAllText(Path.Combine(directory, "metadata.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                action, scenario, started, sourceStarted, interrupted, completed = recoveredTime >= 1.2f, frames,
                simulationHz = 60, captureHz = 30, fixture = thin ? "Suspended 0.2 m platform, top 2.55 m; jump in place then W/S or legacy controls after 0.4 s hanging" : "ActorTraversalTests box dimensions; ordinary forward/jump intent after hidden reset and 60 settling steps",
                columns = new[] { original.Label, "B: production graph without correction", "C: production runtime" },
                original.ClipAssetPath, original.RigAssetPath, original.RootMotionConvention,
                sourceLimits = thin ? "A shows the complete original jump-to-hang at 1x with a fixed source origin, then holds its final pose; it does not show the later pull-up or drop." : "A uses 1x clip time and production root trajectory. Climb A shows only pull-up, held before that phase. Step-up A uses production avatar. Production FBX import root settings differ from scratch; source rig materials adapted for visibility.",
                records
            }, Newtonsoft.Json.Formatting.Indented));
        }
        finally
        {
            original?.Dispose(); HumanInteractionComparison.Hide();
            UnityEngine.Object.DestroyImmediate(host);
            if (sourceMaterial != null) UnityEngine.Object.DestroyImmediate(sourceMaterial);
            actor.ShowControls = controls; actor.ResetActor();
        }
    }
    static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
}

