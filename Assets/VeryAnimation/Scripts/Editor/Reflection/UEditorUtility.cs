using System;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UEditorUtility
    {
        private readonly Func<UnityEngine.Object, GameObject> dg_InstantiateForAnimatorPreview;

        public UEditorUtility()
        {
            var editorUtilityType = ReflectionCommon.GetUnityEditorType("UnityEditor.EditorUtility");
            Assert.IsNotNull(dg_InstantiateForAnimatorPreview = (Func<UnityEngine.Object, GameObject>)Delegate.CreateDelegate(typeof(Func<UnityEngine.Object, GameObject>), null, editorUtilityType.GetMethod("InstantiateForAnimatorPreview", BindingFlags.NonPublic | BindingFlags.Static)));
        }

        public GameObject InstantiateForAnimatorPreview(UnityEngine.Object o)
        {
            return dg_InstantiateForAnimatorPreview(o);
        }
    }
}
