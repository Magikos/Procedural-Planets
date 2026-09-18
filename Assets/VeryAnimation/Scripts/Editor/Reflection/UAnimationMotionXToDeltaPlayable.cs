using System;
using System.Reflection;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace VeryAnimation
{
    internal sealed class UAnimationMotionXToDeltaPlayable
    {
        public Type PlayableType { get; private set; }

        private readonly FieldInfo fi_m_Handle;
        private readonly MethodInfo mi_Create;
        private readonly MethodInfo mi_SetAbsoluteMotion;

        private readonly UPlayable uPlayable;

        public UAnimationMotionXToDeltaPlayable()
        {
            var asmUnityEngine = typeof(UnityEngine.Animations.AnimationClipPlayable).Assembly;
            Assert.IsNotNull(PlayableType = asmUnityEngine.GetType("UnityEngine.Animations.AnimationMotionXToDeltaPlayable"));
            Assert.IsNotNull(fi_m_Handle = PlayableType.GetField("m_Handle", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.IsNotNull(mi_Create = PlayableType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static));
            Assert.IsNotNull(mi_SetAbsoluteMotion = PlayableType.GetMethod("SetAbsoluteMotion"));
            uPlayable = new UPlayable();
        }

        public Playable Create(PlayableGraph graph)
        {
            var obj = mi_Create.Invoke(null, new object[] { graph });
            var handle = (PlayableHandle)fi_m_Handle.GetValue(obj);
            return uPlayable.Create(handle);
        }

        public void SetAbsoluteMotion(Playable playable, bool value)
        {
            var tmp = Activator.CreateInstance(PlayableType);
            fi_m_Handle.SetValue(tmp, playable.GetHandle());
            mi_SetAbsoluteMotion.Invoke(tmp, new object[] { value });
        }
    }
}
