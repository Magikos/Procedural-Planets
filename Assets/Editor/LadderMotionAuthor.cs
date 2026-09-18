using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Records original root travel alongside the production in-place body envelope at native clip time.</summary>
public static class LadderMotionAuthor
{
    public static AnimationClip OffsetBodyOrigin(AnimationClip source, GameObject prefab, Vector3 offset, string path)
    {
        if (source == null || !source.isHumanMotion || !CharacterMath.IsFinite(offset))
            throw new ArgumentException("Body origin fitting requires a humanoid source and finite offset.");
        using var reference = new AuthoredAnimationReference(prefab, source, false);
        float scale = reference.Animator.humanScale;
        Vector3 originalHips = reference.Root.transform.InverseTransformPoint(reference.Animator.GetBoneTransform(HumanBodyBones.Hips).position);
        if (scale <= 0f) throw new InvalidOperationException("Body origin fitting requires a positive avatar scale.");
        var result = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (result == null) { result = new AnimationClip(); AssetDatabase.CreateAsset(result, path); }
        EditorUtility.CopySerialized(source, result); result.name = Path.GetFileNameWithoutExtension(path);
        result.hideFlags = HideFlags.None;
        var settings = AnimationUtility.GetAnimationClipSettings(result);
        settings.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(result, settings);
        // Some imports recenter the body on XZ. Measure that fixed origin before applying the requested placement.
        Vector3 originDifference;
        using (var fitted = new AuthoredAnimationReference(prefab, result, false))
            originDifference = fitted.Root.transform.InverseTransformPoint(fitted.Animator.GetBoneTransform(HumanBodyBones.Hips).position) - originalHips;
        for (int axis = 0; axis < 3; axis++)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT." + "xyz"[axis]);
            var curve = AnimationUtility.GetEditorCurve(source, binding);
            if (curve == null) throw new InvalidOperationException("The source lacks its humanoid body origin curve.");
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++) keys[i].value += (offset[axis] - originDifference[axis]) / scale;
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(result, binding, curve);
        }
        EditorUtility.SetDirty(result);
        return result;
    }

    public static AnimationClip CreateInPlaceStep(AnimationClip imported, string originalPath, float sourceStart, string outputPath)
    {
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(imported)) as ModelImporter;
        var avatarModel = importer != null ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(importer.sourceAvatar)) : null;
        var avatar = avatarModel != null ? avatarModel.GetComponent<Animator>() : null;
        if (avatar == null || avatar.humanScale <= 0f) throw new ArgumentException("Step normalization requires the original source avatar scale.");
        var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(originalPath);
        var sourceClip = AssetDatabase.LoadAllAssetsAtPath(originalPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
        if (!float.IsFinite(sourceStart) || sourceStart < 0f || sourceStart + imported.length > sourceClip.length + .001f)
            throw new ArgumentOutOfRangeException(nameof(sourceStart));
        var source = UnityEngine.Object.Instantiate(sourcePrefab);
        source.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var root = source.GetComponentsInChildren<Transform>().First(t => t.name == "root");
            int intervals = Mathf.CeilToInt(imported.length * 60f);
            var travel = new Vector3[intervals + 1];
            for (int i = 0; i <= intervals; i++)
            {
                sourceClip.SampleAnimation(source, sourceStart + imported.length * i / intervals);
                travel[i] = source.transform.InverseTransformPoint(root.position) / avatar.humanScale;
            }
            var result = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
            if (result == null) { result = new AnimationClip(); AssetDatabase.CreateAsset(result, outputPath); }
            EditorUtility.CopySerialized(imported, result);
            result.name = "Authored Ladder Final Step";
            result.hideFlags = HideFlags.None;
            foreach (string axis in new[] { "x", "z" })
            {
                var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT." + axis);
                var curve = AnimationUtility.GetEditorCurve(imported, binding);
                if (curve == null) throw new InvalidOperationException("The humanoid step is missing its RootT." + axis + " curve.");
                // RootT stores humanoid body translation. Remove only the artist's root track;
                // the collision authority adds the same track back at runtime. Muscles stay untouched.
                var keys = new Keyframe[intervals + 1];
                for (int i = 0; i <= intervals; i++)
                {
                    float time = imported.length * i / intervals;
                    keys[i] = new Keyframe(time, curve.Evaluate(time) - (axis == "x" ? travel[i].x : travel[i].z));
                }
                for (int i = 0; i <= intervals; i++)
                {
                    int before = Mathf.Max(0, i - 1), after = Mathf.Min(intervals, i + 1);
                    float tangent = (keys[after].value - keys[before].value) / (keys[after].time - keys[before].time);
                    keys[i].inTangent = keys[i].outTangent = tangent;
                }
                AnimationUtility.SetEditorCurve(result, binding, new AnimationCurve(keys));
            }
            EditorUtility.SetDirty(result);
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
    }

    public static void InspectRoute(string outputPath, int ladderIndex = 0, bool mount = false)
    {
        var actor = UnityEngine.Object.FindFirstObjectByType<HumanoidAnimationPrototype>();
        var ladder = actor.Ladders[ladderIndex];
        var motion = (mount ? ladder.TopMountMotion : ladder.TopExitMotion).CreateMotion();
        Vector3 start = mount ? ladder.TopApproach : ladder.Bottom + ladder.transform.up * (ladder.Height - motion.Sample(1f).Root.y);
        Vector3 World(Vector3 value) => start + ladder.transform.rotation * value;
        var collision = new ActorCollision(1 << 0);
        var records = new System.Collections.Generic.List<object>();
        for (int i = 1; i <= 48; i++)
        {
            float before = (i - 1) / 48f, after = i / 48f;
            var a = motion.Sample(before); var b = motion.Sample(after);
            Vector3 a0 = World(a.Root + a.CapsuleA), a1 = World(a.Root + a.CapsuleB);
            Vector3 b0 = World(b.Root + b.CapsuleA), b1 = World(b.Root + b.CapsuleB);
            bool clear = collision.ClearCapsuleSegment(a0, a1, a.Radius, b0, b1, b.Radius);
            if (clear) continue;
            Vector3 delta = (b0 + b1 - a0 - a1) * .5f;
            float radius = Mathf.Max(a.Radius, b.Radius) + Mathf.Max((b0 - a0 - delta).magnitude, (b1 - a1 - delta).magnitude);
            records.Add(new { before, after, a0 = V(a0), a1 = V(a1), b0 = V(b0), b1 = V(b1), radius,
                startOverlap = Physics.OverlapCapsule(a0, a1, a.Radius, 1 << 0, QueryTriggerInteraction.Ignore).Select(c => c.name).ToArray(),
                endOverlap = Physics.OverlapCapsule(b0, b1, b.Radius, 1 << 0, QueryTriggerInteraction.Ignore).Select(c => c.name).ToArray(),
                sweep = delta.sqrMagnitude > .00000001f ? Physics.CapsuleCastAll(a0, a1, radius, delta.normalized, delta.magnitude, 1 << 0,
                    QueryTriggerInteraction.Ignore).Select(hit => new { collider = hit.collider.name, hit.distance, point = V(hit.point) }).ToArray() : null });
        }
        File.WriteAllText(outputPath, Newtonsoft.Json.JsonConvert.SerializeObject(new { mount, ladderIndex, start = V(start), blocked = records }, Newtonsoft.Json.Formatting.Indented));
    }

    public static void Bake(ActorTraversalMotionAsset asset, GameObject prefab, string originalPath, bool descending,
        float sourceStart = 0f, float sourceEnd = -1f, bool groundStep = false, AnimationClip humanoidSource = null)
    {
        if (asset == null || asset.Clip == null || prefab == null) throw new ArgumentException("Ladder motion requires an asset, clip, and actor.");
        var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(originalPath);
        var sourceClip = AssetDatabase.LoadAllAssetsAtPath(originalPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
        float lastSecond = sourceEnd < 0f ? sourceClip.length : sourceEnd;
        if (!float.IsFinite(sourceStart) || !float.IsFinite(lastSecond) || sourceStart < 0f ||
            lastSecond <= sourceStart || lastSecond > sourceClip.length + .001f ||
            Mathf.Abs(lastSecond - sourceStart - asset.Clip.length) > .002f)
            throw new ArgumentException("Ladder source interval must match the production clip duration.");
        var source = UnityEngine.Object.Instantiate(sourcePrefab);
        source.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            using var production = new AuthoredAnimationReference(prefab, asset.Clip, false);
            var importer = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(humanoidSource != null ? humanoidSource : asset.Clip));
            var avatarModel = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(importer.sourceAvatar));
            var sourceAnimator = avatarModel != null ? avatarModel.GetComponent<Animator>() : null;
            if (sourceAnimator == null || sourceAnimator.humanScale <= 0f)
                throw new InvalidOperationException("Ladder root transfer requires the original source avatar scale.");
            float retargetScale = production.Animator.humanScale / sourceAnimator.humanScale;
            var root = source.GetComponentsInChildren<Transform>().First(t => t.name == "root");
            Quaternion basis = Quaternion.Euler(0f, descending ? 180f : 0f, 0f);
            Vector3 start = Vector3.zero;
            var frames = new ActorTraversalMotion.Frame[121];
            for (int i = 0; i < frames.Length; i++)
            {
                float phase = i / 120f;
                sourceClip.SampleAnimation(source, Mathf.Lerp(sourceStart, sourceEnd < 0f ? sourceClip.length : sourceEnd, phase));
                production.Sample(asset.Clip.length * phase);
                Vector3 position = source.transform.InverseTransformPoint(root.position);
                if (i == 0) start = position;
                float yaw = Mathf.DeltaAngle(0f, root.eulerAngles.y + (descending ? 180f : 0f));
                Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
                Vector3 Bone(HumanBodyBones bone) => rotation * production.Root.transform.InverseTransformPoint(production.Animator.GetBoneTransform(bone).position);
                frames[i] = new ActorTraversalMotion.Frame { Root = basis * (position - start) * retargetScale,
                    CapsuleA = Bone(HumanBodyBones.Hips), CapsuleB = Bone(HumanBodyBones.Head),
                    RootYawDegrees = yaw, Radius = .14f, PlanarFitWeight = phase };
                if (groundStep && Vector3.ProjectOnPlane(frames[i].CapsuleA, Vector3.up).magnitude > .6f)
                    throw new InvalidOperationException("The trimmed ladder step retains an unnormalized body origin; correct its root import before baking.");
                if (groundStep) frames[i].Root.y = 0f;
            }
            asset.ElevatedLanding = !groundStep; asset.Frames = frames;
            asset.MaxHeightAdjustment = .15f; asset.MaxPlanarAdjustment = .15f;
            asset.CreateMotion(); EditorUtility.SetDirty(asset);
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
    }

    public static void Inspect(string outputPath)
        => InspectClips(outputPath, new[] { "End_toPlatform", "Down_Start" });

    public static void InspectBottom(string outputPath)
        => InspectClips(outputPath, new[] { "Up_Start" });

    static void InspectClips(string outputPath, string[] names)
    {
        var actor = UnityEngine.Object.FindFirstObjectByType<HumanoidAnimationPrototype>();
        if (actor == null) throw new InvalidOperationException("Open the humanoid review scene first.");
        var records = new System.Collections.Generic.List<object>();
        const string folder = "Assets/Art/Characters/Animations/Ladder/";
        foreach (string name in names)
        {
            string sourcePath = folder + "RootMotion/Traversal_Ladder_Climb_" + name + ".fbx";
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            var sourceClip = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
            var productionClip = AssetDatabase.LoadAllAssetsAtPath(folder + "AnimSeq_Traversal_Ladder_Climb_" + name + ".FBX")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
            var source = UnityEngine.Object.Instantiate(sourcePrefab);
            source.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                using var production = new AuthoredAnimationReference(actor.CharacterPrefab, productionClip, false);
                production.Root.transform.GetChild(0).localScale = Vector3.one;
                production.Root.transform.localScale = actor.transform.localScale;
                var root = source.GetComponentsInChildren<Transform>().First(t => t.name == "root");
                Vector3 start = Vector3.zero;
                var frames = new System.Collections.Generic.List<object>();
                for (int i = 0; i <= 120; i++)
                {
                    float phase = i / 120f;
                    sourceClip.SampleAnimation(source, sourceClip.length * phase);
                    production.Sample(productionClip.length * phase);
                    Vector3 position = source.transform.InverseTransformPoint(root.position);
                    if (i == 0) start = position;
                    Vector3 Bone(HumanBodyBones bone) => production.Root.transform.InverseTransformPoint(production.Animator.GetBoneTransform(bone).position);
                    Vector3 SourceBone(string bone) => source.transform.InverseTransformPoint(source.GetComponentsInChildren<Transform>().First(t => t.name == bone).position);
                    frames.Add(new { phase, seconds = sourceClip.length * phase, root = V(position), delta = V(position - start),
                        sourceHips = V(SourceBone("pelvis")), sourceHead = V(SourceBone("head")),
                        sourceLeftFoot = V(SourceBone("foot_l")), sourceRightFoot = V(SourceBone("foot_r")),
                        sourceLeftHand = V(SourceBone("hand_l")), sourceRightHand = V(SourceBone("hand_r")),
                        rotation = V(root.eulerAngles), hips = V(Bone(HumanBodyBones.Hips)), head = V(Bone(HumanBodyBones.Head)),
                        leftHand = V(Bone(HumanBodyBones.LeftHand)), rightHand = V(Bone(HumanBodyBones.RightHand)),
                        leftFoot = V(Bone(HumanBodyBones.LeftFoot)), rightFoot = V(Bone(HumanBodyBones.RightFoot)) });
                }
                records.Add(new { name, sourcePath, sourceClip.length, sourceTransformScale = V(source.transform.localScale), frames });
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
        File.WriteAllText(outputPath, Newtonsoft.Json.JsonConvert.SerializeObject(records, Newtonsoft.Json.Formatting.Indented));
    }

    static object V(Vector3 value) => new { x = value.x, y = value.y, z = value.z };
}
