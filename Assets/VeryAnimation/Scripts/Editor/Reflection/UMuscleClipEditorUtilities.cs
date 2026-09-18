using System;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UMuscleClipEditorUtilities
    {
        private readonly MethodInfo mi_GetMuscleClipQualityInfo;
        private readonly Func<AnimationClip, float, float, object> dg_GetMuscleClipQualityInfo;

        private readonly UMuscleClipQualityInfo uMuscleClipQualityInfo;

        public class MuscleClipQualityInfo
        {
            public float loop = 0.0f;
            public float loopOrientation = 0.0f;
            public float loopPositionY = 0.0f;
            public float loopPositionXZ = 0.0f;
        }

        public class UMuscleClipQualityInfo
        {
            private readonly Func<object, float> dg_get_loop;
            private readonly Func<object, float> dg_get_loopOrientation;
            private readonly Func<object, float> dg_get_loopPositionY;
            private readonly Func<object, float> dg_get_loopPositionXZ;

            public UMuscleClipQualityInfo()
            {
                var muscleClipQualityInfoType = ReflectionCommon.GetUnityEditorType("UnityEditor.MuscleClipQualityInfo");
                Assert.IsNotNull(dg_get_loop = ReflectionCommon.CreateGetFieldDelegate<float>(muscleClipQualityInfoType.GetField("loop", BindingFlags.Public | BindingFlags.Instance)));
                Assert.IsNotNull(dg_get_loopOrientation = ReflectionCommon.CreateGetFieldDelegate<float>(muscleClipQualityInfoType.GetField("loopOrientation", BindingFlags.Public | BindingFlags.Instance)));
                Assert.IsNotNull(dg_get_loopPositionY = ReflectionCommon.CreateGetFieldDelegate<float>(muscleClipQualityInfoType.GetField("loopPositionY", BindingFlags.Public | BindingFlags.Instance)));
                Assert.IsNotNull(dg_get_loopPositionXZ = ReflectionCommon.CreateGetFieldDelegate<float>(muscleClipQualityInfoType.GetField("loopPositionXZ", BindingFlags.Public | BindingFlags.Instance)));
            }

            public float GetLoop(object instance) => dg_get_loop(instance);
            public float GetLoopOrientation(object instance) => dg_get_loopOrientation(instance);
            public float GetLoopPositionY(object instance) => dg_get_loopPositionY(instance);
            public float GetLoopPositionXZ(object instance) => dg_get_loopPositionXZ(instance);
        }

        public UMuscleClipEditorUtilities()
        {
            var muscleClipUtilityType = ReflectionCommon.GetUnityEditorType("UnityEditor.MuscleClipUtility");
            Assert.IsNotNull(muscleClipUtilityType);
            Assert.IsNotNull(mi_GetMuscleClipQualityInfo = muscleClipUtilityType.GetMethod("GetMuscleClipQualityInfo", BindingFlags.NonPublic | BindingFlags.Static));
            Assert.IsNotNull(dg_GetMuscleClipQualityInfo = (Func<AnimationClip, float, float, object>)Delegate.CreateDelegate(typeof(Func<AnimationClip, float, float, object>), null, mi_GetMuscleClipQualityInfo));

            uMuscleClipQualityInfo = new UMuscleClipQualityInfo();
        }

        public MuscleClipQualityInfo GetMuscleClipQualityInfo(AnimationClip clip, float startTime, float stopTime)
        {
            var info = dg_GetMuscleClipQualityInfo(clip, startTime, stopTime);
            return new()
            {
                loop = uMuscleClipQualityInfo.GetLoop(info),
                loopOrientation = uMuscleClipQualityInfo.GetLoopOrientation(info),
                loopPositionY = uMuscleClipQualityInfo.GetLoopPositionY(info),
                loopPositionXZ = uMuscleClipQualityInfo.GetLoopPositionXZ(info),
            };
        }
    }
}
