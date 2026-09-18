using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Playables;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Animations;


#if VERYANIMATION_TIMELINE
using UnityEngine.Timeline;
#endif

namespace VeryAnimation
{
    internal class UAnimationWindow
    {
        public static UAnimationWindow CreateInstance()
        {
#if UNITY_6000_6_OR_NEWER
            return new UAnimationWindow_6000_6();
#elif UNITY_6000_3_OR_NEWER
            return new UAnimationWindow_6000_3();
#elif UNITY_2023_1_OR_NEWER
            return new UAnimationWindow_2023_1();
#else
            return new UAnimationWindow();
#endif
        }

        protected VeryAnimationWindow VAW => VeryAnimationWindow.instance;

        protected Func<object, IList> dg_get_s_AnimationWindows;
        protected Func<object, object> dg_get_m_AnimEditor;
        protected Func<object, object> dg_get_m_LockTracker;
        protected MethodInfo mi_OnSelectionChange;
        protected MethodInfo mi_EditSequencerClip;

        protected class UAnimEditor
        {
            private readonly MethodInfo mi_get_selection;
            private readonly PropertyInfo pi_triggerFraming;
            private readonly MethodInfo mi_SwitchBetweenCurvesAndDopesheet;
            private readonly MethodInfo mi_UpdateSelectedKeysToCurveEditor;
            private readonly Func<object, object> dg_get_m_State;
            private Func<object> dg_get_selection;
            private Func<object> dg_get_curveEditor;
            private readonly MethodInfo mi_get_curveEditor;

            public UAnimEditor()
            {
                var animEditorType = ReflectionCommon.GetUnityEditorType("UnityEditor.AnimEditor");
                Assert.IsNotNull(mi_get_selection = animEditorType.GetProperty("selection").GetGetMethod());
                Assert.IsNotNull(pi_triggerFraming = animEditorType.GetProperty("triggerFraming", BindingFlags.NonPublic | BindingFlags.Instance));
                Assert.IsNotNull(mi_SwitchBetweenCurvesAndDopesheet = animEditorType.GetMethod("SwitchBetweenCurvesAndDopesheet", BindingFlags.NonPublic | BindingFlags.Instance));
                Assert.IsNotNull(mi_UpdateSelectedKeysToCurveEditor = animEditorType.GetMethod("UpdateSelectedKeysToCurveEditor", BindingFlags.NonPublic | BindingFlags.Instance));
                Assert.IsNotNull(mi_get_curveEditor = animEditorType.GetProperty("curveEditor", BindingFlags.Instance | BindingFlags.NonPublic).GetGetMethod(true));
                Assert.IsNotNull(dg_get_m_State = ReflectionCommon.CreateGetFieldDelegate<object>(animEditorType.GetField("m_State", BindingFlags.NonPublic | BindingFlags.Instance)));
            }

            public object GetAnimationWindowState(object instance)
            {
                if (instance == null) return null;
                return dg_get_m_State(instance);
            }

            public object GetSelection(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_selection, instance, mi_get_selection);
            public void SetTriggerFraming(object instance)
            {
                if (instance == null) return;
                pi_triggerFraming.SetValue(instance, true, null);
            }

            public void SwitchBetweenCurvesAndDopesheet(object instance)
            {
                if (instance == null) return;
                mi_SwitchBetweenCurvesAndDopesheet.Invoke(instance, null);
            }

            public void UpdateSelectedKeysToCurveEditor(object instance)
            {
                if (instance == null) return;
                mi_UpdateSelectedKeysToCurveEditor.Invoke(instance, null);
            }

            public object GetCurveEditor(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_curveEditor, instance, mi_get_curveEditor);
        }
        protected class UCurveEditor
        {
            private Func<bool> dg_get_hasSelection;
            private readonly MethodInfo mi_get_hasSelection;
            private Action dg_ClearSelection;
            private readonly MethodInfo mi_ClearSelection;

            public UCurveEditor()
            {
                var curveEditorType = ReflectionCommon.GetUnityEditorType("UnityEditor.CurveEditor");
                Assert.IsNotNull(mi_get_hasSelection = curveEditorType.GetProperty("hasSelection", BindingFlags.Public | BindingFlags.Instance).GetGetMethod());
                Assert.IsNotNull(mi_ClearSelection = curveEditorType.GetMethod("ClearSelection", BindingFlags.NonPublic | BindingFlags.Instance));
            }

            public bool HasSelection(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_hasSelection, instance, mi_get_hasSelection);
            public void ClearSelection(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_ClearSelection, instance, mi_ClearSelection);
        }
        internal class UAnimationWindowState
        {
            public static UAnimationWindowState CreateInstance()
            {
#if UNITY_6000_6_OR_NEWER
                return new UAnimationWindow_6000_6.UAnimationWindowState_6000_6();
#elif UNITY_6000_3_OR_NEWER
                return new UAnimationWindow_6000_3.UAnimationWindowState_6000_3();
#elif UNITY_2023_1_OR_NEWER
                return new UAnimationWindow_2023_1.UAnimationWindowState_2023_1();
#else
                return new UAnimationWindowState();
#endif
            }

            protected Type animationWindowStateType;
            protected PropertyInfo pi_refresh;
            protected MethodInfo mi_ForceRefresh;
            protected MethodInfo mi_CurveWasModified;
            protected MethodInfo mi_SelectKey;
            protected MethodInfo mi_ClearKeySelections;
            protected MethodInfo mi_ClearHierarchySelection;
            protected MethodInfo mi_SelectHierarchyItem;
            protected MethodInfo mi_UnSelectHierarchyItem;
            protected MethodInfo mi_StartRecording;
            protected MethodInfo mi_StopRecording;
            protected MethodInfo mi_StartPlayback;
            protected MethodInfo mi_StopPlayback;
            protected MethodInfo mi_StartPreview;
            protected MethodInfo mi_StopPreview;
            protected Func<object, bool> dg_get_showCurveEditor;
            protected Func<object, object> dg_get_hierarchyData;
            protected Func<object, bool> dg_get_linkedWithSequencer;
            protected Func<object, IList> dg_get_m_ActiveCurvesCache;
            protected Action<object, IList> dg_set_m_ActiveCurvesCache;
            protected Func<object, IList> dg_get_m_dopelinesCache;
            protected Action<object, IList> dg_set_m_dopelinesCache;
            protected Action<object, EditorCurveBinding?> dg_set_m_lastAddedCurveBinding;
            protected Func<object> dg_get_controlInterface;
            protected Func<GameObject> dg_get_activeRootGameObject;
            protected Func<Component> dg_get_activeAnimationPlayer;
            protected Func<bool> dg_get_playing;
            protected Func<bool> dg_get_recording;
            protected Func<bool> dg_get_previewing;
            protected Func<bool> dg_get_canPreview;
            protected Func<int> dg_get_currentFrame;
            protected Action<int> dg_set_currentFrame;
            protected Func<float> dg_get_currentTime;
            protected Action<float> dg_set_currentTime;
            protected Func<IList> dg_get_allCurves;
            protected Func<IList> dg_get_activeCurves;
            protected Func<IList> dg_get_dopelines;
            protected Func<IEnumerable> dg_get_selectedKeyHashes;
            protected Func<AnimationClip> dg_get_activeAnimationClip;
            protected Action<AnimationClip> dg_set_activeAnimationClip;
            protected Action<object, IList> dg_set_m_AllCurvesCache;
            protected Func<bool> dg_get_filterBySelection;
            protected Action<bool> dg_set_filterBySelection;
            protected Func<bool> dg_get_showReadOnly;
            protected Action<bool> dg_set_showReadOnly;
            protected MethodInfo mi_get_controlInterface;
            protected MethodInfo mi_get_activeRootGameObject;
            protected MethodInfo mi_get_activeAnimationPlayer;
            protected MethodInfo mi_get_playing;
            protected MethodInfo mi_get_recording;
            protected MethodInfo mi_get_previewing;
            protected MethodInfo mi_get_canPreview;
            protected MethodInfo mi_get_currentFrame;
            protected MethodInfo mi_set_currentFrame;
            protected MethodInfo mi_get_currentTime;
            protected MethodInfo mi_set_currentTime;
            protected MethodInfo mi_get_allCurves;
            protected MethodInfo mi_get_activeCurves;
            protected MethodInfo mi_get_dopelines;
            protected MethodInfo mi_get_selectedKeyHashes;
            protected MethodInfo mi_get_activeAnimationClip;
            protected MethodInfo mi_set_activeAnimationClip;
            protected MethodInfo mi_get_filterBySelection;
            protected MethodInfo mi_set_filterBySelection;
            protected MethodInfo mi_get_showReadOnly;
            protected MethodInfo mi_set_showReadOnly;

