using System;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UPrefabStage
    {
        private Func<bool> dg_get_autoSave;
        private readonly MethodInfo mi_get_autoSave;

        public UPrefabStage()
        {
            Assert.IsNotNull(mi_get_autoSave = typeof(PrefabStage).GetProperty("autoSave", BindingFlags.Instance | BindingFlags.NonPublic).GetGetMethod(true));
        }

        public bool GetAutoSave(PrefabStage instance) => instance != null && ReflectionCommon.InvokeInstanceDelegate(ref dg_get_autoSave, instance, mi_get_autoSave);
    }
}
