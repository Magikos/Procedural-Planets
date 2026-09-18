using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;
using System;
using System.Reflection;

#if VERYANIMATION_TIMELINE
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEditor.Timeline;
#endif

namespace VeryAnimation
{
#if VERYANIMATION_TIMELINE
    internal sealed class UTimelineWindow
    {
        private readonly Func<EditorWindow> dg_get_instance;
        private Func<object> dg_get_state;
        private readonly MethodInfo mi_get_state;
        private readonly Func<object, object> dg_get_m_LockTracker;

        public UTimelineWindowTimeControl TimelineWindowTimeControl { get; private set; }
        public UTimelineState TimelineState { get; private set; }
        public UTrackAsset TrackAsset { get; private set; }
        public UAnimationTrack AnimationTrack { get; private set; }
        public UAnimationPlayableAsset AnimationPlayableAsset { get; private set; }
        public UTimelineAnimationUtilities TimelineAnimationUtilities { get; private set; }
        public UTimelineHelpers TimelineHelpers { get; private set; }
        public UEditorGUIUtility EditorGUIUtility { get; private set; }

        public UTimelineWindow()
        {
            var timelineWindowType = ReflectionCommon.GetUnityEditorType("UnityEditor.Timeline.TimelineWindow");
            Assert.IsNotNull(dg_get_instance = (Func<EditorWindow>)Delegate.CreateDelegate(typeof(Func<EditorWindow>), null, timelineWindowType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetGetMethod()));
            Assert.IsNotNull(mi_get_state = timelineWindowType.GetProperty("state").GetGetMethod());
            Assert.IsNotNull(dg_get_m_LockTracker = ReflectionCommon.CreateGetFieldDelegate<object>(timelineWindowType.GetField("m_LockTracker", BindingFlags.NonPublic | BindingFlags.Instance)));

            TimelineWindowTimeControl = new UTimelineWindowTimeControl();
            TimelineState = new UTimelineState();
            TrackAsset = new UTrackAsset();
            AnimationTrack = new UAnimationTrack();
            AnimationPlayableAsset = new UAnimationPlayableAsset();
            TimelineAnimationUtilities = new UTimelineAnimationUtilities();
            TimelineHelpers = new UTimelineHelpers();
            EditorGUIUtility = new UEditorGUIUtility();
        }

        public class UTimelineState //UWindowState
        {
            public UISequenceState ISequenceState { get; private set; }

            protected Func<bool> dg_get_recording;
            protected MethodInfo mi_get_recording;
            protected Action<bool> dg_set_recording;
            protected MethodInfo mi_set_recording;
            protected Func<bool> dg_get_previewMode;
            protected MethodInfo mi_get_previewMode;
            protected Action<bool> dg_set_previewMode;
            protected MethodInfo mi_set_previewMode;
            protected Action<bool> dg_set_rebuildGraph;
            protected MethodInfo mi_set_rebuildGraph;
            protected Func<TrackAsset, bool> dg_get_IsArmedForRecord;
            protected MethodInfo mi_IsArmedForRecord;
            protected Action<bool> dg_SetPlaying;
            protected MethodInfo mi_SetPlaying;
            protected Action dg_EvaluateImmediate;
            protected MethodInfo mi_EvaluateImmediate;
            protected Action dg_Refresh;
            protected MethodInfo mi_Refresh;

            private Func<object> dg_get_editSequence;
            private readonly MethodInfo mi_get_editSequence;

            public UTimelineState()
            {
                var windowStateType = ReflectionCommon.GetUnityEditorType("UnityEditor.Timeline.WindowState");
                Assert.IsNotNull(mi_get_editSequence = windowStateType.GetProperty("editSequence").GetGetMethod());
                Assert.IsNotNull(mi_get_recording = windowStateType.GetProperty("recording").GetGetMethod());
                Assert.IsNotNull(mi_set_recording = windowStateType.GetProperty("recording").GetSetMethod());
                Assert.IsNotNull(mi_get_previewMode = windowStateType.GetProperty("previewMode").GetGetMethod());
                Assert.IsNotNull(mi_set_previewMode = windowStateType.GetProperty("previewMode").GetSetMethod());
                Assert.IsNotNull(mi_set_rebuildGraph = windowStateType.GetProperty("rebuildGraph").GetSetMethod());
                Assert.IsNotNull(mi_IsArmedForRecord = windowStateType.GetMethod("IsArmedForRecord"));
                Assert.IsNotNull(mi_SetPlaying = windowStateType.GetMethod("SetPlaying"));
                Assert.IsNotNull(mi_EvaluateImmediate = windowStateType.GetMethod("EvaluateImmediate"));
                Assert.IsNotNull(mi_Refresh = windowStateType.GetMethod("Refresh"));

                ISequenceState = new UISequenceState();
            }

