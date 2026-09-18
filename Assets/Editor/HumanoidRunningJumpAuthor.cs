using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Preserves the owned running jump's curves in an editable motor-driven variant.</summary>
public static class HumanoidRunningJumpAuthor
{
    const string Folder = "Assets/Art/Characters/";
    public const string SourcePath = Folder + "Animations/ReviewCandidates/Traversal_Movement_Jump_fromRun_toRun.fbx";
    public const string LibraryPath = Folder + "Motion/Authored Jump Performances.asset";
    public const string ForwardSourcePath = Folder + "Animations/ReviewCandidates/Traversal_JumpRunForward.FBX";

    public static ActorAnimationPerformanceLibrary ConfigureContextualLandings()
    {
        var library = CreateLibrary();
        var entry = library.Entries.Single(item => item.Id == "humanoid.jump.running");
        const string path = Folder + "Animations/Forward Jump.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            if (File.Exists(path)) throw new IOException("The forward jump path already exists but is not imported.");
            var source = AssetDatabase.LoadAllAssetsAtPath(ForwardSourcePath).OfType<AnimationClip>()
                .First(item => !item.name.StartsWith("__preview__", StringComparison.Ordinal));
            clip = UnityEngine.Object.Instantiate(source);
            clip.name = "Forward Jump";
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            settings.loopBlendPositionXZ = settings.loopBlendPositionY = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
        }
        const string landingPath = Folder + "Animations/Running Landing Right.anim";
        var runningClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(landingPath);
        if (runningClip == null)
        {
            if (File.Exists(landingPath)) throw new IOException("The running landing path already exists but is not imported.");
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Animations/Running Jump.anim");
            runningClip = UnityEngine.Object.Instantiate(source);
            runningClip.name = "Running Landing Right";
            var settings = AnimationUtility.GetAnimationClipSettings(runningClip);
            settings.mirror = true;
            AnimationUtility.SetAnimationClipSettings(runningClip, settings);
            AssetDatabase.CreateAsset(runningClip, landingPath);
        }
        var stop = library.Entries.Single(item => item.Condition == "").Phases.Single(item => item.Name == "Landing");
        var runningLanding = new ActorAnimationPerformanceLibrary.Phase
        { Name = "Landing", Clip = runningClip, StartNormalized = 29f / 40f, EndNormalized = 34f / 40f, BlendSeconds = .1f };
        entry.Phases = new[] { Phase("Ascent", .4f, .667f), Phase("Apex", .667f, .7f),
            Phase("Descent", .7f, .967f), runningLanding,
            new ActorAnimationPerformanceLibrary.Phase { Name = "LandingPrepare", Clip = runningClip,
                StartNormalized = 27f / 40f, EndNormalized = 29f / 40f, BlendSeconds = .1f },
            new ActorAnimationPerformanceLibrary.Phase { Name = "LandingStop", Clip = stop.Clip,
                StartNormalized = stop.StartNormalized, EndNormalized = stop.EndNormalized, BlendSeconds = .16f } };
        library.Snapshot();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(clip);
        AssetDatabase.SaveAssetIfDirty(library);
        MatchLandingToRun();
        return library;

        ActorAnimationPerformanceLibrary.Phase Phase(string name, float start, float end) => new()
        {
            Name = name, Clip = clip, StartNormalized = start / clip.length, EndNormalized = end / clip.length,
            BlendSeconds = .12f
        };
    }

    public static float MatchLandingToRun()
    {
        var library = CreateLibrary();
        var entry = library.Entries.Single(item => item.Id == "humanoid.jump.running");
        var landing = entry.Phases.Single(phase => phase.Name == "Landing");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Art/Characters/Human/Converted/SM_Chr_Rider_01_v2/SM_Chr_Rider_01_Fit.prefab");
        var run = AssetDatabase.LoadAllAssetsAtPath(Folder + "Animations/Run Forward.fbx").OfType<AnimationClip>()
            .First(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
        using var jumpReference = new AuthoredAnimationReference(prefab, landing.Clip, false);
        using var runReference = new AuthoredAnimationReference(prefab, run, false);
        jumpReference.Sample(landing.EndNormalized * landing.Clip.length);
        var left = Foot(jumpReference, HumanBodyBones.LeftFoot);
        var right = Foot(jumpReference, HumanBodyBones.RightFoot);
        float best = float.PositiveInfinity;
        float matched = 0f;
        for (int sample = 0; sample < 120; sample++)
        {
            float phase = sample / 120f;
            runReference.Sample(phase * run.length);
            float error = (Foot(runReference, HumanBodyBones.LeftFoot) - left).sqrMagnitude +
                (Foot(runReference, HumanBodyBones.RightFoot) - right).sqrMagnitude;
            if (error >= best) continue;
            best = error;
            matched = phase;
        }
        entry.LocomotionExitPhase = matched;
        library.Snapshot();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
        return matched;

        static Vector3 Foot(AuthoredAnimationReference reference, HumanBodyBones bone) =>
            reference.Animator.GetBoneTransform(bone).position - reference.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
    }

    public static ActorAnimationPerformanceLibrary CreateLibrary()
    {
        var existing = AssetDatabase.LoadAssetAtPath<ActorAnimationPerformanceLibrary>(LibraryPath);
        if (existing != null) return existing;
        if (File.Exists(LibraryPath)) throw new IOException("The jump library path already exists but is not imported.");
        var fallback = AssetDatabase.LoadAssetAtPath<ActorAnimationPerformanceLibrary>(Folder + "Motion/Short Hop Performances.asset");
        if (fallback == null) throw new InvalidOperationException("The standing jump library is required.");
        var source = AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
        if (source == null || !source.isHumanMotion) throw new InvalidOperationException("Import the owned humanoid running jump first.");
        const string clipPath = Folder + "Animations/Running Jump.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            if (File.Exists(clipPath)) throw new IOException("The running jump path already exists but is not imported.");
            clip = UnityEngine.Object.Instantiate(source);
            clip.name = "Running Jump";
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            // The motor supplies travel and the flight parabola. Keep all authored muscle curves unchanged.
            settings.loopBlendPositionXZ = false;
            settings.loopBlendPositionY = false;
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssetIfDirty(clip);
        }
        var library = UnityEngine.Object.Instantiate(fallback);
        library.name = "Authored Jump Performances";
        var entry = new ActorAnimationPerformanceLibrary.Entry
        {
            Id = "humanoid.jump.running", Action = "Jump", RigFamily = "Humanoid", Condition = "Running",
            // Source frames: push-off 10, body apex 16-17, touchdown 29, running recovery 34 (30 Hz).
            Phases = new[] { Phase("Ascent", 10f, 16f), Phase("Apex", 16f, 17f),
                Phase("Descent", 17f, 29f), Phase("Landing", 29f, 34f) }
        };
        library.Entries = library.Entries.Concat(new[] { entry }).ToArray();
        library.Snapshot();
        AssetDatabase.CreateAsset(library, LibraryPath);
        AssetDatabase.SaveAssetIfDirty(library);
        return library;

        ActorAnimationPerformanceLibrary.Phase Phase(string name, float first, float last) => new()
        {
            Name = name, Clip = clip, StartNormalized = first / 40f, EndNormalized = last / 40f,
            BlendSeconds = .12f
        };
    }
}
