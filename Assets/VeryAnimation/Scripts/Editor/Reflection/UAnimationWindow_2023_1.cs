#if UNITY_2023_1_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal class UAnimationWindow_2023_1 : UAnimationWindow    //2023.1 or later
    {
        internal class UAnimationWindowState_2023_1 : UAnimationWindowState
        {
            protected PropertyInfo pi_recording;
            protected PropertyInfo pi_playing;
            protected PropertyInfo pi_previewing;
            protected MethodInfo mi_GoToPreviousKeyframe;
            protected MethodInfo mi_GoToNextKeyframe;
            protected MethodInfo mi_GoToFirstKeyframe;
            protected MethodInfo mi_GoToLastKeyframe;
            protected Action<object, bool> dg_set_m_AllCurvesCacheDirty;
            protected Action<object, bool> dg_set_m_FilteredCurvesCacheDirty;
            protected Action<object, bool> dg_set_m_ActiveCurvesCacheDirty;
            protected Action<object, IList> dg_set_m_FilteredCurvesCache;

            public UAnimationWindowState_2023_1() : base()
            {
                Assert.IsNotNull(pi_recording = animationWindowStateType.GetProperty("recording"));
                Assert.IsNotNull(pi_playing = animationWindowStateType.GetProperty("playing"));
                Assert.IsNotNull(pi_previewing = animationWindowStateType.GetProperty("previewing"));
                Assert.IsNotNull(mi_GoToPreviousKeyframe = animationWindowStateType.GetMethod("GoToPreviousKeyframe"));
                Assert.IsNotNull(mi_GoToNextKeyframe = animationWindowStateType.GetMethod("GoToNextKeyframe"));
                Assert.IsNotNull(mi_GoToFirstKeyframe = animationWindowStateType.GetMethod("GoToFirstKeyframe"));
                Assert.IsNotNull(mi_GoToLastKeyframe = animationWindowStateType.GetMethod("GoToLastKeyframe"));
                Assert.IsNotNull(dg_set_m_AllCurvesCacheDirty = ReflectionCommon.CreateSetFieldDelegate<bool>(animationWindowStateType.GetField("m_AllCurvesCacheDirty", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_set_m_FilteredCurvesCacheDirty = ReflectionCommon.CreateSetFieldDelegate<bool>(animationWindowStateType.GetField("m_FilteredCurvesCacheDirty", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_set_m_ActiveCurvesCacheDirty = ReflectionCommon.CreateSetFieldDelegate<bool>(animationWindowStateType.GetField("m_ActiveCurvesCacheDirty", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_set_m_FilteredCurvesCache = ReflectionCommon.CreateSetFieldDelegate<IList>(animationWindowStateType.GetField("m_FilteredCurvesCache", BindingFlags.NonPublic | BindingFlags.Instance)));
            }

            public override void ClearCache(object instance)
            {
                if (instance == null) return;
                dg_set_m_dopelinesCache(instance, null);  //Cache Clear
                dg_set_m_AllCurvesCacheDirty(instance, true);
                dg_set_m_FilteredCurvesCacheDirty(instance, true);
                dg_set_m_ActiveCurvesCacheDirty(instance, true);
            }
            public override bool StartRecording(object instance)
            {
                if (instance == null) return false;
                try
                {
                    pi_recording.SetValue(instance, true);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public override bool StopRecording(object instance)
            {
                if (instance == null) return false;
                try
                {
                    pi_recording.SetValue(instance, false);
                    pi_previewing.SetValue(instance, false);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public override bool StartPlayback(object instance)
            {
                if (instance == null) return false;
                try
                {
                    pi_playing.SetValue(instance, true);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public override bool StopPlayback(object instance)
            {
                if (instance == null) return false;
                try
                {
                    pi_playing.SetValue(instance, false);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public override bool StartPreview(object instance)
            {
                if (instance == null) return false;
                try
                {
                    pi_previewing.SetValue(instance, true);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public override bool StopPreview(object instance)
            {
                if (instance == null) return false;
                try
                {
                    pi_previewing.SetValue(instance, false);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    return false;
                }
                return true;
            }
            public override void GoToPreviousKeyframe(object instance)
            {
                if (instance == null) return;
                mi_GoToPreviousKeyframe.Invoke(instance, null);
            }
            public override void GoToNextKeyframe(object instance)
            {
                if (instance == null) return;
                mi_GoToNextKeyframe.Invoke(instance, null);
            }
            public override void GoToFirstKeyframe(object instance)
            {
                if (instance == null) return;
                mi_GoToFirstKeyframe.Invoke(instance, null);
            }
            public override void GoToLastKeyframe(object instance)
            {
                if (instance == null) return;
                mi_GoToLastKeyframe.Invoke(instance, null);
            }

            public override void SetFilteredCurves(object instance, IList curves)
            {
                if (instance == null) return;
                dg_set_m_FilteredCurvesCacheDirty(instance, false);
                dg_set_m_FilteredCurvesCache(instance, curves);
            }
        }
        internal class UAnimationWindowControl_2023_1 : UAnimationWindowControl
        {
            public UAnimationWindowControl_2023_1() : base()
            {
                var iAnimationWindowControllerType = ReflectionCommon.GetUnityEditorType("UnityEditor.IAnimationWindowController");
                Assert.IsNotNull(mi_get_canRecord = iAnimationWindowControllerType.GetProperty("canRecord").GetGetMethod());
            }
        }

        internal sealed class UAnimationWindowSelectionItem_2023_1 : UAnimationWindowSelectionItem
        {
            public UAnimationWindowSelectionItem_2023_1() : base()
            {
            }

            public override IList GetCurves(object instance)
            {
                Assert.IsTrue(false);
                return null;
            }
            public override void SetCurvesCache(object instance, IList curves)
            {
                if (instance == null) return;
                Assert.IsTrue(false);
            }
            public override IList GetCurvesCache(object instance)
            {
                if (instance == null) return null;
                Assert.IsTrue(false);
                return null;
            }
            public override void ClearCurvesCache(object instance)
            {
                Assert.IsTrue(false);
            }
            public override Type GetEditorCurveValueType(object instance, EditorCurveBinding binding)
            {
                Assert.IsTrue(false);
                return null;
            }
        }

        public override void MoveToNextKeyframe()
        {
            uAnimationWindowState.GoToNextKeyframe(AnimationWindowStateInstance);
            Repaint();
        }
        public override void MoveToPreviousKeyframe()
        {
            uAnimationWindowState.GoToPreviousKeyframe(AnimationWindowStateInstance);
            Repaint();
        }
        public override void MoveToFirstKeyframe()
        {
            uAnimationWindowState.GoToFirstKeyframe(AnimationWindowStateInstance);
            Repaint();
        }
        public override void MoveToLastKeyframe()
        {
            uAnimationWindowState.GoToLastKeyframe(AnimationWindowStateInstance);
            Repaint();
        }

        public override void PropertySortOrFilterByBindings(List<EditorCurveBinding> bindings)
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
                var allCurves = uAnimationWindowState.GetAllCurves(aws);
                uAnimationWindowSelectionItem.swapDummyCurves = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(uAnimationWindowCurve.CurveType));
                {
                    foreach (var curve in allCurves)
                    {
                        var binding = uAnimationWindowCurve.GetBinding(curve);
                        if (!bindings.Contains(binding))
                            continue;
                        uAnimationWindowSelectionItem.swapDummyCurves.Add(curve);
                    }
                }
                uAnimationWindowState.SetFilteredCurves(aws, uAnimationWindowSelectionItem.swapDummyCurves);
                uAnimationWindowHierarchyDataSource.UpdateData(hierarchyData);
            }
            else
            {
                uAnimationWindowSelectionItem.swapDummyCurves = null;
                uAnimationWindowHierarchyDataSource.UpdateData(hierarchyData);
            }

            Repaint();
        }

    }
}
#endif
