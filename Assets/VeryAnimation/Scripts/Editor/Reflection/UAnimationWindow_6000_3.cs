#if UNITY_6000_3_OR_NEWER
using System;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine.Animations;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace VeryAnimation
{
    internal class UAnimationWindow_6000_3 : UAnimationWindow_2023_1
    {
        internal class UAnimationWindowState_6000_3 : UAnimationWindowState_2023_1
        {
            private readonly MethodInfo mi_get_controller;

            public UAnimationWindowState_6000_3() : base()
            {
                Assert.IsNotNull(mi_ForceRefresh = animationWindowStateType.GetMethod("RefreshClip"));
                Assert.IsNotNull(mi_get_controller = animationWindowStateType.GetProperty("controller").GetGetMethod());
            }

            public override object GetControlInterface(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_controlInterface, instance, mi_get_controller);
        }
        internal sealed class UAnimationWindowControl_6000_3 : UAnimationWindowControl_2023_1
        {
            public UAnimationWindowControl_6000_3() : base()
            {
                var animationWindowControlType = ReflectionCommon.GetUnityEditorType("UnityEditor.AnimationWindowBuiltin.AnimationWindowControl");
                Assert.IsNotNull(dg_get_m_Graph = ReflectionCommon.CreateGetFieldDelegate<PlayableGraph>(animationWindowControlType.GetField("m_Graph", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_get_m_ClipPlayable = ReflectionCommon.CreateGetFieldDelegate<AnimationClipPlayable>(animationWindowControlType.GetField("m_ClipPlayable", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_get_m_CandidateClipPlayable = ReflectionCommon.CreateGetFieldDelegate<AnimationClipPlayable>(animationWindowControlType.GetField("m_CandidateClipPlayable", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(dg_get_m_DefaultPosePlayable = ReflectionCommon.CreateGetFieldDelegate<AnimationClipPlayable>(animationWindowControlType.GetField("m_DefaultPosePlayable", BindingFlags.NonPublic | BindingFlags.Instance)));
                Assert.IsNotNull(mi_ResampleAnimationHasFlag = animationWindowControlType.GetMethod("ResampleAnimation", BindingFlags.NonPublic | BindingFlags.Instance));
                Assert.IsNotNull(mi_DestroyGraph = animationWindowControlType.GetMethod("DestroyGraph", BindingFlags.NonPublic | BindingFlags.Instance));
            }
        }
    }
}
#endif
