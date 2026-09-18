using System;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UEditorGUIUtility
    {
        private readonly Func<string, Texture2D> dg_LoadIcon;
        private readonly Func<MessageType, Texture2D> dg_GetHelpIcon;
        private readonly Func<object, int> dg_get_s_LastControlID;

        public class UEditorLockTracker
        {
            private readonly PropertyInfo pi_isLocked;

            public UEditorLockTracker()
            {
                var editorLockTrackerType = ReflectionCommon.GetUnityEditorType("UnityEditor.EditorGUIUtility+EditorLockTracker");
                Assert.IsNotNull(pi_isLocked = editorLockTrackerType.GetProperty("isLocked", BindingFlags.NonPublic | BindingFlags.Instance));
            }

            public bool GetLock(object instance)
            {
                if (instance == null) return false;
                return (bool)pi_isLocked.GetValue(instance, null);
            }
            public void SetLock(object instance, bool flag)
            {
                if (instance == null) return;
                pi_isLocked.SetValue(instance, flag, null);
            }
        }

        public UEditorLockTracker uEditorLockTracker;

        public UEditorGUIUtility()
        {
            var editorGUIUtilityType = ReflectionCommon.GetUnityEditorType("UnityEditor.EditorGUIUtility");

            Assert.IsNotNull(dg_LoadIcon = (Func<string, Texture2D>)Delegate.CreateDelegate(typeof(Func<string, Texture2D>), null, editorGUIUtilityType.GetMethod("LoadIcon", BindingFlags.Static | BindingFlags.NonPublic)));
            Assert.IsNotNull(dg_GetHelpIcon = (Func<MessageType, Texture2D>)Delegate.CreateDelegate(typeof(Func<MessageType, Texture2D>), null, editorGUIUtilityType.GetMethod("GetHelpIcon", BindingFlags.Static | BindingFlags.NonPublic)));
            Assert.IsNotNull(dg_get_s_LastControlID = ReflectionCommon.CreateGetFieldDelegate<int>(editorGUIUtilityType.GetField("s_LastControlID", BindingFlags.NonPublic | BindingFlags.Static)));

            uEditorLockTracker = new UEditorLockTracker();
        }

        public Texture2D LoadIcon(string name)
        {
            return dg_LoadIcon(name);
        }

        public Texture2D GetHelpIcon(MessageType type)
        {
            return dg_GetHelpIcon(type);
        }

        public int GetLastControlID()
        {
            return dg_get_s_LastControlID(null);
        }
    }
}
