using System;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UTreeView
    {
        private Func<object, object> dg_get_m_TreeView;

        private readonly UTreeViewController uTreeViewController;

        private class UTreeViewController
        {
            private Action<int> dg_OffsetSelection;
            private MethodInfo mi_OffsetSelection;

            public void OffsetSelection(object instance, int offset)
            {
                if (instance == null) return;
                mi_OffsetSelection ??= instance.GetType().GetMethod("OffsetSelection", BindingFlags.Instance | BindingFlags.Public);
                Assert.IsNotNull(mi_OffsetSelection);
                ReflectionCommon.InvokeInstanceDelegate(ref dg_OffsetSelection, instance, mi_OffsetSelection, offset);
            }
        }

        public UTreeView()
        {
            var treeViewType = ReflectionCommon.GetUnityEditorType("UnityEditor.IMGUI.Controls.TreeView");

            dg_get_m_TreeView = ReflectionCommon.CreateGetFieldDelegate<object>(treeViewType.GetField("m_TreeView", BindingFlags.NonPublic | BindingFlags.Instance));

            uTreeViewController = new UTreeViewController();
        }

        public void OffsetSelection(object instance, int offset)
        {
            if (instance == null) return;

            dg_get_m_TreeView ??= ReflectionCommon.CreateGetFieldDelegate<object>(instance.GetType().GetField("m_TreeView", BindingFlags.NonPublic | BindingFlags.Instance));   //Unity6000.5 or later
            Assert.IsNotNull(dg_get_m_TreeView);

            var treeViewController = dg_get_m_TreeView(instance);
            if (treeViewController == null) return;

            uTreeViewController.OffsetSelection(treeViewController, offset);
        }
    }
}