            public object GetEditSequence(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_editSequence, instance, mi_get_editSequence);

            public virtual PlayableDirector GetCurrentDirector(object instance)
            {
                if (instance == null) return null;
                return ISequenceState.GetDirector(GetEditSequence(instance));
            }

            public bool GetRecording(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_recording, instance, mi_get_recording);
            public void SetRecording(object instance, bool enable)
            {
                try
                {
                    ReflectionCommon.InvokeInstanceDelegate(ref dg_set_recording, instance, mi_set_recording, enable);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }

            public bool GetPreviewMode(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_previewMode, instance, mi_get_previewMode);
            public virtual void SetPreviewMode(object instance, bool enable)
            {
                if (instance == null) return;
                ReflectionCommon.InvokeInstanceDelegate(ref dg_set_previewMode, instance, mi_set_previewMode, enable);
                if (!enable)
                {
                    SetPlaying(instance, false);
                }
                else
                {
                    ReflectionCommon.InvokeInstanceDelegate(ref dg_set_rebuildGraph, instance, mi_set_rebuildGraph, true);
                }
            }

            public void SetPlaying(object instance, bool enable) => ReflectionCommon.InvokeInstanceDelegate(ref dg_SetPlaying, instance, mi_SetPlaying, enable);

            public void EvaluateImmediate(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_EvaluateImmediate, instance, mi_EvaluateImmediate);
            public void Refresh(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_Refresh, instance, mi_Refresh);

            public virtual int GetFrame(object instance)
            {
                if (instance == null) return 0;
                return ISequenceState.GetFrame(GetEditSequence(instance));
            }
            public virtual void SetFrame(object instance, int frame)
            {
                if (instance == null) return;
                ISequenceState.SetFrame(GetEditSequence(instance), frame);
            }

            public virtual float GetFrameRate(object instance)
            {
                if (instance == null) return 0f;
                return ISequenceState.GetFrameRate(GetEditSequence(instance));
            }

            public bool IsArmedForRecord(object instance, TrackAsset track) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_IsArmedForRecord, instance, mi_IsArmedForRecord, track);
        }
        public class UTimelineWindowTimeControl
        {
            public Type ControlType { get; protected set; }

            protected Func<object, TimelineClip> dg_get_m_Clip;
            protected Func<TrackAsset> dg_get_track;
            protected MethodInfo mi_get_track;
            protected Func<object> dg_get_state;

            protected UTimelineState uTimelineState;

