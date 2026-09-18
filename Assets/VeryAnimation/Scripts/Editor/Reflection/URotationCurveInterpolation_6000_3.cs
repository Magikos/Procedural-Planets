#if UNITY_6000_3_OR_NEWER
using System.Reflection;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class URotationCurveInterpolation_6000_3 : URotationCurveInterpolation
    {
        public URotationCurveInterpolation_6000_3()
        {
            var rotationCurveInterpolationType = ReflectionCommon.GetUnityEditorType("UnityEditor.AnimationWindowBuiltin.RotationCurveInterpolation");
            Assert.IsNotNull(mi_SetInterpolation = rotationCurveInterpolationType.GetMethod("SetInterpolation", BindingFlags.NonPublic | BindingFlags.Static));
        }
    }
}
#endif