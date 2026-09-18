using System;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UAnimatorControllerTool
    {
        private readonly Func<object, object> dg_get_tool;

        public UAnimatorControllerTool()
        {
            var animatorControllerToolType = ReflectionCommon.GetUnityEditorType("UnityEditor.Graphs.AnimatorControllerTool");
            {
                var fi_tool = animatorControllerToolType.GetField("tool", BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(dg_get_tool = ReflectionCommon.CreateGetFieldDelegate<object>(fi_tool));
            }
        }

        public EditorWindow Instance => (EditorWindow)dg_get_tool(null);
    }
}