            public UTimelineWindowTimeControl()
            {
                ControlType = ReflectionCommon.GetUnityEditorType("UnityEditor.Timeline.TimelineWindowTimeControl");
                Assert.IsNotNull(dg_get_m_Clip = ReflectionCommon.CreateGetFieldDelegate<TimelineClip>(ControlType.GetField("m_Clip", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(mi_get_track = ControlType.GetProperty("track", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true));
                Assert.IsNotNull(dg_get_state = (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), null, ControlType.GetProperty("state", BindingFlags.NonPublic | BindingFlags.Static).GetGetMethod(true)));

                uTimelineState = new UTimelineState();
            }

            public virtual TrackAsset GetTrackAsset(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_track, instance, mi_get_track);
            public virtual object GetTimelineState(object instance)
            {
                if (instance == null) return null;
                return dg_get_state();
            }

            public TimelineClip GetTimelineClip(object instance)
            {
                if (instance == null) return null;
                return dg_get_m_Clip(instance);
            }
            public PlayableAsset GetPlayableAsset(object instance)
            {
                if (instance == null) return null;
                var clip = dg_get_m_Clip(instance);
                if (clip == null) return null;
                return clip.asset as PlayableAsset;
            }
            public UnityEngine.Object GetGenericBinding(object instance)
            {
                var currentDirector = uTimelineState.GetCurrentDirector(GetTimelineState(instance));
                if (currentDirector == null) return null;
                var trackAsset = GetTrackAsset(instance);
                while (trackAsset != null)
                {
                    var o = currentDirector.GetGenericBinding(trackAsset);
                    if (o != null) return o;
                    trackAsset = trackAsset.parent as TrackAsset;
                }
                return null;
            }
            public AnimationClip GetAnimationClip(object instance)
            {
                if (instance == null) return null;
                var clip = dg_get_m_Clip(instance);
                if (clip == null) return null;
                return clip.animationClip;
            }
            public void SetAnimationClip(object instance, AnimationClip animClip, string undoName = null)
            {
                if (instance == null) return;
                var clip = dg_get_m_Clip(instance);
                if (clip == null) return;
                var animationPlayableAsset = clip.asset as AnimationPlayableAsset;
                if (animationPlayableAsset == null) return;
                if (undoName != null)
                    Undo.RecordObject(animationPlayableAsset, undoName);
                animationPlayableAsset.clip = animClip;
            }

            public bool IsArmedForRecord(object instance)
            {
                var currentDirector = uTimelineState.GetCurrentDirector(GetTimelineState(instance));
                if (currentDirector == null) return false;
                var state = GetTimelineState(instance);
                if (state == null) return false;
                var trackAsset = GetTrackAsset(instance);
                if (trackAsset == null) return false;
                return uTimelineState.IsArmedForRecord(state, trackAsset);
            }
        }

        public class UTrackAsset
        {
            private Func<bool> dg_get_locked;
            private readonly MethodInfo mi_get_locked;

            public UTrackAsset()
            {
                Assert.IsNotNull(mi_get_locked = typeof(TrackAsset).GetProperty("locked").GetGetMethod());
            }

            public virtual bool GetLocked(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_locked, instance, mi_get_locked);

            public static int GetChildTrackCount(TrackAsset track)
            {
                if (track == null)
                    return 0;
                var count = 0;
                foreach (var _ in track.GetChildTracks())
                    count++;
                return count;
            }
        }
        public class UAnimationTrack
        {
            private readonly Func<int, bool> dg_UsesAbsoluteMotion;
            private readonly Func<object, int, GameObject, bool> dg_RequiresMotionXPlayable;
            private readonly Func<object, GameObject, bool, int> dg_GetOffsetMode;

            private Func<Vector3> dg_GetSceneOffsetPosition;
            private readonly MethodInfo mi_get_sceneOffsetPosition;
            private Func<Vector3> dg_GetSceneOffsetRotation;
            private readonly MethodInfo mi_get_sceneOffsetRotation;
            private Func<bool> dg_AnimatesRootTransform;
            private readonly MethodInfo mi_AnimatesRootTransform;
            private readonly FieldInfo fi_InfiniteClip;
            private readonly FieldInfo fi_InfiniteClipApplyFootIK;
            private readonly Func<object, bool> dg_get_InfiniteClipApplyFootIK;

            public UAnimationTrack()
            {
                Assert.IsNotNull(dg_UsesAbsoluteMotion = ReflectionCommon.CreateConvertingDelegate<Func<int, bool>>(typeof(AnimationTrack).GetMethod("UsesAbsoluteMotion", BindingFlags.NonPublic | BindingFlags.Static)));
                Assert.IsNotNull(dg_RequiresMotionXPlayable = ReflectionCommon.CreateConvertingDelegate<Func<object, int, GameObject, bool>>(typeof(AnimationTrack).GetMethod("RequiresMotionXPlayable", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_GetOffsetMode = ReflectionCommon.CreateConvertingDelegate<Func<object, GameObject, bool, int>>(typeof(AnimationTrack).GetMethod("GetOffsetMode", BindingFlags.NonPublic | BindingFlags.Instance)));

                Assert.IsNotNull(mi_get_sceneOffsetPosition = typeof(AnimationTrack).GetProperty("sceneOffsetPosition", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true));
                Assert.IsNotNull(mi_get_sceneOffsetRotation = typeof(AnimationTrack).GetProperty("sceneOffsetRotation", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true));
                Assert.IsNotNull(mi_AnimatesRootTransform = typeof(AnimationTrack).GetMethod("AnimatesRootTransform", BindingFlags.NonPublic | BindingFlags.Instance));

                Assert.IsNotNull(fi_InfiniteClip = typeof(AnimationTrack).GetField("m_InfiniteClip", BindingFlags.Instance | BindingFlags.NonPublic));
                Assert.IsNotNull(fi_InfiniteClipApplyFootIK = typeof(AnimationTrack).GetField("m_InfiniteClipApplyFootIK", BindingFlags.Instance | BindingFlags.NonPublic));
                Assert.IsNotNull(dg_get_InfiniteClipApplyFootIK = ReflectionCommon.CreateGetFieldDelegate<bool>(fi_InfiniteClipApplyFootIK));
            }

            public bool UsesAbsoluteMotion(int mode) => dg_UsesAbsoluteMotion(mode);

            public Vector3 GetSceneOffsetPosition(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_GetSceneOffsetPosition, instance, mi_get_sceneOffsetPosition);
            public Quaternion GetSceneOffsetRotation(object instance) => Quaternion.Euler(ReflectionCommon.InvokeInstanceDelegate(ref dg_GetSceneOffsetRotation, instance, mi_get_sceneOffsetRotation));
            public bool RequiresMotionXPlayable(object instance, int mode, GameObject gameObject) => dg_RequiresMotionXPlayable(instance, mode, gameObject);
            public int GetOffsetMode(object instance, GameObject go, bool animatesRootTransform) => dg_GetOffsetMode(instance, go, animatesRootTransform);
            public bool AnimatesRootTransform(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_AnimatesRootTransform, instance, mi_AnimatesRootTransform);

            public void SetInfiniteClip(AnimationTrack track, AnimationClip clip) => fi_InfiniteClip.SetValue(track, clip);
            public bool GetInfiniteClipApplyFootIK(AnimationTrack track) => dg_get_InfiniteClipApplyFootIK(track);
        }
        public class UAnimationPlayableAsset
        {
            private Func<bool> dg_get_hasRootTransforms;
            private readonly MethodInfo mi_get_hasRootTransforms;

            public UAnimationPlayableAsset()
            {
                Assert.IsNotNull(mi_get_hasRootTransforms = typeof(AnimationPlayableAsset).GetProperty("hasRootTransforms", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true));
            }

            public virtual bool GetHasRootTransforms(AnimationPlayableAsset instance) => instance != null && ReflectionCommon.InvokeInstanceDelegate(ref dg_get_hasRootTransforms, instance, mi_get_hasRootTransforms);
        }
        public class UTimelineCreateUtilities
        {
            private readonly MethodInfo mi_CreateAnimationClipForTrack;

            public UTimelineCreateUtilities()
            {
                var timelineCreateUtilitiesType = ReflectionCommon.GetUnityEditorType("UnityEngine.Timeline.TimelineCreateUtilities");
                Assert.IsNotNull(timelineCreateUtilitiesType);
                Assert.IsNotNull(mi_CreateAnimationClipForTrack = timelineCreateUtilitiesType.GetMethod("CreateAnimationClipForTrack", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(TrackAsset), typeof(bool) }, null));
            }

            public AnimationClip CreateInfiniteClipAsset(AnimationTrack track, string name)
            {
                return mi_CreateAnimationClipForTrack.Invoke(null, new object[] { string.IsNullOrEmpty(name) ? "Recorded" : name, track, false }) as AnimationClip;
            }
        }
        public class UTimelineAnimationUtilities
        {
            private readonly MethodInfo mi_CreateTimeController;

            public UTimelineAnimationUtilities()
            {
                var timelineAnimationUtilitiesType = ReflectionCommon.GetUnityEditorType("UnityEditor.Timeline.TimelineAnimationUtilities");
                var methods = timelineAnimationUtilitiesType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                foreach (var mi in methods)
                {
                    if (mi.Name != "CreateTimeController") continue;
                    var parameters = mi.GetParameters();
                    if (parameters.Length != 2) continue;
                    if (parameters[0].Name == "state" &&
                        parameters[1].Name == "clip")
                    {
                        mi_CreateTimeController = mi;
                        break;
                    }
                }
                if (mi_CreateTimeController == null)
                {   //Timeline 1.4.0
                    foreach (var mi in methods)
                    {
                        if (mi.Name != "CreateTimeController") continue;
                        var parameters = mi.GetParameters();
                        if (parameters.Length != 1) continue;
                        if (parameters[0].Name == "clip")
                        {
                            mi_CreateTimeController = mi;
                            break;
                        }
                    }
                }
                Assert.IsNotNull(mi_CreateTimeController);
            }

            public object CreateTimeController(object timelineState, TimelineClip clip)
            {
                if (mi_CreateTimeController.GetParameters().Length == 2)
                    return mi_CreateTimeController.Invoke(null, new object[] { timelineState, clip });
                else
                    return mi_CreateTimeController.Invoke(null, new object[] { clip });    //Timeline 1.4.0
            }
        }
        public class UTimelineHelpers
        {
            private readonly MethodInfo mi_CreateTrack;

            public UTimelineHelpers()
            {
                var timelineHelpersType = ReflectionCommon.GetUnityEditorType("UnityEditor.Timeline.TimelineHelpers");
                Assert.IsNotNull(timelineHelpersType);
                Assert.IsNotNull(mi_CreateTrack = timelineHelpersType.GetMethod("CreateTrack", new[] { typeof(Type), typeof(TrackAsset), typeof(string) }));
            }

            public TrackAsset CreateTrack(Type type, TrackAsset parent, string name)
            {
                return mi_CreateTrack.Invoke(null, new object[] { type, parent, name }) as TrackAsset;
            }
        }
        public class UTrackExtensions
        {
            private readonly MethodInfo mi_UnarmForRecord;

            public UTrackExtensions()
            {
                Assert.IsNotNull(mi_UnarmForRecord = typeof(TrackExtensions).GetMethod("UnarmForRecord", BindingFlags.Static | BindingFlags.NonPublic));
            }

            public void UnarmForRecord(TrackAsset track)
            {
                mi_UnarmForRecord.Invoke(null, new object[] { track });
            }
        }
        public class UAnimationTrackExtensions
        {
            private readonly MethodInfo mi_ConvertToClipMode;

            public UAnimationTrackExtensions()
            {
                var animationTrackExtensionsType = ReflectionCommon.GetUnityEditorType("UnityEditor.Timeline.AnimationTrackExtensions");
                Assert.IsNotNull(animationTrackExtensionsType);
                Assert.IsNotNull(mi_ConvertToClipMode = animationTrackExtensionsType.GetMethod("ConvertToClipMode", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
            }

            public void ConvertToClipMode(AnimationTrack track)
            {
                mi_ConvertToClipMode.Invoke(null, new object[] { track });
            }
        }
        public class UExtrapolation
        {
            private readonly MethodInfo mi_CalculateExtrapolationTimes;

            public UExtrapolation()
            {
                var extrapolationType = ReflectionCommon.GetUnityEditorType("UnityEngine.Timeline.Extrapolation");
                Assert.IsNotNull(extrapolationType);
                Assert.IsNotNull(mi_CalculateExtrapolationTimes = extrapolationType.GetMethod("CalculateExtrapolationTimes", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
            }

            public void CalculateExtrapolationTimes(TrackAsset track)
            {
                mi_CalculateExtrapolationTimes.Invoke(null, new object[] { track });
            }
        }
        public class UTimelineClip
        {
            private readonly MethodInfo mi_set_recordable;
            private readonly MethodInfo mi_set_preExtrapolationMode;
            private readonly MethodInfo mi_set_postExtrapolationMode;

            public UTimelineClip()
            {
                Assert.IsNotNull(mi_set_recordable = typeof(TimelineClip).GetProperty("recordable").GetSetMethod(true));
                Assert.IsNotNull(mi_set_preExtrapolationMode = typeof(TimelineClip).GetProperty("preExtrapolationMode").GetSetMethod(true));
                Assert.IsNotNull(mi_set_postExtrapolationMode = typeof(TimelineClip).GetProperty("postExtrapolationMode").GetSetMethod(true));
            }

            public void SetRecordable(TimelineClip clip, bool value)
            {
                mi_set_recordable.Invoke(clip, new object[] { value });
            }
            public void SetPreExtrapolationMode(TimelineClip clip, TimelineClip.ClipExtrapolation value)
            {
                mi_set_preExtrapolationMode.Invoke(clip, new object[] { value });
            }
            public void SetPostExtrapolationMode(TimelineClip clip, TimelineClip.ClipExtrapolation value)
            {
                mi_set_postExtrapolationMode.Invoke(clip, new object[] { value });
            }
        }
        public class UISequenceState
        {
            private Func<PlayableDirector> dg_get_director;
            private readonly MethodInfo mi_get_director;
            private Func<int> dg_get_frame;
            private readonly MethodInfo mi_get_frame;
            private Action<int> dg_set_frame;
            private readonly MethodInfo mi_set_frame;
            private Func<float> dg_get_frameRate;
            private Func<double> dg_get_frameRateDouble;

            public UISequenceState()
            {
                var sequenceStateType = ReflectionCommon.GetUnityEditorType("UnityEditor.Timeline.SequenceState");
                Assert.IsNotNull(mi_get_director = sequenceStateType.GetProperty("director").GetGetMethod());
                Assert.IsNotNull(mi_get_frame = sequenceStateType.GetProperty("frame").GetGetMethod());
                Assert.IsNotNull(mi_set_frame = sequenceStateType.GetProperty("frame").GetSetMethod());
            }

            public PlayableDirector GetDirector(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_director, instance, mi_get_director);

            public int GetFrame(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_frame, instance, mi_get_frame);
            public void SetFrame(object instance, int frame) => ReflectionCommon.InvokeInstanceDelegate(ref dg_set_frame, instance, mi_set_frame, frame);

            public float GetFrameRate(object instance)
            {
                if (instance == null) return 0f;
                if (!(dg_get_frameRate != null && dg_get_frameRate.Target == instance) &&
                    !(dg_get_frameRateDouble != null && dg_get_frameRateDouble.Target == instance))
                {
                    var mi = instance.GetType().GetProperty("frameRate").GetGetMethod();
                    dg_get_frameRate = null;
                    dg_get_frameRateDouble = null;
                    if (mi.ReturnType == typeof(double))
                        dg_get_frameRateDouble = (Func<double>)Delegate.CreateDelegate(typeof(Func<double>), instance, mi);
                    else
                        dg_get_frameRate = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), instance, mi);
                }
                if (dg_get_frameRate != null && dg_get_frameRate.Target == instance)
                    return dg_get_frameRate();
                if (dg_get_frameRateDouble != null && dg_get_frameRateDouble.Target == instance)
                    return (float)dg_get_frameRateDouble();
                return 0f;
            }
        }

        public EditorWindow Instance
        {
            get { return dg_get_instance(); }
        }

        public object State
        {
            get
            {
                if (Instance == null) return null;
                return ReflectionCommon.InvokeInstanceDelegate(ref dg_get_state, Instance, mi_get_state);
            }
        }

        public PlayableDirector GetCurrentDirector()
        {
            return TimelineState.GetCurrentDirector(State);
        }

        public bool GetRecording()
        {
            return TimelineState.GetRecording(State);
        }
        public void SetRecording(bool enable)
        {
            TimelineState.SetRecording(State, enable);
        }

        public bool GetPreviewMode()
        {
            return TimelineState.GetPreviewMode(State);
        }
        public void SetPreviewMode(bool enable)
        {
            TimelineState.SetPreviewMode(State, enable);
        }

        public void SetPlaying(bool enable)
        {
            TimelineState.SetPlaying(State, enable);
        }

        public void EvaluateImmediate()
        {
            TimelineState.EvaluateImmediate(State);
        }
        public void Refresh()
        {
            TimelineState.Refresh(State);
        }

        public void Close()
        {
            if (Instance != null)
                Instance.Close();
        }

        public bool GetLock(EditorWindow aw)
        {
            if (aw == null) return false;
            return EditorGUIUtility.uEditorLockTracker.GetLock(dg_get_m_LockTracker(aw));
        }
        public void SetLock(EditorWindow aw, bool flag)
        {
            if (aw == null) return;
            EditorGUIUtility.uEditorLockTracker.SetLock(dg_get_m_LockTracker(aw), flag);
        }

    }
#endif
}
