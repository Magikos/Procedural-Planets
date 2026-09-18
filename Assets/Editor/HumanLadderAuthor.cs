using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanLadderAuthor
{
    public const string LibraryPath = "Assets/Art/Characters/Motion/Ladder Performances.asset";
    public const float ApproachSourceStart = 72f / 30f;
    public const float ApproachSourceEnd = 94f / 30f;
    const string Clips = "Assets/Art/Characters/Animations/Ladder/";

    [MenuItem("Tools/Actors/Human/Add Ladder Review")]
    public static void Configure()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring ladders.");
        var actor = UnityEngine.Object.FindFirstObjectByType<HumanoidAnimationPrototype>();
        if (actor == null) throw new InvalidOperationException("Open the humanoid review scene first.");
        var library = AssetDatabase.LoadAssetAtPath<ActorAnimationPerformanceLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<ActorAnimationPerformanceLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        var names = new[] { "MountBottom", "MountTop", "Up", "Down", "Idle", "ExitBottom", "ExitTop" };
        var gripSource = AssetDatabase.LoadAllAssetsAtPath(Clips + "AnimSeq_Traversal_Ladder_Climb_Up_Loop.FBX")
            .OfType<AnimationClip>().First(clip => !clip.name.StartsWith("__", StringComparison.Ordinal));
        AnimationClip Grip(string sourceName, string file, AnimationCurve weight)
        {
            string path = Clips + file;
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path) ??
                LadderGripAuthor.Create(LadderGaitAuthor.Source(sourceName), path, gripSource, weight);
        }
        var up = Grip("Ladder Up", "CCP Rung Grip Up.anim", AnimationCurve.Constant(0f, 1f, 1f));
        var bottom = Grip("LadderBottomUp", "CCP Rung Grip Bottom.anim", AnimationCurve.EaseInOut(.2f, 0f, .7f, 1f));
        var top = Grip("Ladder TopUp", "CCP Rung Grip Top.anim", AnimationCurve.EaseInOut(.35f, 1f, .8f, 0f));
        var selected = new[] { bottom, LadderGaitAuthor.Reverse(top, Clips + "CCP Top Down.anim"), up,
            LadderGaitAuthor.Reverse(up, Clips + "CCP Climb Down.anim"), LadderGaitAuthor.Source("Ladder Idle"),
            LadderGaitAuthor.Reverse(bottom, Clips + "CCP Bottom Down.anim"), top };
        library.Entries = new[] { new ActorAnimationPerformanceLibrary.Entry { Id = "humanoid.ladder", Action = "Ladder", RigFamily = "Humanoid",
            Phases = names.Select((name, i) => new ActorAnimationPerformanceLibrary.Phase { Name = name, Clip = selected[i],
                Loop = name == "Up" || name == "Down" || name == "Idle", BlendSeconds = .18f }).ToArray() } };
        _ = library.Snapshot();
        var approachSource = AssetDatabase.LoadAllAssetsAtPath(Clips + "Ladder Final Step.FBX")
            .OfType<AnimationClip>().First(clip => !clip.name.StartsWith("__", StringComparison.Ordinal));
        var approachClip = LadderMotionAuthor.CreateInPlaceStep(approachSource,
            Clips + "RootMotion/Traversal_WalkForwardStartAndStop.FBX", ApproachSourceStart, Clips + "Ladder Final Step.anim");
        library.Entries[0].Phases = library.Entries[0].Phases.Concat(new[] {
            new ActorAnimationPerformanceLibrary.Phase { Name = "ApproachStep", Clip = approachClip, BlendSeconds = .08f }
        }).ToArray();
        var topMirrored = LadderGaitAuthor.Mirror(top, Clips + "CCP Top Up Mirrored.anim");
        library.Entries[0].Phases = library.Entries[0].Phases.Concat(new[] {
            new ActorAnimationPerformanceLibrary.Phase { Name = "ExitTopMirrored", Clip = topMirrored, BlendSeconds = .18f },
            new ActorAnimationPerformanceLibrary.Phase { Name = "MountTopMirrored",
                Clip = LadderGaitAuthor.Reverse(topMirrored, Clips + "CCP Top Down Mirrored.anim"), BlendSeconds = .18f },
            new ActorAnimationPerformanceLibrary.Phase { Name = "ExitBottomMirrored",
                Clip = LadderGaitAuthor.Mirror(selected[5], Clips + "CCP Bottom Down Mirrored.anim"), BlendSeconds = .18f }
        }).ToArray();
        var fastPhases = new[] { (Name: "SprintUp", Asset: "SprintUp"), (Name: "SprintUpRight", Asset: "SprintUpRight"), (Name: "SlideStart", Asset: "SlideStart"),
            (Name: "Slide", Asset: "SlideLoop"), (Name: "SlideEnd", Asset: "SlideEnd") };
        library.Entries[0].Phases = library.Entries[0].Phases.Concat(fastPhases.Select(entry =>
        {
            var motion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(
                "Assets/Art/Characters/Motion/Ladder " + entry.Asset + " Motion.asset");
            if (motion == null || motion.Clip == null)
                throw new InvalidOperationException("Missing authored ladder motion: " + entry.Asset);
            if (entry.Name.StartsWith("SprintUp", StringComparison.Ordinal))
            {
                bool rightLeading = entry.Name == "SprintUpRight";
                string hand = rightLeading ? "RightHand" : "LeftHand";
                string path = Clips + "Sprint " + hand + " Rung Grip.anim";
                var lead = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.3f, 0f), new Keyframe(.46f, 1f), new Keyframe(1f, 1f));
                var follow = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.5f, 1f), new Keyframe(.65f, 0f), new Keyframe(.8f, 0f), new Keyframe(.96f, 1f), new Keyframe(1f, 1f));
                motion.Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path) ?? LadderGripAuthor.Create(
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(Clips + "Sprint " + hand + ".anim"), path, gripSource,
                    rightLeading ? follow : lead, rightLeading ? lead : follow, fingersOnly: true);
                // Finger-only authoring leaves the sampled body/root envelope unchanged.
                EditorUtility.SetDirty(motion); AssetDatabase.SaveAssetIfDirty(motion);
            }
            return new ActorAnimationPerformanceLibrary.Phase { Name = entry.Name, Clip = motion.Clip,
                Loop = entry.Name == "SprintUp" || entry.Name == "SprintUpRight" || entry.Name == "Slide", BlendSeconds = .18f };
        })).ToArray();
        const string sprintTopSource = Clips + "UrgencyCandidates/mantle-high-3m-climbup-run-to-run.fbx";
        const string sprintTopPath = Clips + "Sprint Top Pull Up.anim";
        var sprintTopClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sprintTopPath);
        if (sprintTopClip == null)
        {
            var original = AssetDatabase.LoadAllAssetsAtPath(sprintTopSource).OfType<AnimationClip>()
                .First(clip => !clip.name.StartsWith("__", StringComparison.Ordinal));
            sprintTopClip = new AnimationClip();
            EditorUtility.CopySerialized(original, sprintTopClip);
            sprintTopClip.name = "Sprint Top Pull Up";
            AssetDatabase.CreateAsset(sprintTopClip, sprintTopPath);
        }
        var sprintTopMotion = LadderGaitAuthor.Bake(sprintTopClip, actor.CharacterPrefab,
            "Assets/Art/Characters/Motion/Ladder SprintTop Motion.asset");
        // Native hand support is at the 2.55m platform edge, 1.63m ahead of the running start.
        sprintTopMotion.ReferenceEdge = new Vector3(-.2f, 2.55f, 1.63f);
        EditorUtility.SetDirty(sprintTopMotion);
        library.Entries[0].Phases = library.Entries[0].Phases.Concat(new[] {
            new ActorAnimationPerformanceLibrary.Phase { Name = "SprintTop", Clip = sprintTopClip, BlendSeconds = .1f }
        }).ToArray();
        const string approachPath = "Assets/Art/Characters/Motion/Ladder ApproachStep Motion.asset";
        var approachMotion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(approachPath);
        if (approachMotion == null) { approachMotion = ScriptableObject.CreateInstance<ActorTraversalMotionAsset>(); AssetDatabase.CreateAsset(approachMotion, approachPath); }
        approachMotion.Clip = approachClip;
        LadderMotionAuthor.Bake(approachMotion, actor.CharacterPrefab,
            Clips + "RootMotion/Traversal_WalkForwardStartAndStop.FBX", false, ApproachSourceStart, ApproachSourceEnd, true, approachSource);
        ActorTraversalMotionAsset Motion(string phase) => LadderGaitAuthor.Bake(
            library.Entries[0].Phases.First(p => p.Name == phase).Clip, actor.CharacterPrefab,
            "Assets/Art/Characters/Motion/Ladder " + phase + " Motion.asset");
        var exitMotion = Motion("ExitTop"); var mountMotion = Motion("MountTop");
        var bottomMotion = Motion("MountBottom"); var bottomExitMotion = Motion("ExitBottom");
        var upMotion = Motion("Up"); var downMotion = Motion("Down");
        var mirroredExitMotion = Motion("ExitTopMirrored"); Motion("MountTopMirrored");
        var mirroredBottomExit = Motion("ExitBottomMirrored");
        LadderGaitAuthor.Mirror(LadderGaitAuthor.Source("Ladder TopUp"), Clips + "CCP Original Top Up Mirrored.anim");
        LadderGaitAuthor.Mirror(LadderGaitAuthor.Source("LadderBottomUp"), Clips + "CCP Original Bottom Up Mirrored.anim");
        var existing = GameObject.Find("Authored ladder review");
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
        var root = new GameObject("Authored ladder review");
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Review/ReviewObstacle.mat");
        var ladders = new LadderInteraction[2];
        for (int i = 0; i < 2; i++)
        {
            var ladderRoot = new GameObject(i == 0 ? "Ladder 2.55m" : "Ladder 3.75m");
            ladderRoot.transform.SetParent(root.transform, false);
            ladderRoot.transform.position = new Vector3(-8f - i * 3f, 0f, -3f);
            var ladder = ladders[i] = ladderRoot.AddComponent<LadderInteraction>();
            ladder.TopExitMotion = exitMotion; ladder.TopMountMotion = mountMotion; ladder.BottomMountMotion = bottomMotion;
            ladder.TopExitMirroredMotion = mirroredExitMotion;
            ladder.BottomExitMirroredMotion = mirroredBottomExit;
            ladder.SlideStartMotion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>("Assets/Art/Characters/Motion/Ladder SlideStart Motion.asset");
            ladder.SlideMotion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>("Assets/Art/Characters/Motion/Ladder SlideLoop Motion.asset");
            ladder.SlideEndMotion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>("Assets/Art/Characters/Motion/Ladder SlideEnd Motion.asset");
            // Keep fast ladder ascent in the normal authored gait. The wall-climb takes pull the torso into the rungs.
            ladder.SprintLeftMotion = ladder.SprintRightMotion = null;
            ladder.SprintTopMotion = sprintTopMotion;
            ladder.SprintPlaybackRate = 2f;
            ladder.ApproachStepMotion = approachMotion; ladder.BottomExitMotion = bottomExitMotion;
            ladder.UpGaitMotion = upMotion; ladder.DownGaitMotion = downMotion;
            ladder.Height = i == 0 ? 2.55f : 3.75f;
            ladder.RungSpacing = i == 0 ? .3f : .25f;
            // Fit rung pitch and palm depth from complete native support intervals; keep the existing contact correction cap.
            ladder.RungOffset = i == 0 ? .207f : .204f;
            ladder.RungPalmDepth = .08f;
            ladder.BodyDistance = .325f + ladder.RungPalmDepth;
            ladder.GaitStartLeftPalm = new Vector3(-.21589537f, .993262053f, .304675817f);
            ladder.GaitStartRightPalm = new Vector3(.255465657f, 1.34235013f, .322969f);
            void Box(string name, Vector3 position, Vector3 scale, bool collision)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
                box.transform.SetParent(ladderRoot.transform, false); box.transform.localPosition = position; box.transform.localScale = scale;
                if (material != null) box.GetComponent<Renderer>().sharedMaterial = material;
                if (!collision) UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
            }
            for (int side = -1; side <= 1; side += 2)
                Box("Rail", new Vector3(side * ladder.Width * .5f, ladder.Height * .5f, 0f), new Vector3(.045f, ladder.Height + .3f, .055f), true);
            for (float y = ladder.RungOffset; y <= ladder.Height + .001f; y += ladder.RungSpacing)
                Box("Rung", new Vector3(0f, y, 0f), new Vector3(ladder.Width, .035f, .035f), false);
            // Authored body envelopes permit the real platform edge to meet the rungs.
            Box("Top platform", new Vector3(0f, ladder.Height - .1f, .75f), new Vector3(2f, .2f, 1.5f), true);
            Box("Ground pad", new Vector3(0f, -.1f, -.4f), new Vector3(2.5f, .2f, 3.5f), true);
        }
        actor.Ladders = ladders; actor.LadderPerformances = library;
        EditorUtility.SetDirty(library); EditorUtility.SetDirty(actor);
        EditorSceneManager.MarkSceneDirty(actor.gameObject.scene);
        AssetDatabase.SaveAssets();
    }
}


