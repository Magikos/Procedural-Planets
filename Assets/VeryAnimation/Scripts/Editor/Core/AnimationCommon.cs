using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal static class AnimationCommon
    {
        private static UAnimationUtility UAnimationUtility => s_uAnimationUtility ??= new UAnimationUtility();
        private static UEditorUtility UEditorUtility => s_uEditorUtility ??= new UEditorUtility();
        private static UCurveUtility UCurveUtility => s_uCurveUtility ??= new UCurveUtility();
        private static URotationCurveInterpolation URotationCurveInterpolation => s_uRotationCurveInterpolation ??= URotationCurveInterpolation.CreateInstance();

        private static UAnimationUtility s_uAnimationUtility;
        private static UEditorUtility s_uEditorUtility;
        private static UCurveUtility s_uCurveUtility;
        private static URotationCurveInterpolation s_uRotationCurveInterpolation;

        public static string GetBindingDisplayName(EditorCurveBinding binding) =>
            string.IsNullOrEmpty(binding.path) ? binding.propertyName : $"{binding.path} - {binding.propertyName}";
            
        public static void SetEditorCurves(AnimationClip clip, IReadOnlyDictionary<EditorCurveBinding, AnimationCurve> datas, bool preClear = false)
        {
            if (clip == null || datas == null || datas.Count == 0)
                return;

            var bindings = datas.Keys.ToArray();
            var curves = datas.Values.ToArray();

            if (preClear)
                AnimationUtility.SetEditorCurves(clip, bindings, new AnimationCurve[datas.Count]);

            AnimationUtility.SetEditorCurves(clip, bindings, curves);
        }
        public static void SetObjectReferenceCurves(AnimationClip clip, IReadOnlyDictionary<EditorCurveBinding, ObjectReferenceKeyframe[]> datas, bool preClear = false)
        {
            if (clip == null || datas == null || datas.Count == 0)
                return;

            var bindings = datas.Keys.ToArray();
            var curves = datas.Values.ToArray();

            if (preClear)
                AnimationUtility.SetObjectReferenceCurves(clip, bindings, new ObjectReferenceKeyframe[datas.Count][]);

            AnimationUtility.SetObjectReferenceCurves(clip, bindings, curves);
        }

        public static AnimationClip[] GetUniqueAnimationClips(GameObject gameObject) =>
            AnimationUtility.GetAnimationClips(gameObject).Distinct().ToArray();

        public static UnityEditor.Animations.AnimatorController GetAnimatorController(Animator animator)
        {
            UnityEditor.Animations.AnimatorController ac = null;
            if (animator != null)
            {
                if (animator.runtimeAnimatorController is AnimatorOverrideController owc)
                {
                    ac = owc.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                }
                else
                {
                    ac = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                }
            }
            return ac;
        }

        public static void DisableBehaviors(GameObject root, Predicate<Behaviour> shouldKeep = null)
        {
            if (root == null) return;
            var behaviours = root.GetComponentsInChildren<Behaviour>(true);
            foreach (var b in behaviours)
            {
                if (b == null) continue;
                if (shouldKeep != null ? shouldKeep(b) : b is Animator or Animation)
                    continue;
                b.enabled = false;
            }
        }

        public static void EnableOnlyAnimatorAndAnimation(GameObject root)
        {
            if (root == null) return;
            var behaviours = root.GetComponentsInChildren<Behaviour>(true);
            foreach (var b in behaviours)
            {
                if (b == null) continue;
                b.enabled = b is Animator or Animation;
            }
        }

        private static GameObject InstantiateForPreviewCore(GameObject gameObject)
        {
            var tmpObject = UEditorUtility.InstantiateForAnimatorPreview(gameObject);
            AnimatorUtility.DeoptimizeTransformHierarchy(tmpObject);
            tmpObject.SetActive(true);

            Assert.IsTrue(tmpObject.hideFlags.HasFlag(HideFlags.HideAndDontSave));
            Assert.IsNull(tmpObject.transform.parent);
            tmpObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            tmpObject.transform.localScale = Vector3.one;
            return tmpObject;
        }

        public static GameObject InstantiateForPreviewHasAnimator(GameObject gameObject)
        {
            var tmpObject = InstantiateForPreviewCore(gameObject);
            try
            {
                if (tmpObject.TryGetComponent<Animation>(out var animation))
                {
                    Animation.DestroyImmediate(animation);
                }
                if (!tmpObject.TryGetComponent<Animator>(out _))
                {
                    tmpObject.AddComponent<Animator>();
                }

                EnableOnlyAnimatorAndAnimation(tmpObject);

                return tmpObject;
            }
            catch
            {
                if (tmpObject != null)
                    GameObject.DestroyImmediate(tmpObject);
                throw;
            }
        }
        public static GameObject InstantiateForPreview(GameObject gameObject)
        {
            var tmpObject = InstantiateForPreviewCore(gameObject);
            try
            {
                if (gameObject.TryGetComponent<Animator>(out _))
                {
                    if (tmpObject.TryGetComponent<Animation>(out var animation))
                    {
                        Animation.DestroyImmediate(animation);
                        if (!tmpObject.TryGetComponent<Animator>(out _))
                            tmpObject.AddComponent<Animator>();
                    }
                }
                else if (gameObject.TryGetComponent<Animation>(out _))
                {
                    if (tmpObject.TryGetComponent<Animator>(out var animator))
                    {
                        Animator.DestroyImmediate(animator);
                        if (!tmpObject.TryGetComponent<Animation>(out _))
                            tmpObject.AddComponent<Animation>();
                    }
                }
                else
                {
                    if (tmpObject.TryGetComponent<Animation>(out var animation))
                        Animation.DestroyImmediate(animation);
                    if (tmpObject.TryGetComponent<Animator>(out var animator))
                        Animator.DestroyImmediate(animator);
                }

                EnableOnlyAnimatorAndAnimation(tmpObject);

                return tmpObject;
            }
            catch
            {
                if (tmpObject != null)
                    GameObject.DestroyImmediate(tmpObject);
                throw;
            }
        }

        public static HumanPoseHandler CreateHumanPoseHandler(Animator animator)
        {
            var humanPoseHandler = new HumanPoseHandler(animator.avatar, animator.avatarRoot);
            #region Avoiding Unity's bug
            {
                //Hips You need to call SetHumanPose once if there is a scale in the top. Otherwise, the result of GetHumanPose becomes abnormal.
                var hp = new HumanPose()
                {
                    bodyPosition = new Vector3(0f, 1f, 0f),
                    bodyRotation = Quaternion.identity,
                    muscles = new float[HumanTrait.MuscleCount],
                };
                humanPoseHandler.SetHumanPose(ref hp);
            }
            #endregion
            return humanPoseHandler;
        }

        public static AnimationClip CreateNewClipAtPath(string clipPath)
        {
            var newClip = new AnimationClip();
            {
                var info = AnimationUtility.GetAnimationClipSettings(newClip);
                info.loopTime = true;
                UAnimationUtility.SetAnimationClipSettingsNoDirty(newClip, info);
            }

            var assetClip = AssetDatabase.LoadMainAssetAtPath(clipPath) as AnimationClip;
            if (assetClip != null)
            {
                newClip.name = assetClip.name;
                EditorUtility.CopySerialized(newClip, assetClip);
                AssetDatabase.SaveAssetIfDirty(assetClip);
                AnimationClip.DestroyImmediate(newClip);
                return assetClip;
            }
            else
            {
                AssetDatabase.CreateAsset(newClip, clipPath);
                return newClip;
            }
        }
        public static void ResetAnimationClipSettings(AnimationClip clip)
        {
            if (clip == null)
                return;

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.heightFromFeet = false;
            settings.keepOriginalPositionXZ = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalOrientation = true;
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionXZ = true;
            settings.loopBlendPositionY = true;
            settings.mirror = false;
            settings.loopBlend = false;
            settings.cycleOffset = 0;
            settings.level = 0;
            settings.orientationOffsetY = 0;
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);  //Call before SetAnimationEvents to avoid bugs (may not be reflected later)
        }
        public static void AddMissingTransforms(GameObject gameObject, AnimationClip clip)
        {
            var paths = new HashSet<string>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!string.IsNullOrEmpty(binding.path))
                    paths.Add(binding.path);
            }
            foreach (var path in paths)
            {
                if (gameObject.transform.Find(path) != null)
                    continue;
                var segments = path.Split('/');
                var current = gameObject.transform;
                foreach (var segment in segments)
                {
                    var child = current.Find(segment);
                    if (child == null)
                    {
                        var go = new GameObject(segment);
                        go.transform.SetParent(current, false);
                        child = go.transform;
                    }
                    current = child;
                }
            }
        }
        public static Dictionary<EditorCurveBinding, EditorCurveBinding> ConvertForKeyframeReduction(GameObject tmpObject, AnimationClip tmpClip)
        {
            var otherCurveDic = new Dictionary<EditorCurveBinding, EditorCurveBinding>();

            var bindings = AnimationUtility.GetCurveBindings(tmpClip);
            Dictionary<EditorCurveBinding, AnimationCurve> removeDatas = new(bindings.Length);
            Dictionary<EditorCurveBinding, AnimationCurve> addDatas = new(bindings.Length);
            foreach (var binding in bindings)
            {
                var valueType = AnimationUtility.GetEditorCurveValueType(tmpObject, binding);
                if (binding.type == typeof(Transform) ||
                    binding.type == typeof(Animator))
                {
                    continue;
                }
                else if (valueType != typeof(float))
                {
                    continue;   //To SimpleReductionKeyframe
                }
                var curve = AnimationUtility.GetEditorCurve(tmpClip, binding);
                if (curve == null)
                    continue;
                removeDatas.Add(binding, null);
                var add = new GameObject($"VA_KeyframeReduction_{otherCurveDic.Count}");
                add.transform.SetParent(tmpObject.transform);
                var addBinding = new EditorCurveBinding()
                {
                    type = typeof(Transform),
                    path = AnimationUtility.CalculateTransformPath(add.transform, tmpObject.transform),
                    propertyName = PropertyName.Position[0],
                };
                addDatas.Add(addBinding, curve);
                otherCurveDic.Add(binding, addBinding);
            }
            SetEditorCurves(tmpClip, removeDatas);
            SetEditorCurves(tmpClip, addDatas);

            return otherCurveDic;
        }
        public static Dictionary<EditorCurveBinding, AnimationCurve> ImportByKeyframeReduction(AnimationClip clip, AnimationClip reductionClip, Dictionary<EditorCurveBinding, EditorCurveBinding> otherCurveDic,
                                                                                                bool enableAnimator = true, bool enableAnimatorRootAndIKGoal = true, bool enableTransform = true, bool enableOther = true)
        {
            Dictionary<EditorCurveBinding, AnimationCurve> datas = new();
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                var srcCurve = AnimationUtility.GetEditorCurve(clip, binding);
                if (srcCurve == null) continue;
                var reductionCurve = AnimationUtility.GetEditorCurve(reductionClip, binding);
                if (reductionCurve == null)
                {
                    if (otherCurveDic.TryGetValue(binding, out EditorCurveBinding origBinding))
                        reductionCurve = AnimationUtility.GetEditorCurve(reductionClip, origBinding);
                }
                if (reductionCurve == null) continue;

                if (!enableAnimator && binding.type == typeof(Animator))
                    continue;
                if (enableAnimator && !enableAnimatorRootAndIKGoal && binding.type == typeof(Animator))
                {
                    if (Binding.RootT.Contains(binding) || Binding.RootQ.Contains(binding))
                        continue;
                    if (IsAvatarIKGoalBinding(binding))
                        continue;
                }
                if (!enableTransform && binding.type == typeof(Transform))
                    continue;
                if (!enableOther && (binding.type != typeof(Animator) && binding.type != typeof(Transform)))
                    continue;
                if (srcCurve.length <= reductionCurve.length)
                    continue;
                if (IsRotationQuaternionBinding(binding))
                {
                    #region Quaternion
                    bool allClear = true;
                    for (int dof = 0; dof < 4; dof++)
                    {
                        var subBinding = ChangeDOFIndex(binding, dof);
                        var subSrcCurve = AnimationUtility.GetEditorCurve(clip, subBinding);
                        var subReductionCurve = AnimationUtility.GetEditorCurve(reductionClip, subBinding);
                        if (subReductionCurve == null)
                        {
                            if (otherCurveDic.TryGetValue(subBinding, out EditorCurveBinding origBinding))
                                subReductionCurve = AnimationUtility.GetEditorCurve(reductionClip, origBinding);
                        }
                        if (subSrcCurve == null || subReductionCurve == null ||
                            subSrcCurve.length <= subReductionCurve.length)
                        {
                            allClear = false;
                            break;
                        }
                    }
                    if (!allClear)
                        continue;
                    #endregion
                }
                datas.Add(binding, reductionCurve);
            }
            return datas;
        }

        public static void SetKeyframeTangentModeLinear(AnimationCurve curve, float time) => SetKeyframeTangentModeLinear(curve, FindKeyframeAtTime(curve, time));
        public static void SetKeyframeTangentModeLinear(AnimationCurve curve, int keyIndex)
        {
            if (keyIndex < 0 || keyIndex >= curve.length) return;
            AnimationUtility.SetKeyRightTangentMode(curve, keyIndex, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyLeftTangentMode(curve, keyIndex, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyBroken(curve, keyIndex, false);
            UAnimationUtility.UpdateTangentsFromModeSurrounding(curve, keyIndex);
        }
        public static void SetKeyframeTangentModeClampedAuto(AnimationCurve curve, float time) => SetKeyframeTangentModeClampedAuto(curve, FindKeyframeAtTime(curve, time));
        public static void SetKeyframeTangentModeClampedAuto(AnimationCurve curve, int keyIndex)
        {
            if (keyIndex < 0 || keyIndex >= curve.length) return;
            AnimationUtility.SetKeyLeftTangentMode(curve, keyIndex, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, keyIndex, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyBroken(curve, keyIndex, false);
            UAnimationUtility.UpdateTangentsFromModeSurrounding(curve, keyIndex);
        }
        public static void SetKeyframeTangentFlat(AnimationCurve curve, float time) => SetKeyframeTangentFlat(curve, FindKeyframeAtTime(curve, time));
        public static void SetKeyframeTangentFlat(AnimationCurve curve, int keyIndex)
        {
            if (keyIndex < 0 || keyIndex >= curve.length) return;
            AnimationUtility.SetKeyBroken(curve, keyIndex, false);
            AnimationUtility.SetKeyRightTangentMode(curve, keyIndex, AnimationUtility.TangentMode.Free);
            AnimationUtility.SetKeyLeftTangentMode(curve, keyIndex, AnimationUtility.TangentMode.Free);
            var key = curve.keys[keyIndex];
            key.weightedMode = WeightedMode.None;
            key.inTangent = 0;
            key.outTangent = 0;
            curve.MoveKey(keyIndex, key);
            UAnimationUtility.UpdateTangentsFromModeSurrounding(curve, keyIndex);
        }

        public static void SetAnimationCurveTangent(AnimationCurve curve, Type valueType)
        {
            if (valueType == typeof(bool))
            {
                for (int i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyBroken(curve, i, true);
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                    UAnimationUtility.UpdateTangentsFromModeSurrounding(curve, i);
                }
            }
            else if (valueType == typeof(int))
            {
                for (int i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyBroken(curve, i, true);
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    UAnimationUtility.UpdateTangentsFromModeSurrounding(curve, i);
                }
            }
            else
            {
                for (int i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyBroken(curve, i, false);
                    UAnimationUtility.UpdateTangentsFromModeSurrounding(curve, i);
                }
            }
        }

        private static int BinarySearchKeyframeAt(int count, Func<int, float> getTime, float time)
        {
            if (count <= 0) return -1;
            int begin = 0, end = count - 1;
            while (end - begin > 1)
            {
                var index = begin + Mathf.FloorToInt((end - begin) / 2f);
                if (time < getTime(index))
                {
                    if (end == index) break;
                    end = index;
                }
                else
                {
                    if (begin == index) break;
                    begin = index;
                }
            }
            if (Mathf.Abs(getTime(begin) - time) < EditorCommon.TimeEpsilon)
                return begin;
            if (Mathf.Abs(getTime(end) - time) < EditorCommon.TimeEpsilon)
                return end;
            return -1;
        }
        private static int BinarySearchKeyframeBefore(int count, Func<int, float> getTime, float time, float frameRate)
        {
            time = EditorCommon.SnapToFrame(time, frameRate);

            if (count == 0)
                return -1;
            if (getTime(count - 1) < time)
                return count - 1;
            if (getTime(0) >= time)
                return -1;

            int begin = 0, end = count - 1;
            while (end - begin > 1)
            {
                var index = begin + Mathf.FloorToInt((end - begin) / 2f);
                if (time < getTime(index))
                {
                    if (end == index) break;
                    end = index;
                }
                else
                {
                    if (begin == index) break;
                    begin = index;
                }
            }
            if (Mathf.Abs(getTime(begin) - time) < EditorCommon.TimeEpsilon)
                return begin - 1;
            return begin;
        }
        private static int BinarySearchKeyframeAfter(int count, Func<int, float> getTime, float time, float frameRate)
        {
            time = EditorCommon.SnapToFrame(time, frameRate);

            if (count == 0)
                return -1;
            if (getTime(0) > time)
                return 0;
            if (getTime(count - 1) <= time)
                return -1;

            int begin = 0, end = count - 1;
            while (end - begin > 1)
            {
                var index = begin + Mathf.CeilToInt((end - begin) / 2f);
                if (time > getTime(index))
                {
                    if (begin == index) break;
                    begin = index;
                }
                else
                {
                    if (end == index) break;
                    end = index;
                }
            }
            if (Mathf.Abs(getTime(end) - time) < EditorCommon.TimeEpsilon)
                return end + 1 < count ? end + 1 : -1;
            return end;
        }

        public static int FindKeyframeAtTime(AnimationCurve curve, float time) => BinarySearchKeyframeAt(curve.length, i => curve[i].time, time);
        public static int FindKeyframeAtTime(Keyframe[] keys, float time) => BinarySearchKeyframeAt(keys.Length, i => keys[i].time, time);
        public static int FindKeyframeAtTime(ObjectReferenceKeyframe[] keys, float time) => BinarySearchKeyframeAt(keys.Length, i => keys[i].time, time);
        public static int FindKeyframeAtTime(List<ObjectReferenceKeyframe> keys, float time) => BinarySearchKeyframeAt(keys.Count, i => keys[i].time, time);
        public static int FindKeyframeAtTime(AnimationEvent[] events, float time) => BinarySearchKeyframeAt(events.Length, i => events[i].time, time);
        public static int FindKeyframeIndex(AnimationCurve curve, AnimationCurve findCurve, int findIndex)
        {
            var index = FindKeyframeAtTime(curve, findCurve[findIndex].time);
            if (index >= 0)
            {
                //if(curve[index].Equals(key))  GC Alloc...
                if (Mathf.Approximately(curve[index].time, findCurve[findIndex].time) &&
                    Mathf.Approximately(curve[index].value, findCurve[findIndex].value) &&
                    Mathf.Approximately(curve[index].inTangent, findCurve[findIndex].inTangent) &&
                    Mathf.Approximately(curve[index].outTangent, findCurve[findIndex].outTangent) &&
                    Mathf.Approximately(curve[index].inWeight, findCurve[findIndex].inWeight) &&
                    Mathf.Approximately(curve[index].outWeight, findCurve[findIndex].outWeight) &&
                    curve[index].weightedMode == findCurve[findIndex].weightedMode &&
                    AnimationUtility.GetKeyLeftTangentMode(curve, index) == AnimationUtility.GetKeyLeftTangentMode(findCurve, findIndex) &&
                    AnimationUtility.GetKeyRightTangentMode(curve, index) == AnimationUtility.GetKeyRightTangentMode(findCurve, findIndex))
                {
                    return index;
                }
            }
            return -1;
        }
        public static int FindKeyframeIndexValueOnly(AnimationCurve curve, AnimationCurve findCurve, int findIndex)
        {
            var index = FindKeyframeAtTime(curve, findCurve[findIndex].time);
            if (index >= 0)
            {
                if (Mathf.Approximately(curve[index].time, findCurve[findIndex].time) &&
                    Mathf.Approximately(curve[index].value, findCurve[findIndex].value))
                {
                    return index;
                }
            }
            return -1;
        }
        public static int FindBeforeNearKeyframeAtTime(AnimationCurve curve, float time, float frameRate) => BinarySearchKeyframeBefore(curve.length, i => curve[i].time, time, frameRate);
        public static int FindBeforeNearKeyframeAtTime(ObjectReferenceKeyframe[] keys, float time, float frameRate) => BinarySearchKeyframeBefore(keys.Length, i => keys[i].time, time, frameRate);
        public static int FindBeforeNearKeyframeAtTime(AnimationEvent[] events, float time, float frameRate) => BinarySearchKeyframeBefore(events.Length, i => events[i].time, time, frameRate);
        public static int FindAfterNearKeyframeAtTime(AnimationCurve curve, float time, float frameRate) => BinarySearchKeyframeAfter(curve.length, i => curve[i].time, time, frameRate);
        public static int FindAfterNearKeyframeAtTime(ObjectReferenceKeyframe[] keys, float time, float frameRate) => BinarySearchKeyframeAfter(keys.Length, i => keys[i].time, time, frameRate);
        public static int FindAfterNearKeyframeAtTime(AnimationEvent[] events, float time, float frameRate) => BinarySearchKeyframeAfter(events.Length, i => events[i].time, time, frameRate);

        public static int AddInbetweenKeyframe(AnimationCurve curve, float time)
        {
            var keyIndex = FindKeyframeAtTime(curve, time);
            if (keyIndex >= 0) return -1;
            keyIndex = UAnimationUtility.AddInbetweenKey(curve, time);
            if (keyIndex < 0) return -1;
            UCurveUtility.SetKeyModeFromContext(curve, keyIndex);
            UAnimationUtility.UpdateTangentsFromModeSurrounding(curve, keyIndex);
            return keyIndex;
        }
        public static void RemoveBetweenKeyframe(AnimationCurve curve, float min, float max)
        {
            for (int i = curve.length - 1; i >= 0; i--)
            {
                var t = curve[i].time;
                if (t > min && t < max)
                    curve.RemoveKey(i);
            }
        }
        public static void BakeBetweenKeyframe(AnimationCurve curve, int beginFrame, int endFrame, float frameRate)
        {
            if (beginFrame > endFrame) return;
            var keys = new MiniKeyframe[endFrame - beginFrame + 1];
            for (int frame = beginFrame; frame <= endFrame; frame++)
            {
                var time = EditorCommon.GetFrameTime(frame, frameRate);
                var value = curve.Evaluate(time);
                var index = frame - beginFrame;
                keys[index] = new MiniKeyframe(time, value);
            }
            foreach (var key in keys)
            {
                SetKeyframe(curve, key.time, key.value);
            }
        }

        public static int AddKeyframe(AnimationCurve curve, float time, float value)
        {
            var keyIndex = curve.AddKey(time, value);
            if (keyIndex < 0) return -1;
            UCurveUtility.SetKeyModeFromContext(curve, keyIndex);
            UAnimationUtility.UpdateTangentsFromModeSurrounding(curve, keyIndex);
            return keyIndex;
        }
        public static int SetKeyframe(AnimationCurve curve, float time, float value)
        {
            var keyIndex = FindKeyframeAtTime(curve, time);
            if (keyIndex < 0)
            {
                keyIndex = AddKeyframe(curve, time, value);
            }
            else
            {
                var keyframe = curve[keyIndex];
                keyframe.value = value;
                curve.MoveKey(keyIndex, keyframe);
            }
            return keyIndex;
        }
        public static int SetKeyframe(AnimationCurve curve, Keyframe keyframe)
        {
            var keyIndex = FindKeyframeAtTime(curve, keyframe.time);
            if (keyIndex < 0)
            {
                keyIndex = curve.AddKey(keyframe);
            }
            else
            {
                curve.MoveKey(keyIndex, keyframe);
            }
            return keyIndex;
        }

        public static AnimationClip CreateNewTrimClip(string path, AnimationClip sourceClip, float beginTime, float endTime)
        {
            var newClip = path != null ? CreateNewClipAtPath(path) : new AnimationClip();

            float halfFrameTime = EditorCommon.GetHalfFrameTime(sourceClip.frameRate);

            var settings = AnimationUtility.GetAnimationClipSettings(sourceClip);
            AnimationUtility.SetAnimationClipSettings(newClip, settings);
            newClip.frameRate = sourceClip.frameRate;

            try
            {
                {
                    var bindings = AnimationUtility.GetObjectReferenceCurveBindings(sourceClip);
                    var datas = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>(bindings.Length);
                    var keys = new List<ObjectReferenceKeyframe>();
                    for (int i = 0; i < bindings.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("Trim animation clip - Reference curves", GetBindingDisplayName(bindings[i]), i / (float)bindings.Length);

                        var rkeys = AnimationUtility.GetObjectReferenceCurve(sourceClip, bindings[i]);
                        if (keys.Capacity < rkeys.Length + 1)
                            keys.Capacity = rkeys.Length + 1;
                        keys.Clear();

                        var beginKey = new ObjectReferenceKeyframe();
                        var hasBeginKey = false;
                        foreach (var key in rkeys)
                        {
                            if (key.time <= beginTime + halfFrameTime)
                            {
                                beginKey = key;
                                hasBeginKey = true;
                                continue;
                            }
                            if (!hasBeginKey)
                            {
                                beginKey = key;
                                hasBeginKey = true;
                            }
                            break;
                        }
                        if (hasBeginKey)
                        {
                            beginKey.time = 0f;
                            keys.Add(beginKey);
                        }

                        foreach (var key in rkeys)
                        {
                            if (key.time <= beginTime + halfFrameTime || key.time > endTime + halfFrameTime) continue;
                            var tmp = key;
                            tmp.time = EditorCommon.SnapToFrame(tmp.time - beginTime, sourceClip.frameRate);
                            keys.Add(tmp);
                        }
                        datas.Add(bindings[i], keys.ToArray());
                    }
                    SetObjectReferenceCurves(newClip, datas);
                }

                {
                    var bindings = AnimationUtility.GetCurveBindings(sourceClip);
                    var datas = new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
                    var keys = new List<Keyframe>();
                    for (int i = 0; i < bindings.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("Trim animation clip - Float curves", GetBindingDisplayName(bindings[i]), i / (float)bindings.Length);

                        var curve = AnimationUtility.GetEditorCurve(sourceClip, bindings[i]);
                        {
                            if (keys.Capacity < curve.length + 2)
                                keys.Capacity = curve.length + 2;
                            keys.Clear();
                            for (int j = 0; j < curve.length; j++)
                            {
                                if (curve[j].time < beginTime - halfFrameTime || curve[j].time > endTime + halfFrameTime) continue;
                                var tmp = curve[j];
                                tmp.time = EditorCommon.SnapToFrame(tmp.time - beginTime, sourceClip.frameRate);
                                keys.Add(tmp);
                            }
                            if (keys.FindIndex((x) => Mathf.Approximately(x.time, 0f)) < 0)
                            {
                                keys.Insert(0, new Keyframe(0, curve.Evaluate(beginTime)));
                            }
                            if (keys.FindIndex((x) => Mathf.Approximately(x.time, endTime - beginTime)) < 0)
                            {
                                keys.Add(new Keyframe(endTime - beginTime, curve.Evaluate(endTime)));
                            }
                            curve.keys = keys.ToArray();
                        }
                        datas.Add(bindings[i], curve);
                    }
                    SetEditorCurves(newClip, datas);
                }

                {
                    var events = AnimationUtility.GetAnimationEvents(sourceClip);
                    List<AnimationEvent> newEvents = new(events.Length);
                    for (int i = 0; i < events.Length; i++)
                    {
                        if (events[i].time < beginTime - halfFrameTime || events[i].time > endTime + halfFrameTime) continue;
                        var tmp = events[i];
                        tmp.time = EditorCommon.SnapToFrame(tmp.time - beginTime, sourceClip.frameRate);
                        newEvents.Add(tmp);
                    }
                    AnimationUtility.SetAnimationEvents(newClip, newEvents.ToArray());
                }

                newClip.EnsureQuaternionContinuity();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return newClip;
        }
        public static void SimpleReductionKeyframe(AnimationClip clip, GameObject rootObject)
        {
            {
                var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                var datas = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>(bindings.Length);
                foreach (var binding in bindings)
                {
                    var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    if (curve == null)
                        continue;
                    var keys = new List<ObjectReferenceKeyframe>(curve);
                    bool updated = false;
                    for (int i = 1; i < keys.Count - 1; i++)
                    {
                        if (keys[i - 1].value == keys[i].value)
                        {
                            keys.RemoveAt(i--);
                            updated = true;
                        }
                    }
                    if (updated)
                        datas.Add(binding, keys.ToArray());
                }
                SetObjectReferenceCurves(clip, datas);
            }
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                var datas = new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
                foreach (var binding in bindings)
                {
                    if (binding.type == typeof(Animator) || binding.type == typeof(Transform))
                        continue;
                    var valueType = AnimationUtility.GetEditorCurveValueType(rootObject, binding);
                    if (valueType == null || valueType == typeof(float))
                        continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null)
                        continue;
                    bool updated = false;
                    for (int i = 1; i < curve.length - 1; i++)
                    {
                        if (Mathf.Approximately(curve[i - 1].value, curve[i].value) &&
                            Mathf.Approximately(curve[i + 1].value, curve[i].value))
                        {
                            curve.RemoveKey(i--);
                            updated = true;
                        }
                    }
                    if (updated)
                        datas.Add(binding, curve);
                }
                SetEditorCurves(clip, datas);
            }
        }
        public static bool ConvertToHumanoidClip(AnimationClip clip, GameObject rootObject)
        {
            if (!rootObject.TryGetComponent<Animator>(out var animator))
                return false;
            if (!animator.isHuman)
                return false;

            clip.legacy = false;

            var tmpObject = InstantiateForPreviewHasAnimator(rootObject);
            try
            {
                tmpObject.TryGetComponent<Animator>(out var tmpAnimator);
                Assert.IsNotNull(tmpAnimator);
                tmpAnimator.Rebind();
                using var humanPoseHandler = CreateHumanPoseHandler(tmpAnimator);

                tmpAnimator.applyRootMotion = false;
                tmpAnimator.avatar = null;
                tmpAnimator.Rebind();

                var humanPose = new HumanPose();
                clip.SampleAnimation(tmpObject, 0f);
                humanPoseHandler.GetHumanPose(ref humanPose);

                var lastFrame = EditorCommon.GetLastFrame(clip.length, clip.frameRate);

                var dataRootT = new FloatCurveData[]
                {
                    new(Binding.RootT[0], lastFrame + 1),
                    new(Binding.RootT[1], lastFrame + 1),
                    new(Binding.RootT[2], lastFrame + 1),
                };
                var dataRootQ = new FloatCurveData[]
                {
                    new(Binding.RootQ[0], lastFrame + 1),
                    new(Binding.RootQ[1], lastFrame + 1),
                    new(Binding.RootQ[2], lastFrame + 1),
                    new(Binding.RootQ[3], lastFrame + 1),
                };
                var dataMuscle = new FloatCurveData[HumanTrait.MuscleCount];
                {
                    var musclePropertyName = new MusclePropertyName();
                    for (int i = 0; i < dataMuscle.Length; i++)
                        dataMuscle[i] = new(EditorCurveBinding.FloatCurve("", typeof(Animator), musclePropertyName.PropertyNames[i]), lastFrame + 1);
                }
                var dataIKGoalPositions = new FloatCurveData[AvatarIKGoal.RightHand - AvatarIKGoal.LeftFoot + 1][];
                var dataIKGoalRotations = new FloatCurveData[AvatarIKGoal.RightHand - AvatarIKGoal.LeftFoot + 1][];
                {
                    for (AvatarIKGoal avatarIKGoal = AvatarIKGoal.LeftFoot; avatarIKGoal <= AvatarIKGoal.RightHand; avatarIKGoal++)
                    {
                        dataIKGoalPositions[(int)avatarIKGoal] = new FloatCurveData[3];
                        dataIKGoalRotations[(int)avatarIKGoal] = new FloatCurveData[4];
                        for (int dof = 0; dof < dataIKGoalPositions[(int)avatarIKGoal].Length; dof++)
                            dataIKGoalPositions[(int)avatarIKGoal][dof] = new(EditorCurveBinding.FloatCurve("", typeof(Animator), $"{avatarIKGoal}T.{PropertyName.Dof[dof]}"), lastFrame + 1);
                        for (int dof = 0; dof < dataIKGoalRotations[(int)avatarIKGoal].Length; dof++)
                            dataIKGoalRotations[(int)avatarIKGoal][dof] = new(EditorCurveBinding.FloatCurve("", typeof(Animator), $"{avatarIKGoal}Q.{PropertyName.Dof[dof]}"), lastFrame + 1);
                    }
                }
                Transform[] goalHumanoidBones;
                Quaternion[] goalPostRotations;
                {
                    var avatarIKGoalLength = AvatarIKGoal2HumanoidBone.Length;
                    goalHumanoidBones = new Transform[avatarIKGoalLength];
                    goalPostRotations = new Quaternion[avatarIKGoalLength];
                    var uAvatar = new UAvatar();
                    for (AvatarIKGoal avatarIKGoal = AvatarIKGoal.LeftFoot; avatarIKGoal <= AvatarIKGoal.RightHand; avatarIKGoal++)
                    {
                        var humanoidIndex = AvatarIKGoal2HumanoidBone[(int)avatarIKGoal];
                        var originalBone = animator.GetBoneTransform(humanoidIndex);
                        if (originalBone == null)
                            continue;
                        var path = AnimationUtility.CalculateTransformPath(originalBone, rootObject.transform);
                        goalHumanoidBones[(int)avatarIKGoal] = tmpObject.transform.Find(path);
                        Assert.IsNotNull(goalHumanoidBones[(int)avatarIKGoal]);
                        goalPostRotations[(int)avatarIKGoal] = uAvatar.GetPostRotation(animator.avatar, (int)humanoidIndex);
                    }
                }

                var rootT = tmpObject.transform;

                var previousRootQ = Quaternion.identity;
                var previousGoalQ = new Quaternion[AvatarIKGoal.RightHand - AvatarIKGoal.LeftFoot + 1];

                for (int frame = 0; frame <= lastFrame; frame++)
                {
                    EditorUtility.DisplayProgressBar("ConvertToHumanoidClip", $"{frame} / {lastFrame}", frame / (float)lastFrame);

                    var time = EditorCommon.GetFrameTime(frame, clip.frameRate);
                    clip.SampleAnimation(tmpObject, time);

                    humanPoseHandler.GetHumanPose(ref humanPose);

                    var rootQ = humanPose.bodyRotation;
                    if (frame > 0)
                    {
                        rootQ = FixReverseRotationQuaternion(rootQ, previousRootQ);
                    }
                    previousRootQ = rootQ;

                    for (int dof = 0; dof < dataRootT.Length; dof++)
                        dataRootT[dof].SetKey(time, humanPose.bodyPosition[dof]);
                    for (int dof = 0; dof < dataRootQ.Length; dof++)
                        dataRootQ[dof].SetKey(time, rootQ[dof]);
                    for (int muscle = 0; muscle < dataMuscle.Length; muscle++)
                        dataMuscle[muscle].SetKey(time, humanPose.muscles[muscle]);

                    for (AvatarIKGoal avatarIKGoal = AvatarIKGoal.LeftFoot; avatarIKGoal <= AvatarIKGoal.RightHand; avatarIKGoal++)
                    {
                        if (goalHumanoidBones[(int)avatarIKGoal] == null)
                            continue;
                        goalHumanoidBones[(int)avatarIKGoal].GetPositionAndRotation(out Vector3 ikGoalPosition, out Quaternion ikGoalRotation);
                        ikGoalRotation *= goalPostRotations[(int)avatarIKGoal];
                        if (avatarIKGoal == AvatarIKGoal.LeftFoot || avatarIKGoal == AvatarIKGoal.RightFoot)
                        {
                            Vector3 footBottom = new(avatarIKGoal == AvatarIKGoal.LeftFoot ? animator.leftFeetBottomHeight : animator.rightFeetBottomHeight, 0, 0);
                            ikGoalPosition += (ikGoalRotation * footBottom);
                        }
                        (ikGoalPosition, ikGoalRotation) = CalcAvatarIKGoal(ikGoalPosition, ikGoalRotation, humanPose.bodyPosition, humanPose.bodyRotation, animator.humanScale);

                        if (frame > 0)
                        {
                            ikGoalRotation = FixReverseRotationQuaternion(ikGoalRotation, previousGoalQ[(int)avatarIKGoal]);
                        }
                        previousGoalQ[(int)avatarIKGoal] = ikGoalRotation;

                        for (int dof = 0; dof < dataIKGoalPositions[(int)avatarIKGoal].Length; dof++)
                            dataIKGoalPositions[(int)avatarIKGoal][dof].SetKey(time, ikGoalPosition[dof]);
                        for (int dof = 0; dof < dataIKGoalRotations[(int)avatarIKGoal].Length; dof++)
                            dataIKGoalRotations[(int)avatarIKGoal][dof].SetKey(time, ikGoalRotation[dof]);
                    }
                }

                EditorUtility.DisplayProgressBar("Export animation clip - Humanoid curves", "Set", 1f);

                var datas = new Dictionary<EditorCurveBinding, AnimationCurve>();

                // Remove humanoid conflict
                {
                    HashSet<string> humanoidConflictPath = new()
                    {
                        AnimationUtility.CalculateTransformPath(animator.transform, rootObject.transform)
                    };
                    for (HumanBodyBones hi = 0; hi < HumanBodyBones.LastBone; hi++)
                    {
                        var bone = animator.GetBoneTransform(hi);
                        if (bone == null)
                            continue;
                        do
                        {
                            humanoidConflictPath.Add(AnimationUtility.CalculateTransformPath(bone, rootObject.transform));
                            bone = bone.parent;
                        } while (bone != null && bone != animator.transform);
                    }

                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type == typeof(Transform) &&
                            humanoidConflictPath.Contains(binding.path))
                        {
                            datas.Add(binding, null);
                        }
                    }

                    SetEditorCurves(clip, datas);
                    datas.Clear();
                }

                {
                    static void AddCurveWithTangent(Dictionary<EditorCurveBinding, AnimationCurve> datas, EditorCurveBinding binding, AnimationCurve curve, Type valueType)
                    {
                        EditorUtility.DisplayProgressBar("Export animation clip - Humanoid curves", GetBindingDisplayName(binding), 1f);

                        if (curve != null)
                            SetAnimationCurveTangent(curve, valueType);
                        datas.Add(binding, curve);
                    }

                    for (int dof = 0; dof < dataRootT.Length; dof++)
                        AddCurveWithTangent(datas, dataRootT[dof].Binding, dataRootT[dof].CreateAnimationCurve(), typeof(float));
                    for (int dof = 0; dof < dataRootQ.Length; dof++)
                        AddCurveWithTangent(datas, dataRootQ[dof].Binding, dataRootQ[dof].CreateAnimationCurve(), typeof(float));
                    for (int muscle = 0; muscle < dataMuscle.Length; muscle++)
                        AddCurveWithTangent(datas, dataMuscle[muscle].Binding, dataMuscle[muscle].CreateAnimationCurve(), typeof(float));
                    for (AvatarIKGoal avatarIKGoal = AvatarIKGoal.LeftFoot; avatarIKGoal <= AvatarIKGoal.RightHand; avatarIKGoal++)
                    {
                        if (goalHumanoidBones[(int)avatarIKGoal] == null)
                            continue;
                        for (int dof = 0; dof < dataIKGoalPositions[(int)avatarIKGoal].Length; dof++)
                            AddCurveWithTangent(datas, dataIKGoalPositions[(int)avatarIKGoal][dof].Binding, dataIKGoalPositions[(int)avatarIKGoal][dof].CreateAnimationCurve(), typeof(float));
                        for (int dof = 0; dof < dataIKGoalRotations[(int)avatarIKGoal].Length; dof++)
                            AddCurveWithTangent(datas, dataIKGoalRotations[(int)avatarIKGoal][dof].Binding, dataIKGoalRotations[(int)avatarIKGoal][dof].CreateAnimationCurve(), typeof(float));
                    }
                    SetEditorCurves(clip, datas);
                }

                clip.EnsureQuaternionContinuity();
            }
            finally
            {
                GameObject.DestroyImmediate(tmpObject);
                EditorUtility.ClearProgressBar();
            }

            return true;
        }
        public static void ConvertToRawEuler(AnimationClip clip)
        {
            try
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                {
                    List<EditorCurveBinding> convertBindings = new(bindings.Length);
                    for (int i = 0; i < bindings.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("Export animation clip - ConvertToRawEuler", GetBindingDisplayName(bindings[i]), i / (float)bindings.Length);

                        if (bindings[i].type != typeof(Transform) ||
                            !bindings[i].propertyName.StartsWith(URotationCurveInterpolation.PrefixForInterpolation[(int)URotationCurveInterpolation.Mode.RawQuaternions], StringComparison.Ordinal))
                            continue;
                        if (convertBindings.FindIndex((x) => x.path == bindings[i].path) < 0)
                        {
                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                            {
                                var binding = bindings[i];
                                binding.propertyName = PropertyName.Rotation[(int)URotationCurveInterpolation.Mode.Baked][dofIndex];
                                convertBindings.Add(binding);
                            }
                            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                            {
                                var binding = bindings[i];
                                binding.propertyName = PropertyName.Rotation[(int)URotationCurveInterpolation.Mode.NonBaked][dofIndex];
                                convertBindings.Add(binding);
                            }
                        }
                    }
                    if (convertBindings.Count > 0)
                        URotationCurveInterpolation.SetInterpolation(clip, convertBindings.ToArray(), URotationCurveInterpolation.Mode.RawEuler);
                }
                #region FixReverseRotation
                bindings = AnimationUtility.GetCurveBindings(clip);
                {
                    var datas = new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
                    for (int i = 0; i < bindings.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("Export animation clip - ConvertToRawEuler", GetBindingDisplayName(bindings[i]), i / (float)bindings.Length);

                        if (!bindings[i].propertyName.StartsWith(URotationCurveInterpolation.PrefixForInterpolation[(int)URotationCurveInterpolation.Mode.RawEuler], StringComparison.Ordinal)) continue;
                        var curve = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                        FixReverseRotationEuler(curve);
                        SetAnimationCurveTangent(curve, typeof(float));
                        datas.Add(bindings[i], curve);
                    }
                    SetEditorCurves(clip, datas);
                }
                #endregion

                clip.EnsureQuaternionContinuity();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        public static bool TransferRootMotionToRootNodeTransform(AnimationClip clip, GameObject rootObject, GameObject rootMotionBone)
        {
            if (rootMotionBone == rootObject)
                return false;
            var tmpObject = InstantiateForPreviewHasAnimator(rootObject);
            try
            {
                if (tmpObject.TryGetComponent<Animator>(out var tmpAnimator))
                {
                    tmpAnimator.applyRootMotion = false;
                    tmpAnimator.avatar = null;
                    tmpAnimator.Rebind();
                }
                var genericRootMotionBonePath = AnimationUtility.CalculateTransformPath(rootMotionBone.transform, rootObject.transform);
                var rootActiveBinding = Binding.Active(genericRootMotionBonePath);
                var tmpRootMotionBone = AnimationUtility.GetAnimatedObject(tmpObject, rootActiveBinding) as GameObject;
                if (tmpRootMotionBone == null)
                    return false;

                var lastFrame = EditorCommon.GetLastFrame(clip.length, clip.frameRate);

                var dataRootT = new FloatCurveData[]
                {
                    new(Binding.RootT[0], lastFrame + 1),
                    new(Binding.RootT[1], lastFrame + 1),
                    new(Binding.RootT[2], lastFrame + 1),
                };
                var dataRootQ = new FloatCurveData[]
                {
                    new(Binding.RootQ[0], lastFrame + 1),
                    new(Binding.RootQ[1], lastFrame + 1),
                    new(Binding.RootQ[2], lastFrame + 1),
                    new(Binding.RootQ[3], lastFrame + 1),
                };
                var dataRootNodePosition = new FloatCurveData[]
                {
                    new(EditorCurveBinding.FloatCurve(genericRootMotionBonePath, typeof(Transform), PropertyName.Position[0]), lastFrame + 1),
                    new(EditorCurveBinding.FloatCurve(genericRootMotionBonePath, typeof(Transform), PropertyName.Position[1]), lastFrame + 1),
                    new(EditorCurveBinding.FloatCurve(genericRootMotionBonePath, typeof(Transform), PropertyName.Position[2]), lastFrame + 1),
                };
                var dataRootNodeRotation = new FloatCurveData[]
                {
                    new(EditorCurveBinding.FloatCurve(genericRootMotionBonePath, typeof(Transform), PropertyName.RotationQuaternion[0]), lastFrame + 1),
                    new(EditorCurveBinding.FloatCurve(genericRootMotionBonePath, typeof(Transform), PropertyName.RotationQuaternion[1]), lastFrame + 1),
                    new(EditorCurveBinding.FloatCurve(genericRootMotionBonePath, typeof(Transform), PropertyName.RotationQuaternion[2]), lastFrame + 1),
                    new(EditorCurveBinding.FloatCurve(genericRootMotionBonePath, typeof(Transform), PropertyName.RotationQuaternion[3]), lastFrame + 1),
                };

                var rootT = tmpObject.transform;
                var rootNodeT = tmpRootMotionBone.transform;

                clip.SampleAnimation(tmpObject, 0);
                rootT.GetPositionAndRotation(out var startRootPosition, out var startRootRotation);
                rootNodeT.GetLocalPositionAndRotation(out var startRootNodeLocalPosition, out var startRootNodeLocalRotation);

                var previousRootQ = startRootRotation;
                var previousRootNodeLocalRotation = startRootNodeLocalRotation;
                for (int frame = 0; frame <= lastFrame; frame++)
                {
                    EditorUtility.DisplayProgressBar("TransferRootMotionToRootNodeTransform", $"{frame} / {lastFrame}", frame / (float)lastFrame);

                    var time = EditorCommon.GetFrameTime(frame, clip.frameRate);

                    //Reset
                    rootT.SetPositionAndRotation(startRootPosition, startRootRotation);
                    rootNodeT.SetLocalPositionAndRotation(startRootNodeLocalPosition, startRootNodeLocalRotation);

                    clip.SampleAnimation(tmpObject, time);

                    rootT.GetPositionAndRotation(out var rootTValue, out var rootQValue);
                    rootNodeT.GetPositionAndRotation(out var rootNodePosition, out var rootNodeRotation);
                    rootT.SetPositionAndRotation(startRootPosition, startRootRotation);
                    rootNodeT.SetPositionAndRotation(rootNodePosition, rootNodeRotation);
                    rootNodeT.GetLocalPositionAndRotation(out var rootNodeLocalPosition, out var rootNodeLocalRotation);

                    rootQValue = FixReverseRotationQuaternion(rootQValue, previousRootQ);
                    previousRootQ = rootQValue;
                    rootNodeLocalRotation = FixReverseRotationQuaternion(rootNodeLocalRotation, previousRootNodeLocalRotation);
                    previousRootNodeLocalRotation = rootNodeLocalRotation;

                    for (int dof = 0; dof < dataRootT.Length; dof++)
                        dataRootT[dof].SetKey(time, rootTValue[dof]);
                    for (int dof = 0; dof < dataRootQ.Length; dof++)
                        dataRootQ[dof].SetKey(time, rootQValue[dof]);
                    for (int dof = 0; dof < dataRootNodePosition.Length; dof++)
                        dataRootNodePosition[dof].SetKey(time, rootNodeLocalPosition[dof]);
                    for (int dof = 0; dof < dataRootNodeRotation.Length; dof++)
                        dataRootNodeRotation[dof].SetKey(time, rootNodeLocalRotation[dof]);
                }

                var datas = new Dictionary<EditorCurveBinding, AnimationCurve>();

                // Remove root motion conflict
                {
                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    foreach (var binding in bindings)
                    {
                        if (binding.type == typeof(Transform) &&
                            binding.path == "")
                        {
                            datas.Add(binding, null);
                        }
                    }
                }

                {
                    static void AddCurveWithTangent(Dictionary<EditorCurveBinding, AnimationCurve> datas, EditorCurveBinding binding, AnimationCurve curve, Type valueType)
                    {
                        EditorUtility.DisplayProgressBar("TransferRootMotionToRootNodeTransform", GetBindingDisplayName(binding), 1f);

                        if (curve != null)
                            SetAnimationCurveTangent(curve, valueType);
                        datas.Add(binding, curve);
                    }

                    for (int dof = 0; dof < dataRootT.Length; dof++)
                        AddCurveWithTangent(datas, dataRootT[dof].Binding, dataRootT[dof].CreateAnimationCurve(), typeof(float));
                    for (int dof = 0; dof < dataRootQ.Length; dof++)
                        AddCurveWithTangent(datas, dataRootQ[dof].Binding, dataRootQ[dof].CreateAnimationCurve(), typeof(float));
                    for (int dof = 0; dof < dataRootNodePosition.Length; dof++)
                        AddCurveWithTangent(datas, dataRootNodePosition[dof].Binding, dataRootNodePosition[dof].CreateAnimationCurve(), typeof(float));
                    for (int dof = 0; dof < dataRootNodeRotation.Length; dof++)
                        AddCurveWithTangent(datas, dataRootNodeRotation[dof].Binding, dataRootNodeRotation[dof].CreateAnimationCurve(), typeof(float));

                    SetEditorCurves(clip, datas);
                }

                clip.EnsureQuaternionContinuity();
            }
            finally
            {
                GameObject.DestroyImmediate(tmpObject);
                EditorUtility.ClearProgressBar();
            }

            return true;
        }
        public static void BakeKeyframesForFbxExport(AnimationClip clip)
        {
            var lastFrame = EditorCommon.GetLastFrame(clip.length, clip.frameRate);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var datas = new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
            try
            {
                for (int i = 0; i < bindings.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("FullBakeKeyframes", GetBindingDisplayName(bindings[i]), i / (float)bindings.Length);

                    var curve = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                    var keys = new Keyframe[lastFrame + 1];
                    for (int frame = 0; frame <= lastFrame; frame++)
                    {
                        var time = EditorCommon.GetFrameTime(frame, clip.frameRate);
                        keys[frame] = new Keyframe(time, curve.Evaluate(time));

                        //The FBX exporter can produce abnormal results when tangentMode == 0, so I am forcing an override.
                        UAnimationUtility.SetKeyLeftTangentMode(ref keys[frame], AnimationUtility.TangentMode.Linear);
                        UAnimationUtility.SetKeyRightTangentMode(ref keys[frame], AnimationUtility.TangentMode.Linear);
                        UAnimationUtility.SetKeyBroken(ref keys[frame], false);
                    }
                    datas.Add(bindings[i], new AnimationCurve(keys));
                }
                SetEditorCurves(clip, datas);

                clip.EnsureQuaternionContinuity();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        public static bool RemoveStartOffset(AnimationClip clip, Vector3? offsetPosition = null, Quaternion? offsetRotation = null, Vector3? offsetScale = null)
        {
            var positionBindings = new EditorCurveBinding[]
            {
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), PropertyName.Position[0]),
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), PropertyName.Position[1]),
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), PropertyName.Position[2]),
            };
            var quaternionBindings = new EditorCurveBinding[]
            {
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), PropertyName.RotationQuaternion[0]),
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), PropertyName.RotationQuaternion[1]),
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), PropertyName.RotationQuaternion[2]),
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), PropertyName.RotationQuaternion[3]),
            };
            var scaleBindings = new EditorCurveBinding[]
            {
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), PropertyName.Scale[0]),
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), PropertyName.Scale[1]),
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), PropertyName.Scale[2]),
            };
            var positionCurves = new AnimationCurve[]
            {
                AnimationUtility.GetEditorCurve(clip, positionBindings[0]),
                AnimationUtility.GetEditorCurve(clip, positionBindings[1]),
                AnimationUtility.GetEditorCurve(clip, positionBindings[2]),
            };
            var quaternionCurves = new AnimationCurve[]
            {
                AnimationUtility.GetEditorCurve(clip, quaternionBindings[0]),
                AnimationUtility.GetEditorCurve(clip, quaternionBindings[1]),
                AnimationUtility.GetEditorCurve(clip, quaternionBindings[2]),
                AnimationUtility.GetEditorCurve(clip, quaternionBindings[3]),
            };
            var scaleCurves = new AnimationCurve[]
            {
                AnimationUtility.GetEditorCurve(clip, scaleBindings[0]),
                AnimationUtility.GetEditorCurve(clip, scaleBindings[1]),
                AnimationUtility.GetEditorCurve(clip, scaleBindings[2]),
            };
            if (positionCurves.Contains(null) || quaternionCurves.Contains(null))
                return false;

            try
            {
                var lastFrame = EditorCommon.GetLastFrame(clip.length, clip.frameRate);
                var newPositionKeys = new MiniKeyframeList[positionCurves.Length];
                for (int i = 0; i < positionCurves.Length; i++)
                    newPositionKeys[i] = new MiniKeyframeList(lastFrame + 1);
                var newQuaternionKeys = new MiniKeyframeList[quaternionCurves.Length];
                for (int i = 0; i < quaternionCurves.Length; i++)
                    newQuaternionKeys[i] = new MiniKeyframeList(lastFrame + 1);
                var newScaleKeys = new MiniKeyframeList[scaleCurves.Length];
                for (int i = 0; i < scaleCurves.Length; i++)
                    newScaleKeys[i] = new MiniKeyframeList(lastFrame + 1);

                static float RemoveScaleOffset(float value, float startValue)
                {
                    return Mathf.Approximately(startValue, 0f) ? value : value / startValue;
                }

                var rotation = offsetRotation ?? EvaluateQuaternionNormalized(quaternionCurves, 0f);
                var startScale = offsetScale ?? EvaluateVector3(scaleCurves, 0f, Vector3.one);
                var worldToLocalMatrix = Matrix4x4.TRS(
                    offsetPosition ?? EvaluateVector3(positionCurves, 0f),
                    rotation,
                    startScale
                ).inverse;
                var startRotationInverse = Quaternion.Inverse(rotation);
                startRotationInverse.Normalize();

                for (int frame = 0; frame <= lastFrame; frame++)
                {
                    EditorUtility.DisplayProgressBar("RemoveStartOffset", $"{frame} / {lastFrame}", frame / (float)lastFrame);

                    var time = EditorCommon.GetFrameTime(frame, clip.frameRate);

                    var position = EvaluateVector3(positionCurves, time);
                    var quaternion = EvaluateQuaternionNormalized(quaternionCurves, time);
                    var scale = EvaluateVector3(scaleCurves, time, Vector3.one);

                    position = worldToLocalMatrix.MultiplyPoint(position);
                    quaternion = (startRotationInverse * quaternion).normalized;
                    scale = new Vector3(
                        RemoveScaleOffset(scale.x, startScale.x),
                        RemoveScaleOffset(scale.y, startScale.y),
                        RemoveScaleOffset(scale.z, startScale.z)
                    );

                    for (int i = 0; i < newPositionKeys.Length; i++)
                        newPositionKeys[i].SetKey(time, position[i]);
                    for (int i = 0; i < newQuaternionKeys.Length; i++)
                        newQuaternionKeys[i].SetKey(time, quaternion[i]);
                    for (int i = 0; i < newScaleKeys.Length; i++)
                    {
                        if (scaleCurves[i] != null)
                            newScaleKeys[i].SetKey(time, scale[i]);
                    }
                }

                var datas = new Dictionary<EditorCurveBinding, AnimationCurve>();

                {
                    for (int i = 0; i < newPositionKeys.Length; i++)
                        datas.Add(positionBindings[i], null);
                    for (int i = 0; i < newQuaternionKeys.Length; i++)
                        datas.Add(quaternionBindings[i], null);
                    for (int i = 0; i < newScaleKeys.Length; i++)
                    {
                        if (scaleCurves[i] != null)
                            datas.Add(scaleBindings[i], null);
                    }

                    SetEditorCurves(clip, datas);
                    datas.Clear();
                }
                {
                    static void AddCurveWithTangent(Dictionary<EditorCurveBinding, AnimationCurve> datas, EditorCurveBinding binding, AnimationCurve curve, Type valueType)
                    {
                        EditorUtility.DisplayProgressBar("RemoveStartOffset", GetBindingDisplayName(binding), 1f);

                        if (curve != null)
                            SetAnimationCurveTangent(curve, valueType);
                        datas.Add(binding, curve);
                    }

                    for (int i = 0; i < newPositionKeys.Length; i++)
                        AddCurveWithTangent(datas, positionBindings[i], newPositionKeys[i].CreateAnimationCurve(), typeof(float));
                    for (int i = 0; i < newQuaternionKeys.Length; i++)
                        AddCurveWithTangent(datas, quaternionBindings[i], newQuaternionKeys[i].CreateAnimationCurve(), typeof(float));
                    for (int i = 0; i < newScaleKeys.Length; i++)
                    {
                        if (scaleCurves[i] != null)
                            AddCurveWithTangent(datas, scaleBindings[i], newScaleKeys[i].CreateAnimationCurve(), typeof(float));
                    }

                    SetEditorCurves(clip, datas);
                }

                clip.EnsureQuaternionContinuity();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return true;
        }

        public static (Vector3, Quaternion) CalcAvatarIKGoal(Vector3 ikGoalPosition, Quaternion ikGoalRotation, Vector3 bodyPosition, Quaternion bodyRotation, float humanScale)
        {
            Quaternion invRootQ = Quaternion.Inverse(bodyRotation);
#if false   //While there is some information suggesting this is correct, it will not be used in this asset because the results are incorrect
            ikGoalPosition = invRootQ * (ikGoalPosition - bodyPosition);
            ikGoalPosition *= humanScale;
#else
            ikGoalPosition = invRootQ * (ikGoalPosition - bodyPosition * humanScale);
            ikGoalPosition *= 1f / humanScale;
#endif
            ikGoalRotation = invRootQ * ikGoalRotation;
            return (ikGoalPosition, ikGoalRotation);
        }

        public static Vector3 EvaluateVector3(AnimationCurve[] curves, float time, Vector3? fallBack = null)
        {
            Vector3 result = fallBack ?? Vector3.zero;
            for (int i = 0; i < 3; i++)
                if (curves[i] != null) result[i] = curves[i].Evaluate(time);
            return result;
        }
        public static Quaternion EvaluateQuaternionNormalized(AnimationCurve[] curves, float time, Quaternion? fallBack = null)
        {
            var result = fallBack.HasValue ? new Vector4(fallBack.Value.x, fallBack.Value.y, fallBack.Value.z, fallBack.Value.w) : new Vector4(0, 0, 0, 1);
            for (int i = 0; i < 4; i++)
            {
                if (curves[i] != null)
                    result[i] = curves[i].Evaluate(time);
            }
            result.Normalize();
            if (result.sqrMagnitude > 0f)
                return new Quaternion(result.x, result.y, result.z, result.w);
            return Quaternion.identity;
        }

        public static Quaternion FixReverseRotationQuaternion(AnimationCurve[] curves, float time, Quaternion rotation, float frameRate)
        {
            var beforeTime = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (curves[i] == null) continue;
                var index = FindBeforeNearKeyframeAtTime(curves[i], time, frameRate);
                if (index >= 0)
                    beforeTime = Mathf.Max(beforeTime, curves[i][index].time);
            }
            var beforeRotation = EvaluateQuaternionNormalized(curves, beforeTime);
            return FixReverseRotationQuaternion(rotation, beforeRotation);
        }
        public static Quaternion FixReverseRotationQuaternion(Quaternion rotation, Quaternion beforeRotation)
        {
            var rot = rotation * Quaternion.Inverse(beforeRotation);
            if (rot.w < 0f)
            {
                for (int i = 0; i < 4; i++)
                    rotation[i] = -rotation[i];
            }
            return rotation;
        }
        public static Vector3 FixReverseRotationEuler(AnimationCurve[] curves, float time, Vector3 eulerAngles, float frameRate)
        {
            Vector3 beforeEulerAngles = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                if (curves[i] != null)
                {
                    var beforeTime = 0f;
                    {
                        var index = FindBeforeNearKeyframeAtTime(curves[i], time, frameRate);
                        if (index >= 0)
                            beforeTime = Mathf.Max(beforeTime, curves[i][index].time);
                    }
                    beforeEulerAngles[i] = curves[i].Evaluate(beforeTime);
                }
            }
            return FixReverseRotationEuler(eulerAngles, beforeEulerAngles);
        }
        public static Vector3 FixReverseRotationEuler(Vector3 eulerAngles, Vector3 beforeEulerAngles)
        {
            for (int i = 0; i < 3; i++)
            {
                while (Mathf.Abs(eulerAngles[i] - beforeEulerAngles[i]) > 180f)
                {
                    var beforeValue = eulerAngles[i];
                    if (beforeEulerAngles[i] < eulerAngles[i])
                        eulerAngles[i] -= 360f;
                    else
                        eulerAngles[i] += 360f;
                    if (eulerAngles[i] == beforeValue)
                        break;
                }
            }
            return eulerAngles;
        }
        public static bool FixReverseRotationEuler(AnimationCurve curve)
        {
            bool updated = false;
            for (int i = 1; i < curve.length; i++)
            {
                var keyframe = curve[i];
                if (Mathf.Abs(keyframe.value - curve[i - 1].value) <= 180f)
                    continue;
                while (Mathf.Abs(keyframe.value - curve[i - 1].value) > 180f)
                {
                    var beforeValue = keyframe.value;
                    if (keyframe.value < curve[i - 1].value)
                        keyframe.value += 360f;
                    else
                        keyframe.value -= 360f;
                    if (keyframe.value == beforeValue)
                        break;
                }
                curve.MoveKey(i, keyframe);
                updated = true;
            }
            return updated;
        }

        public static bool IsBlendShapePropertyName(string name) => name.StartsWith("blendShape.", StringComparison.Ordinal);
        public static string BlendShapeName2PropertyName(string name) => $"blendShape.{name}";
        public static string PropertyName2BlendShapeName(string name) => name["blendShape.".Length..];

        public static EditorCurveBinding ChangeDOFIndex(EditorCurveBinding binding, int dofIndex)
        {
            binding.propertyName = binding.propertyName[..^PropertyName.DotDof[dofIndex].Length];
            binding.propertyName += PropertyName.DotDof[dofIndex];
            return binding;
        }
        public static int GetDOFIndex(in EditorCurveBinding binding)
        {
            for (int i = 0; i < PropertyName.DotDof.Length; i++)
            {
                if (binding.propertyName.EndsWith(PropertyName.DotDof[i], StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        public static bool IsActiveBinding(in EditorCurveBinding binding)
        {
            return !binding.isPPtrCurve &&
                    binding.type == typeof(GameObject) &&
                    binding.propertyName == "m_IsActive";
        }

        public static bool IsRotationQuaternionBinding(in EditorCurveBinding binding)
        {
            if (binding.type == typeof(Animator))
            {
                for (int dofIndex = 0; dofIndex < 4; dofIndex++)
                {
                    if (binding == Binding.RootQ[dofIndex])
                        return true;
                }
                for (int dofIndex = 0; dofIndex < 4; dofIndex++)
                {
                    if (binding == Binding.MotionQ[dofIndex])
                        return true;
                }
                foreach (var names in PropertyName.AvatarIKGoalQ)
                {
                    foreach (var name in names)
                    {
                        if (binding.propertyName == name)
                            return true;
                    }
                }
            }
            else if (binding.type == typeof(Transform))
            {
                for (int dofIndex = 0; dofIndex < 4; dofIndex++)
                {
                    if (binding.propertyName == PropertyName.RotationQuaternion[dofIndex])
                        return true;
                }
            }
            return false;
        }

        public static bool IsAvatarIKGoalBinding(in EditorCurveBinding binding)
        {
            if (binding.type != typeof(Animator))
                return false;

            var propertyName = binding.propertyName;

            foreach (var ikGoalT in PropertyName.AvatarIKGoalT)
            {
                if (ikGoalT.Contains(propertyName))
                    return true;
            }

            foreach (var ikGoalQ in PropertyName.AvatarIKGoalQ)
            {
                if (ikGoalQ.Contains(propertyName))
                    return true;
            }

            return false;
        }

        internal struct MiniKeyframe
        {
            public MiniKeyframe(float time, float value)
            {
                this.time = time;
                this.value = value;
            }
            public readonly Keyframe Key => new(time, value);

            public float time;
            public float value;
        }
        internal class MiniKeyframeList
        {
            public MiniKeyframeList()
            {
                keys = new List<MiniKeyframe>();
            }
            public MiniKeyframeList(int capacity)
            {
                keys = new List<MiniKeyframe>(capacity);
            }

            private readonly List<MiniKeyframe> keys;

            public void SetKey(float time, float value)
            {
                if (keys.Count <= 1)
                {
                    keys.Add(new MiniKeyframe(time, value));
                }
                else
                {
                    if (Mathf.Approximately(keys[^2].value, value) &&
                        Mathf.Approximately(keys[^1].value, value))
                    {
                        var key = keys[^1];
                        key.time = time;
                        keys[^1] = key;
                    }
                    else
                    {
                        keys.Add(new MiniKeyframe(time, value));
                    }
                }
            }
            public void Clear() => keys.Clear();
            public AnimationCurve CreateAnimationCurve()
            {
                var newKeys = new Keyframe[keys.Count];
                for (int i = 0; i < keys.Count; i++)
                {
                    newKeys[i] = keys[i].Key;
                }
                return new(newKeys);
            }
        }
        internal struct MiniObjectReferenceKeyframe
        {
            public MiniObjectReferenceKeyframe(float time, UnityEngine.Object value)
            {
                this.time = time;
                this.value = value;
            }
            public readonly ObjectReferenceKeyframe Key => new() { time = time, value = value };

            public float time;
            public UnityEngine.Object value;
        }
        internal class MiniObjectReferenceKeyframeList
        {
            public MiniObjectReferenceKeyframeList()
            {
                keys = new List<MiniObjectReferenceKeyframe>();
            }
            public MiniObjectReferenceKeyframeList(int capacity)
            {
                keys = new List<MiniObjectReferenceKeyframe>(capacity);
            }

            private readonly List<MiniObjectReferenceKeyframe> keys;

            public void SetKey(float time, UnityEngine.Object value)
            {
                if (keys.Count <= 1)
                {
                    keys.Add(new MiniObjectReferenceKeyframe(time, value));
                }
                else
                {
                    if (keys[^2].value == value &&
                        keys[^1].value == value)
                    {
                        var key = keys[^1];
                        key.time = time;
                        keys[^1] = key;
                    }
                    else
                    {
                        keys.Add(new MiniObjectReferenceKeyframe(time, value));
                    }
                }
            }
            public void Clear() => keys.Clear();
            public ObjectReferenceKeyframe[] CreateObjectReferenceKeyframes()
            {
                var newKeys = new ObjectReferenceKeyframe[keys.Count];
                for (int i = 0; i < keys.Count; i++)
                {
                    newKeys[i] = keys[i].Key;
                }
                return newKeys;
            }
        }
        internal class FloatCurveData
        {
            public EditorCurveBinding Binding { get; private set; }
            
            private readonly MiniKeyframeList keys;

            public FloatCurveData(EditorCurveBinding binding, int capacity)
            {
                this.Binding = binding;
                this.keys = new MiniKeyframeList(capacity);
            }

            public void SetKey(float time, float value) => keys.SetKey(time, value);
            public void Clear() => keys.Clear();
            public AnimationCurve CreateAnimationCurve() => keys.CreateAnimationCurve();
        }

        public static class PropertyName
        {
            public static readonly string[] Dof =
            {
                "x", "y", "z", "w"
            };
            public static readonly string[] DotDof =
            {
                ".x", ".y", ".z", ".w"
            };
            public static readonly string[] Position =
            {
                "m_LocalPosition.x",
                "m_LocalPosition.y",
                "m_LocalPosition.z",
            };
            public static readonly string[] Scale =
            {
                "m_LocalScale.x",
                "m_LocalScale.y",
                "m_LocalScale.z",
            };
            public static readonly string[][] Rotation = //URotationCurveInterpolation.Mode
            {
                new string[] { "localEulerAnglesBaked.x", "localEulerAnglesBaked.y", "localEulerAnglesBaked.z" }, 
                new string[] { "localEulerAngles.x", "localEulerAngles.y", "localEulerAngles.z" },
                new string[] { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" },
                new string[] { "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z" },
                new string[] { null },
            };
            public static string[] RotationQuaternion => Rotation[(int)URotationCurveInterpolation.Mode.RawQuaternions];
            public static string[] RotationEuler => Rotation[(int)URotationCurveInterpolation.Mode.RawEuler];

            public static string[][] AvatarIKGoalT =
            {
                new string[] { "LeftFootT.x", "LeftFootT.y", "LeftFootT.z" },
                new string[] { "RightFootT.x", "RightFootT.y", "RightFootT.z" },
                new string[] { "LeftHandT.x", "LeftHandT.y", "LeftHandT.z" },
                new string[] { "RightHandT.x", "RightHandT.y", "RightHandT.z" },
            };
            public static string[][] AvatarIKGoalQ =
            {
                new string[] { "LeftFootQ.x", "LeftFootQ.y", "LeftFootQ.z", "LeftFootQ.w" },
                new string[] { "RightFootQ.x", "RightFootQ.y", "RightFootQ.z", "RightFootQ.w" },
                new string[] { "LeftHandQ.x", "LeftHandQ.y", "LeftHandQ.z", "LeftHandQ.w" },
                new string[] { "RightHandQ.x", "RightHandQ.y", "RightHandQ.z", "RightHandQ.w" },
            };
        }

        public static class Binding
        {
            public static EditorCurveBinding Active(string path) => EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive");

            public static readonly EditorCurveBinding[] RootT =
            {
                EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.x"),
                EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y"),
                EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.z"),
            };
            public static readonly EditorCurveBinding[] RootQ =
            {
                EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.x"),
                EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.y"),
                EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.z"),
                EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.w"),
            };
            public static readonly EditorCurveBinding[] MotionT =
            {
                EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionT.x"),
                EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionT.y"),
                EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionT.z"),
            };
            public static readonly EditorCurveBinding[] MotionQ =
            {
                EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionQ.x"),
                EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionQ.y"),
                EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionQ.z"),
                EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionQ.w"),
            };
        }

        public static HumanBodyBones[] AvatarIKGoal2HumanoidBone = new HumanBodyBones[]
        {
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
        };
    }
}
