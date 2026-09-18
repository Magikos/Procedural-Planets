using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal class UEditorWindow

    {
        public static UEditorWindow CreateInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return new UEditorWindow_2023_1();
#else
            return new UEditorWindow();
#endif
        }

        private Func<int> dg_GetNumTabs;
        private readonly MethodInfo mi_GetNumTabs;

        private readonly FieldInfo fi_m_Parent;

        public class UDockArea
        {
            private readonly Type dockAreaType;

            private readonly PropertyInfo pi_selected;
            private readonly MethodInfo mi_AddTab;

            public UDockArea()
            {
                dockAreaType = ReflectionCommon.GetUnityEditorType("UnityEditor.DockArea");

                Assert.IsNotNull(pi_selected = dockAreaType.GetProperty("selected", BindingFlags.Public | BindingFlags.Instance));
                Assert.IsNotNull(mi_AddTab = dockAreaType.GetMethod("AddTab", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(EditorWindow), typeof(bool) }, null));
            }

            public int GetSelected(UnityEngine.Object dockArea)
            {
                if (dockArea == null) return -1;
                return (int)pi_selected.GetValue(dockArea, null);
            }
            public void SetSelected(UnityEngine.Object dockArea, int selected)
            {
                if (dockArea == null) return;
                pi_selected.SetValue(dockArea, selected, null);
            }

            public void AddTab(UnityEngine.Object dockArea, EditorWindow pane)
            {
                if (dockArea == null) return;
                mi_AddTab.Invoke(dockArea, new object[] { pane, true });
            }
        }

        private readonly UDockArea uDockArea;

        public UEditorWindow()
        {
            var editorWindowType = ReflectionCommon.GetUnityEditorType("UnityEditor.EditorWindow");

            Assert.IsNotNull(fi_m_Parent = editorWindowType.GetField("m_Parent", BindingFlags.NonPublic | BindingFlags.Instance));
            Assert.IsNotNull(mi_GetNumTabs = editorWindowType.GetMethod("GetNumTabs", BindingFlags.NonPublic | BindingFlags.Instance));

            uDockArea = new UDockArea();
        }

        public virtual IList GetActiveEditorWindows()
        {
            return Resources.FindObjectsOfTypeAll<EditorWindow>();
        }

        public UnityEngine.Object GetEditorWindows(Type type)
        {
            foreach (var w in GetActiveEditorWindows())
            {
                if (w.GetType() == type)
                {
                    return (UnityEngine.Object)w;
                }
            }
            return null;
        }

        public int GetNumTabs(EditorWindow w) => w == null ? 0 : ReflectionCommon.InvokeInstanceDelegate(ref dg_GetNumTabs, w, mi_GetNumTabs);

        public bool IsDockBrother(EditorWindow w1, EditorWindow w2)
        {
            if (w1 == null || w2 == null)
                return false;
            var parent1 = fi_m_Parent.GetValue(w1) as UnityEngine.Object;
            var parent2 = fi_m_Parent.GetValue(w2) as UnityEngine.Object;
            return parent1 != null && parent2 != null && parent1 == parent2;
        }

        public int GetSelectedTab(EditorWindow w)
        {
            return uDockArea.GetSelected(fi_m_Parent.GetValue(w) as UnityEngine.Object);
        }
        public void SetSelectedTab(EditorWindow w, int selected)
        {
            uDockArea.SetSelected(fi_m_Parent.GetValue(w) as UnityEngine.Object, selected);
        }
        public void AddTab(EditorWindow w, EditorWindow pane)
        {
            uDockArea.AddTab(fi_m_Parent.GetValue(w) as UnityEngine.Object, pane);
        }
    }
}
