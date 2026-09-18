using System;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UEditorGUI
    {
        private readonly Func<bool> dg_IsEditingTextField;

        public class UGUIContents
        {
            private readonly Func<GUIContent> dg_titleSettingsIcon;
            private readonly Func<GUIContent> dg_helpIcon;

            public UGUIContents()
            {
                var gUIContentsType = ReflectionCommon.GetUnityEditorType("UnityEditor.EditorGUI+GUIContents");
                Assert.IsNotNull(dg_titleSettingsIcon = (Func<GUIContent>)Delegate.CreateDelegate(typeof(Func<GUIContent>), null, gUIContentsType.GetProperty("titleSettingsIcon", BindingFlags.NonPublic | BindingFlags.Static).GetGetMethod(true)));
                Assert.IsNotNull(dg_helpIcon = (Func<GUIContent>)Delegate.CreateDelegate(typeof(Func<GUIContent>), null, gUIContentsType.GetProperty("helpIcon", BindingFlags.NonPublic | BindingFlags.Static).GetGetMethod(true)));
            }

            public GUIContent GetTitleSettingsIcon()
            {
                return dg_titleSettingsIcon();
            }
            public GUIContent GetHelpIcon()
            {
                return dg_helpIcon();
            }
        }

        public UGUIContents GUIContents { get; private set; }

        public UEditorGUI()
        {
            var editorGUIType = ReflectionCommon.GetUnityEditorType("UnityEditor.EditorGUI");
            Assert.IsNotNull(dg_IsEditingTextField = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), null, editorGUIType.GetMethod("IsEditingTextField", BindingFlags.NonPublic | BindingFlags.Static)));

            GUIContents = new UGUIContents();
        }

        public bool IsEditingTextField()
        {
            return dg_IsEditingTextField();
        }
    }
}
