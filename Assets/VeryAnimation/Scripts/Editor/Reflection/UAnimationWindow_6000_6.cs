#if UNITY_6000_6_OR_NEWER
using System;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine.Animations;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace VeryAnimation
{
    internal sealed class UAnimationWindow_6000_6 : UAnimationWindow_6000_3
    {
        internal sealed class UAnimationWindowState_6000_6 : UAnimationWindowState_6000_3
        {
            private Func<string> dg_get_searchFilter;
            private Action<string> dg_set_searchFilter;
            private Func<bool> dg_get_enableQueryBuilder;
            private Action<bool> dg_set_enableQueryBuilder;
            private readonly MethodInfo mi_get_searchFilter;
            private readonly MethodInfo mi_set_searchFilter;
            private readonly MethodInfo mi_get_enableQueryBuilder;
            private readonly MethodInfo mi_set_enableQueryBuilder;

            public UAnimationWindowState_6000_6() : base()
            {
                Assert.IsNotNull(mi_get_searchFilter = animationWindowStateType.GetProperty("searchFilter").GetGetMethod());
                Assert.IsNotNull(mi_set_searchFilter = animationWindowStateType.GetProperty("searchFilter").GetSetMethod());
                Assert.IsNotNull(mi_get_enableQueryBuilder = animationWindowStateType.GetProperty("enableQueryBuilder").GetGetMethod());
                Assert.IsNotNull(mi_set_enableQueryBuilder = animationWindowStateType.GetProperty("enableQueryBuilder").GetSetMethod());
            }
            public override string GetSearchFilter(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_searchFilter, instance, mi_get_searchFilter);
            public override void SetSearchFilter(object instance, string filter) => ReflectionCommon.InvokeInstanceDelegate(ref dg_set_searchFilter, instance, mi_set_searchFilter, filter);
            public override bool GetEnableQueryBuilder(object instance) => ReflectionCommon.InvokeInstanceDelegate(ref dg_get_enableQueryBuilder, instance, mi_get_enableQueryBuilder);
            public override void SetEnableQueryBuilder(object instance, bool enable) => ReflectionCommon.InvokeInstanceDelegate(ref dg_set_enableQueryBuilder, instance, mi_set_enableQueryBuilder, enable);
        }
    }
}
#endif
