using System.Reflection;
using UnityEngine.Animations;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class UAnimationClipPlayable
    {
        private readonly MethodInfo mi_SetRemoveStartOffset;
        private readonly MethodInfo mi_SetOverrideLoopTime;
        private readonly MethodInfo mi_SetLoopTime;
        private readonly MethodInfo mi_SetSampleRate;

        public UAnimationClipPlayable()
        {
            var animationClipPlayableType = typeof(AnimationClipPlayable);
            Assert.IsNotNull(mi_SetRemoveStartOffset = animationClipPlayableType.GetMethod("SetRemoveStartOffset", BindingFlags.Instance | BindingFlags.NonPublic));
            mi_SetOverrideLoopTime = animationClipPlayableType.GetMethod("SetOverrideLoopTime", BindingFlags.Instance | BindingFlags.NonPublic);
            mi_SetOverrideLoopTime ??= animationClipPlayableType.GetMethod("SetOverrideLoopTime", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(mi_SetOverrideLoopTime);
            mi_SetLoopTime = animationClipPlayableType.GetMethod("SetLoopTime", BindingFlags.Instance | BindingFlags.NonPublic);
            mi_SetLoopTime ??= animationClipPlayableType.GetMethod("SetLoopTime", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(mi_SetLoopTime);
            Assert.IsNotNull(mi_SetSampleRate = animationClipPlayableType.GetMethod("SetSampleRate", BindingFlags.Instance | BindingFlags.NonPublic));
        }

        public void SetRemoveStartOffset(AnimationClipPlayable playable, bool value)
        {
            mi_SetRemoveStartOffset.Invoke(playable, new object[] { value });
        }
        public void SetOverrideLoopTime(AnimationClipPlayable playable, bool value)
        {
            mi_SetOverrideLoopTime.Invoke(playable, new object[] { value });
        }
        public void SetLoopTime(AnimationClipPlayable playable, bool value)
        {
            mi_SetLoopTime.Invoke(playable, new object[] { value });
        }
        public void SetSampleRate(AnimationClipPlayable playable, float value)
        {
            mi_SetSampleRate.Invoke(playable, new object[] { value });
        }
    }
}
