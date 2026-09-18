using System;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal class URotationCurveInterpolation
    {
        public static URotationCurveInterpolation CreateInstance()
        {
#if UNITY_6000_3_OR_NEWER
            return new URotationCurveInterpolation_6000_3();
#else
            return new URotationCurveInterpolation();
#endif
        }

        protected MethodInfo mi_GetModeFromCurveData;
        protected MethodInfo mi_SetInterpolation;

        public enum Mode
        {
            Baked,
            NonBaked,
            RawQuaternions,
            RawEuler,
            Undefined,
            Total,
        }
        public static readonly string[] PrefixForInterpolation =
        {
            "localEulerAnglesBaked.",
            "localEulerAngles.",
            "m_LocalRotation.",
            "localEulerAnglesRaw.",
            null,
        };

        public URotationCurveInterpolation()
        {
            var rotationCurveInterpolationType = ReflectionCommon.GetUnityEditorType("UnityEditor.RotationCurveInterpolation");
            Assert.IsNotNull(mi_GetModeFromCurveData = rotationCurveInterpolationType.GetMethod("GetModeFromCurveData", BindingFlags.Public | BindingFlags.Static));
            mi_SetInterpolation = rotationCurveInterpolationType.GetMethod("SetInterpolation", BindingFlags.NonPublic | BindingFlags.Static);
        }

        public Mode GetModeFromCurveData(EditorCurveBinding data)
        {
            return (Mode)mi_GetModeFromCurveData.Invoke(null, new object[] { data });
        }

        public void SetInterpolation(AnimationClip clip, EditorCurveBinding[] curveBindings, Mode newInterpolationMode)
        {
            mi_SetInterpolation.Invoke(null, new object[] { clip, curveBindings, (int)newInterpolationMode });
        }
    }
}
