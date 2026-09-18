using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class HumanoidCrawlStrafeAuthor
{
    const int Samples = 64;
    public static float LastMaximumReachError { get; private set; }

    public static float MeasureCycleDistance(AnimationClip clip, GameObject prefab, Vector3 localDirection)
    {
        if (clip == null || !clip.isHumanMotion || clip.length <= 0f)
            throw new ArgumentException("A positive-duration Humanoid crawl is required.", nameof(clip));
        if (prefab == null) throw new ArgumentNullException(nameof(prefab));
        if (!CharacterMath.IsFinite(localDirection) || localDirection.sqrMagnitude < .0001f || Mathf.Abs(localDirection.y) > .0001f)
            throw new ArgumentException("A finite horizontal crawl direction is required.", nameof(localDirection));
        localDirection.Normalize();
        var actor = UnityEngine.Object.Instantiate(prefab);
        actor.hideFlags = HideFlags.HideAndDontSave;
        ActorAnimationGraph graph = null;
        try
        {
            var animator = actor.GetComponent<Animator>();
            if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
                throw new ArgumentException("A valid Humanoid prefab is required.", nameof(prefab));
            graph = new ActorAnimationGraph(animator, "Crawl support distance measurement", 1);
            var playable = graph.AddBaseClip(0, clip);
            playable.SetSpeed(0d); graph.BaseMixer.SetInputWeight(0, 1f);
            var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
            var previous = new Vector3[2];
            float backwardDistance = 0f;
            int supportSamples = 0;
            const int count = 128;
            for (int sample = 0; sample <= count; sample++)
            {
                playable.SetTime(clip.length * sample / (double)count); graph.Evaluate();
                for (int foot = 0; foot < feet.Length; foot++)
                {
                    Vector3 position = actor.transform.InverseTransformPoint(feet[foot].position);
                    float displacement = Vector3.Dot(position - previous[foot], localDirection);
                    if (sample > 0 && displacement < 0f) { backwardDistance -= displacement; supportSamples++; }
                    previous[foot] = position;
                }
            }
            if (supportSamples == 0) throw new InvalidOperationException("The crawl has no backward support motion in the requested direction.");
            float distance = backwardDistance * count / supportSamples;
            if (!float.IsFinite(distance) || distance <= 0f) throw new InvalidOperationException("The crawl support distance is invalid.");
            return distance;
        }
        finally { graph?.Dispose(); UnityEngine.Object.DestroyImmediate(actor); }
    }

    public static AnimationClip Create(AnimationClip source, GameObject prefab, string destination, bool left, bool replaceExisting = true)
    {
        if (source == null || !source.isHumanMotion || source.length <= 0f) throw new ArgumentException("A positive-duration Humanoid crawl is required.", nameof(source));
        if (prefab == null) throw new ArgumentNullException(nameof(prefab));
        if (string.IsNullOrEmpty(destination) || !destination.StartsWith("Assets/", StringComparison.Ordinal) ||
            !destination.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) || destination.Contains("..") || destination.Contains('\\') ||
            !AssetDatabase.IsValidFolder(Path.GetDirectoryName(destination)?.Replace('\\', '/')))
            throw new ArgumentException("Choose an .anim path inside an existing Assets folder.", nameof(destination));
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
        if ((File.Exists(destination) || AssetDatabase.LoadMainAssetAtPath(destination) != null) && (existing == null || !replaceExisting))
            throw new IOException("Crawl destination already exists: " + destination);
        var actor = UnityEngine.Object.Instantiate(prefab);
        actor.hideFlags = HideFlags.HideAndDontSave;
        var graph = PlayableGraph.Create("Crawl lateral authoring");
        HumanPoseHandler handler = null;
        AnimationClip result = null;
        try
        {
            var animator = actor.GetComponent<Animator>();
            if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
                throw new ArgumentException("A valid Humanoid prefab is required.", nameof(prefab));
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, source); playable.SetSpeed(0d);
            AnimationPlayableOutput.Create(graph, "Crawl", animator).SetSourcePlayable(playable); graph.Play();
            handler = new HumanPoseHandler(animator.avatar, actor.transform);
            var bones = new[] {
                new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
                new[] { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
                new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot },
                new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot }
            };
            var chains = new Transform[4][];
            var solvers = new LimbPoseSolver[4];
            for (int limb = 0; limb < 4; limb++)
            {
                chains[limb] = new Transform[3];
                for (int bone = 0; bone < 3; bone++) chains[limb][bone] = animator.GetBoneTransform(bones[limb][bone]);
                solvers[limb] = new LimbPoseSolver(chains[limb]);
            }
            var endpoints = new Vector3[Samples, 4];
            var footContactRotations = new Quaternion[2];
            var footSideClearance = new float[2];
            for (int foot = 0; foot < 2; foot++)
            {
                var toe = animator.GetBoneTransform(foot == 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                footSideClearance[foot] = .03f + (toe != null
                    ? actor.transform.InverseTransformVector(toe.position - chains[foot + 2][2].position).magnitude : .1f);
            }
            var lowestFoot = new[] { float.PositiveInfinity, float.PositiveInfinity };
            var bends = new Vector3[Samples, 4];
            var means = new Vector3[4];
            var bendMeans = new Vector3[4];
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            for (int sample = 0; sample < Samples; sample++)
            {
                Sample(sample);
                for (int limb = 0; limb < 4; limb++)
                {
                    endpoints[sample, limb] = actor.transform.InverseTransformPoint(chains[limb][2].position);
                    means[limb] += endpoints[sample, limb] / Samples;
                    if (limb >= 2 && endpoints[sample, limb].y < lowestFoot[limb - 2])
                    {
                        lowestFoot[limb - 2] = endpoints[sample, limb].y;
                        footContactRotations[limb - 2] = chains[limb][2].rotation;
                    }
                    bends[sample, limb] = actor.transform.InverseTransformPoint(chains[limb][1].position);
                    bendMeans[limb] += bends[sample, limb] / Samples;
                }
            }
            var widths = new float[4];
            for (int limb = 0; limb < 4; limb++)
            {
                float excursion = 0f;
                for (int sample = 0; sample < Samples; sample++)
                    excursion = Mathf.Max(excursion, Mathf.Abs(endpoints[sample, limb].z - means[limb].z));
                widths[limb] = excursion > .0001f ? Mathf.Clamp01((Mathf.Abs(means[limb].x) - .08f) / excursion) : 1f;
            }
            var muscles = new float[Samples, HumanTrait.MuscleCount];
            var goals = new float[Samples, 2, 7];
            var goalCurves = new AnimationCurve[2, 7];
            for (int foot = 0; foot < 2; foot++) for (int component = 0; component < 7; component++)
            {
                string property = (foot == 0 ? "Left" : "Right") + "Foot" + (component < 3 ? "T." + "xyz"[component] : "Q." + "xyzw"[component - 3]);
                goalCurves[foot, component] = AnimationUtility.GetEditorCurve(source, EditorCurveBinding.FloatCurve("", typeof(Animator), property));
                if (goalCurves[foot, component] == null) throw new InvalidOperationException("Crawl source is missing Humanoid goal curve: " + property);
            }
            Quaternion rotate = Quaternion.AngleAxis(left ? -90f : 90f, Vector3.up);
            var pose = new HumanPose();
            LastMaximumReachError = 0f;
            for (int sample = 0; sample < Samples; sample++)
            {
                Sample(sample);
                handler.GetHumanPose(ref pose);
                Quaternion sourceBodyRotation = pose.bodyRotation;
                for (int limb = 0; limb < 4; limb++)
                {
                    Vector3 originalTipPosition = chains[limb][2].position;
                    Quaternion originalTipRotation = chains[limb][2].rotation;
                    Vector3 delta = rotate * (endpoints[sample, limb] - means[limb]);
                    delta.x *= limb >= 2 ? 1f : widths[limb];
                    // Retain a small approach/release arc without turning the source's lateral sway into a forward stroke.
                    delta.z *= limb >= 2 ? 0f : .25f;
                    Vector3 target = means[limb] + delta;
                    if (limb >= 2)
                        target.x = limb == 2 ? Mathf.Min(-footSideClearance[0], target.x) : Mathf.Max(footSideClearance[1], target.x);
                    Vector3 bendDelta = rotate * (bends[sample, limb] - bendMeans[limb]);
                    bendDelta.x *= limb >= 2 ? 1f : widths[limb];
                    bendDelta.z *= limb >= 2 ? 0f : .25f;
                    Vector3 pole = actor.transform.TransformPoint(bendMeans[limb] + bendDelta) - chains[limb][0].position;
                    // Forward-crawl ankle rocking otherwise survives the lateral endpoint remap as a forward push-off.
                    Quaternion tipRotation = limb >= 2 ? footContactRotations[limb - 2] : chains[limb][2].rotation;
                    float error = solvers[limb].Solve(actor.transform.TransformPoint(target), 1f, 180f, tipRotation, pole);
                    LastMaximumReachError = Mathf.Max(LastMaximumReachError, error);
                    if (limb >= 2)
                    {
                        int foot = limb - 2;
                        float time = source.length * sample / Samples;
                        Vector3 deltaGoal = Quaternion.Inverse(sourceBodyRotation) * (chains[limb][2].position - originalTipPosition) / animator.humanScale;
                        for (int component = 0; component < 3; component++)
                            goals[sample, foot, component] = goalCurves[foot, component].Evaluate(time) + deltaGoal[component];
                        Quaternion sourceGoal = new Quaternion(goalCurves[foot, 3].Evaluate(time), goalCurves[foot, 4].Evaluate(time),
                            goalCurves[foot, 5].Evaluate(time), goalCurves[foot, 6].Evaluate(time));
                        Quaternion deltaRotation = Quaternion.Inverse(sourceBodyRotation) * chains[limb][2].rotation *
                            Quaternion.Inverse(originalTipRotation) * sourceBodyRotation;
                        Quaternion goal = (deltaRotation * sourceGoal).normalized;
                        if (sample > 0)
                        {
                            var previous = new Quaternion(goals[sample - 1, foot, 3], goals[sample - 1, foot, 4], goals[sample - 1, foot, 5], goals[sample - 1, foot, 6]);
                            if (Quaternion.Dot(previous, goal) < 0f) goal = new Quaternion(-goal.x, -goal.y, -goal.z, -goal.w);
                        }
                        for (int component = 0; component < 4; component++) goals[sample, foot, component + 3] = goal[component];
                    }
                }
                handler.GetHumanPose(ref pose);
                for (int muscle = 0; muscle < HumanTrait.MuscleCount; muscle++)
                {
                    if (!float.IsFinite(pose.muscles[muscle])) throw new InvalidOperationException("Lateral crawl solve produced a nonfinite muscle.");
                    muscles[sample, muscle] = pose.muscles[muscle];
                }
            }
            if (LastMaximumReachError > .08f * actor.transform.lossyScale.x)
                throw new InvalidOperationException("Lateral crawl targets exceed available reach: " + LastMaximumReachError);
            result = UnityEngine.Object.Instantiate(source); result.name = Path.GetFileNameWithoutExtension(destination);
            // Mecanim foot goals participate in retargeting and otherwise restore the source forward foot trajectory.
            for (int foot = 0; foot < 2; foot++) for (int component = 0; component < 7; component++)
            {
                var keys = new Keyframe[Samples + 1];
                for (int i = 0; i <= Samples; i++)
                {
                    float tangent = (goals[(i + 1) % Samples, foot, component] - goals[(i + Samples - 1) % Samples, foot, component]) / (2f * source.length / Samples);
                    keys[i] = new Keyframe((float)i / Samples * source.length, goals[i % Samples, foot, component], tangent, tangent);
                }
                string property = (foot == 0 ? "Left" : "Right") + "Foot" + (component < 3 ? "T." + "xyz"[component] : "Q." + "xyzw"[component - 3]);
                AnimationUtility.SetEditorCurve(result, EditorCurveBinding.FloatCurve("", typeof(Animator), property), new AnimationCurve(keys));
            }
            for (int muscle = 0; muscle < HumanTrait.MuscleCount; muscle++)
            {
                string name = HumanTrait.MuscleName[muscle];
                if (!IsLimb(name)) continue;
                var keys = new Keyframe[Samples + 1];
                for (int i = 0; i <= Samples; i++)
                {
                    float tangent = (muscles[(i + 1) % Samples, muscle] - muscles[(i + Samples - 1) % Samples, muscle]) / (2f * source.length / Samples);
                    keys[i] = new Keyframe((float)i / Samples * source.length, muscles[i % Samples, muscle], tangent, tangent);
                }
                AnimationUtility.SetEditorCurve(result, EditorCurveBinding.FloatCurve("", typeof(Animator), name), new AnimationCurve(keys));
            }
            if (existing == null) { AssetDatabase.CreateAsset(result, destination); var created = result; result = null; return created; }
            EditorUtility.CopySerialized(result, existing); EditorUtility.SetDirty(existing); return existing;

            void Sample(int sample)
            {
                playable.SetTime(Mathf.Repeat((float)sample / Samples - settings.cycleOffset, 1f) * source.length);
                graph.Evaluate(0f);
            }
        }
        finally
        {
            if (result != null) UnityEngine.Object.DestroyImmediate(result);
            handler?.Dispose(); graph.Destroy(); UnityEngine.Object.DestroyImmediate(actor);
        }
    }

    static bool IsLimb(string name) => name.StartsWith("Left Arm", StringComparison.Ordinal) || name.StartsWith("Right Arm", StringComparison.Ordinal) ||
        name.StartsWith("Left Forearm", StringComparison.Ordinal) || name.StartsWith("Right Forearm", StringComparison.Ordinal) ||
        name.StartsWith("Left Hand", StringComparison.Ordinal) || name.StartsWith("Right Hand", StringComparison.Ordinal) ||
        name.StartsWith("Left Upper Leg", StringComparison.Ordinal) || name.StartsWith("Right Upper Leg", StringComparison.Ordinal) ||
        name.StartsWith("Left Lower Leg", StringComparison.Ordinal) || name.StartsWith("Right Lower Leg", StringComparison.Ordinal) ||
        name.StartsWith("Left Foot", StringComparison.Ordinal) || name.StartsWith("Right Foot", StringComparison.Ordinal);
}