            public UAnimationWindowState()
            {
                Assert.IsNotNull(animationWindowStateType = ReflectionCommon.GetUnityEditorType("UnityEditorInternal.AnimationWindowState"));
                Assert.IsNotNull(pi_refresh = animationWindowStateType.GetProperty("refresh"));
                mi_ForceRefresh = animationWindowStateType.GetMethod("ForceRefresh");
                Assert.IsNotNull(mi_CurveWasModified = animationWindowStateType.GetMethod("CurveWasModified", BindingFlags.Instance | BindingFlags.NonPublic));
                Assert.IsNotNull(mi_SelectKey = animationWindowStateType.GetMethod("SelectKey"));
                Assert.IsNotNull(mi_ClearKeySelections = animationWindowStateType.GetMethod("ClearKeySelections"));
                Assert.IsNotNull(mi_ClearHierarchySelection = animationWindowStateType.GetMethod("ClearHierarchySelection"));
                Assert.IsNotNull(mi_SelectHierarchyItem = animationWindowStateType.GetMethod("SelectHierarchyItem", new[] { typeof(int), typeof(bool), typeof(bool) }));
                Assert.IsNotNull(mi_UnSelectHierarchyItem = animationWindowStateType.GetMethod("UnSelectHierarchyItem", new[] { typeof(int) }));
                mi_StartRecording = animationWindowStateType.GetMethod("StartRecording");
                mi_StopRecording = animationWindowStateType.GetMethod("StopRecording");
                mi_StartPlayback = animationWindowStateType.GetMethod("StartPlayback");
                mi_StopPlayback = animationWindowStateType.GetMethod("StopPlayback");
                mi_StartPreview = animationWindowStateType.GetMethod("StartPreview");
                mi_StopPreview = animationWindowStateType.GetMethod("StopPreview");
                Assert.IsNotNull(dg_get_showCurveEditor = ReflectionCommon.CreateGetFieldDelegate<bool>(animationWindowStateType.GetField("showCurveEditor")));
                Assert.IsNotNull(dg_get_hierarchyData = ReflectionCommon.CreateGetFieldDelegate<object>(animationWindowStateType.GetField("hierarchyData")));
                Assert.IsNotNull(dg_get_linkedWithSequencer = ReflectionCommon.CreateGetFieldDelegate<bool>(animationWindowStateType.GetField("linkedWithSequencer")));
                Assert.IsNotNull(dg_get_m_ActiveCurvesCache = ReflectionCommon.CreateGetFieldDelegate<IList>(animationWindowStateType.GetField("m_ActiveCurvesCache", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_set_m_ActiveCurvesCache = ReflectionCommon.CreateSetFieldDelegate<IList>(animationWindowStateType.GetField("m_ActiveCurvesCache", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_get_m_dopelinesCache = ReflectionCommon.CreateGetFieldDelegate<IList>(animationWindowStateType.GetField("m_dopelinesCache", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_set_m_dopelinesCache = ReflectionCommon.CreateSetFieldDelegate<IList>(animationWindowStateType.GetField("m_dopelinesCache", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_set_m_lastAddedCurveBinding = ReflectionCommon.CreateSetFieldDelegate<EditorCurveBinding?>(animationWindowStateType.GetField("m_lastAddedCurveBinding", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_set_m_AllCurvesCache = ReflectionCommon.CreateSetFieldDelegate<IList>(animationWindowStateType.GetField("m_AllCurvesCache", BindingFlags.NonPublic | BindingFlags.Instance)));
                mi_get_controlInterface = animationWindowStateType.GetProperty("controlInterface")?.GetGetMethod();
#if !UNITY_6000_3_OR_NEWER
                Assert.IsNotNull(mi_get_controlInterface);
#endif
                Assert.IsNotNull(mi_get_activeRootGameObject = animationWindowStateType.GetProperty("activeRootGameObject").GetGetMethod());
                Assert.IsNotNull(mi_get_activeAnimationPlayer = animationWindowStateType.GetProperty("activeAnimationPlayer").GetGetMethod());
                Assert.IsNotNull(mi_get_playing = animationWindowStateType.GetProperty("playing").GetGetMethod());
                Assert.IsNotNull(mi_get_recording = animationWindowStateType.GetProperty("recording").GetGetMethod());
                Assert.IsNotNull(mi_get_previewing = animationWindowStateType.GetProperty("previewing").GetGetMethod());
                Assert.IsNotNull(mi_get_canPreview = animationWindowStateType.GetProperty("canPreview").GetGetMethod());
                Assert.IsNotNull(mi_get_currentFrame = animationWindowStateType.GetProperty("currentFrame").GetGetMethod());
                Assert.IsNotNull(mi_set_currentFrame = animationWindowStateType.GetProperty("currentFrame").GetSetMethod());
                Assert.IsNotNull(mi_get_currentTime = animationWindowStateType.GetProperty("currentTime").GetGetMethod());
                Assert.IsNotNull(mi_set_currentTime = animationWindowStateType.GetProperty("currentTime").GetSetMethod());
                Assert.IsNotNull(mi_get_allCurves = animationWindowStateType.GetProperty("allCurves").GetGetMethod());
                Assert.IsNotNull(mi_get_activeCurves = animationWindowStateType.GetProperty("activeCurves").GetGetMethod());
                Assert.IsNotNull(mi_get_dopelines = animationWindowStateType.GetProperty("dopelines").GetGetMethod());
                Assert.IsNotNull(mi_get_selectedKeyHashes = animationWindowStateType.GetProperty("selectedKeyHashes", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true));
                Assert.IsNotNull(mi_get_activeAnimationClip = animationWindowStateType.GetProperty("activeAnimationClip").GetGetMethod());
                Assert.IsNotNull(mi_set_activeAnimationClip = animationWindowStateType.GetProperty("activeAnimationClip").GetSetMethod());
                Assert.IsNotNull(mi_get_filterBySelection = animationWindowStateType.GetProperty("filterBySelection").GetGetMethod());
                Assert.IsNotNull(mi_set_filterBySelection = animationWindowStateType.GetProperty("filterBySelection").GetSetMethod());
                Assert.IsNotNull(mi_get_showReadOnly = animationWindowStateType.GetProperty("showReadOnly").GetGetMethod());
                Assert.IsNotNull(mi_set_showReadOnly = animationWindowStateType.GetProperty("showReadOnly").GetSetMethod());
            }

            public enum RefreshType
            {
                None,
                CurvesOnly,
                Everything,
            }

            public bool GetShowCurveEditor(object instance)
            {
                if (instance == null) return false;
                return dg_get_showCurveEditor(instance);
            }
            public object GetHierarchyData(object instance)
            {
                if (instance == null) return null;
                return dg_get_hierarchyData(instance);
            }
            public bool GetLinkedWithSequencer(object instance)
            {
                if (instance == null) return false;
                return dg_get_linkedWithSequencer(instance);
            }
            public virtual object GetControlInterface(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_controlInterface, instance, mi_get_controlInterface);
            public GameObject GetActiveRootGameObject(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_activeRootGameObject, instance, mi_get_activeRootGameObject);
            public Component GetActiveAnimationPlayer(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_activeAnimationPlayer, instance, mi_get_activeAnimationPlayer);
            public RefreshType GetRefresh(object instance)
            {
                if (instance == null) return RefreshType.None;
                return (RefreshType)pi_refresh.GetValue(instance, null);
            }
            public bool GetPlaying(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_playing, instance, mi_get_playing);
            public bool GetRecording(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_recording, instance, mi_get_recording);
            public int GetCurrentFrame(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_currentFrame, instance, mi_get_currentFrame);
            public void SetCurrentFrame(object instance, int value) => ReflectionCommon.InvokeInstanceDelegate(ref dg_set_currentFrame, instance, mi_set_currentFrame, value);
            public float GetCurrentTime(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_currentTime, instance, mi_get_currentTime);
            public void SetCurrentTime(object instance, float value) => ReflectionCommon.InvokeInstanceDelegate(ref dg_set_currentTime, instance, mi_set_currentTime, value);
            public IList GetAllCurves(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_allCurves, instance, mi_get_allCurves);
            public IList GetActiveCurves(object instance)
            {
                if (instance == null) return null;
                //Cache Hit
                var list = dg_get_m_ActiveCurvesCache.Invoke(instance);
                if (list != null)
                    return list;
                //Cache Miss
                list = ReflectionCommon.InvokeInstanceDelegate(ref dg_get_activeCurves, instance, mi_get_activeCurves);
                dg_set_m_ActiveCurvesCache(instance, null);  //Cache Clear
                return list;
            }
            public IList GetDopelines(object instance)
            {
                if (instance == null) return null;
                //Cache Hit
                var list = dg_get_m_dopelinesCache(instance);
                if (list != null)
                    return list;
                //Cache Miss
                list = ReflectionCommon.InvokeInstanceDelegate(ref dg_get_dopelines, instance, mi_get_dopelines);
                dg_set_m_dopelinesCache(instance, null);  //Cache Clear
                return list;
            }
            public virtual void ClearCache(object instance)
            {
                if (instance == null) return;
                dg_set_m_ActiveCurvesCache(instance, null);  //Cache Clear
                dg_set_m_dopelinesCache(instance, null);  //Cache Clear
                dg_set_m_AllCurvesCache(instance, null);  //Cache Clear
            }
            public void ClearLastAddedCurveBinding(object instance)
            {
                if (instance == null) return;
                dg_set_m_lastAddedCurveBinding(instance, null);
            }

            public IEnumerable GetSelectedKeyHashes(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_selectedKeyHashes, instance, mi_get_selectedKeyHashes);

            public void ForceRefresh(object instance)
            {
                if (instance == null) return;
                mi_ForceRefresh.Invoke(instance, null);
            }
            public void SelectKey(object instance, object keyframe)
            {
                if (instance == null) return;
                mi_SelectKey.Invoke(instance, new object[] { keyframe });
            }
            public void ClearKeySelections(object instance)
            {
                if (instance == null) return;
                mi_ClearKeySelections.Invoke(instance, null);
            }
            public void ClearHierarchySelection(object instance)
            {
                if (instance == null) return;
                mi_ClearHierarchySelection.Invoke(instance, null);
            }
            public void SelectHierarchyItem(object instance, int hierarchyNodeID, bool additive, bool triggerSceneSelectionSync)
            {
                if (instance == null) return;
                mi_SelectHierarchyItem.Invoke(instance, new object[] { hierarchyNodeID, additive, triggerSceneSelectionSync });
            }
            public void UnSelectHierarchyItem(object instance, int hierarchyNodeID)
            {
                if (instance == null) return;
                mi_UnSelectHierarchyItem.Invoke(instance, new object[] { hierarchyNodeID });
            }

            public virtual bool StartRecording(object instance)
            {
                if (instance == null) return false;
                try
                {
                    mi_StartRecording.Invoke(instance, null);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public virtual bool StopRecording(object instance)
            {
                if (instance == null) return false;
                try
                {
                    mi_StopRecording.Invoke(instance, null);
                    mi_StopPreview.Invoke(instance, null);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public virtual bool StartPlayback(object instance)
            {
                if (instance == null) return false;
                try
                {
                    mi_StartPlayback.Invoke(instance, null);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public virtual bool StopPlayback(object instance)
            {
                if (instance == null) return false;
                try
                {
                    mi_StopPlayback.Invoke(instance, null);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public virtual bool StartPreview(object instance)
            {
                if (instance == null) return false;
                try
                {
                    mi_StartPreview.Invoke(instance, null);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public virtual bool StopPreview(object instance)
            {
                if (instance == null) return false;
                try
                {
                    mi_StopPreview.Invoke(instance, null);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public virtual void GoToPreviousKeyframe(object instance) { Assert.IsTrue(false); }
            public virtual void GoToNextKeyframe(object instance) { Assert.IsTrue(false); }
            public virtual void GoToFirstKeyframe(object instance) { Assert.IsTrue(false); }
            public virtual void GoToLastKeyframe(object instance) { Assert.IsTrue(false); }
            public virtual void SetFilteredCurves(object instance, IList curves) { Assert.IsTrue(false); }
            public bool GetPreviewing(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_previewing, instance, mi_get_previewing);
            public bool GetCanPreview(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_canPreview, instance, mi_get_canPreview);

            public AnimationClip GetActiveAnimationClip(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_activeAnimationClip, instance, mi_get_activeAnimationClip);
            public void SetActiveAnimationClip(object instance, AnimationClip clip) => ReflectionCommon.InvokeInstanceDelegate(ref dg_set_activeAnimationClip, instance, mi_set_activeAnimationClip, clip);

            public bool GetFilterBySelection(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_filterBySelection, instance, mi_get_filterBySelection);
            public void SetFilterBySelection(object instance, bool enable) => ReflectionCommon.InvokeInstanceDelegate(ref dg_set_filterBySelection, instance, mi_set_filterBySelection, enable);
            public bool GetShowReadOnly(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_showReadOnly, instance, mi_get_showReadOnly);
            public void SetShowReadOnly(object instance, bool enable) => ReflectionCommon.InvokeInstanceDelegate(ref dg_set_showReadOnly, instance, mi_set_showReadOnly, enable);
            public virtual string GetSearchFilter(object instance) => null;
            public virtual void SetSearchFilter(object instance, string filter) { }
            public virtual bool GetEnableQueryBuilder(object instance) => false;
            public virtual void SetEnableQueryBuilder(object instance, bool enable) { }
        }
        internal class UAnimationWindowControl
        {
            public static UAnimationWindowControl CreateInstance()
            {
#if UNITY_6000_3_OR_NEWER
                return new UAnimationWindow_6000_3.UAnimationWindowControl_6000_3();
#elif UNITY_2023_1_OR_NEWER
                return new UAnimationWindow_2023_1.UAnimationWindowControl_2023_1();
#else
                return new UAnimationWindowControl();
#endif
            }

            protected MethodInfo mi_GoToNextKeyframe;
            protected MethodInfo mi_GoToPreviousKeyframe;
            protected MethodInfo mi_GoToFirstKeyframe;
            protected MethodInfo mi_GoToLastKeyframe;
            protected Func<bool> dg_get_canRecord;
            protected MethodInfo mi_get_canRecord;
            protected Func<object, PlayableGraph> dg_get_m_Graph;
            protected Func<object, AnimationClipPlayable> dg_get_m_ClipPlayable;
            protected Func<object, AnimationClipPlayable> dg_get_m_CandidateClipPlayable;
            protected Action<int> dg_ResampleAnimationHasFlag;
            protected MethodInfo mi_ResampleAnimationHasFlag;
            protected Action dg_DestroyGraph;
            protected MethodInfo mi_DestroyGraph;
            protected Func<object, AnimationClipPlayable> dg_get_m_DefaultPosePlayable;

            public UAnimationWindowControl()
            {
                var iAnimationWindowControlType = ReflectionCommon.GetUnityEditorType("UnityEditorInternal.IAnimationWindowControl");
                mi_GoToNextKeyframe = iAnimationWindowControlType.GetMethod("GoToNextKeyframe", Type.EmptyTypes);
                mi_GoToPreviousKeyframe = iAnimationWindowControlType.GetMethod("GoToPreviousKeyframe", Type.EmptyTypes);
                mi_GoToFirstKeyframe = iAnimationWindowControlType.GetMethod("GoToFirstKeyframe", Type.EmptyTypes);
                mi_GoToLastKeyframe = iAnimationWindowControlType.GetMethod("GoToLastKeyframe", Type.EmptyTypes);
                mi_get_canRecord = iAnimationWindowControlType.GetProperty("canRecord").GetGetMethod();
#if !UNITY_2023_1_OR_NEWER
                Assert.IsNotNull(mi_get_canRecord);
#endif
                var animationWindowControlType = ReflectionCommon.GetUnityEditorType("UnityEditorInternal.AnimationWindowControl");
                if (animationWindowControlType != null)
                {
                    Assert.IsNotNull(dg_get_m_Graph = ReflectionCommon.CreateGetFieldDelegate<PlayableGraph>(animationWindowControlType.GetField("m_Graph", BindingFlags.NonPublic | BindingFlags.Instance)));
                    Assert.IsNotNull(dg_get_m_ClipPlayable = ReflectionCommon.CreateGetFieldDelegate<AnimationClipPlayable>(animationWindowControlType.GetField("m_ClipPlayable", BindingFlags.NonPublic | BindingFlags.Instance)));
                    Assert.IsNotNull(dg_get_m_CandidateClipPlayable = ReflectionCommon.CreateGetFieldDelegate<AnimationClipPlayable>(animationWindowControlType.GetField("m_CandidateClipPlayable", BindingFlags.NonPublic | BindingFlags.Instance)));
                    Assert.IsNotNull(dg_get_m_DefaultPosePlayable = ReflectionCommon.CreateGetFieldDelegate<AnimationClipPlayable>(animationWindowControlType.GetField("m_DefaultPosePlayable", BindingFlags.NonPublic | BindingFlags.Instance)));
                    Assert.IsNotNull(mi_ResampleAnimationHasFlag = animationWindowControlType.GetMethod("ResampleAnimation", BindingFlags.NonPublic | BindingFlags.Instance));
                    Assert.IsNotNull(mi_DestroyGraph = animationWindowControlType.GetMethod("DestroyGraph", BindingFlags.NonPublic | BindingFlags.Instance));
                }
            }

            public virtual bool GetCanRecord(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_canRecord, instance, mi_get_canRecord);
            public virtual void ResampleAnimation(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_ResampleAnimationHasFlag, instance, mi_ResampleAnimationHasFlag, 0);
            public void GoToNextKeyframe(object instance)
            {
                if (instance == null) return;
                mi_GoToNextKeyframe.Invoke(instance, null);
            }
            public void GoToPreviousKeyframe(object instance)
            {
                if (instance == null) return;
                mi_GoToPreviousKeyframe.Invoke(instance, null);
            }
            public void GoToFirstKeyframe(object instance)
            {
                if (instance == null) return;
                mi_GoToFirstKeyframe.Invoke(instance, null);
            }
            public void GoToLastKeyframe(object instance)
            {
                if (instance == null) return;
                mi_GoToLastKeyframe.Invoke(instance, null);
            }

            public PlayableGraph GetGraph(object instance)
            {
                if (instance == null) return new PlayableGraph();
                return dg_get_m_Graph(instance);
            }
            public void DestroyGraph(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_DestroyGraph, instance, mi_DestroyGraph);
            public AnimationClipPlayable GetClipPlayable(object instance)
            {
                if (instance == null) return new AnimationClipPlayable();
                return dg_get_m_ClipPlayable(instance);
            }
            public AnimationClipPlayable GetCandidateClipPlayable(object instance)
            {
                if (instance == null) return new AnimationClipPlayable();
                return dg_get_m_CandidateClipPlayable(instance);
            }

            public AnimationClipPlayable GetDefaultPosePlayable(object instance)
            {
                if (instance == null) return new AnimationClipPlayable();
                return dg_get_m_DefaultPosePlayable(instance);
            }
        }
        protected class UAnimationKeyTime
        {
            protected MethodInfo mi_Time;

            public UAnimationKeyTime()
            {
                var animationKeyTimeType = ReflectionCommon.GetUnityEditorType("UnityEditorInternal.AnimationKeyTime");
                mi_Time = animationKeyTimeType.GetMethod("Time", BindingFlags.Public | BindingFlags.Static);
            }
            public object Time(float time, float frameRate)
            {
                return mi_Time.Invoke(null, new object[] { time, frameRate });
            }
        }
        protected class UAnimationWindowCurve
        {
            public Type CurveType { get; private set; }
            protected readonly Func<object, EditorCurveBinding> dg_get_m_Binding;
            protected readonly MethodInfo mi_FindKeyAtTime;

            public UAnimationWindowCurve()
            {
                CurveType = ReflectionCommon.GetUnityEditorType("UnityEditorInternal.AnimationWindowCurve");

                Assert.IsNotNull(dg_get_m_Binding = ReflectionCommon.CreateGetFieldDelegate<EditorCurveBinding>(CurveType.GetField("m_Binding", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(mi_FindKeyAtTime = CurveType.GetMethod("FindKeyAtTime"));
            }

            public EditorCurveBinding GetBinding(object instance)
            {
                if (instance == null) return new EditorCurveBinding();
                return dg_get_m_Binding(instance);
            }
            public object FindKeyAtTime(object instance, object keyTime)
            {
                if (instance == null) return null;
                return mi_FindKeyAtTime.Invoke(instance, new object[] { keyTime });
            }
        }
        internal class UAnimationWindowSelectionItem
        {
            public static UAnimationWindowSelectionItem CreateInstance()
            {
#if UNITY_2023_1_OR_NEWER
                return new UAnimationWindow_2023_1.UAnimationWindowSelectionItem_2023_1();
#else
                return new UAnimationWindowSelectionItem();
#endif
            }

            private Func<IList> dg_get_curves;
            private readonly MethodInfo mi_get_curves;
            protected Action<object, IList> dg_set_m_CurvesCache;
            protected Func<object, IList> dg_get_m_CurvesCache;
            private Action dg_ClearCache;
            private readonly MethodInfo mi_ClearCache;
            private Func<EditorCurveBinding, Type> dg_GetEditorCurveValueType;
            private readonly MethodInfo mi_GetEditorCurveValueType;

            public IList swapDummyCurves;   //It is only used to temporarily replace the actual curve to be displayed.

            public UAnimationWindowSelectionItem()
            {
                var animationWindowSelectionItemType = ReflectionCommon.GetUnityEditorType("UnityEditorInternal.AnimationWindowSelectionItem");
                if (animationWindowSelectionItemType != null)
                {
                    dg_set_m_CurvesCache = ReflectionCommon.CreateSetFieldDelegate<IList>(animationWindowSelectionItemType.GetField("m_CurvesCache", BindingFlags.NonPublic | BindingFlags.Instance));
                    dg_get_m_CurvesCache = ReflectionCommon.CreateGetFieldDelegate<IList>(animationWindowSelectionItemType.GetField("m_CurvesCache", BindingFlags.NonPublic | BindingFlags.Instance));
                    mi_get_curves = animationWindowSelectionItemType.GetProperty("curves")?.GetGetMethod();
                    mi_ClearCache = animationWindowSelectionItemType.GetMethod("ClearCache");
                    mi_GetEditorCurveValueType = animationWindowSelectionItemType.GetMethod("GetEditorCurveValueType");
                }
#if !UNITY_2023_1_OR_NEWER
                Assert.IsNotNull(dg_set_m_CurvesCache);
                Assert.IsNotNull(dg_get_m_CurvesCache);
                Assert.IsNotNull(mi_get_curves);
                Assert.IsNotNull(mi_ClearCache);
                Assert.IsNotNull(mi_GetEditorCurveValueType);
#endif
            }

            public virtual IList GetCurves(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_curves, instance, mi_get_curves);
            public virtual void SetCurvesCache(object instance, IList curves)
            {
                if (instance == null) return;
                dg_set_m_CurvesCache(instance, curves);
            }
            public virtual IList GetCurvesCache(object instance)
            {
                if (instance == null) return null;
                return dg_get_m_CurvesCache(instance);
            }
            public virtual void ClearCurvesCache(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_ClearCache, instance, mi_ClearCache);
            public virtual Type GetEditorCurveValueType(object instance, EditorCurveBinding binding) => ReflectionCommon.InvokeInstanceDelegate(ref dg_GetEditorCurveValueType, instance, mi_GetEditorCurveValueType, binding);
        }
        protected class UAnimationWindowHierarchyDataSource
        {
            private readonly MethodInfo mi_FindItem;
            private readonly MethodInfo mi_UpdateData;

            public UAnimationWindowHierarchyDataSource()
            {
                var animationWindowHierarchyDataSourceType = ReflectionCommon.GetUnityEditorType("UnityEditorInternal.AnimationWindowHierarchyDataSource");
                Assert.IsNotNull(mi_FindItem = animationWindowHierarchyDataSourceType.GetMethod("FindItem", BindingFlags.Public | BindingFlags.Instance));
                Assert.IsNotNull(mi_UpdateData = animationWindowHierarchyDataSourceType.GetMethod("UpdateData", BindingFlags.Public | BindingFlags.Instance));
            }

            public object FindItem(object instance, int id)
            {
                if (instance == null) return null;
                return mi_FindItem.Invoke(instance, new object[] { id });
            }

            public void UpdateData(object instance)
            {
                if (instance == null) return;
                mi_UpdateData.Invoke(instance, null);
            }
        }
        protected class UAnimationWindowHierarchyNode
        {
            private readonly Func<object, IList> dg_get_curves;

            public UAnimationWindowHierarchyNode()
            {
                var animationWindowHierarchyNodeType = ReflectionCommon.GetUnityEditorType("UnityEditorInternal.AnimationWindowHierarchyNode");
                Assert.IsNotNull(dg_get_curves = ReflectionCommon.CreateGetFieldDelegate<IList>(animationWindowHierarchyNodeType.GetField("curves", BindingFlags.Public | BindingFlags.Instance)));
            }

            public IList GetCurves(object instance)
            {
                if (instance == null) return null;
                return dg_get_curves(instance);
            }
        }
        protected class UDopeLine
        {
            private readonly PropertyInfo pi_curves;
            private readonly PropertyInfo pi_hierarchyNodeID;

            public UDopeLine()
            {
                var dopeLineType = ReflectionCommon.GetUnityEditorType("UnityEditorInternal.DopeLine");
                Assert.IsNotNull(pi_curves = dopeLineType.GetProperty("curves"));
                Assert.IsNotNull(pi_hierarchyNodeID = dopeLineType.GetProperty("hierarchyNodeID"));
            }

            public IList GetCurves(object instance)
            {
                if (instance == null) return null;
                return (IList)pi_curves.GetValue(instance, null);
            }
            public int GetHierarchyNodeID(object instance)
            {
                if (instance == null) return -1;
                return (int)pi_hierarchyNodeID.GetValue(instance, null);
            }
        }

        protected UEditorWindow uEditorWindow;
        protected UAnimationWindowUtility uAnimationWindowUtility;
        protected UAnimEditor uAnimEditor;
        protected UCurveEditor uCurveEditor;
        protected UAnimationWindowState uAnimationWindowState;
        protected UAnimationWindowControl uAnimationWindowControl;
        protected UAnimationKeyTime uAnimationKeyTime;
        protected UAnimationWindowCurve uAnimationWindowCurve;
        protected UAnimationWindowSelectionItem uAnimationWindowSelectionItem;
        protected UAnimationWindowHierarchyDataSource uAnimationWindowHierarchyDataSource;
        protected UAnimationWindowHierarchyNode uAnimationWindowHierarchyNode;
        protected UDopeLine uDopeLine;
        protected UAnimationMode uAnimationMode;
        protected UEditorGUIUtility uEditorGUIUtility;
#if VERYANIMATION_TIMELINE
        public UTimelineWindow UTimelineWindow { get; protected set; }
        protected object AnimationTimeWindowControlInstance
        {
            get
            {
                var awc = AnimationWindowControlInstance;
                if (awc != null && awc.GetType() == UTimelineWindow.TimelineWindowTimeControl.ControlType)
                    return awc;
                return null;
            }
        }
#endif

        protected object AnimEditorInstance
        {
            get
            {
                var aw = Instance;
                if (aw == null) return null;
                return dg_get_m_AnimEditor(aw);
            }
        }
        public object AnimationWindowStateInstance
        {
            get
            {
                return uAnimEditor.GetAnimationWindowState(AnimEditorInstance);
            }
        }
        protected object AnimationWindowControlInstance
        {
            get
            {
                return uAnimationWindowState.GetControlInterface(AnimationWindowStateInstance);
            }
        }

        protected object Selection
        {
            get
            {
                var ae = AnimEditorInstance;
                var si = uAnimEditor.GetSelection(ae);
                if (si == null)
                {
                    if (!HasFocus() && Instance != null)
                    {
                        Instance.Focus();
                    }
                    si = uAnimEditor.GetSelection(ae);
                    if (si == null)
                        return null;
                }
                return si;
            }
        }
        public UAnimationWindow()
        {
            var animationWindowType = ReflectionCommon.GetUnityEditorType("UnityEditor.AnimationWindow");

            Assert.IsNotNull(dg_get_s_AnimationWindows = ReflectionCommon.CreateGetFieldDelegate<IList>(animationWindowType.GetField("s_AnimationWindows", BindingFlags.NonPublic | BindingFlags.Static)));
            Assert.IsNotNull(dg_get_m_AnimEditor = ReflectionCommon.CreateGetFieldDelegate<object>(animationWindowType.GetField("m_AnimEditor", BindingFlags.NonPublic | BindingFlags.Instance)));
            Assert.IsNotNull(dg_get_m_LockTracker = ReflectionCommon.CreateGetFieldDelegate<object>(animationWindowType.GetField("m_LockTracker", BindingFlags.NonPublic | BindingFlags.Instance)));
            Assert.IsNotNull(mi_OnSelectionChange = animationWindowType.GetMethod("OnSelectionChange", BindingFlags.NonPublic | BindingFlags.Instance));
            Assert.IsNotNull(mi_EditSequencerClip = animationWindowType.GetMethod("EditSequencerClip", BindingFlags.NonPublic | BindingFlags.Instance));

            uEditorWindow = UEditorWindow.CreateInstance();
            uAnimationWindowUtility = UAnimationWindowUtility.CreateInstance();
            uAnimEditor = new UAnimEditor();
            uCurveEditor = new UCurveEditor();
            uAnimationWindowState = UAnimationWindowState.CreateInstance();
            uAnimationWindowControl = UAnimationWindowControl.CreateInstance();
            uAnimationKeyTime = new UAnimationKeyTime();
            uAnimationWindowCurve = new UAnimationWindowCurve();
            uAnimationWindowSelectionItem = UAnimationWindowSelectionItem.CreateInstance();
            uAnimationWindowHierarchyDataSource = new UAnimationWindowHierarchyDataSource();
            uAnimationWindowHierarchyNode = new UAnimationWindowHierarchyNode();
            uDopeLine = new UDopeLine();
            uAnimationMode = new UAnimationMode();
            uEditorGUIUtility = new UEditorGUIUtility();
#if VERYANIMATION_TIMELINE
            UTimelineWindow = new UTimelineWindow();
#endif
        }

        public EditorWindow Instance
        {
            get
            {
                EditorWindow result = null;
                {
                    var list = dg_get_s_AnimationWindows(null);
                    if (list.Count > 0)
                        result = list[0] as EditorWindow;
                }
                return result;
            }
        }

        public GameObject GetActiveRootGameObject()
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
#if VERYANIMATION_TIMELINE
                var atwc = AnimationTimeWindowControlInstance;
                if (atwc != null)
                {
                    var bindingObject = UTimelineWindow.TimelineWindowTimeControl.GetGenericBinding(atwc);
                    if (bindingObject != null)
                    {
                        if (bindingObject is GameObject)
                        {
                            return bindingObject as GameObject;
                        }
                        else if (bindingObject is Animator)
                        {
                            var animator = bindingObject as Animator;
                            return animator.gameObject;
                        }
                    }
                }
#endif
                return null;
            }
            else
            {
                return uAnimationWindowState.GetActiveRootGameObject(aws);
            }
        }
        public Component GetActiveAnimationPlayer()
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
#if VERYANIMATION_TIMELINE
                var atwc = AnimationTimeWindowControlInstance;
                if (atwc != null)
                {
                    var bindingObject = UTimelineWindow.TimelineWindowTimeControl.GetGenericBinding(atwc);
                    if (bindingObject != null)
                    {
                        if (bindingObject is GameObject)
                        {
                            var gameObject = bindingObject as GameObject;
                            return gameObject.GetComponent<Animator>();
                        }
                        else if (bindingObject is Animator)
                        {
                            return bindingObject as Animator;
                        }
                    }
                }
#endif
                return null;
            }
            else
            {
                return uAnimationWindowState.GetActiveAnimationPlayer(aws);
            }
        }

        public virtual AnimationClip GetSelectionAnimationClip()
        {
            if (Instance == null) return null;
            return uAnimationWindowState.GetActiveAnimationClip(AnimationWindowStateInstance);
        }
        public virtual void SetSelectionAnimationClip(AnimationClip animationClip)
        {
            if (Instance == null) return;
            if (GetSelectionAnimationClip() == animationClip) return;

            var aws = AnimationWindowStateInstance;
            bool playing = uAnimationWindowState.GetPlaying(aws);
            float currentTime = uAnimationWindowState.GetCurrentTime(aws);
            {
                uAnimationWindowState.SetActiveAnimationClip(aws, animationClip);
            }
            uAnimationWindowState.SetCurrentTime(aws, currentTime);
            if (playing)
                uAnimationWindowState.StartPlayback(aws);

            ForceRefresh();
        }

        public void CleanAnimationModeEvents()
        {
            //Added to infer that there may be an error due to remaining actions for deleted windows.
            var onStart = uAnimationMode.GetOnAnimationRecordingStart();
            if (TryRemoveDeadPanelDelegates(ref onStart))
                uAnimationMode.SetOnAnimationRecordingStart(onStart);

            var onStop = uAnimationMode.GetOnAnimationRecordingStop();
            if (TryRemoveDeadPanelDelegates(ref onStop))
                uAnimationMode.SetOnAnimationRecordingStop(onStop);
        }

        private static bool TryRemoveDeadPanelDelegates(ref Action callback)
        {
            if (callback == null) return false;
            bool changed = false;
            foreach (var del in callback.GetInvocationList())
            {
                if (del.Target == null)
                {
                    callback -= (Action)del;
                    changed = true;
                    continue;
                }
                var fi = del.Target.GetType().GetField("m_Panel", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fi == null) continue;
                var panel = fi.GetValue(del.Target);
                if (panel == null || panel.GetType().GetProperty("visualTree").GetValue(panel) == null)
                {
                    callback -= (Action)del;
                    changed = true;
                }
            }
            return changed;
        }

        public void StopAllRecording()
        {
            var list = dg_get_s_AnimationWindows(null);
            foreach (var aw in list)
            {
                if (aw == null) continue;

                var ae = dg_get_m_AnimEditor(aw);
                if (ae == null) continue;

                var aws = uAnimEditor.GetAnimationWindowState(ae);
                if (aws == null) continue;

                if (uAnimationWindowState.GetRecording(aws))
                {
                    uAnimationWindowState.StopRecording(aws);
                }
                else if (GetPreviewing(aws))
                {
                    uAnimationWindowState.StopPreview(aws);
                }
            }
        }
        public void StopRecording()
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetRecording(aws))
            {
                uAnimationWindowState.StopRecording(aws);
            }
            else if (GetPreviewing(aws))
            {
                uAnimationWindowState.StopPreview(aws);
            }
        }
        public bool StartRecording()
        {
            var aws = AnimationWindowStateInstance;
            if (GetCanRecord())
            {
                if (!uAnimationWindowState.GetRecording(aws))
                {
                    if (!uAnimationWindowState.StartRecording(aws))
                        return false;
                }
            }
            else if (GetCanPreview())
            {
                if (!uAnimationWindowState.GetPreviewing(aws))
                {
                    if (!uAnimationWindowState.StartPreview(aws))
                        return false;
                }
            }
            return true;
        }
        public bool GetCanRecord()
        {
            return uAnimationWindowControl.GetCanRecord(AnimationWindowControlInstance);
        }
        public bool GetRecording()
        {
            return uAnimationWindowState.GetRecording(AnimationWindowStateInstance);
        }
        public void StartPreviewing()
        {
            var aws = AnimationWindowStateInstance;
            if (GetPreviewing(aws))
                return;
            uAnimationWindowState.StartPreview(aws);
        }
        public void StopPreviewing()
        {
            var aws = AnimationWindowStateInstance;
            if (!GetPreviewing(aws))
                return;
            uAnimationWindowState.StopPreview(aws);
        }
        public bool GetCanPreview()
        {
            return uAnimationWindowState.GetCanPreview(AnimationWindowStateInstance);
        }
        public bool GetPreviewing()
        {
            return GetPreviewing(AnimationWindowStateInstance);
        }
        private bool GetPreviewing(object aws)
        {
#if VERYANIMATION_TIMELINE
            if (uAnimationWindowState.GetLinkedWithSequencer(aws) && UTimelineWindow.Instance != null)
                return UTimelineWindow.GetPreviewMode();
#endif
            return uAnimationWindowState.GetPreviewing(aws);
        }

        public void PlayingChange()
        {
            var aws = AnimationWindowStateInstance;
            if (!HasFocus() && Instance != null)
                Instance.Focus();
            var playing = uAnimationWindowState.GetPlaying(aws);
            playing = !playing;
            if (playing)
                uAnimationWindowState.StartPlayback(aws);
            else
                uAnimationWindowState.StopPlayback(aws);
        }
        public bool GetPlaying()
        {
            return uAnimationWindowState.GetPlaying(AnimationWindowStateInstance);
        }

        public int GetCurrentFrame()
        {
            return uAnimationWindowState.GetCurrentFrame(AnimationWindowStateInstance);
        }
        public void SetCurrentFrame(int frame)
        {
            uAnimationWindowState.SetCurrentFrame(AnimationWindowStateInstance, frame);
            Repaint();
        }
        public void MoveFrame(int add)
        {
            var clip = GetSelectionAnimationClip();
            var time = EditorCommon.SnapToFrame(GetCurrentTime(), clip.frameRate);
            var addTime = GetFrameTime(add, clip);
            SetCurrentTime(time + addTime);
        }
        public float GetFrameTime(int frame, AnimationClip clip)
        {
            return EditorCommon.SnapToFrame(frame * (1f / clip.frameRate), clip.frameRate);
        }

        public float GetCurrentTime()
        {
            return uAnimationWindowState.GetCurrentTime(AnimationWindowStateInstance);
        }
        public void SetCurrentTime(float time)
        {
            time = EditorCommon.SnapToFrame(time, GetSelectionAnimationClip().frameRate);
            uAnimationWindowState.SetCurrentTime(AnimationWindowStateInstance, time);
            Repaint();
        }

        public void MoveToNextFrame()
        {
            MoveFrame(1);
        }
        public void MoveToPrevFrame()
        {
            MoveFrame(-1);
        }
        public virtual void MoveToNextKeyframe()
        {
            uAnimationWindowControl.GoToNextKeyframe(AnimationWindowControlInstance);
            Repaint();
        }
        public virtual void MoveToPreviousKeyframe()
        {
            uAnimationWindowControl.GoToPreviousKeyframe(AnimationWindowControlInstance);
            Repaint();
        }
        public virtual void MoveToFirstKeyframe()
        {
            uAnimationWindowControl.GoToFirstKeyframe(AnimationWindowControlInstance);
            Repaint();
        }
        public virtual void MoveToLastKeyframe()
        {
            uAnimationWindowControl.GoToLastKeyframe(AnimationWindowControlInstance);
            Repaint();
        }

        public void SwitchBetweenCurvesAndDopesheet()
        {
            var ae = AnimEditorInstance;
            uAnimEditor.SwitchBetweenCurvesAndDopesheet(ae);
            if (VAW.EditorSettings.SettingAutorunFrameAll)
            {
                uAnimEditor.SetTriggerFraming(ae);
            }
            Repaint();
        }
        public bool IsShowCurveEditor()
        {
            return uAnimationWindowState.GetShowCurveEditor(AnimationWindowStateInstance);
        }

        public void ClearKeySelections()
        {
            var ae = AnimEditorInstance;
            var aws = AnimationWindowStateInstance;
            if (ae == null || aws == null)
                return;
            if (IsShowCurveEditor())
            {
                var curveEditor = uAnimEditor.GetCurveEditor(ae);
                if (curveEditor != null)
                {
                    if (uCurveEditor.HasSelection(curveEditor))
                    {
                        uCurveEditor.ClearSelection(curveEditor);
                        Repaint();
                    }
                }
            }
            else
            {
                var list = uAnimationWindowState.GetSelectedKeyHashes(aws);
                if (list != null)
                {
                    var e = list.GetEnumerator();
                    if (e.MoveNext())
                    {
                        uAnimationWindowState.ClearKeySelections(aws);
                        Repaint();
                    }
                }
            }
        }

        public virtual void PropertySortOrFilterByBindings(List<EditorCurveBinding> bindings)
        {
            var aws = AnimationWindowStateInstance;
            var si = Selection;
            if (aws == null || si == null)
                return;
            var hierarchyData = uAnimationWindowState.GetHierarchyData(aws);
            if (hierarchyData == null)
                return;

            uAnimationWindowState.ClearCache(aws);
            if (bindings != null && bindings.Count > 0)
            {
                var bindingSet = new HashSet<EditorCurveBinding>(bindings);
                var selectionItemCurves = uAnimationWindowSelectionItem.GetCurves(si);
                uAnimationWindowSelectionItem.swapDummyCurves = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(uAnimationWindowCurve.CurveType));
                {
                    foreach (var curve in selectionItemCurves)
                    {
                        var binding = uAnimationWindowCurve.GetBinding(curve);
                        if (!bindingSet.Contains(binding))
                            continue;
                        uAnimationWindowSelectionItem.swapDummyCurves.Add(curve);
                    }
                }
                uAnimationWindowSelectionItem.SetCurvesCache(si, uAnimationWindowSelectionItem.swapDummyCurves);
                uAnimationWindowHierarchyDataSource.UpdateData(hierarchyData);
                uAnimationWindowSelectionItem.SetCurvesCache(si, selectionItemCurves);
            }
            else
            {
                uAnimationWindowSelectionItem.swapDummyCurves = null;
                uAnimationWindowHierarchyDataSource.UpdateData(hierarchyData);
            }

            Repaint();
        }
        public bool IsSelectedItemCurvesDummySwapped => uAnimationWindowSelectionItem.swapDummyCurves != null;
        public bool ContainsSelectedItemCurvesDummySwapped(EditorCurveBinding binding)
        {
            if (uAnimationWindowSelectionItem.swapDummyCurves == null)
                return false;
            foreach (var curve in uAnimationWindowSelectionItem.swapDummyCurves)
            {
                var cbinding = uAnimationWindowCurve.GetBinding(curve);
                if (cbinding == binding)
                    return true;
                //RawQuaternions are displayed as NonBaked in the AnimationWindow, so special care is required.
                if (cbinding.type == typeof(Transform) && binding.type == typeof(Transform) && cbinding.path == binding.path &&
                    cbinding.propertyName.StartsWith(URotationCurveInterpolation.PrefixForInterpolation[(int)URotationCurveInterpolation.Mode.NonBaked], StringComparison.Ordinal) &&
                    binding.propertyName.StartsWith(URotationCurveInterpolation.PrefixForInterpolation[(int)URotationCurveInterpolation.Mode.RawQuaternions], StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
        public void ClearSelectedItemCurvesDummySwapped()
        {
            if (uAnimationWindowSelectionItem.swapDummyCurves == null)
                return;
            uAnimationWindowSelectionItem.swapDummyCurves = null;
            ForceRefresh();
        }

        public void SynchroCurveSelection(List<EditorCurveBinding> bindings)
        {
            var ae = AnimEditorInstance;
            var aws = AnimationWindowStateInstance;
            if (ae == null || aws == null)
                return;

            uAnimationWindowState.ClearKeySelections(aws);
            uAnimationWindowState.ClearHierarchySelection(aws);
            uAnimationWindowState.ClearLastAddedCurveBinding(aws);

            if (bindings.Count > 0)
            {
                var bindingSet = new HashSet<EditorCurveBinding>(bindings);
                var animationKeyTime = uAnimationKeyTime.Time(GetCurrentTime(), GetSelectionAnimationClip().frameRate);
                foreach (object dopeline in uAnimationWindowState.GetDopelines(aws))
                {
                    foreach (var curve in uDopeLine.GetCurves(dopeline))
                    {
                        var cbinding = uAnimationWindowCurve.GetBinding(curve);
                        if (bindingSet.Contains(cbinding))
                        {
                            uAnimationWindowState.SelectHierarchyItem(aws, uDopeLine.GetHierarchyNodeID(dopeline), true, false);
                            var keyframe = uAnimationWindowCurve.FindKeyAtTime(curve, animationKeyTime);
                            if (keyframe != null)
                                uAnimationWindowState.SelectKey(aws, keyframe);
                        }
                    }
                }
                if (IsShowCurveEditor())
                {
                    uAnimEditor.UpdateSelectedKeysToCurveEditor(ae);
                }
            }

            if (VAW.EditorSettings.SettingAutorunFrameAll)
            {
                uAnimEditor.SetTriggerFraming(ae);
            }

            Repaint();
        }

        public List<EditorCurveBinding> GetCurveSelection()
        {
            var list = new List<EditorCurveBinding>();
            var activeCurves = uAnimationWindowState.GetActiveCurves(AnimationWindowStateInstance);
            if (activeCurves == null)
                return list;
            foreach (var curve in activeCurves)
            {
                var cbinding = uAnimationWindowCurve.GetBinding(curve);
                list.Add(cbinding);
            }
            return list;
        }
        public bool GetAllCurveBindings(List<EditorCurveBinding> list)
        {
            if (list == null)
                return false;
            list.Clear();
            var allCurves = uAnimationWindowState.GetAllCurves(AnimationWindowStateInstance);
            if (allCurves == null)
                return false;
            if (list.Capacity < allCurves.Count)
                list.Capacity = allCurves.Count;
            foreach (var curve in allCurves)
            {
                var binding = uAnimationWindowCurve.GetBinding(curve);
                list.Add(binding);
            }
            return true;
        }

        public List<EditorCurveBinding> GetMissingCurveBindings()
        {
            var aws = AnimationWindowStateInstance;
            List<EditorCurveBinding> list = new();
            var hierarchyData = uAnimationWindowState.GetHierarchyData(aws);
            foreach (object dopeline in uAnimationWindowState.GetDopelines(aws))
            {
                var hierarchyNodeID = uDopeLine.GetHierarchyNodeID(dopeline);
                var windowHierarchyNode = uAnimationWindowHierarchyDataSource.FindItem(hierarchyData, hierarchyNodeID);
                if (windowHierarchyNode == null) continue;
                if (uAnimationWindowUtility.IsNodeLeftOverCurve(aws, windowHierarchyNode))
                {
                    var curves = uAnimationWindowHierarchyNode.GetCurves(windowHierarchyNode);
                    if (curves == null) continue;
                    foreach (var curve in curves)
                    {
                        if (curve == null) continue;
                        var binding = uAnimationWindowCurve.GetBinding(curve);
                        list.Add(binding);
                    }
                }
            }
            return list;
        }

        public void GetNearKeyframeTimes(float[] nextTimes, float[] prevTimes)
        {
            var aws = AnimationWindowStateInstance;
            Array curves;
            {
                var list = uAnimationWindowState.GetAllCurves(aws);
                curves = Array.CreateInstance(uAnimationWindowCurve.CurveType, list.Count);
                list.CopyTo(curves, 0);
            }
            var frameRate = GetSelectionAnimationClip().frameRate;
            if (nextTimes != null)
            {
                var time = GetCurrentTime();
                for (int i = 0; i < nextTimes.Length; i++)
                {
                    nextTimes[i] = uAnimationWindowUtility.GetNextKeyframeTime(curves, time, frameRate);
                    if (time != nextTimes[i])
                        time = nextTimes[i];
                    else
                        nextTimes[i] = -1f;
                }
            }
            if (prevTimes != null)
            {
                var time = GetCurrentTime();
                for (int i = 0; i < prevTimes.Length; i++)
                {
                    prevTimes[i] = uAnimationWindowUtility.GetPreviousKeyframeTime(curves, time, frameRate);
                    if (time != prevTimes[i])
                        time = prevTimes[i];
                    else
                        prevTimes[i] = -1f;
                }
            }
        }

        public bool IsDoneRefresh()
        {
            var refresh = uAnimationWindowState.GetRefresh(AnimationWindowStateInstance);
            return refresh == UAnimationWindowState.RefreshType.None;
        }
        public void ForceRefresh()
        {
            uAnimationWindowState.ForceRefresh(AnimationWindowStateInstance);
            Repaint();
        }

        public void ResampleAnimation()
        {
            uAnimationWindowControl.ResampleAnimation(AnimationWindowControlInstance);
        }

        public void Repaint()
        {
            if (!HasFocus())
                return;

            var list = dg_get_s_AnimationWindows(null);
            if (list.Count > 0)
            {
                (list[0] as EditorWindow).Repaint();
                #region OtherAnimationWindows
                if (list.Count > 1)
                {
                    var clip = GetSelectionAnimationClip();
                    for (int i = 1; i < list.Count; i++)
                    {
                        var ew = list[i] as EditorWindow;
                        if (ew.hasFocus)
                        {
                            var ae = dg_get_m_AnimEditor(ew);
                            var aws = uAnimEditor.GetAnimationWindowState(ae);
                            var sclip = uAnimationWindowState.GetActiveAnimationClip(aws);
                            if (clip == sclip)
                            {
                                uAnimationWindowState.ForceRefresh(aws);
                                ew.Repaint();
                            }
                        }
                    }
                }
                #endregion
            }
        }

        public bool HasFocus() => Instance != null && Instance.hasFocus;

        public void Close()
        {
            if (Instance == null)
                return;
            Instance.Close();
        }

        public bool GetLock(EditorWindow aw) => uEditorGUIUtility.uEditorLockTracker.GetLock(dg_get_m_LockTracker(aw));
        public void SetLock(EditorWindow aw, bool flag) => uEditorGUIUtility.uEditorLockTracker.SetLock(dg_get_m_LockTracker(aw), flag);

        public bool GetFilterBySelection() => uAnimationWindowState.GetFilterBySelection(AnimationWindowStateInstance);
        public void SetFilterBySelection(bool enable) => uAnimationWindowState.SetFilterBySelection(AnimationWindowStateInstance, enable);
        public bool GetShowReadOnly() => uAnimationWindowState.GetShowReadOnly(AnimationWindowStateInstance);
        public void SetShowReadOnly(bool enable) => uAnimationWindowState.SetShowReadOnly(AnimationWindowStateInstance, enable);
        public string GetSearchFilter() => uAnimationWindowState.GetSearchFilter(AnimationWindowStateInstance);
        public void SetSearchFilter(string filter) => uAnimationWindowState.SetSearchFilter(AnimationWindowStateInstance, filter);
        public bool GetEnableQueryBuilder() => uAnimationWindowState.GetEnableQueryBuilder(AnimationWindowStateInstance);
        public void SetEnableQueryBuilder(bool enable) => uAnimationWindowState.SetEnableQueryBuilder(AnimationWindowStateInstance, enable);

        public void OnSelectionChange()
        {
            if (Instance == null) return;
            mi_OnSelectionChange.Invoke(Instance, null);
        }

        public bool GetRemoveStartOffset()
        {
#if VERYANIMATION_TIMELINE
            if (GetLinkedWithTimeline())
                return GetTimelineAnimationRemoveStartOffset();
#endif
            return false;
        }

        public PlayableGraph GetPlayableGraph()
        {
            return uAnimationWindowControl.GetGraph(AnimationWindowControlInstance);
        }
        public void DestroyPlayableGraph()
        {
            uAnimationWindowControl.DestroyGraph(AnimationWindowControlInstance);
        }
        public AnimationClipPlayable GetClipPlayable()
        {
            return uAnimationWindowControl.GetClipPlayable(AnimationWindowControlInstance);
        }
        public AnimationClipPlayable GetCandidateClipPlayable()
        {
            return uAnimationWindowControl.GetCandidateClipPlayable(AnimationWindowControlInstance);
        }
        public AnimationClipPlayable GetDefaultPosePlayable()
        {
            return uAnimationWindowControl.GetDefaultPosePlayable(AnimationWindowControlInstance);
        }
        public AnimationLayerMixerPlayable GetLayerMixerPlayable()
        {
            var playable = GetClipPlayable().GetOutput(0);
            while (playable.IsValid())
            {
                if (playable.GetPlayableType() == typeof(AnimationLayerMixerPlayable))
                    return (AnimationLayerMixerPlayable)playable;
                playable = playable.GetOutput(0);
            }
            return AnimationLayerMixerPlayable.Null;
        }

        public bool GetLinkedWithTimeline()
        {
#if VERYANIMATION_TIMELINE
            return uAnimationWindowState.GetLinkedWithSequencer(AnimationWindowStateInstance);
#else
            return false;
#endif
        }
#if VERYANIMATION_TIMELINE
        public bool GetTimelineTrackAssetEditable()
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
                var atwc = AnimationTimeWindowControlInstance;
                if (atwc != null)
                {
                    var trackAsset = UTimelineWindow.TimelineWindowTimeControl.GetTrackAsset(atwc);
                    if (trackAsset != null && !trackAsset.muted)
                    {
                        var locked = UTimelineWindow.TrackAsset.GetLocked(trackAsset);
                        if (!locked)
                            return true;
                    }
                }
            }
            return false;
        }
        public bool GetTimelineHasFocus()
        {
            var timelineWindow = UTimelineWindow.Instance;
            return timelineWindow != null && timelineWindow.hasFocus;
        }

        public bool GetTimelineRecording()
        {
            return UTimelineWindow.GetRecording();
        }
        public void SetTimelineRecording(bool enable)
        {
            UTimelineWindow.SetRecording(enable);
        }

        public bool GetTimelinePreviewMode()
        {
            return UTimelineWindow.GetPreviewMode();
        }
        public void SetTimelinePreviewMode(bool enable)
        {
            UTimelineWindow.SetPreviewMode(enable);
        }

        public void SetTimelinePlaying(bool enable)
        {
            UTimelineWindow.SetPlaying(enable);
        }

        public AnimationClip GetTimelineAnimationClip()
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
                var atwc = AnimationTimeWindowControlInstance;
                if (atwc != null)
                {
                    return UTimelineWindow.TimelineWindowTimeControl.GetAnimationClip(atwc);
                }
            }
            return null;
        }
        public void SetTimelineAnimationClip(AnimationClip clip, string undoName = null)
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
                var atwc = AnimationTimeWindowControlInstance;
                if (atwc != null)
                {
                    UTimelineWindow.TimelineWindowTimeControl.SetAnimationClip(atwc, clip, undoName);
                }
            }
        }

        public void GetTimelineAnimationTrackInfo(out bool animatesRootTransform, out bool requiresMotionXPlayable, out bool usesAbsoluteMotion)
        {
            animatesRootTransform = false;
            requiresMotionXPlayable = false;
            usesAbsoluteMotion = false;

            var animationTrack = GetTimelineAnimationTrack(true);
            if (animationTrack == null)
                return;
            var go = GetActiveRootGameObject();

            animatesRootTransform = UTimelineWindow.AnimationTrack.AnimatesRootTransform(animationTrack);
            var mode = UTimelineWindow.AnimationTrack.GetOffsetMode(animationTrack, go, animatesRootTransform);
            requiresMotionXPlayable = UTimelineWindow.AnimationTrack.RequiresMotionXPlayable(animationTrack, mode, go);
            usesAbsoluteMotion = UTimelineWindow.AnimationTrack.UsesAbsoluteMotion(mode);
        }
        public bool GetTimelineRootMotionOffsets(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            //Track Offsets
            var animationTrack = GetTimelineAnimationTrack(true);
            {
                if (animationTrack == null)
                    return false;

                var hasRootTransforms = UTimelineWindow.AnimationTrack.AnimatesRootTransform(animationTrack);
                if (!hasRootTransforms)
                    return false;

                if (animationTrack.trackOffset == TrackOffset.Auto || animationTrack.trackOffset == TrackOffset.ApplyTransformOffsets)
                {
                    position = animationTrack.position;
                    rotation = animationTrack.rotation;
                }
                else if (animationTrack.trackOffset == TrackOffset.ApplySceneOffsets)
                {
                    position = UTimelineWindow.AnimationTrack.GetSceneOffsetPosition(animationTrack);
                    rotation = UTimelineWindow.AnimationTrack.GetSceneOffsetRotation(animationTrack);
                }
            }

            //Clip Offsets
            {
                var animationPlayableAsset = GetTimelineAnimationPlayableAsset();
                if (animationPlayableAsset != null)
                {
                    position += rotation * animationPlayableAsset.position;
                    rotation *= animationPlayableAsset.rotation;
                }
                else
                {
                    animationTrack = GetTimelineAnimationTrack(false);
                    position += rotation * animationTrack.infiniteClipOffsetPosition;
                    rotation *= animationTrack.infiniteClipOffsetRotation;
                }
            }

            return true;
        }
        public bool GetTimelineApplyFootIK()
        {
            var animationPlayableAsset = GetTimelineAnimationPlayableAsset();
            if (animationPlayableAsset != null)
            {
                return animationPlayableAsset.applyFootIK;
            }
            else
            {
                var animationTrack = GetTimelineAnimationTrack(false);
                return UTimelineWindow.AnimationTrack.GetInfiniteClipApplyFootIK(animationTrack);
            }
        }

        public int GetTimelineFrame()
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
                var atwc = AnimationTimeWindowControlInstance;
                if (atwc != null)
                {
                    var state = UTimelineWindow.TimelineWindowTimeControl.GetTimelineState(atwc);
                    return UTimelineWindow.TimelineState.GetFrame(state);
                }
            }
            return 0;
        }
        public void SetTimelineFrame(int frame)
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
                var atwc = AnimationTimeWindowControlInstance;
                if (atwc != null)
                {
                    var state = UTimelineWindow.TimelineWindowTimeControl.GetTimelineState(atwc);
                    UTimelineWindow.TimelineState.SetFrame(state, frame);
                }
            }
        }

        public float GetTimelineFrameRate()
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
                var atwc = AnimationTimeWindowControlInstance;
                if (atwc != null)
                {
                    var state = UTimelineWindow.TimelineWindowTimeControl.GetTimelineState(atwc);
                    return UTimelineWindow.TimelineState.GetFrameRate(state);
                }
            }
            return 0f;
        }

        public bool IsTimelineArmedForRecord()
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
                var awc = AnimationWindowControlInstance;
                if (awc != null && awc.GetType() == UTimelineWindow.TimelineWindowTimeControl.ControlType)
                {
                    return UTimelineWindow.TimelineWindowTimeControl.IsArmedForRecord(awc);
                }
            }
            return false;
        }

        public bool EditSequencerClip(TimelineClip timelineClip)
        {
            var sourceObject = GetActiveRootGameObject();
            object controlInterface = UTimelineWindow.TimelineAnimationUtilities.CreateTimeController(UTimelineWindow.State, timelineClip);
#pragma warning disable IDE0029
            return (bool)mi_EditSequencerClip.Invoke(Instance, new object[] { timelineClip.animationClip != null ? timelineClip.animationClip : timelineClip.curves, sourceObject, controlInterface });
#pragma warning restore IDE0029
        }

        public PlayableDirector GetTimelineCurrentDirector()
        {
            return UTimelineWindow.GetCurrentDirector();
        }

        public AnimationTrack GetTimelineAnimationTrack(bool top = false)
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
                var atwc = AnimationTimeWindowControlInstance;
                if (atwc != null)
                {
                    var animationTrack = UTimelineWindow.TimelineWindowTimeControl.GetTrackAsset(atwc) as AnimationTrack;
                    if (animationTrack != null && top)
                    {
                        while (animationTrack.parent is AnimationTrack)
                        {
                            var track = animationTrack.parent as AnimationTrack;
                            if (track == null)
                                break;
                            animationTrack = track;
                        }
                    }
                    return animationTrack;
                }
            }
            return null;
        }
        public AnimationTrack CreateTimelineOverrideTrack()
        {
            var parentTrack = GetTimelineAnimationTrack(true);
            if (parentTrack == null)
                return null;
            return UTimelineWindow.TimelineHelpers.CreateTrack(parentTrack.GetType(), parentTrack, $"Override {UTimelineWindow.UTrackAsset.GetChildTrackCount(parentTrack)}") as AnimationTrack;
        }
        public TimelineClip GetTimelineClip()
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
                var atwc = AnimationTimeWindowControlInstance;
                if (atwc != null)
                {
                    return UTimelineWindow.TimelineWindowTimeControl.GetTimelineClip(atwc);
                }
            }
            return null;
        }
        public AnimationPlayableAsset GetTimelineAnimationPlayableAsset()
        {
            var aws = AnimationWindowStateInstance;
            if (uAnimationWindowState.GetLinkedWithSequencer(aws))
            {
                var atwc = AnimationTimeWindowControlInstance;
                if (atwc != null)
                {
                    return UTimelineWindow.TimelineWindowTimeControl.GetPlayableAsset(atwc) as AnimationPlayableAsset;
                }
            }
            return null;
        }
        public bool GetTimelineAnimationPlayableAssetHasRootTransforms()
        {
            var animationPlayableAsset = GetTimelineAnimationPlayableAsset();
            if (animationPlayableAsset == null)
                return false;
            return UTimelineWindow.AnimationPlayableAsset.GetHasRootTransforms(animationPlayableAsset);
        }
        public bool GetTimelineAnimationRemoveStartOffset()
        {
            var animationPlayableAsset = GetTimelineAnimationPlayableAsset();
            if (animationPlayableAsset != null)
                return animationPlayableAsset.removeStartOffset;
            else
                return false;
        }
#endif
    }
}
