using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class USceneView
    {
        private readonly PropertyInfo pi_viewIsLockedToObject;

        public USceneView()
        {
            var sceneViewType = ReflectionCommon.GetUnityEditorType("UnityEditor.SceneView");
            Assert.IsNotNull(pi_viewIsLockedToObject = sceneViewType.GetProperty("viewIsLockedToObject", BindingFlags.NonPublic | BindingFlags.Instance));
        }

        public void SetViewIsLockedToObject(SceneView instance, bool flag)
        {
            pi_viewIsLockedToObject.SetValue(instance, flag, null);
        }

        public bool Frame(SceneView instance, Bounds bounds, bool instant = true)
        {
            return instance.Frame(bounds, instant);
        }
    }
}
