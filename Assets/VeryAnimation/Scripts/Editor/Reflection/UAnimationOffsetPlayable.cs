using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace VeryAnimation
{
    internal sealed class UAnimationOffsetPlayable
    {
        public Type PlayableType { get; private set; }

        private readonly FieldInfo fi_m_Handle;
        private readonly MethodInfo mi_Create;
        private readonly MethodInfo mi_SetPosition;
        private readonly MethodInfo mi_SetRotation;
        private readonly MethodInfo mi_GetPosition;
        private readonly MethodInfo mi_GetRotation;

        private readonly object instance;
        private readonly UPlayable uPlayable;

        public UAnimationOffsetPlayable()
        {
            var asmUnityEngine = typeof(UnityEngine.Animations.AnimationClipPlayable).Assembly;
            Assert.IsNotNull(PlayableType = asmUnityEngine.GetType("UnityEngine.Animations.AnimationOffsetPlayable"));
            Assert.IsNotNull(fi_m_Handle = PlayableType.GetField("m_Handle", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.IsNotNull(mi_Create = PlayableType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static));
            Assert.IsNotNull(mi_SetPosition = PlayableType.GetMethod("SetPosition", BindingFlags.Public | BindingFlags.Instance));
            Assert.IsNotNull(mi_SetRotation = PlayableType.GetMethod("SetRotation", BindingFlags.Public | BindingFlags.Instance));
            Assert.IsNotNull(mi_GetPosition = PlayableType.GetMethod("GetPosition", BindingFlags.Public | BindingFlags.Instance));
            Assert.IsNotNull(mi_GetRotation = PlayableType.GetMethod("GetRotation", BindingFlags.Public | BindingFlags.Instance));
            uPlayable = new UPlayable();
            instance = Activator.CreateInstance(PlayableType);
        }

        public Playable Create(PlayableGraph graph, Vector3 position, Quaternion rotation, int inputCount)
        {
            var obj = mi_Create.Invoke(null, new object[] { graph, position, rotation, inputCount });
            var handle = (PlayableHandle)fi_m_Handle.GetValue(obj);
            return uPlayable.Create(handle);
        }

        public void SetPosition(IPlayable playable, Vector3 value)
        {
            fi_m_Handle.SetValue(instance, playable.GetHandle());
            mi_SetPosition.Invoke(instance, new object[] { value });
        }
        public void SetRotation(IPlayable playable, Quaternion value)
        {
            fi_m_Handle.SetValue(instance, playable.GetHandle());
            mi_SetRotation.Invoke(instance, new object[] { value });
        }
        public Vector3 GetPosition(IPlayable playable)
        {
            fi_m_Handle.SetValue(instance, playable.GetHandle());
            return (Vector3)mi_GetPosition.Invoke(instance, null);
        }
        public Quaternion GetRotation(IPlayable playable)
        {
            fi_m_Handle.SetValue(instance, playable.GetHandle());
            return (Quaternion)mi_GetRotation.Invoke(instance, null);
        }
    }
}
