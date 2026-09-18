using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UAnimatorController
    {
        private Func<int, AnimatorStateMachine> dg_FindEffectiveRootStateMachine;

        private readonly MethodInfo mi_FindEffectiveRootStateMachine;

        public UAnimatorController()
        {
            var animatorControllerType = typeof(UnityEditor.Animations.AnimatorController);

            Assert.IsNotNull(mi_FindEffectiveRootStateMachine = animatorControllerType.GetMethod("FindEffectiveRootStateMachine", BindingFlags.NonPublic | BindingFlags.Instance));
        }

        public AnimatorStateMachine FindEffectiveRootStateMachine(object instance, int layerIndex)
        {
            return ReflectionCommon.InvokeInstanceDelegate(ref dg_FindEffectiveRootStateMachine, instance, mi_FindEffectiveRootStateMachine, layerIndex);
        }
    }
}
