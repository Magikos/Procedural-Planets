#if VERYANIMATION_TIMELINE
using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Timeline;
using UnityEditor.Timeline.Actions;

namespace VeryAnimation
{
    [MenuEntry("Add Additive Track", MenuPriority.CustomTrackActionSection.addOverrideTrack+1)]
    class AddVAAdditiveTrackAction : TrackAction
    {
        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            var uTimelineHelpers = new UTimelineWindow.UTimelineHelpers();
            foreach (var animTrack in tracks.OfType<VAAnimationTrack>())
            {
                var animationTrack = uTimelineHelpers.CreateTrack(animTrack.GetType(), animTrack, $"Additive {UTimelineWindow.UTrackAsset.GetChildTrackCount(animTrack)}") as VAAnimationTrack;
                Assert.IsNotNull(animationTrack);
                if (animationTrack != null)
                    animationTrack.blendingAdditive = true;
            }

            return true;
        }

        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks)
        {
            if (tracks.Any(t => t.isSubTrack || !typeof(VAAnimationTrack).IsAssignableFrom(t.GetType())))
                return ActionValidity.NotApplicable;

            if (tracks.Any(t => t.lockedInHierarchy))
                return ActionValidity.Invalid;

            return ActionValidity.Valid;
        }

        abstract class BlendingModeBaseAction : TrackAction
        {
            public abstract AnimatorLayerBlendingMode BlendingMode { get; }

            public override ActionValidity Validate(IEnumerable<TrackAsset> tracks)
            {
                if (tracks.Any(t => !t.isSubTrack))
                    return ActionValidity.NotApplicable;

                if (tracks.Any(t => !typeof(VAAnimationTrack).IsAssignableFrom(t.GetType())))
                    return ActionValidity.NotApplicable;

                if (tracks.Any(t => t.lockedInHierarchy))
                    return ActionValidity.Invalid;

                return ActionValidity.Valid;
            }

            public override bool Execute(IEnumerable<TrackAsset> tracks)
            {
                var uTrackExtensions = new UTimelineWindow.UTrackExtensions();
                foreach (var animTrack in tracks.OfType<VAAnimationTrack>())
                {
                    uTrackExtensions.UnarmForRecord(animTrack);
                    animTrack.blendingAdditive = BlendingMode == AnimatorLayerBlendingMode.Additive;
                }

                TimelineEditor.Refresh(RefreshReason.ContentsModified);
                return true;
            }
        }

        [MenuEntry("Blending/Override", MenuPriority.CustomTrackActionSection.applyTrackOffset+1)]
        [ApplyDefaultUndo]
        class BlendingOverrideAction : BlendingModeBaseAction
        {
            public override AnimatorLayerBlendingMode BlendingMode
            {
                get { return AnimatorLayerBlendingMode.Override; }
            }
        }
        [MenuEntry("Blending/Additive", MenuPriority.CustomTrackActionSection.applySceneOffset+1)]
        [ApplyDefaultUndo]
        class BlendingAdditiveAction : BlendingModeBaseAction
        {
            public override AnimatorLayerBlendingMode BlendingMode
            {
                get { return AnimatorLayerBlendingMode.Additive; }
            }
        }
    }

    [MenuEntry("Convert To VA Animation Track", MenuPriority.CustomTrackActionSection.convertToClipMode + 1)]
    class ConvertVAAnimationTrackAction : ConvertTrackAction
    {
        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            return ConvertTrack(tracks, typeof(AnimationTrack), typeof(VAAnimationTrack));
        }

        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks)
        {
            if (tracks.Any(t => typeof(AnimationTrack) != t.GetType()))
                return ActionValidity.NotApplicable;

            if (tracks.Any(t => HasUnsupportedChildTrack(t, typeof(AnimationTrack))))
                return ActionValidity.NotApplicable;

            return base.Validate(tracks);
        }
    }

    [MenuEntry("Convert To Animation Track", MenuPriority.CustomTrackActionSection.convertToClipMode + 1)]
    class ConvertAnimationTrackAction : ConvertTrackAction
    {
        private const string UndoName = "Convert To Animation Track";

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            var selectedTracks = tracks.ToArray();
            var trackArray = GetConvertTracks(selectedTracks);
            if (trackArray.Length <= 0)
                return false;

            var additiveTracks = trackArray.Where(t => t.blendingAdditive).ToArray();
            if (additiveTracks.Length > 0)
            {
                var director = TimelineEditor.inspectedDirector;
                if (director == null)
                    return false;

                var uAnimationTrack = new UTimelineWindow.UAnimationTrack();
                var uAnimationTrackExtensions = new UTimelineWindow.UAnimationTrackExtensions();
                var uExtrapolation = new UTimelineWindow.UExtrapolation();
                var uTimelineClip = new UTimelineWindow.UTimelineClip();
                var uTimelineCreateUtilities = new UTimelineWindow.UTimelineCreateUtilities();

                var bakeResults = new List<AdditiveBakeResult>();
                bool hasBakeError = false;

                var easeState = new TemporaryEaseState();
                var mainTrackTimingsCache = new Dictionary<AnimationTrack, SourceClipTiming[]>();
                var mainTrackBindingsCache = new Dictionary<AnimationTrack, HashSet<EditorCurveBinding>>();
                foreach (var track in additiveTracks)
                {
                    var sourceClips = GetAnimationPlayableAssetClips(track);
                    var hasInfiniteClip = !track.inClipMode && track.infiniteClip != null && !track.infiniteClip.empty;
                    if (sourceClips.Length <= 0 && !hasInfiniteClip)
                        continue;

                    var rootGO = GetBoundRootGameObject(director, track);
                    if (rootGO == null ||
                        !rootGO.TryGetComponent<Animator>(out var animator))
                    {
                        hasBakeError = true;
                        continue;
                    }

                    var dstClip = uTimelineCreateUtilities.CreateInfiniteClipAsset(track, $"Baked {track.name}");
                    if (dstClip == null)
                    {
                        hasBakeError = true;
                        continue;
                    }

                    var mainTrack = GetMainTrack(track);
                    if (!mainTrackTimingsCache.TryGetValue(mainTrack, out var sourceClipTimings))
                    {
                        sourceClipTimings = GetHierarchySourceClipTimings(mainTrack);
                        mainTrackTimingsCache[mainTrack] = sourceClipTimings;
                    }
                    if (!mainTrackBindingsCache.TryGetValue(mainTrack, out var sourceBindings))
                    {
                        sourceBindings = GetHierarchySourceCurveBindings(mainTrack);
                        mainTrackBindingsCache[mainTrack] = sourceBindings;
                    }
                    GetBakeRange(sourceClipTimings, track.timelineAsset.duration, out var bakeStart, out var bakeEnd);
                    bool baked;
                    Vector3 startOffsetPosition;
                    Quaternion startOffsetRotation;
                    easeState.Suppress(sourceClipTimings);
                    try
                    {
                        baked = BakeTrackRangeToClip(director, track, dstClip, rootGO, animator,
                            bakeStart, bakeEnd, sourceBindings,
                            out startOffsetPosition, out startOffsetRotation);
                    }
                    finally
                    {
                        easeState.Restore();
                    }
                    if (!baked)
                    {
                        hasBakeError = true;
                        continue;
                    }

                    bakeResults.Add(new AdditiveBakeResult(uAnimationTrack, uAnimationTrackExtensions, uExtrapolation, uTimelineClip, track, sourceClipTimings, bakeStart, dstClip,
                        startOffsetPosition, startOffsetRotation));
                }

                if (hasBakeError)
                    return false;

                foreach (var result in bakeResults)
                    result.Apply();
            }

            return ConvertTrack(selectedTracks, typeof(VAAnimationTrack), typeof(AnimationTrack));
        }

        private static AnimationTrack GetMainTrack(AnimationTrack track)
        {
            while (track != null && track.isSubTrack && track.parent is AnimationTrack parent)
                track = parent;
            return track;
        }

        private static VAAnimationTrack[] GetConvertTracks(IEnumerable<TrackAsset> tracks)
        {
            var results = new List<VAAnimationTrack>();
            var resultSet = new HashSet<VAAnimationTrack>();
            void Add(TrackAsset track)
            {
                if (track is not VAAnimationTrack vaTrack || !resultSet.Add(vaTrack))
                    return;

                results.Add(vaTrack);
                foreach (var childTrack in vaTrack.GetChildTracks())
                    Add(childTrack);
            }

            foreach (var track in tracks)
                Add(track);

            return results.ToArray();
        }

        private sealed class AdditiveBakeResult
        {
            private readonly UTimelineWindow.UAnimationTrack uAnimationTrack;
            private readonly UTimelineWindow.UAnimationTrackExtensions uAnimationTrackExtensions;
            private readonly UTimelineWindow.UExtrapolation uExtrapolation;
            private readonly UTimelineWindow.UTimelineClip uTimelineClip;

            private readonly VAAnimationTrack track;
            private readonly TimelineClip[] timelineClips;
            private readonly SourceClipTiming[] sourceClipTimings;
            private readonly double bakeStart;
            private readonly AnimationClip bakedClip;
            private readonly Vector3 startOffsetPosition;
            private readonly Quaternion startOffsetRotation;

            public AdditiveBakeResult(UTimelineWindow.UAnimationTrack uAnimationTrack,
                                        UTimelineWindow.UAnimationTrackExtensions uAnimationTrackExtensions,
                                        UTimelineWindow.UExtrapolation uExtrapolation,
                                        UTimelineWindow.UTimelineClip uTimelineClip,
                                        VAAnimationTrack track,
                                        SourceClipTiming[] sourceClipTimings, double bakeStart, AnimationClip bakedClip,
                                        Vector3 startOffsetPosition, Quaternion startOffsetRotation)
            {
                this.uAnimationTrack = uAnimationTrack;
                this.uAnimationTrackExtensions = uAnimationTrackExtensions;
                this.uExtrapolation = uExtrapolation;
                this.uTimelineClip = uTimelineClip;

                this.track = track;
                timelineClips = track.GetClips().ToArray();
                this.sourceClipTimings = sourceClipTimings;
                this.bakeStart = bakeStart;
                this.bakedClip = bakedClip;
                this.startOffsetPosition = startOffsetPosition;
                this.startOffsetRotation = startOffsetRotation;
            }

            public void Apply()
            {
                if (track == null || bakedClip == null)
                    return;

                UndoExtensions.RegisterTrack(track, UndoName);

                if (track.timelineAsset != null)
                {
                    foreach (var timelineClip in timelineClips)
                        track.timelineAsset.DeleteClip(timelineClip);
                }

                uAnimationTrack.SetInfiniteClip(track, bakedClip);
                var offsetPosition = startOffsetPosition;
                var offsetRotation = startOffsetRotation;
                if (track.isSubTrack &&
                    track.parent is AnimationTrack parentTrack)
                {
                    Vector3 parentPosition = parentTrack.position;
                    Quaternion parentRotation = Quaternion.identity;
                    if (parentTrack.trackOffset == TrackOffset.ApplyTransformOffsets ||
                        parentTrack.trackOffset == TrackOffset.Auto)
                    {
                        parentRotation = parentTrack.rotation;
                    }
                    else if (parentTrack.trackOffset == TrackOffset.ApplySceneOffsets)
                    {
                        parentPosition = uAnimationTrack.GetSceneOffsetPosition(parentTrack);
                        parentRotation = uAnimationTrack.GetSceneOffsetRotation(parentTrack);
                    }

                    var invParentRotation = Quaternion.Inverse(parentRotation);
                    offsetPosition = invParentRotation * (startOffsetPosition - parentPosition);
                    offsetRotation = invParentRotation * startOffsetRotation;
                }
                track.infiniteClipOffsetPosition = offsetPosition;
                track.infiniteClipOffsetEulerAngles = offsetRotation.eulerAngles;

                track.trackOffset = TrackOffset.ApplyTransformOffsets;
                track.position = Vector3.zero;
                track.rotation = Quaternion.identity;
                track.blendingAdditive = false;
                track.name = track.name.Replace("Additive", "Override");
                EditorUtility.SetDirty(track);

                uAnimationTrackExtensions.ConvertToClipMode(track);
                ApplyClipTiming(uExtrapolation, uTimelineClip, track, sourceClipTimings, bakeStart);
            }
        }

        private UAvatar uAvatar;

        private bool BakeTrackRangeToClip(PlayableDirector director, AnimationTrack track, AnimationClip dstClip,
            GameObject rootGO, Animator animator, double bakeStart, double bakeEnd,
            HashSet<EditorCurveBinding> sourceBindings, out Vector3 startOffsetPosition, out Quaternion startOffsetRotation)
        {
            startOffsetPosition = Vector3.zero;
            startOffsetRotation = Quaternion.identity;

            if (director == null || track == null || track.timelineAsset == null || dstClip == null || rootGO == null || animator == null)
                return false;

            var frameRate = (float)track.timelineAsset.editorSettings.frameRate;
            var duration = (float)(bakeEnd - bakeStart);
            if (frameRate <= 0f || duration <= 0f)
                return false;

            uAvatar ??= new UAvatar();

            var transforms = rootGO.GetComponentsInChildren<Transform>(true);
            var activeState = new TemporaryActiveState(rootGO, transforms);
            var animatorState = new TemporaryAnimatorState(animator);
            var activationTrackState = new TemporaryActivationTrackState(director, track.timelineAsset, rootGO, transforms);
            var allBindings = new List<EditorCurveBinding>();
            var allBindingSet = new HashSet<EditorCurveBinding>();
            void AddBinding(EditorCurveBinding binding)
            {
                if (allBindingSet.Add(binding))
                    allBindings.Add(binding);
            }
            foreach (var t in transforms)
            {
                foreach (var binding in AnimationUtility.GetAnimatableBindings(t.gameObject, rootGO))
                    AddBinding(binding);
            }
            foreach (var binding in activationTrackState.GetActiveBindings())
                AddBinding(binding);

            var frameCount = Mathf.RoundToInt(duration * frameRate);

            var fDatas = new Dictionary<EditorCurveBinding, AnimationCommon.MiniKeyframeList>();
            var rDatas = new Dictionary<EditorCurveBinding, AnimationCommon.MiniObjectReferenceKeyframeList>();
            var fFirstValues = new Dictionary<EditorCurveBinding, float>();
            var rFirstValues = new Dictionary<EditorCurveBinding, UnityEngine.Object>();
            var fNeedWrite = new Dictionary<EditorCurveBinding, bool>();
            var rNeedWrite = new Dictionary<EditorCurveBinding, bool>();

            EditorCurveBinding rootActiveBinding;
            {
                var genericRootMotionBonePath = animator.avatar != null ? uAvatar.GetGenericRootMotionBonePath(animator.avatar) : string.Empty;
                rootActiveBinding = AnimationCommon.Binding.Active(genericRootMotionBonePath);
            }

            var savedTime = director.time;
            try
            {
                activeState.Activate();
                animatorState.Activate();
                activationTrackState.DisableBindings();
                director.RebuildGraph();
                VAAnimationTrack.SetSettings(director);

                for (int frame = 0; frame <= frameCount; frame++)
                {
                    var sampleOffset = frame / frameRate;
                    var time = bakeStart + sampleOffset;
                    activeState.Activate();
                    animatorState.Activate();
                    director.time = time;
                    director.Evaluate();

                    EditorUtility.DisplayProgressBar(UndoName,
                        $"{track.name}: {frame} / {frameCount}", frame / (float)Mathf.Max(1, frameCount));

                    if (frame == 0)
                    {
                        rootGO.transform.GetLocalPositionAndRotation(out startOffsetPosition, out startOffsetRotation);
                    }

                    foreach (var b in allBindings)
                    {
                        if (b.isPPtrCurve)
                        {
                            if (AnimationUtility.GetObjectReferenceValue(rootGO, b, out var val))
                            {
                                if (!rDatas.TryGetValue(b, out var keys))
                                {
                                    rDatas[b] = keys = new AnimationCommon.MiniObjectReferenceKeyframeList(frameCount + 1);
                                    rFirstValues[b] = val;
                                    rNeedWrite[b] = false;
                                }
                                else if (!rNeedWrite[b] && val != rFirstValues[b])
                                {
                                    rNeedWrite[b] = true;
                                }
                                keys.SetKey(sampleOffset, val);
                            }
                        }
                        else
                        {
                            var result = activationTrackState.TryGetActiveValue(b, time, out var val);
                            if (!result)
                                result = AnimationUtility.GetFloatValue(rootGO, b, out val);
                            if (result)
                            {
                                if (!fDatas.TryGetValue(b, out var keys))
                                {
                                    fDatas[b] = keys = new AnimationCommon.MiniKeyframeList(frameCount + 1);
                                    fFirstValues[b] = val;
                                    fNeedWrite[b] = false;
                                }
                                else if (!fNeedWrite[b] && val != fFirstValues[b])
                                {
                                    fNeedWrite[b] = true;
                                }
                                keys.SetKey(sampleOffset, val);
                            }
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    activationTrackState.RestoreBindings();
                    activeState.Activate();
                    animatorState.Activate();
                    director.RebuildGraph();
                    VAAnimationTrack.SetSettings(director);
                    director.time = savedTime;
                    director.Evaluate();
                }
                finally
                {
                    animatorState.Restore();
                    activeState.Restore();
                    EditorUtility.ClearProgressBar();
                }
            }

            #region Same members
            foreach (var pair in fDatas)
            {
                if (!fNeedWrite.TryGetValue(pair.Key, out var needed) || !needed)
                    continue;

                var lastIndex = pair.Key.propertyName.LastIndexOf('.');
                if (lastIndex >= 0)
                {
                    var pName = pair.Key.propertyName[..(lastIndex + 1)];
                    foreach (var pairSub in fDatas)
                    {
                        if (fNeedWrite.TryGetValue(pairSub.Key, out var subNeeded) && subNeeded)
                            continue;

                        if (pair.Key == pairSub.Key ||
                            pair.Key.path != pairSub.Key.path)
                            continue;
                        if (!pairSub.Key.propertyName.StartsWith(pName, StringComparison.Ordinal))
                            continue;
                        fNeedWrite[pairSub.Key] = true;
                    }
                }
            }
            #endregion

            foreach (var propertyName in AnimationCommon.PropertyName.Position.Concat(AnimationCommon.PropertyName.RotationQuaternion))
            {
                var binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), propertyName);
                if (fDatas.ContainsKey(binding))
                    fNeedWrite[binding] = true;
            }

            var filteredFDatas = new Dictionary<EditorCurveBinding, AnimationCurve>();
            foreach (var pair in fDatas)
            {
                if (!fNeedWrite.TryGetValue(pair.Key, out var needed) || !needed)
                    continue;
                var valueType = AnimationUtility.GetEditorCurveValueType(rootGO, pair.Key);
                var curve = pair.Value.CreateAnimationCurve();
                AnimationCommon.SetAnimationCurveTangent(curve, valueType);
                filteredFDatas.Add(pair.Key, curve);
            }

            var filteredRDatas = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>();
            foreach (var pair in rDatas)
            {
                if (!rNeedWrite.TryGetValue(pair.Key, out var needed) || !needed)
                    continue;
                filteredRDatas.Add(pair.Key, pair.Value.CreateObjectReferenceKeyframes());
            }

            Undo.RegisterCompleteObjectUndo(dstClip, UndoName);
            dstClip.frameRate = frameRate;
            AnimationCommon.SetEditorCurves(dstClip, filteredFDatas);
            AnimationCommon.SetObjectReferenceCurves(dstClip, filteredRDatas);

            AnimationCommon.ResetAnimationClipSettings(dstClip);

            Assert.IsTrue(AnimationCommon.RemoveStartOffset(dstClip, startOffsetPosition, startOffsetRotation));

            if (animator.isHuman)
            {
                AnimationCommon.ConvertToHumanoidClip(dstClip, rootGO);
            }
            else if (animator.applyRootMotion && !string.IsNullOrEmpty(rootActiveBinding.path))
            {
                var rootMotionBone = AnimationUtility.GetAnimatedObject(rootGO, rootActiveBinding) as GameObject;
                if (rootMotionBone != null)
                {
                    AnimationCommon.TransferRootMotionToRootNodeTransform(dstClip, rootGO, rootMotionBone);
                }
            }

            if (sourceBindings != null)
            {
                var removeCurves = new Dictionary<EditorCurveBinding, AnimationCurve>();
                foreach (var binding in AnimationUtility.GetCurveBindings(dstClip))
                {
                    if (!sourceBindings.Contains(binding))
                        removeCurves.Add(binding, null);
                }
                AnimationCommon.SetEditorCurves(dstClip, removeCurves);

                var removeObjectReferenceCurves = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>();
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(dstClip))
                {
                    if (!sourceBindings.Contains(binding))
                        removeObjectReferenceCurves.Add(binding, null);
                }
                AnimationCommon.SetObjectReferenceCurves(dstClip, removeObjectReferenceCurves);
            }

            EditorUtility.SetDirty(dstClip);

            return true;
        }

        private sealed class TemporaryActiveState
        {
            private readonly List<GameObject> gameObjects = new();
            private readonly List<bool> activeStates = new();
            private readonly HashSet<GameObject> gameObjectSet = new();

            public TemporaryActiveState(GameObject root, IEnumerable<Transform> transforms)
            {
                if (root == null)
                    return;

                var ancestors = new Stack<GameObject>();
                for (var t = root.transform.parent; t != null; t = t.parent)
                    ancestors.Push(t.gameObject);
                while (ancestors.Count > 0)
                    Add(ancestors.Pop());

                Add(root);
                if (transforms == null)
                    return;

                foreach (var t in transforms)
                {
                    if (t != null)
                        Add(t.gameObject);
                }
            }

            public void Activate()
            {
                foreach (var go in gameObjects)
                {
                    if (go != null && !go.activeSelf)
                        go.SetActive(true);
                }
            }

            public void Restore()
            {
                for (int i = gameObjects.Count - 1; i >= 0; i--)
                {
                    var go = gameObjects[i];
                    if (go != null && go.activeSelf != activeStates[i])
                        go.SetActive(activeStates[i]);
                }
            }

            private void Add(GameObject gameObject)
            {
                if (gameObject == null || !gameObjectSet.Add(gameObject))
                    return;
                gameObjects.Add(gameObject);
                activeStates.Add(gameObject.activeSelf);
            }
        }

        private sealed class TemporaryAnimatorState
        {
            private readonly Animator animator;
            private readonly bool enabled;
            private readonly bool fireEvents;
            private readonly AnimatorCullingMode cullingMode;

            public TemporaryAnimatorState(Animator animator)
            {
                this.animator = animator;
                if (animator == null)
                    return;

                enabled = animator.enabled;
                fireEvents = animator.fireEvents;
                cullingMode = animator.cullingMode;
            }

            public void Activate()
            {
                if (animator == null)
                    return;

                animator.enabled = true;
                animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            public void Restore()
            {
                if (animator == null)
                    return;

                animator.enabled = enabled;
                animator.fireEvents = fireEvents;
                animator.cullingMode = cullingMode;
            }
        }

        private sealed class TemporaryActivationTrackState
        {
            private readonly PlayableDirector director;
            private readonly List<BindingState> bindingStates = new();
            private readonly List<ActivationState> activationStates = new();

            public TemporaryActivationTrackState(PlayableDirector director, TimelineAsset timelineAsset, GameObject root, IEnumerable<Transform> transforms)
            {
                this.director = director;
                if (director == null || timelineAsset == null || root == null)
                    return;

                var hierarchySet = new HashSet<GameObject>();
                var pathByGameObject = new Dictionary<GameObject, string>();
                void AddHierarchyObject(Transform t)
                {
                    if (t == null || !hierarchySet.Add(t.gameObject))
                        return;
                    pathByGameObject.Add(t.gameObject, AnimationUtility.CalculateTransformPath(t, root.transform));
                }

                AddHierarchyObject(root.transform);
                if (transforms != null)
                {
                    foreach (var t in transforms)
                        AddHierarchyObject(t);
                }

                var forceActiveSet = new HashSet<GameObject>(hierarchySet);
                for (var t = root.transform.parent; t != null; t = t.parent)
                    forceActiveSet.Add(t.gameObject);

                foreach (var activationTrack in timelineAsset.GetOutputTracks().OfType<ActivationTrack>())
                {
                    if (activationTrack == null || activationTrack.mutedInHierarchy)
                        continue;

                    var boundGameObject = GetBoundGameObject(director, activationTrack);
                    if (boundGameObject == null || !forceActiveSet.Contains(boundGameObject))
                        continue;

                    bindingStates.Add(new BindingState(activationTrack, director.GetGenericBinding(activationTrack)));
                    if (pathByGameObject.TryGetValue(boundGameObject, out var path))
                        activationStates.Add(new ActivationState(activationTrack, AnimationCommon.Binding.Active(path)));
                }
            }

            public IEnumerable<EditorCurveBinding> GetActiveBindings()
            {
                foreach (var state in activationStates)
                    yield return state.Binding;
            }

            public void DisableBindings()
            {
                foreach (var state in bindingStates)
                    director.SetGenericBinding(state.Track, null);
            }

            public void RestoreBindings()
            {
                foreach (var state in bindingStates)
                    director.SetGenericBinding(state.Track, state.Binding);
            }

            public bool TryGetActiveValue(EditorCurveBinding binding, double time, out float value)
            {
                value = 0f;
                if (!AnimationCommon.IsActiveBinding(binding))
                    return false;

                var found = false;
                foreach (var state in activationStates)
                {
                    if (state.Binding.path != binding.path)
                        continue;

                    value = state.Evaluate(time) ? 1f : 0f;
                    found = true;
                }
                return found;
            }

            private static GameObject GetBoundGameObject(PlayableDirector director, TrackAsset track)
            {
                var binding = director.GetGenericBinding(track);
                if (binding is GameObject gameObject)
                    return gameObject;
                if (binding is Component component)
                    return component.gameObject;
                return null;
            }

            private readonly struct BindingState
            {
                public readonly ActivationTrack Track;
                public readonly UnityEngine.Object Binding;

                public BindingState(ActivationTrack track, UnityEngine.Object binding)
                {
                    Track = track;
                    Binding = binding;
                }
            }

            private readonly struct ActivationState
            {
                public readonly ActivationTrack Track;
                public readonly EditorCurveBinding Binding;

                public ActivationState(ActivationTrack track, EditorCurveBinding binding)
                {
                    Track = track;
                    Binding = binding;
                }

                public bool Evaluate(double time)
                {
                    foreach (var clip in Track.GetClips())
                    {
                        if (clip != null && time >= clip.start && time < clip.end)
                            return true;
                    }
                    return false;
                }
            }
        }

        private static GameObject GetBoundRootGameObject(PlayableDirector director, TrackAsset track)
        {
            while (director != null && track != null)
            {
                var boundObj = director.GetGenericBinding(track);
                if (boundObj is Animator anim)
                    return anim.gameObject;
                if (boundObj is GameObject go)
                    return go;
                track = track.parent as TrackAsset;
            }
            return null;
        }

        private static TimelineClip[] GetAnimationPlayableAssetClips(AnimationTrack track)
        {
            return track.GetClips()
                .Where(clip => clip.asset is AnimationPlayableAsset asset && asset.clip != null && clip.duration > 0.0)
                .ToArray();
        }

        private sealed class SourceClipTiming
        {
            public readonly TimelineClip FirstClip;
            public readonly TimelineClip LastClip;
            public readonly double Start;
            public readonly double End;
            public readonly double EaseInDuration;
            public readonly double EaseOutDuration;
            public readonly TimelineClip.ClipExtrapolation PreExtrapolationMode;
            public readonly TimelineClip.ClipExtrapolation PostExtrapolationMode;
            public readonly bool ApplyFootIK;

            private SourceClipTiming(TimelineClip firstClip, TimelineClip lastClip, bool applyFootIK)
            {
                FirstClip = firstClip;
                LastClip = lastClip;
                Start = firstClip.start;
                End = lastClip.end;
                ApplyFootIK = applyFootIK;

                var trimmedStart = Start + firstClip.easeInDuration;
                var trimmedEnd = End - lastClip.easeOutDuration;
                if (trimmedStart < trimmedEnd)
                {
                    Start = trimmedStart;
                    End = trimmedEnd;
                }
                EaseInDuration = 0f;
                EaseOutDuration = 0f;
                PreExtrapolationMode = TimelineClip.ClipExtrapolation.None;
                PostExtrapolationMode = TimelineClip.ClipExtrapolation.None;
            }

            public static SourceClipTiming[] Create(IEnumerable<TimelineClip> clips)
            {
                var sortedClips = clips.Where(clip => clip != null).OrderBy(clip => clip.start).ToArray();
                if (sortedClips.Length == 0)
                    return null;

                var results = new List<SourceClipTiming>();
                var firstClip = sortedClips[0];
                var lastClip = sortedClips[0];
                var applyFootIK = GetApplyFootIK(sortedClips[0]);
                foreach (var clip in sortedClips.Skip(1))
                {
                    if (clip.start > lastClip.end)
                    {
                        results.Add(new SourceClipTiming(firstClip, lastClip, applyFootIK));
                        firstClip = clip;
                        lastClip = clip;
                        applyFootIK = GetApplyFootIK(clip);
                        continue;
                    }

                    applyFootIK |= GetApplyFootIK(clip);
                    if (clip.end > lastClip.end)
                    {
                        lastClip = clip;
                    }
                }
                results.Add(new SourceClipTiming(firstClip, lastClip, applyFootIK));
                return results.ToArray();
            }

            private static bool GetApplyFootIK(TimelineClip clip)
            {
                return ((AnimationPlayableAsset)clip.asset).applyFootIK;
            }
        }

        private static SourceClipTiming[] GetHierarchySourceClipTimings(AnimationTrack track)
        {
            var clips = new List<TimelineClip>();
            if (!CollectHierarchyClips(track, clips))
                return null;

            return SourceClipTiming.Create(clips);
        }

        private static bool CollectHierarchyClips(AnimationTrack track, List<TimelineClip> clips)
        {
            if (track == null)
                return true;

            if (!track.inClipMode && track.infiniteClip != null && !track.infiniteClip.empty)
                return false;

            clips.AddRange(GetAnimationPlayableAssetClips(track));
            foreach (var childTrack in track.GetChildTracks())
            {
                if (childTrack is AnimationTrack childAnimTrack && !CollectHierarchyClips(childAnimTrack, clips))
                    return false;
            }
            return true;
        }

        private static HashSet<EditorCurveBinding> GetHierarchySourceCurveBindings(AnimationTrack track)
        {
            var bindings = new HashSet<EditorCurveBinding>();
            void Collect(AnimationTrack animTrack)
            {
                AddSourceCurveBindings(animTrack, bindings);
                foreach (var childTrack in animTrack.GetChildTracks())
                {
                    if (childTrack is AnimationTrack childAnimTrack)
                        Collect(childAnimTrack);
                }
            }
            if (track != null)
                Collect(track);
            return bindings;
        }

        private static void AddSourceCurveBindings(AnimationTrack track, HashSet<EditorCurveBinding> bindings)
        {
            if (track == null)
                return;

            foreach (var timelineClip in GetAnimationPlayableAssetClips(track))
                AddClipCurveBindings(((AnimationPlayableAsset)timelineClip.asset).clip, bindings);
            if (!track.inClipMode && track.infiniteClip != null && !track.infiniteClip.empty)
                AddClipCurveBindings(track.infiniteClip, bindings);
        }

        private static void AddClipCurveBindings(AnimationClip clip, HashSet<EditorCurveBinding> bindings)
        {
            if (clip == null)
                return;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                bindings.Add(binding);
                if (binding.type == typeof(Transform) && IsRotationPropertyName(binding.propertyName))
                {
                    foreach (var propertyName in AnimationCommon.PropertyName.RotationQuaternion)
                        bindings.Add(EditorCurveBinding.FloatCurve(binding.path, typeof(Transform), propertyName));
                }
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                bindings.Add(binding);
        }

        private static bool IsRotationPropertyName(string propertyName)
        {
            foreach (var prefix in URotationCurveInterpolation.PrefixForInterpolation)
            {
                if (prefix != null && propertyName.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void GetBakeRange(SourceClipTiming[] timings, double fullDuration, out double bakeStart, out double bakeEnd)
        {
            if (timings == null || timings.Length == 0)
            {
                bakeStart = 0.0;
                bakeEnd = fullDuration;
                return;
            }

            bakeStart = timings[0].Start;
            bakeEnd = timings[0].End;
            foreach (var timing in timings)
            {
                if (timing.Start < bakeStart)
                    bakeStart = timing.Start;
                if (timing.End > bakeEnd)
                    bakeEnd = timing.End;
            }
        }

        private static void ApplyClipTiming(UTimelineWindow.UExtrapolation uExtrapolation, UTimelineWindow.UTimelineClip uTimelineClip, AnimationTrack track, SourceClipTiming[] timings, double bakeStart)
        {
            if (track == null || timings == null || timings.Length == 0)
                return;

            var convertedClips = track.GetClips().ToArray();
            if (convertedClips.Length != 1)
            {
                Debug.LogError($"Converted TimelineClip not found: {track.name}");
                return;
            }

            UndoExtensions.RegisterTrack(track, UndoName);

            var timelineClips = new TimelineClip[timings.Length];
            timelineClips[0] = convertedClips[0];
            for (int i = 1; i < timings.Length; i++)
                timelineClips[i] = DuplicateConvertedClip(uTimelineClip, track, convertedClips[0]);

            for (int i = 0; i < timings.Length; i++)
            {
                var timelineClip = timelineClips[i];
                if (timelineClip == null)
                    continue;

                var timing = timings[i];
                timelineClip.start = timing.Start;
                timelineClip.clipIn = timing.Start - bakeStart;
                timelineClip.duration = timing.End - timing.Start;
                timelineClip.easeInDuration = timing.EaseInDuration;
                timelineClip.easeOutDuration = timing.EaseOutDuration;
                uTimelineClip.SetPreExtrapolationMode(timelineClip, timing.PreExtrapolationMode);
                uTimelineClip.SetPostExtrapolationMode(timelineClip, timing.PostExtrapolationMode);
                if (timelineClip.asset is AnimationPlayableAsset animationPlayableAsset)
                {
                    animationPlayableAsset.applyFootIK = timing.ApplyFootIK;
                    EditorUtility.SetDirty(animationPlayableAsset);
                }
            }
            uExtrapolation.CalculateExtrapolationTimes(track);
            EditorUtility.SetDirty(track);
        }

        private static TimelineClip DuplicateConvertedClip(UTimelineWindow.UTimelineClip uTimelineClip, AnimationTrack track, TimelineClip sourceClip)
        {
            if (sourceClip.asset is not AnimationPlayableAsset sourceAsset || sourceAsset.clip == null)
            {
                Debug.LogError($"Converted AnimationPlayableAsset not found: {track.name}");
                return null;
            }

            var newClip = track.CreateClip(sourceAsset.clip);
            if (newClip.asset is AnimationPlayableAsset newAsset)
                EditorUtility.CopySerialized(sourceAsset, newAsset);
            newClip.displayName = sourceClip.displayName;
            uTimelineClip.SetRecordable(newClip, sourceClip.recordable);
            return newClip;
        }

        private sealed class TemporaryEaseState
        {
            private readonly List<TimelineClip> clips = new();
            private readonly List<double> easeInDurations = new();
            private readonly List<double> easeOutDurations = new();
            private readonly HashSet<TimelineClip> clipSet = new();

            public void Suppress(SourceClipTiming[] timings)
            {
                if (timings == null)
                    return;

                foreach (var timing in timings)
                {
                    Add(timing.FirstClip);
                    Add(timing.LastClip);
                    timing.FirstClip.easeInDuration = 0.0;
                    timing.LastClip.easeOutDuration = 0.0;
                }
            }

            public void Restore()
            {
                for (int i = 0; i < clips.Count; i++)
                {
                    clips[i].easeInDuration = easeInDurations[i];
                    clips[i].easeOutDuration = easeOutDurations[i];
                }
                clips.Clear();
                easeInDurations.Clear();
                easeOutDurations.Clear();
                clipSet.Clear();
            }

            private void Add(TimelineClip clip)
            {
                if (clip == null || !clipSet.Add(clip))
                    return;
                clips.Add(clip);
                easeInDurations.Add(clip.easeInDuration);
                easeOutDurations.Add(clip.easeOutDuration);
            }
        }

        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks)
        {
            if (tracks.Any(t => typeof(VAAnimationTrack) != t.GetType()))
                return ActionValidity.NotApplicable;

            if (tracks.Any(t => HasUnsupportedChildTrack(t, typeof(VAAnimationTrack))))
                return ActionValidity.NotApplicable;

            if (TimelineEditor.inspectedDirector == null)
                return ActionValidity.NotApplicable;

            foreach (var track in tracks)
            {
                var rootGO = GetBoundRootGameObject(TimelineEditor.inspectedDirector, track);
                if (rootGO == null ||
                    !rootGO.TryGetComponent<Animator>(out _))
                    return ActionValidity.NotApplicable;
            }

            return base.Validate(tracks);
        }
    }

    abstract class ConvertTrackAction : TrackAction
    {
        private static string GetEditorClassIdentifier(Type type)
        {
            return $"{type.Assembly.GetName().Name}::{type.FullName}";
        }

        private static void DestroyStaleTrackInspectors(List<TrackAsset> replaceTracks)
        {
            var animationTrackInspectorType = ReflectionCommon.GetUnityEditorType("UnityEditor.Timeline.AnimationTrackInspector");
            foreach (var editor in Resources.FindObjectsOfTypeAll<Editor>())
            {
                if (editor == null)
                    continue;

                var editorType = editor.GetType();
                if (editorType != animationTrackInspectorType && editorType != typeof(VAAnimationTrackInspector))
                    continue;

                var stale = false;
                foreach (var editorTarget in editor.targets)
                {
                    if (editorTarget == null ||
                        (editorTarget is TrackAsset trackTarget && replaceTracks.Contains(trackTarget)))
                    {
                        stale = true;
                        break;
                    }
                }
                if (stale)
                    Editor.DestroyImmediate(editor);
            }
        }

        protected static bool HasUnsupportedChildTrack(TrackAsset track, Type before)
        {
            foreach (var childTrack in track.GetChildTracks())
            {
                if (childTrack != null && childTrack.GetType() != before)
                    return true;
            }
            return false;
        }

        protected bool ConvertTrack(IEnumerable<TrackAsset> tracks, Type before, Type after)
        {
            var convertTracks = tracks.Where(x => x != null && x.GetType() == before).ToArray();
            if (convertTracks.Length <= 0)
                return false;

            MonoScript mono = null;
            {
                ScriptableObject tmp = null;
                try
                {
                    tmp = ScriptableObject.CreateInstance(after) as ScriptableObject;
                    if (tmp != null)
                        mono = MonoScript.FromScriptableObject(tmp);
                }
                finally
                {
                    if (tmp != null)
                        ScriptableObject.DestroyImmediate(tmp);
                }
            }
            Assert.IsNotNull(mono);

            var replaceTracks = new List<TrackAsset>();
            foreach (var animTrack in convertTracks)
            {
                foreach (var childTrack in animTrack.GetChildTracks())
                {
                    if (childTrack == null)
                        continue;
                    if (childTrack.GetType() != before)
                    {
                        Debug.LogWarningFormat("Cannot convert track '{0}' because child track '{1}' is {2}, not {3}.", animTrack.name, childTrack.name, childTrack.GetType().Name, before.Name);
                        return false;
                    }
                    replaceTracks.Add(childTrack);
                }
                replaceTracks.Add(animTrack);
            }

            UndoExtensions.RegisterCompleteTimeline(replaceTracks.First().timelineAsset, $"Convert from {before} to {after}");

            {
                var newSelection = Selection.objects
                    .Where(o => o is not TrackAsset track || !replaceTracks.Contains(track))
                    .ToArray();
                if (newSelection.Length != Selection.objects.Length)
                {
                    Selection.objects = newSelection;
                    ActiveEditorTracker.sharedTracker.ForceRebuild();
                }
                DestroyStaleTrackInspectors(replaceTracks);
            }

            var editorClassIdentifier = GetEditorClassIdentifier(after);
            foreach (var track in replaceTracks)
            {
                var so = new SerializedObject(track);
                var prop = so.FindProperty("m_Script");
                if (prop == null)
                    continue;
                prop.objectReferenceValue = mono;
                var editorClassIdentifierProp = so.FindProperty("m_EditorClassIdentifier");
                if (editorClassIdentifierProp != null)
                    editorClassIdentifierProp.stringValue = editorClassIdentifier;
                so.ApplyModifiedProperties();
            }

            foreach (var track in replaceTracks)
            {
                if (track != null)
                    AssetDatabase.SaveAssetIfDirty(track);
            }
            AssetDatabase.Refresh();
            EditorUtility.RequestScriptReload();

            return true;
        }

        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks)
        {
            if (tracks.Any(t => t.isSubTrack))
                return ActionValidity.NotApplicable;

            foreach (AnimationTrack track in tracks.Cast<AnimationTrack>())
            {
                if (track.IsRecording() || track.lockedInHierarchy || track.mutedInHierarchy)
                    return ActionValidity.Invalid;
                foreach (var childTrack in track.GetChildTracks().Cast<AnimationTrack>())
                {
                    if (childTrack != null && (childTrack.IsRecording() || childTrack.lockedInHierarchy || childTrack.mutedInHierarchy))
                        return ActionValidity.Invalid;
                }
            }

            if (VeryAnimationWindow.instance != null &&
                VeryAnimationWindow.instance.VA != null &&
                VeryAnimationWindow.instance.VA.IsEdit)
                return ActionValidity.Invalid;

            return ActionValidity.Valid;
        }
    }
}
#endif
